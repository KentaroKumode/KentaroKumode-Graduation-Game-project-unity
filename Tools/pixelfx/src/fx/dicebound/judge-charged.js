// DICE BOUND 用 (2026-11-14): 審判官の大技が溜まった状態の待機 (チャージのターンの後、 大技のターンまで出し続けるループ)。
// 頭上で日輪がゆっくり回る。 絵は judge-charge.js の sun() ── チャージの終わりも同じ関数で描くので、 途切れずに続く。
// 画面全体 (480×270)・原点は日輪の中心。 4 秒で継ぎ目なく回る (1 秒 8 コマで 32 コマ)。
import { fit } from "../../core/index.js";
import { meta as CHARGE, sun, SUN_T } from "./judge-charge.js";

export const meta = {
  name: "Judge charged (DICE BOUND)",
  desc: "The Judge holds his verdict: a sun turns slowly above his head, its rays breathing.",
  tags: ["holy", "dicebound", "boss", "charge", "idle", "loop"],
  size: CHARGE.size,
  origin: CHARGE.origin,
  duration: SUN_T,
  loop: true,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, rays: 8, scale: 1, beams: 1 },
};

export function draw(p, t, o) {
  sun(p, fit(o.palette, 8), t, o.rays, o.scale, 1, o.beams);
}
