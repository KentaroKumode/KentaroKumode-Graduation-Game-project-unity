// DICE BOUND 斧・会心リワーク案 (axe-crit-v2・holy 金):
//   通常とは別の技に見える 2 段構え:
//     段 1 (f2): 敵の胴を大きく横に薙ぐ (1 コマ太面 → 即 1/5 細弧)
//     溜めの一拍 (f5〜f6): 真上に鋭い閃光
//     段 2 (f7): 渾身の真上からの縦断ち割り (1 コマ太面 → f8 で細縁＋元の impacts/ground-slam が炸裂)
import { Particles, P, Rng, fit, sparkle, clamp, ease } from "../../core/index.js";
import { through, swing, onSwing, travel, slashEnd } from "../melee/_lib.js";
import { glowSlash } from "./_blade.js";

export const meta = {
  name: "Axe crit v2 (DICE BOUND)",
  desc: "Two-stage critical axe execution: a wide horizontal cleave whips across the enemy, followed after a beat by a colossal overhead chop that splits the floor.",
  tags: ["dicebound", "player", "melee", "axe", "crit", "one-shot"],
  size: [176, 154],
  origin: [88, 132],
  duration: 0.9,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#3b1d2a", "#7a3a2c", "#c4652e", "#f0a040", "#ffd25e", "#fff0a0", "#ffffff"],
    sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"],
    flow: 2.6,
    seed: 29,
  },
};

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks });
  // 段 1: 大きく横に薙ぐ (speed=0.65, dt=0.02708s → f2=0.0271s で 1 コマ太面 → f3 で即細弧)
  const H = swing({
    cx: 4, cy: -34, R: 58, sy: 0.42, k: 0.16, rot: 0.10, a0: -0.35, dir: 1, span: 3.35,
    W: 26, t0: 0, sweep: 0.019, hold: 0.011, trail: 0.145, ramp: C,
    snap: true, snapDur: 0.014, snapCut: 0.83, glowScale: 1.05, traceFade: 0.11, dotFade: true,
  });
  // 段 2: 渾身の縦振り下ろし (t0=0.138 → f7=0.1625s で u=0.0245s 1 コマ太面 → f8=0.1896s で即細縁＋ground-slam)
  const T2 = 0.138;
  const V = through(0, -46, Math.PI / 2, 1, 150, 0.68, 0.5, {
    W: 25, t0: T2, sweep: 0.019, hold: 0.011, trail: 0.175, ramp: C,
    snap: true, snapDur: 0.014, snapCut: 0.84, glowScale: 1.15, traceFade: 0.14, dotFade: true,
  });
  const VE = {
    ...V, R: V.R - V.W * 0.72, W: V.W * 0.34, t0: T2 + 0.004, sweep: 0.017, hold: 0.008, trail: 0.055, ramp: C.slice(0, 6),
    snap: true, snapDur: 0.012, snapCut: 0.86, glowScale: 0.85, traceFade: 0.015, dotFade: false,
  };
  const endH = slashEnd(H), endV = slashEnd(V);
  let t = 0, doneH = false, doneV = false;

  return {
    update(dt) {
      t += dt;
      if (!doneH && t >= H.sweep) {
        doneH = true;
        for (let i = 0; i < 6; i++) {
          const a = Math.atan2(endH.dy, endH.dx) + Rg.range(-0.45, 0.45), sp = Rg.range(130, 290);
          ps.spawn(P.SPARK, endH.x, endH.y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.05, 0.12));
        }
        const lives = [0.11, 0.145, 0.18, 0.215], fracs = [0.45, 0.65, 0.82, 0.96];
        for (let i = 0; i < lives.length; i++) {
          const tr = travel(H, H.span * fracs[i]), sp = Rg.range(45, 85);
          ps.spawn(P.MOTE, tr.x, tr.y, tr.dx * sp, tr.dy * sp, lives[i], i & 1 ? C[5] : C[4], 0.55);
        }
      }
      if (!doneV && t >= T2 + V.sweep) {
        doneV = true;
        for (let i = 0; i < 10; i++) {
          const a = (Rg.chance(0.5) ? 0 : Math.PI) + Rg.range(-0.45, 0.45), sp = Rg.range(140, 320);
          ps.spawn(P.SPARK, endV.x, endV.y, Math.cos(a) * sp, Math.sin(a) * sp - Rg.range(15, 70), Rg.range(0.06, 0.14));
        }
        const lives = [0.12, 0.155, 0.19, 0.225], fracs = [0.50, 0.66, 0.82, 0.96];
        for (let i = 0; i < lives.length; i++) {
          const tr = travel(V, V.span * fracs[i]), sp = Rg.range(45, 85);
          ps.spawn(P.MOTE, tr.x, tr.y, tr.dx * sp, tr.dy * sp, lives[i], i & 1 ? C[5] : C[4], 0.6);
        }
      }
      ps.update(dt);
      for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 6.5 * dt; m.vy -= m.vy * 6.5 * dt; }
    },
    draw(p) {
      // 段 1: 横薙ぎ
      glowSlash(p, H, t, o.flow, 1.05);
      if (t > 0 && t < 0.065) {
        const [x, y] = onSwing(H, H.R - 3, H.span * ease.outExpo(clamp(t / H.sweep)));
        p.glow(x, y, 10, C[3], 0.36 * (1 - t / 0.065));
      }
      // 振りかぶりの頂点閃光 (f5〜f6: 縦断ち割りへの溜めの一拍)
      if (t >= 0.085 && t < T2 + 0.01) {
        const k = Math.sin(clamp((t - 0.085) / (T2 + 0.01 - 0.085)) * Math.PI);
        sparkle(p, 0, -90, 11 * k, C[6], C[4], 0.65 * k);
        p.glow(0, -90, 10, C[4], 0.32 * k);
      }
      // 段 2: 縦の断ち割り
      glowSlash(p, VE, t, o.flow, 0.5);
      glowSlash(p, V, t, o.flow, 1.2);
      const uv = t - T2;
      const g = uv - V.sweep * 0.6;
      if (g > 0 && g < 0.08) sparkle(p, endV.x, endV.y, 9.5 * ease.outBack(Math.min(1, g / 0.025)) * (1 - g / 0.08), C[5], C[3], 0.55);
      if (uv > 0 && uv < 0.075) {
        const [x, y] = onSwing(V, V.R - 4, V.span * ease.outExpo(clamp(uv / V.sweep)));
        const k = 1 - uv / 0.075;
        p.glow(x, y, 12, C[3], 0.42 * k);
        p.glow(x, y, 6, C[5], 0.36 * k);
      }
      ps.draw(p);
    },
  };
}
