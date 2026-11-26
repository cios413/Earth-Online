"""Bake vintage red/teal arrow colors into the existing OBJ UVs; never edit geometry."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/BodyThrow/Models/Throwables'
OUTPUT = ROOT / 'Assets/Resources/Throwables/Materials/ArrowRetro_BaseColor.png'
SIZE = 2048


def paint(points):
    x, y, z = points[..., 0], points[..., 1], points[..., 2]
    rng = np.random.default_rng(47)
    grain = rng.normal(0, 1, x.shape)
    # Dry red ink on the original cylindrical shaft.
    color = np.empty((*x.shape, 3), dtype=np.float32)
    color[:] = [228, 38, 43]
    color += (np.sin(z*1100+y*340)*2+grain*5)[..., None]
    highlight = np.clip((y+.005)/.010, 0, 1)
    color += highlight[..., None]*np.array([18, 11, 3])
    head = z < -.386
    head_color = np.zeros_like(color)+[22, 49, 42]
    head_color += (grain*4)[..., None]
    # Slight teal dry-brush flecks in the charcoal arrowhead.
    fleck = np.sin(y*3200+z*1500)*np.sin(z*5100+x*1200) > .78
    head_color[fleck] = [45, 132, 105]
    color[head] = head_color[head]
    tail = z > .365
    feather_lines = np.sin(z*1450+np.abs(y)*9000+x*1800) > .25
    tail_color = np.zeros_like(color)+[41, 175, 137]
    tail_color[feather_lines] = [15, 106, 85]
    tail_color += (grain*5)[..., None]
    color[tail] = tail_color[tail]
    # Narrow paper-colored bindings and small dark wraps at both ends.
    wrapped = ((z > -.384) & (z < -.346)) | ((z > .330) & (z < .365))
    thread = np.sin(z*2250+x*300+y*300) > .08
    color[wrapped & thread] = [230, 219, 175]
    color[wrapped & ~thread] = [49, 66, 51]
    chips = grain > 2.25
    color[chips] = color[chips]*.70+np.array([245, 229, 191])*.30
    return np.clip(color, 0, 255).astype(np.uint8)


def build():
    vertices, uvs, faces = [], [], []
    for line in (SOURCE / 'Arrow_0.obj').read_text(encoding='utf-8-sig').splitlines():
        parts = line.split()
        if not parts:
            continue
        if parts[0] == 'v':
            vertices.append([float(n) for n in parts[1:4]])
        elif parts[0] == 'vt':
            uvs.append([float(n) for n in parts[1:3]])
        elif parts[0] == 'f':
            face = [tuple(int(n) for n in p.split('/')[:2]) for p in parts[1:]]
            faces.extend([[face[0], face[i], face[i+1]] for i in range(1, len(face)-1)])
    vertices, uvs = np.array(vertices), np.array(uvs)
    image = np.zeros((SIZE, SIZE, 3), dtype=np.uint8)
    coverage = np.zeros((SIZE, SIZE), dtype=np.uint8)
    for face in faces:
        tri = np.array([uvs[uv-1] for _, uv in face])*[SIZE-1, -(SIZE-1)]+[0, SIZE-1]
        xyz = np.array([vertices[v-1] for v, _ in face])
        low = np.maximum(0, np.floor(tri.min(0)).astype(int))
        high = np.minimum(SIZE-1, np.ceil(tri.max(0)).astype(int))
        yy, xx = np.mgrid[low[1]:high[1]+1, low[0]:high[0]+1]
        a, b, c = tri
        det = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(det) < 1e-8:
            continue
        w0 = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/det
        w1 = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/det
        w2 = 1-w0-w1
        inside = (w0 >= -.003) & (w1 >= -.003) & (w2 >= -.003)
        points = w0[..., None]*xyz[0]+w1[..., None]*xyz[1]+w2[..., None]*xyz[2]
        tile = paint(points)
        image[yy[inside], xx[inside]] = tile[inside]
        coverage[yy[inside], xx[inside]] = 255
    # Dilate colored UV islands into their gutters to avoid black mip seams.
    for _ in range(5):
        grown = np.array(Image.fromarray(image).filter(ImageFilter.MaxFilter(3)))
        expanded = np.array(Image.fromarray(coverage).filter(ImageFilter.MaxFilter(3)))
        mask = (coverage == 0) & (expanded > 0)
        image[mask] = grown[mask]
        coverage = expanded
    Image.fromarray(image).save(OUTPUT)
    print(f'Saved {OUTPUT}; original mesh and UVs unchanged.')


if __name__ == '__main__':
    build()
