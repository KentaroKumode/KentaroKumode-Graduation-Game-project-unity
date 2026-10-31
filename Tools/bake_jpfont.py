"""日本語のピクセルフォント (mihiPixelmoji) を 16 ドットのビットマップに焼き、 Unity が読む .bytes にする。

液晶の GUI はテクスチャへ 1 ドットずつ打つ作りなので、 TextMesh Pro の SDF を重ねると滲む。
元の ttf は 1 ドット = 4 単位 (unitsPerEm 64 = 16 ドット) の格子で描かれているので、
16 px で 2 値化して描けば元のドットそのものが取れる。

出力: Assets/Resources/Fonts/mihi16.bytes (Game2DJpFont が読む)
  "MJF1" / int32 字数 / 字ごとに: uint32 文字コード, uint8 送り幅, 16 行 × uint16 (最上位ビット = 左端)
使い方: python Tools/bake_jpfont.py
"""
import struct
from pathlib import Path

from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
TTF = ROOT / "Assets/TextMesh Pro/Fonts/mihiPixelmoji_v1.1.ttf"
OUT = ROOT / "Assets/Resources/Fonts/mihi16.bytes"
CELL = 16


def main():
    cmap = TTFont(str(TTF)).getBestCmap()
    font = ImageFont.truetype(str(TTF), CELL)
    out = bytearray(b"MJF1")
    glyphs = []
    for cp in sorted(cmap):
        if cp > 0xFFFF or cp < 0x20:
            continue
        ch = chr(cp)
        img = Image.new("1", (CELL, CELL), 0)
        d = ImageDraw.Draw(img)
        d.fontmode = "1"                       # 2 値 (アンチエイリアスなし)
        d.text((0, 0), ch, font=font, fill=1)  # 上端 = アセンダ (基線は上から 14 ドット)
        rows = []
        for y in range(CELL):
            bits = 0
            for x in range(CELL):
                if img.getpixel((x, y)):
                    bits |= 1 << (15 - x)
            rows.append(bits)
        adv = int(round(font.getlength(ch)))
        glyphs.append((cp, adv, rows))
    out += struct.pack("<i", len(glyphs))
    for cp, adv, rows in glyphs:
        out += struct.pack("<IB", cp, max(0, min(255, adv)))
        out += struct.pack("<16H", *rows)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(bytes(out))
    print(f"{len(glyphs)} glyphs -> {OUT.relative_to(ROOT)} ({len(out)} bytes)")


if __name__ == "__main__":
    main()
