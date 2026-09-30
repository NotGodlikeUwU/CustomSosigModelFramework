import assert from 'node:assert/strict';
import fs from 'node:fs';
import zlib from 'node:zlib';
import { Matrix4, Quaternion, Vector3 } from 'three';

const specs = [
  ['Hips', 'Pelvis', 'Spine2', true], ['Chest', 'Spine2', 'Neck1', true],
  ['Head', 'Head1', null, true],
  ['LeftUpperArm', 'L_UpperArm', 'L_Forearm', false], ['LeftLowerArm', 'L_Forearm', 'L_Hand', false],
  ['RightUpperArm', 'R_UpperArm', 'R_Forearm', false], ['RightLowerArm', 'R_Forearm', 'R_Hand', false],
  ['LeftUpperLeg', 'L_Thigh', 'L_Calf', true], ['LeftLowerLeg', 'L_Calf', 'L_Foot', true],
  ['RightUpperLeg', 'R_Thigh', 'R_Calf', true], ['RightLowerLeg', 'R_Calf', 'R_Foot', true],
];

function readModel(addon) {
  const manifest = JSON.parse(fs.readFileSync(`Addons/${addon}/customsosig-model.json`, 'utf8'));
  const aliases = new Map(manifest.boneAliases.map(item => [item.source, `ValveBiped.Bip01_${item.role}`]));
  const data = zlib.gunzipSync(fs.readFileSync(`Addons/${addon}/${manifest.mesh}`));
  let at = 0;
  const u8 = () => data.readUInt8(at++);
  const i32 = () => { const value = data.readInt32LE(at); at += 4; return value; };
  const f32 = () => { const value = data.readFloatLE(at); at += 4; return value; };
  const vec3 = () => new Vector3(f32(), f32(), f32());
  assert.equal(data.toString('ascii', at, at += 4), 'CSK1');
  assert.equal(i32(), 1);
  f32(); f32();
  const boneCount = i32();
  const bones = [];
  const world = [];
  for (let index = 0; index < boneCount; index++) {
    const length = i32();
    const source = data.toString('utf8', at, at += length);
    const parent = i32();
    const pos = vec3();
    const rot = new Quaternion(f32(), f32(), f32(), f32());
    const scale = vec3();
    bones.push({ name: aliases.get(source) ?? source, parent });
    const local = new Matrix4().compose(pos, rot, scale);
    world.push(parent >= 0 ? world[parent].clone().multiply(local) : local);
  }
  at += boneCount * 16 * 4;
  const byName = new Map(bones.map((bone, index) => [bone.name, index]));
  const segments = specs.map(([label, startName, endName, armor]) => {
    const start = byName.get(`ValveBiped.Bip01_${startName}`);
    const end = endName ? byName.get(`ValveBiped.Bip01_${endName}`) : undefined;
    const origin = start === undefined ? null : new Vector3().setFromMatrixPosition(world[start]);
    const axis = origin === null ? null : end === undefined
      ? new Vector3(0, 1, 0).transformDirection(world[start])
      : new Vector3().setFromMatrixPosition(world[end]).sub(origin).normalize();
    return { label, start, armor, origin, axis, count: 0, min: Infinity, max: -Infinity, radius: 0 };
  });
  const anchor = new Map(segments.flatMap((part, index) => part.start === undefined ? [] : [[part.start, index]]));
  const assigned = bones.map((_, index) => {
    for (let cursor = index; cursor >= 0; cursor = bones[cursor].parent)
      if (anchor.has(cursor)) return anchor.get(cursor);
    return -1;
  });
  const meshCount = i32();
  for (let mesh = 0; mesh < meshCount; mesh++) {
    i32(); // material
    const count = i32();
    for (let vertex = 0; vertex < count; vertex++) {
      const position = vec3();
      at += 12 + 8; // normal and UV
      const joints = [u8(), u8(), u8(), u8()];
      const weights = [f32(), f32(), f32(), f32()];
      let strongest = 0;
      for (let i = 1; i < 4; i++) if (weights[i] > weights[strongest]) strongest = i;
      const part = segments[assigned[joints[strongest]]];
      if (!part?.armor || !part.axis || !part.origin) continue;
      const offset = position.sub(part.origin);
      const y = offset.dot(part.axis);
      const radius = offset.addScaledVector(part.axis, -y).length();
      part.min = Math.min(part.min, y);
      part.max = Math.max(part.max, y);
      part.radius = Math.max(part.radius, radius);
      part.count++;
    }
    const triangles = i32();
    at += triangles * 2;
  }
  return segments.filter(part => part.armor);
}

for (const addon of ['Metrocop', 'MW4Milsim']) {
  const parts = readModel(addon);
  const manifest = JSON.parse(fs.readFileSync(`Addons/${addon}/customsosig-model.json`, 'utf8'));
  const scale = manifest.armorHitboxScale || 1;
  for (const part of parts) {
    assert(part.count >= 16, `${addon} ${part.label} has too few vertices: ${part.count}`);
    assert(part.radius > 0.015 && part.radius < 0.8, `${addon} ${part.label} radius out of range: ${part.radius}`);
    assert(part.max > part.min, `${addon} ${part.label} height invalid`);
  }
  console.log(`${addon} (armor fit ${scale}): ` + parts.map(part => `${part.label} ${part.count} vertices radius=${((part.radius + 0.035) * scale).toFixed(3)}m height=${((part.max - part.min + 0.07) * scale).toFixed(3)}m`).join(', '));
  if (addon === 'MW4Milsim') {
    const chest = parts.find(part => part.label === 'Chest');
    const fullWidth = (chest.radius + 0.035) * 2 * scale;
    assert(scale >= 1 / 3 && scale <= 1 / 2.5,
      'MW4 armor fit should shrink all dimensions by approximately 2.5-3x');
    assert(fullWidth > 0.29 && fullWidth < 0.33,
      `MW4 chest armor should be about 0.31m wide, got ${fullWidth}m`);
  }
  if (addon === 'Metrocop') {
    assert(scale > 0.66 && scale < 0.67,
      'Metrocop armor fit should be about 1.5x smaller than the previous envelope');
    const chest = parts.find(part => part.label === 'Chest');
    const fullWidth = (chest.radius + 0.035) * 2 * scale;
    assert(fullWidth > 0.30 && fullWidth < 0.32,
      `Metrocop chest armor should be about 0.31m wide, got ${fullWidth}m`);
  }
}
