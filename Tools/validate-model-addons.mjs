import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { canonicalBone } from './addon-bone-map.mjs';

const manifest = JSON.parse(fs.readFileSync('Addons/Metrocop/customsosig-model.json', 'utf8'));
const roles = ['walk','backward','strafe_left','strafe_right','strafe_left_fast','strafe_right_fast','hit','death','crouch_idle','crouch_forward_left','crouch_forward_right','crouch_left','crouch_backward','stand_to_crouch','crouch_to_stand','falling','knocked_down','relaxed_idle','rifle_idle','rifle_aim','rifle_fire','rifle_reload','handgun_idle','handgun_aim','handgun_fire','handgun_reload','rifle_jog','handgun_jog','jog_forward','jog_backward','jog_left_forward','jog_right_forward','jog_left_backward','jog_right_backward','breathing'];
assert.equal(manifest.schemaVersion, 1);
assert.equal(manifest.impactEffect, 'meat');
assert.equal(new Set(manifest.animations.map((entry) => entry.role)).size, manifest.animations.length);
for (const role of roles) assert(manifest.animations.some((entry) => entry.role === role), `Missing ${role}`);
const files = [manifest.mesh, ...manifest.materials.flatMap((entry) => [entry.albedo, entry.normal]).filter(Boolean), ...manifest.animations.map((entry) => entry.file)];
assert.equal(manifest.bloodMask, 'assets/blood-mask.png');
assert.equal(manifest.bloodNoise, 'assets/blood-noise.png');
for (const [entry, original] of [[manifest.bloodMask, 'Blood_Mask.png'], [manifest.bloodNoise, 'Blood_Noise.png']]) {
  const source = path.join('Addons', 'Metrocop', entry);
  assert.equal(crypto.createHash('sha256').update(fs.readFileSync(source)).digest('hex'), crypto.createHash('sha256').update(fs.readFileSync(path.join('Blood_Effect', original))).digest('hex'));
  const staged = path.join('dist', 'BepInEx', 'plugins', 'NotGodlike-Metrocop', entry);
  assert.equal(crypto.createHash('sha256').update(fs.readFileSync(source)).digest('hex'), crypto.createHash('sha256').update(fs.readFileSync(staged)).digest('hex'));
}
const hash = (file) => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
for (const file of files) {
  assert(!path.isAbsolute(file) && !file.split(/[\\/]/).includes('..'));
  const source = path.join('Addons', 'Metrocop', file);
  const staged = path.join('dist', 'BepInEx', 'plugins', 'NotGodlike-Metrocop', file);
  assert.equal(hash(source), hash(staged));
  assert.equal(hash(source), hash(path.join('backups', 'v3.5.0-working-framework-baseline', 'Assets', 'metrocop', path.basename(file))), `Working model data changed: ${file}`);
}
const framework = 'dist/BepInEx/plugins/NotGodlike-CustomSosigModelFramework';
assert(fs.existsSync(path.join(framework, 'CustomSosigModelFramework.dll')));
assert(!fs.readdirSync(framework).some((file) => /\.gz$|\.png$|metrocop/i.test(file)));
assert.equal(canonicalBone('mixamorigHips', { boneAliases: [{ source: 'mixamorigHips', role: 'Pelvis' }] }), 'ValveBiped.Bip01_Pelvis');
console.log(`Passed: ${roles.length} animation roles, ${files.length} byte-identical working addon assets, code-only framework, bone alias conversion.`);
