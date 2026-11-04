"""DenkiChip に無い字を、 glyphs.txt のドット絵から拡張フォント (.ttf) にする。

本体の x8y12pxDenkiChip.ttf には手を入れない。 足した字だけを持つ別のフォントを作り、 使う側でつなぐ:
  TextMesh Pro … 拡張フォントの SDF を本体の SDF の fallback にする (GameFont.ExtTmpPath・GameFont.Tmp がつなぐ)
  液晶のドット字 … Tools/bake_jpfont.py が本体と拡張をまとめて焼く

座標は本体と同じ: unitsPerEm 1200・1 ドット = 100 単位・上端 1100・下端 -100。
ドットは横に続く点を 1 本の長方形にまとめ、 縦に接する長方形はそのまま重ねずに並べる (輪郭は重ならない)。

配布条件: DenkiChip は SIL Open Font License 1.1。 改変版も OFL で、 元の著作権表示を残す。
予約フォント名は宣言されていないが、 本体と取り違えないよう名前を変えて "x8y12pxDenkiChip Ext" とする。

使い方: python Tools/fontext/build_ext_font.py   (字を直したら実行し、 Unity で拡張の SDF を作り直してからドット字を焼き直す)
"""
from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SRC = HERE / "glyphs.txt"
BASE_TTF = ROOT / "Assets/Fonts/x8y12pxDenkiChip.ttf"
OUT_TTF = ROOT / "Assets/Fonts/x8y12pxDenkiChipExt.ttf"
UNIT, TOP = 100, 1100
FAMILY = "x8y12pxDenkiChip Ext"


def load(path):
    lines = [l.rstrip("\n") for l in path.read_text(encoding="utf-8").splitlines() if not l.startswith("# ")]
    out, i = {}, 0
    while i < len(lines):
        if not lines[i].strip():
            i += 1; continue
        ch, adv = lines[i].split()
        out[ch] = (int(adv), lines[i + 1:i + 13]); i += 13
    return out


def draw(rows, pen):
    for y, row in enumerate(rows):
        x = 0
        while x < len(row):
            if row[x] != "#":
                x += 1; continue
            x0 = x
            while x < len(row) and row[x] == "#":
                x += 1
            l, r, t, b = x0 * UNIT, x * UNIT, TOP - y * UNIT, TOP - (y + 1) * UNIT
            pen.moveTo((l, b)); pen.lineTo((l, t)); pen.lineTo((r, t)); pen.lineTo((r, b)); pen.closePath()   # 時計回り = 外側


def main():
    glyphs = load(SRC)
    base = TTFont(str(BASE_TTF))
    base_cmap = base.getBestCmap()
    dup = [ch for ch in glyphs if ord(ch) in base_cmap]
    if dup:
        raise SystemExit("本体に既にある字: " + "".join(dup))

    names = {ch: "uni%04X" % ord(ch) for ch in glyphs}
    order = [".notdef"] + list(names.values())
    fb = FontBuilder(1200, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({ord(ch): n for ch, n in names.items()})

    outlines, metrics = {}, {}
    pen = TTGlyphPen(None)
    outlines[".notdef"] = pen.glyph(); metrics[".notdef"] = (1000, 0)
    for ch, (adv, rows) in glyphs.items():
        pen = TTGlyphPen(None)
        draw(rows, pen)
        g = pen.glyph()
        outlines[names[ch]] = g
        lsb = min((i for row in rows for i, c in enumerate(row) if c == "#"), default=0) * UNIT
        metrics[names[ch]] = (adv * UNIT, lsb)
    fb.setupGlyf(outlines)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=1100, descent=-100)

    copyright = base["name"].getDebugName(0) + "; additional glyphs 2026 DICE BOUND"
    fb.setupNameTable({
        "copyright": copyright,
        "familyName": FAMILY,
        "styleName": "Regular",
        "uniqueFontIdentifier": FAMILY + " Regular",
        "fullName": FAMILY,
        "psName": FAMILY.replace(" ", "") + "-Regular",
        "version": "Version 1.000",
        "licenseDescription": base["name"].getDebugName(13),
        "licenseInfoURL": base["name"].getDebugName(14),
    })
    fb.setupOS2(sTypoAscender=1100, sTypoDescender=-100, sTypoLineGap=0, usWinAscent=1100, usWinDescent=100)
    fb.setupPost()
    fb.save(str(OUT_TTF))
    print(f"{len(glyphs)} 字 -> {OUT_TTF.relative_to(ROOT)}: {''.join(glyphs)}")


if __name__ == "__main__":
    main()
