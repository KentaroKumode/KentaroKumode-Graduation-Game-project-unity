"""端子アイコン (Game2DTerminalIcons.cs) の確認用一覧画像を書き出す。

使い方: python Tools/terminal_icons_preview.py
  - .cs の表を正規表現で取り出し、 行数・行の長さ・外周の空き・60 種そろっているかを検査する。
  - 全アイコンを 8 倍に拡大し、 名前つきで並べた 1 枚を Captures/terminal_icons.png に書く。
"""
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICONS_CS = os.path.join(ROOT, "Assets", "Scripts", "UI", "Game2D", "Game2DTerminalIcons.cs")
CARDS_CS = os.path.join(ROOT, "Assets", "Scripts", "CombatSystem", "TerminalCards.cs")
OUT = os.path.join(ROOT, "Captures", "terminal_icons.png")

SIZE = 16
SCALE = 8
BODY = (0x3A, 0x4A, 0x52)
SHADES = {1: (0x5D, 0x70, 0x78), 2: (0x9F, 0xB3, 0xB8), 3: (0xE8, 0xF0, 0xEE)}
BG = (0x1A, 0x20, 0x24)
TEXT = (0xE8, 0xF0, 0xEE)


def read(path):
    with open(path, encoding="utf-8-sig") as f:
        return f.read()


def terminal_ids():
    """TerminalIds の定数名 → 表示名 (並び順を保つ)。"""
    src = read(CARDS_CS)
    m = re.search(r"public static class TerminalIds\s*\{(.*?)\n    \}", src, re.S)
    body = m.group(1) if m else ""
    return re.findall(r"(\w+)\s*=\s*\"([^\"]+)\"", body)


def icon_table():
    src = read(ICONS_CS)
    out = {}
    for m in re.finditer(r"TerminalIds\.(\w+)\s*,\s*new\[\]\s*\{(.*?)\}\s*\}", src, re.S):
        out[m.group(1)] = re.findall(r"\"([^\"]*)\"", m.group(2))
    return out


def load_font(px):
    for p in [r"C:\Windows\Fonts\meiryo.ttc", r"C:\Windows\Fonts\msgothic.ttc", r"C:\Windows\Fonts\YuGothM.ttc"]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, px)
            except OSError:
                pass
    return ImageFont.load_default()


def main():
    ids = terminal_ids()
    table = icon_table()
    errors = []
    names = [n for n, _ in ids]
    missing = [f"{n} ({jp})" for n, jp in ids if n not in table]
    extra = [n for n in table if n not in names]
    for n, rows in table.items():
        if len(rows) != SIZE:
            errors.append(f"{n}: 行数 {len(rows)}")
        for y, r in enumerate(rows):
            if len(r) != SIZE:
                errors.append(f"{n}: {y} 行目の長さ {len(r)} ({r!r})")
            if re.search(r"[^.123]", r):
                errors.append(f"{n}: {y} 行目に不正な字 ({r!r})")
            if (y in (0, SIZE - 1) and r.strip(".")) or (len(r) == SIZE and (r[0] != "." or r[-1] != ".")):
                errors.append(f"{n}: {y} 行目が外周にかかる ({r!r})")

    print(f"TerminalIds: {len(ids)} 種 / 表: {len(table)} 種")
    if missing:
        print("足りない id:", ", ".join(missing))
    if extra:
        print("TerminalIds に無い名前:", ", ".join(extra))
    for e in errors:
        print("NG", e)

    # 一覧画像
    cols = 10
    cell_w, cell_h = SIZE * SCALE + 24, SIZE * SCALE + 44
    rows_n = (len(ids) + cols - 1) // cols
    img = Image.new("RGB", (cols * cell_w + 12, rows_n * cell_h + 12), BG)
    draw = ImageDraw.Draw(img)
    font = load_font(15)
    for i, (n, jp) in enumerate(ids):
        cx = 12 + (i % cols) * cell_w
        cy = 12 + (i // cols) * cell_h
        draw.rectangle([cx, cy, cx + SIZE * SCALE - 1, cy + SIZE * SCALE - 1], fill=BODY)
        rows = table.get(n)
        if rows:
            for y, r in enumerate(rows[:SIZE]):
                for x, ch in enumerate(r[:SIZE]):
                    if ch in "123":
                        x0, y0 = cx + x * SCALE, cy + y * SCALE
                        draw.rectangle([x0, y0, x0 + SCALE - 1, y0 + SCALE - 1], fill=SHADES[int(ch)])
        else:
            draw.line([cx, cy, cx + SIZE * SCALE, cy + SIZE * SCALE], fill=(200, 60, 60), width=3)
        draw.text((cx, cy + SIZE * SCALE + 4), jp, fill=TEXT, font=font)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print("書き出し:", OUT)
    return 1 if (missing or errors) else 0


if __name__ == "__main__":
    sys.exit(main())
