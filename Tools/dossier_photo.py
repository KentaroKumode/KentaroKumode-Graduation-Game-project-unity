# -*- coding: utf-8 -*-
"""
dossier_photo.py — ドット絵を「古い考古学写真」風カードに変換する (2026-07-14)

パイプライン: 切り抜き → 拡大(nearest) → 軽いボケ → セピア減色(グラデーションマップ)
            → ハーフトーン網点(ordered dither) → ビネット → 粒子ノイズ
            → 紙カード台紙(染み・縁) → キャプション(手書き風フォント)

使い方:
  python tools/dossier_photo.py input.png output.png
  python tools/dossier_photo.py input.png output.png --crop 128,0,64,64 --zoom 14 ^
      --caption "ARCHAEOLOGICAL SPECIMEN, SITE 7" --year 1923

  --crop x,y,w,h : 元画像から切り抜く矩形 (省略時は不透明部分の自動バウンディング)
  --zoom N       : ドットの拡大倍率 (既定 12)
  --blur N       : ボケの強さ px (既定 2.5)
  --cell N       : 網点セルのサイズ px (既定 3)
  --size WxH     : カード全体サイズ (既定 1024x631)
"""
import argparse
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# セピアのグラデーションマップ (暗 → 明)
SEPIA_STOPS = [
    (0.00, (16, 11, 6)),
    (0.25, (54, 38, 22)),
    (0.50, (107, 79, 46)),
    (0.75, (181, 148, 100)),
    (1.00, (232, 217, 176)),
]
PAPER = (216, 200, 164)
PAPER_EDGE = (196, 178, 140)
INK = (60, 45, 28)

BAYER8 = np.array([
    [0, 32, 8, 40, 2, 34, 10, 42],
    [48, 16, 56, 24, 50, 18, 58, 26],
    [12, 44, 4, 36, 14, 46, 6, 38],
    [60, 28, 52, 20, 62, 30, 54, 22],
    [3, 35, 11, 43, 1, 33, 9, 41],
    [51, 19, 59, 27, 49, 17, 57, 25],
    [15, 47, 7, 39, 13, 45, 5, 37],
    [63, 31, 55, 23, 61, 29, 53, 21],
], dtype=np.float32) / 64.0


def sepia_map(t: np.ndarray) -> np.ndarray:
    """輝度 0..1 をセピアグラデーションへ写像。"""
    out = np.zeros(t.shape + (3,), dtype=np.float32)
    for (t0, c0), (t1, c1) in zip(SEPIA_STOPS, SEPIA_STOPS[1:]):
        m = (t >= t0) & (t <= t1)
        f = np.zeros_like(t)
        f[m] = (t[m] - t0) / max(t1 - t0, 1e-6)
        for ch in range(3):
            out[..., ch][m] = c0[ch] + (c1[ch] - c0[ch]) * f[m]
    return out


def auto_crop(img: Image.Image):
    a = np.array(img)
    if a.shape[2] == 4:
        mask = a[..., 3] > 8
    else:
        mask = a[..., :3].sum(axis=2) > 24  # 黒背景想定
    ys, xs = np.where(mask)
    if len(xs) == 0:
        return img
    pad = 2
    return img.crop((max(xs.min() - pad, 0), max(ys.min() - pad, 0),
                     min(xs.max() + pad, img.width), min(ys.max() + pad, img.height)))


def make_photo(sprite: Image.Image, zoom: int, blur: float, cell: int,
               photo_w: int, photo_h: int, seed: int) -> Image.Image:
    rng = random.Random(seed)
    big = sprite.convert("RGBA").resize((sprite.width * zoom, sprite.height * zoom), Image.NEAREST)

    # 写真キャンバス (被写体を中央やや下に・はみ出し歓迎 = 接写の圧)
    canvas = Image.new("RGBA", (photo_w, photo_h), (0, 0, 0, 255))
    scale = max(photo_w / big.width, photo_h / big.height) * 1.15  # 少しズームで見切れさせる
    big = big.resize((int(big.width * scale), int(big.height * scale)), Image.NEAREST)
    canvas.alpha_composite(big, ((photo_w - big.width) // 2, int((photo_h - big.height) * 0.35)))

    # ソフトフォーカス
    soft = canvas.convert("RGB").filter(ImageFilter.GaussianBlur(blur))

    # 輝度 → トーンカーブ (ハイライト飛ばし気味 = 古い印画紙)
    lum = np.asarray(soft, dtype=np.float32).mean(axis=2) / 255.0
    lum = np.power(lum, 0.72) * 1.08

    # ハーフトーン: セル単位の ordered dither で 6 階調に量子化。
    # 深い影では網点を効かせない (背景が市松にならず黒に沈むように)
    h, w = lum.shape
    yy, xx = np.mgrid[0:h, 0:w]
    thr = (BAYER8[(yy // cell) % 8, (xx // cell) % 8] - 0.5) * np.clip((lum - 0.06) * 6, 0, 1)
    levels = 6
    q = np.clip(np.round(lum * (levels - 1) + thr) / (levels - 1), 0, 1)

    # ビネット (強め) + 深部の黒潰し
    cy, cx = h / 2, w / 2
    r = np.sqrt(((yy - cy) / cy) ** 2 + ((xx - cx) / cx) ** 2)
    q *= np.clip(1.06 - 0.52 * r ** 2.0, 0.22, 1.0)
    q[q < 0.10] *= 0.35

    rgb = sepia_map(q)

    # 粒子ノイズ
    noise = np.random.default_rng(seed).normal(0, 6.5, (h, w, 1)).astype(np.float32)
    rgb = np.clip(rgb + noise, 0, 255).astype(np.uint8)
    photo = Image.fromarray(rgb)

    # 引っかき傷 (薄い明るい線を数本)
    d = ImageDraw.Draw(photo, "RGBA")
    for _ in range(rng.randint(2, 4)):
        x0 = rng.uniform(0, w); y0 = rng.uniform(0, h)
        ang = rng.uniform(-0.4, 0.4) + rng.choice([0, math.pi / 2])
        ln = rng.uniform(h * 0.2, h * 0.7)
        d.line([(x0, y0), (x0 + math.cos(ang) * ln, y0 + math.sin(ang) * ln)],
               fill=(232, 217, 176, rng.randint(14, 30)), width=1)
    return photo


def make_card(photo: Image.Image, card_w: int, card_h: int, caption: str, year: str,
              seed: int, font_path: str | None) -> Image.Image:
    rng = random.Random(seed + 1)
    card = Image.new("RGB", (card_w, card_h), PAPER)
    d = ImageDraw.Draw(card, "RGBA")

    # 紙の質感: 低周波の染み + 粒子
    stains = Image.new("L", (card_w // 8, card_h // 8), 128)
    sd = np.random.default_rng(seed + 2).normal(0, 22, (card_h // 8, card_w // 8))
    stains = Image.fromarray(np.clip(128 + sd, 0, 255).astype(np.uint8)).resize(
        (card_w, card_h), Image.BILINEAR).filter(ImageFilter.GaussianBlur(18))
    sarr = (np.asarray(stains, dtype=np.float32) - 128) * 0.25
    carr = np.asarray(card, dtype=np.float32) + sarr[..., None]
    card = Image.fromarray(np.clip(carr, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(card, "RGBA")

    # 縁の摩耗 (角を暗く)
    for corner in [(0, 0), (card_w, 0), (0, card_h), (card_w, card_h)]:
        r = rng.randint(60, 140)
        d.ellipse([corner[0] - r, corner[1] - r, corner[0] + r, corner[1] + r],
                  fill=(150, 130, 96, rng.randint(20, 45)))

    # 写真を貼る (下にキャプション帯を残す)
    margin = int(card_w * 0.035)
    cap_h = int(card_h * 0.10)
    pw, ph = card_w - margin * 2, card_h - margin * 2 - cap_h
    photo = photo.resize((pw, ph), Image.LANCZOS)
    # 写真の縁 (白フチ + 影)
    d.rectangle([margin - 3, margin - 3, margin + pw + 3, margin + ph + 3], fill=PAPER_EDGE)
    card.paste(photo, (margin, margin))

    # キャプション (手書き風フォント: Windows なら Ink Free / Segoe Script)
    font = None
    for cand in ([font_path] if font_path else []) + [
            "C:/Windows/Fonts/Inkfree.ttf", "C:/Windows/Fonts/segoesc.ttf"]:
        try:
            font = ImageFont.truetype(cand, int(cap_h * 0.62))
            break
        except (OSError, TypeError):
            continue
    if font is None:
        font = ImageFont.load_default()
    d = ImageDraw.Draw(card)
    ty = margin + ph + int(cap_h * 0.16)
    d.text((card_w / 2, ty), caption, font=font, fill=INK, anchor="ma")
    if year:
        d.text((card_w - margin - 8, ty), year, font=font, fill=INK, anchor="ra")
    return card


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input"); ap.add_argument("output")
    ap.add_argument("--crop", default=None, help="x,y,w,h")
    ap.add_argument("--zoom", type=int, default=12)
    ap.add_argument("--blur", type=float, default=2.5)
    ap.add_argument("--cell", type=int, default=3)
    ap.add_argument("--size", default="1024x631")
    ap.add_argument("--caption", default="ARCHAEOLOGICAL SPECIMEN, SITE 7")
    ap.add_argument("--year", default="1923")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--font", default=None, help="キャプション用 ttf のパス")
    args = ap.parse_args()

    card_w, card_h = (int(v) for v in args.size.lower().split("x"))
    img = Image.open(args.input).convert("RGBA")
    if args.crop:
        x, y, w, h = (int(v) for v in args.crop.split(","))
        img = img.crop((x, y, x + w, y + h))
    else:
        img = auto_crop(img)

    photo = make_photo(img, args.zoom, args.blur, args.cell,
                       int(card_w * 0.93), int(card_h * 0.83), args.seed)
    card = make_card(photo, card_w, card_h, args.caption, args.year, args.seed, args.font)
    card.save(args.output)
    print(f"saved: {args.output} ({card_w}x{card_h})")


if __name__ == "__main__":
    main()
