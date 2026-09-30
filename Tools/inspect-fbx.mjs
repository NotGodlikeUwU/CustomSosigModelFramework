import fs from 'node:fs';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';

const input = process.argv[2];
if (!input) {
  throw new Error('Usage: node inspect-fbx.mjs <file.fbx>');
}

const bytes = fs.readFileSync(input);
const arrayBuffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
const root = new FBXLoader().parse(arrayBuffer, '');
const nodes = [];
root.traverse((node) => {
  const geometry = node.geometry;
  if (!geometry) {
    nodes.push({ name: node.name, type: node.type, position: node.position.toArray(), rotation: node.rotation.toArray(), scale: node.scale.toArray() });
    return;
  }

  geometry.computeBoundingBox();
  geometry.computeBoundingSphere();
  nodes.push({
    name: node.name,
    type: node.type,
    position: node.position.toArray(),
    rotation: node.rotation.toArray(),
    scale: node.scale.toArray(),
    matrix: node.matrix.toArray(),
    attributes: Object.fromEntries(
      Object.entries(geometry.attributes).map(([name, value]) => [name, {
        count: value.count,
        itemSize: value.itemSize,
      }]),
    ),
    indexed: Boolean(geometry.index),
    indexCount: geometry.index?.count ?? 0,
    boundingBox: {
      min: geometry.boundingBox.min.toArray(),
      max: geometry.boundingBox.max.toArray(),
    },
    boundingSphereRadius: geometry.boundingSphere.radius,
  });
});

console.log(JSON.stringify({
  input,
  animations: root.animations.map((clip) => ({
    name: clip.name,
    duration: clip.duration,
    tracks: clip.tracks.length,
  })),
  nodes,
}, null, 2));
