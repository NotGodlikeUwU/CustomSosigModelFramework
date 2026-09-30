import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve('Addons/MW4Milsim');
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'customsosig-model.json'), 'utf8'));
const mesh = JSON.parse(fs.readFileSync(path.join(root, 'assets/milsim.cskmesh.json'), 'utf8'));
const metrocop = JSON.parse(fs.readFileSync('Addons/Metrocop/customsosig-model.json', 'utf8'));
assert.equal(manifest.schemaVersion, 1);
assert.equal(manifest.id, 'NotGodlike.mw4milsim');
assert.equal(manifest.menuName, 'MW4 Milsim');
assert.equal(manifest.surface.smoothness, 0.28);
assert.equal(manifest.surface.metallic, 0.05);
assert.equal(manifest.impactEffect, 'meat');
assert.equal(manifest.eyes.enabled, false);
assert.equal(manifest.animations.length, metrocop.animations.length);
assert.deepEqual(manifest.animations.map(x => x.role), metrocop.animations.map(x => x.role));
assert(mesh.boneCount > 20 && mesh.boneCount <= 255);
assert(mesh.maximumBindPoseError < 0.001);
assert(mesh.boundsY.height > 1.7 && mesh.boundsY.height < 2.1);
assert(mesh.primitives.length > 0 && mesh.primitives.length <= 16);
assert.equal(mesh.primitives.length, manifest.materials.length);
for (const primitive of mesh.primitives) {
  assert(primitive.vertices > 0 && primitive.vertices <= 65000);
  assert(primitive.material >= 0 && primitive.material < manifest.materials.length);
  assert(primitive.maximumWeightError < 0.01);
}
const actualBones = new Set(mesh.bones.map(x => x.name));
for (const role of ['Pelvis','Spine','Spine1','Spine2','Neck1','Head1',
  'L_Clavicle','L_UpperArm','L_Forearm','L_Hand','L_Thigh','L_Calf','L_Foot',
  'R_Clavicle','R_UpperArm','R_Forearm','R_Hand','R_Thigh','R_Calf','R_Foot']) {
  assert(actualBones.has(`ValveBiped.Bip01_${role}`), `Missing humanoid role ${role}`);
}
const files = [manifest.mesh, manifest.bloodMask, manifest.bloodNoise,
  ...manifest.materials.flatMap(x => [x.albedo, x.normal]), ...manifest.animations.map(x => x.file)];
for (const relative of files) {
  const full = path.resolve(root, relative);
  assert(full.startsWith(root + path.sep) && fs.statSync(full).size > 0, `Missing or unsafe asset ${relative}`);
}
for (const clip of manifest.animations) {
  const report = JSON.parse(fs.readFileSync(path.join(root, clip.file.replace(/\.gz$/, '.json')), 'utf8'));
  assert(report.maxSegmentDirectionErrorDegrees < 0.001, `Bad retarget ${clip.role}`);
  assert(report.tracks.length >= 40, `Too few animation tracks for ${clip.role}`);
  assert(report.frameCount >= 2, `Empty animation ${clip.role}`);
}
console.log(`MW4 Milsim validated: ${mesh.boneCount} bones, ${mesh.primitives.length} material atlases, ${manifest.animations.length} retargeted animations, blood assets.`);
