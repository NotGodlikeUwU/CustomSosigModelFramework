import fs from 'node:fs';
import zlib from 'node:zlib';
import assert from 'node:assert/strict';
import { Matrix4, Quaternion, Vector3 } from 'three';

class Reader {
  constructor(file) { this.data = zlib.gunzipSync(fs.readFileSync(file)); this.offset = 0; }
  int() { const value = this.data.readInt32LE(this.offset); this.offset += 4; return value; }
  float() { const value = this.data.readFloatLE(this.offset); this.offset += 4; return value; }
  byte() { return this.data[this.offset++]; }
  string() { const count = this.int(); const value = this.data.toString('utf8', this.offset, this.offset + count); this.offset += count; return value; }
  vector() { return new Vector3(this.float(), this.float(), this.float()); }
  quaternion() { return new Quaternion(this.float(), this.float(), this.float(), this.float()); }
}
const mesh = new Reader('Assets/metrocop/metrocop.cskmesh.gz');
mesh.offset = 4; assert.equal(mesh.int(), 1);
const minY = mesh.float(); const maxY = mesh.float(); const count = mesh.int();
const bones = Array.from({ length: count }, () => ({ name: mesh.string(), parent: mesh.int(), position: mesh.vector(), rotation: mesh.quaternion(), scale: mesh.vector() }));
const bindposes = Array.from({ length: count }, () => new Matrix4().set(...Array.from({ length: 16 }, () => mesh.float())));
const soles = new Map();
const primitiveCount = mesh.int();
for (let primitive = 0; primitive < primitiveCount; primitive++) {
  mesh.int(); const vertices = mesh.int();
  for (let vertex = 0; vertex < vertices; vertex++) {
    const position = mesh.vector(); mesh.offset += 20;
    const indices = Array.from({ length: 4 }, () => mesh.byte());
    const weights = Array.from({ length: 4 }, () => mesh.float());
    const dominant = indices[weights.indexOf(Math.max(...weights))];
    if (/_(L|R)_(Foot|Toe0)$/.test(bones[dominant].name) && (!soles.has(dominant) || soles.get(dominant).position.y > position.y)) soles.set(dominant, { position, indices, weights });
  }
  mesh.offset += mesh.int() * 2;
}
assert(soles.size >= 2, 'Insufficient sole markers');
const scale = 1.72 / (maxY - minY);
const root = new Matrix4().makeScale(scale, scale, scale).multiply(new Matrix4().makeTranslation(0, -minY, 0));
function worldMatrices(rotations) {
  const world = [];
  for (let bone = 0; bone < bones.length; bone++) {
    const definition = bones[bone];
    const local = new Matrix4().compose(definition.position, rotations[bone], definition.scale);
    world[bone] = (definition.parent >= 0 ? world[definition.parent] : root).clone().multiply(local);
  }
  return world;
}
function solePoints(world) {
  return [...soles.values()].map((sole) => sole.indices.reduce((point, bone, index) => point.add(sole.position.clone().applyMatrix4(bindposes[bone]).applyMatrix4(world[bone]).multiplyScalar(sole.weights[index])), new Vector3()));
}
let cases = 0;
for (const name of ['lpsp_relaxed_idle', 'lpsp_jog_forward', 'lpsp_jog_backward', 'mixamo_crouch_idle', 'mixamo_stand_to_crouch', 'mixamo_crouch_to_stand', 'mixamo_knocked_down']) {
  const clip = new Reader(`Assets/metrocop/${name}.csanim.gz`); clip.offset = 4; assert.equal(clip.int(), 1);
  clip.string(); clip.float(); clip.float(); const frames = clip.int(); const tracks = clip.int();
  const samples = Array.from({ length: frames }, () => bones.map((bone) => bone.rotation));
  for (let track = 0; track < tracks; track++) {
    const bone = clip.int();
    for (let frame = 0; frame < frames; frame++) {
      const rotation = clip.quaternion(); assert(Number.isFinite(rotation.length()) && Math.abs(rotation.length() - 1) < 0.00001);
      samples[frame][bone] = rotation;
    }
  }
  let largestCorrection = 0;
  for (const rotations of samples) {
    const points = solePoints(worldMatrices(rotations));
    const minimum = Math.min(...points.map((point) => point.y));
    for (const floor of [0, 0.3, -0.2]) {
      const correction = floor + 0.008 - minimum;
      const grounded = Math.min(...points.map((point) => point.y + correction));
      assert(Math.abs(grounded - floor - 0.008) < 1e-6);
      cases++;
    }
    largestCorrection = Math.max(largestCorrection, Math.abs(0.008 - minimum));
  }
  console.log(`${name}: ${frames} valid poses; max baseline sole offset ${largestCorrection.toFixed(3)}m`);
}
console.log(`Passed ${cases} flat-floor geometry cases with ${soles.size} skinned sole markers. Physics queries and runtime state still require in-game validation.`);
