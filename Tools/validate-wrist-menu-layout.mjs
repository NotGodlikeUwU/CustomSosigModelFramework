import assert from 'node:assert/strict';
import fs from 'node:fs';

const source = fs.readFileSync('src/CustomSosigReplacer/CustomSosigsWristMenu.cs', 'utf8');
const constant = name => {
  const match = source.match(new RegExp(`private const (?:int|float) ${name} = ([0-9.]+)f?;`));
  assert(match, `Missing ${name}`);
  return Number(match[1]);
};
const columns = constant('ChoiceColumns');
const columnSpacing = constant('ChoiceColumnSpacing');
const rowSpacing = constant('ChoiceRowSpacing');
const firstY = constant('FirstChoiceRowY');
assert.equal(columns, 3, 'Keep the original compact three-column grid');
assert.match(source, /if \(labelRect != rect\)/, 'Long labels must not stretch the button root');

function intersects(a, b) {
  return Math.abs(a.x - b.x) < (a.w + b.w) / 2 &&
    Math.abs(a.y - b.y) < (a.h + b.h) / 2;
}
for (let choices = 2; choices <= 12; choices++) {
  const slots = Array.from({ length: choices }, (_, index) => ({
    x: (index % columns - 1) * columnSpacing,
    y: firstY - Math.floor(index / columns) * rowSpacing,
    w: 108,
    h: 55,
  }));
  const choiceRows = Math.ceil(choices / columns);
  slots.push({ x: 0, y: firstY - choiceRows * rowSpacing, w: 108 * 0.6, h: 55 * 0.6 });
  for (let i = 0; i < slots.length; i++)
    for (let j = i + 1; j < slots.length; j++)
      assert(!intersects(slots[i], slots[j]), `Overlapping slots ${i} and ${j} with ${choices} choices`);
}
console.log('Wrist Menu slot layout validated for 2–12 model choices (RectTransform bounds only).');
