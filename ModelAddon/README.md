# CustomSosigModelFramework Model Addon Template

This is a generic source template, **not a ready-to-install mod**. It intentionally does not include model, texture, or licensed animation files. The separate [Unity example project](../UnityModelAddon/README.md) now includes the already prepared **MW4 Milsim** model and the local Mixamo and weapon animation FBXs. It does **not** use `super hot.blend1`. Both templates live outside `Addons`, so the build script will not install an incomplete model. A finished addon is a separate folder under `BepInEx/plugins` containing `customsosig-model.json` and `assets/`; it does not need its own DLL.

## 1. Prepare a rigged model

Export the character from Blender as a binary **GLB 2.0** with UVs, skin weights, and one humanoid armature/skin. If clothing consists of multiple objects, combine them into one skinned mesh while retaining separate materials if needed. The exported GLB must contain exactly **one mesh and one skin**, at most **255 bones**, at most **16 material primitives**, and at most **65,000 vertices per primitive**. The leg and foot bones must influence the footwear geometry because the framework uses them to determine ground contact. Keep no more than four significant bone weights per vertex. Check the A/T-pose, forward direction, and size in meters in Blender before conversion. Copying animation files cannot animate an unrigged model.

The GLB bone names must match the `boneAliases` entries in `customsosig-model.json`. The example aliases target a typical Mixamo rig with names such as `mixamorigHips` and `mixamorigLeftArm`. If your exporter uses names such as `mixamorig:Hips`, change each `source` to the **exact** GLB bone name. Keep the `role` values unchanged. For a different rig, replace all relevant `source` values; the mandatory roles are listed in the [general addon documentation](../../Docs/ModelAddons.md). You may also add finger aliases such as `L_Finger0`, `L_Finger01`, and `L_Finger02`, with corresponding entries for the other fingers and the right hand.

## 2. Create the addon folder

From the project root:

```powershell
Copy-Item -LiteralPath '.\Templates\ModelAddon' -Destination '.\Addons\YourModel' -Recurse
New-Item -ItemType Directory -Force '.\Addons\YourModel\assets' | Out-Null
```

Edit `Addons/YourModel/customsosig-model.json`. Set a unique `id` (for example, `author.soldier`), the visible `displayName` and `menuName`, the actual `heightMeters`, the correct `boneAliases`, and **one `materials` entry for each GLB material in material-index order**. Put your own albedo and normal PNGs in `assets/` and reference them with relative paths. Remove the `normal` field if you do not have a normal map. The template uses `textureConvention: "gltf-images"`. If your external PNGs are vertically inverted relative to images embedded in the GLB, use `external-flipped` instead. Do not flip both the PNGs and the UVs.

`surface.smoothness` is the Unity Standard shader's smoothness value, not roughness: **lower smoothness means higher roughness and less gloss**. `metallic` also affects reflections. These values apply only to this addon. `impactEffect: "native"` keeps the usual hit effect. For custom blood, change it to `"meat"` and supply your own `bloodMask` and `bloodNoise` PNGs as described in the [general addon documentation](../../Docs/ModelAddons.md). Leave `eyes` disabled until you have measured the lens UV regions of your own model.

`armorHitboxScale` controls the size of the automatically generated armor colliders and their blue Wrist Menu overlay. The default `1.0` uses the measured size; `0.667` shrinks each dimension by about 1.5 times. Supported values are `0.25` through `1.5`. You can edit the value here or use **Custom Sosigs > Armor Hitbox Scale** in the [Unity example project](../UnityModelAddon/README.md#adjust-armor-hitbox-size-from-unity) to open and save this manifest through the Unity Editor. Unity scene/FBX `BoxCollider` components are not exported into the H3VR addon.

## 3. Retarget the animations

Edit `animation-sources.json`. The first 17 entries refer to the Mixamo FBX files included in this local project (walking, hit reaction, crouching, and falling). The remaining 18 refer to local Low Poly Shooter Pack animations for weapon poses, jogging, firing, and reloading. Their `bind` field points to the reference skeleton `SK_TP_CH_Default.fbx`, which is needed for correct static weapon poses. Example paths are relative to **the project root**, where you run the command. Replace them with your own absolute or relative paths if the files are elsewhere.

The FBX and GLB bone names do not have to match. The converter maps recognized Mixamo and Low Poly Shooter Pack bones to canonical roles, then applies the target model's `boneAliases`. However, the target rig still needs every mandatory joint, a reasonable A/T-pose, and correctly oriented local bone axes. The converter cannot create missing arms or legs or repair broken skinning. All **35 roles** need an animated FBX source. Do not copy Metrocop's finished `.csanim.gz` files: their bone indices and bind pose belong to a different skeleton.

```powershell
& .\Tools\prepare-model-addon.ps1 `
  -Manifest '.\Addons\YourModel\customsosig-model.json' `
  -ModelGlb 'H:\path\to\your-model.glb' `
  -AnimationSources '.\Addons\YourModel\animation-sources.json'
```

The command creates `assets/model.cskmesh.gz` and 35 `.csanim.gz` files. The original FBX and GLB files are not included in the in-game package. If the mesh exceeds the format limits, reduce its geometry or bone count, or combine materials into atlases **in the source model**, then export again. `Tools/export-milsim-blend.py` is an example of rig and atlas preparation for a complex character, but its specific bone and material names must be adapted for other models.

## 4. Validate and install

Run `Tools/build-plugin.ps1` and `Tools/install-plugin.ps1` with H3VR closed. The preparation script above runs the mesh converter; you do not need to call `export-skinned-glb.mjs` separately. The build script packages only direct subfolders of `Addons` that contain `customsosig-model.json`. In the BepInEx log, check for `Discovered model addon: author.soldier` followed by `Loaded ... rig`. Select the addon by name in Wrist Menu > Custom Sosigs, then test walking, crouching, weapon handling, hits, armor, falling, and death in game. Existing files and a successful build do not prove that the animations look correct in a headset.

Publish the addon only if you have the rights to distribute its model, textures, and animations. This template does not grant rights to Call of Duty, Mixamo, or Low Poly Shooter Pack assets.
