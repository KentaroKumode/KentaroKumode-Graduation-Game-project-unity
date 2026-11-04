"""ゲームのフォント (UI/GameFont.cs の指定) を、 液晶の GUI 用の 2 値のドット字に焼き、 Unity が読む .bytes にする。

液晶の GUI はテクスチャへ 1 ドットずつ打つ作りなので、 TextMesh Pro の SDF を重ねると滲む。
元のフォントの 1 ドットが 1 px になる大きさ (GameFont.DotPx) で 2 値化して描けば、 元のドットそのものが取れる。

**どのフォントを焼くかはここに書かない。** Assets/Scripts/UI/GameFont.cs の定数を読む:
  TmpPath    … TextMesh Pro のフォント (Resources 下)。 その .asset が持つ元の ttf の GUID から ttf を探して焼く
  ExtTmpPath … 本体に無い字を持つ拡張フォント (同じく .asset → ttf)。 本体に無い字だけを足す。 無ければ飛ばす
  DotPath    … 出力 (Resources 下・.bytes を付ける)
  DotPx      … 焼く大きさ (px) = 1 字の高さ (行の高さ)
フォントを変えたら GameFont.cs を直して、 これを実行し直す。

本体 (DenkiChip) は 12px のビットマップを内蔵していて、 PIL はそれを描くのでドットが正確に取れる。
拡張フォントは輪郭だけで、 PIL で描くと半ドットずれるので、 輪郭の内側にドットの中心があるかで拾う。

出力の形式 (Game2DJpFont が読む):
  "MJF2" / uint8 字の高さ H / int32 字数 / 字ごとに: uint32 文字コード, uint8 送り幅, H 行 × uint16 (最上位ビット = 左端・幅は 16 ドットまで)
使い方: python Tools/bake_jpfont.py
"""
import re
import struct
from pathlib import Path

from fontTools.pens.pointInsidePen import PointInsidePen
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
GAMEFONT = ROOT / "Assets/Scripts/UI/GameFont.cs"


def const(src, name):
    m = re.search(r"\b" + name + r"\s*=\s*(\"([^\"]*)\"|(\d+))\s*;", src)
    if not m:
        raise SystemExit(f"GameFont.cs に {name} が見つからない")
    return m.group(2) if m.group(2) is not None else int(m.group(3))


def ttf_of(sdf_asset):
    """TMP のフォントの .asset が持つ元の ttf の GUID → その ttf のパス。"""
    text = sdf_asset.read_text(encoding="utf-8", errors="ignore")
    m = re.search(r"m_SourceFontFileGUID:\s*([0-9a-f]{32})", text) or re.search(r"sourceFontFileGUID:\s*([0-9a-f]{32})", text)
    if not m:
        raise SystemExit(f"{sdf_asset.name} に元の ttf の GUID が無い")
    guid = m.group(1)
    for meta in (ROOT / "Assets").rglob("*.meta"):
        if meta.suffixes[-2:-1] and meta.name.lower().endswith((".ttf.meta", ".otf.meta")):
            if ("guid: " + guid) in meta.read_text(encoding="utf-8", errors="ignore"):
                return meta.with_suffix("")
    raise SystemExit(f"GUID {guid} の ttf が Assets に無い")


def main():
    src = GAMEFONT.read_text(encoding="utf-8")
    tmp_path, dot_path, cell = const(src, "TmpPath"), const(src, "DotPath"), const(src, "DotPx")
    if not 1 <= cell <= 32:
        raise SystemExit("DotPx は 1〜32")
    sdf = ROOT / "Assets/Resources" / (tmp_path + ".asset")
    ttf = ttf_of(sdf)
    out_path = ROOT / "Assets/Resources" / (dot_path + ".bytes")

    cmap = TTFont(str(ttf)).getBestCmap()
    font = ImageFont.truetype(str(ttf), cell)
    glyphs, wide = [], 0
    for cp in sorted(cmap):
        if cp > 0xFFFF or cp < 0x20:
            continue
        ch = chr(cp)
        img = Image.new("1", (16, cell), 0)
        d = ImageDraw.Draw(img)
        d.fontmode = "1"                       # 2 値 (アンチエイリアスなし)
        d.text((0, 0), ch, font=font, fill=1)  # 上端 = アセンダ
        rows = []
        for y in range(cell):
            bits = 0
            for x in range(16):
                if img.getpixel((x, y)):
                    bits |= 1 << (15 - x)
            rows.append(bits)
        adv = int(round(font.getlength(ch)))
        wide = max(wide, adv)
        glyphs.append((cp, adv, rows))

    ext_note = ""
    m = re.search(r"\bExtTmpPath\s*=\s*\"([^\"]*)\"\s*;", src)
    ext_sdf = ROOT / "Assets/Resources" / (m.group(1) + ".asset") if m else None
    if ext_sdf is not None and ext_sdf.exists():
        have = {cp for cp, _, _ in glyphs}
        added = outline_glyphs(ttf_of(ext_sdf), cell, have)
        glyphs = sorted(glyphs + added)
        wide = max([wide] + [adv for _, adv, _ in added])
        ext_note = f" + 拡張 {len(added)} 字"
    elif ext_sdf is not None:
        print(f"注意: {ext_sdf.relative_to(ROOT)} が無いので拡張フォントの字は焼かない")

    out = bytearray(b"MJF2") + struct.pack("<Bi", cell, len(glyphs))
    for cp, adv, rows in glyphs:
        out += struct.pack("<IB", cp, max(0, min(255, adv)))
        out += struct.pack("<%dH" % cell, *rows)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(bytes(out))
    print(f"{ttf.name} @ {cell}px{ext_note}: {len(glyphs)} glyphs (最大の送り幅 {wide}) -> {out_path.relative_to(ROOT)} ({len(out)} bytes)")


def outline_glyphs(ttf, cell, skip):
    """輪郭だけのフォントから、 ドットの中心が輪郭の内側にあるかでドットを拾う (skip の字は飛ばす)。"""
    f = TTFont(str(ttf))
    gs, unit, asc = f.getGlyphSet(), f["head"].unitsPerEm / cell, f["hhea"].ascent
    out = []
    for cp, name in sorted(f.getBestCmap().items()):
        if cp > 0xFFFF or cp < 0x20 or cp in skip:
            continue
        g = gs[name]
        rows = []
        for y in range(cell):
            bits = 0
            for x in range(16):
                pen = PointInsidePen(gs, ((x + 0.5) * unit, asc - (y + 0.5) * unit))
                g.draw(pen)
                if pen.getResult():
                    bits |= 1 << (15 - x)
            rows.append(bits)
        out.append((cp, int(round(g.width / unit)), rows))
    return out


if __name__ == "__main__":
    main()
