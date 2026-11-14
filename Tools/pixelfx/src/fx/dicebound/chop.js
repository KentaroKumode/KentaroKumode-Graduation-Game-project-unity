// DICE BOUND 用 (2026-11-14): 斧の、 縦の振り下ろし (刃の軌跡だけ)。 着いた所の絵は、 impacts/heavy-landing (通常) / impacts/ground-slam (会心) を重ねる (weapons.js)。
// (作者: 斧は横から殴ってはいけない ── 縦に振り下ろす。 最初に使った melee/heavy-cleave は、 横から見た剣士が前へ振り下ろす絵で、 正面から見ると右から殴って見えた)
// 原点 = 刃が着く所 (敵の足もと)。 真上から、 ほぼまっすぐ (わずかに反って) 落ちる。 太いのは頭 (下) のすぐ後ろ。 _scaled.js で大きくして使う。
// 作り: melee/_lib の swing と smear (sword-slash と同じ斬撃の部品)。 塗りは slash() の式に、 流れの筋を少し足した物 (dicebound/blade-slash と同じ)。
// The crescent recipe (the edge whips out, holds, the tail follows, thinning and cooling down the ramp; shavings and sparks) is adapted from Sprite Fusion's
// open-source slash (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, sparkle, clamp, smooth, ease, noise2 } from "../../core/index.js";
import { through, smear, trace, glowAlong, slashEnd, shavings, onSwing } from "../melee/_lib.js";

export const meta = {
  name: "Axe chop (DICE BOUND)",
  desc: "An axe comes straight down from overhead: a thick crescent drops to the mark, white at its edge, cooling as it trails off, throwing sparks where it lands.",
  tags: ["dicebound", "player", "melee", "axe", "one-shot"],
  size: [120, 150],
  origin: [60, 130],
  duration: 0.8,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1d1620", "#3a3440", "#5c5664", "#8a8494", "#b8b2c0", "#e4e0ea", "#ffffff"],
    sparks: ["#ffffff", "#e4e0ea", "#b8b2c0", "#8a8494"],
    heavy: false,   // 会心: 太く・長く残る・残像が 1 本付く
    flow: 2.4,      // 何倍に広げて使うか (筋の細かさを、 広げた後の画面のドットに合わせる)
    seed: 4,
  },
};

function chopSlash(p, s, t, FLOW) {
  const u = t - s.t0;
  if (u < 0) return;
  const R = s.ramp, n = R.length, life = s.sweep + s.hold + s.trail;
  const head = ease.outExpo(clamp(u / s.sweep)), tail = smooth(s.sweep + s.hold, life, u), age = clamp(u / life);
  const U = s.span * head, T = s.span * tail, W = s.W * (1 - tail * 0.55);
  if (tail < 1 && U - T > 0.01) {
    smear(p, s, T, U, W, (uu, r, out) => {
      const v = (uu - T) / (U - T), w = W * Math.pow(Math.sin(Math.PI * Math.pow(v, 2.2)), 0.6);   // 頭のすぐ後ろが一番太い
      if (w < 0.35) return;
      const q = (s.R - r) / w;
      if (q < 0 || q > 1) return;
      const strand = noise2(uu * s.R * 0.04 * FLOW - age * 10, r * 0.5 * FLOW, 31);   // 流れに沿った筋を少し
      const ci = q < 0.22 && age < 0.5 ? 0 : Math.round(q * (n - 1) * 0.75 + age * 2.4 - v * 0.8 + 0.4 + (strand > 0.45 ? -1 : strand < -0.55 ? 0.9 : 0));
      out.c = R[n - 1 - clamp(ci, 0, n - 1)];
      out.a = Math.min(1, v / 0.4);
      // 縁の発光 (2026-11-14 作者: 剣のように発光を足す): 外の縁の細い帯に、 光を乗せる
      // 縁は強く光らせる: 外の縁 1.8 (元の座標) の帯は、 色も白に寄せ、 光を強く乗せる。 その内側 2 つぶんにも、 弱い光
      const dr = s.R - r;
      if (dr < 1.8 && age < 0.8) { if (dr < 0.9) out.c = R[n - 1]; out.l = R[n - 1]; out.k = 1.3 * (1 - age * 0.8); }
      else if (dr < 4 && age < 0.6) { out.l = R[n - 2]; out.k = 0.5 * (1 - age); }
    });
    glowAlong(p, s, s.R - W * 0.4, T, U, 7 + W, R[n - 3], (uu) => 0.42 * (1 - age) * ((uu - T) / (U - T)), 0, 0.3);
    glowAlong(p, s, s.R - 0.5, T, U, 6, R[n - 1], (uu) => 0.6 * (1 - age) * Math.min(1, ((uu - T) / (U - T)) * 2), 0, 0.2);   // 縁に沿った、 白い光
  }
  const th = 0.85 * (1 - clamp((u - s.sweep - s.hold) / (s.trail + 0.45)));
  // 縁の残像 (通った跡の線): 点線ではなく明るい線で残し、 光らせる
  if (tail > 0.02 && th > 0) { trace(p, s, s.R - 0.5, 0, T, R[n - 2], (uu) => Math.min(1, th * 1.4) * (0.5 + 0.5 * uu / s.span), 0, 0.3); trace(p, s, s.R - 1.4, 0, T, R[n - 4], (uu) => th * (0.4 + 0.6 * uu / s.span), 0, 0.3); glowAlong(p, s, s.R - 0.5, 0, T, 6, R[n - 2], (uu) => 0.55 * th * (0.4 + 0.6 * uu / s.span), 0, 0.25); }
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, sparks: o.sparks }), big = !!o.heavy;
  // 真上から足もとへ: (0, −46) を真下向きに通る、 半径 150 の弧 (ほぼまっすぐ・わずかに右へ反る)。 長さ 0.66 rad ≈ 100
  const A = through(0, -46, Math.PI / 2, 1, 150, 0.66, 0.5, { W: big ? 17 : 13, t0: 0, sweep: 0.07, hold: big ? 0.12 : 0.08, trail: big ? 0.34 : 0.26, ramp: C });
  const E = { ...A, R: A.R - A.W * 0.75, W: A.W * (big ? 0.38 : 0.28), t0: 0.02, hold: 0.06, trail: 0.24, ramp: C.slice(0, 6) };   // 内側の残像 (通常にも付ける・細く)
  const end = slashEnd(A);
  let t = 0, landed = false;
  return {
    update(dt) {
      t += dt;
      if (!landed && t >= A.sweep) {
        landed = true;
        // 着いた所から左右へ走る火花・軌跡の外へ飛ぶ削り屑
        for (let i = 0; i < (big ? 26 : 14); i++) { const a = (Rg.chance(0.5) ? 0 : Math.PI) + Rg.range(-0.5, 0.5), sp = Rg.range(120, 340); ps.spawn(P.SPARK, end.x, end.y, Math.cos(a) * sp, Math.sin(a) * sp - Rg.range(20, 90), Rg.range(0.14, 0.38)); }
        shavings(ps, Rg, A, big ? 26 : 14, [C[4], C[5]], 0.2);
      }
      ps.update(dt);
    },
    draw(p) {
      if (E) chopSlash(p, E, t, o.flow);
      chopSlash(p, A, t, o.flow);
      const g = t - A.sweep * 0.6;
      if (g > 0 && g < 0.22) sparkle(p, end.x, end.y, (big ? 12 : 9) * ease.outBack(Math.min(1, g / 0.05)) * (1 - g / 0.22), C[5], C[3], 0.6);
      if (t < 0.3) { const [x, y] = onSwing(A, A.R - 4, A.span * ease.outExpo(clamp(t / A.sweep))); p.glow(x, y, 46, C[3], 0.6 * (1 - t / 0.3)); p.glow(x, y, 16, C[5], 0.5 * (1 - t / 0.3)); }
      ps.draw(p);
    },
  };
}
