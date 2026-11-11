"""画面に出す文字が、液晶のドット字 (DenkiChip + 拡張・Game2DJpFont) に入っているかを調べる。

使い方:
  python Tools/glyph_check.py <ファイル> [<ファイル> ...]     # ファイルの中の全部の字を調べる
  python Tools/glyph_check.py --text "調べたい文"

無い字があれば一覧を出して終了コード 1。 無い字は Tools/fontext/glyphs.txt に足して焼き直す
(手順は docs/design/gui-needed.md)。 焼いたドット字の正本は GameFont.cs の DotPath。
"""
import io, os, re, struct, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def dot_path():
    src = io.open(os.path.join(ROOT, "Assets/Scripts/UI/GameFont.cs"), encoding="utf-8-sig").read()
    m = re.search(r'\bDotPath\s*=\s*"([^"]*)"', src)
    return os.path.join(ROOT, "Assets/Resources", m.group(1) + ".bytes")


def glyphs():
    b = open(dot_path(), "rb").read()
    cell, n = struct.unpack("<Bi", b[4:9])
    off, cps = 9, set()
    for _ in range(n):
        cp, _adv = struct.unpack("<IB", b[off:off + 5]); cps.add(cp); off += 5 + 2 * cell
    return cps


def main():
    args = sys.argv[1:]
    if not args: sys.exit(__doc__)
    text = args[1] if args[0] == "--text" else "".join(io.open(p, encoding="utf-8-sig").read() for p in args)
    have = glyphs()
    miss = sorted({ch for ch in text if ord(ch) >= 0x20 and ch not in "\r\n\t" and ord(ch) not in have})
    if miss:
        print("ドット字に無い字 (%d): %s" % (len(miss), "".join(miss)))
        sys.exit(1)
    print("全部ある")


if __name__ == "__main__":
    main()
