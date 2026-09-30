import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { AnimationMixer, LoopOnce, Matrix4, Quaternion, Vector3 } from 'three';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';
import { readAddonManifest, canonicalBone } from './addon-bone-map.mjs';

const sourcePath = process.argv[2];
const targetPath = process.argv[3];
const outputPath = process.argv[4];
const retargetMode = process.argv[5] ?? 'hybrid';
const sourceBindPath = process.argv[6] === '-' ? undefined : process.argv[6];
const modelManifest = readAddonManifest(process.argv[7]);
if (!sourcePath || !targetPath || !outputPath) {
  throw new Error('Usage: node export-retargeted-fbx-animation.mjs <source.fbx> <target.glb> <output.csanim.gz> [hybrid|world|local|world-swapped-arms] [source-bind.fbx]');
}
if (!['rig', 'world-swapped-arms', 'hybrid', 'world', 'local'].includes(retargetMode)) throw new Error(`Unknown retarget mode: ${retargetMode}`);

function loadTargetSkeleton(filePath) {
  const file = fs.readFileSync(filePath);
  if (file.toString('ascii', 0, 4) !== 'glTF' || file.readUInt32LE(4) !== 2) throw new Error('Target must be GLB 2.0');
  let offset = 12;
  let gltf;
  while (offset < file.length) {
    const length = file.readUInt32LE(offset);
    const type = file.toString('ascii', offset + 4, offset + 8);
    if (type === 'JSON') gltf = JSON.parse(file.toString('utf8', offset + 8, offset + 8 + length));
    offset += 8 + length;
  }
  if (!gltf || (gltf.skins?.length ?? 0) !== 1) throw new Error('Target GLB must contain exactly one skin');
  const skin = gltf.skins[0];
  const jointToBone = new Map(skin.joints.map((joint, bone) => [joint, bone]));
  const nodeParent = new Map();
  gltf.nodes.forEach((node, parent) => (node.children ?? []).forEach((child) => nodeParent.set(child, parent)));
  const bones = skin.joints.map((nodeIndex) => {
    const node = gltf.nodes[nodeIndex];
    let parentNode = nodeParent.get(nodeIndex);
    while (parentNode !== undefined && !jointToBone.has(parentNode)) parentNode = nodeParent.get(parentNode);
    const r = node.rotation ?? [0, 0, 0, 1];
    const t = node.translation ?? [0, 0, 0];
    return {
      name: canonicalBone(node.name, modelManifest),
      parent: parentNode === undefined ? -1 : jointToBone.get(parentNode),
      translation: new Vector3(t[0], t[1], -t[2]),
      rotation: new Quaternion(-r[0], -r[1], r[2], r[3]).normalize(),
    };
  });
  const world = [];
  const positions = [];
  for (let index = 0; index < bones.length; index += 1) {
    world[index] = bones[index].parent >= 0
      ? world[bones[index].parent].clone().multiply(bones[index].rotation)
      : bones[index].rotation.clone();
    positions[index] = bones[index].parent >= 0
      ? positions[bones[index].parent].clone().add(bones[index].translation.clone().applyQuaternion(world[bones[index].parent]))
      : bones[index].translation.clone();
  }
  return { bytes: file, bones, world, positions };
}

function mirrorQuaternion(q) {
  return new Quaternion(-q.x, -q.y, q.z, q.w).normalize();
}

const semanticMap = [
  { sources: ['Hips', 'mixamorigHips', 'pelvis'], target: 'ValveBiped.Bip01_Pelvis' },
  { sources: ['Spine', 'mixamorigSpine', 'spine_01'], target: 'ValveBiped.Bip01_Spine' },
  { sources: ['Chest', 'mixamorigSpine1', 'spine_02'], target: 'ValveBiped.Bip01_Spine1' },
  { sources: ['UpperChest', 'mixamorigSpine2', 'spine_03'], target: 'ValveBiped.Bip01_Spine2' },
  { sources: ['Neck', 'mixamorigNeck', 'neck_01'], target: 'ValveBiped.Bip01_Neck1' },
  { sources: ['Head', 'mixamorigHead', 'head'], target: 'ValveBiped.Bip01_Head1' },
  { sources: ['LeftShoulder', 'mixamorigLeftShoulder', 'clavicle_l'], target: 'ValveBiped.Bip01_L_Clavicle' },
  { sources: ['LeftUpperArm', 'mixamorigLeftArm', 'upperarm_l'], target: 'ValveBiped.Bip01_L_UpperArm' },
  { sources: ['LeftLowerArm', 'mixamorigLeftForeArm', 'lowerarm_l'], target: 'ValveBiped.Bip01_L_Forearm' },
  { sources: ['LeftHand', 'mixamorigLeftHand', 'hand_l'], target: 'ValveBiped.Bip01_L_Hand' },
  { sources: ['RightShoulder', 'mixamorigRightShoulder', 'clavicle_r'], target: 'ValveBiped.Bip01_R_Clavicle' },
  { sources: ['RightUpperArm', 'mixamorigRightArm', 'upperarm_r'], target: 'ValveBiped.Bip01_R_UpperArm' },
  { sources: ['RightLowerArm', 'mixamorigRightForeArm', 'lowerarm_r'], target: 'ValveBiped.Bip01_R_Forearm' },
  { sources: ['RightHand', 'mixamorigRightHand', 'hand_r'], target: 'ValveBiped.Bip01_R_Hand' },
  { sources: ['LeftUpperLeg', 'mixamorigLeftUpLeg', 'thigh_l'], target: 'ValveBiped.Bip01_L_Thigh' },
  { sources: ['LeftLowerLeg', 'mixamorigLeftLeg', 'calf_l'], target: 'ValveBiped.Bip01_L_Calf' },
  { sources: ['LeftFoot', 'mixamorigLeftFoot', 'foot_l'], target: 'ValveBiped.Bip01_L_Foot' },
  { sources: ['RightUpperLeg', 'mixamorigRightUpLeg', 'thigh_r'], target: 'ValveBiped.Bip01_R_Thigh' },
  { sources: ['RightLowerLeg', 'mixamorigRightLeg', 'calf_r'], target: 'ValveBiped.Bip01_R_Calf' },
  { sources: ['RightFoot', 'mixamorigRightFoot', 'foot_r'], target: 'ValveBiped.Bip01_R_Foot' },
];

const sourceBytes = fs.readFileSync(sourcePath);
const sourceBuffer = sourceBytes.buffer.slice(sourceBytes.byteOffset, sourceBytes.byteOffset + sourceBytes.byteLength);
const sourceRoot = new FBXLoader().parse(sourceBuffer, '');
if (sourceRoot.animations.length === 0) throw new Error('Source FBX has no animation clips');
const clip = sourceRoot.animations[0];
sourceRoot.updateMatrixWorld(true);
const sourceNodes = new Map();
sourceRoot.traverse((node) => { if (node.name && !sourceNodes.has(node.name)) sourceNodes.set(node.name, node); });
for (const side of ['Left', 'Right']) {
  const valveSide = side === 'Left' ? 'L' : 'R';
  const unrealSide = side === 'Left' ? 'l' : 'r';
  ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky'].forEach((finger, fingerIndex) => {
    for (let joint = 1; joint <= 3; joint++) {
      const suffix = joint === 1 ? '' : String(joint - 1);
      const sources = [`mixamorig${side}Hand${finger}${joint}`, `${side}Hand${finger}${joint}`, `${finger.toLowerCase()}_0${joint}_${unrealSide}`];
      if (sources.some((name) => sourceNodes.has(name))) semanticMap.push({ sources, target: `ValveBiped.Bip01_${valveSide}_Finger${fingerIndex}${suffix}` });
    }
  });
}
let sourceBindNodes = sourceNodes;
let bindScene = sourceRoot;
if (sourceBindPath) {
  const bindBytes = fs.readFileSync(sourceBindPath);
  const bindBuffer = bindBytes.buffer.slice(bindBytes.byteOffset, bindBytes.byteOffset + bindBytes.byteLength);
  const bindRoot = new FBXLoader().parse(bindBuffer, '');
  bindScene = bindRoot;
  bindRoot.updateMatrixWorld(true);
  sourceBindNodes = new Map();
  bindRoot.traverse((node) => { if (node.name && !sourceBindNodes.has(node.name)) sourceBindNodes.set(node.name, node); });
}
const resolvedSemantics = semanticMap.map((entry) => {
  const sourceCandidates = retargetMode === 'world-swapped-arms' && /_(L|R)_(Clavicle|UpperArm|Forearm|Hand)$/.test(entry.target)
    ? entry.sources.map((candidate) => candidate
      .replace('Left', '__SIDE__').replace('Right', 'Left').replace('__SIDE__', 'Right'))
    : entry.sources;
  const source = sourceCandidates.find((candidate) => sourceNodes.has(candidate));
  if (!source) throw new Error(`Source bone is missing; tried: ${entry.sources.join(', ')}`);
  return { source, target: entry.target };
});
const sourceBindWorld = new Map();
const sourceBindPositions = new Map();
const inverseBindMatrices = new Map();
bindScene.traverse((node) => {
  if (!node.isSkinnedMesh) return;
  node.skeleton.bones.forEach((bone, i) => inverseBindMatrices.set(bone.name, node.skeleton.boneInverses[i].clone().invert()));
});
for (const { source: sourceName } of resolvedSemantics) {
  const node = sourceBindNodes.get(sourceName);
  if (!node) throw new Error(`Source bind skeleton is missing bone: ${sourceName}`);
  const bindMatrix = inverseBindMatrices.get(sourceName);
  const rotation = bindMatrix ? new Quaternion().setFromRotationMatrix(bindMatrix.clone().extractRotation(bindMatrix)) : node.getWorldQuaternion(new Quaternion());
  const position = bindMatrix ? new Vector3().setFromMatrixPosition(bindMatrix) : node.getWorldPosition(new Vector3());
  sourceBindWorld.set(sourceName, mirrorQuaternion(rotation));
  sourceBindPositions.set(sourceName, new Vector3(position.x, position.y, -position.z));
}

const target = loadTargetSkeleton(targetPath);
const targetIndices = new Map(target.bones.map((bone, index) => [bone.name, index]));
const targetToSource = new Map();
for (const { source: sourceName, target: targetName } of resolvedSemantics) {
  const targetIndex = targetIndices.get(targetName);
  if (targetIndex === undefined) throw new Error(`Target bone is missing: ${targetName}`);
  targetToSource.set(targetIndex, sourceName);
}

function anatomyBasis(pelvis, head, left, right) {
  const up = head.clone().sub(pelvis).normalize();
  const across = left.clone().sub(right).normalize();
  const forward = new Vector3().crossVectors(across, up).normalize();
  across.crossVectors(up, forward).normalize();
  return new Quaternion().setFromRotationMatrix(new Matrix4().makeBasis(across, up, forward));
}
const semanticSource = (suffix) => resolvedSemantics.find((entry) => entry.target.endsWith(suffix)).source;
const sourceBasis = anatomyBasis(...['_Pelvis', '_Head1', '_L_Thigh', '_R_Thigh'].map((suffix) => sourceBindPositions.get(semanticSource(suffix))));
const targetBasis = anatomyBasis(...['_Pelvis', '_Head1', '_L_Thigh', '_R_Thigh'].map((suffix) => target.positions[targetIndices.get('ValveBiped.Bip01' + suffix)]));
const rigAlignment = targetBasis.clone().multiply(sourceBasis.clone().invert());
const segmentErrors = [];

const fps = 24;
const frameCount = Math.max(2, Math.round(clip.duration * fps) + 1);
const tracks = [...targetToSource.keys()].sort((a, b) => a - b).map((boneIndex) => ({ boneIndex, frames: [] }));
const mixer = new AnimationMixer(sourceRoot);
const action = mixer.clipAction(clip);
action.setLoop(LoopOnce, 1);
action.clampWhenFinished = true;
action.play();
const leftHandIndex = targetIndices.get('ValveBiped.Bip01_L_Hand');
const rightHandIndex = targetIndices.get('ValveBiped.Bip01_R_Hand');
const pelvisIndex = targetIndices.get('ValveBiped.Bip01_Pelvis');
const bindHandAxis = leftHandIndex !== undefined && rightHandIndex !== undefined
  ? target.positions[leftHandIndex].clone().sub(target.positions[rightHandIndex]).normalize()
  : null;
let crossedHandFrames = 0;
const handSamples = [];

for (let frame = 0; frame < frameCount; frame += 1) {
  const time = frame === frameCount - 1 ? clip.duration : Math.min(clip.duration, frame / fps);
  mixer.setTime(time);
  sourceRoot.updateMatrixWorld(true);
  const animatedWorld = [];
  const animatedPositions = [];
  for (let boneIndex = 0; boneIndex < target.bones.length; boneIndex += 1) {
    const targetBone = target.bones[boneIndex];
    const sourceName = targetToSource.get(boneIndex);
    let desiredWorld;
    if (sourceName) {
      const sourceAnimated = mirrorQuaternion(sourceNodes.get(sourceName).getWorldQuaternion(new Quaternion()));
      const sourceBind = sourceBindWorld.get(sourceName);
      const isArm = /_(L|R)_(Clavicle|UpperArm|Forearm|Hand)$/.test(targetBone.name);
      const useBindLocal = retargetMode === 'local' || (retargetMode === 'hybrid' && isArm);
      if (retargetMode === 'rig') {
        const worldDelta = sourceAnimated.clone().multiply(sourceBind.clone().invert());
        desiredWorld = rigAlignment.clone().multiply(worldDelta).multiply(rigAlignment.clone().invert()).multiply(target.world[boneIndex]).normalize();
        // Match semantic segment directions, not the incompatible FBX bone axes.
        // Preserve transferred twist while correcting A-pose/T-pose differences.
        const childIndex = target.bones.findIndex((bone, index) => bone.parent === boneIndex && targetToSource.has(index));
        if (childIndex >= 0) {
          const childSource = sourceNodes.get(targetToSource.get(childIndex));
          const from = sourceNodes.get(sourceName).getWorldPosition(new Vector3());
          const to = childSource.getWorldPosition(new Vector3());
          const direction = to.sub(from); direction.z *= -1;
          direction.applyQuaternion(rigAlignment).normalize();
          const current = target.bones[childIndex].translation.clone().applyQuaternion(desiredWorld).normalize();
          if (current.lengthSq() > 0.5 && direction.lengthSq() > 0.5) {
            desiredWorld.premultiply(new Quaternion().setFromUnitVectors(current, direction)).normalize();
            const actual = target.bones[childIndex].translation.clone().applyQuaternion(desiredWorld).normalize();
            segmentErrors.push(Math.acos(Math.min(1, Math.max(-1, actual.dot(direction)))) * 180 / Math.PI);
          }
        }
      } else if (useBindLocal) {
        const localDelta = sourceBind.clone().invert().multiply(sourceAnimated).normalize();
        desiredWorld = target.world[boneIndex].clone().multiply(localDelta).normalize();
      } else {
        const worldDelta = sourceAnimated.clone().multiply(sourceBind.clone().invert()).normalize();
        desiredWorld = worldDelta.multiply(target.world[boneIndex]).normalize();
      }
    } else {
      desiredWorld = targetBone.parent >= 0
        ? animatedWorld[targetBone.parent].clone().multiply(targetBone.rotation)
        : targetBone.rotation.clone();
    }
    animatedWorld[boneIndex] = desiredWorld;
    animatedPositions[boneIndex] = targetBone.parent >= 0
      ? animatedPositions[targetBone.parent].clone().add(targetBone.translation.clone().applyQuaternion(animatedWorld[targetBone.parent]))
      : targetBone.translation.clone();
    const track = tracks.find((candidate) => candidate.boneIndex === boneIndex);
    if (track) {
      const local = targetBone.parent >= 0
        ? animatedWorld[targetBone.parent].clone().invert().multiply(desiredWorld).normalize()
        : desiredWorld.clone();
      const previous = track.frames[track.frames.length - 1];
      if (previous && previous.dot(local) < 0) local.set(-local.x, -local.y, -local.z, -local.w);
      track.frames.push(local);
    }
  }
  if (leftHandIndex !== undefined && rightHandIndex !== undefined) {
    const handAxis = animatedPositions[leftHandIndex].clone().sub(animatedPositions[rightHandIndex]);
    if (handAxis.dot(bindHandAxis) < 0) crossedHandFrames += 1;
    if (pelvisIndex !== undefined) {
      const pelvis = animatedPositions[pelvisIndex];
      handSamples.push({
        left: animatedPositions[leftHandIndex].clone().sub(pelvis),
        right: animatedPositions[rightHandIndex].clone().sub(pelvis),
      });
    }
  }
}

function axisRange(samples, side, axis) {
  if (samples.length === 0) return null;
  const values = samples.map((sample) => sample[side][axis]);
  return { min: Math.min(...values), max: Math.max(...values) };
}

class Writer {
  constructor() { this.parts = []; }
  bytes(value) { this.parts.push(Buffer.from(value)); }
  int(value) { const data = Buffer.allocUnsafe(4); data.writeInt32LE(value); this.parts.push(data); }
  float(value) { const data = Buffer.allocUnsafe(4); data.writeFloatLE(value); this.parts.push(data); }
  string(value) { const data = Buffer.from(value, 'utf8'); this.int(data.length); this.bytes(data); }
  finish() { return Buffer.concat(this.parts); }
}

const writer = new Writer();
writer.bytes('CSA1');
writer.int(1);
writer.string(path.basename(sourcePath, path.extname(sourcePath)));
writer.float(clip.duration);
writer.float(fps);
writer.int(frameCount);
writer.int(tracks.length);
for (const track of tracks) {
  writer.int(track.boneIndex);
  for (const rotation of track.frames) {
    writer.float(rotation.x); writer.float(rotation.y); writer.float(rotation.z); writer.float(rotation.w);
  }
}

const raw = writer.finish();
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, zlib.gzipSync(raw, { level: 9 }));
const report = {
  source: path.resolve(sourcePath),
  sourceSha256: crypto.createHash('sha256').update(sourceBytes).digest('hex'),
  target: path.resolve(targetPath),
  targetSha256: crypto.createHash('sha256').update(target.bytes).digest('hex'),
  sourceBind: sourceBindPath ? path.resolve(sourceBindPath) : null,
  clip: clip.name,
  retargetMode,
  maxSegmentDirectionErrorDegrees: segmentErrors.length ? Math.max(...segmentErrors) : null,
  duration: clip.duration,
  fps,
  frameCount,
  crossedHandFrames,
  handMotionRelativeToPelvis: {
    leftX: axisRange(handSamples, 'left', 'x'),
    leftY: axisRange(handSamples, 'left', 'y'),
    leftZ: axisRange(handSamples, 'left', 'z'),
    rightX: axisRange(handSamples, 'right', 'x'),
    rightY: axisRange(handSamples, 'right', 'y'),
    rightZ: axisRange(handSamples, 'right', 'z'),
  },
  tracks: tracks.map((track) => {
    const bind = target.bones[track.boneIndex].rotation;
    const maxLocalDeltaDegrees = Math.max(...track.frames.map((rotation) => {
      const dot = Math.min(1, Math.abs(bind.dot(rotation)));
      return 2 * Math.acos(dot) * 180 / Math.PI;
    }));
    const first = track.frames[0];
    const maxAnimatedDeltaDegrees = Math.max(...track.frames.map((rotation) => {
      const dot = Math.min(1, Math.abs(first.dot(rotation)));
      return 2 * Math.acos(dot) * 180 / Math.PI;
    }));
    return { boneIndex: track.boneIndex, bone: target.bones[track.boneIndex].name, source: targetToSource.get(track.boneIndex), maxLocalDeltaDegrees, maxAnimatedDeltaDegrees };
  }),
  compressedBytes: fs.statSync(outputPath).size,
};
fs.writeFileSync(outputPath.replace(/\.gz$/i, '.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
