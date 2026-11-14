// DICE BOUND 用 (2026-11-14): ヴェスカ 4 段目 (天与) だけの大技の溜め。 待機 (dicebound/vesca-charged) の 0 コマ目から続く。
// 針が暴れる ── 加速しながら回り続け、 文字盤そのものが横にずれて、 影が二重三重に出る。 その次のコマから、 画面全体が乱れて、 信号が切れる (dicebound/vesca-glitch → vesca-off)。
// 1〜3 段目は「時が止まる」、 4 段目は「時が壊れる」。 画面全体 (480×270)・原点は文字盤の中心。
import { fit, rand, WHITE } from "../../core/index.js";
import { meta as CHARGE, clock, hung, NOON, TICK } from "./vesca-charge.js";

export const meta = {
  name: "Vesca signal breaking (DICE BOUND)",
  desc: "Vesca's clock breaks: the hand spins faster and faster and the face itself jerks sideways, ghosting double and triple.",
  tags: ["time", "glitch", "dicebound", "boss", "charge", "one-shot"],
  size: CHARGE.size,
  origin: CHARGE.origin,
  duration: 1.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, lead: 1.0, seed: 3 },
};

export function draw(p, t, o) {
  const C = fit(o.palette, 8), k = Math.min(1, t / o.lead), f = Math.floor(t * 8 + 1e-4);
  // 針: 目盛りを飛ばしながら、 だんだん速く (1 コマごとに 1 → 5 目盛り)
  const hand = NOON + Math.round(k * k * 40) * TICK;
  // 文字盤のずれ: 1 コマごとに違う量。 後半ほど大きい
  const amp = 2 + 26 * k * k, dx = Math.round((rand(f, 1, o.seed) - 0.5) * 2 * amp), dy = k > 0.6 ? Math.round((rand(f, 2, o.seed) - 0.5) * 6 * k) : 0;
  hung(p, C, 11, 1 - 0.6 * k, f % 2);
  if (k > 0.25) clock(p, C, -dx * 1.6, 0, hand - TICK * 2, 1, C[3]);   // 影 (逆へずれる・暗い)
  if (k > 0.55) clock(p, C, dx * 2.4, dy * 2, hand + TICK * 3, 1, C[4]);
  clock(p, C, dx, dy, hand, 1, k > 0.85 && f % 2 ? WHITE : null);
}
