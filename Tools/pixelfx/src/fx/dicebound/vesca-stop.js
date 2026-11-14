// DICE BOUND 用 (2026-11-14): ヴェスカ 1〜3 段目の大技の溜め。 待機 (dicebound/vesca-charged) の 0 コマ目から続く。
// 針が進む ── 1 目盛りごとに間が延びていき (0 / 0.12 / 0.28 / 0.5 / 0.8 秒)、 止まる。 文字盤が白くなる。 その次のコマで、 画面全体が凍って砕ける (dicebound/vesca-shatter)。
// 画面全体 (480×270)・原点は文字盤の中心。 作り: 間が延びていく針 = arcane/time-stop の TICKS。
import { fit, clamp, ease, WHITE } from "../../core/index.js";
import { meta as CHARGE, clock, hung, NOON, TICK } from "./vesca-charge.js";

export const meta = {
  name: "Vesca time stop (DICE BOUND)",
  desc: "Vesca's clock runs down: the hand ticks on, each tick later than the last, and stops; the face blanches white.",
  tags: ["time", "dicebound", "boss", "charge", "one-shot"],
  size: CHARGE.size,
  origin: CHARGE.origin,
  duration: 1.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, lead: 1.0 },
};

const TICKS = [0, 0.12, 0.28, 0.5, 0.8];

export function draw(p, t, o) {
  const C = fit(o.palette, 8);
  let a = 0;
  for (const tk of TICKS) if (t >= tk) a += ease.outBack(clamp((t - tk) / 0.07), 3);
  hung(p, C, 11, 1, 0);
  // 最後の 1 目盛りの後 (0.9 秒〜) は、 文字盤が白く抜ける
  clock(p, C, 0, 0, NOON + a * TICK, 1, t >= o.lead - 0.1 ? WHITE : null);
}
