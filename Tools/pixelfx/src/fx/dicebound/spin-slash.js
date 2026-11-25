// DICE BOUND 用 (2026-11-24): melee/spin-slash の写し。 smear: false (既定) は元と 1 バイトも変わらない。
// smear: true では、一周の回転を高速化し、閉じて白く光る太い輪をちょうど 1 コマだけにして次コマで即 1/5 細輪へ落とし、点間引きで消す。
// The crescent shading, cooling ramp, dotted trace, glint and spark recipe is adapted from Sprite Fusion's
// open-source slash (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, PF, Rng, fit, sparkle, clamp, smooth, ease, rand, TAU, WHITE } from "../../core/index.js";
import { swing, onSwing, travel, smear, trace, glowAlong } from "../melee/_lib.js";

export const meta = {
  name: "Spin slash (DICE BOUND)",
  desc: "The blade whirls once round the waist on a tilted plane, behind the body and back in front; the closed ring flares white, dissolves into a dotted trace and throws sparks.",
  tags: ["dicebound", "melee", "slash", "spin", "sword", "one-shot"],
  size: [208, 104],
  origin: [104, 84],
  duration: 0.8,
  loop: false,
  fps: 30,
  bg: "ground",
  actor: 2,
  options: {
    palette: ["#16224a", "#1f4a86", "#2a7fc0", "#48b8ec", "#9fe2ff", "#e2f7ff", "#ffffff"],
    sparks: ["#fff3a6", "#ffe26b", "#ff8a2a", "#f2561d"],
    radius: 46,
    smear: false,
    seed: 3,
  },
};

export function create(o) {
  const C = fit(o.palette, 7), n = C.length, Rg = new Rng(o.seed), R0 = o.radius, sm = !!o.smear;
  const SW = sm ? 0.072 : 0.17;
  const FLARE = sm ? 0.13 : 0.3;
  const turn = (tt) => 1 - Math.pow(1 - clamp(tt / SW), 1.6);
  const passedAt = (f) => SW * (1 - Math.pow(1 - clamp(f), 1 / 1.6));
  const ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), lights = new Lights();
  const A = swing({ cx: 0, cy: -17, R: R0, sy: 0.27, rot: -0.1, a0: 0.5, dir: -1, k: 0.15 });
  const E = { ...A, R: R0 * 0.8, k: 0.1 };
  const W = sm ? 7.5 : 16, WE = sm ? 2.5 : 5;
  const DIM = [C[0], C[0], C[1], C[2], C[3], C[4], C[5]];
  let t = 0, closed = false, lastU = 0;

  const body = (p, s, w0, lag, half, rim) => {
    const U = TAU * turn(t - lag), ramp = half < 0 ? DIM : C;
    if (U < 0.02 || t - lag > SW + (sm ? 0 : 0.02)) return;
    smear(p, s, 0, U, w0, (u, r, out) => {
      const age = t - lag - passedAt(u / TAU), lead = U - u;
      const w = w0 * Math.pow(clamp(lead / 0.85), 0.5) * (0.3 + 0.7 * Math.exp(-age / (sm ? 0.035 : 0.075))) * Math.pow(clamp(u / 1.3), 0.7);
      if (w < 0.45) return;
      const q = (s.R - r) / w;
      if (q < 0 || q > 1) return;
      const ci = rim && q < 0.26 && age < 0.06 ? 0 : Math.round(q * (n - 1) * 0.7 + age * 13 + (rim ? 0.4 : 1.4));
      out.c = ramp[n - 1 - clamp(ci, 0, n - 1)];
      out.a = Math.min(1, u / 1.1 + 0.2);
    }, half);
  };

  const ring = (p, half) => {
    if (sm) {
      if (t < SW) {
        body(p, E, WE, 0.012, half, false);
        body(p, A, W, 0, half, true);
        glowAlong(p, A, A.R - 3, 0, TAU * turn(t), 6.5, C[3], (u) => (half < 0 ? 0.18 : 0.28) * Math.exp(-(t - passedAt(u / TAU)) / 0.045), half);
        return;
      }
      // 閉じた瞬間 (f4: t=0.081s, f=0.07): 太い白金の輪をちょうど 1 コマ → f5 (f=0.28) で即 1/5 細輪へ落とし点間引きで消す
      const f = (t - SW) / FLARE, ramp = half < 0 ? DIM : C;
      if (f < 1) {
        const thick = f < 0.22;
        const R = A.R + 4.5 * ease.outCubic(f);
        const th = thick ? 11 : Math.max(0.85, 2.4 * Math.pow(1 - (f - 0.22) / 0.78, 1.35) + 0.35);
        const S = { ...A, R }, step = thick ? 6 : f < 0.48 ? 5 : f < 0.74 ? 4 : 3;
        smear(p, S, 0, TAU, th, (u, r, out) => {
          const q = (R - r) / th;
          if (q < 0 || q > 1) return;
          out.c = thick ? (q < 0.55 ? WHITE : ramp[5]) : q < 0.45 ? ramp[step] : ramp[step - 1];
          out.a = f < 0.5 ? 1 : 1.5 - f;
        }, half);
        const wg = thick ? 1.0 : 0.48 + 0.52 * (1 - (f - 0.22) / 0.78);
        glowAlong(p, S, R - 1.5, 0, TAU, (thick ? 9.5 : 5.2) * wg, C[4], (thick ? 0.36 : 0.24) * (1 - f * 0.8), half);
      }
      // 点間引きの輪残像 (純点線では発光なし)
      const df = clamp((t - SW - 0.05) / 0.16);
      if (t > SW + 0.05 && df < 1) {
        const baseStep = 1.4 / Math.max(4, A.R);
        for (let idx = 0; ; idx++) {
          const u = idx * baseStep;
          if (u > TAU) break;
          const g = A.a0 + A.dir * u;
          if (half && (Math.sin(g) < 0 ? -1 : 1) !== half) continue;
          if (rand(idx * 17 + 5, 11, 29) < Math.pow(df, 0.85)) continue;
          const [x, y] = onSwing(A, A.R + 2.5, u);
          p.dot(x, y, df < 0.5 ? ramp[4] : ramp[3], Math.max(0.28, 1 - df * 0.65));
        }
      }
      return;
    }
    body(p, E, WE, 0.025, half, false);
    body(p, A, W, 0, half, true);
    if (t < SW + 0.04) glowAlong(p, A, A.R - 5, 0, TAU * turn(t), 14, C[3], (u) => (half < 0 ? 0.2 : 0.3) * Math.exp(-(t - passedAt(u / TAU)) / 0.08), half);
    if (t < SW) return;
    const f = (t - SW) / FLARE, ramp = half < 0 ? DIM : C;
    if (f < 1) {
      const R = A.R + 6 * ease.outCubic(f), th = f < 0.13 ? 9 : Math.max(1, 6 * Math.pow(1 - f, 1.5) + 0.5), S = { ...A, R };
      const step = f < 0.13 ? 6 : f < 0.3 ? 5 : f < 0.55 ? 4 : 3;
      smear(p, S, 0, TAU, th, (u, r, out) => {
        const q = (R - r) / th;
        if (q < 0 || q > 1) return;
        out.c = f < 0.13 ? (q < 0.6 ? WHITE : ramp[5]) : q < 0.45 ? ramp[step] : ramp[step - 1];
        out.a = f < 0.4 ? 1 : 1.3 - f;
      }, half);
      glowAlong(p, S, R - 2, 0, TAU, 14, C[4], 0.32 * (1 - f) * (1 - f), half);
    }
    const k = 1 - clamp((t - SW - 0.06) / 0.4);
    if (k > 0) trace(p, A, A.R - 0.5, 0, TAU, ramp[4], (u) => clamp(k * 1.5 - rand(Math.floor(u * 30), 5) * 0.8) * 0.95, half);
  };

  return {
    update(dt) {
      t += dt;
      const U = TAU * turn(t);
      if (t < SW) {
        const stepU = sm ? 1.1 : 0.3;
        for (let u = lastU; u < U; u += stepU) {
          const tr = travel(A, u), ox = tr.x - A.cx, oy = tr.y - A.cy, l = Math.hypot(ox, oy) || 1, sp = Rg.range(40, sm ? 90 : 120);
          ps.spawn(P.MOTE, tr.x, tr.y, tr.dx * sp + (ox / l) * sp * 0.35, tr.dy * sp * 0.6 - Rg.range(4, 16), sm ? Rg.range(0.09, 0.16) : Rg.range(0.22, 0.45), C[4 + Rg.int(0, 1)], 0.55, 0, Math.sin(tr.a) < 0 ? PF.BACK : 0);
        }
        lastU = U;
      }
      if (!closed && t >= SW) {
        closed = true;
        if (sm) {
          lights.add(A.cx, A.cy + 2, 16, C[3], 0.36, 0.09);
          const end = travel(A, TAU);
          for (let i = 0; i < 6; i++) {
            const a = Math.atan2(end.dy, end.dx) + Rg.range(-0.45, 0.35), sp = Rg.range(150, 310);
            ps.spawn(P.SPARK, end.x, end.y, Math.cos(a) * sp, Math.sin(a) * sp - Rg.range(10, 40), Rg.range(0.05, 0.12));
          }
        } else {
          lights.add(A.cx, A.cy + 4, 90, C[3], 0.4, 0.4);
          const end = travel(A, TAU);
          for (let i = 0; i < 9; i++) {
            const a = Math.atan2(end.dy, end.dx) + Rg.range(-0.45, 0.35), sp = Rg.range(160, 360);
            ps.spawn(P.SPARK, end.x, end.y, Math.cos(a) * sp, Math.sin(a) * sp - Rg.range(20, 70), Rg.range(0.14, 0.36));
          }
          for (let i = 0; i < 16; i++) {
            const u = Rg.range(0, TAU), tr = travel(A, u), sp = Rg.range(110, 260);
            ps.spawn(P.SPARK, tr.x, tr.y, tr.dx * sp, tr.dy * sp - Rg.range(20, 80), Rg.range(0.12, 0.32), 0, 1, 0, Math.sin(tr.a) < 0 ? PF.BACK : 0);
          }
          for (let i = 0; i < 6; i++) {
            const u = Rg.range(0, TAU), [x, y] = onSwing(A, A.R + Rg.range(-1, 3), u);
            ps.spawn(P.STAR, x, y, 0, -Rg.range(2, 8), Rg.range(0.25, 0.5), Rg.chance(0.5) ? C[5] : WHITE, Rg.range(1.2, 2.6), Rg.range(0.04, 0.16), Math.sin(A.a0 + A.dir * u) < 0 ? PF.BACK : 0);
          }
        }
      }
      ps.update(dt);
      if (sm) for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 6.5 * dt; m.vy -= m.vy * 6.5 * dt; }
      lights.update(dt);
    },
    drawBack(p) {
      ring(p, -1);
      ps.draw(p, "back");
    },
    draw(p) {
      ring(p, 1);
      if (sm) {
        const g = t - SW * 0.85, tip = onSwing(A, A.R - 3, TAU);
        if (g > 0 && g < 0.085) sparkle(p, tip[0], tip[1], 7 * ease.outBack(Math.min(1, g / 0.02)) * (1 - g / 0.085), C[5], C[3], 0.55);
        if (t > 0 && t < SW + 0.03) {
          const U = TAU * turn(t), [x, y] = onSwing(A, A.R - 3, U), far = Math.sin(A.a0 + A.dir * U) < 0;
          p.glow(x, y, far ? 8 : 11, C[3], (far ? 0.24 : 0.36) * (1 - smooth(SW - 0.02, SW + 0.03, t)));
        }
        ps.draw(p, "front");
        lights.draw(p);
        return;
      }
      if (t < 0.06) {
        const [x, y] = onSwing(A, A.R - 3, 0);
        sparkle(p, x, y, 7 * (1 - t / 0.06), C[5], C[3], 0.5);
      }
      const g = t - SW * 0.85, tip = onSwing(A, A.R - 3, TAU);
      if (g > 0 && g < 0.24) sparkle(p, tip[0], tip[1], 10 * ease.outBack(Math.min(1, g / 0.05)) * (1 - g / 0.24), C[5], C[3], 0.6);
      if (t < SW + 0.05) {
        const U = TAU * turn(t), [x, y] = onSwing(A, A.R - 5, U), far = Math.sin(A.a0 + A.dir * U) < 0;
        p.glow(x, y, far ? 26 : 38, C[3], (far ? 0.3 : 0.5) * (1 - smooth(SW - 0.04, SW + 0.05, t)));
      }
      ps.draw(p, "front");
      lights.draw(p);
    },
  };
}
