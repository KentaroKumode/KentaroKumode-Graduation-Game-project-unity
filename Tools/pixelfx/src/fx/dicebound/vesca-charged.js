// DICE BOUND 用 (2026-11-14): ヴェスカの大技が溜まった状態の待機 (チャージのターンの後、 大技のターンまで出し続けるループ)。
// 虚の玉の上に時計の文字盤。 渦が巻き、 粒が吸い込まれ続け、 長針が逆に、 短針が順に回る。 絵は vesca-charge.js の held()。
// 玉のまわりだけ (240×176)・原点は玉の中心。 3 秒で継ぎ目なく回る (1 秒 8 コマで 24 コマ)。
// 大技へは 0 / 6 / 12 / 18 コマ目で入る (溜めを、 入る位相ごとに 4 本焼く ── vesca-stop / vesca-lost の phase)。
import { fit } from "../../core/index.js";
import { VIOLET, held, HELD } from "./vesca-charge.js";

export const meta = {
  name: "Vesca charged (DICE BOUND)",
  desc: "Vesca holds stopped time: a void orb under a clock face, the vortex winding in, the long hand turning backwards and the short hand forwards.",
  tags: ["time", "void", "dicebound", "boss", "charge", "idle", "loop"],
  size: [240, 176],
  origin: [120, 88],
  duration: HELD,
  loop: true,
  fps: 30,
  bg: "dark",
  options: { palette: VIOLET, broken: false },   // broken = 4 段目用 (壊れかけの時計・vesca-charge.js の held())
};

export function draw(p, t, o) {
  held(p, fit(o.palette, 8), t, 1, null, !!o.broken);
}
