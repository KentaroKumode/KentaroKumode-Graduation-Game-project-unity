// DICE BOUND 用 (2026-11-14): 審判官の大技がこちらへ届く。 画面の大半に、 天からの光 (holy/blessing を画面いっぱいに広げた物) が降りる。
// 白く飛んだコマ (dicebound/judge-release) から始める: 白が抜けると、 もう光の中にいて、 光の粒が降っている。 しばらく照らして、 淡くなって消える。
// 画面全体 (480×270)・原点は左上 (ここでは全部、 画面の左上からの座標)。
// 作り (holy/blessing): 上が狭く下が広い光の円錐・その中の光の筋 (1 本ずつ明滅)・筋の上を光が下へ走る細い線・降りながらきらめく光の粒 (着いた所で弾ける)・漂う細かい塵。
// 変えた所: 幅を画面の大半に (上 250・下 440 ドット)・筋を 4 → 8 本・粒を 20 → 70 個・1 回きりで、 最後に淡くなって消える。
// The cone of light, its shimmering rays, the falling sparkles and the motes are adapted from this repository's holy/blessing
// (after Sprite Fusion's open-source light effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, cycle, sparkle, smooth, clamp, lerp, TAU, WHITE } from "../../core/index.js";
import { GOLD } from "../holy/_lib.js";

const W = 480, H = 270, T = 3, CX = 240;

export const meta = {
  name: "Judge verdict (DICE BOUND)",
  desc: "The Judge's sentence falls on the viewer: a vast cone of light from above fills most of the screen, sparkles drifting down through it and bursting where they land, then it thins and clears.",
  tags: ["holy", "dicebound", "boss", "screen", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 3.25,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: GOLD, top: 125, bottom: 220, hold: 2.0, fade: 1.0, power: 1, seed: 7 },   // top / bottom = 光の半幅 (画面の上端 / 下端)
};

// 円錐の中の光の筋: [位置 (-1〜1), 幅, 1 周あたりの明滅の回数, 位相]
const RAYS = [[-0.82, 0.07, 1, 0.3], [-0.55, 0.1, 2, 1.7], [-0.3, 0.06, 1, 4.1], [-0.08, 0.09, 2, 2.6], [0.17, 0.07, 1, 5.2], [0.4, 0.11, 2, 0.9], [0.63, 0.06, 1, 3.3], [0.85, 0.08, 2, 4.7]];
const LINES = [1, 3, 5, 6];   // 細い線として見せる筋

export function draw(p, t, o) {
  const C = fit(o.palette, 8), K = (1 - smooth(o.hold, o.hold + o.fade, t)) * o.power;
  if (K <= 0.005) return;
  const halfW = (y) => lerp(o.top, o.bottom, y / H);
  const ray = (q) => {
    let I = 0.3 * Math.max(0, 1 - q * q);
    for (const [c, w, k, ph] of RAYS) I += Math.exp(-(((q - c) / w) ** 2)) * (0.55 + 0.45 * Math.sin((t / T) * TAU * k + ph)) * 0.6;
    return I * smooth(1.25, 0.75, Math.abs(q));
  };
  // 円錐: 柔らかい光と、 薄い金の霞。 下ほど濃い
  for (let y = 0; y < H; y++) {
    const hw = halfW(y), g = 0.6 + 0.4 * (y / H);
    for (let x = Math.max(0, Math.floor(CX - hw * 1.25)); x < Math.min(W, CX + hw * 1.25); x++) {
      const I = ray((x + 0.5 - CX) / hw) * g * K;
      if (I < 0.02) continue;
      p.blendPx(x, y, C[5], I * 0.2);
      p.add(x, y, C[5], I * 0.62);   // 光を強めに (弱いと、 液晶の色の段で石の灰色に落ちて、 金に見えない)
    }
  }
  // 筋の何本かは細い線。 光が下へゆっくり走る
  for (const i of LINES) {
    const [c, , k, ph] = RAYS[i];
    for (let y = 0; y < H; y++) {
      const pulse = 0.5 + 0.5 * Math.sin(y * 0.11 - (t / T) * TAU * 3 + ph);
      p.put(Math.round(CX + c * halfW(y)), y, pulse > 0.85 ? WHITE : C[6], (0.1 + 0.45 * pulse) * (0.5 + 0.5 * Math.sin((t / T) * TAU * k + ph)) * smooth(0, 30, y) * K);
    }
  }
  // 光の粒: 降りながらきらめき、 着いた所で弾ける。 白が抜けた時には、 もう画面じゅうに降っている (1 周 3 秒のうち、 どこかの途中から始まる)
  cycle(t, T, 70, o.seed, () => T, (age, life, R, i) => {
    const q = R.range(-0.92, 0.92), v = R.range(60, 120), sway = R.range(2, 6), ph = R.range(0, TAU), size = R.range(0.8, 3), land = R.range(150, 262) / v;
    const at = (a) => { const y = -6 + v * a; return [CX + q * halfW(y) + Math.sin(a * 2.2 + ph) * sway, y]; };
    if (age > land) {
      const w = (age - land) / 0.16;
      if (w < 1) { const [lx, ly] = at(land); sparkle(p, lx, ly, (4 * (1 - w) + 1) * Math.min(1, K * 2), WHITE, C[6], 0.5 * (1 - w) * K); }
      return;
    }
    const [x, y] = at(age), tw = 0.6 + 0.4 * Math.sin(age * (7 + (i % 4)) + ph), fade = Math.min(1, age / 0.3) * Math.min(1, K * 2);
    if (fade < 0.3) return;
    if (size > 1.2) sparkle(p, x, y, size * tw * fade, tw > 0.85 ? WHITE : C[6], C[5], 0.35 * fade);
    else { p.put(Math.round(x), Math.round(y), tw > 0.8 ? WHITE : C[6], fade); p.add(Math.round(x), Math.round(y), C[5], 0.35 * fade); }
  });
  // 光の中に漂う細かい塵
  cycle(t, T, 60, o.seed + 9, (g) => g.range(1.2, 2.4), (age, life, R) => {
    const u = age / life, y = 10 + R.range(0, 240) + u * R.range(6, 14), q = R.range(-1, 1), x = CX + q * halfW(y) + Math.sin(age * 1.7 + q * 5) * 1.5, k = Math.sin(u * Math.PI) * ray(q) * K;
    if (k > 0.12) p.put(Math.round(x), Math.round(y), k > 0.45 ? WHITE : C[6], Math.min(1, k * 2.2));
  });
}
