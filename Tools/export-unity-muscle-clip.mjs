import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const input = process.argv[2];
const output = process.argv[3];
if (!input || !output) throw new Error('Usage: node export-unity-muscle-clip.mjs <input.anim> <output.csmuscle.gz>');

const source = fs.readFileSync(input, 'utf8');
const clipName = /^  m_Name: (.+)$/m.exec(source)?.[1]?.trim() ?? path.basename(input, '.anim');
const curveBlocks = source.split(/\r?\n(?=  - curve:)/g).slice(1);
const curves = [];
let duration = 0;

for (const block of curveBlocks) {
  const attribute = /^    attribute: (.+)$/m.exec(block)?.[1]?.trim();
  if (!attribute) continue;
  const keys = [];
  const keyPattern = /      - serializedVersion: 2\r?\n        time: ([^\r\n]+)\r?\n        value: ([^\r\n]+)\r?\n        inSlope: ([^\r\n]+)\r?\n        outSlope: ([^\r\n]+)/g;
  for (const match of block.matchAll(keyPattern)) {
    const key = match.slice(1).map(Number);
    if (key.some((value) => !Number.isFinite(value))) continue;
    keys.push({ time: key[0], value: key[1], inSlope: key[2], outSlope: key[3] });
    duration = Math.max(duration, key[0]);
  }
  if (keys.length > 0) curves.push({ attribute, keys });
}

if (curves.length === 0 || duration <= 0) throw new Error(`No usable float curves found in ${input}`);

class Writer {
  constructor() { this.parts = []; }
  bytes(value) { this.parts.push(Buffer.from(value)); }
  int(value) { const data = Buffer.allocUnsafe(4); data.writeInt32LE(value); this.parts.push(data); }
  float(value) { const data = Buffer.allocUnsafe(4); data.writeFloatLE(value); this.parts.push(data); }
  string(value) { const data = Buffer.from(value, 'utf8'); this.int(data.length); this.bytes(data); }
  finish() { return Buffer.concat(this.parts); }
}

const writer = new Writer();
writer.bytes('CSM1');
writer.int(1);
writer.string(clipName);
writer.float(duration);
writer.int(curves.length);
for (const curve of curves) {
  writer.string(curve.attribute);
  writer.int(curve.keys.length);
  for (const key of curve.keys) {
    writer.float(key.time);
    writer.float(key.value);
    writer.float(key.inSlope);
    writer.float(key.outSlope);
  }
}

const raw = writer.finish();
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, zlib.gzipSync(raw, { level: 9 }));
const result = {
  source: path.resolve(input),
  sourceSha256: crypto.createHash('sha256').update(source).digest('hex'),
  clipName,
  duration,
  curveCount: curves.length,
  keyCount: curves.reduce((total, curve) => total + curve.keys.length, 0),
  attributes: curves.map((curve) => curve.attribute),
  compressedBytes: fs.statSync(output).size,
};
fs.writeFileSync(output.replace(/\.gz$/i, '.json'), JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2));
