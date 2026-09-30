from pathlib import Path
from PIL import Image

PROJECT = Path(__file__).resolve().parents[1]
SOURCE = PROJECT / "metrocop" / "textures"
OUTPUT = PROJECT / "Assets" / "metrocop"
OUTPUT.mkdir(parents=True, exist_ok=True)

TEXTURES = {
    "body_0.png": ("body.png", 2048),
    "body_n_1.png": ("body_n.png", 2048),
    "mask_3.png": ("mask.png", 2048),
    "mask_n_4.png": ("mask_n.png", 2048),
}

for source_name, (output_name, maximum_size) in TEXTURES.items():
    source_path = SOURCE / source_name
    output_path = OUTPUT / output_name
    with Image.open(source_path) as image:
        image = image.convert("RGB")
        if max(image.size) > maximum_size:
            image.thumbnail((maximum_size, maximum_size), Image.Resampling.LANCZOS)
        image.save(output_path, format="PNG", optimize=True)
        print(f"{source_name}: {image.size[0]}x{image.size[1]} -> {output_path}")
