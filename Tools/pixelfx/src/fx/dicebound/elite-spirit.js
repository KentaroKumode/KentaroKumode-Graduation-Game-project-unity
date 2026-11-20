// DICE BOUND 用 (2026-11-18・精鋭のオーラの候補): fire/spirit-flame の写し。 式は元のまま、 形を決めている数に倍率 K (`scale`) を掛けた。
//   掛けた数: 炎の高さ・幅、 波の振れ (波の細かさは 1/K)、 光の半径、 ちぎれる渦と冷たい火花の位置・速さ・半径。 粒の数は ×K。
//   1 本の霊火を、 敵の両肩の後ろに 2 本 (左右で時刻をずらす) 置く。 位置は敵の形 (肩の高さの左右の端) から決める。
//   変えた所: 中を上る明るい筋と火花を 1 ドットから 2 ドットに (大きくすると細すぎるため)。 尺は元の 3 秒のまま (24 コマで 72 コマ)。
// The layered tongue, its noise-torn edges and the shed puffs are adapted from Sprite Fusion's open-source fire
// (https://www.spritefusion.com/pixel-art-vfx/fire), MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, mix, WHITE, TAU } from "../../core/index.js";
import { noiseFor, hotFirst, looped, drift, soft } from "../fire/_lib.js";
import { shape } from "../status/_lib.js";

export const meta = {
  name: "Elite spirit flames (DICE BOUND)",
  desc: "Two cold ghost fires scaled up behind an elite foe's shoulders, hollow-hearted, shedding curls and cold sparks.",
  tags: ["dicebound", "elite", "aura", "spirit", "loop"],
  size: [480, 270],
  origin: [240, 204],
  duration: 3,
  loop: true,
  fps: 24,
  bg: "dark",
  actor: 2,
  options: {
    palette: ["#040a16", "#08203a", "#0b3a52", "#0f5e6e", "#16908e", "#2cc4b4", "#7ef0dc", "#c8fff2", "#ffffff"],
    seed: 7, scale: 1.9, shoulder: 0.32, inset: 0.16,   // shoulder = 肩の高さ (体の上からの割合)・inset = 端から内へ寄せる割合 (体の幅に対して)
  },
};

const D = meta.duration, NL = noiseFor(D);
const GHOST = [[1, 1, 1], [0.8, 0.92, 3], [0.6, 0.78, 6], [0.3, 0.42, 2], [0.14, 0.22, 0]];

// 元の ghostTongue。 r は画面の行、 rr = r / K が元の行。 centre は元の単位で返すので K を掛ける
function ghostTongue(p, x, y, h, w, centre, N, n, H, layers, K, jit = 0.5) {
  for (const [lw, lh, ri] of layers) {
    const L = h * lh, c = H[ri];
    for (let r = 0; r <= L; r++) {
      const u = r / Math.max(1, L), v = r / h, rr = r / K;
      const half = w * lw * Math.pow(1 - u, 0.75) * Math.min(1, (rr + 1.5) / 4) + N(6, n, rr * 0.4) * jit * u * K;
      if (half < 0.3) continue;
      const cx = x + centre(v, rr) * K, Y = Math.round(y - r);
      for (let px = Math.round(cx - half), x1 = Math.round(cx + half); px <= x1; px++) p.px(px, Y, c);
    }
  }
}

function wisp(p, t, o, X, Y, K, S) {
  const F = fit(o.palette, 9), H = hotFirst(F), ph = t / D;
  const N = (rate, seed = 0, off = 0) => NL(t, rate, seed, off);
  const COLD = mix(H[3], WHITE, 0.15), bob = Math.sin(ph * TAU) * 1.5 * K;
  const lean = N(0.67, S + 2) * 3;
  const wave = (v, r, k = 0) => (Math.sin(r * 0.24 - ph * TAU * 2 + k) * 2.2 + Math.sin(r * 0.11 - ph * TAU + k * 0.5) * 3.2) * v * v + lean * v * v;   // 元の単位
  const H0 = 44 * K * (0.92 + 0.08 * N(1.3, S + 3)), y0 = Y + bob, h0 = H0 / K;
  const br = 0.9 + 0.1 * N(2, S + 4);
  p.glow(X, y0 - 16 * K, 60 * K, COLD, 0.36 * br);
  p.glow(X + wave(0.5, 20) * 0.5 * K, y0 - 20 * K, 26 * K, H[2], 0.2 * br);
  looped(t, D, Math.round(40 * K), S + 300, (r) => ({ x: r.range(-4, 4), y: -h0 * r.range(0.55, 0.95), vx: r.range(-7, 7), vy: -r.range(10, 22), life: r.range(1, 1.9), s: r.range(1.4, 2.8), a: r.range(0, TAU), w: r.range(2, 3.5) }), (q, age) => {
    const [x, y] = drift((q.x + wave(0.9, 26, 0) * 0.5) * K, q.y * K + y0 - Y, q.vx * K, q.vy * K, age, 0, -6 * K, 1.2, 0.6), k = 1 - age / q.life;
    const cx = X + x + Math.sin(age * q.w + q.a) * 3 * K * (age + 0.2), a = Math.min(1, age * 5) * k, yy = Y + y;
    soft(p, cx, yy, q.s * K * (0.8 + age * 0.8), H[4], 0.35 * a);
    soft(p, cx - 0.4 * K, yy - 0.6 * K, q.s * K * 0.5 * (0.8 + age * 0.5), H[2], 0.4 * a * k);
    p.glow(cx, yy, 6 * K, H[3], 0.15 * a);
  });
  const wl = Math.max(0, 0.25 + 0.55 * N(1.7, S + 6)), wr = Math.max(0, 0.25 + 0.55 * N(1.7, S + 7));
  if (wl > 0.12) ghostTongue(p, X - 4 * K, y0 - 4 * K, H0 * wl, 2.4 * K, (v, r) => wave(v, r, 2.1) - 6 * v * v + 3 * Math.sin(v * 5 - ph * TAU * 2) * v, N, S * 11 + 1, H, GHOST, K);
  if (wr > 0.12) ghostTongue(p, X + 4.2 * K, y0 - 4 * K, H0 * wr, 2.4 * K, (v, r) => wave(v, r, -1.8) + 6 * v * v - 3 * Math.sin(v * 5 - ph * TAU * 2 + 1) * v, N, S * 11 + 2, H, GHOST, K);
  ghostTongue(p, X, y0, H0, 8.6 * K, (v, r) => wave(v, r), N, S * 11, H, GHOST, K, 0.6);
  // bright strands drifting up through the hollow heart (2 px wide)
  for (const k of [0, 1]) {
    for (let r = 4 * K; r < H0 * 0.72; r++) {
      const rr = r / K, v = rr / h0, x = (wave(v, rr) + Math.sin(rr * 0.35 - ph * TAU * 3 + k * Math.PI) * 2.2 * (1 - v)) * K, on = Math.sin(rr * 0.2 - ph * TAU * 4 + k * 2) > 0.2;
      if (on) (p.px(X + x, y0 - r, k ? H[2] : H[1]), p.px(X + x + 1, y0 - r, k ? H[2] : H[1]));
    }
  }
  for (const v of [0.2, 0.45, 0.7]) p.glow(X + wave(v, h0 * v) * K, y0 - H0 * v, 12 * K, H[2], 0.16 * br);
  p.glow(X, y0 - 4 * K, 9 * K, H[1], 0.22 + 0.06 * N(3, S + 5));
  // cold sparks rising off it, twinkling (2×2, a cross when they flash)
  looped(t, D, Math.round(34 * K), S + 400, (r) => ({ x: r.range(-7, 7), y: -r.range(4, 28), vx: r.range(-12, 12), vy: -r.range(14, 34), life: r.range(0.9, 1.8), a: r.range(0, TAU) }), (q, age) => {
    const [x, y] = drift(q.x * K, q.y * K + y0 - Y, q.vx * K, q.vy * K, age, 0, -10 * K, 1.2, 1.2), k = 1 - age / q.life, tw = 0.5 + 0.5 * Math.sin(age * 14 + q.a);
    const sx = X + x + Math.sin(age * 3 + q.a) * 3 * K, sy = Y + y;
    if (k * (0.4 + 0.6 * tw) < 0.2) return;
    const c = tw > 0.7 && k > 0.5 ? H[0] : k > 0.5 ? H[1] : H[3];
    p.px(sx, sy, c); p.px(sx + 1, sy, c); p.px(sx, sy + 1, c); p.px(sx + 1, sy + 1, c);
    if (tw > 0.85 && k > 0.6) for (const [dx, dy] of [[-1, 0], [-2, 0], [2, 0], [3, 0], [0, -1], [0, -2], [0, 2], [0, 3]]) p.px(sx + dx, sy + dy, H[3]);
    p.glow(sx, sy, 4 * K, H[2], 0.3 * k);
  });
}

function spots(o) {
  // 体の左右の端 (体の高さの shoulder〜shoulder+0.3 の行で一番外) から、 体の幅 × inset だけ内へ。 高さは shoulder + 0.15
  const a = o.actor, S = shape(a);
  let l = S.w, r = 0;
  for (let y = Math.round(S.h * o.shoulder); y < Math.round(S.h * (o.shoulder + 0.3)); y++) { const [L, R] = S.rows[y]; if (L >= 0) (l = Math.min(l, L)), (r = Math.max(r, R)); }
  const my = Math.round(S.h * (o.shoulder + 0.15)), inset = (r - l) * o.inset;
  return [[a.x + l + inset, a.y + my, 0], [a.x + r - inset, a.y + my, 1]];
}

export function drawBack(p, t, o) {
  const K = o.scale;
  for (const [x, y, i] of spots(o)) wisp(p, (t + i * 1.25) % D, o, x, y, K, o.seed + i * 17);
}

export function draw() {}
