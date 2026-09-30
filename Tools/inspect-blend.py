import bpy
from collections import Counter

meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH']
armatures = [obj for obj in bpy.data.objects if obj.type == 'ARMATURE']
materials = Counter(slot.material.name for obj in meshes for slot in obj.material_slots if slot.material)
print("BLEND_FILE", bpy.data.filepath)
print("COUNTS", "mesh_objects", len(meshes), "vertices", sum(len(obj.data.vertices) for obj in meshes),
      "armatures", len(armatures), "materials", len(materials), "packed_images", sum(bool(i.packed_file) for i in bpy.data.images))
print("ARMATURES", [(obj.name, len(obj.data.bones)) for obj in armatures])
for obj in armatures:
    print("BONES", obj.name, [bone.name for bone in obj.data.bones])
print("MESH_PARENTS", Counter(obj.parent.name if obj.parent else None for obj in meshes))
print("LARGEST_MESHES", [(obj.name, len(obj.data.vertices), [s.material.name if s.material else None for s in obj.material_slots])
                         for obj in sorted(meshes, key=lambda x: len(x.data.vertices), reverse=True)[:12]])
print("MATERIALS", materials)
print("ACTIONS", [(a.name, a.frame_range[:]) for a in bpy.data.actions])
if armatures:
    skeleton = armatures[0].data.bones
    weighted = set()
    for obj in meshes:
        groups = {group.index: group.name for group in obj.vertex_groups}
        for vertex in obj.data.vertices:
            weighted.update(groups[g.group] for g in vertex.groups if g.weight > 0.0001 and g.group in groups)
    required = set(weighted)
    for name in list(weighted):
        if name not in skeleton:
            continue
        bone = skeleton[name]
        while bone:
            required.add(bone.name)
            bone = bone.parent
    print("WEIGHTED_BONES", len(weighted), "WITH_ANCESTORS", len(required))
    print("WEIGHTED_NONHUMANOID", [name for name in sorted(weighted) if not name.startswith(('j_', 'tag_'))][:50])
    print("ROOT_BONE", [bone.name for bone in skeleton if not bone.parent])
print("SCENE_UNITS", bpy.context.scene.unit_settings.system, bpy.context.scene.unit_settings.scale_length)
print("ARMATURE_TRANSFORM", [(o.name, tuple(o.location), tuple(o.rotation_euler), tuple(o.scale)) for o in armatures])
print("MESH_TRANSFORM", [(o.name, tuple(o.location), tuple(o.rotation_euler), tuple(o.scale)) for o in meshes[:3]])
corners = [o.matrix_world @ __import__('mathutils').Vector(corner) for o in meshes for corner in o.bound_box]
print("WORLD_BOUNDS", [(min(c[i] for c in corners), max(c[i] for c in corners)) for i in range(3)])
if armatures:
    print("FACE_BONES", [(name, tuple(round(x, 3) for x in armatures[0].data.bones[name].head_local))
                         for name in ['j_head', 'j_eyeball_le', 'j_eyeball_ri', 'j_nose_tip', 'j_spine4'] if name in armatures[0].data.bones])
for material in list(materials)[:10]:
    mat = bpy.data.materials[material]
    print("MATERIAL_SAMPLE", material, [(n.name, n.image.name if n.image else None) for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE'] if mat.node_tree else None)
