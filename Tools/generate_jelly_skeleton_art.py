"""Original hand-drawn vector-like PNG artwork for the tracked jelly skeleton.

Pillow only; no downloaded/reference pixels. Run from any directory. PNGs are
transparent, supersampled and designed along +X for existing joint segments.
The connected, recolorable jelly envelope is rendered separately by Unity.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

OUT = Path(__file__).resolve().parents[1] / 'Assets/Resources/CartoonJelly'
S = 3
CREAM = '#FFF6DE'
HIGHLIGHT = '#FFFFFF'
SHADE = '#E6D4BE'
INK = '#493655'


def canvas(w, h):
    return Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0))


def ellipse(im, rect, color):
    ImageDraw.Draw(im).ellipse(tuple(round(v * S) for v in rect), fill=color)


def path(im, commands, fill=None, stroke=None, width=1):
    """Simple cubic path flattened for antialiased Pillow polygon/line drawing."""
    points = []
    for command in commands:
        if command[0] == 'M':
            points.append((command[1], command[2]))
        elif command[0] == 'L':
            points.append((command[1], command[2]))
        elif command[0] == 'C':
            start = points[-1]
            _, x1, y1, x2, y2, x3, y3 = command
            for i in range(1, 25):
                t = i / 24
                u = 1 - t
                points.append((u**3*start[0]+3*u*u*t*x1+3*u*t*t*x2+t**3*x3,
                               u**3*start[1]+3*u*u*t*y1+3*u*t*t*y2+t**3*y3))
    scaled = [(round(x*S), round(y*S)) for x, y in points]
    draw = ImageDraw.Draw(im)
    if fill:
        draw.polygon(scaled, fill=fill)
    if stroke:
        draw.line(scaled, fill=stroke, width=round(width*S), joint='curve')


def save(im, name):
    im = im.resize((im.width//S, im.height//S), Image.Resampling.LANCZOS)
    im.save(OUT / (name + '.png'))
    return im


def skull():
    im = canvas(512, 512)
    outline = [('M', 118, 336), ('C', 64, 313, 65, 240, 72, 185),
               ('C', 78, 85, 151, 46, 250, 43), ('C', 348, 36, 431, 75, 442, 176),
               ('C', 450, 238, 450, 304, 397, 327), ('L', 366, 337),
               ('C', 353, 347, 362, 377, 338, 382), ('L', 176, 382),
               ('C', 148, 376, 165, 345, 147, 340), ('L', 118, 336)]
    path(im, outline, fill=CREAM)
    path(im, [('M', 416, 152), ('C', 445, 233, 434, 300, 390, 315),
              ('L', 354, 327), ('C', 344, 342, 352, 366, 329, 368)], stroke=SHADE, width=12)
    path(im, [('M', 108, 171), ('C', 115, 102, 170, 77, 223, 75)], stroke=HIGHLIGHT, width=16)
    # Friendly, large soft eye sockets with small translucent glints.
    path(im, [('M', 122, 190), ('C', 140, 166, 195, 177, 213, 201),
              ('C', 240, 237, 220, 278, 186, 283), ('C', 141, 291, 101, 228, 122, 190)], fill=INK)
    path(im, [('M', 300, 196), ('C', 322, 176, 370, 169, 391, 190),
              ('C', 414, 223, 379, 279, 340, 282), ('C', 296, 285, 277, 224, 300, 196)], fill=INK)
    ellipse(im, (132, 193, 147, 207), '#756185')
    ellipse(im, (307, 196, 322, 210), '#756185')
    path(im, [('M', 232, 304), ('C', 231, 285, 245, 277, 255, 292),
              ('C', 268, 274, 284, 284, 282, 302), ('C', 280, 323, 265, 327, 256, 315),
              ('C', 247, 327, 233, 323, 232, 304)], fill=INK)
    # Visible tooth gaps cut all the way through the jaw edge.
    for x, h in [(191, 24), (225, 34), (260, 30), (294, 35), (328, 24)]:
        path(im, [('M', x, 385), ('L', x, 385-h)], stroke=(0, 0, 0, 0), width=10)
    path(im, [('M', 160, 410), ('C', 184, 390, 206, 407, 256, 407),
              ('C', 307, 407, 334, 390, 357, 408), ('C', 373, 429, 352, 441, 331, 445),
              ('L', 183, 445), ('C', 157, 441, 143, 427, 160, 410)], fill=CREAM)
    for x in [194, 226, 260, 294, 326]:
        path(im, [('M', x, 401), ('L', x, 420)], stroke=(0, 0, 0, 0), width=9)
    path(im, [('M', 192, 436), ('C', 229, 442, 297, 442, 331, 435)], stroke=SHADE, width=6)
    return save(im, 'Skull')


def torso():
    im = canvas(512, 768)
    # Sternum/spine; clavicles follow the upper rib cage.
    path(im, [('M', 254, 63), ('C', 253, 147, 263, 241, 255, 336),
              ('C', 252, 410, 259, 495, 256, 553)], stroke=CREAM, width=24)
    for sign in [-1, 1]:
        def p(x, y): return (256+sign*x, y)
        a=p(18, 83); b=p(83, 59); c=p(156, 64); d=p(177, 111)
        path(im, [('M', *a), ('C', *b, *c, *d)], stroke=CREAM, width=18)
        # Three cartoon rib loops, broad at the sides and pinched at the sternum.
        for y, breadth, drop in [(155, 153, 108), (267, 173, 105), (378, 152, 96)]:
            path(im, [('M', *p(17, y)), ('C', *p(breadth*.50, y-42), *p(breadth, y-25), *p(breadth, y+25)),
                      ('C', *p(breadth+14, y+drop), *p(58, y+drop+7), *p(18, y+61))],
                 stroke=CREAM, width=18)
    path(im, [('M', 248, 77), ('C', 247, 215, 251, 374, 249, 514)], stroke='#FFFFFF', width=7)
    # Heart-shaped pelvis, one closed bone mass.
    path(im, [('M', 253, 581), ('C', 203, 500, 113, 508, 107, 571),
              ('C', 102, 624, 143, 655, 194, 668), ('L', 318, 668),
              ('C', 366, 653, 413, 622, 403, 568), ('C', 391, 508, 303, 503, 253, 581)], fill=CREAM)
    path(im, [('M', 118, 562), ('C', 126, 529, 166, 526, 191, 543)], stroke=HIGHLIGHT, width=9)
    path(im, [('M', 309, 652), ('C', 353, 642, 387, 616, 389, 582)], stroke=SHADE, width=10)
    ellipse(im, (154, 585, 211, 627), (0, 0, 0, 0))
    ellipse(im, (300, 585, 357, 627), (0, 0, 0, 0))
    # Hip sockets extend towards the thigh segment ends.
    ellipse(im, (152, 654, 202, 700), CREAM)
    ellipse(im, (309, 654, 359, 700), CREAM)
    # Existing rig segments run +X from shoulder to hip.
    im = im.transpose(Image.Transpose.ROTATE_90)
    return save(im, 'RibcagePelvis')


def bone(paired=False):
    im = canvas(512, 160)
    if paired:
        for y in (59, 101):
            path(im, [('M', 61, y), ('C', 170, y-11, 320, y+13, 448, y)], stroke=CREAM, width=17)
        for x in (56, 454):
            ellipse(im, (x-21, 48, x+21, 112), CREAM)
    else:
        path(im, [('M', 64, 80), ('C', 173, 65, 339, 92, 448, 80)], stroke=CREAM, width=27)
        for x in (55, 457):
            ellipse(im, (x-27, 44, x+24, 88), CREAM)
            ellipse(im, (x-26, 74, x+24, 116), CREAM)
        path(im, [('M', 97, 72), ('C', 209, 65, 303, 77, 414, 76)], stroke=HIGHLIGHT, width=7)
    ellipse(im, (39, 49, 50, 60), HIGHLIGHT)
    path(im, [('M', 460, 88), ('L', 466, 103)], stroke=SHADE, width=7)
    return save(im, 'PairedBone' if paired else 'SingleBone')


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    images = [skull(), torso(), bone(), bone(True)]
    board = Image.new('RGB', (1200, 900), '#201E35')
    draw = ImageDraw.Draw(board)
    draw.text((40, 25), 'JELLY BONES / ORIGINAL TRANSPARENT ARTWORK', fill='#F9F2DC', font_size=26)
    positions = [(40, 90, 430, 430), (500, 80, 650, 440), (80, 580, 1000, 150), (80, 735, 1000, 150)]
    for im, (x, y, w, h) in zip(images, positions):
        copy = im.copy(); copy.thumbnail((w, h), Image.Resampling.LANCZOS)
        board.paste(copy, (x, y), copy)
    previews = OUT.parents[1] / 'BodyThrow/ArtPreviews'
    previews.mkdir(parents=True, exist_ok=True)
    board.save(previews / 'ArtworkSheet.png')
    # The authoring contact sheet is not a runtime Resources texture.
    for legacy in (OUT / 'ArtworkSheet.png', OUT / 'ArtworkSheet.png.meta'):
        if legacy.exists():
            legacy.unlink()
    print('Created original artwork in', OUT)
