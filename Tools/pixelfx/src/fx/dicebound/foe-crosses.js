// DICE BOUND 用 (2026-11-14): 吸血 (血の眷属) の、 吸い終わりに敵のまわりへ出る十字。 (作者: オーブが吸われると同時に、 小さな回復の十字が赤で敵のまわりに出るといい)
// holy/heal-aoe の十字の出し方 (ぽんと出て、 揺れながら浮き上がり、 四隅へほどける) を、 敵の体のまわりに散らした物。 原点 = 敵の足もと。 _scaled.js で大きくして使う (foes.js)。
import { Rng, fit, sparkle, clamp, ease, WHITE } from "../../core/index.js";
import { plus } from "../holy/_lib.js";

export const meta = {
  name: "Foe crosses (DICE BOUND)",
  desc: "Small plus signs pop out round the enemy's body, sway upward and come apart.",
  tags: ["dicebound", "enemy", "heal", "one-shot"],
  size: [120, 110],
  origin: [60, 96],
  duration: 2.2,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: ["#14040a", "#3a0a14", "#6a101c", "#a01824", "#d42a30", "#ff5a50", "#ff9a8a", "#ffe6de"], count: 12, width: 44, height: 50, t0: 0.6, seed: 5 },
};

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed);
  const sites = Array.from({ length: o.count }, (_, i) => { const big = i % 5 < 2; return { x: R.range(-1, 1) * o.width / 2, y: -R.range(4, o.height), t0: o.t0 + i * 0.045 + R.range(0, 0.05), rise: R.range(14, 26), life: R.range(0.8, 1.2), sway: R.range(0, 6.28), L: big ? 3 + (i % 5 === 0 ? 1 : 0) : 2, thick: big ? 3 : 1 }; });
  let t = 0;
  return {
    update(dt) { t += dt; },
    draw(p) {
      for (const s of sites) {
        const a = t - s.t0;
        if (a < 0 || a > s.life) continue;
        const u = a / s.life, h = s.rise * ease.outCubic(u), x = s.x + Math.sin(a * 4 + s.sway) * 1.5 * u, y = s.y - h;
        const pop = ease.outBack(clamp(a / 0.12), 2.6), end = s.life - 0.15, L = Math.max(1, Math.round(s.L * pop));
        if (a < 0.04) sparkle(p, x, y, 3, WHITE, C[6], 0.5);
        else if (a < end) { plus(p, x, y, L, s.thick, C, 1, Math.sin(a * 12 + s.sway * 3) > 0.8); p.glow(x, y, 5, C[5], 0.3); }
        else { const v = (a - end) / 0.15; for (const [dx, dy] of [[1, 1], [1, -1], [-1, 1], [-1, -1]]) p.dot(x + dx * (2 + v * 5), y + dy * (2 + v * 5) - v * 2, v < 0.4 ? WHITE : C[6], 1 - v); }
      }
    },
  };
}
