import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { Matrix4, Quaternion, Vector3 } from 'three';
import { readAddonManifest, canonicalBone } from './addon-bone-map.mjs';

const input = process.argv[2];
const output = process.argv[3];
const modelManifest = readAddonManifest(process.argv[4]);
if (!input || !output) throw new Error('Usage: node export-skinned-glb.mjs <input.glb> <output.cskmesh.gz>');

const file = fs.readFileSync(input);
if (file.toString('ascii', 0, 4) !== 'glTF' || file.readUInt32LE(4) !== 2) throw new Error('Expected a GLB 2.0 file');
let offset = 12;
let gltf;
let binary;
while (offset < file.length) {
  const length = file.readUInt32LE(offset);
  const type = file.toString('ascii', offset + 4, offset + 8);
  const dataOffset = offset + 8;
  if (type === 'JSON') gltf = JSON.parse(file.toString('utf8', dataOffset, dataOffset + length));
  if (type.startsWith('BIN')) binary = file.subarray(dataOffset, dataOffset + length);
  offset = dataOffset + length;
}
if (!gltf || !binary) throw new Error('GLB is missing JSON or BIN data');
if ((gltf.skins?.length ?? 0) !== 1) throw new Error('Expected exactly one skin');
if ((gltf.meshes?.length ?? 0) !== 1) throw new Error('Expected exactly one mesh');

const componentSizes = { 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 };
const typeComponents = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };
function readAccessor(index) {
  const accessor = gltf.accessors[index];
  const view = gltf.bufferViews[accessor.bufferView];
  const components = typeComponents[accessor.type];
  const componentSize = componentSizes[accessor.componentType];
  const elementSize = components * componentSize;
  const stride = view.byteStride ?? elementSize;
  const start = (view.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
  const values = new Array(accessor.count * components);
  for (let element = 0; element < accessor.count; element += 1) {
    let cursor = start + element * stride;
    for (let component = 0; component < components; component += 1) {
      let value;
      switch (accessor.componentType) {
        case 5121: value = binary.readUInt8(cursor); break;
        case 5123: value = binary.readUInt16LE(cursor); break;
        case 5125: value = binary.readUInt32LE(cursor); break;
        case 5126: value = binary.readFloatLE(cursor); break;
        default: throw new Error(`Unsupported component type ${accessor.componentType}`);
      }
      values[element * components + component] = value;
      cursor += componentSize;
    }
  }
  return { accessor, components, values };
}

const skin = gltf.skins[0];
const jointToBone = new Map(skin.joints.map((joint, bone) => [joint, bone]));
const nodeParent = new Map();
(gltf.nodes ?? []).forEach((node, parent) => (node.children ?? []).forEach((child) => nodeParent.set(child, parent)));
const bones = skin.joints.map((nodeIndex) => {
  const node = gltf.nodes[nodeIndex];
  let parentNode = nodeParent.get(nodeIndex);
  while (parentNode !== undefined && !jointToBone.has(parentNode)) parentNode = nodeParent.get(parentNode);
  const translation = node.translation ?? [0, 0, 0];
  const rotation = node.rotation ?? [0, 0, 0, 1];
  const scale = node.scale ?? [1, 1, 1];
  return {
    name: canonicalBone(node.name ?? `Bone_${nodeIndex}`, modelManifest),
    parent: parentNode === undefined ? -1 : jointToBone.get(parentNode),
    translation: [translation[0], translation[1], -translation[2]],
    rotation: [-rotation[0], -rotation[1], rotation[2], rotation[3]],
    scale,
  };
});

const inverseBindAccessor = readAccessor(skin.inverseBindMatrices);
if (inverseBindAccessor.components !== 16 || inverseBindAccessor.accessor.count !== bones.length) throw new Error('Invalid inverse bind matrices');
const mirror = [1, 1, -1, 1];
const bindposes = [];
for (let matrixIndex = 0; matrixIndex < bones.length; matrixIndex += 1) {
  const source = inverseBindAccessor.values.slice(matrixIndex * 16, matrixIndex * 16 + 16);
  const rowMajor = [];
  for (let row = 0; row < 4; row += 1) {
    for (let column = 0; column < 4; column += 1) {
      rowMajor.push(source[column * 4 + row] * mirror[row] * mirror[column]);
    }
  }
  bindposes.push(rowMajor);
}

const worldMatrices = [];
let maximumBindPoseError = 0;
for (let index = 0; index < bones.length; index += 1) {
  const bone = bones[index];
  const local = new Matrix4().compose(new Vector3(...bone.translation), new Quaternion(...bone.rotation), new Vector3(...bone.scale));
  const world = bone.parent >= 0 ? worldMatrices[bone.parent].clone().multiply(local) : local;
  worldMatrices[index] = world;
  const bind = new Matrix4().set(...bindposes[index]);
  const identity = world.clone().multiply(bind).elements;
  const expected = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  for (let element = 0; element < 16; element += 1) maximumBindPoseError = Math.max(maximumBindPoseError, Math.abs(identity[element] - expected[element]));
}
if (maximumBindPoseError > 0.001) throw new Error(`Skeleton and inverse bind matrices disagree: max error ${maximumBindPoseError}`);

const primitives = gltf.meshes[0].primitives.map((primitive) => {
  if ((primitive.mode ?? 4) !== 4) throw new Error('Only triangle primitives are supported');
  const position = readAccessor(primitive.attributes.POSITION);
  const normal = readAccessor(primitive.attributes.NORMAL);
  const uv = readAccessor(primitive.attributes.TEXCOORD_0);
  const joints = readAccessor(primitive.attributes.JOINTS_0);
  const weights = readAccessor(primitive.attributes.WEIGHTS_0);
  const indices = readAccessor(primitive.indices);
  const count = position.accessor.count;
  if ([normal, uv, joints, weights].some((item) => item.accessor.count !== count)) throw new Error('Mismatched vertex attribute counts');
  if (count > 65000) throw new Error('Primitive exceeds Unity 5.6 16-bit mesh limit');
  if (indices.values.some((value) => value > 65535)) throw new Error('Index exceeds UInt16 range');

  const convertedIndices = [];
  for (let i = 0; i < indices.values.length; i += 3) convertedIndices.push(indices.values[i], indices.values[i + 2], indices.values[i + 1]);
  let maximumWeightError = 0;
  let maximumJointIndex = 0;
  for (let vertex = 0; vertex < count; vertex += 1) {
    const start = vertex * 4;
    const sum = weights.values[start] + weights.values[start + 1] + weights.values[start + 2] + weights.values[start + 3];
    maximumWeightError = Math.max(maximumWeightError, Math.abs(sum - 1));
    for (let influence = 0; influence < 4; influence += 1) maximumJointIndex = Math.max(maximumJointIndex, joints.values[start + influence]);
  }
  if (maximumJointIndex >= bones.length) throw new Error(`Primitive references joint ${maximumJointIndex}, but the skin has ${bones.length} bones`);
  if (maximumWeightError > 0.01) throw new Error(`Primitive skin weights are not normalized: max error ${maximumWeightError}`);
  return {
    material: primitive.material ?? 0,
    count,
    positions: position.values,
    normals: normal.values,
    uvs: uv.values,
    joints: joints.values,
    weights: weights.values,
    indices: convertedIndices,
    maximumWeightError,
  };
});

let minY = Infinity;
let maxY = -Infinity;
for (const primitive of primitives) {
  for (let index = 1; index < primitive.positions.length; index += 3) {
    minY = Math.min(minY, primitive.positions[index]);
    maxY = Math.max(maxY, primitive.positions[index]);
  }
}

class Writer {
  constructor() { this.parts = []; }
  bytes(value) { this.parts.push(Buffer.from(value)); }
  int(value) { const b = Buffer.allocUnsafe(4); b.writeInt32LE(value); this.parts.push(b); }
  float(value) { const b = Buffer.allocUnsafe(4); b.writeFloatLE(value); this.parts.push(b); }
  ushort(value) { const b = Buffer.allocUnsafe(2); b.writeUInt16LE(value); this.parts.push(b); }
  byte(value) { const b = Buffer.allocUnsafe(1); b.writeUInt8(value); this.parts.push(b); }
  string(value) { const b = Buffer.from(value, 'utf8'); this.int(b.length); this.bytes(b); }
  finish() { return Buffer.concat(this.parts); }
}

const writer = new Writer();
writer.bytes('CSK1');
writer.int(1);
writer.float(minY);
writer.float(maxY);
writer.int(bones.length);
for (const bone of bones) {
  writer.string(bone.name);
  writer.int(bone.parent);
  bone.translation.forEach((value) => writer.float(value));
  bone.rotation.forEach((value) => writer.float(value));
  bone.scale.forEach((value) => writer.float(value));
}
for (const matrix of bindposes) matrix.forEach((value) => writer.float(value));
writer.int(primitives.length);
for (const primitive of primitives) {
  writer.int(primitive.material);
  writer.int(primitive.count);
  for (let vertex = 0; vertex < primitive.count; vertex += 1) {
    const p = vertex * 3;
    const uv = vertex * 2;
    const weight = vertex * 4;
    writer.float(primitive.positions[p]); writer.float(primitive.positions[p + 1]); writer.float(-primitive.positions[p + 2]);
    writer.float(primitive.normals[p]); writer.float(primitive.normals[p + 1]); writer.float(-primitive.normals[p + 2]);
    // External source PNGs are the vertical inverse of the GLB's embedded
    // images. With Unity's bottom-left PNG sampling, these original UV values
    // already address the correct pixels; another V flip misplaces the atlas.
    writer.float(primitive.uvs[uv]); writer.float(modelManifest?.textureConvention === 'gltf-images' ? 1 - primitive.uvs[uv + 1] : primitive.uvs[uv + 1]);
    for (let i = 0; i < 4; i += 1) writer.byte(primitive.joints[weight + i]);
    for (let i = 0; i < 4; i += 1) writer.float(primitive.weights[weight + i]);
  }
  writer.int(primitive.indices.length);
  primitive.indices.forEach((value) => writer.ushort(value));
}

fs.mkdirSync(path.dirname(output), { recursive: true });
const uncompressed = writer.finish();
fs.writeFileSync(output, zlib.gzipSync(uncompressed, { level: 9 }));
const info = {
  source: path.basename(input),
  sourceSha256: crypto.createHash('sha256').update(file).digest('hex'),
  animationClipCount: gltf.animations?.length ?? 0,
  maximumBindPoseError,
  boneCount: bones.length,
  bones: bones.map((bone) => ({ name: bone.name, parent: bone.parent })),
  primitives: primitives.map((primitive) => ({ material: primitive.material, vertices: primitive.count, triangles: primitive.indices.length / 3, maximumWeightError: primitive.maximumWeightError })),
  boundsY: { min: minY, max: maxY, height: maxY - minY },
  uncompressedBytes: uncompressed.length,
  compressedBytes: fs.statSync(output).size,
};
fs.writeFileSync(output.replace(/\.gz$/i, '.json'), JSON.stringify(info, null, 2));
console.log(JSON.stringify(info, null, 2));
