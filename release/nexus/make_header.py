# Builds the Nexus Mods main image (1920x1080) from real xr-sim captures of the mod (left eye images).
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

SHOTS = r"F:\Claude\CasualtyVR\Working\xrsim-shots"
OUT = r"F:\Claude\CasualtyVR\release\nexus"
W, H = 1920, 1080
HALF = W // 2

def fit(img, box_w, box_h, cx, cy, crop_w):
    """Crop a box_w:box_h window of width crop_w centred on (cx, cy), resized to box_w x box_h."""
    crop_h = round(crop_w * box_h / box_w)
    left = max(0, min(img.width - crop_w, cx - crop_w // 2))
    top = max(0, min(img.height - crop_h, cy - crop_h // 2))
    return img.crop((left, top, left + crop_w, top + crop_h)).resize((box_w, box_h), Image.LANCZOS)

def lift(img, gamma, bright, contrast=1.0, sat=1.0):
    img = img.point(lambda v: round(255 * (v / 255) ** gamma))
    img = ImageEnhance.Brightness(img).enhance(bright)
    img = ImageEnhance.Contrast(img).enhance(contrast)
    return ImageEnhance.Color(img).enhance(sat)

dio = Image.open(f"{SHOTS}\\nxdio_angle_left.png").convert("RGB")
fp = Image.open(f"{SHOTS}\\nxfp_other_left.png").convert("RGB")

# Left: the 3D diorama (lifepod and the blocks around it), zoomed in. Right: first person in the lifepod.
left = lift(fit(dio, HALF, H, 560, 505, 470), 0.62, 1.25, 1.1, 1.15)
right = lift(fit(fp, HALF, H, 470, 552, 960), 0.85, 1.12, 1.05, 1.1)

canvas = Image.new("RGB", (W, H), (6, 8, 14))
canvas.paste(left, (0, 0))
canvas.paste(right, (HALF, 0))

draw = ImageDraw.Draw(canvas, "RGBA")
# Divider
draw.rectangle((HALF - 3, 0, HALF + 2, 740), fill=(120, 200, 255, 230))

# Bottom title band: transparent at the top, dark below
band_top = 690
for y in range(band_top, H):
    a = min(235, int((y - band_top) / (H - band_top) * 420))
    draw.line((0, y, W, y), fill=(4, 6, 12, a))

bold = r"C:\Windows\Fonts\bahnschrift.ttf"
title_font = ImageFont.truetype(bold, 150)
title_font.set_variation_by_name("Bold")
sub_font = ImageFont.truetype(bold, 46)
sub_font.set_variation_by_name("SemiBold")
tag_font = ImageFont.truetype(bold, 38)
tag_font.set_variation_by_name("SemiBold")

def text_center(y, text, font, fill, shadow=True):
    w = draw.textlength(text, font=font)
    x = (W - w) / 2
    if shadow:
        for dx, dy in ((4, 4), (2, 2)):
            draw.text((x + dx, y + dy), text, font=font, fill=(0, 0, 0, 200))
    draw.text((x, y), text, font=font, fill=fill)

text_center(800, "CasualtyVR", title_font, (235, 245, 255, 255))
text_center(968, "VR for Casualties: Unknown  \u2022  3D diorama  \u2022  first-person mode  \u2022  motion controls", sub_font, (130, 205, 255, 255))

def tag(x, y, text):
    w = draw.textlength(text, font=tag_font)
    draw.rounded_rectangle((x, y, x + w + 40, y + 64), radius=14, fill=(6, 10, 20, 200), outline=(120, 200, 255, 220), width=3)
    draw.text((x + 20, y + 10), text, font=tag_font, fill=(225, 240, 255, 255))

tag(40, 40, "3D diorama")
tag(HALF + 40, 40, "First person")

canvas.save(f"{OUT}\\casualtyvr_nexus_main.png", optimize=True)
canvas.resize((640, 360), Image.LANCZOS).save(f"{OUT}\\casualtyvr_nexus_main_preview_640.png")
print("ok")
