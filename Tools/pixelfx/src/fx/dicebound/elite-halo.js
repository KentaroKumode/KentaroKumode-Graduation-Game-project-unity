// DICE BOUND 用 (2026-11-18・精鋭のオーラの候補): holy/halo の写し。 式は元のまま、 形を決めている数に倍率 K (`scale`) を掛けた。
//   掛けた数: 輪の横と縦の半径・頭からの高さ・太さ (手前 2 ドット → 2K ドット・奥 1 → 0.7K)・点の数・光の半径・きらめきの腕・降る粉の距離と数。
//   変えた所: 輪の位置を、 敵の頭 (形の上 12% の画素の真ん中) の上に置く。 頭の画素を照らして描き直す所は外した (敵の 4 色を崩すため)。 頭へ落ちる光 (加算) は残した。
//   降る粉は 2×2 の点。 尺は元の 2 秒 (24 コマで 48 コマ)。
import { fit, sparkle, cycle, TAU, WHITE } from "../../core/index.js";
import { GOLD } from "../holy/_lib.js";
import { shape } from "../status/_lib.js";

export const meta = {
  name: "Elite halo (DICE BOUND)",
  desc: "A halo scaled up over an elite foe's head: a ring of light with a glint running round it, dust sifting down.",
  tags: ["dicebound", "elite", "aura", "halo", "loop"],
  size: [480, 270],
  origin: [240, 204],
  duration: 2,
  loop: true,
  fps: 24,
  bg: "dark",
  actor: 2,
  options: { palette: GOLD, seed: 5, scale: 3, above: 5, halo: 0.5, thick: 0.65 },   // halo = 輪の内の光の半径の倍率 (×K だと輪の内が光で埋まり、 皿に見える)・thick = 手前の太さの倍率 (同じ理由)   // above = 頭の上から輪までの高さ (元の単位・K を掛ける)
};

const RX = 9.5, RY = 2.6;
const apart = (a, b) => Math.abs(((a - b + Math.PI) % TAU + TAU) % TAU - Math.PI);

function head(a) {
  const S = shape(a), n = Math.max(2, Math.round(S.h * 0.12));
  let sx = 0, c = 0;
  for (let y = 0; y < n; y++) for (let x = 0; x < S.w; x++) if (S.on(x, y)) (sx += x), c++;
  return [a.x + (c ? sx / c : S.w / 2), a.y];
}

export function draw(p, t, o) {
  const K = o.scale, C = fit(o.palette, 8), T = meta.duration, ph = (t / T) * TAU, A = o.actor, [HX, HT] = head(A);
  const y0 = HT - o.above * K + (Math.sin(ph) > 0.55 ? -K * 0.7 : 0), gl = Math.PI / 2 + ph, rx = RX * K, ry = RY * K;
  // light falling on the head (additive)
  p.glowLine(HX, y0 + 2 * K, HX, y0 + 9 * K, 9 * K, C[4], 0.16);
  // the ring
  const near2 = Math.round(2 * K * o.thick), far = Math.max(1, Math.round(0.7 * K));
  for (let j = 0, n = Math.round(92 * K); j < n; j++) {
    const g = (j / n) * TAU, x = HX + Math.cos(g) * rx, y = y0 + Math.sin(g) * ry, near = Math.sin(g) >= 0;
    const shimmer = Math.sin(g * 3 - ph * 2) > 0.7, hot = apart(g, gl) < 0.3;
    if (near) {
      for (let d = 0; d < near2; d++) p.px(x, y + d, d < near2 / 2 ? (hot ? WHITE : shimmer ? WHITE : C[6]) : hot ? C[6] : C[4]);
    } else for (let d = 0; d < far; d++) p.dot(x, y + d, hot ? WHITE : shimmer ? C[6] : C[5], 0.9);
  }
  p.glow(HX, y0, 20 * K * o.halo, C[4], 0.4);
  p.glow(HX, y0 + K, 9 * K * o.halo, WHITE, 0.16);
  const gx = HX + Math.cos(gl) * rx, gy = y0 + Math.sin(gl) * ry;
  if (Math.sin(gl) > -0.2) sparkle(p, gx, gy + 0.5 * K, (1.5 + 2.5 * Math.max(0, Math.sin(gl))) * K, WHITE, C[6], 0.45);
  else p.glow(gx, gy, 5 * K, C[6], 0.3);
  cycle(t, T, Math.round(6 * K * 0.7), o.seed + 3, (g) => g.range(0.3, 0.5), (age, life, R) => {
    const g = R.range(0, TAU), d = R.range(1.1, 1.5), k = Math.sin((age / life) * Math.PI);
    sparkle(p, HX + Math.cos(g) * rx * d, y0 + Math.sin(g) * ry * d - R.range(1, 4) * K, (1 + 2 * k) * K * 0.8, WHITE, C[6], 0.4 * k);
  });
  cycle(t, T, Math.round(16 * K), o.seed, (g) => g.range(0.8, 1.5), (age, life, R) => {
    const g = R.range(0, TAU), u = age / life, x = HX + Math.cos(g) * rx * R.range(0.8, 1) + Math.sin(u * 5 + g) * 1.2 * K, y = y0 + Math.sin(g) * ry + K + u * R.range(7, 14) * K;
    const tw = 0.6 + 0.4 * Math.sin(age * 18 + g * 7), k = Math.sin(u * Math.PI) * tw, c = tw > 0.85 ? WHITE : C[6], a = Math.min(1, k * 1.5);
    p.dot(x, y, c, a); p.dot(x + 1, y, c, a); p.dot(x, y + 1, c, a); p.dot(x + 1, y + 1, c, a);
    p.light(x, y, C[5], 0.5 * k);
  });
}
