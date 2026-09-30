# Unity Model Addon Authoring Template

This is an asset-free Unity project scaffold for inspecting a custom Sosig model and editing its addon manifest. It is **not** a game-ready H3VR mod, and it intentionally does not include MW4/Call of Duty character files, Mixamo or Low Poly Shooter Pack clips, textures, or other third-party assets.

## Open the project

Open this directory with Unity **2022.3.22f1** using the Built-in Render Pipeline. Unity will generate its local `Library/` folder. Add your own licensed model and animation assets under `Assets/Example/`, then create a temporary scene to inspect the rig and clips. H3VR does not load Unity scenes, prefabs, or AssetBundles through this framework; the runtime uses the converted addon files described in the framework documentation.

## Adjust armor hitboxes

After you have prepared a model addon manifest, open **Custom Sosigs > Armor Hitbox Scale** in Unity. Choose **Open addon manifest...**, select your `customsosig-model.json`, adjust the **Armor hitbox scale**, then click **Save scale to manifest**.

- `1.0` keeps the automatically measured size.
- `0.667` scales each collider dimension to about two thirds (roughly 1.5 times smaller).
- Values from `0.25` to `1.5` are supported by the runtime.

The setting changes generated armor-zone dimensions and their blue `Armor Hitbox` overlay around each segment center. It does not resize native Sosig armor, ragdoll/body colliders, or move the hitbox centers. Adding a Unity `BoxCollider` does not affect the H3VR addon. Verify armor coverage and damage in-game after each change; overly small zones can sit inside the visible character mesh.

## Build a runtime addon

Use the generic [Model Addon Template](../ModelAddon/README.md) and [model addon guide](../../Docs/ModelAddons.md) for manifest fields, bone aliases, the 35 animation roles, and conversion commands. The `ArmorHitboxScaleWindow.cs` editor helper edits the manifest only; it does not export or install the model. Build and test the generated package with H3VR closed before distributing it.

## Asset rights

Only add assets that you created or have permission to use and redistribute. This scaffold and the framework do not grant rights to third-party models, animations, textures, game content, or trademarks.
