"""Prepare the supplied MW4 Milsim blend as a schema-1 model addon.

Run with Blender in background mode and the .blend opened. This only writes
generated files under Addons/MW4Milsim; it never saves the input .blend.
"""
import bpy
import json
import math
import struct
import sys
from collections import defaultdict
from pathlib import Path
from mathutils import Matrix
import numpy as np


project = Path(bpy.data.filepath).parent
addon = project / "Addons" / "MW4Milsim"
assets = addon / "assets"
assets.mkdir(parents=True, exist_ok=True)
armatures = [obj for obj in bpy.data.objects if obj.type == 'ARMATURE']
meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH']
if len(armatures) != 1 or not meshes:
    raise RuntimeError("Expected one armature and at least one mesh")
armature = armatures[0]
if any(not any(m.type == 'ARMATURE' and m.object == armature for m in obj.modifiers) for obj in meshes):
    raise RuntimeError("Every source mesh must be skinned to the same armature")

aliases = {
    'j_mainroot': 'Pelvis', 'j_spinelower': 'Spine', 'j_spineupper': 'Spine1',
    'j_spine4': 'Spine2', 'j_neck': 'Neck1', 'j_head': 'Head1',
    'j_clavicle_le': 'L_Clavicle', 'j_shoulder_le': 'L_UpperArm',
    'j_elbow_le': 'L_Forearm', 'j_wrist_le': 'L_Hand',
    'j_clavicle_ri': 'R_Clavicle', 'j_shoulder_ri': 'R_UpperArm',
    'j_elbow_ri': 'R_Forearm', 'j_wrist_ri': 'R_Hand',
    'j_hip_le': 'L_Thigh', 'j_knee_le': 'L_Calf', 'j_ankle_le': 'L_Foot',
    'j_ball_le': 'L_Toe0', 'j_hip_ri': 'R_Thigh', 'j_knee_ri': 'R_Calf',
    'j_ankle_ri': 'R_Foot', 'j_ball_ri': 'R_Toe0',
}
for source_side, target_side in [('le', 'L'), ('ri', 'R')]:
    for source_finger, target_finger in [('thumb', 0), ('index', 1), ('mid', 2), ('ring', 3), ('pinky', 4)]:
        for segment in (1, 2, 3):
            name = f'j_{source_finger}_{source_side}_{segment}'
            if name in armature.data.bones:
                suffix = '' if segment == 1 else str(segment - 1)
                aliases[name] = f'{target_side}_Finger{target_finger}{suffix}'
for name in aliases:
    if name not in armature.data.bones:
        raise RuntimeError(f"Required animation bone missing: {name}")

# glTF/Unity legacy skin format is limited to 255 indices. Preserve every
# humanoid control bone, its ancestors, and the most influential detail bones.
weighted = defaultdict(float)
for obj in meshes:
    groups = {group.index: group.name for group in obj.vertex_groups}
    for vertex in obj.data.vertices:
        for group in vertex.groups:
            if group.weight > 0 and group.group in groups:
                weighted[groups[group.group]] += group.weight
parent = {bone.name: bone.parent.name if bone.parent else None for bone in armature.data.bones}
protected = set(aliases)
for name in list(protected):
    current = parent[name]
    while current:
        protected.add(current)
        current = parent[current]
ordered = sorted((name for name in weighted if name in parent), key=lambda name: weighted[name], reverse=True)
keep = set(protected)
for name in ordered:
    chain = []
    current = name
    while current and current not in keep:
        chain.append(current)
        current = parent[current]
    if len(keep) + len(chain) <= 250:
        keep.update(chain)
if len(keep) > 250:
    raise RuntimeError(f"Protected skeleton is too large: {len(keep)} bones")

def retained_ancestor(name):
    current = parent.get(name)
    while current and current not in keep:
        current = parent.get(current)
    if not current:
        raise RuntimeError(f"No retained ancestor for weighted bone {name}")
    return current

for obj in meshes:
    group_names = {group.index: group.name for group in obj.vertex_groups}
    destination_groups = {}
    for vertex in obj.data.vertices:
        for group in list(vertex.groups):
            source = group_names.get(group.group)
            if source in keep or not source or group.weight <= 0:
                continue
            destination = retained_ancestor(source)
            target = destination_groups.get(destination)
            if target is None:
                target = obj.vertex_groups.get(destination) or obj.vertex_groups.new(name=destination)
                destination_groups[destination] = target
            target.add([vertex.index], group.weight, 'ADD')
    for group in list(obj.vertex_groups):
        if group.name not in keep:
            obj.vertex_groups.remove(group)

bpy.context.view_layer.objects.active = armature
bpy.ops.object.mode_set(mode='EDIT')
edit_bones = armature.data.edit_bones
for bone in list(edit_bones):
    if bone.name in keep:
        wanted_parent = retained_ancestor(bone.name) if parent[bone.name] and parent[bone.name] not in keep else parent[bone.name]
        if wanted_parent and bone.parent and bone.parent.name != wanted_parent:
            bone.parent = edit_bones[wanted_parent]
for bone in list(edit_bones):
    if bone.name not in keep:
        edit_bones.remove(bone)
bpy.ops.object.mode_set(mode='OBJECT')
print("KEPT_BONES", len(armature.data.bones), "DROPPED_BONES", len(parent) - len(keep))

# The COD source uses centimeters and faces +X. The framework expects meters
# and model-local -Z after Blender glTF Y-up and the runtime handedness mirror.
model_transform = Matrix.Rotation(-math.pi / 2, 4, 'Z') @ Matrix.Scale(0.01, 4)
for obj in meshes:
    obj.data.transform(model_transform)
    obj.data.update()
bpy.context.view_layer.objects.active = armature
bpy.ops.object.mode_set(mode='EDIT')
for bone in armature.data.edit_bones:
    bone.transform(model_transform, scale=True)
bpy.ops.object.mode_set(mode='OBJECT')

material_sources = {}
for obj in meshes:
    for slot in obj.material_slots:
        material = slot.material
        if not material or material.name in material_sources:
            continue
        images = {node.name: node.image for node in material.node_tree.nodes
                  if node.type == 'TEX_IMAGE' and node.image} if material.node_tree else {}
        material_sources[material.name] = (images.get('BASE_COLOR'), images.get('MAIN_NORMAL'))

source_names = list(material_sources)
tile_size = 1024
padding = 4
tiles_per_side = 4
atlas_size = tile_size * tiles_per_side
# The first source parts are much larger than the accessories. Keeping at most
# twelve per atlas also keeps each resulting GLB primitive below 65k vertices.
materials_per_atlas = 12
atlas_count = math.ceil(len(source_names) / materials_per_atlas)
atlas_materials = []
for atlas_index in range(atlas_count):
    material = bpy.data.materials.new(f'MW4 Milsim Atlas {atlas_index:02d}')
    material.use_nodes = True
    atlas_materials.append(material)

def pixels_for_tile(image, kind):
    if image is None or image.size[0] < 1 or image.size[1] < 1:
        color = (0.35, 0.36, 0.35, 1) if kind == 'base' else (0.5, 0.5, 1, 1)
        return np.broadcast_to(np.asarray(color, dtype=np.float32),
                               (tile_size - 2 * padding, tile_size - 2 * padding, 4))
    copy = image.copy()
    size = tile_size - 2 * padding
    copy.scale(size, size)
    pixels = np.empty(size * size * 4, dtype=np.float32)
    copy.pixels.foreach_get(pixels)
    bpy.data.images.remove(copy)
    return pixels.reshape((size, size, 4))

atlas_files = {}
for atlas_index in range(atlas_count):
    for kind, channel in [('base', 0), ('normal', 1)]:
        atlas_pixels = np.zeros((atlas_size, atlas_size, 4), dtype=np.float32)
        atlas_pixels[:, :, 3] = 1
        for slot in range(materials_per_atlas):
            index = atlas_index * materials_per_atlas + slot
            if index >= len(source_names):
                break
            source = material_sources[source_names[index]][channel]
            tile = np.pad(pixels_for_tile(source, kind), ((padding, padding), (padding, padding), (0, 0)), mode='edge')
            row, col = divmod(slot, tiles_per_side)
            atlas_pixels[row * tile_size:(row + 1) * tile_size,
                         col * tile_size:(col + 1) * tile_size, :] = tile
        image = bpy.data.images.new(f'MW4 Milsim {kind} Atlas {atlas_index:02d}',
                                    width=atlas_size, height=atlas_size, alpha=True)
        image.pixels.foreach_set(atlas_pixels.reshape(-1))
        image.file_format = 'PNG'
        path = assets / f'atlas_{atlas_index:02d}_{kind}.png'
        image.filepath_raw = str(path)
        image.save()
        bpy.data.images.remove(image)
        atlas_files[(atlas_index, kind)] = 'assets/' + path.name
        print("ATLAS", path, flush=True)

material_to_atlas = {name: divmod(index, materials_per_atlas) for index, name in enumerate(source_names)}
for obj in meshes:
    for slot_index, slot in enumerate(obj.material_slots):
        if not slot.material:
            continue
        atlas_index, tile_index = material_to_atlas[slot.material.name]
        obj.data.materials[slot_index] = atlas_materials[atlas_index]
        row, col = divmod(tile_index, tiles_per_side)
        for polygon in obj.data.polygons:
            if polygon.material_index != slot_index:
                continue
            for loop_index in polygon.loop_indices:
                uv = obj.data.uv_layers.active.data[loop_index].uv
                uv.x = (col * tile_size + padding + (uv.x % 1) * (tile_size - 2 * padding)) / atlas_size
                uv.y = (row * tile_size + padding + (uv.y % 1) * (tile_size - 2 * padding)) / atlas_size

bpy.ops.object.select_all(action='DESELECT')
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
joined = bpy.context.view_layer.objects.active
joined.name = 'MW4 Milsim'
joined.data.name = 'MW4 Milsim Skinned Mesh'
print("JOINED_VERTICES", len(joined.data.vertices), "MATERIAL_SLOTS", len(joined.material_slots))

bpy.ops.object.select_all(action='DESELECT')
joined.select_set(True)
armature.select_set(True)
bpy.context.view_layer.objects.active = joined
glb = addon / 'milsim.glb'
bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True,
                          export_yup=True, export_animations=False, export_materials='EXPORT',
                          export_apply=False, export_cameras=False, export_lights=False)
with glb.open('rb') as stream:
    header = stream.read(20)
    if header[:4] != b'glTF' or header[16:20] != b'JSON':
        raise RuntimeError('Blender did not produce a GLB 2.0 file')
    gltf = json.loads(stream.read(struct.unpack_from('<I', header, 12)[0]))
if len(gltf.get('skins', [])) != 1 or len(gltf.get('meshes', [])) != 1:
    raise RuntimeError(f"Expected one skin and one mesh: {len(gltf.get('skins', []))}, {len(gltf.get('meshes', []))}")

template = json.loads((project / 'Addons' / 'Metrocop' / 'customsosig-model.json').read_text(encoding='utf-8'))
template.update(id='NotGodlike.mw4milsim', displayName='Call of Duty MW4 Milsim', menuName='MW4 Milsim',
                mesh='assets/milsim.cskmesh.gz', heightMeters=1.885,
                textureConvention='gltf-images', impactEffect='meat',
                surface={'smoothness': 0.28, 'metallic': 0.05},
                materials=[], boneAliases=[{'source': source, 'role': target} for source, target in aliases.items()],
                eyes={'enabled': False})
for material in gltf['materials']:
    name = material['name']
    if not name.startswith('MW4 Milsim Atlas '):
        raise RuntimeError(f"Unexpected GLB material: {name}")
    index = int(name.rsplit(' ', 1)[-1])
    template['materials'].append({'name': name,
                                  'albedo': atlas_files[(index, 'base')],
                                  'normal': atlas_files[(index, 'normal')]})
(addon / 'customsosig-model.json').write_text(json.dumps(template, indent=2) + '\n', encoding='utf-8')
print("EXPORTED", glb, "BONES", len(gltf['skins'][0]['joints']),
      "MATERIALS", len(template['materials']), "ATLASES", atlas_count)
