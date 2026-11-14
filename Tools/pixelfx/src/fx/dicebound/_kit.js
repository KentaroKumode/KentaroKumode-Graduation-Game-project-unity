// DICE BOUND 用 (2026-11-14): ボスの技を、 このゲームの画面 (480×270) の大きさで描くための部品。
// 元の効果は横スクロールの小さい人物に合わせた大きさ (高さ 30〜60 ドット) で、 そのまま置くと小さすぎる (作者の指摘)。 拡大はしない (ドットの大きさが揃わなくなる) ──
// 大きい形を、 はじめからこの大きさで描く。 粒 (火花・残り火・煙・灰・星) は core の Particles の種類を、 技ごとに組み合わせて出す。
// 形の描き方は、 元の効果から学んだ物: 光の槍 = transitions/slash-wipe の一閃・炸裂 = impacts/crit-hit の星形・印 = shadow/hex-sigil・三日月 = melee の斬撃・輪 = fire/fire-ring の band。
import { clamp, smooth, ease, TAU, WHITE } from "../../core/index.js";
import { smear, trace, glowAlong } from "../melee/_lib.js";

export const W = 480, H = 270, CHEST = [240, 130], FOOT = [240, 196], HEAD = [240, 60], BOARD = [240, 247];

/** 光の槍 / 光線: (x0,y0) → (x1,y1)。 幅は w0 → w1 (こちらへ近いほど太く、 が基本)。 白い芯・明るい縁・淡いにじみ。 */
export function beam(p, C, x0, y0, x1, y1, w0, w1, k = 1) {
  if (Math.hypot(x1 - x0, y1 - y0) < 2 || k <= 0) return;   // まだ伸びていない
  const dx = x1 - x0, dy = y1 - y0, L = Math.hypot(dx, dy) || 1, ux = dx / L, uy = dy / L, pad = Math.max(w0, w1) * 0.9 + 2;
  p.shade(Math.min(x0, x1) - pad, Math.min(y0, y1) - pad, Math.max(x0, x1) + pad, Math.max(y0, y1) + pad, (x, y, o) => {
    const al = (x - x0) * ux + (y - y0) * uy;
    if (al < 0 || al > L) return;
    const hw = Math.max(0.5, (w0 + ((w1 - w0) * al) / L) / 2), d = Math.abs(-(x - x0) * uy + (y - y0) * ux) / hw;
    if (d > 1.7) return;
    if (d <= 1.15) { o.c = d < 0.45 ? WHITE : d < 0.8 ? C[6] : C[5]; o.a = k; }
    else { o.c = C[4]; o.a = 0.5 * k; }
    o.l = C[5]; o.k = 0.55 * k * (1 - d / 1.7);
  });
}

/** 星形の炸裂: n 本の棘。 中心は白く、 縁へ色が落ちる。 R = 棘の長さ・rot = 向き・sy = 縦の潰し (床に寝かせる時 < 1)。 */
export function starburst(p, C, x0, y0, R, n, rot, k = 1, sy = 1) {
  if (R < 2 || k <= 0) return;
  const sec = TAU / n;
  p.shade(x0 - R - 1, y0 - R * sy - 1, x0 + R + 1, y0 + R * sy + 1, (x, y, o) => {
    const dx = x - x0, dy = (y - y0) / sy, d = Math.hypot(dx, dy);
    if (d > R) return;
    const a = (((Math.atan2(dy, dx) - rot) % sec) + sec) % sec, tri = Math.abs(a / sec - 0.5) * 2, rr = R * (0.22 + 0.78 * Math.pow(tri, 2.4));   // 棘の真ん中で tri = 1
    if (d > rr) return;
    const u = d / rr;
    o.c = u < 0.5 ? WHITE : u < 0.78 ? C[6] : C[5]; o.a = k;
    o.l = C[5]; o.k = 0.5 * k;
  });
}

/** 楕円の輪 (床に寝た輪・盤を囲む輪)。 a0〜a1 の範囲だけ描ける (走って描かれる輪)。 */
export function oval(p, x0, y0, rx, ry, th, c, k = 1, a0 = 0, a1 = TAU) {
  if (rx < 1 || k <= 0) return;
  const n = Math.ceil(rx * 3.2 * Math.abs(a1 - a0) / TAU) + 2;
  for (let j = 0; j <= n; j++) {
    const g = a0 + ((a1 - a0) * j) / n, cx = Math.cos(g), sy = Math.sin(g);
    for (let d = 0; d < th; d++) p.dot(x0 + cx * (rx - d), y0 + sy * (ry - d * (ry / rx)), c, k);
  }
}

/** 三日月の斬撃: 中心 (cx,cy)・半径 R の弧 a0→a1。 真ん中が一番太い (w)。 外の縁が白く、 内へ色が落ちる。 prog = 0〜1 で、 弧のどこまで描くか。 sy = 縦の潰し。 */
export function crescent(p, C, cx, cy, R, a0, a1, w, k = 1, prog = 1, sy = 1) {
  const n = Math.ceil(R * Math.abs(a1 - a0) * 1.6) + 2, to = n * clamp(prog);
  for (let j = 0; j <= to; j++) {
    const u = j / n, g = a0 + (a1 - a0) * u, wd = w * Math.pow(Math.sin(Math.PI * u), 0.7), c = Math.cos(g), s = Math.sin(g);
    for (let d = 0; d <= wd; d += 0.6) {
      const q = d / Math.max(1, wd);
      p.dot(cx + c * (R - d), cy + s * (R - d) * sy, q < 0.3 ? WHITE : q < 0.6 ? C[6] : q < 0.85 ? C[5] : C[4], k * (q < 0.85 ? 1 : 0.6));
    }
    if (j % 5 === 0) p.glow(cx + c * R, cy + s * R * sy, 5 + wd * 0.5, C[5], 0.25 * k);
  }
}

/**
 * 印: 2 重の輪・その間の文様・中に n 点の星 (step つ飛ばしで結ぶ)・中心の目。 prog = 0〜1 で、 輪 → 星 → 文様の順に描かれる。 hot = 白く燃える (0〜1)。
 * sy = 縦の潰し (盤の上に寝かせる)。 線は 2 ドット。
 */
export function sigil(p, C, x0, y0, R, sy, prog, hot = 0, n = 7, step = 3, rot = -Math.PI / 2) {
  const col = hot > 0.4 ? WHITE : C[6], dim = hot > 0.4 ? WHITE : C[4], ring = clamp(prog / 0.35), star = clamp((prog - 0.3) / 0.45), gl = clamp((prog - 0.7) / 0.3);
  oval(p, x0, y0, R, R * sy, 2, col, 1, rot, rot + TAU * ring);
  oval(p, x0, y0, R * 0.84, R * 0.84 * sy, 1, dim, 1, rot + TAU, rot + TAU - TAU * ring);
  const pt = (i) => [x0 + Math.cos(rot + (i * TAU) / n) * R * 0.82, y0 + Math.sin(rot + (i * TAU) / n) * R * 0.82 * sy];
  for (let i = 0; i < n; i++) {
    const u = clamp(star * n - i);
    if (u <= 0) break;
    const [ax, ay] = pt(i * step), [bx, by] = pt((i + 1) * step), ex = ax + (bx - ax) * u, ey = ay + (by - ay) * u;
    p.line(ax, ay, ex, ey, col); p.line(ax, ay + 1, ex, ey + 1, dim);
    if (u < 1) p.glow(ex, ey, 6, WHITE, 0.6);
  }
  // 輪の間の文様: 小さな印を n*2 個
  for (let i = 0; i < n * 2 * gl; i++) {
    const g = rot + ((i + 0.5) * TAU) / (n * 2), gx = x0 + Math.cos(g) * R * 0.92, gy = y0 + Math.sin(g) * R * 0.92 * sy;
    p.dot(gx, gy, col); p.dot(gx + 1, gy, col); p.dot(gx, gy - 1, i % 2 ? col : dim); if (i % 3 === 0) p.dot(gx - 1, gy + 1, dim);
  }
  if (star >= 1) { p.circle(x0, y0, 3, col); p.circle(x0, y0, 1.2, C[1]); }
  if (hot > 0) p.glow(x0, y0, R * 0.9, C[5], 0.5 * hot);
}

/** 画面の一部を光で満たす (盤が照らされる・当たりの余光)。 x の中心から離れるほど弱い。 */
export function wash(p, c, x0, y0, x1, y1, k) {
  if (k <= 0.01) return;
  const mx = (x0 + x1) / 2, hw = (x1 - x0) / 2;
  for (let y = y0; y < y1; y += 2) for (let x = x0 + (y & 2 ? 1 : 0); x < x1; x += 2) p.add(x, y, c, k * (1 - Math.abs(x - mx) / hw) ** 0.7);
}

/** 時刻 at に 1 回だけ fn を呼ぶための小道具: const once = onces(); once("burst", t >= 0.1, () => …) */
export function onces() {
  const done = new Set();
  return (key, when, fn) => { if (when && !done.has(key)) { done.add(key); fn(); } };
}

/** 斬撃: (x0,y0) → (x1,y1) へ、 bow だけ反った弧。 真ん中が一番太い (w)。 片側の縁が白く、 反対へ色が落ちる。 prog = 0〜1 で、 どこまで描くか。 */
export function slash(p, C, x0, y0, x1, y1, bow, w, k = 1, prog = 1) {
  if (k <= 0 || w < 0.5) return;
  const dx = x1 - x0, dy = y1 - y0, L = Math.hypot(dx, dy) || 1, nx = -dy / L, ny = dx / L, mx = (x0 + x1) / 2 + nx * bow, my = (y0 + y1) / 2 + ny * bow, n = Math.ceil(L * 1.6), to = n * clamp(prog);
  for (let j = 0; j <= to; j++) {
    const u = j / n, a = 1 - u, x = a * a * x0 + 2 * a * u * mx + u * u * x1, y = a * a * y0 + 2 * a * u * my + u * u * y1, wd = w * Math.pow(Math.sin(Math.PI * u), 0.6);
    for (let d = -wd * 0.35; d <= wd * 0.65; d += 0.6) {
      const q = (d + wd * 0.35) / Math.max(1, wd);
      p.dot(x + nx * d, y + ny * d, q < 0.3 ? WHITE : q < 0.6 ? C[6] : q < 0.85 ? C[5] : C[4], k * (q < 0.85 ? 1 : 0.6));
    }
    if (j % 6 === 0) p.glow(x, y, 5 + wd * 0.5, C[5], 0.25 * k);
  }
}

/** 斬撃の上の点 (火花を出す位置などに使う)。 u = 0〜1。 */
export function slashAt(x0, y0, x1, y1, bow, u) {
  const dx = x1 - x0, dy = y1 - y0, L = Math.hypot(dx, dy) || 1, mx = (x0 + x1) / 2 - (dy / L) * bow, my = (y0 + y1) / 2 + (dx / L) * bow, a = 1 - u;
  return [a * a * x0 + 2 * a * u * mx + u * u * x1, a * a * y0 + 2 * a * u * my + u * u * y1];
}

/** 当たりの閃光: 横に長い光の玉 (白い芯・明るい縁) と、 その中心を通る細い横の光の筋。 星形 (棘の炸裂) は使わない ── ×や四角に見えて安っぽい (2026-11-14 作者の指摘)。 */
export function lens(p, C, x0, y0, rx, ry, k = 1) {
  if (rx < 2 || k <= 0) return;
  p.shade(x0 - rx - 1, y0 - ry - 1, x0 + rx + 1, y0 + ry + 1, (x, y, o) => {
    const d = Math.hypot((x - x0) / rx, (y - y0) / ry);
    if (d > 1) return;
    o.c = d < 0.5 ? WHITE : d < 0.8 ? C[6] : C[5]; o.a = k; o.l = C[5]; o.k = 0.5 * k;
  });
  p.glow(x0, y0, rx * 1.3, C[5], 0.4 * k);
  for (let x = -rx * 2.2; x <= rx * 2.2; x++) { const u = Math.abs(x) / (rx * 2.2); p.dot(x0 + x, y0, u < 0.5 ? WHITE : C[6], k * (1 - u)); }
}

/** 小さな菱形 (宝石・楔の頭)。 */
export function diamond(p, x0, y0, r, c, rim) {
  for (let dy = -r; dy <= r; dy++) for (let dx = -(r - Math.abs(dy)); dx <= r - Math.abs(dy); dx++) p.dot(x0 + dx, y0 + dy, Math.abs(dx) + Math.abs(dy) === r ? rim : c);
}

/**
 * 大きな斬撃 (melee/sword-slash の作りを、 この画面の大きさで)。 s = melee/_lib の swing に { span, W, t0, sweep, hold, trail, ramp (暗い → 白の 7 色) }。
 * 元の slash() との違いは塗りだけ: 元は太さ 12 ドットで、 色の段を幅の割合で決めている。 それを太くすると、 同じ幅の帯が並ぶ縞になる (作者: 等比グラデで安っぽい)。
 * 元の見え方は「外の縁の白が 1〜2 ドット・淡い胴が広い・内の縁だけ暗い 2 ドット」なので、 **縁はドットの幅で決める**:
 *   外の縁 = 白 2.5 ドット / 胴 = 淡い色 (内寄りの 4 割は 1 段深い) / 内の縁 = 暗い色 2 ドット。
 * 時間が経つと 1 段ずつ冷め (白が消え、 胴が深くなる)、 尾 (後ろ 1/4) も 1 段冷める。 尾は網点でほどける。 頭のすぐ後ろが一番太いのは元と同じ。
 */
export function bladeSlash(p, s, t, half = 0) {
  const u = t - s.t0;
  if (u < 0) return;
  const R = s.ramp, n = R.length, LV = [R[n - 1], R[n - 2], R[n - 3], R[n - 4], R[Math.max(0, n - 6)]];   // 白・淡・中・深・暗
  const life = s.sweep + s.hold + s.trail, head = ease.outExpo(clamp(u / s.sweep)), tail = smooth(s.sweep + s.hold, life, u), age = clamp(u / life);
  const U = s.span * head, T = s.span * tail, W = s.W * (1 - tail * 0.55), cool = (age > 0.3 ? 1 : 0) + (age > 0.55 ? 1 : 0) + (age > 0.8 ? 1 : 0);
  if (tail < 1 && U - T > 0.01) {
    smear(p, s, T, U, W, (uu, r, out) => {
      const v = (uu - T) / (U - T), w = W * Math.pow(Math.sin(Math.PI * Math.pow(v, 2.2)), 0.6);
      if (w < 0.35) return;
      const d = s.R - r;   // 外の縁からのドット数
      if (d < 0 || d > w) return;
      const lv = d < 2.5 ? 0 : d > w - 2 && w > 6 ? 3 : d < 2.5 + (w - 4.5) * 0.6 ? 1 : 2;
      out.c = LV[Math.min(4, lv + cool + (v < 0.25 ? 1 : 0))];
      out.a = Math.min(1, v / 0.4);
    }, half);
    glowAlong(p, s, s.R - W * 0.4, T, U, 6 + W * 0.5, R[n - 3], (uu) => 0.24 * (1 - age) * ((uu - T) / (U - T)), half, 0.3);
  }
  // 縁の点線: 斬撃が通り過ぎた所に残り、 しばらくして消える (元と同じ。 残る長さは s.linger 秒・無ければ元の trail + 0.2)。 大きい斬撃では、 外の縁と、 そのすぐ内の 2 本
  const th = 0.85 * (1 - clamp((u - s.sweep - s.hold) / ((s.linger ?? 0) + s.trail + (s.linger ? 0 : 0.2))));
  if (tail > 0.02 && th > 0) { trace(p, s, s.R - 0.5, 0, T, R[n - 4], (uu) => th * (0.4 + 0.6 * uu / s.span), half); if (s.linger && s.W > 8) trace(p, s, s.R - 2.5, 0, T, R[2], (uu) => th * 0.7 * (0.4 + 0.6 * uu / s.span), half, 1.2); }
}
