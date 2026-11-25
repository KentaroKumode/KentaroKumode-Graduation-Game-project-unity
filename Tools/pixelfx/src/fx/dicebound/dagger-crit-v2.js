// DICE BOUND 短剣・会心リワーク案 (dagger-crit-v2・holy 金):
//   段 1 (f2〜f10): 敵の急所を 5 方向から貫く高速交差連斬 (星陣の糸を張るように、各 1 コマ太面 → 細い金の糸を残す)。
//   段 2 (f11〜f12): 全糸が交わる中心が一瞬光り、真横一閃の極太断ち切り＋melee/thrust 着想の貫通星 (1 コマ) で一息に決める。
import { Particles, Lights, P, Rng, fit, sparkle, clamp, ease, rand, TAU, WHITE } from "../../core/index.js";
import { through, swing, slashEnd, travel, glowAlong } from "../melee/_lib.js";
import { glowSlash } from "./_blade.js";
import { ringSlash } from "./cross-slash.js";

export const meta = {
  name: "Dagger crit v2 (DICE BOUND)",
  desc: "Five rapid intersecting slashes weave a razor-thin golden star web across the target, then converge into a single wide piercing execution flash.",
  tags: ["dicebound", "player", "melee", "dagger", "crit", "one-shot"],
  size: [180, 120],
  origin: [90, 60],
  duration: 1.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#3b1d2a", "#7a3a2c", "#c4652e", "#f0a040", "#ffd25e", "#fff0a0", "#ffffff"],
    sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"],
    flow: 2.8,
    seed: 17,
  },
};

// melee/thrust 着想の鋭い 4 点貫通星 (p.line で拡大後も欠けない)
function pierceStar(p, x, y, L, k, C) {
  if (L < 1 || k <= 0.02) return;
  const arms = [[1, 0, L * 1.15], [-1, 0, L * 1.15], [0, -1, L * 0.48], [0, 1, L * 0.48]];
  for (const [dx, dy, len] of arms) {
    const m1 = len * 0.35, m2 = len * 0.72;
    p.line(x, y, x + dx * m1, y + dy * m1, WHITE, k);
    p.line(x + dx * m1, y + dy * m1, x + dx * m2, y + dy * m2, C[5], k * 0.9);
    p.line(x + dx * m2, y + dy * m2, x + dx * len, y + dy * len, C[4], k * 0.65);
  }
  p.px(x, y, WHITE);
  p.glow(x, y, 4 + L * 0.25, C[4], 0.32 * k);
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), lights = new Lights();
  // 段 1: 急所 (0, 0) を貫く 5 方向の高速交差斬り (speed=0.72, dt=0.030s → 間隔 0.060s で f2, f4, f6, f8, f10 が各 1 コマ太面)
  const ANGLES = [
    [0.52, 1, -2, -2],
    [Math.PI - 0.52, -1, 2, -2],
    [-0.18, 1, 0, 2],
    [Math.PI + 0.82, -1, -2, 1],
    [-0.82, 1, 2, 1],
  ];
  const cuts = ANGLES.map(([th, dir, ox, oy], i) => through(ox, oy, th, dir, 72, 0.76, 0.5, {
    W: 9.5, t0: 0.005 + i * 0.060, sweep: 0.014, hold: 0.017, trail: 0.24 - i * 0.02, ramp: C, lens: true,
    snap: true, snapDur: 0.006, snapCut: 0.85, glowScale: 0.95, traceFade: 0.09, dotFade: true,
  }));
  // 段 2: 収束穿ちの一閃 (f12: t=0.330s に 1 コマ太面 → f13 で極細の金線へ即落ちる)
  const FIN_T0 = 0.305;
  const FIN = swing({
    cx: 0, cy: 2, R: 54, sy: 0.24, k: 0.14, rot: -0.06, a0: 0.14, dir: 1, span: 2.95,
    W: 21, t0: FIN_T0, sweep: 0.018, hold: 0.013, trail: 0.15, ramp: C, sharp: true, wideSnap: true, flareDur: 0.125,
  });
  // 横に走る鋭い速度線 (f12〜f14)
  const speedLines = [-10, -5, 5, 10].map((y, idx) => ({ y, x0: -48 + idx * 6, len: Rg.range(24, 42), id: idx + 1 }));
  const ends = cuts.map(slashEnd), done = cuts.map(() => false);
  let t = 0, finDone = false;

  return {
    update(dt) {
      t += dt;
      cuts.forEach((s, i) => {
        if (done[i] || t < s.t0 + s.sweep) return;
        done[i] = true;
        const e = ends[i];
        lights.add(e.x, e.y, 7, C[4], 0.24, 0.04);
        for (let k = 0; k < 2; k++) {
          const a = Math.atan2(e.dy, e.dx) + Rg.range(-0.4, 0.4), sp = Rg.range(120, 230);
          ps.spawn(P.SPARK, e.x, e.y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.04, 0.08));
        }
      });
      if (!finDone && t >= FIN_T0 + FIN.sweep) {
        finDone = true;
        lights.add(0, 0, 12, C[4], 0.42, 0.08);
        lights.add(0, 0, 6.5, WHITE, 0.55, 0.04);
        for (let k = 0; k < 10; k++) {
          const s = k % 2 ? 1 : -1;
          const a = (s > 0 ? 0 : Math.PI) + Rg.range(-0.35, 0.35), sp = Rg.range(160, 340);
          ps.spawn(P.SPARK, s * 10, Rg.range(-2, 2), Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.06, 0.14));
        }
        const lives = [0.13, 0.165, 0.20, 0.235], fracs = [0.25, 0.45, 0.70, 0.90];
        for (let k = 0; k < lives.length; k++) {
          const tr = travel(FIN, FIN.span * fracs[k]), sp = Rg.range(45, 85);
          ps.spawn(P.MOTE, tr.x, tr.y, tr.dx * sp, tr.dy * sp, lives[k], k & 1 ? C[5] : C[4], 0.55);
        }
      }
      ps.update(dt);
      for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 6.5 * dt; m.vy -= m.vy * 6.5 * dt; }
      lights.update(dt);
    },
    draw(p) {
      // 段 1: 5 本の交差斬り (細線が急所に交差して残る)
      for (const s of cuts) glowSlash(p, s, t, o.flow, 0.95);
      // f11 (t=0.300s): 5 本が交わる中心が一瞬だけ鋭く光る (溜めの一拍)
      if (t >= 0.282 && t < FIN_T0 + 0.015) {
        const k = 1 - Math.abs((t - 0.300) / 0.022);
        if (k > 0) sparkle(p, 0, 0, 10 * k, WHITE, C[4], 0.65 * k);
      }
      // 段 2: 決めの横一閃 (f12 で 1 コマ太面 → f13 で極細の金線へ即落ちる)
      if (t >= FIN_T0) {
        const tF = FIN_T0 + 0.038;
        ringSlash(p, FIN, t, tF);
        const f = (t - tF) / FIN.flareDur;
        if (t > FIN_T0 + 0.008 && f > -0.3 && f < 1) {
          glowAlong(p, FIN, FIN.R - 1, 0, FIN.span, 5.5, C[6], (uu) => 0.32 * (1 - Math.max(0, f)) * Math.pow(Math.sin(Math.PI * uu / FIN.span), 1.6), 0, 0.14);
        }
      }
      // 決めの瞬間: 鋭い 4 点貫通星＋横の速度線＋貫通リング
      const q = t - FIN_T0 - 0.012;
      if (q >= 0 && q < 0.12) {
        const k = q / 0.12;
        const g = ease.outBack(clamp(q / 0.02), 1.8) * (1 - k);
        pierceStar(p, 0, 0, 32 * g, 1 - k, C);
        if (k < 0.7) {
          const rk = k / 0.7, rx = 4 + 12 * ease.outCubic(rk), ry = 8 + 18 * ease.outCubic(rk);
          for (let a = 0; a < TAU; a += 0.1) p.dot(Math.cos(a) * rx, Math.sin(a) * ry, rk < 0.35 ? WHITE : C[5], 1 - rk);
        }
        for (const sl of speedLines) {
          const x0 = sl.x0 + 95 * k, x1 = x0 + sl.len * (1 - k * 0.5);
          if (k > 0.55) {
            const df = (k - 0.55) / 0.45;
            for (let i = 0; i < 8; i++) if (rand(i * 13 + sl.id * 7, 5, 19) > df) p.dot(x0 + ((x1 - x0) * i) / 7, sl.y, C[4], 0.7 * (1 - df));
          } else {
            p.line(x0, sl.y, x1, sl.y, k < 0.3 ? WHITE : C[5], 1 - k);
          }
        }
      }
      ps.draw(p);
      lights.draw(p);
    },
  };
}
