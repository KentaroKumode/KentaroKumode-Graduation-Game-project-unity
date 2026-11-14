// DICE BOUND 用 (2026-11-14): ヴェスカ 4 段目 (天与) だけの大技の溜め。 待機 (dicebound/vesca-charged) の phase 秒目から続く。
// 針も渦も、 加速していく (1 秒で 7 倍)。 文字盤が横にずれて、 影が二重三重に出る。 その次のコマから、 画面全体が乱れて、 信号が切れる (dicebound/vesca-glitch → vesca-off)。
// 1〜3 段目は「時が止まる」、 4 段目は「時が壊れる」。 玉のまわりだけ (240×176)・原点は玉の中心。
import { fit, rand, TAU, WHITE } from "../../core/index.js";
import { VIOLET, held, clock, NOON, HELD } from "./vesca-charge.js";

export const meta = {
  name: "Vesca signal breaking (DICE BOUND)",
  desc: "Vesca's clock breaks: hands and vortex race faster and faster and the face itself jerks sideways, ghosting double and triple.",
  tags: ["time", "glitch", "dicebound", "boss", "charge", "one-shot"],
  size: [240, 176],
  origin: [120, 88],
  duration: 1.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: VIOLET, phase: 0, lead: 1.0, seed: 3 },   // phase = 待機の何秒目から続けるか (vesca-stop と同じ・4 本焼く)
};

export function draw(p, t, o) {
  const C = fit(o.palette, 8), k = Math.min(1, t / o.lead), f = Math.floor(t * 8 + 1e-4), tv = o.phase + t + 3 * t * t;   // 速さが 1 → 7 へ
  // 文字盤のずれ: 1 コマごとに違う量。 後半ほど大きい
  const amp = 1 + 22 * k * k, dx = Math.round((rand(f, 1, o.seed) - 0.5) * 2 * amp), a = (TAU * tv) / HELD;
  if (k > 0.25) clock(p, C, -dx * 1.6, 0, NOON - a + 0.5, NOON + a - 0.4, 1, C[3]);   // 影 (逆へずれる・暗い)
  if (k > 0.55) clock(p, C, dx * 2.2, 0, NOON - a - 0.7, NOON + a + 0.9, 1, C[4]);
  const tx = p.tx; p.tx = tx + dx;
  held(p, C, tv, 1, k > 0.85 && f % 2 ? WHITE : null, true);   // 4 段目の待機 (壊れかけ) から続く
  p.tx = tx;
}
