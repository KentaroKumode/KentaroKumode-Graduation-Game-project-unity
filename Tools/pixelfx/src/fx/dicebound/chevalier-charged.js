// DICE BOUND 用 (2026-11-14): シュヴァリエの大技が溜まった状態の待機 (チャージのターンの後、 大技のターンまで出し続けるループ)。
// かなり暗く落とした画面 (ゲーム側) に、 鋭いきらめきが 1 つだけ、 長さの脈を打つ。 絵は chevalier-charge.js の glintHeld()。
// 画面全体 (480×270)・原点はきらめきの中心。 1.5 秒で継ぎ目なく回る (1 秒 8 コマで 12 コマ)。 回る物が無いので、 大技へは 0 / 6 コマ目で入ればよい。
import { fit } from "../../core/index.js";
import { meta as CHARGE, glintHeld, HELD } from "./chevalier-charge.js";

export const meta = {
  name: "Chevalier charged (DICE BOUND)",
  desc: "The Chevalier holds the thrust: in the dark, one hard, thin glint pulses before his chest.",
  tags: ["blade", "dicebound", "boss", "charge", "idle", "loop"],
  size: CHARGE.size,
  origin: CHARGE.origin,
  duration: HELD,
  loop: true,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette },
};

export function draw(p, t, o) {
  glintHeld(p, fit(o.palette, 8), t, 0);
}
