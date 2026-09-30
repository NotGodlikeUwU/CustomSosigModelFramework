# Custom Sosig Model Framework

**A modular custom-character framework for Hot Dogs, Horseshoes & Hand Grenades (H3VR).**

The framework replaces the rendered appearance of H3VR Sosigs with humanoid models supplied by independent addon packages. H3VR remains responsible for the Sosig's AI, health, armor, weapons, aiming, damage, and game-mode behavior. The framework drives the custom model's animation and visual hitboxes from that native Sosig.

The framework and its companion packages are authored by **NotGodlike**.

## Features

- Modular model addons: install or remove models without rebuilding the framework.
- Wrist Menu integration under **Custom Sosigs**:
  - **Sosig** restores the original H3VR appearance.
  - **Random** chooses from the installed model addons for each Sosig.
  - Each addon registers its own named model option.
- Runtime animation mapping for locomotion, crouching, weapon handling, hit reactions, transitions, and death/fall states.
- Visual hand IK to keep animated hands near the Sosig's actual weapon grip without changing H3VR's weapon aiming or accuracy.
- Armor-aware custom hitboxes and an optional translucent blue **Armor Hitbox** visualization. Body hitboxes are not drawn.
- Custom blood impact and death effects for supported model addons.
- Existing and newly spawned Sosigs use the selected model. The selection is saved in the BepInEx configuration.

## Installation

This source repository does not include a compiled release or the model/animation payloads for Metrocop and MW4 Milsim. Obtain compatible framework and addon packages separately; then install them as separate folders under `H3VR/BepInEx/plugins/` and start H3VR. Select **Custom Sosigs** from the Wrist Menu. The framework alone falls back to the original Sosig appearance until at least one compatible model addon is installed.

Packages are intentionally separate:

```text
BepInEx/plugins/
├── NotGodlike-CustomSosigModelFramework/
│   └── CustomSosigModelFramework.dll
├── NotGodlike-Metrocop/                 # optional model addon
│   ├── customsosig-model.json
│   └── assets/
└── NotGodlike-MW4Milsim/                # optional model addon
    ├── customsosig-model.json
    └── assets/
```

Only install one copy of `CustomSosigModelFramework.dll`. Do not install an older standalone Sosig replacer alongside the framework.

> This source repository contains the framework and model-authoring templates. Game models, textures, and animation files are not bundled here unless their redistribution rights are explicitly documented. Model addons may be distributed separately by their respective authors.

## Configuration

The BepInEx configuration file is:

```text
BepInEx/config/NotGodlike.h3vr.customsosigmodelframework.cfg
```

The selected model and Armor Hitbox display preference are saved automatically. If a selected addon is not installed, the framework falls back to the original Sosig appearance. The Armor Hitbox toggle is a visualization only; it does not change damage, armor, or collision behavior.

## Creating a model addon

No framework code changes are needed for a normal addon. Start with either:

- [`Templates/ModelAddon`](Templates/ModelAddon/README.md) for the addon manifest and runtime asset layout.
- [`Templates/UnityModelAddon`](Templates/UnityModelAddon/README.md) for the Unity authoring project scaffold and armor-hitbox editing helper.

Read the full format and preparation guide in [`Docs/ModelAddons.md`](Docs/ModelAddons.md). The preparation script is [`Tools/prepare-model-addon.ps1`](Tools/prepare-model-addon.ps1).

In brief, an addon supplies a valid model manifest, converted skinned mesh, materials/textures, a compatible humanoid skeleton, and animation clips mapped to the framework's animation roles. An FBX or GLB placed directly into the game folder is not sufficient. Animation quality depends on compatible bone mapping, consistent bind pose/orientation, and correct import settings.

### Armor hitboxes in Unity

The Unity template includes an editor helper for adjusting the addon armor-hitbox volumes. Use the visual model bounds and armor coverage as reference, then export and validate the generated addon data in H3VR. The runtime armor overlay only visualizes armor zones that participate in the custom hitbox path; unarmored body regions are intentionally omitted.

## Build and install from source

This project targets the H3VR assemblies installed on the build machine. Close H3VR before installing a rebuilt plugin.

```powershell
.\Tools\build-plugin.ps1
.\Tools\install-plugin.ps1
```

The scripts build the plugin against local H3VR/BepInEx assemblies and deploy locally available framework/addon packages. The repository itself does not provide those game assemblies or third-party model data. Review script parameters and paths before using them on a different installation.

## Validation

The `Tools/validate-*.mjs` scripts include checks tied to locally available addon payloads and game builds; check each script's prerequisites before running it. A successful compilation or static validation does not prove in-game appearance or behavior. Wrist Menu layout, animation transitions, weapon grip, armor damage, blood effects, and ragdoll behavior must be verified in H3VR with the relevant addon installed.

## Compatibility and troubleshooting

- Confirm the framework DLL and addon manifests are in separate plugin folders under `BepInEx/plugins`.
- Check `BepInEx/LogOutput.log` for addon discovery messages and asset/import errors.
- Make sure each addon ID is unique and each manifest path resolves within its own package.
- If an addon is not available, the framework should use the original Sosig model rather than breaking the native Sosig.
- If animation bones twist or fail to move, verify the source rig, bind pose, humanoid mapping, scale, and per-clip root-motion/import settings before changing runtime code.
- Keep H3VR's native Sosig components enabled: they own AI, weapon logic, armor, and damage.

## Repository layout

```text
src/                         Framework plugin source
Docs/                        Addon format and implementation notes
Tools/                       Build, conversion, and validation scripts
Templates/ModelAddon/        Data package starter template
Templates/UnityModelAddon/    Unity authoring project scaffold
```

## License and third-party assets

No rights to third-party content are granted by this repository. In particular, it does not grant rights to redistribute H3VR, Call of Duty, Half-Life 2, Mixamo, Low Poly Shooter Pack, or other third-party models, textures, animations, names, or trademarks. Obtain the applicable permissions and follow each asset's license before packaging or publishing an addon. The framework code and documentation remain subject to the repository's license, if one is added.

See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for project-specific attribution and asset notes.
