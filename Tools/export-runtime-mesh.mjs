import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';

const input = process.argv[2];
const output = process.argv[3];
if (!input || !output) {
  throw new Error('Usage: node export-runtime-mesh.mjs <input.fbx> <output.meshbin.gz>');
}

const bytes = fs.readFileSync(input);
const arrayBuffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
const root = new FBXLoader().parse(arrayBuffer, '');
let geometry;
root.traverse((node) => { if (!geometry && node.geometry) geometry = node.geometry; });
if (!geometry) throw new Error('No mesh geometry found in FBX');
if (root.animations.length !== 0) throw new Error('This static-mesh exporter does not support animation clips');

const positions = geometry.attributes.position.array;
const normals = geometry.attributes.normal.array;
if (!positions || !normals || positions.length !== normals.length || positions.length % 9 !== 0) {
  throw new Error('Expected a non-indexed triangle mesh with position and normal attributes');
}

const vertexCount = positions.length / 3;
const centroid = [0, 0, 0];
for (let i = 0; i < positions.length; i += 3) {
  centroid[0] += positions[i];
  centroid[1] += positions[i + 1];
  centroid[2] += positions[i + 2];
}
for (let axis = 0; axis < 3; axis += 1) centroid[axis] /= vertexCount;

const covariance = [[0, 0, 0], [0, 0, 0], [0, 0, 0]];
for (let i = 0; i < positions.length; i += 3) {
  const p = [positions[i] - centroid[0], positions[i + 1] - centroid[1], positions[i + 2] - centroid[2]];
  for (let row = 0; row < 3; row += 1) {
    for (let column = 0; column < 3; column += 1) covariance[row][column] += p[row] * p[column];
  }
}
for (let row = 0; row < 3; row += 1) {
  for (let column = 0; column < 3; column += 1) covariance[row][column] /= vertexCount;
}

function eigenDecompositionSymmetric3(matrix) {
  const a = matrix.map((row) => [...row]);
  const vectors = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
  for (let iteration = 0; iteration < 32; iteration += 1) {
    let p = 0;
    let q = 1;
    let maximum = Math.abs(a[p][q]);
    for (const [candidateP, candidateQ] of [[0, 2], [1, 2]]) {
      if (Math.abs(a[candidateP][candidateQ]) > maximum) {
        p = candidateP;
        q = candidateQ;
        maximum = Math.abs(a[p][q]);
      }
    }
    if (maximum < 1e-9) break;

    const angle = 0.5 * Math.atan2(2 * a[p][q], a[q][q] - a[p][p]);
    const c = Math.cos(angle);
    const s = Math.sin(angle);
    const app = a[p][p];
    const aqq = a[q][q];
    const apq = a[p][q];
    a[p][p] = c * c * app - 2 * s * c * apq + s * s * aqq;
    a[q][q] = s * s * app + 2 * s * c * apq + c * c * aqq;
    a[p][q] = a[q][p] = 0;

    for (let r = 0; r < 3; r += 1) {
      if (r === p || r === q) continue;
      const arp = a[r][p];
      const arq = a[r][q];
      a[r][p] = a[p][r] = c * arp - s * arq;
      a[r][q] = a[q][r] = s * arp + c * arq;
    }
    for (let r = 0; r < 3; r += 1) {
      const vrp = vectors[r][p];
      const vrq = vectors[r][q];
      vectors[r][p] = c * vrp - s * vrq;
      vectors[r][q] = s * vrp + c * vrq;
    }
  }

  return [0, 1, 2]
    .map((index) => ({ value: a[index][index], vector: [vectors[0][index], vectors[1][index], vectors[2][index]] }))
    .sort((left, right) => right.value - left.value);
}

const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const normalize = (value) => {
  const length = Math.sqrt(dot(value, value));
  return value.map((component) => component / length);
};

const eigen = eigenDecompositionSymmetric3(covariance);
let up = normalize(eigen[0].vector);
let forward = normalize(eigen[2].vector);

const projections = [];
for (let i = 0; i < positions.length; i += 3) {
  const centered = [positions[i] - centroid[0], positions[i + 1] - centroid[1], positions[i + 2] - centroid[2]];
  projections.push({ along: dot(centered, up), radial2: dot(centered, centered) - Math.pow(dot(centered, up), 2) });
}
projections.sort((left, right) => left.along - right.along);
const sampleCount = Math.max(100, Math.floor(projections.length * 0.06));
const lowRadius = projections.slice(0, sampleCount).reduce((sum, item) => sum + item.radial2, 0) / sampleCount;
const highRadius = projections.slice(-sampleCount).reduce((sum, item) => sum + item.radial2, 0) / sampleCount;
if (lowRadius < highRadius) up = up.map((value) => -value);

let right = normalize(cross(up, forward));
forward = normalize(cross(right, up));

const transformed = new Float32Array(positions.length);
const transformedNormals = new Float32Array(normals.length);
let minY = Infinity;
let maxY = -Infinity;
let minX = Infinity;
let maxX = -Infinity;
let minZ = Infinity;
let maxZ = -Infinity;
for (let i = 0; i < positions.length; i += 3) {
  const centered = [positions[i] - centroid[0], positions[i + 1] - centroid[1], positions[i + 2] - centroid[2]];
  const x = dot(centered, right);
  const y = dot(centered, up);
  const z = dot(centered, forward);
  transformed[i] = x;
  transformed[i + 1] = y;
  transformed[i + 2] = z;
  minX = Math.min(minX, x); maxX = Math.max(maxX, x);
  minY = Math.min(minY, y); maxY = Math.max(maxY, y);
  minZ = Math.min(minZ, z); maxZ = Math.max(maxZ, z);

  const normal = normalize([normals[i], normals[i + 1], normals[i + 2]]);
  transformedNormals[i] = dot(normal, right);
  transformedNormals[i + 1] = dot(normal, up);
  transformedNormals[i + 2] = dot(normal, forward);
}

const sourceHeight = maxY - minY;
const centerX = (minX + maxX) * 0.5;
const centerZ = (minZ + maxZ) * 0.5;
for (let i = 0; i < transformed.length; i += 3) {
  transformed[i] = (transformed[i] - centerX) / sourceHeight;
  transformed[i + 1] = (transformed[i + 1] - minY) / sourceHeight;
  transformed[i + 2] = (transformed[i + 2] - centerZ) / sourceHeight;
}

const maximumVerticesPerChunk = 60000;
const chunks = [];
for (let startVertex = 0; startVertex < vertexCount; startVertex += maximumVerticesPerChunk) {
  const count = Math.min(maximumVerticesPerChunk, vertexCount - startVertex);
  chunks.push({ startVertex, count: count - (count % 3) });
}
if (chunks.reduce((sum, chunk) => sum + chunk.count, 0) !== vertexCount) throw new Error('Chunk split lost vertices');

const byteLength = 4 + 4 + 4 + chunks.reduce((sum, chunk) => sum + 4 + chunk.count * 6 * 4, 0);
const outputBuffer = Buffer.allocUnsafe(byteLength);
let offset = 0;
outputBuffer.write('CSM1', offset, 'ascii'); offset += 4;
outputBuffer.writeInt32LE(1, offset); offset += 4;
outputBuffer.writeInt32LE(chunks.length, offset); offset += 4;
for (const chunk of chunks) {
  outputBuffer.writeInt32LE(chunk.count, offset); offset += 4;
  for (let localVertex = 0; localVertex < chunk.count; localVertex += 1) {
    const sourceOffset = (chunk.startVertex + localVertex) * 3;
    for (let axis = 0; axis < 3; axis += 1) { outputBuffer.writeFloatLE(transformed[sourceOffset + axis], offset); offset += 4; }
    for (let axis = 0; axis < 3; axis += 1) { outputBuffer.writeFloatLE(transformedNormals[sourceOffset + axis], offset); offset += 4; }
  }
}

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, zlib.gzipSync(outputBuffer, { level: 9 }));
const info = {
  source: path.basename(input),
  sourceSha256: crypto.createHash('sha256').update(bytes).digest('hex'),
  sourceAnimations: root.animations.length,
  vertexCount,
  triangleCount: vertexCount / 3,
  chunkCount: chunks.length,
  eigenvalues: eigen.map((item) => item.value),
  basis: { right, up, forward },
  normalizedBounds: {
    min: [(minX - centerX) / sourceHeight, 0, (minZ - centerZ) / sourceHeight],
    max: [(maxX - centerX) / sourceHeight, 1, (maxZ - centerZ) / sourceHeight],
  },
};
fs.writeFileSync(output.replace(/\.gz$/i, '.json'), JSON.stringify(info, null, 2));
console.log(JSON.stringify(info, null, 2));
