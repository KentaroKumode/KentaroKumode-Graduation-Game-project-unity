// DICE BOUND 用 (2026-11-14): 交差する二太刀 (剣の段 3・段 4)。 melee/double-slash の写しで、 **違うのは斬撃の塗りだけ**。
// 元は、 斬撃の中を色の段で塗る。 この画面の大きさに広げると、 内側のべた塗りが目立って見た目が悪い (作者の指摘)。
// 回転切り (spin-slash) と同じ作りにした: 太いのは頭のすぐ後ろだけで、 通った所はすぐ痩せる → ×が閉じると全体が白い帯で光る → 細って、 冷めて、 点線にほどける (中空 = 抜き感)。
// 動き・形・火花・交点の光は元のまま。 _scaled.js で大きくして使う (weapons.js)。
// The crescent, trace, glint and spark recipe is adapted from Sprite Fusion's open-source slash
// (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, fit, sparkle, flare, clamp, ease, rand, WHITE } from "../../core/index.js";
import { through, smear, trace, glowAlong, slashEnd, shavings, sliver } from "../melee/_lib.js";

export const meta = {
  name: "Cross slash (DICE BOUND)",
  desc: "A down cut, then an up cut that crosses it; each stroke is solid only as it whips through and in the white flash when the X closes, then hollows out to its bright rim.",
  tags: ["dicebound", "player", "melee", "slash", "one-shot"],
  size: [164, 108],
  origin: [56, 92],
  duration: 0.8,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#2a0a22", "#5e1238", "#a01f4e", "#e0386a", "#ff86a6", "#ffd2dd", "#ffffff"],
    sparks: ["#fff3a6", "#ffe26b", "#ff8a2a", "#f2561d"],
    seed: 6,
  },
};

const FLARE = 0.3;   // ×が閉じてから、 白く光って、 細って、 点にほどけるまで (spin-slash と同じ)

/**
 * melee/spin-slash の中空の作り方を、 そのまま 1 本の斬撃に当てた物 (2026-11-14 作者: 中空の方法は Spin slash をしっかり参照)。 spin-slash は「縁だけ残す」のではなく:
 *   1. 振っている間: 太いのは頭のすぐ後ろだけ。 縁が通ってから時間が経った所は、 幅が 3 割まで痩せる (exp(−経過 / 0.075))。 色も、 経過で一気に冷める (経過 × 13 段)。
 *   2. 閉じた瞬間 (tFlare): 弧の全体が、 太い白い帯になって光る (最初の 13%)。 少し外へ膨らみながら、 (1 − f)^1.5 で細り、 1 段ずつ冷め、 最後は網点で薄くなる。
 *   3. そのあと: 通った跡の点線が、 所々欠けながら残って消える。
 */
export function ringSlash(p, s, t, tFlare) {
  const u = t - s.t0;
  if (u < 0) return;
  const R = s.ramp, n = R.length, head = ease.outExpo(clamp(u / s.sweep)), U = s.span * head, f = (t - tFlare) / FLARE;
  const passed = (frac) => s.sweep * Math.min(1, -Math.log2(Math.max(1e-3, 1 - frac)) / 10);   // 縁が、 弧の frac の所を通った時刻 (outExpo の逆)
  if (f < 0 && U > 0.02) {
    smear(p, s, 0, U, s.W, (uu, r, out) => {
      const age = u - passed(uu / s.span), lead = U - uu;
      // s.sharp: 両端を鋭く (頭も尾も、 長く細く尖らせる)。 既定は元の spin-slash と同じ丸み
      const w = s.W * (s.sharp ? Math.pow(clamp(lead / (s.span * 0.4)), 1.3) : Math.pow(clamp(lead / (s.span * 0.22)), 0.5)) * (0.3 + 0.7 * Math.exp(-age / 0.075)) * Math.pow(clamp(uu / (s.span * (s.sharp ? 0.45 : 0.3))), s.sharp ? 1.4 : 0.7);
      if (w < 0.45) return;
      const q = (s.R - r) / w;
      if (q < 0 || q > 1) return;
      const ci = q < 0.26 && age < 0.06 ? 0 : Math.round(q * (n - 1) * 0.7 + age * 13 + 0.4);
      out.c = R[n - 1 - clamp(ci, 0, n - 1)];
      out.a = Math.min(1, uu / (s.span * 0.3) + 0.2);
    });
    glowAlong(p, s, s.R - s.W * 0.3, 0, U, 14, R[3], (uu) => 0.3 * Math.exp(-(u - passed(uu / s.span)) / 0.08), 0, 0.3);
  } else if (f >= 0 && f < 1) {
    const RR = s.R + 0.8 * ease.outCubic(f), th = f < 0.13 ? s.W * 0.56 : Math.max(1, s.W * 0.375 * Math.pow(1 - f, 1.5) + 0.5), S = { ...s, R: RR }, step = f < 0.13 ? 6 : f < 0.3 ? 5 : f < 0.55 ? 4 : 3;
    smear(p, S, 0, U, th, (uu, r, out) => {
      const w = th * Math.pow(Math.sin(Math.PI * clamp(uu / s.span)), s.sharp ? 1.7 : 0.5);   // 両端は細る (弧は閉じた輪ではないので)。 sharp なら、 針のように尖る
      if (w < 0.4) return;
      const q = (RR - r) / w;
      if (q < 0 || q > 1) return;
      out.c = f < 0.13 ? (q < 0.6 ? WHITE : R[5]) : q < 0.45 ? R[step] : R[step - 1];
      out.a = f < 0.4 ? 1 : 1.3 - f;
    });
    glowAlong(p, S, RR - 2, 0, U, 14, R[4], s.sharp ? (uu) => 0.32 * (1 - f) * (1 - f) * Math.pow(Math.sin(Math.PI * clamp(uu / s.span)), 1.5) : 0.32 * (1 - f) * (1 - f), 0, 0.3);
  }
  const k = 1 - clamp((t - tFlare - 0.06) / 0.4);
  if (f >= 0 && k > 0) trace(p, s, s.R - 0.5, 0, s.span, R[4], (uu) => clamp(k * 1.5 - rand(Math.floor(uu * 30), 5) * 0.8) * 0.95);
}

const UP = 0.14; // the second cut starts
const R0 = 62;

const cut = (x, y, th, dir, span, extra) => through(x, y, th, dir, R0, span, 0.55, extra);

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), lights = new Lights();
  const X = 34, Y = -23;
  const down = cut(X, Y, 0.72, 1, 0.84, { W: 10, t0: 0, sweep: 0.06, hold: 0.07, trail: 0.24, ramp: C, lens: true });
  // 2 太刀目は、 1 太刀目を左右に返した物 (2026-11-14: 元は右下から左上へ斬り上げる・角度も太さも 1 太刀目と違う ── 横から見た剣士の動きには合うが、 正面から見ると×が歪んで見える)。
  //   右上から左下へ、 同じ角度・同じ反り・同じ太さで。 交点は弧のちょうど同じ所
  const up = cut(X, Y, Math.PI - 0.72, -1, 0.84, { W: 10, t0: UP, sweep: 0.06, hold: 0.07, trail: 0.24, ramp: C, lens: true });
  const ends = [slashEnd(down), slashEnd(up)], slivers = [-2.4, -1.1, 0.2, 1.6].map((a) => ({ a: a + Rg.range(-0.2, 0.2), r: Rg.range(5, 8), v: Rg.range(70, 120), arc: Rg.range(0.4, 0.7) }));
  const CLOSE = UP + 0.012;
  let t = 0, cut1 = false, cut2 = false, closed = false;
  return {
    update(dt) {
      t += dt;
      if (!cut1 && t >= down.sweep) {
        cut1 = true;
        shavings(ps, Rg, down, 10, [C[4], C[5]]);
        for (let i = 0; i < 6; i++) {
          const a = Math.atan2(ends[0].dy, ends[0].dx) + Rg.range(-0.5, 0.5), sp = Rg.range(120, 300);
          ps.spawn(P.SPARK, ends[0].x, ends[0].y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.12, 0.3));
        }
      }
      if (!closed && t >= CLOSE) {
        closed = true;
        lights.add(X, Y, 56, C[3], 0.45, 0.4);
        lights.add(X, Y, 18, WHITE, 0.6, 0.08);
        for (let i = 0; i < 18; i++) {
          const a = Rg.range(-Math.PI, Math.PI) * 0.8, sp = Rg.range(140, 360);
          ps.spawn(P.SPARK, X, Y, Math.cos(a) * sp, Math.sin(a) * sp - 40, Rg.range(0.14, 0.4));
        }
        for (let i = 0; i < 5; i++) ps.spawn(P.STAR, X + Rg.range(-14, 14), Y + Rg.range(-12, 12), 0, -4, Rg.range(0.2, 0.4), Rg.chance(0.5) ? C[5] : WHITE, Rg.range(1, 2.2), Rg.range(0.05, 0.2));
      }
      if (!cut2 && t >= UP + up.sweep) {
        cut2 = true;
        shavings(ps, Rg, up, 12, [C[4], C[5]]);
      }
      ps.update(dt);
      lights.update(dt);
    },
    draw(p) {
      // both strokes flash white for two frames when the X closes
      const flash = t >= CLOSE && t < CLOSE + 0.07 ? 1 : 0;
      down.hot = up.hot = flash;
      // ×が閉じる = 2 太刀目が交点を越えた頃。 2 本とも、 そこで白く光る
      ringSlash(p, down, t, CLOSE + 0.03);
      ringSlash(p, up, t, CLOSE + 0.03);
      // glints at the ends of the strokes
      for (const [s, e] of [[down, ends[0]], [up, ends[1]]]) {
        const g = t - s.t0 - s.sweep * 0.6;
        if (g > 0 && g < 0.2) sparkle(p, e.x, e.y, 7 * ease.outBack(Math.min(1, g / 0.05)) * (1 - g / 0.2), C[5], C[3], 0.5);
      }
      // the crossing: a flare, slivers flying off it, light
      const q = t - CLOSE;
      if (q >= 0 && q < 0.26) {
        const k = q / 0.26;
        flare(p, X, Y, 22 * Math.pow(1 - k, 1.4), 1 - k, C);
        p.glow(X, Y, 34, C[4], 0.35 * (1 - k));
      }
      if (q >= 0.03 && q < 0.15) {
        const k = (q - 0.03) / 0.12;
        for (const s of slivers) sliver(p, X, Y, s.a, s.r + s.v * (q - 0.03), s.arc * (1 - 0.4 * k), k < 0.4 ? C[6] : k < 0.7 ? C[5] : C[4], 1 - k);
      }
      // light rides each edge as it goes
      for (const s of [down, up]) {
        const u = t - s.t0;
        if (u <= 0 || u >= 0.25) continue;
        const e = slashEnd({ ...s, span: s.span * ease.outExpo(clamp(u / s.sweep)) });
        p.glow(e.x, e.y, 40, C[3], 0.5 * (1 - u / 0.25));
      }
      ps.draw(p);
      lights.draw(p);
    },
  };
}
