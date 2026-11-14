// DICE BOUND 用 (2026-11-14): 元の効果を、 **式を 1 行も書き直さずに**、 この画面の大きさで描く仕組み。
// 元の効果は横スクロールの小さい人物用 (高さ 30〜60 ドット)。 写しを作って数を掛け直す方法 (vesca-hex・vesca-slash・tenyo-sword) は 1 本ごとに手間がかかる。
// ここでは、 描画の座標の変換 (Painter の toX / toY / fromX / fromY) に倍率 K を通す。
//   面を塗る描き方 (shade / each / circle / ring / disc …) は、 画面の画素を 1 つずつ回って、 元の座標へ戻して色を決める作りなので、 K 倍でも**画面の解像度で描き直される** (拡大して貼るのと違い、 ドットは大きくならない)。
//   線 (line) は、 両端を K 倍の位置へ移してから、 画面の上で 1 ドットの線を引く。 点 (px / dot) は 1 ドットのまま、 K 倍の位置に出る。
//   光 (glow / glowLine / glowRing) は半径を画面のドットで持っているので、 半径に K を掛ける。
// 形の比率は元のまま大きくなる (色の段の幅も K 倍)。 べた塗りが目立つ物は、 写しを作って縁と筋を直す (library/dicebound/moves.md の決まり 2)。
import { resolveOptions, Bits, Particles, P, B } from "../../core/index.js";

const KEYS = ["toX", "toY", "fromX", "fromY", "cellX", "cellY", "glow", "glowLine", "glowRing", "rect"];

/** fn の中の描画を、 画面の (x, y) を原点に、 K 倍の大きさで、 rot ラジアン回して行う (rot > 0 で時計回り。 元が右向きの絵を π/2 で下向きに)。 左右反転は考えない。 */
export function withScale(p, K, x, y, fn, rot = 0, G = 1) {   // G = ぼかした光 (glow) の強さの倍率
  const tx = p.tx + x, ty = p.ty + y, P = Object.getPrototypeOf(p), c = Math.cos(rot), s = Math.sin(rot);
  p.toX = (lx, ly) => tx + (lx * c - ly * s) * K; p.toY = (lx, ly) => ty + (lx * s + ly * c) * K;
  p.fromX = (sx, sy) => ((sx - tx) * c + (sy - ty) * s) / K; p.fromY = (sx, sy) => (-(sx - tx) * s + (sy - ty) * c) / K;
  p.cellX = (lx, ly) => Math.floor(p.toX(lx, ly)); p.cellY = (lx, ly) => Math.floor(p.toY(lx, ly));
  p.glow = (gx, gy, r, c, k) => P.glow.call(p, gx, gy, r * K, c, k * G);
  p.glowLine = (x0, y0, x1, y1, r, c, k) => P.glowLine.call(p, x0, y0, x1, y1, r * K, c, k * G);
  p.glowRing = (gx, gy, R, r, c, k) => P.glowRing.call(p, gx, gy, R * K, r * K, c, k * G);
  p.rect = (rx, ry, w, h, c, a = 1) => p.shade(rx, ry, rx + w, ry + h, (_x, _y, o) => { o.c = typeof c === "function" ? c(0, 0) : c; o.a = typeof a === "function" ? a(0, 0) : a; });
  try { fn(); } finally { for (const k of KEYS) delete p[k]; }
}

/**
 * fn の間だけ、 元の効果が出す粒の一部を出させない (元のコードは触らない)。 drop = ["CHUNK" (跳ねる岩・小石), "GRIT" (砂利)] など。
 * 元の効果は、 生成の時と update の中で粒を出すので、 その両方をこれで包む。
 */
function muted(drop, fn, puff = null) {   // puff = { SMOKE: [大きさの倍率, 何個に分けるか] }: 煙などの粒を、 小さく・数を増やして出させる (大きく広げると、 煙がべたの大きな塊になるため)
  drop = drop || [];
  if (!drop.length && !puff) return fn();
  const add = Bits.prototype.add, spawn = Particles.prototype.spawn, chunk = drop.includes("CHUNK"), grit = drop.includes("GRIT");
  Bits.prototype.add = function (b) { if (chunk && b && b.kind === B.CHUNK) return undefined; return add.call(this, b); };
  const kinds = drop.filter((n) => n !== "CHUNK" && n !== "GRIT" && P[n] !== undefined).map((n) => P[n]);   // ほかの粒 ("STAR" など) も名前で止められる
  const fine = puff ? Object.entries(puff).map(([n, v]) => [P[n], v[0], v[1]]) : [];
  Particles.prototype.spawn = function (k, ...rest) {
    if ((grit && k === P.GRIT) || kinds.includes(k)) return undefined;
    const f = fine.find((q) => q[0] === k);
    if (!f) return spawn.call(this, k, ...rest);
    // 小さく分ける: 元の 1 個を、 大きさ f[1] 倍の粒 f[2] 個に (元の大きさの幅に、 決まった位置で並べる。 乱数は使わない = 元の効果の乱数の列を変えない)
    const [x, y, vx, vy, life, col, size = 1, ...more] = rest, n = f[2];
    let last;
    for (let i = 0; i < n; i++) { const u = n > 1 ? i / (n - 1) - 0.5 : 0, w = (i % 2 ? 1 : -1); last = spawn.call(this, k, x + u * size * 1.6, y + w * size * 0.35, vx * (1 + u * 0.5), vy * (1 + w * 0.18), life * (1 - 0.12 * Math.abs(u) * 2), col, size * f[1], ...more); }
    return last;
  };
  try { return fn(); } finally { Bits.prototype.add = add; Particles.prototype.spawn = spawn; }
}

/**
 * 元の効果 SRC を、 K 倍・画面の at に (元の座標の anchor が来るように)・speed 倍の速さで流す物を作る。
 * opts = 元の効果へ渡す設定 (palette など)。 返り値は create() が返す形 ({ update, draw })。
 */
export function scaledFx(SRC, { K = 3, at = [240, 136], anchor = [0, 0], speed = 1, delay = 0, life = 0, drop = null, puff = null, rot = 0, start = 0, end = 0, glow = 1, opts = {} } = {}) {   // glow = ぼかした光の強さの倍率   // start = 元の効果の何秒目から流すか (前半を飛ばす)・end = 何秒目で止めるか   // rot = 回す角度 (anchor を中心に)   // life = ループの効果を、 何秒で消すか (最後の 0.25 秒で網点で薄くなる)
  const o = resolveOptions(SRC.meta, opts), inner = SRC.create ? muted(drop, () => SRC.create(o), puff) : null, cr = Math.cos(rot), sr = Math.sin(rot), x = at[0] - (anchor[0] * cr - anchor[1] * sr) * K, y = at[1] - (anchor[0] * sr + anchor[1] * cr) * K, D = SRC.meta.duration;
  let t = -delay;
  if (start > 0) { if (inner && inner.update) muted(drop, () => { for (let q = 0; q < start; q += 1 / 120) inner.update(1 / 120); }, puff); }
  const T0 = start, END = end || D;
  return {
    done: () => t + T0 > END,
    update(dt) { const was = t; t += dt * speed; if (t > 0 && inner && inner.update) muted(drop, () => inner.update(was < 0 ? t : dt * speed), puff); },
    draw(p) {
      if (t < 0 || (!SRC.meta.loop && t + T0 > END) || (life && t > life)) return;
      const f = p.fade;
      if (life) p.fade = f * Math.min(1, t / 0.15, (life - t) / 0.25);
      withScale(p, K, x, y, () => {
        if (inner) { if (inner.drawBack) inner.drawBack(p); inner.draw(p); }
        else { if (SRC.drawBack) SRC.drawBack(p, t + T0, o); SRC.draw(p, t + T0, o); }
      }, rot, glow);
      p.fade = f;
    },
  };
}
