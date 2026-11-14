// DICE BOUND 用 (2026-11-14): 短剣の攻撃。 melee/dagger-flurry の写し。 動き・形・火花は元のまま。 違う所:
//   斬撃の塗りを glowSlash (_blade.js) に替えた ── 縁が強く光り、 通った跡が明るい線で残り、 ブルームがかかる (作者: 剣・斧と同じ感じで)。
//   会心 (wide: true) は、 最初に真横の大きな斬撃を 1 本足し、 それを中空にした (作者: 一番初めの真横の大きな斬撃に抜き感を)。 連撃はそのあと。
// _scaled.js で大きくして使う (weapons.js)。 色は palette で渡す (通常 = 剣の通常と同じ鋼の青白・会心 = 剣の会心と同じ金)。
// The crescent, trace, glint and spark recipe is adapted from Sprite Fusion's open-source slash
// (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, fit, sparkle, clamp, ease, WHITE } from "../../core/index.js";
import { swing, slashEnd, shavings, sliver, glowAlong } from "../melee/_lib.js";
import { glowSlash } from "./_blade.js";
import { ringSlash } from "./cross-slash.js";

export const meta = {
  name: "Dagger (DICE BOUND)",
  desc: "A flurry of small quick crescents at every angle, each with a flick of sparks, finished by one big cut; on a critical hit, a wide flat slash crosses first and hollows out to its bright rim.",
  tags: ["dicebound", "player", "melee", "dagger", "one-shot"],
  size: [148, 104],
  origin: [36, 86],
  duration: 1.3,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1b1630", "#3a2f63", "#6150a3", "#9480d8", "#c7b8f5", "#efe9ff", "#ffffff"],
    sparks: ["#ffffff", "#efe9ff", "#c7b8f5", "#9480d8"],
    wide: false,   // 会心: 最初に、 真横の大きな斬撃が 1 本 (光った一瞬のあと、 中が抜ける)
    flow: 2.5,     // 何倍に広げて使うか
    bloom: 1,
    // 敵の汎用の斬撃 (foes.js の dagger) 用: 既定は今までどおり
    count: 6,       // 小さな斬撃の数
    every: 0.055,   // 斬撃の間
    finish: true,   // 締めの一太刀
    gather: 1,      // 斬撃の位置の散らばり (1 = 元のまま・小さいほど真ん中に寄って重なる)
    clumsy: 0,      // 拙さ (1 で: 間がばらつく・振りが遅い・弧が短く太い)
    seed: 12,
  },
};

const EVERY = 0.055;
// small cuts: where, how they're turned, which way they go
const CUTS = [
  [21, -27, -0.5, 1], [33, -16, 0.9, -1], [26, -38, 2.4, 1], [37, -30, -1.8, -1], [19, -17, 0.2, 1], [30, -24, 1.4, -1],
];

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), lights = new Lights();
  const T_OFF = o.wide ? 0.3 : 0;   // 会心は、 大きな一太刀のあとに連撃
  // 真横の大きな斬撃 (会心): 連撃の締めの一太刀 (big) と同じ形を、 大きく。 右から左へ
  const WIDE = o.wide ? swing({ cx: 26, cy: -27, R: 48, sy: 0.3, k: 0.2, rot: -0.1, a0: 0.15, dir: 1, span: 3.0, W: 15, t0: 0, sweep: 0.07, hold: 0.06, trail: 0.24, ramp: C, sharp: true }) : null;   // sharp: 両端を針のように尖らせる (作者: どちらの端も丸くて、 シャープさが足りない)
  const small = CUTS.slice(0, o.count).map(([x, y, rot, dir], i) => swing({ cx: 26 + (x - 26) * o.gather, cy: -27 + (y + 27) * o.gather, R: Rg.range(12, 15), sy: Rg.range(0.55, 0.75), rot, a0: Rg.range(-2.8, -2.2), dir, span: Rg.range(2.3, 2.8), W: Rg.range(5.2, 6.4), t0: T_OFF + i * o.every, sweep: 0.035, hold: 0.03, trail: 0.13, ramp: C }));
  // the finisher: one wide flat cut right across the front, left to right
  const big = swing({ cx: 24, cy: -26, R: 30, sy: 0.36, k: 0.24, rot: 0.12, a0: 3.0, dir: -1, span: 2.85, W: 11, t0: T_OFF + small.length * o.every + 0.04, sweep: 0.055, hold: 0.06, trail: 0.24, ramp: C });
  if (o.clumsy) small.forEach((q) => { q.t0 += Rg.range(0, o.every * 0.7) * o.clumsy; q.sweep *= 1 + 1.4 * o.clumsy; q.hold *= 1 + o.clumsy; q.span *= 1 - 0.3 * o.clumsy; q.W *= 1 + 0.35 * o.clumsy; });
  const all = o.finish ? [...small, big] : small, ends = all.map(slashEnd), done = all.map(() => false);
  const slivers = [-2.6, -1.5, -0.4, 0.7].map((a) => ({ a: a + Rg.range(-0.2, 0.2), r: Rg.range(4, 7), v: Rg.range(70, 110), arc: Rg.range(0.4, 0.7) }));
  let t = 0;
  return {
    update(dt) {
      t += dt;
      all.forEach((s, i) => {
        if (done[i] || t < s.t0 + s.sweep) return;
        done[i] = true;
        const e = ends[i], last = s === big;
        lights.add(e.x, e.y, last ? 56 : 26, C[3], last ? 0.6 : 0.4, last ? 0.4 : 0.14);
        if (last) lights.add(e.x, e.y, 16, WHITE, 0.7, 0.08);
        for (let k = 0; k < (last ? 12 : 3); k++) {
          const a = Math.atan2(e.dy, e.dx) + Rg.range(-0.5, 0.5), sp = Rg.range(100, last ? 340 : 220);
          ps.spawn(P.SPARK, e.x, e.y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.1, last ? 0.35 : 0.22));
        }
        shavings(ps, Rg, s, last ? 14 : 4, [C[4], C[5]]);
        if (last) for (let k = 0; k < 5; k++) ps.spawn(P.STAR, s.cx + Rg.range(-22, 22), s.cy + Rg.range(-14, 14), 0, -4, Rg.range(0.2, 0.45), Rg.chance(0.5) ? C[5] : WHITE, Rg.range(1, 2.2), Rg.range(0.05, 0.25));
      });
      ps.update(dt);
      lights.update(dt);
    },
    draw(p) {
      if (WIDE) {
        // spin-slash と同じ中空の作り (dicebound/cross-slash の ringSlash): 頭の後ろだけ太い → 振り抜いた瞬間に白い帯で光る → 細って点線にほどける。 光る間は、 帯に沿って光を足す
        ringSlash(p, WIDE, t, WIDE.sweep + 0.02);
        const f = (t - WIDE.sweep - 0.02) / 0.3;
        if (f > -0.3 && f < 1) glowAlong(p, WIDE, WIDE.R - 1, 0, WIDE.span, 7, C[6], (uu) => 0.35 * (1 - Math.max(0, f)) * o.bloom * Math.pow(Math.sin(Math.PI * uu / WIDE.span), 1.6), 0, 0.12);   // 光も、 両端では絞る (端が丸く見えないように)
      }
      for (const s of all) glowSlash(p, s, t, o.flow, o.bloom);
      all.forEach((s, i) => {
        const e = ends[i], g = t - s.t0 - s.sweep * 0.6, last = s === big, life = last ? 0.24 : 0.12;
        if (g > 0 && g < life) sparkle(p, e.x, e.y, (last ? 10 : 4) * ease.outBack(Math.min(1, g / 0.04)) * (1 - g / life), C[5], C[3], last ? 0.6 : 0.35);
        // light rides each edge while it's moving
        const u = t - s.t0;
        if (u > 0 && u < s.sweep + 0.1) {
          const h = slashEnd({ ...s, span: s.span * ease.outExpo(clamp(u / s.sweep)) });
          p.glow(h.x, h.y, last ? 40 : 22, C[3], (last ? 0.5 : 0.35) * (1 - u / (s.sweep + 0.1)));
        }
      });
      // the finisher throws slivers off its end
      const q = o.finish ? t - big.t0 - big.sweep : -1;
      if (q > 0.02 && q < 0.14) {
        const k = (q - 0.02) / 0.12, e = ends[ends.length - 1];
        for (const s of slivers) sliver(p, e.x, e.y, s.a + Math.atan2(e.dy, e.dx), s.r + s.v * (q - 0.02), s.arc * (1 - 0.4 * k), k < 0.4 ? C[6] : k < 0.7 ? C[5] : C[4], 1 - k);
      }
      ps.draw(p);
      lights.draw(p);
    },
  };
}
