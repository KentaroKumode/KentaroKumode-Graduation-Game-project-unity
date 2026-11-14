// DICE BOUND 用 (2026-11-14): ヴェスカの大技が溜まった状態の待機 (チャージのターンの後、 大技のターンまで出し続けるループ)。
// 胸の前に時計の文字盤。 針が 1 目盛り進んでは戻る (時が引っかかっている)。 まわりの粒は宙に止まったまま。 絵は vesca-charge.js の clockHeld()。
// 画面全体 (480×270)・原点は文字盤の中心。 1.5 秒で継ぎ目なく回る (1 秒 8 コマで 12 コマ)。 大技へは 0 コマ目 (針が 12 時) で入る。
import { fit } from "../../core/index.js";
import { meta as CHARGE, clockHeld, HELD } from "./vesca-charge.js";

export const meta = {
  name: "Vesca charged (DICE BOUND)",
  desc: "Vesca holds stopped time: a clock face before her chest whose hand ticks forward one mark and snaps back, motes hanging frozen around it.",
  tags: ["time", "dicebound", "boss", "charge", "idle", "loop"],
  size: CHARGE.size,
  origin: CHARGE.origin,
  duration: HELD,
  loop: true,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette },
};

export function draw(p, t, o) {
  clockHeld(p, fit(o.palette, 8), t, 1);
}
