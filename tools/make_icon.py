"""Regenerates icon.ico (blue rounded square + white scissors), matching the in-app LogoMark.
Usage:  python tools/make_icon.py        (needs Pillow: pip install pillow)
"""
from PIL import Image, ImageDraw
import os

def render(S):
    SS = 4; N = S * SS
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    grad = Image.new("RGBA", (N, N))
    top, bot = (59, 130, 246), (29, 78, 216)
    gd = ImageDraw.Draw(grad)
    for y in range(N):
        t = y / (N - 1)
        gd.line([(0, y), (N, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (N, N), 0)
    pad = int(N * 0.03)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, N - 1 - pad, N - 1 - pad], radius=int(N * 0.24), fill=255)
    im.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(im)
    s = N * 0.58; ox = oy = (N - s) / 2; k = s / 24
    w = max(2, int(round((2.3 if S <= 24 else 2.0) * k)))
    P = lambda x, y: (ox + x * k, oy + y * k)
    def circ(cx, cy, r): d.ellipse([P(cx - r, cy - r), P(cx + r, cy + r)], outline="white", width=w)
    def line(a, b):
        d.line([P(*a), P(*b)], fill="white", width=w)
        for q in (a, b):
            x, y = P(*q); d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill="white")
    # Same scissors geometry as Icons.Paint("snip") in src/UI.cs (24x24 grid)
    circ(6, 6, 3); circ(6, 18, 3)
    line((8.12, 8.12), (12, 12)); line((20, 4), (8.12, 15.88)); line((14.8, 14.8), (20, 20))
    return im.resize((S, S), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [render(s) for s in sizes]
out = os.path.join(os.path.dirname(__file__), "..", "icon.ico")
imgs[-1].save(out, sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
print("wrote", os.path.abspath(out))
