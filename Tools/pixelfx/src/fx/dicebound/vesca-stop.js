// DICE BOUND 用 (2026-11-14): ヴェスカ 1〜3 段目の大技の溜め。 待機 (dicebound/vesca-charged) の phase 秒目から続く。
// 針も渦も、 滑らかに遅くなって、 止まる (0.8 秒)。 止まった文字盤が白く抜ける。 その次のコマで、 画面全体が凍って砕ける (dicebound/vesca-shatter)。
// 玉のまわりだけ (240×176)・原点は玉の中心。 作り: 時刻そのものを遅くして held() を描く (arcane/time-stop の「時の流れが 0 へ落ちる」)。
import { fit, WHITE } from "../../core/index.js";
import { VIOLET, held, RC } from "./vesca-charge.js";

export const meta = {
  name: "Vesca time stop (DICE BOUND)",
  desc: "Vesca's clock runs down: hands and vortex slow smoothly to a dead stop, and the face blanches white.",
  tags: ["time", "dicebound", "boss", "charge", "one-shot"],
  size: [240, 176],
  origin: [120, 88],
  duration: 1.0,
  loop: false,
  fps: 30,
  bg: "dark",
  // phase = 待機の何秒目から続けるか (0 / 0.75 / 1.5 / 2.25 = 待機の 0 / 6 / 12 / 18 コマ目。 4 本焼く)。 slow = 止まるまでの秒
  options: { palette: VIOLET, phase: 0, slow: 0.8, lead: 1.0 },
};

export function draw(p, t, o) {
  const C = fit(o.palette, 8), u = Math.min(t, o.slow), tv = o.phase + u - (u * u) / (2 * o.slow);   // 速さが 1 → 0 へ、 まっすぐ落ちる
  const white = t >= o.lead - 0.12;
  held(p, C, tv, 1, white ? WHITE : null);
  if (white) p.ring(0, 0, RC + 2, 3, WHITE);
}
