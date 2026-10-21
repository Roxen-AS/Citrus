"""Regenerate the original Citrus vector icon. Build-only dependencies, never needed at runtime.

python -m venv .tools/artwork
.tools/artwork/bin/pip install CairoSVG==2.8.2 Pillow==12.0.0
.tools/artwork/bin/python packaging/generate-artwork.py
"""
from pathlib import Path
import io
import cairosvg
from PIL import Image

resources = Path(__file__).resolve().parents[1] / "src/Citrus.Windows/Resources"
png = cairosvg.svg2png(url=str(resources / "Citrus.svg"), output_width=256, output_height=256)
Image.open(io.BytesIO(png)).save(resources / "Citrus.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
