// DICE BOUND 用 (2026-11-24): melee/thrust の写し (盾の叩きつけ用に 90 度回して使う)。
// smear: false (既定) は melee/thrust と 1 バイトも変わらない。
// smear: true では、K 倍拡大時に点々になる両脇と芯の筋を p.line の鋭い直線の束に整え、着弾光を絞り、末尾を点間引きで消す。
// The speed lines are adapted from Sprite Fusion's open-source dash and the ray star from his sparkle
// (https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, fit, sparkle, clamp, smooth, ease, rand, TAU, WHITE } from "../../core/index.js";

export const meta = {
  name: "Slam thrust (DICE BOUND)",
  desc: "A piercing stab: a bundle of streaks shoots out from the hand to a point, a long four-point star flares at the tip with a pierce ring, sparks and speed lines.",
  tags: ["dicebound", "melee", "thrust", "shield", "one-shot"],
  size: [196, 76],
  origin: [34, 62],
  duration: 0.75,
  loop: false,
  fps: 30,
  bg: "ground",
  actor: 2,
  options: {
    palette: ["#2a1608", "#6b3c10", "#b8701a", "#f0b42e", "#ffe27a", "#fff6cf", "#ffffff"],
    sparks: ["#fff3a6", "#ffe26b", "#ff8a2a", "#f2561d"],
    reach: 66,
    smear: false,
    crit: false,
    seed: 4,
  },
};

function longStar(p, x, y, L, k, C, sharp = false) {
  if (L < 1) return;
  const arms = [[1, 0, L], [-1, 0, L * 0.7], [0, -1, L * 0.42], [0, 1, L * 0.42]];
  if (sharp) {
    for (const [dx, dy, len] of arms) {
      const m1 = len * 0.32, m2 = len * 0.68;
      p.line(x, y, x + dx * m1, y + dy * m1, WHITE, k);
      p.line(x + dx * m1, y + dy * m1, x + dx * m2, y + dy * m2, C[5], k * 0.9);
      p.line(x + dx * m2, y + dy * m2, x + dx * len, y + dy * len, C[4], k * 0.65);
    }
    p.px(x, y, WHITE);
    p.glow(x, y, 3.5 + L * 0.35, C[4], 0.28 * k);
    p.glow(x, y, 2 + L * 0.16, WHITE, 0.24 * k);
    return;
  }
  for (const [dx, dy, len] of arms)
    for (let d = 1; d <= len; d += 0.5) {
      const u = d / len, c = u < 0.3 ? WHITE : u < 0.65 ? C[5] : C[4];
      p.dot(x + dx * d, y + dy * d, c, u < 0.7 ? 1 : 1 - (u - 0.7) / 0.35);
      if (u < 0.25 && len > 6) (p.dot(x + dx * d - dy, y + dy * d + dx, C[5]), p.dot(x + dx * d + dy, y + dy * d - dx, C[5]));
    }
  for (const [dx, dy] of [[1, 1], [1, -1], [-1, 1], [-1, -1]]) for (let d = 1; d <= Math.min(3, L * 0.14); d++) p.px(x + dx * d, y + dy * d, d < 2 ? C[5] : C[4]);
  p.px(x, y, WHITE);
  p.glow(x, y, 6 + L * 1.3, C[4], 0.35 * k);
  p.glow(x, y, 3 + L * 0.4, WHITE, 0.3 * k);
}

// 鋭い 1 本の直線筋 (尾が暗く、先端ほど白く尖る)。 fade > 0 で端から点間引きで消える
function sharpStreak(p, x0, y0, x1, y1, C, k = 1, hot = false, fade = 0, id = 0) {
  const dx = x1 - x0, dy = y1 - y0, L = Math.hypot(dx, dy);
  if (L < 1 || k <= 0.02) return;
  if (fade > 0.35) {
    // 終盤は点間引きで滑らかに消す
    const df = clamp((fade - 0.35) / 0.65);
    const n = Math.max(2, Math.ceil(L * 0.8));
    for (let i = 0; i <= n; i++) {
      const u = i / n;
      if (u < df * 0.65) continue;
      if (rand(i + id * 31, id * 7 + 3, 17) < Math.pow(df, 0.85) / (0.25 + 0.75 * u)) continue;
      p.dot(x0 + dx * u, y0 + dy * u, u > 0.7 ? C[5] : C[3], Math.max(0.3, (1 - df * 0.65) * k));
    }
    return;
  }
  const a1 = 0.42, a2 = 0.82;
  const mx1 = x0 + dx * a1, my1 = y0 + dy * a1, mx2 = x0 + dx * a2, my2 = y0 + dy * a2;
  p.line(x0, y0, mx1, my1, hot ? C[5] : C[3], k * 0.65);
  p.line(mx1, my1, mx2, my2, hot ? C[6] : C[5], k * 0.92);
  p.line(mx2, my2, x1, y1, WHITE, k);
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), lights = new Lights();
  const sm = !!o.smear, crit = !!o.crit;
  const OUT = sm ? 0.056 : 0.07, HOLD = sm ? 0.014 : 0.06, PULL = sm ? 0.095 : 0.2;
  const HX = 9, HY = -20, TX = HX + o.reach, TY = HY;
  const streaks = sm
    ? [-9, -6.5, -4, -2, 0, 2, 4, 6.5, 9].map((off, idx) => ({
        off,
        x0: HX - 8 + Math.abs(off) * 0.9 + Rg.range(0, 4),
        t0: Math.abs(off) * 0.0012,
        w: off === 0 ? 1.5 : 1,
        hot: Math.abs(off) <= 2,
        len: 1 - Math.abs(off) * 0.018 - Rg.range(0, 0.08),
        id: idx + 1,
      }))
    : [-7, -4, -2, 0, 2, 4, 7].map((off) => ({ off, x0: HX + 1 + Rg.range(0, 5) + Math.abs(off) * 0.6, t0: Rg.range(0, 0.02) + Math.abs(off) * 0.003, w: off === 0 ? 2 : 1, hot: off === 0, len: Rg.range(0.75, 1) }));
  // smear: 両脇の鋭い直線の束 (太い柱の左右に並ぶ連続直線)
  const flankLines = sm
    ? [-18.5, -15.5, -13, -10.5, 10.5, 13, 15.5, 18.5].map((off, idx) => ({
        x0: HX + 6 + Rg.range(0, 18),
        y: HY + off * (crit ? 1.08 : 1),
        len: Rg.range(26, 46),
        v: Rg.range(360, 520),
        t0: Rg.range(0.0, 0.022),
        dur: Rg.range(0.11, 0.16),
        id: 20 + idx,
      }))
    : [];
  const lines = [];
  let t = 0, struck = false;
  return {
    update(dt) {
      t += dt;
      if (!sm) {
        if (t < OUT + 0.08 && Rg.chance(0.5)) {
          const s = Rg.sign();
          lines.push({ x: HX + Rg.range(0, o.reach * 0.6), y: HY + s * Rg.range(7, 17), l: Rg.range(18, 42), v: Rg.range(200, 340), t });
        }
        for (let i = lines.length - 1; i >= 0; i--) if (t - lines[i].t > 0.24) lines.splice(i, 1);
        for (const l of lines) l.x += l.v * dt;
      }
      if (!struck && t >= OUT) {
        struck = true;
        if (sm) {
          lights.add(TX, TY, crit ? 11 : 9, C[4], crit ? 0.38 : 0.30, 0.08);
          lights.add(TX, TY, crit ? 6 : 5, WHITE, crit ? 0.50 : 0.40, 0.04);
          for (let i = 0; i < (crit ? 8 : 6); i++) {
            const a = Rg.range(-0.6, 0.6), sp = Rg.range(140, 290);
            ps.spawn(P.SPARK, TX, TY, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.05, 0.12));
          }
          const lives = [0.10, 0.13, 0.16, 0.19];
          for (let i = 0; i < lives.length; i++) {
            const s = i % 2 ? 1 : -1;
            ps.spawn(P.MOTE, TX - Rg.range(2, 10), TY + s * Rg.range(3, 10), Rg.range(40, 85), s * Rg.range(25, 65), lives[i], i & 1 ? C[5] : C[4], 0.55);
          }
        } else {
          lights.add(TX, TY, 44, C[4], 0.45, 0.3);
          lights.add(TX, TY, 14, WHITE, 0.6, 0.06);
          for (let i = 0; i < 12; i++) {
            const a = Rg.range(-0.55, 0.45), sp = Rg.range(150, 330);
            ps.spawn(P.SPARK, TX, TY, Math.cos(a) * sp, Math.sin(a) * sp - 30, Rg.range(0.12, 0.35));
          }
          for (let i = 0; i < 10; i++) {
            const a = Rg.range(-0.9, 0.9) + (Rg.chance(0.3) ? Math.PI : 0), sp = Rg.range(40, 120);
            ps.spawn(P.MOTE, TX + Rg.range(-6, 2), TY + Rg.range(-2, 2), Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.25, 0.5), Rg.chance(0.5) ? C[5] : C[4], 0.6);
          }
          for (let i = 0; i < 4; i++) ps.spawn(P.STAR, TX + Rg.range(-10, 8), TY + Rg.range(-9, 9), 0, -3, Rg.range(0.2, 0.4), Rg.chance(0.5) ? C[5] : WHITE, Rg.range(1, 2), Rg.range(0.06, 0.2));
        }
      }
      ps.update(dt);
      if (sm) for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 7 * dt; m.vy -= m.vy * 7 * dt; }
      lights.update(dt);
    },
    draw(p) {
      if (sm) {
        // 両脇の鋭い直線の束 (p.line で連続直線として描く)
        for (const fl of flankLines) {
          const lt = t - fl.t0;
          if (lt < 0 || lt > fl.dur) continue;
          const k = lt / fl.dur;
          const headX = Math.min(TX + 4, fl.x0 + fl.v * lt);
          const curLen = fl.len * (1 - k * 0.55);
          const tailX = headX - curLen;
          sharpStreak(p, tailX, fl.y, headX, fl.y, C, (1 - k) * (crit ? 0.95 : 0.85), false, k, fl.id);
        }
        // 中央の収束する鋭い直線の束
        const pull = smooth(OUT + HOLD, OUT + HOLD + PULL, t);
        for (const s of streaks) {
          const u = clamp((t - s.t0) / OUT), head = ease.outExpo(u);
          if (u <= 0 || pull >= 1) continue;
          const y0 = HY + s.off, end = s.x0 + (TX - s.x0) * s.len;
          const x1 = s.x0 + (end - s.x0) * head;
          const xa = s.x0 + (end - s.x0) * Math.max(pull, head * 0.22 * (1 - s.len) * 3);
          const yAt = (x) => y0 + (TY - y0) * clamp((x - s.x0) / (TX - s.x0)) * 0.78;
          sharpStreak(p, xa, yAt(xa), x1, yAt(x1), C, 1 - pull * 0.5, s.hot, pull, s.id);
        }
        if (t < OUT + HOLD + PULL * 0.6) {
          const pk = 1 - clamp((t - OUT) / (HOLD + PULL * 0.6));
          p.glowLine(HX + (TX - HX) * pull, HY, HX + (TX - HX) * ease.outExpo(clamp(t / OUT)), TY, crit ? 4.5 : 3.5, C[4], (crit ? 0.24 : 0.18) * pk);
        }
        // 着弾の星とリング (短く鋭く)
        const q = t - OUT;
        if (q >= 0 && q < 0.15) {
          const g = ease.outBack(clamp(q / 0.025), 2.0) * (1 - smooth(0.03, 0.15, q));
          longStar(p, TX, TY, (crit ? 19 : 16) * g, 1 - q / 0.15, C, true);
        }
        if (q >= 0 && q < 0.09) {
          const k = q / 0.09, rx = 2 + 5 * ease.outCubic(k), ry = 4 + 10 * ease.outCubic(k);
          for (let a = 0; a < TAU; a += 0.08) p.dot(TX + 3 + 6 * k + Math.cos(a) * rx, TY + Math.sin(a) * ry, k < 0.35 ? WHITE : C[5], 1 - k);
        }
        ps.draw(p);
        lights.draw(p);
        return;
      }
      if (t < 0.05) sparkle(p, HX + 2, HY, 5 * (1 - t / 0.05) + 1, C[5], C[3], 0.4);
      for (const l of lines) {
        const k = (t - l.t) / 0.24;
        for (let d = 0; d < l.l * (1 - k * 0.6); d++) p.dot(l.x - d, l.y, d < 3 ? WHITE : d < l.l * 0.4 ? C[5] : C[3], (1 - d / l.l) * (1 - k));
      }
      const pull = smooth(OUT + HOLD, OUT + HOLD + PULL, t);
      for (const s of streaks) {
        const u = clamp((t - s.t0) / OUT), head = ease.outExpo(u);
        if (u <= 0 || pull >= 1) continue;
        const y0 = HY + s.off, end = s.x0 + (TX - s.x0) * s.len, x1 = s.x0 + (end - s.x0) * head, xa = s.x0 + (end - s.x0) * Math.max(pull, head * 0.35 * (1 - s.len) * 3);
        const yAt = (x) => y0 + (TY - y0) * clamp((x - s.x0) / (TX - s.x0)) * 0.85;
        for (let x = xa; x <= x1; x += 0.5) {
          const v = (x - xa) / Math.max(1, x1 - xa), c = v > 0.88 ? WHITE : s.hot ? (v > 0.35 ? C[6] : C[5]) : v > 0.55 ? C[5] : v > 0.25 ? C[4] : C[3];
          p.dot(x, yAt(x), c, v < 0.12 ? v / 0.12 : 1);
          if (s.w > 1 && v > 0.1) p.dot(x, yAt(x) + 1, v > 0.6 ? C[5] : C[3], v < 0.2 ? v / 0.2 : 1);
        }
      }
      if (t < OUT + HOLD + PULL) p.glowLine(HX + (TX - HX) * pull, HY, HX + (TX - HX) * ease.outExpo(clamp(t / OUT)), TY, 6, C[4], 0.22 * (1 - pull));
      const q = t - OUT;
      if (q >= 0 && q < 0.4) {
        const g = ease.outBack(clamp(q / 0.04), 2.4) * (1 - smooth(0.08, 0.4, q)) * (1 + 0.12 * Math.sin(q * 70));
        longStar(p, TX, TY, 24 * g, 1 - q / 0.4, C);
      }
      if (q >= 0 && q < 0.2) {
        const k = q / 0.2, rx = 2 + 7 * ease.outCubic(k), ry = 4 + 12 * ease.outCubic(k);
        for (let a = 0; a < TAU; a += 0.05) p.dot(TX + 4 + 10 * k + Math.cos(a) * rx, TY + Math.sin(a) * ry, k < 0.3 ? WHITE : C[5], 1.15 - k);
      }
      ps.draw(p);
      lights.draw(p);
    },
  };
}
