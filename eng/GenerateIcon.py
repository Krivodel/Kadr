from pathlib import Path
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw, ImageFilter


def generate():
    assets = Path(__file__).resolve().parent.parent / "src/Kadr.Desktop/Assets"
    root = ET.parse(assets / "AppIcon.svg").getroot()
    namespace = {"svg": "http://www.w3.org/2000/svg"}
    group = root.find("svg:g", namespace)
    shadow = root.find("svg:defs/svg:filter/svg:feDropShadow", namespace)
    assert group is not None and shadow is not None
    scale = 4
    size = int(root.attrib["width"])
    mask = Image.new("L", (size * scale, size * scale))
    drawing = ImageDraw.Draw(mask)
    for polygon in group.findall("svg:polygon", namespace):
        points = [tuple(float(value) * scale for value in pair.split(","))
                  for pair in polygon.attrib["points"].split()]
        drawing.polygon(points, fill=255)
    shadow_mask = Image.new("L", mask.size)
    shadow_mask.paste(mask, (int(float(shadow.attrib["dx"]) * scale), int(float(shadow.attrib["dy"]) * scale)))
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(float(shadow.attrib["stdDeviation"]) * scale))
    shadow_mask = shadow_mask.point(lambda value: round(value * float(shadow.attrib["flood-opacity"])))
    background = Image.new("RGBA", mask.size, shadow.attrib["flood-color"])
    background.putalpha(shadow_mask)
    foreground = Image.new("RGBA", mask.size, group.attrib["fill"])
    foreground.putalpha(mask)
    result = Image.alpha_composite(background, foreground)
    result.save(assets / "AppIcon.ico", sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    result.resize((512, 512), Image.Resampling.LANCZOS).save(assets / "AppIcon.png")


if __name__ == "__main__":
    generate()
