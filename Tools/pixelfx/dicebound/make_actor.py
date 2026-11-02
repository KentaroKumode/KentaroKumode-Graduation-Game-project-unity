"""敵の絵 (透明背景の PNG・1 体) から、 lcd_export.mjs の --actor に渡す「形」の JSON を作る。

  python make_actor.py 敵.png 出力.json

足もと中心を原点にする (x = -幅/2, y = -高さ)。 ゲームの戦闘演出で敵を足もと中心に置くのと同じ位置。
毒・出血の効果は、 この形から傷・血管・汗・泡の位置を決める (Tools/pixelfx/README.md)。
"""
import json, sys
from PIL import Image


def main():
    src, out = sys.argv[1], sys.argv[2]
    im = Image.open(src).convert("RGBA")
    bb = im.getbbox()
    if bb is None: sys.exit("絵が空 (透明な画素しか無い)")
    im = im.crop(bb); w, h = im.size; px = im.load()
    data, colors = [0] * (w * h), [None] * (w * h)
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0: continue
            i = y * w + x; data[i] = 1; colors[i] = "#%02x%02x%02x" % (r, g, b)
    json.dump({"w": w, "h": h, "x": -(w // 2), "y": -h, "data": data, "colors": colors}, open(out, "w"))
    print(f"{src} -> {out}  {w}x{h}")


if __name__ == "__main__":
    main()
