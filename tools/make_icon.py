"""
アプリアイコン（ログの書類 + 虫眼鏡）を生成する。

    python tools/make_icon.py

出力:
    src/VRCLogAnalyzer/Resources/app.ico   … exe とウィンドウのアイコン（16〜256px）
    docs/img/icon.png                      … README 用（256px）

小さいサイズでも潰れないよう、各サイズを 4 倍で描いてから縮小し、
32px 以下では書類の行数を減らして線を太くしている。
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SCALE = 4

BG_TOP = (98, 76, 230)       # 藍色
BG_BOTTOM = (37, 140, 240)   # 青
PAPER = (255, 255, 255)
LINE = (150, 165, 205)
LENS_FILL = (220, 238, 255, 235)
RING = (255, 255, 255)
HANDLE = (40, 48, 90)


def draw(size: int) -> Image.Image:
    s = size * SCALE
    small = size <= 32
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # 背景: 縦グラデーションの角丸四角
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / (s - 1)
        c = tuple(round(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        gd.line([(0, y), (s, y)], fill=c + (255,))
    mask = Image.new("L", (s, s), 0)
    pad = round(s * 0.04)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, s - pad, s - pad], radius=round(s * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)

    # ログの書類
    px0, py0, px1, py1 = s * 0.20, s * 0.15, s * 0.66, s * 0.82
    d.rounded_rectangle([px0, py0, px1, py1], radius=round(s * 0.05), fill=PAPER)
    rows = 3 if small else 5
    lw = round(s * (0.055 if small else 0.035))
    top, bottom = py0 + s * 0.12, py1 - s * 0.14
    for i in range(rows):
        y = top + (bottom - top) * i / (rows - 1)
        x1 = px1 - s * (0.08 if i % 2 == 0 else 0.16)
        d.line([(px0 + s * 0.07, y), (x1, y)], fill=LINE, width=lw)

    # 虫眼鏡（取っ手 → レンズ → 枠の順に重ねる）
    cx, cy, r = s * 0.62, s * 0.60, s * (0.20 if small else 0.19)
    ring_w = round(s * (0.075 if small else 0.06))
    hx0, hy0 = cx + r * 0.70, cy + r * 0.70
    d.line([(hx0, hy0), (s * 0.86, s * 0.86)], fill=HANDLE, width=round(s * (0.11 if small else 0.09)))
    lens = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(lens).ellipse([cx - r, cy - r, cx + r, cy + r], fill=LENS_FILL)
    img.alpha_composite(lens)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=RING, width=ring_w)

    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    images = [draw(n) for n in SIZES]
    ico = ROOT / "src" / "VRCLogAnalyzer" / "Resources" / "app.ico"
    images[-1].save(ico, format="ICO", sizes=[(n, n) for n in SIZES], append_images=images[:-1])
    png = ROOT / "docs" / "img" / "icon.png"
    images[-1].save(png)
    print(f"wrote {ico.relative_to(ROOT)} and {png.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
