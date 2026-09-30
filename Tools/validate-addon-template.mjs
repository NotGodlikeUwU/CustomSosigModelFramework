import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const templateRoot = path.resolve('Templates/ModelAddon');
const template = JSON.parse(fs.readFileSync(path.join(templateRoot, 'customsosig-model.json'), 'utf8'));
const sources = JSON.parse(fs.readFileSync(path.join(templateRoot, 'animation-sources.json'), 'utf8'));
const reference = JSON.parse(fs.readFileSync('Addons/Metrocop/customsosig-model.json', 'utf8'));
assert.equal(template.schemaVersion, 1);
assert.equal(template.impactEffect, 'native');
assert(!fs.existsSync('Addons/ModelAddon'), 'Placeholder template must not be installed as an addon');
assert.deepEqual(template.animations.map(x => x.role), reference.animations.map(x => x.role));
assert.deepEqual(sources.map(x => x.role), template.animations.map(x => x.role));
assert.equal(new Set(template.animations.map(x => x.file)).size, template.animations.length);
const mapped = new Set(template.boneAliases.map(x => x.role));
for (const side of ['L', 'R'])
  for (const part of ['Clavicle', 'UpperArm', 'Forearm', 'Hand', 'Thigh', 'Calf', 'Foot'])
    assert(mapped.has(`${side}_${part}`), `Missing ${side}_${part}`);
for (const part of ['Pelvis', 'Spine', 'Spine1', 'Spine2', 'Neck1', 'Head1'])
  assert(mapped.has(part), `Missing ${part}`);
for (const source of sources) {
  assert(fs.existsSync(source.source), `Missing example animation: ${source.source}`);
  if (source.bind) assert(fs.existsSync(source.bind), `Missing bind skeleton: ${source.bind}`);
}
console.log(`Addon template validated: ${template.animations.length} roles, ${template.boneAliases.length} sample aliases, all example FBX paths exist.`);
