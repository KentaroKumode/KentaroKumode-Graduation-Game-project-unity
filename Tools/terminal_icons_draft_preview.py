# 端子アイコンの下書き (テキスト) を検査して、 一覧の PNG を書き出す。
#   使い方: python Tools/terminal_icons_draft_preview.py <下書き.txt> <出力.png>
#   下書きの形: 「@定数名」の行の後に 16 行 (各 16 文字・ . 1 2 3)。 # で始まる行は注釈。
import io, sys
from PIL import Image, ImageDraw


def load(path):
    icons, cur, rows = {}, None, []
    for line in io.open(path, encoding="utf-8").read().splitlines():
        s = line.rstrip()
        if s.startswith("#") or (not s and cur is None): continue
        if s.startswith("@"):
            if cur: icons[cur] = rows
            cur, rows = s[1:].strip(), []
        elif cur is not None and s: rows.append(s)
    if cur: icons[cur] = rows
    return icons


def check(icons):
    bad = []
    for k, r in icons.items():
        if len(r) != 16: bad.append(f"{k}: 行数 {len(r)}")
        for i, x in enumerate(r):
            if len(x) != 16: bad.append(f"{k}: {i + 1} 行目の長さ {len(x)}")
            if any(c not in ".123" for c in x): bad.append(f"{k}: {i + 1} 行目に使えない字")
        if len(r) == 16 and all(len(x) == 16 for x in r):
            edge = r[0] + r[15] + "".join(x[0] + x[15] for x in r)
            if any(c != "." for c in edge): bad.append(f"{k}: 外周にドット")
    return bad


if __name__ == "__main__":
    icons = load(sys.argv[1]); bad = check(icons)
    for b in bad: print("NG", b)
    ok = {k: v for k, v in icons.items() if len(v) == 16 and all(len(x) == 16 for x in v)}
    PAL = {"1": (93, 112, 120), "2": (159, 179, 184), "3": (232, 240, 238)}
    BODY = [(176, 86, 74), (74, 120, 168), (184, 150, 62), (79, 138, 92)]
    cols = 5; cw, ch = 250, 170
    img = Image.new("RGB", (cols * cw, ((len(ok) + cols - 1) // cols) * ch), (25, 28, 29)); d = ImageDraw.Draw(img)
    for n, (k, rows) in enumerate(ok.items()):
        ox, oy = (n % cols) * cw + 8, (n // cols) * ch + 20
        d.text((ox, oy - 16), k, fill=(230, 184, 92))
        d.rectangle([ox, oy, ox + 127, oy + 127], fill=(58, 74, 82))
        for y in range(16):
            for x in range(16):
                c = rows[y][x]
                if c in PAL: d.rectangle([ox + x * 8, oy + y * 8, ox + x * 8 + 7, oy + y * 8 + 7], fill=PAL[c])
        # USB の本体色に載せた実寸 3 倍 (種類の色 4 つのうち 2 つで見る)
        for j, body in enumerate((BODY[n % 4], BODY[(n + 1) % 4])):
            bx, by = ox + 140 + j * 52, oy + 30
            dark = tuple(int(v * 0.5) for v in body); mid = tuple(int(v + (255 - v) * 0.45) for v in body); lit = tuple(int(v + (255 - v) * 0.88) for v in body)
            d.rectangle([bx - 6, by - 6, bx + 53, by + 53], fill=body)
            for y in range(16):
                for x in range(16):
                    c = rows[y][x]
                    if c in "123": d.rectangle([bx + x * 3, by + y * 3, bx + x * 3 + 2, by + y * 3 + 2], fill={"1": dark, "2": mid, "3": lit}[c])
    img.save(sys.argv[2]); print(len(ok), "icons ->", sys.argv[2], "| NG", len(bad))
