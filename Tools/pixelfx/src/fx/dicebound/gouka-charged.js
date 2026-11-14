// DICE BOUND 用 (2026-11-14): 灰燼の王の〈業火〉が溜まった状態の待機 (チャージのターンの後、 大技のターンまで出し続けるループ)。
// 王の胸に、 白く焼けた芯と十字の光が脈を打ち、 火の粉がまばらに、 ゆっくり吸い寄せられる。 画面全体 (480×270)・原点は王の足もとの中心。
// チャージ (dicebound/gouka-charge) の最後のコマから、 そのまま続く。 1.5 秒で継ぎ目なく回る (1 秒 8 コマで 12 コマ)。
import { fit, cycle, ease, TAU, WHITE } from "../../core/index.js";
import { meta as CHARGE, cross, ball, REST } from "./gouka-charge.js";

const D = 1.5;

export const meta = {
  name: "Gouka charged (DICE BOUND)",
  desc: "The Ash King holds his gathered inferno: a white-hot point and a cross of light pulse at his chest while a few embers drift slowly in.",
  tags: ["fire", "dicebound", "boss", "charge", "idle", "loop"],
  size: [480, 270],
  origin: [240, 196],
  duration: D,
  loop: true,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, chest: 58, seed: 6 },
};

export function draw(p, t, o) {
  const F = fit(o.palette, 9), CY = -o.chest, ph = t / D, pulse = 0.5 + 0.5 * Math.sin(ph * TAU * 2);
  // 6 コマに 1 回、 1 コマだけ 1 ドット震える (溜まり切っていて不安定)
  const f = Math.floor(t * 8 + 1e-4), jit = f % 6 === 2 ? 1 : f % 6 === 3 ? -1 : 0;
  // 火の粉: まばらに、 ゆっくり、 渦を巻いて芯へ
  cycle(t, D, 16, o.seed * 3 + 1, (g) => g.range(0.8, 1.3), (age, life, R) => {
    const a0 = R.range(0, TAU), d0 = R.range(50, 130), spin = R.sign() * R.range(0.5, 1.2), c = R.pick([F[4], F[5], F[6]]);
    const pos = (q) => { const a = a0 + spin * q, d = d0 * (1 - ease.inQuad(q)) + 4; return [Math.cos(a) * d, CY + Math.sin(a) * d * 0.7]; };
    const q = age / life, [x, y] = pos(q), [x2, y2] = pos(Math.max(0, q - 0.05));
    p.line(x2, y2, x, y, c, Math.min(1, q * 4));
  });
  ball(p, F, jit, CY, 3 + 0.7 * pulse);
  cross(p, F, jit, CY, REST + 7 * pulse);
  p.glow(0, CY, 30 + 8 * pulse, F[5], 0.3 + 0.12 * pulse);
  p.glow(0, CY, 12, WHITE, 0.4 + 0.15 * pulse);
}
