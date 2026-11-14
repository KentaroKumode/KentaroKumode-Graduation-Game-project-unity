"""書き出した絵 (Tools/pixelfx/dicebound/sheets/<名前>.png.bytes + <名前>.json) を、 ゲームが読み込んだ後の形まで作って
Assets/Resources/Game2DFx/<名前>.fxb.bytes に置く (2026-11-15)。 ゲームが読むのはこれだけ (PNG と json はビルドに入れない)。
export_fx.py / export_game.py が書き出しの最後に呼ぶ。 手で呼ぶ時:

  python Tools/pixelfx/dicebound/bake_fxb.py            # 全部
  python Tools/pixelfx/dicebound/bake_fxb.py tenyo      # 名前の頭で絞る

なぜ: ゲームの中で PNG を展開して画素を走査すると、 大きい絵 1 枚で 100〜250 ms 止まる (戦闘中のコマ落ち)。
ここで 2026-11-15 までの FxSheet.cs の読み込み (PNG を走査するコンストラクタ) と<b>同じ規則</b>で、 コマごとの「帯 + 残りの画素 + 色の番号」を作って詰めておく
(2026-11-15 に、 その C# と全 94 枚の全コマで配列が同じになる事を確かめた)。 ゲーム側は解凍して配列へ写すだけ (主スレッドの外でできる)。
画素は 1 つも変えない ── 形を変えるだけ。 毎回、 書いた .fxb を読み戻して、 コマごとの不透明な画素の位置と色が PNG と 1 つも違わない事を確かめる (check)。

形 (先頭 8 バイトの後ろは raw deflate で圧縮・数は全部 little endian):
  "FXB1"  u32 解凍後の長さ
  -- 以下 解凍後 --
  i32 W, H, PivotX, PivotY, Fps   u8 Loop   i32 コマ数   i32 色の数 (0 = 色の一覧を持たない・色を 4 バイトで持つ)   u32 色[色の数]
  コマごと:
    i32 画素の数 n, 帯の数 m
    帯があれば: 帯の色 (色の一覧があれば u8 の番号・無ければ u32)
    u8 画素の位置の幅 (2 / 4)   位置の差分 [n] (位置 = y * W + x。 1 つ前の位置との差・最初は 0 からの差)
    画素の色 [n] (u8 の番号 / u32)
    u8 帯の位置の幅 (2 / 4)   帯の始まりの差分 [m]   u16 帯の長さ [m]
"""
import io, json, os, struct, sys, zlib
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SRC = os.path.join(HERE, "sheets")
DEST = os.path.join(PROJ, "Assets", "Resources", "Game2DFx")
RUN_MIN = 4          # FxSheet.RunMin


def _first_reach(vals, top_n):
    """C# の走査と同じ「一番多い色」: 数が最大の色のうち、 最大の数に先に届いた色 (if (n > topN) で入れ替える規則)。"""
    u, inv, cnt = np.unique(vals, return_inverse=True, return_counts=True)
    cands = np.nonzero(cnt == top_n)[0]
    if len(cands) == 1: return int(u[cands[0]])
    best, best_i = None, None
    for c in cands:
        where = np.nonzero(inv == c)[0]
        i = where[top_n - 1]
        if best_i is None or i < best_i: best, best_i = int(u[c]), i
    return best


def _frame(px, rect, src, W):
    """コマ 1 枚 → (画素の位置[], 画素の色[], 帯の始まり[], 帯の長さ[], 帯の色 or None)。 位置は y * W + x (コマの中)。"""
    x0, y0, w, h = rect["x"], rect["y"], rect["w"], rect["h"]
    ox, oy = (src["x"], src["y"]) if src else (0, 0)
    sub = px[y0:y0 + h, x0:x0 + w]
    opaque = sub[:, :, 3] > 0
    val = (np.uint32(255) << 24) | (sub[:, :, 2].astype(np.uint32) << 16) | (sub[:, :, 1].astype(np.uint32) << 8) | sub[:, :, 0].astype(np.uint32)
    ys, xs = np.nonzero(opaque)                      # 行の順 (C# の走査と同じ並び)
    vals = val[ys, xs]
    n_op = len(vals)
    lin = (oy + ys).astype(np.int64) * W + (ox + xs)
    if n_op == 0:
        return lin, vals, None, None, None
    top_n = int(np.unique(vals, return_counts=True)[1].max())
    if top_n >= RUN_MIN:
        top = _first_reach(vals, top_n)
        m = opaque & (val == top)
        # 行ごとの最大の連なり (端に 0 を足して、 立ち上がりと立ち下がりを拾う)
        pad = np.zeros((h, w + 2), dtype=np.int8); pad[:, 1:-1] = m
        d = np.diff(pad, axis=1)
        ry, rs = np.nonzero(d == 1); _, re = np.nonzero(d == -1)
        ln = re - rs
        keep = ln >= RUN_MIN
        ry, rs, ln = ry[keep], rs[keep], ln[keep]
        if len(ry) > 0:
            in_run = np.zeros((h, w + 1), dtype=np.int32)
            np.add.at(in_run, (ry, rs), 1); np.add.at(in_run, (ry, rs + ln), -1)
            in_run = np.cumsum(in_run, axis=1)[:, :w] > 0
            pts = ~in_run[ys, xs]
            n_pts = int(pts.sum())
            if n_pts * 8 + len(ry) * 8 < n_op * 8:
                rlin = (oy + ry).astype(np.int64) * W + (ox + rs)
                return lin[pts], vals[pts], rlin, ln.astype(np.uint16), top
    return lin, vals, None, None, None


def _deltas(lin):
    d = np.diff(np.concatenate([[0], lin])).astype(np.int64)
    if len(d) and d.min() < 0: raise ValueError("位置が昇順でない")
    if len(d) == 0 or d.max() < 65536: return b"\x02" + d.astype("<u2").tobytes()
    return b"\x04" + d.astype("<u4").tobytes()


def bake(name, src=SRC, dest=DEST):
    js = json.load(io.open(os.path.join(src, name + ".json"), encoding="utf-8"))
    p = js["meta"]["pixelfx"]
    W, H = p["frameSize"]; PX, PY = p["pivot"]
    fps = max(1, int(p["fps"])); loop = bool(p.get("loop", False))
    px = np.array(Image.open(os.path.join(src, name + ".png.bytes")).convert("RGBA"))
    frames = [_frame(px, f["frame"], f.get("spriteSourceSize"), W) for f in js["frames"]]
    # 色の一覧: コマの順に、 画素の色 → 帯の色 の初出順 (FxSheet.cs と同じ番号の振り方)
    pal = {}
    for lin, vals, rl, rn, top in frames:
        if len(vals):
            u, first = np.unique(vals, return_index=True)
            for v in u[np.argsort(first)]: pal.setdefault(int(v), len(pal))
        if rl is not None: pal.setdefault(int(top), len(pal))
    use_pal = len(pal) <= 256
    out = io.BytesIO()
    out.write(struct.pack("<5iBii", W, H, PX, PY, fps, 1 if loop else 0, len(frames), len(pal) if use_pal else 0))
    if use_pal:
        inv = np.zeros(len(pal), dtype="<u4")
        for v, i in pal.items(): inv[i] = v
        out.write(inv.tobytes())
        lut = {v: i for v, i in pal.items()}
    for lin, vals, rl, rn, top in frames:
        m = 0 if rl is None else len(rl)
        out.write(struct.pack("<ii", len(lin), m))
        if m: out.write(struct.pack("<B", lut[int(top)]) if use_pal else struct.pack("<I", int(top)))
        out.write(_deltas(lin))
        if use_pal:
            u, idx = np.unique(vals, return_inverse=True)
            out.write(np.array([lut[int(v)] for v in u], dtype=np.uint8)[idx].tobytes())
        else:
            out.write(vals.astype("<u4").tobytes())
        if m:
            out.write(_deltas(rl)); out.write(rn.astype("<u2").tobytes())
    raw = out.getvalue()
    co = zlib.compressobj(9, zlib.DEFLATED, -15)
    body = co.compress(raw) + co.flush()
    data = b"FXB1" + struct.pack("<I", len(raw)) + body
    check(name, data, px, js)
    io.open(os.path.join(dest, name + ".fxb.bytes"), "wb").write(data)
    return len(data), len(raw)


def check(name, data, px, js):
    """書いた .fxb を FxSheet.FromFxb と同じ手順で読み戻し、 コマごとの不透明な画素 (位置と色) が PNG と同じかを確かめる。 違えば止まる。"""
    raw = zlib.decompress(data[8:], -15)
    if len(raw) != struct.unpack_from("<I", data, 4)[0]: raise SystemExit("%s: 解凍後の長さが違う" % name)
    W, H, PX, PY, fps, loop, nf, npal = struct.unpack_from("<5iBii", raw, 0); o = struct.calcsize("<5iBii")
    pal = np.frombuffer(raw, "<u4", npal, o) if npal else None; o += 4 * npal
    if nf != len(js["frames"]): raise SystemExit("%s: コマ数が違う" % name)
    def pos(o, n):
        wide = raw[o]; o += 1
        d = np.frombuffer(raw, "<u2" if wide == 2 else "<u4", n, o).astype(np.int64)
        return np.cumsum(d), o + n * wide
    for k, f in enumerate(js["frames"]):
        n, m = struct.unpack_from("<ii", raw, o); o += 8
        runcol = None
        if m:
            if npal: runcol = int(pal[raw[o]]); o += 1
            else: runcol = struct.unpack_from("<I", raw, o)[0]; o += 4
        lin, o = pos(o, n)
        if npal: col = pal[np.frombuffer(raw, np.uint8, n, o)]; o += n
        else: col = np.frombuffer(raw, "<u4", n, o); o += 4 * n
        got = np.zeros(W * H, np.uint64)                     # 0 = 透明 (色は必ず 255 << 24 を含む)
        got[lin] = col
        if m:
            rl, o = pos(o, m); ln = np.frombuffer(raw, "<u2", m, o); o += 2 * m
            cnt = np.zeros(W * H + 1, np.int64); np.add.at(cnt, rl, 1); np.add.at(cnt, rl + ln, -1)
            inrun = np.cumsum(cnt)[:W * H] > 0
            if (got[inrun] != 0).any(): raise SystemExit("%s: コマ %d の帯と画素が重なる" % (name, k))
            got[inrun] = runcol
        r, s = f["frame"], f.get("spriteSourceSize") or {"x": 0, "y": 0}
        exp = np.zeros((H, W), np.uint64)
        sub = px[r["y"]:r["y"] + r["h"], r["x"]:r["x"] + r["w"]]
        v = (np.uint64(255) << np.uint64(24)) | (sub[:, :, 2].astype(np.uint64) << np.uint64(16)) | (sub[:, :, 1].astype(np.uint64) << np.uint64(8)) | sub[:, :, 0].astype(np.uint64)
        v[sub[:, :, 3] == 0] = 0
        exp[s["y"]:s["y"] + r["h"], s["x"]:s["x"] + r["w"]] = v
        if not np.array_equal(exp.reshape(-1), got): raise SystemExit("%s: コマ %d の画素が PNG と違う" % (name, k))
    if o != len(raw): raise SystemExit("%s: 長さが合わない" % name)


def names(src=SRC):
    return sorted(f[:-len(".png.bytes")] for f in os.listdir(src) if f.endswith(".png.bytes"))


def main(argv):
    pre = argv[1:]
    tot = 0
    for n in names():
        if pre and not any(n.startswith(q) for q in pre): continue
        sz, raw = bake(n)
        tot += sz
        print("%-26s %8d B (解凍後 %9d B) 読み戻して PNG と一致" % (n, sz, raw))
    print("合計 %.2f MB" % (tot / 1e6))


if __name__ == "__main__":
    main(sys.argv)
