"""Export the prepared MW4 example GLB as an FBX Unity can import natively.

Run with Blender: blender -b --python Tools/export-mw4-unity-fbx.py -- INPUT.glb OUTPUT.fbx
The GLB remains the source of truth for the framework's runtime converter.
"""

import sys
from pathlib import Path

import bpy


args = sys.argv[sys.argv.index("--") + 1:]
if len(args) != 2:
    raise SystemExit("Expected INPUT.glb OUTPUT.fbx")
source, destination = (Path(value).resolve() for value in args)
if not source.is_file():
    raise SystemExit(f"Missing model: {source}")
destination.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(source))

armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and len(obj.data.vertices) > 0]
skinned = [obj for obj in meshes if any(mod.type == "ARMATURE" for mod in obj.modifiers)]
print("IMPORTED_MESHES", [(obj.name, len(obj.data.vertices), bool(obj in skinned)) for obj in meshes])
if len(armatures) != 1 or len(skinned) != 1:
    raise RuntimeError(f"Expected one armature and skinned mesh, got {len(armatures)} and {[(obj.name, len(obj.data.vertices)) for obj in meshes]}")
armature, mesh = armatures[0], skinned[0]
if not any(mod.type == "ARMATURE" for mod in mesh.modifiers):
    raise RuntimeError("The imported mesh has no armature modifier")

bpy.ops.object.select_all(action="DESELECT")
armature.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = armature
bpy.ops.export_scene.fbx(
    filepath=str(destination),
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True,
    bake_space_transform=False,
    add_leaf_bones=False,
    bake_anim=False,
    use_mesh_modifiers=True,
    path_mode="AUTO",
)
print(f"UNITY_FBX {destination} bones={len(armature.data.bones)} vertices={len(mesh.data.vertices)}")
