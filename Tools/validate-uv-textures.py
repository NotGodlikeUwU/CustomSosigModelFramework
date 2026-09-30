"""Verify the exact GLB/external image relationship and runtime UV convention."""
import gzip
import io
import json
import struct
from pathlib import Path
from PIL import Image, ImageChops

root = Path(__file__).resolve().parents[1]
glb = (root / 'metrocop/source/remade metro cop HL2.glb').read_bytes()
json_length = struct.unpack_from('<I', glb, 12)[0]
document = json.loads(glb[20:20 + json_length])
binary_start = 28 + json_length
for index, name in [(0, 'body_0.png'), (1, 'body_n_1.png'), (3, 'mask_3.png'), (4, 'mask_n_4.png')]:
    view = document['bufferViews'][document['images'][index]['bufferView']]
    start = binary_start + view.get('byteOffset', 0)
    embedded = Image.open(io.BytesIO(glb[start:start + view['byteLength']])).convert('RGB')
    external = Image.open(root / 'metrocop/textures' / name).convert('RGB')
    assert ImageChops.difference(embedded, external.transpose(Image.Transpose.FLIP_TOP_BOTTOM)).getbbox() is None
    print(f'{name}: embedded image equals external image vertically inverted')

runtime = gzip.decompress((root / 'Assets/metrocop/metrocop.cskmesh.gz').read_bytes())
offset = 16
bone_count = struct.unpack_from('<i', runtime, offset)[0]; offset += 4
for _ in range(bone_count):
    length = struct.unpack_from('<i', runtime, offset)[0]
    offset += 4 + length + 4 + 40
offset += bone_count * 64
primitive_count = struct.unpack_from('<i', runtime, offset)[0]; offset += 4
total = 0
for primitive in document['meshes'][0]['primitives']:
    material, count = struct.unpack_from('<ii', runtime, offset); offset += 8
    assert material == primitive.get('material', 0)
    accessor = document['accessors'][primitive['attributes']['TEXCOORD_0']]
    view = document['bufferViews'][accessor['bufferView']]
    start = binary_start + view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    assert accessor['componentType'] == 5126 and accessor['count'] == count
    uv_pairs = []
    for vertex in range(count):
        source_uv = struct.unpack_from('<ff', glb, start + vertex * view.get('byteStride', 8))
        runtime_uv = struct.unpack_from('<ff', runtime, offset + 24)
        assert source_uv == runtime_uv, (vertex, source_uv, runtime_uv)
        uv_pairs.append(runtime_uv)
        offset += 52
    index_count = struct.unpack_from('<i', runtime, offset)[0]; offset += 4
    indices = struct.unpack_from('<' + 'H' * index_count, runtime, offset); offset += index_count * 2
    if material == 1:
        image = Image.open(root / 'Assets/metrocop/mask.png').convert('RGB')
        eye_counts = [0, 0]
        for triangle in range(0, index_count, 3):
            u, v = [sum(uv_pairs[indices[triangle + corner]][axis] for corner in range(3)) / 3 for axis in range(2)]
            u %= 1; v %= 1
            side = 0 if abs(u - 0.205) < abs(u - 0.798) else 1
            dx = (u - [0.205, 0.798][side]) / 0.042
            dy = (v - 0.623) / 0.035
            if dx * dx + dy * dy < 1:
                pixel = image.getpixel((min(image.width - 1, int(u * image.width)), min(image.height - 1, int((1 - v) * image.height))))
                if max(pixel) < 0.42 * 255:
                    eye_counts[side] += 1
        assert min(eye_counts) > 0, eye_counts
        print(f'Unlit eye surface selection: left/right lens triangles {eye_counts}')
    total += count
assert offset == len(runtime)
print(f'Verified {total} exported UV pairs: no extra vertical flip')

albedo = Image.open(root / 'Assets/metrocop/mask.png').convert('RGB')
for u, v in [(0.205, 0.622), (0.798, 0.624)]:
    color = albedo.getpixel((int(u * albedo.width), int((1 - v) * albedo.height)))
    assert max(color) < 0.32 * 255, color
    print(f'Eye lens UV ({u}, {v}): dark lens pixel {color}, emission enabled only inside lens region')
