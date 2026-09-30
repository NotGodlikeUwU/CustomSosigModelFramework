# Custom Sosig Model Addon Template

This folder is a data-package starter, not a ready-to-install mod. It intentionally contains no character mesh, textures, or animation binaries. Use the template manifest and animation-role list as a starting point for an addon built with assets you own or are licensed to redistribute.

## 1. Prepare a rigged model

Export a binary GLB 2.0 with UVs, skin weights, and one humanoid armature/skin. The runtime converter expects one mesh and one skin, at most 255 bones, at most 16 material primitives, and no more than 65,000 vertices per primitive. Keep a valid T/A-pose, consistent forward direction, sensible meter scale, and up to four significant bone weights per vertex. Footwear should be weighted to the leg/foot bones because the framework estimates ground contact from the skinned geometry.

Set every `boneAliases[].source` value in `customsosig-model.json` to the exact bone name in your exported GLB. Keep the framework `role` names intact. The sample aliases use a common Mixamo-style rig; other rigs need their own complete mapping. See [`../../Docs/ModelAddons.md`](../../Docs/ModelAddons.md) for required roles and manifest fields.

## 2. Configure the addon package

Copy this folder into your project as `Addons/YourModel`, then set a unique addon `id`, `displayName`, `menuName`, and actual `heightMeters`. Add one `materials` entry for each material in GLB index order. Put your own albedo and optional normal PNGs in the package's `assets/` directory, and reference them by relative path. Do not flip both the texture and the UVs.

`surface.smoothness` follows Unity's convention: lower smoothness is rougher and less glossy. `armorHitboxScale` controls generated armor collider dimensions. `1.0` keeps the measured size; `0.667` scales each dimension to about two thirds. The supported range is `0.25` to `1.5`. You can adjust the value in the manifest or use the Unity template's Armor Hitbox Scale window. Scene or FBX colliders are not loaded by the game framework.

## 3. Retarget compatible animations

Edit `animation-sources.json` and point its 35 role entries at your own compatible FBX animation clips. The template paths are placeholders; no animation files are included. When a pose-only FBX uses a separate bind skeleton, set its `bind` path as supported by the converter. The target model still needs all required joints, a consistent bind pose, and correctly oriented bone axes. Do not copy finished `.csanim.gz` clips from an unrelated rig: bone indices and bind pose are model-specific.

## 4. Convert and validate

From the framework repository root, run:

```powershell
& .\Tools\prepare-model-addon.ps1 `
  -Manifest '.\Addons\YourModel\customsosig-model.json' `
  -ModelGlb 'C:\path\to\your-model.glb'
node .\Tools\validate-model-addons.mjs
```

Install the finished addon as a separate folder under `BepInEx/plugins/`. Do not give two installed addons the same `id`. A successful conversion is not a substitute for checking animation, armor coverage, weapon grip, and hit effects in H3VR.

## Asset rights

You are responsible for obtaining permission to use and redistribute the model, textures, and animations in your addon. This framework and template do not grant rights to third-party assets or trademarks.
