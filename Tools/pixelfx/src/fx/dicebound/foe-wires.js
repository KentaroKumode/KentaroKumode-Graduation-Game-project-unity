// DICE BOUND 用 (2026-11-14): 弱体をかける (コボルドの〈罠師〉)。 (作者: 一点から出過ぎ・派手すぎ。 スパイ映画のレーザー警報装置のように、 いろいろな点から、 いろいろな角度で)
// こちらの盤のまわりに、 細い線が 1 本ずつ、 ぱっと張られていく。 どれも別の点から別の角度。 張り終わると、 しばらく残って、 1 本ずつ切れる。 地味な灰色。
// 画面全体 (480×270)・原点は左上。 自前 (元にした効果は無い。 線の端の止め具と、 かすかな光だけ)。
import { Rng, fit, clamp } from "../../core/index.js";

export const meta = {
  name: "Foe wires (DICE BOUND)",
  desc: "Thin grey lines snap taut across the viewer's board one after another, each from a different point at a different angle, hold, then cut out one by one.",
  tags: ["dicebound", "enemy", "debuff", "one-shot"],
  size: [480, 270],
  origin: [0, 0],
  duration: 1.9,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: ["#1d1620", "#3a3440", "#5c5664", "#8a8494", "#b8b2c0", "#e4e0ea", "#ffffff"], count: 9, seed: 21 },
};

export function create(o) {
  const C = fit(o.palette, 7), R = new Rng(o.seed);
  // 盤のまわりの枠 (左・右・上・下の辺) から 2 点を選んで結ぶ。 同じ辺どうしは選ばない
  const X0 = 26, X1 = 454, Y0 = 150, Y1 = 266;
  const onSide = (k) => (k === 0 ? [X0, R.range(Y0, Y1)] : k === 1 ? [X1, R.range(Y0, Y1)] : k === 2 ? [R.range(X0 + 30, X1 - 30), Y0] : [R.range(X0 + 30, X1 - 30), Y1]);
  const wires = Array.from({ length: o.count }, (_, i) => { const a = R.int(0, 3); let b = R.int(0, 3); if (b === a) b = (a + 1 + R.int(0, 2)) % 4; return { a: onSide(a), b: onSide(b), t0: 0.05 + i * 0.07 + R.range(0, 0.03), t1: 1.15 + i * 0.05, ph: R.range(0, 6) }; });
  let t = 0;
  return {
    update(dt) { t += dt; },
    draw(p) {
      for (const w of wires) {
        const u = t - w.t0;
        if (u < 0 || t > w.t1 + 0.1) continue;
        const [ax, ay] = w.a, [bx, by] = w.b, grow = clamp(u / 0.06), cut = clamp((t - w.t1) / 0.1), L = Math.hypot(bx - ax, by - ay), fresh = u < 0.1;
        // 張られる時は a から b へ走り、 切れる時は真ん中から両端へ縮む
        const s0 = cut * 0.5, s1 = 1 - cut * 0.5;
        for (let d = 0; d <= L * grow; d += 1) {
          const k = d / L;
          if (cut > 0 && k > s0 && k < s1) continue;
          const x = ax + (bx - ax) * k, y = ay + (by - ay) * k, flick = Math.sin(d * 0.35 + t * 9 + w.ph) > 0.86;
          p.dot(x, y, fresh ? C[6] : flick ? C[5] : (d | 0) % 6 < 4 ? C[4] : C[3]);
        }
        if (grow >= 1 && cut <= 0) p.glowLine(ax, ay, bx, by, 3, C[4], fresh ? 0.3 : 0.1);
        // 両端の止め具
        for (const [x, y, on] of [[ax, ay, true], [bx, by, grow >= 1]]) if (on && cut < 1) { p.dot(x, y, C[6]); p.dot(x - 1, y, C[4]); p.dot(x + 1, y, C[4]); p.dot(x, y - 1, C[4]); p.dot(x, y + 1, C[4]); p.glow(x, y, 5, C[5], fresh ? 0.6 : 0.25); }
      }
    },
  };
}
