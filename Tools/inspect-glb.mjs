import fs from 'node:fs';

const input = process.argv[2];
if (!input) throw new Error('Usage: node inspect-glb.mjs <file.glb>');
const file = fs.readFileSync(input);
if (file.toString('ascii', 0, 4) !== 'glTF') throw new Error('Not a GLB file');
const version = file.readUInt32LE(4);
const declaredLength = file.readUInt32LE(8);
let offset = 12;
let json;
const chunks = [];
while (offset < file.length) {
  const length = file.readUInt32LE(offset);
  const type = file.toString('ascii', offset + 4, offset + 8);
  const dataOffset = offset + 8;
  chunks.push({ type, length, dataOffset });
  if (type === 'JSON') json = JSON.parse(file.toString('utf8', dataOffset, dataOffset + length));
  offset = dataOffset + length;
}
if (!json) throw new Error('GLB JSON chunk not found');

const accessorSummary = (index) => {
  const accessor = json.accessors?.[index];
  return accessor ? { index, type: accessor.type, componentType: accessor.componentType, count: accessor.count, min: accessor.min, max: accessor.max } : null;
};
const nodes = (json.nodes ?? []).map((node, index) => ({
  index,
  name: node.name,
  mesh: node.mesh,
  skin: node.skin,
  children: node.children,
  translation: node.translation,
  rotation: node.rotation,
  scale: node.scale,
}));
const meshes = (json.meshes ?? []).map((mesh, index) => ({
  index,
  name: mesh.name,
  primitives: mesh.primitives.map((primitive) => ({
    mode: primitive.mode ?? 4,
    material: primitive.material,
    indices: accessorSummary(primitive.indices),
    attributes: Object.fromEntries(Object.entries(primitive.attributes).map(([name, accessor]) => [name, accessorSummary(accessor)])),
  })),
}));
const skins = (json.skins ?? []).map((skin, index) => ({
  index,
  name: skin.name,
  skeleton: skin.skeleton,
  joints: skin.joints.map((joint) => ({ index: joint, name: json.nodes?.[joint]?.name })),
  inverseBindMatrices: accessorSummary(skin.inverseBindMatrices),
}));
const animations = (json.animations ?? []).map((animation, index) => ({
  index,
  name: animation.name,
  samplers: animation.samplers.map((sampler) => ({
    interpolation: sampler.interpolation ?? 'LINEAR',
    input: accessorSummary(sampler.input),
    output: accessorSummary(sampler.output),
  })),
  channels: animation.channels.map((channel) => ({
    sampler: channel.sampler,
    node: channel.target.node,
    nodeName: json.nodes?.[channel.target.node]?.name,
    path: channel.target.path,
  })),
}));

console.log(JSON.stringify({
  version,
  declaredLength,
  actualLength: file.length,
  asset: json.asset,
  chunks,
  scene: json.scene,
  scenes: json.scenes,
  nodes,
  meshes,
  skins,
  animations,
  materials: json.materials,
  textures: json.textures,
  images: json.images,
}, null, 2));
