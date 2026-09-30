# Unity Project Context

<!-- unity-onboarding:generated:start -->

Last analyzed: 2026-09-24

## Project summary

Confirmed: this workspace is a source/build project for an H3VR BepInEx plugin, not a conventional Unity project. It has no local `Assets/`, `Packages/manifest.json`, or `ProjectSettings/ProjectVersion.txt` at the project root. Runtime integration targets the installed H3VR Unity game and the r2modman profile.

## Confirmed environment

- Game: H3VR installed at `H:\SteamLibrary\steamapps\common\H3VR`.
- Game assemblies: legacy `v2.0.50727` CLR metadata; `UnityEngine.dll` is the monolithic pre-module assembly style.
- Mod loader: BepInEx 5.4.1700 in the selected profile.
- Patching: HarmonyX/`0Harmony.dll` from BepInEx.
- Target profile: `E:\Games\r2mods\H3VR\profiles\Default`.
- Reference Unity project: CoDZombies-H3VR uses Unity 5.6.3p4 and MeatKit.

## Architecture

- `Plugin` owns configuration, mesh loading, material creation, and Harmony lifetime.
- `SosigStartPatch` attaches one proxy after every `FistVR.Sosig.Start`.
- `SosigVisualProxy` owns visual hiding and per-frame proxy pose.
- `RuntimeSkinnedAsset` loads the preconverted rig, bind poses, bone weights, UVs, and meshes without requiring an AssetBundle or Unity Editor.
- Vanilla Sosig AI, damage, physics links, colliders, IFF, inventory, and weapons remain authoritative.

## Animation and asset constraints

Confirmed: `metrocop/source/remade metro cop HL2.glb` contains a 55-joint ValveBiped skin, 20,685 vertices across two material primitives, UVs, skin weights, bind poses, and embedded/external body and mask textures. It has zero animation clips. Runtime animation is therefore procedural (gait, idle motion, and weapon-hand targeting), with real skeletal deformation but no authored HL2 clips.

## Testing and validation

- Static validation: compile against the exact installed H3VR/BepInEx assemblies.
- Packaging validation: compare source/dist/profile SHA-256 hashes.
- Runtime validation still required: BepInEx log, Proving Grounds/Take & Hold spawn, scale/orientation, held weapons, clothing suppression, death/ragdoll, and scene transitions.

## Available tooling

- No Unity Editor installation was found in the standard Unity Hub path.
- No Unity MCP provider is configured.
- Node/three.js is used only as an offline FBX conversion tool.
- The plugin is compiled with Roslyn against the game's own .NET/Unity assemblies.

## Important risks and unknowns

- Procedural joint axes and hand targeting need visual tuning against H3VR weapon poses.
- Visual orientation/offset must be confirmed inside H3VR and may need config tuning.
- Hiding every non-weapon child renderer may interact with unusual modded Sosig accessories; runtime testing is required.

## Sources inspected

- `metrocop/source/remade metro cop HL2.glb` and supplied texture set
- `superhot_enemy_stl.fbx` (superseded source retained for reference only)
- H3VR `Assembly-CSharp.dll`, `UnityEngine.dll`, and core framework assemblies
- Profile `mods.yml`, BepInEx core, installed plugins
- CoDZombies-H3VR commit `eeb85194724125d2533f2c19505f75f4471ac839`
- H3VR Modding Wiki “Making Mods”

<!-- unity-onboarding:generated:end -->
