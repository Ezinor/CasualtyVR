# Builds the Nexus Mods banner (1920x480) from real xr-sim captures of the mod (left eye images, 1032x1104).
from PIL import Image, ImageDraw, ImageEnhance, ImageFont

SHOTS = r"F:\Claude\CasualtyVR\Working\xrsim-shots"
OUT = r"F:\Claude\CasualtyVR\release\nexus"
W, H = 1920, 480

def fit(img, box_w, box_h, cx, cy, crop_w):
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
fp2 = Image.open(f"{SHOTS}\\nxfp_hands_left.png").convert("RGB")

# Title area on the left (dark, with a soft blue glow), then the diorama, then first person.
title_w, panel_w = 720, 600
canvas = Image.new("RGB", (W, H), (6, 8, 14))
canvas.paste(lift(fit(dio, panel_w, H, 560, 455, 470), 0.62, 1.25, 1.1, 1.15), (title_w, 0))
canvas.paste(lift(fit(fp, panel_w, H, 500, 400, 960), 0.85, 1.12, 1.05, 1.1), (title_w + panel_w, 0))
draw = ImageDraw.Draw(canvas, "RGBA")
for r in range(420, 0, -6):   # glow behind the title
    a = int(28 * (1 - r / 420))
    draw.ellipse((260 - r, 240 - r * 0.8, 260 + r, 240 + r * 0.8), fill=(40, 90, 160, a))
# Blend the title area into the diorama, and a divider between the two views.
for x in range(title_w - 40, title_w + 160):
    a = int(255 * max(0.0, 1 - (x - (title_w - 40)) / 200) ** 1.5)
    draw.line((x, 0, x, H), fill=(6, 8, 14, a))
draw.rectangle((title_w + panel_w - 2, 0, title_w + panel_w + 1, H), fill=(120, 200, 255, 200))
# Thin accent line at the bottom
draw.rectangle((0, H - 6, W, H), fill=(120, 200, 255, 255))

font = r"C:\Windows\Fonts\bahnschrift.ttf"
title = ImageFont.truetype(font, 140); title.set_variation_by_name("Bold")
sub = ImageFont.truetype(font, 44); sub.set_variation_by_name("SemiBold")
small = ImageFont.truetype(font, 32); small.set_variation_by_name("SemiLight")

def shadowed(xy, text, f, fill):
    x, y = xy
    for dx, dy in ((4, 4), (2, 2)):
        draw.text((x + dx, y + dy), text, font=f, fill=(0, 0, 0, 200))
    draw.text((x, y), text, font=f, fill=fill)

shadowed((64, 100), "CasualtyVR", title, (235, 245, 255, 255))
shadowed((76, 268), "VR for Casualties: Unknown", sub, (130, 205, 255, 255))
shadowed((78, 336), "3D diorama  \u2022  first-person mode  \u2022  motion controls", small, (215, 230, 245, 255))

canvas.save(f"{OUT}\\casualtyvr_nexus_banner.png", optimize=True)
print("ok")
