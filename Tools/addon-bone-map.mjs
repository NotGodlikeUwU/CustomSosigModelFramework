import fs from 'node:fs';
export function readAddonManifest(file) {
  if (!file) return null;
  const manifest = JSON.parse(fs.readFileSync(file, 'utf8'));
  if (manifest.schemaVersion !== 1) throw new Error('Unsupported addon manifest schema');
  return manifest;
}
export function canonicalBone(name, manifest) {
  const alias = manifest?.boneAliases?.find((entry) => entry.source === name);
  return alias ? `ValveBiped.Bip01_${alias.role}` : name;
}
