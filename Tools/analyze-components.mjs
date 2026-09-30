import fs from 'node:fs';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';

const input = process.argv[2];
if (!input) throw new Error('Usage: node analyze-components.mjs <file.fbx>');

const bytes = fs.readFileSync(input);
const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
const root = new FBXLoader().parse(buffer, '');
let positions;
root.traverse((node) => {
  if (!positions && node.geometry) positions = node.geometry.attributes.position.array;
});
if (!positions) throw new Error('No mesh geometry found');

const triangleCount = positions.length / 9;
const parent = new Int32Array(triangleCount);
for (let i = 0; i < triangleCount; i += 1) parent[i] = i;

function find(value) {
  let rootValue = value;
  while (parent[rootValue] !== rootValue) rootValue = parent[rootValue];
  while (parent[value] !== value) {
    const next = parent[value];
    parent[value] = rootValue;
    value = next;
  }
  return rootValue;
}

function union(a, b) {
  a = find(a);
  b = find(b);
  if (a !== b) parent[b] = a;
}

const ownerByVertex = new Map();
for (let triangle = 0; triangle < triangleCount; triangle += 1) {
  for (let corner = 0; corner < 3; corner += 1) {
    const offset = triangle * 9 + corner * 3;
    const key = `${positions[offset].toFixed(5)},${positions[offset + 1].toFixed(5)},${positions[offset + 2].toFixed(5)}`;
    const owner = ownerByVertex.get(key);
    if (owner === undefined) ownerByVertex.set(key, triangle);
    else union(triangle, owner);
  }
}

const components = new Map();
for (let triangle = 0; triangle < triangleCount; triangle += 1) {
  const component = find(triangle);
  let stats = components.get(component);
  if (!stats) {
    stats = {
      triangles: 0,
      min: [Infinity, Infinity, Infinity],
      max: [-Infinity, -Infinity, -Infinity],
    };
    components.set(component, stats);
  }
  stats.triangles += 1;
  for (let corner = 0; corner < 3; corner += 1) {
    const offset = triangle * 9 + corner * 3;
    for (let axis = 0; axis < 3; axis += 1) {
      const value = positions[offset + axis];
      stats.min[axis] = Math.min(stats.min[axis], value);
      stats.max[axis] = Math.max(stats.max[axis], value);
    }
  }
}

const result = [...components.values()]
  .sort((a, b) => b.triangles - a.triangles)
  .map((component, index) => ({ index, ...component }));
console.log(JSON.stringify({ triangleCount, componentCount: result.length, components: result }, null, 2));
