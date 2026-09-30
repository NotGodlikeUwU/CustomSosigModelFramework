import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import assert from 'node:assert/strict';
import { Quaternion } from 'three';

const names = ['rifle_jog', 'handgun_jog', 'jog_forward', 'jog_backward', 'jog_left_forward', 'jog_right_forward', 'jog_left_backward', 'jog_right_backward', 'breathing'];
for (const name of names) {
  const file = path.join('Assets', 'metrocop', `lpsp_${name}.csanim.gz`);
  const data = zlib.gunzipSync(fs.readFileSync(file));
  let offset = 4;
  const int = () => { const value = data.readInt32LE(offset); offset += 4; return value; };
  const float = () => { const value = data.readFloatLE(offset); offset += 4; return value; };
  assert.equal(data.toString('ascii', 0, 4), 'CSA1');
  assert.equal(int(), 1);
  const stringLength = int(); offset += stringLength;
  const duration = float(); const fps = float(); const frames = int(); const tracks = int();
  assert(duration > 0 && fps > 0 && frames >= 2 && tracks === 50);
  for (let track = 0; track < tracks; track++) {
    const bone = int(); assert(bone >= 0 && bone < 55);
    for (let frame = 0; frame < frames; frame++) {
      const q = new Quaternion(float(), float(), float(), float());
      assert([q.x, q.y, q.z, q.w].every(Number.isFinite));
      assert(Math.abs(q.length() - 1) < 0.00001);
      // Recoil/breathing at their reference frame must leave the base pose
      // unchanged, unlike the former absolute pose replacement.
      const base = new Quaternion().setFromAxisAngle({ x: 0, y: 1, z: 0 }, 0.4);
      const unchanged = base.clone().multiply(q.clone().invert().multiply(q));
      assert(Math.abs(unchanged.dot(base)) > 0.999999);
    }
  }
  assert.equal(offset, data.length, `Unexpected trailing data in ${file}`);
  const report = JSON.parse(fs.readFileSync(file.replace(/\.gz$/, '.json')));
  assert(report.maxSegmentDirectionErrorDegrees < 0.001);
  const dynamicArms = report.tracks.filter((t) => /UpperArm|Forearm/.test(t.bone) && t.maxAnimatedDeltaDegrees > 1);
  if (/jog/.test(name)) assert(dynamicArms.length >= 2, `${name} has no meaningful arm motion`);
  console.log(`${name}: valid ${frames} frames, ${tracks} bones, ${dynamicArms.length} animated arm segments`);
}
