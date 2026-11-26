"""Draw original transparent alien face art for the existing jelly head overlay.

Large dark almond eyes, tiny nose, lime forehead and star-shaped reflections
interpret the supplied reference. Antennae are runtime jelly geometry, not paint.
Requires Pillow and NumPy. Run from any directory.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SIZE = 1536
SCALE = SIZE / 512


def path(start, curves):
    points = [start]
    p0 = np.array(start, float)
    for c1, c2, end in curves:
        p1, p2, p3 = map(lambda p: np.array(p, float), (c1, c2, end))
        for t in np.linspace(0, 1, 70)[1:]:
            p = (1-t)**3*p0 + 3*(1-t)**2*t*p1 + 3*(1-t)*t*t*p2 + t**3*p3
            points.append(tuple(p))
        p0 = p3
    return [(round(x*SCALE), round(y*SCALE)) for x, y in points]


def mask(points):
    result = Image.new('L', (SIZE, SIZE))
    ImageDraw.Draw(result).polygon(points, fill=255)
    return result


def ellipse_mask(box):
    result = Image.new('L', (SIZE, SIZE))
    ImageDraw.Draw(result).ellipse(tuple(round(v*SCALE) for v in box), fill=255)
    return result


def gradient(base, highlight, center, spread):
    yy, xx = np.mgrid[:SIZE, :SIZE].astype(np.float32) / SCALE
    weight = np.exp(-(((xx-center[0])/spread[0])**2 + ((yy-center[1])/spread[1])**2))
    rgb = np.asarray(base) + weight[..., None]*(np.asarray(highlight)-np.asarray(base))
    return Image.fromarray(np.uint8(np.clip(rgb, 0, 255))).convert('RGBA')


def draw():
    image = Image.new('RGBA', (SIZE, SIZE))
    outline = path((252, 39), [
        ((137, 31), (61, 100), (57, 221)),
        ((49, 333), (122, 409), (229, 448)),
        ((246, 455), (269, 455), (287, 447)),
        ((394, 404), (463, 322), (456, 217)),
        ((449, 108), (365, 36), (252, 39)),
    ])
    outer = mask(outline)
    image.paste(gradient((31, 105, 69), (170, 214, 78), (205, 155), (230, 315)), (0, 0), outer)
    inner = outer.filter(ImageFilter.MinFilter(25))
    image.paste(gradient((64, 143, 75), (185, 221, 98), (184, 125), (230, 330)), (0, 0), inner)

    left = path((176, 186), [
        ((136, 146), (87, 158), (86, 221)),
        ((83, 288), (123, 353), (210, 377)),
        ((228, 330), (210, 226), (176, 186)),
    ])
    right = path((336, 186), [
        ((376, 146), (425, 158), (426, 221)),
        ((429, 288), (389, 353), (302, 377)),
        ((284, 330), (302, 226), (336, 186)),
    ])
    for shape in (left, right):
        eye = mask(shape)
        image.paste((26, 90, 65, 255), (0, 0), eye.filter(ImageFilter.MaxFilter(19)))
        image.paste(gradient((3, 22, 28), (26, 74, 72), (175, 214), (235, 180)), (0, 0), eye)

    draw = ImageDraw.Draw(image)
    for cx, cy, radius in [(144, 233, 20), (365, 240, 25)]:
        star = [(cx,cy-radius), (cx+2.3,cy-3), (cx+radius*.66,cy), (cx+2.3,cy+3),
                (cx,cy+radius), (cx-2.3,cy+3), (cx-radius*.66,cy), (cx-2.3,cy-3)]
        draw.polygon([(round(x*SCALE), round(y*SCALE)) for x,y in star], fill=(224, 255, 240, 250))
        draw.ellipse(((cx+12)*SCALE, (cy+24)*SCALE, (cx+16)*SCALE, (cy+28)*SCALE), fill=(179, 231, 213, 210))
        draw.ellipse(((cx-17)*SCALE, (cy+37)*SCALE, (cx-14)*SCALE, (cy+40)*SCALE), fill=(145, 208, 193, 185))

    nose = path((254, 349), [((242, 350), (239, 365), (248, 370)),
                              ((254, 373), (262, 367), (260, 359)),
                              ((259, 353), (257, 350), (254, 349))])
    image.paste((17, 64, 51, 255), (0, 0), mask(nose))
    draw = ImageDraw.Draw(image)
    mouth = path((237, 398), [((248, 405), (265, 405), (277, 396))])
    draw.line(mouth, fill=(44, 111, 65, 210), width=round(3*SCALE))
    highlight = ellipse_mask((158, 79, 232, 103)).filter(ImageFilter.GaussianBlur(9*SCALE))
    image.alpha_composite(Image.composite(Image.new('RGBA', image.size, (224, 244, 161, 90)),
                                         Image.new('RGBA', image.size), highlight))
    image = image.resize((768, 768), Image.Resampling.LANCZOS)
    image.save(ROOT / 'Assets/Resources/CartoonJelly/AlienFace.png')


if __name__ == '__main__':
    draw()
