import fs from 'node:fs';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';

const input = process.argv[2];
const axes = process.argv[3] ?? 'xy';
const width = Number(process.argv[4] ?? 100);
const height = Number(process.argv[5] ?? 50);
const bytes = fs.readFileSync(input);
const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
const root = new FBXLoader().parse(buffer, '');
let positions;
root.traverse((node) => { if (!positions && node.geometry) positions = node.geometry.attributes.position.array; });

const axisIndices = { x: 0, y: 1, z: 2 };
const a = axisIndices[axes[0]];
const b = axisIndices[axes[1]];
const valuesA = [];
const valuesB = [];
for (let i = 0; i < positions.length; i += 3) {
  valuesA.push(positions[i + a]);
  valuesB.push(positions[i + b]);
}
valuesA.sort((x, y) => x - y);
valuesB.sort((x, y) => x - y);
const percentile = (values, p) => values[Math.floor((values.length - 1) * p)];
const minA = percentile(valuesA, 0.001);
const maxA = percentile(valuesA, 0.999);
const minB = percentile(valuesB, 0.001);
const maxB = percentile(valuesB, 0.999);
const grid = Array.from({ length: height }, () => new Uint32Array(width));
for (let i = 0; i < positions.length; i += 3) {
  const x = Math.round(((positions[i + a] - minA) / (maxA - minA)) * (width - 1));
  const y = Math.round(((positions[i + b] - minB) / (maxB - minB)) * (height - 1));
  if (x >= 0 && x < width && y >= 0 && y < height) grid[height - 1 - y][x] += 1;
}
const shades = ' .:-=+*#%@';
console.log(`${axes}: [${minA.toFixed(2)}, ${maxA.toFixed(2)}] x [${minB.toFixed(2)}, ${maxB.toFixed(2)}]`);
for (const row of grid) {
  console.log([...row].map((count) => shades[Math.min(shades.length - 1, Math.floor(Math.log2(count + 1)))]).join(''));
}
