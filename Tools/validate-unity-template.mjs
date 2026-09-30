import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';

const root = path.resolve('Templates/UnityModelAddon');
const example = path.join(root, 'Assets/Example');
const source = JSON.parse(fs.readFileSync('Templates/ModelAddon/animation-sources.json', 'utf8'));
const manifest = JSON.parse(fs.readFileSync('Addons/MW4Milsim/customsosig-model.json', 'utf8'));
const version = fs.readFileSync(path.join(root, 'ProjectSettings/ProjectVersion.txt'), 'utf8');
assert.match(version, /2022\.3\.22f1/);
assert(fs.existsSync(path.join(root, 'Packages/manifest.json')));
assert(fs.existsSync(path.join(root, 'Assets/Editor/ArmorHitboxScaleWindow.cs')));

const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
assert.equal(sha(path.join(example, 'Source/milsim.glb')), sha('Addons/MW4Milsim/milsim.glb'));
for (const material of manifest.materials) {
  for (const key of ['albedo', 'normal']) {
    const name = path.basename(material[key]);
    assert.equal(sha(path.join(example, 'Textures', name)), sha(path.join('Addons/MW4Milsim/assets', name)));
  }
}
assert.equal(source.length, 35);
for (const entry of source) {
  const isWeapon = entry.source.startsWith('animations/lpsp/');
  const folder = isWeapon ? 'Weapon' : 'Mixamo';
  assert(fs.existsSync(path.join(example, 'Animations', folder, path.basename(entry.source))), `Missing ${entry.role}`);
  if (entry.bind) assert(fs.existsSync(path.join(example, 'Animations/Weapon', path.basename(entry.bind))));
}

const fbx = path.join(example, 'MW4Milsim.fbx');
const bytes = fs.readFileSync(fbx);
const model = new FBXLoader().parse(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
const skinned = [];
const names = new Set();
model.traverse(node => {
  names.add(node.name);
  if (node.isSkinnedMesh) skinned.push(node);
});
assert.equal(skinned.length, 1, 'Unity FBX must contain one skinned model');
for (const role of ['Pelvis', 'Spine', 'Head1', 'L_UpperArm', 'L_Hand', 'L_Thigh', 'L_Foot', 'R_UpperArm', 'R_Hand', 'R_Thigh', 'R_Foot']) {
  const alias = manifest.boneAliases.find(item => item.role === role);
  assert(alias && names.has(alias.source), `Unity FBX missing ${role}: ${alias?.source}`);
}
console.log(`Unity template sources validated: ${skinned[0].geometry.getAttribute('position').count} vertices, 35 roles, ${manifest.materials.length} material atlases. Unity Editor import/Avatar remains unverified.`);
