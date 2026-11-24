"""Create original reference-inspired pearlescent grenade art (transparent RGBA).

Requires Pillow and NumPy. Re-run from any directory; no external image inputs.
The shading, glass rim, pin, heart and tag are baked into a single flat image.
"""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parents[1] / 'Assets/Resources/Throwables/Previews'
SIZE = 1024
S = 2


def layer():
    return Image.new('RGBA', (SIZE*S, SIZE*S))


def line(im, points, fill, width):
    ImageDraw.Draw(im).line([(int(x*S), int(y*S)) for x, y in points],
                            fill=fill, width=int(width*S), joint='curve')


def ellipse(im, box, fill, outline=None, width=1):
    ImageDraw.Draw(im).ellipse(tuple(int(v*S) for v in box), fill=fill,
                              outline=outline, width=int(width*S))


def polygon(im, points, fill):
    ImageDraw.Draw(im).polygon([(int(x*S), int(y*S)) for x, y in points], fill=fill)


def curve(im, points, fill, width):
    a, b, c, d = [np.array(p) for p in points]
    samples = []
    for t in np.linspace(0, 1, 100):
        samples.append((1-t)**3*a+3*(1-t)**2*t*b+3*(1-t)*t*t*c+t**3*d)
    line(im, samples, fill, width)


def ring(im, center, radii, angle, color, width):
    a = math.radians(angle)
    points = []
    for t in np.linspace(0, math.tau, 240):
        x, y = radii[0]*math.cos(t), radii[1]*math.sin(t)
        points.append((center[0]+x*math.cos(a)-y*math.sin(a),
                       center[1]+x*math.sin(a)+y*math.cos(a)))
    line(im, points, color, width)


def star(im, x, y, r, color):
    polygon(im, [(x,y-r),(x+r*.19,y-r*.17),(x+r*.66,y),
                 (x+r*.15,y+r*.18),(x,y+r),(x-r*.18,y+r*.18),
                 (x-r*.66,y),(x-r*.18,y-r*.18)], color)


def build():
    im = layer()
    # Soft contact shadow lives in the transparent image, not a 3D model.
    shadow = layer()
    ellipse(shadow, (226, 823, 841, 950), (116, 46, 132, 67))
    im.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(22*S)))
    # Lever behind the glass body. Rounded edges have a shaded pink bevel.
    lever = layer()
    polygon(lever, [(350,181),(663,125),(717,151),(798,383),(775,405),
                    (718,398),(643,223),(397,247)], '#cf4b94')
    polygon(lever, [(347,175),(660,119),(701,143),(777,376),(749,383),
                    (675,202),(394,232)], '#ff9ec8')
    line(lever, [(351,175),(659,120),(698,143),(773,374)], '#ffe7f3', 5)
    line(lever, [(394,232),(675,202),(749,383)], '#e768a9', 6)
    ellipse(lever, (330,162,381,208), '#e56ca9', '#ffdaeb', 4)
    ellipse(lever, (337,164,368,187), '#ffd8e9')
    # Embossed heart on the safety lever.
    heart = [(474,192),(455,175),(445,184),(444,197),(478,224),
             (506,190),(501,177),(489,176),(474,192)]
    line(lever, heart, '#d45997', 5)
    line(lever, [(446,195),(477,221),(502,189)], '#ffd3e8', 3)
    polygon(lever, [(427,250),(620,229),(643,329),(430,349)], '#e89bb8')
    polygon(lever, [(438,255),(606,238),(625,329),(444,345)], '#ffd4ce')
    line(lever, [(442,258),(450,336)], '#fff4d9', 13)
    line(lever, [(586,245),(602,328)], '#f3a1c8', 19)
    im.alpha_composite(lever)
    # Analytic sphere shading: iridescent glass, soft speculars and rolled rim.
    n = SIZE*S
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)/S
    x, y = (xx-511)/326, (yy-610)/319
    rr = x*x+y*y
    r = np.sqrt(rr)
    z = np.sqrt(np.maximum(0, 1-rr))
    rgb = np.empty((n,n,3), np.float32)
    rgb[:] = [247, 147, 197]
    wave = np.sin(x*5.4+y*4.2+z*5)*.5+.5
    rgb += wave[:,:,None]*np.array([-19,12,40])
    warm = np.exp(-((x+.27)**2/.15+(y-.28)**2/.10))*z
    rgb += warm[:,:,None]*np.array([13,59,7])
    shade = np.maximum(0, .65*x+.40*y)*z
    rgb -= shade[:,:,None]*np.array([46,73,47])
    for cx,cy,rx,ry,amount in [(-.42,-.53,.18,.27,.94),(-.56,-.10,.10,.21,.80),
                               (.27,.52,.18,.15,.79),(-.15,.66,.32,.07,.60)]:
        g = np.exp(-((x-cx)**2/rx**2+(y-cy)**2/ry**2))*amount
        rgb = rgb*(1-g[:,:,None])+np.array([255,251,234])*g[:,:,None]
    rim = np.exp(-((r-.978)/.014)**2)*.92
    rgb = rgb*(1-rim[:,:,None])+np.array([255,237,249])*rim[:,:,None]
    inner = np.exp(-((r-.94)/.026)**2)*.44
    rgb = rgb*(1-inner[:,:,None])+np.array([236,199,251])*inner[:,:,None]
    rng = np.random.default_rng(824)
    rgb += rng.normal(0,.6,(n,n,1))
    alpha = np.clip((1-r)*n/4,0,1)*245
    pixels = np.dstack((np.clip(rgb,0,255),alpha)).astype(np.uint8)
    im.alpha_composite(Image.fromarray(pixels))
    # A gently curved seam sells the glass shell's volume.
    curve(im, [(191,624),(363,580),(702,587),(834,635)], '#dc80b3', 8)
    curve(im, [(191,619),(363,574),(702,581),(834,630)], '#fff1eb', 4)
    curve(im, [(198,633),(368,590),(698,598),(829,643)], (255,215,240,165), 3)
    curve(im, [(227,497),(241,424),(300,367),(371,347)], '#fff8ef', 9)
    curve(im, [(241,499),(252,441),(273,408),(300,390)], '#ffdce9', 3)
    # Silver-lilac pull ring with a reflected pink inner edge.
    ring(im, (716,240), (119,78), -48, '#c0569c', 20)
    ring(im, (713,235), (119,78), -48, '#f5badb', 15)
    ring(im, (711,232), (119,78), -48, '#fce9fa', 7)
    ring(im, (713,236), (108,68), -48, '#b8a2e7', 4)
    ellipse(im, (639,142,669,180), '#ed85b6', '#ffe1ee', 4)
    # Small rose-gold chain and dangling star tag.
    for i in range(7):
        cy = 331+i*14
        cx = 699-15*math.sin(i*.6)
        ring(im, (cx,cy), (10,15), -18, '#d58ca6', 5)
        ring(im, (cx-1,cy-1), (10,15), -18, '#ffe2ce', 2)
    tag = layer()
    polygon(tag, [(669,424),(725,407),(792,524),(721,560),(657,452)], '#c77a9c')
    polygon(tag, [(665,418),(722,404),(788,518),(718,553),(653,448)], '#ffe3e9')
    line(tag, [(665,418),(722,404),(788,518)], '#fff9f2', 4)
    ellipse(tag, (673,424,688,440), '#d889a7', '#fff5df', 2)
    star(tag, 720,482,28,'#ee70b3')
    im.alpha_composite(tag)
    star(im, 320,403,18,'#fffaf3')
    star(im, 747,732,13,'#fff8ef')
    im = im.resize((SIZE,SIZE),Image.Resampling.LANCZOS)
    im.save(OUT/'DreamGrenade.png')
    print(f'Saved {OUT / "DreamGrenade.png"}')


if __name__ == '__main__':
    build()
