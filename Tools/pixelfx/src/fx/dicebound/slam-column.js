// DICE BOUND 用 (2026-11-14): 盾を上から叩きつける時の、 落ちて来る重い塊の軌跡 (太い光の柱)。 原点 = 着く所。 画面のドットで描く (_scaled.js では K = 1 で使う)。
// melee/thrust を下向きにした筋の束だけでは、 線が細くて軽い (作者: もっと鈍重に)。 その後ろに、 上が細く下が太い柱を 1 本、 ゆっくり落とす (melee/hammer-smash の「動きの楔」の考え方)。
import { fit, clamp, ease } from "../../core/index.js";
import { beam } from "./_kit.js";

export const meta = {
  name: "Slam column (DICE BOUND)",
  desc: "The heavy wedge of motion behind a shield slammed straight down: a column of light, narrow above and broad below, that drops onto the mark and thins away.",
  tags: ["dicebound", "player", "shield", "one-shot"],
  size: [160, 260],
  origin: [80, 250],
  duration: 0.7,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: ["#212f6b", "#2c58aa", "#3a8bd8", "#62c8f4", "#b8c9e8", "#e8f1ff", "#ffffff", "#ffffff"], height: 230, width: 46, drop: 0.14, seed: 1 },
};

export function draw(p, t, o) {
  const C = fit(o.palette, 8), reach = ease.inQuad(clamp(t / o.drop)), fade = 1 - clamp((t - o.drop) / 0.3);
  if (fade <= 0) return;
  // 落ちて来る間は、 柱の下の端が着く所へ近づく。 着いた後は、 細って消える
  const y1 = -o.height * (1 - reach);
  beam(p, C, 0, y1 - o.height, 0, y1, o.width * 0.35 * fade + 1, o.width * fade + 2, 1);
}
