// DICE BOUND 用 (2026-11-18・精鋭のオーラの候補): shadow/shadow-flames の写し。 式は元のまま、 形を決めている数に倍率 K (`scale`) を掛けた。
//   掛けた数: 舌の位置・高さ・幅、 揺れの幅、 縁のノイズの振れ (ノイズの細かさは 1/K)、 光の半径、 火の粉・煙の位置・速さ・半径、 粒の数 (×K)。
//   変えた所: 煙の上る距離だけ K × rise (画面の上へ抜けるため)・煙の玉の半径は ×K×puff・煙の数は ×smoke (既定 0 = 煙なし: 大きくすると上に黒い玉が浮くため)・ちぎれる玉の半径は ×lick (×K だと風船)・舌の横の間隔は ×K×spread・ちぎれる玉と火の粉の上る距離は ×K×climb。 尺 2.4 → 2.5 秒 (1 秒 24 コマで割り切れるように)。 敵の背中から立ち上るように、 舌と煙と光を drawBack (敵の後ろ) へ、 火の粉だけ draw (前)。
//   舌の先の白い縁は 1 ドットから 2 ドットに (大きくした時に縁が細すぎるため・moves.md の決まり 2)。 火の粉の半分は 2×2 の点。
// Tongues built from nested layers, each narrower and shorter than the last, are adapted from Sprite Fusion's
// open-source fire (https://www.spritefusion.com/pixel-art-vfx/fire), MIT License, Copyright (c) 2026 Hugo Duprez.
// Here the layers run the other way: a pale skin outside, violet within, a black heart at the core.
import { cycle, fit, noise2, TAU, WHITE, mix } from "../../core/index.js";
import { VOID } from "../shadow/_lib.js";

export const meta = {
  name: "Elite shadow flames (DICE BOUND)",
  desc: "Shadow flames scaled up behind an elite foe: a black heart in pale-skinned flame rising off its back, dark smoke and embers.",
  tags: ["dicebound", "elite", "aura", "shadow", "loop"],
  size: [480, 270],
  origin: [240, 204],
  duration: 2.5,
  loop: true,
  fps: 24,
  bg: "dark",
  options: { palette: VOID, seed: 12, scale: 4.6, spread: 1.5, rise: 0.2, lick: 2, smoke: 0, puff: 0.55, climb: 0.5 },   // climb = ちぎれる玉と火の粉の上る距離の倍率 (画面の上へ抜けないように)   // puff = 煙の玉の半径の倍率 (×K だと画面の上へはみ出す)   // spread = 舌の横の間隔の倍率 (敵の体の横からのぞくように)・lick = ちぎれる炎の玉の半径の倍率 (K だと風船に見える)・smoke = 煙の数の倍率   // rise = 煙の上る距離の倍率 (K に掛ける・画面の上へ抜けないように)
};

const D = meta.duration;
const TONGUES = [[-11, 20, 4.2, 1], [11, 22, 4.4, 2], [-6, 31, 5.4, 3], [6, 29, 5.2, 4], [0, 40, 6.6, 5]];
const LAYERS = [[1, 1, 0], [0.8, 0.88, 1], [0.54, 0.68, 2], [0.32, 0.48, 3]];
const lnoise = (t, v, seed, speed = 5) => noise2(Math.cos((TAU * t) / D) * speed + v, Math.sin((TAU * t) / D) * speed, seed);

function flames(p, t, o) {
  const K = o.scale, SP = o.spread, C = fit(o.palette, 8), S = o.seed, TONE = [C[6], C[4], C[2], C[0]];
  const GX = Math.ceil(22 * K * SP + 8), GY = Math.ceil(60 * K);
  const grid = new Int8Array(GX * 2 * GY).fill(-1), set = (x, y, v) => {
    const i = Math.round(x) + GX, j = Math.round(y) + GY;
    if (i >= 0 && j >= 0 && i < GX * 2 && j < GY) grid[j * GX * 2 + i] = v;
  };
  const order = TONGUES.map((_, i) => i).sort((a, b) => Math.abs(TONGUES[b][0]) - Math.abs(TONGUES[a][0]));
  for (const i of order) {
    const [dx0, H0_, w0, n] = TONGUES[i], dx = dx0 * K * SP, H0 = H0_ * K, w = w0 * K;
    const h = H0 * (0.78 + 0.22 * lnoise(t, n * 3, n, 4)), sway = lnoise(t, n * 7, n + 9, 3) * 3 * K;
    for (const [lw, lh, tone] of LAYERS) {
      const Hl = h * lh;
      for (let r = 0; r <= Hl; r++) {
        const u = r / Math.max(1, Hl), rr = r / K;
        const half = w * lw * Math.pow(1 - u, 0.85) * Math.min(1, (rr + 1.5) / 3.5) + lnoise(t, rr * 0.45 + n, n + 20, 6) * 1.1 * K + lnoise(t, rr * 1.3 + n * 5, n + 50, 9) * 0.6 * K;
        const cx = dx + sway * u * u * lh + lnoise(t, rr * 0.2 + n * 2, n + 30, 5) * u * 1.4 * K;
        if (half < 0.3) continue;
        for (let x = Math.round(cx - half); x <= Math.round(cx + half); x++) {
          if (tone === 0 && u > 0.45 && lnoise(t, (x / K) * 0.9 + rr * 0.7 + n, n + 60, 8) > 0.35) continue;
          set(x, -r, tone);
        }
      }
    }
  }
  const at = (i, j) => (i < 0 || j < 0 || i >= GX * 2 || j >= GY ? -1 : grid[j * GX * 2 + i]);
  for (let j = 0; j < GY; j++)
    for (let i = 0; i < GX * 2; i++) {
      const v = grid[j * GX * 2 + i];
      if (v < 0) continue;
      const x = i - GX, y = j - GY;
      let c = TONE[v];
      if (v === 0 && y < -24 * K) {
        if (at(i, j - 1) < 0) c = y < -32 * K ? WHITE : C[7];
        else if (at(i, j - 2) < 0) c = C[7];
      }
      p.px(x + 0.5, y + 0.5, c);
    }
  // licks of pale flame breaking off the tips
  cycle(t, D, Math.round(34 * K * 0.6), S, (r) => r.range(0.25, 0.5) * 1.3, (age, L, r) => {
    const u = age / L, k = r.int(0, 4), [dx, H0] = TONGUES[k], x = (dx * SP + r.range(-4, 4) + u * r.range(-5, 5)) * K, y = (-H0 * r.range(0.45, 0.85) - u * r.range(10, 20) * o.climb) * K;
    const KL = o.lick, rad = (r.range(1, 2.2) * (1 - u) + 0.3) * KL;
    p.circle(x, y, rad + 0.6 * KL, u < 0.5 ? C[6] : C[4]);
    if (rad > KL) p.circle(x, y + 0.4 * KL, rad * 0.5, u < 0.3 ? C[2] : C[4]);
  });
  // light: a violet bloom round it, but none on the black heart
  const fl = 0.85 + 0.15 * lnoise(t, 0, 77, 6);
  p.glow(0, -16 * K, 52 * K, C[4], 0.5 * fl);
  p.glow(0, -12 * K, 26 * K, C[6], 0.34 * fl);
  for (let j = 0; j < GY; j++)
    for (let i = 0; i < GX * 2; i++) if (grid[j * GX * 2 + i] === 3) {
      const sx = p.cellX(i - GX + 0.5, j - GY + 0.5) - p.ox, sy = p.cellY(i - GX + 0.5, j - GY + 0.5) - p.oy;
      if (sx >= 0 && sy >= 0 && sx < p.w && sy < p.h) p.L[sy * p.w + sx] = 0;
    }
}

export function drawBack(p, t, o) {
  const K = o.scale, C = fit(o.palette, 8), S = o.seed;
  p.ellipse(0, 0.5, 22 * K * o.spread, 3.2 * K, (d) => (d < 0.6 ? C[0] : C[1]), (d) => (d < 0.85 ? 1 : (1 - d) * 6));
  p.glow(0, -2 * K, 46 * K, C[4], 0.3);
  cycle(t, D, Math.round(14 * o.smoke), S + 1, (r) => r.range(1.2, 1.9), (age, L, r) => {
    const u = age / L, x = (r.range(-6, 6) * o.spread + Math.sin(u * 4 + r.range(0, 6)) * 5 * u) * K, y = (-30 * K - u * r.range(36, 52) * K * o.rise), rad = (3 + u * 6) * K * o.puff;
    const a = Math.min(1, u * 6) * (1 - u) * 0.7;
    p.disc(x, y, rad, mix(mix(C[3], C[2], 0.5), C[1], u), a);
    p.disc(x - rad * 0.2, y - rad * 0.3, rad * 0.5, C[3], a * 0.4);
    p.disc(x, y + rad * 0.45, rad * 0.55, C[5], a * 0.45 * (1 - u));
  });
  flames(p, t, o);
}

export function draw(p, t, o) {
  const K = o.scale, C = fit(o.palette, 8), S = o.seed;
  // embers drifting up, flickering, cooling to violet (half of them 2×2)
  cycle(t, D, Math.round(30 * K * 0.7), S + 2, (r) => r.range(1, 1.8), (age, L, r) => {
    const u = age / L, x = (r.range(-12, 12) * o.spread + Math.sin(u * 6 + r.range(0, 6)) * 5 * u) * K, y = (-10 - u * r.range(40, 70) * o.climb) * K, big = r.next() < 0.5;
    if (u > 0.7 && Math.sin(age * 40 + x) < 0) return;
    const c = u < 0.3 ? WHITE : u < 0.6 ? C[7] : C[6];
    p.px(x, y, c);
    if (big && u < 0.6) (p.px(x + 1, y, c), p.px(x, y + 1, c), p.px(x + 1, y + 1, c));
    p.glow(x, y, 3 * K * 0.6, C[6], 0.35 * (1 - u));
  });
}
