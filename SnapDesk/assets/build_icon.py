"""Build the geometric SnapDesk icon; requires Pillow only when regenerating assets."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent


def draw_icon(size):
    scale = 4
    image = Image.new("RGBA", (256 * scale, 256 * scale))
    draw = ImageDraw.Draw(image)

    def rect(box, color, radius=0):
        box = tuple(int(v * scale) for v in box)
        if radius:
            draw.rounded_rectangle(box, radius * scale, fill=color)
        else:
            draw.rectangle(box, fill=color)

    rect((8, 8, 248, 248), "#087F82", 55)
    rect((16, 14, 240, 237), "#109D98", 49)
    white = "#FFFFFF"
    # Four viewfinder corners remain readable at taskbar sizes.
    for box in [(53, 53, 101, 69), (53, 53, 69, 101),
                (155, 53, 203, 69), (187, 53, 203, 101),
                (53, 155, 69, 203), (53, 187, 101, 203),
                (187, 155, 203, 203), (155, 187, 203, 203)]:
        rect(box, white, 6)
    rect((88, 93, 168, 163), "#08666F", 10)
    draw.polygon([(90 * scale, 150 * scale), (113 * scale, 121 * scale),
                  (134 * scale, 143 * scale), (148 * scale, 130 * scale),
                  (167 * scale, 151 * scale), (167 * scale, 161 * scale),
                  (90 * scale, 161 * scale)], fill="#97F4DA")
    draw.ellipse((141 * scale, 105 * scale, 155 * scale, 119 * scale), fill=white)
    return image.resize((size, size), Image.Resampling.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = {size: draw_icon(size) for size in sizes}
frames[256].save(ROOT / "SnapDesk.ico", sizes=[(s, s) for s in sizes],
                 append_images=[frames[s] for s in sizes if s != 256])
frames[128].save(ROOT / "icon-preview.png")
print("Created SnapDesk.ico and icon-preview.png")
