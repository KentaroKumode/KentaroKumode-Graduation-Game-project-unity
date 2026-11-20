// DICE BOUND 用 (2026-11-18・精鋭のオーラの候補): status/cursed の写し。 式は元のまま、 形を決めている数に倍率 K (`scale`) を掛けた。
//   残した物: 体の両脇から巻きながら立ちのぼる煙の帯・足もとの染み (縁が光る)・体のまわりの淡い光。
//   外した物: 体を下から染める紫 (敵の絵を塗りつぶすので。 精鋭の色は色替えの方で出す)・燃える目・頭の上のどくろ (呪いの状態異常と紛れる)。
//   掛けた数: 煙の半径・上る速さ・揺れ・外へ流れる幅、 染みの横の広がり・太さ (1 ドットの段 → K ドットの段)、 光の半径。 煙の出どころは体の高さの割合なので、 そのまま。
//   変えた所: 尺 2.4 → 2.5 秒 (1 秒 24 コマで割り切れるように)。 煙の出どころを 4 → 6 か所 (体が大きいので腰の高さにも)。
// The soft lit smoke puffs are
// adapted from Sprite Fusion's open-source poison (https://www.spritefusion.com/pixel-art-vfx/poison),
// MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, rand, mix, smooth, TAU } from "../../core/index.js";
import { shape } from "../status/_lib.js";

export const meta = {
  name: "Elite cursed smoke (DICE BOUND)",
  desc: "Cursed smoke scaled up round an elite foe: winding trails rise off its sides and a dark stain with a glowing rim pools at its feet.",
  tags: ["dicebound", "elite", "aura", "curse", "loop"],
  size: [480, 270],
  origin: [240, 204],
  duration: 2.5,
  loop: true,
  fps: 24,
  bg: "dark",
  actor: 2,
  options: { palette: ["#0c0612", "#1f0f2e", "#3a1a55", "#5e2a85", "#8a3fc0", "#b86ef0", "#e0b0ff", "#ffffff"], seed: 9, scale: 2.6 },
};

const D = meta.duration;
const SPOTS = [[0.12, -1], [0.1, 1], [0.5, -1], [0.48, 1], [0.74, -1], [0.72, 1]];

function trails(p, C, a, S, t, K, front) {
  SPOTS.forEach(([q, side], j) => {
    if ((j % 2 === 0) !== front) return;
    const my = Math.round(S.h * q), [l, r] = S.rows[my] ?? [0, 0];
    const x0 = a.x + (side < 0 ? l : r + 1), y0 = a.y + my, life = 1.6, n = 18;
    for (let i = n - 1; i >= 0; i--) {
      const age = (((t - (i / n) * D - j * 0.37) % D) + D) % D, tb = t - age, u = age / life;
      if (u >= 1) continue;
      const sway = Math.sin((tb / D) * TAU * 2 + j * 1.7) * 4 * K * Math.min(1, u * 1.6);
      const x = x0 + side * u * 7 * K + sway + (rand(i, j, 71) - 0.5) * 2.5 * K * u, y = y0 - age * 19 * K - 5 * K * u * u;
      const rad = (1.3 + 3.6 * (1 - Math.pow(1 - u, 3))) * K * (0.7 + 0.6 * rand(i, j, 72)), k = smooth(0, 0.12, u) * (1 - smooth(0.45, 1, u)) * (front ? 0.5 : 0.62);
      const c = mix(rand(i, j, 70) < 0.5 ? C[4] : mix(C[4], C[3], 0.4), C[1], u * 0.85);
      p.disc(x, y, rad * 1.08, c, k * 0.4);
      p.disc(x + rad * 0.05, y + rad * 0.08, rad * 0.76, c, k * 0.55);
      p.disc(x - rad * 0.22, y - rad * 0.3, rad * 0.48, mix(c, C[6], 0.25), k * 0.38 * (1 - u * 0.6));
      if (i % 3 === 0) p.glow(x, y, rad * 1.6, C[4], 0.08 * k);
    }
  });
}

export function drawBack(p, t, o) {
  const C = fit(o.palette, 8), a = o.actor, S = shape(a), K = o.scale;
  // the stain pooling round the feet, its rim glowing (each 1 px row of the original is K px here)
  // 足の幅: 元は一番下の行だけを見る (人形の足は平ら)。 敵の絵は一番下が盾の先や爪のことがあるので、 下 8% の行の左右の端を使う
  let fl = S.w, fr = 0;
  for (let y = Math.floor(S.h * 0.92); y < S.h; y++) { const [l, r] = S.rows[y]; if (l >= 0) (fl = Math.min(fl, l)), (fr = Math.max(fr, r)); }
  const cx = a.x + (fl + fr + 1) / 2, rx = (fr - fl) / 2 + (7 + 2 * Math.sin((t / D) * TAU)) * K;
  for (let x = Math.floor(cx - rx - K); x <= cx + rx + K; x++) {
    const q = Math.abs(x + 0.5 - cx) / rx, nn = rand(Math.floor(x / K * 2), 31);
    if (q > 1 + nn * 0.12) continue;
    for (let y = 0; y < K; y++) p.px(x, y, q > 0.8 ? C[4] : nn < 0.2 ? C[3] : C[2]);
    if (q < 0.85) for (let y = K; y < 2 * K; y++) p.px(x, y, C[1]);
    if (q < 0.55 && nn < 0.7) for (let y = 2 * K; y < 3 * K; y++) p.px(x, y, C[1]);
  }
  p.shade(cx - rx - 6 * K, -4 * K, cx + rx + 6 * K, 5 * K, (x, y, out) => {
    const d = Math.hypot((x - cx) / (rx + 6 * K), y / (4 * K));
    if (d < 1) (out.l = C[4]), (out.k = 0.2 * (1 - d) * (1 - d));
  });
  trails(p, C, a, S, t, K, false);
  p.glow(0, a.y + S.h * 0.45, 30 * K, C[3], 0.12);
}

export function draw(p, t, o) {
  const C = fit(o.palette, 8), a = o.actor, S = shape(a);
  trails(p, C, a, S, t, o.scale, true);
}
