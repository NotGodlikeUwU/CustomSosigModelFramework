import assert from 'node:assert/strict';
import fs from 'node:fs';

const proxy = fs.readFileSync('src/CustomSosigReplacer/SosigVisualProxy.cs', 'utf8');
const sourceFiles = fs.readdirSync('src/CustomSosigReplacer').filter(file => file.endsWith('.cs'));
assert.match(proxy, /ApplySosigHandPose\(1f - _ballisticWeight\)/);
assert.match(proxy, /Vector3 grip = hand\.HeldObject\.RecoilHolder\.position/);
assert.match(proxy, /Vector3\.Distance\(visualHand\.position, grip\) > 0\.22f/);
assert.doesNotMatch(proxy, /hand\.Target\.(?:position|rotation)\s*=/);
assert.doesNotMatch(proxy, /ApplyAuthoredHandTarget|DriveNativeHandsFromAuthoredPose/);
for (const file of sourceFiles) {
  const source = fs.readFileSync(`src/CustomSosigReplacer/${file}`, 'utf8');
  assert.doesNotMatch(source, /\[HarmonyPatch\(typeof\(SosigHand\),\s*"(?:Hold|UpdateGunHandlingPose)"\)\]/,
    `${file} must not override native Sosig gun aiming`);
}
console.log('Native gun targets and SosigHand aiming methods remain unmodified by the visual proxy.');
