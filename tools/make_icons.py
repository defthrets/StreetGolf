#!/usr/bin/env python
"""
Draws the Street Golf HUD icons.

    python tools/make_icons.py            writes StreetGolf/icons/*.png
    python tools/make_icons.py --sheet    also writes build/icon_sheet.png to look at

Every icon is a plain white silhouette on transparency with a soft dark rim,
256 by 256, drawn at four times that and scaled down so the edges are smooth.
The script tints them at draw time, so there is no colour in the files.

Needs Pillow:  pip install pillow
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

S = 1024            # working size; the files are S / 4
OUT = 256
RIM = 25            # dilation of the dark rim, in working pixels (odd)
RIM_ALPHA = 120

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEST = os.path.join(ROOT, "StreetGolf", "icons")


# ---------------------------------------------------------------- helpers
def chaikin(pts, n=4, closed=True):
    """Corner cutting: turns a coarse polygon into a smooth one."""
    for _ in range(n):
        out = []
        m = len(pts)
        rng = range(m) if closed else range(m - 1)
        for i in rng:
            a = pts[i]
            b = pts[(i + 1) % m]
            out.append((0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]))
            out.append((0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1]))
        if not closed:
            out = [pts[0]] + out + [pts[-1]]
        pts = out
    return pts


def star(cx, cy, r_out, r_in, points=5, rot=-90.0):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * 180.0 / points)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def thick_line(d, pts, w, fill=255):
    """A polyline with round joins and round caps."""
    d.line(pts, fill=fill, width=w, joint="curve")
    for p in (pts[0], pts[-1]):
        d.ellipse((p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2), fill=fill)


def new():
    img = Image.new("L", (S, S), 0)
    return img, ImageDraw.Draw(img)


def finish(mask, name):
    rim = mask.filter(ImageFilter.MaxFilter(RIM)).filter(ImageFilter.GaussianBlur(6))
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rim_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rim_layer.putalpha(rim.point(lambda v: v * RIM_ALPHA // 255))
    out.alpha_composite(rim_layer)
    white = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    white.putalpha(mask)
    out.alpha_composite(white)
    out = out.resize((OUT, OUT), Image.LANCZOS)
    out.save(os.path.join(DEST, name + ".png"))
    return out


# ---------------------------------------------------------------- icons
ICONS = {}


def icon(fn):
    ICONS[fn.__name__] = fn
    return fn


@icon
def ball(d):
    d.ellipse((182, 182, 842, 842), fill=255)
    # dimples, on a hex grid, only where they sit well inside the ball
    r = 34
    for row in range(-3, 4):
        for col in range(-3, 4):
            x = 512 + col * 120 + (60 if row % 2 else 0)
            y = 512 + row * 104
            if math.hypot(x - 512, y - 512) < 265:
                d.ellipse((x - r, y - r, x + r, y + r), fill=0)


def shaft(d):
    thick_line(d, [(760, 120), (520, 690)], 46)


@icon
def driver(d):
    shaft(d)
    head = chaikin([(430, 660), (560, 690), (640, 760), (650, 850), (580, 910),
                    (400, 920), (260, 880), (220, 800), (280, 700), (380, 660)], 4)
    d.polygon(head, fill=255)


@icon
def iron(d):
    shaft(d)
    d.polygon([(480, 660), (560, 660), (620, 900), (250, 900), (250, 840), (450, 780)], fill=255)


@icon
def wedge(d):
    shaft(d)
    d.polygon([(470, 660), (570, 660), (680, 900), (230, 900), (230, 830), (430, 760)], fill=255)
    # grooves cut into the face
    for i in range(3):
        y = 790 + i * 34
        d.line([(340 - i * 20, y), (600 + i * 16, y)], fill=0, width=12)


@icon
def putter(d):
    thick_line(d, [(700, 120), (560, 740)], 46)
    d.rounded_rectangle((220, 760, 760, 870), radius=40, fill=255)
    d.rectangle((520, 700, 590, 800), fill=255)


@icon
def flame(d):
    outer = chaikin([(512, 90), (600, 250), (700, 380), (770, 560), (760, 720),
                     (660, 880), (512, 930), (360, 880), (260, 720), (250, 560),
                     (330, 400), (390, 480), (420, 380), (470, 250)], 4)
    d.polygon(outer, fill=255)
    inner = chaikin([(512, 560), (590, 660), (610, 780), (560, 860), (460, 860),
                     (410, 780), (430, 690), (480, 640)], 4)
    d.polygon(inner, fill=0)


@icon
def bomb(d):
    d.ellipse((160, 340, 780, 960), fill=255)
    d.rounded_rectangle((520, 280, 700, 400), radius=30, fill=255)
    d.arc((560, 180, 840, 460), start=190, end=320, fill=255, width=44)
    d.polygon(star(850, 190, 88, 34, 4, 0), fill=255)


@icon
def super(d):
    d.ellipse((560, 240, 900, 580), fill=255)
    for i, (y, ln) in enumerate(((330, 380), (410, 480), (490, 400))):
        x1 = 520 - i * 10
        d.rounded_rectangle((x1 - ln, y - 28, x1, y + 28), radius=28, fill=255)
    d.polygon(star(700, 690, 120, 46, 4, 0), fill=255)


@icon
def car(d):
    d.rounded_rectangle((120, 520, 904, 730), radius=70, fill=255)
    d.polygon([(290, 530), (400, 370), (660, 370), (790, 530)], fill=255)
    for cx in (300, 724):
        d.ellipse((cx - 100, 640, cx + 100, 840), fill=255)
        d.ellipse((cx - 40, 700, cx + 40, 780), fill=0)
    # window
    d.polygon([(410, 410), (630, 410), (700, 510), (330, 510)], fill=0)


@icon
def dent(d):
    car(d)
    d.polygon(star(230, 450, 150, 60, 7, 0), fill=0)
    d.polygon(star(230, 450, 86, 34, 7, 0), fill=255)


@icon
def ped(d):
    d.ellipse((422, 120, 602, 300), fill=255)
    d.rounded_rectangle((380, 320, 644, 640), radius=90, fill=255)
    thick_line(d, [(390, 360), (300, 620)], 70)
    thick_line(d, [(634, 360), (724, 620)], 70)
    thick_line(d, [(450, 620), (430, 900)], 84)
    thick_line(d, [(574, 620), (594, 900)], 84)


@icon
def trophy(d):
    d.polygon([(290, 170), (734, 170), (700, 520), (512, 610), (324, 520)], fill=255)
    d.arc((150, 200, 400, 470), start=90, end=270, fill=255, width=46)
    d.arc((624, 200, 874, 470), start=270, end=90, fill=255, width=46)
    d.rectangle((476, 600, 548, 740), fill=255)
    d.rounded_rectangle((340, 740, 684, 850), radius=30, fill=255)
    d.polygon(star(512, 370, 90, 36), fill=0)


@icon
def badge(d):
    d.ellipse((150, 150, 874, 874), fill=255)
    d.ellipse((215, 215, 809, 809), fill=0)
    d.polygon(star(512, 520, 250, 105), fill=255)


@icon
def eye(d):
    lens = chaikin([(120, 512), (300, 300), (512, 250), (724, 300), (904, 512),
                    (724, 724), (512, 774), (300, 724)], 4)
    d.polygon(lens, fill=255)
    d.ellipse((392, 392, 632, 632), fill=0)
    d.ellipse((448, 448, 576, 576), fill=255)


@icon
def baton(d):
    thick_line(d, [(280, 800), (780, 200)], 80)
    thick_line(d, [(470, 570), (560, 660)], 60)
    thick_line(d, [(300, 790), (250, 850)], 60)


@icon
def impact(d):
    d.polygon(star(512, 512, 400, 170, 8, 0), fill=255)


@icon
def crack(d):
    d.ellipse((392, 392, 632, 632), fill=255)
    arms = [
        [(512, 512), (640, 380), (700, 300), (760, 200)],
        [(512, 512), (660, 560), (780, 540), (900, 590)],
        [(512, 512), (560, 690), (520, 800), (600, 920)],
        [(512, 512), (360, 640), (330, 760), (220, 860)],
        [(512, 512), (350, 470), (240, 500), (130, 430)],
        [(512, 512), (430, 330), (450, 220), (380, 120)],
    ]
    for arm in arms:
        for i in range(len(arm) - 1):
            thick_line(d, [arm[i], arm[i + 1]], 54 - i * 12)


@icon
def trail(d):
    d.ellipse((600, 200, 900, 500), fill=255)
    for (x, y, r) in ((470, 520, 84), (330, 660, 62), (210, 790, 44)):
        d.ellipse((x - r, y - r, x + r, y + r), fill=255)


@icon
def aim(d):
    d.ellipse((190, 190, 834, 834), fill=255)
    d.ellipse((260, 260, 764, 764), fill=0)
    d.ellipse((452, 452, 572, 572), fill=255)
    for (a, b) in (((512, 90), (512, 300)), ((512, 724), (512, 934)),
                   ((90, 512), (300, 512)), ((724, 512), (934, 512))):
        thick_line(d, [a, b], 60)


@icon
def curve(d):
    d.ellipse((150, 660, 370, 880), fill=255)
    d.arc((260, 200, 1300, 1240), start=180, end=262, fill=255, width=64)
    d.polygon([(660, 140), (900, 170), (770, 330)], fill=255)


@icon
def ruler(d):
    d.rounded_rectangle((110, 400, 914, 620), radius=36, fill=255)
    for i in range(9):
        x = 190 + i * 80
        h = 110 if i % 2 == 0 else 60
        d.rectangle((x - 14, 400, x + 14, 400 + h), fill=0)


@icon
def arrow_l(d):
    thick_line(d, [(640, 160), (330, 512), (640, 864)], 110)


@icon
def arrow_r(d):
    thick_line(d, [(384, 160), (694, 512), (384, 864)], 110)


@icon
def arrow_ud(d):
    thick_line(d, [(300, 420), (512, 190), (724, 420)], 96)
    thick_line(d, [(300, 604), (512, 834), (724, 604)], 96)


@icon
def flag(d):
    thick_line(d, [(330, 120), (330, 880)], 44)
    d.polygon([(352, 140), (790, 320), (352, 500)], fill=255)
    d.ellipse((170, 830, 690, 930), fill=255)
    d.ellipse((250, 855, 610, 905), fill=0)


@icon
def star5(d):
    d.polygon(star(512, 540, 420, 170), fill=255)


@icon
def clock(d):
    d.ellipse((130, 130, 894, 894), fill=255)
    d.ellipse((200, 200, 824, 824), fill=0)
    thick_line(d, [(512, 512), (512, 300)], 56)
    thick_line(d, [(512, 512), (680, 600)], 56)


@icon
def tee(d):
    d.polygon([(370, 170), (654, 170), (590, 320), (560, 320), (545, 880),
               (479, 880), (464, 320), (434, 320)], fill=255)
    d.rounded_rectangle((370, 120, 654, 200), radius=30, fill=255)


@icon
def swing(d):
    # a club mid swing with a motion arc
    thick_line(d, [(300, 860), (620, 330)], 46)
    d.rounded_rectangle((560, 230, 760, 340), radius=40, fill=255)
    d.arc((120, 100, 880, 860), start=215, end=300, fill=255, width=40)
    d.arc((40, 20, 960, 940), start=225, end=290, fill=255, width=28)


@icon
def dpad(d):
    d.rounded_rectangle((400, 110, 624, 914), radius=40, fill=255)
    d.rounded_rectangle((110, 400, 914, 624), radius=40, fill=255)
    d.polygon([(512, 200), (450, 300), (574, 300)], fill=0)
    d.polygon([(512, 824), (450, 724), (574, 724)], fill=0)
    d.polygon([(200, 512), (300, 450), (300, 574)], fill=0)
    d.polygon([(824, 512), (724, 450), (724, 574)], fill=0)


# ---------------------------------------------------------------- main
def main():
    os.makedirs(DEST, exist_ok=True)
    made = []
    for name, fn in ICONS.items():
        img, d = new()
        fn(d)
        made.append((name, finish(img, name)))
        print("  %-10s ok" % name)

    if "--sheet" in sys.argv:
        cols = 8
        rows = (len(made) + cols - 1) // cols
        cell = 120
        sheet = Image.new("RGBA", (cols * cell, rows * cell + 20), (14, 17, 20, 255))
        sd = ImageDraw.Draw(sheet)
        for i, (name, im) in enumerate(made):
            x = (i % cols) * cell
            y = (i // cols) * cell
            tile = im.resize((96, 96), Image.LANCZOS)
            sheet.alpha_composite(tile, (x + 12, y + 6))
            sd.text((x + 12, y + 104), name, fill=(200, 200, 200, 255))
        os.makedirs(os.path.join(ROOT, "build"), exist_ok=True)
        p = os.path.join(ROOT, "build", "icon_sheet.png")
        sheet.save(p)
        print("sheet:", p)


if __name__ == "__main__":
    main()
