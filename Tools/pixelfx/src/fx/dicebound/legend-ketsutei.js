// DICE BOUND 段 4 LEGENDARY 斧「血帝廻天」v4:
//   通常 (crit=false): v3 から 1 バイトも変更なし。
//   会心 (crit=true): 空中の太いドーナツ輪 (RING / RING_IN) を完全削除し、衝撃波は地面 (足もとの輪と割れ) のみに整理。
//     溜め (f2〜f6: 5 コマ) と極太の縦振り下ろし (f7: 1 コマ太面) は維持しつつ、空中輪を削った分だけ地面の灼け (灼熱地割れ 12 本＋地表面の灼け光帯＋2 重の大地面衝撃波) と血の筋 (着弾 24 本＋f9 第二波噴き上げ 10 本) を強化して通常との明確な差を作る。
import { Particles, P, Rng, fit, sparkle, band, clamp, ease, rand, TAU } from "../../core/index.js";
import { through, onSwing, travel, slashEnd } from "../melee/_lib.js";
import { makeCracks, SOOT } from "../impacts/_lib.js";
import { glowSlash } from "./_blade.js";

export const meta = {
  name: "Ketsutei Kaiten v4 (DICE BOUND Legendary Axe)",
  desc: "Crimson dusk blood-axe chop. Normal unchanged from v3; Critical removes all airborne rings and focuses all impact power into the 5-frame crimson windup, colossal vertical chop, intense ground scorch/cracks/floor shockwaves, and erupting blood ribbons.",
  tags: ["dicebound", "player", "legend", "axe", "one-shot"],
  size: [210, 160],
  origin: [105, 134],
  duration: 1.15,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1a1821", "#43404f", "#690612", "#8d0e1c", "#af1d2d", "#c4b7f0", "#e4def9"],
    sparks: ["#e4def9", "#c4b7f0", "#af1d2d", "#8d0e1c"],
    crit: false,
    flow: 2.5,
    seed: 19,
  },
};

// 空気が赤く灼ける (通常は v3 と完全同一・会心は広域かつ濃く)
function duskAirWash(p, C, k, wide = false) {
  if (k <= 0.02) return;
  const rx = wide ? 104 : 84, topY = wide ? -124 : -108, botY = wide ? 14 : 6;
  const peakA = wide ? 0.60 : 0.42;
  for (let y = topY; y <= botY; y += 2) {
    const vy = 1 - Math.abs((y + 52) / (wide ? 78 : 62));
    if (vy <= 0) continue;
    for (let x = -rx + (y & 2 ? 1 : 0); x <= rx; x += 2) {
      const vx = 1 - Math.abs(x / (rx + 4));
      if (vx <= 0) continue;
      const a = k * Math.pow(vx * vy, 0.65) * peakA;
      if (a > 0.04) p.dot(x, y, a > 0.24 ? C[4] : C[3], a);
    }
  }
}

// 会心専用: 地面が広く深く灼け割れる大亀裂＋地表面の灼け残り (点間引き消滅)
function drawScorchedGround(p, cracks, t, t0, dur, C) {
  const u = (t - t0) / dur;
  if (u < 0 || u >= 1) return;
  const grow = ease.outCubic(clamp(u / 0.14));
  const heat = clamp(1 - u / 0.52);
  const df = clamp((u - 0.44) / 0.56);

  // 地表面の灼熱の焼け跡 (楕円の網点で足もとが赤く灼け残る)
  if (df < 0.72) {
    const sa = (1 - df / 0.72) * (0.42 + 0.35 * heat);
    const rx = 86 * grow, ry = 17 * grow;
    for (let y = Math.floor(-ry); y <= Math.ceil(ry); y += 2) {
      const ny = y / Math.max(1, ry);
      for (let x = Math.floor(-rx) + (y & 2 ? 1 : 0); x <= Math.ceil(rx); x += 2) {
        const nx = x / Math.max(1, rx), d = Math.hypot(nx, ny);
        if (d > 1) continue;
        const k = (1 - d * d) * sa;
        if (k > 0.06) p.dot(x, y, d < 0.35 && heat > 0.45 ? C[5] : d < 0.7 ? C[4] : C[3], k);
      }
    }
  }

  for (let ci = 0; ci < cracks.length; ci++) {
    const c = cracks[ci], G = c.len * grow;
    for (const path of [c.pts, ...c.kids]) {
      for (let i = 1; i < path.length; i++) {
        const [x0, y0, d0] = path[i - 1], [x1, y1, d1] = path[i];
        if (d0 >= G) break;
        const frac = d1 > G ? (G - d0) / (d1 - d0) : 1;
        const ex = x0 + (x1 - x0) * frac, ey = y0 + (y1 - y0) * frac;
        if (df > 0) {
          const steps = Math.max(1, Math.ceil(Math.hypot(ex - x0, ey - y0) * 1.1));
          for (let s = 0; s <= steps; s++) {
            const k = s / steps, x = x0 + (ex - x0) * k, y = y0 + (ey - y0) * k;
            const d = d0 + (d1 - d0) * frac * k;
            const keep = 0.25 + 0.75 * clamp(1 - d / Math.max(6, c.len));
            if (rand(Math.round(x * 3) + ci * 19, Math.round(y * 3) + i * 11, 29) < Math.pow(df, 0.85) / keep) continue;
            p.dot(x, y, df < 0.45 ? C[4] : C[2], Math.max(0.3, 1 - df * 0.65));
          }
        } else {
          const col = heat > 0.65 ? C[6] : heat > 0.35 ? C[5] : heat > 0.12 ? C[4] : SOOT;
          p.line(x0, y0, ex, ey, col, 1);
          p.line(x0, y0 + 1, ex, ey + 1, C[3], 0.8);
          if (heat > 0.22 && (i & 1) === 0) p.glow((x0 + ex) * 0.5, (y0 + ey) * 0.5, 5.2, C[4], 0.28 * heat);
        }
      }
    }
  }
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), crit = !!o.crit;
  // speed=0.65 → 1 コマ = 0.02708s
  // 通常 (v3 と 100% 同一): T1=0 → f2=0.0271s (1 コマ太面) → f3 細縁
  // 会心 (v4): 溜め f2〜f6 (5 コマ) → T1=0.140 → f7=0.1625s (極太 1 コマ太面) → f8 細縁＋地面の衝撃波と大灼け＆血の筋噴き上げ (空中輪なし)
  const T1 = crit ? 0.140 : 0;
  const A = through(0, -48, Math.PI / 2, 1, 155, 0.70, 0.5, {
    W: crit ? 40 : 27, t0: T1, sweep: 0.020, hold: 0.011, trail: crit ? 0.20 : 0.16, ramp: C,
    snap: true, snapDur: 0.014, snapCut: 0.84, glowScale: crit ? 1.38 : 1.05, traceFade: crit ? 0.16 : 0.13, dotFade: true,
  });
  const E = {
    ...A, R: A.R - A.W * (crit ? 0.68 : 0.70), W: A.W * (crit ? 0.40 : 0.34), t0: T1 + (crit ? 0.004 : 0.005), sweep: 0.018, hold: 0.008, trail: 0.06, ramp: C.slice(0, 6),
    snap: true, snapDur: 0.012, snapCut: 0.86, glowScale: crit ? 1.02 : 0.78, traceFade: 0.015, dotFade: false,
  };

  // 会心のみ: 地面の亀裂を 12 本へ増強 (空中ドーナツ輪は完全削除)
  const cracks = crit ? makeCracks(Rg, 12, { len: [38, 102], dive: 0.46, maxY: 22, forks: 0.70, x0: [3, 10] }) : null;
  const chargeStreams = crit ? Array.from({ length: 12 }, (_, i) => ({
    a0: (i / 12) * TAU + Rg.range(-0.12, 0.12),
    r0: Rg.range(36, 62),
    spin: (i % 2 ? 1 : -1) * Rg.range(0.75, 1.25),
  })) : [];
  const T_SURGE = crit ? 0.205 : -1; // f9: 地面の裂け目から第二波の血の筋が噴き上がる

  const endA = slashEnd(A);
  const wisps = [];
  const spawnBladeWisps = (s, count) => {
    for (let i = 0; i < count; i++) {
      const fr = 0.12 + (i / Math.max(1, count - 1)) * 0.82;
      const tr = travel(s, s.span * fr);
      const side = i % 2 ? 1 : -1;
      wisps.push({
        x: tr.x + side * 2, y: tr.y,
        vx: side * Rg.range(crit ? 115 : 68, crit ? 245 : 150) + tr.dx * 25,
        vy: -Rg.range(24, crit ? 125 : 72) + tr.dy * 10,
        life: Rg.range(0.20, crit ? 0.36 : 0.33), age: 0, seed: Rg.range(0, 100), pts: [[tr.x, tr.y]], id: wisps.length + 1,
      });
    }
  };
  const spawnGroundBloodSurge = (count) => {
    for (let i = 0; i < count; i++) {
      const side = i % 2 ? 1 : -1;
      const x0 = side * Rg.range(12, 68), y0 = Rg.range(-4, 6);
      wisps.push({
        x: x0, y: y0,
        vx: side * Rg.range(90, 210),
        vy: -Rg.range(95, 195),
        life: Rg.range(0.20, 0.32), age: 0, seed: Rg.range(0, 100), pts: [[x0, y0]], id: wisps.length + 1,
      });
    }
  };

  let t = 0, landedA = false, surged = false;
  return {
    update(dt) {
      t += dt;
      if (!landedA && t >= T1 + A.sweep) {
        landedA = true;
        spawnBladeWisps(A, crit ? 24 : 12);
        for (let i = 0; i < (crit ? 24 : 10); i++) {
          const a = (Rg.chance(0.5) ? 0 : Math.PI) + Rg.range(-0.55, 0.4), sp = Rg.range(150, crit ? 420 : 340);
          ps.spawn(P.SPARK, endA.x, endA.y, Math.cos(a) * sp, Math.sin(a) * sp - Rg.range(18, crit ? 110 : 80), Rg.range(0.06, 0.16));
        }
        const lives = [0.13, 0.165, 0.20, 0.24], fracs = [0.45, 0.62, 0.80, 0.96];
        for (let i = 0; i < lives.length; i++) {
          const tr = travel(A, A.span * fracs[i]), sp = Rg.range(50, 95);
          ps.spawn(P.MOTE, tr.x, tr.y, tr.dx * sp, tr.dy * sp, lives[i], i & 1 ? C[5] : C[4], 0.6);
        }
      }
      if (crit && !surged && t >= T_SURGE) {
        surged = true;
        spawnGroundBloodSurge(10);
      }
      for (let i = wisps.length - 1; i >= 0; i--) {
        const w = wisps[i];
        w.age += dt;
        if (w.age > w.life) { wisps.splice(i, 1); continue; }
        w.vx += Math.sin(w.age * 16 + w.seed) * 105 * dt;
        w.vy -= 26 * dt;
        w.vx -= w.vx * 4.0 * dt;
        w.vy -= w.vy * 4.0 * dt;
        w.x += w.vx * dt;
        w.y += w.vy * dt;
        w.pts.push([w.x, w.y]);
        if (w.pts.length > (crit ? 15 : 13)) w.pts.shift();
      }
      ps.update(dt);
      for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 6.5 * dt; m.vy -= m.vy * 6.5 * dt; }
    },
    draw(p) {
      // ---- 会心溜め (f2〜f6: 空気が赤く灼け、血が可視域の頭上 (0, -52) の大斧刃先へ渦巻き集束＋足もと予兆環) ----
      if (crit && t > 0.01 && t < T1 + 0.012) {
        const u = clamp((t - 0.012) / (T1 - 0.012));
        duskAirWash(p, C, 0.42 + 0.65 * ease.inQuad(u), true);
        const gx = 0, gy = -52;
        const cRingR = 44 * (1.18 - 0.88 * ease.inQuad(u));
        const crSteps = 44;
        for (let i = 0; i < crSteps; i++) {
          const a = (i / crSteps) * TAU + u * 0.6;
          const col = u > 0.62 ? C[6] : u > 0.30 ? C[5] : C[4];
          p.dot(gx + Math.cos(a) * cRingR, gy + Math.sin(a) * cRingR * 0.55, col, 0.65 + 0.35 * u);
        }
        for (const st of chargeStreams) {
          const r1 = st.r0 * (1 - ease.inQuad(u)) + 2;
          const r0 = r1 + 16 * (1 - u * 0.3);
          const a1 = st.a0 + st.spin * u;
          const a0 = st.a0 + st.spin * Math.max(0, u - 0.22);
          const col = u > 0.55 ? C[6] : u > 0.22 ? C[5] : C[4];
          const x0 = gx + Math.cos(a0) * r0, y0 = gy + Math.sin(a0) * r0 * 0.56;
          const x1 = gx + Math.cos(a1) * r1, y1 = gy + Math.sin(a1) * r1 * 0.56;
          p.line(x0, y0, x1, y1, col, 0.72 + 0.28 * u);
          p.line(x0, y0 + 0.7, x1, y1 + 0.7, C[4], 0.55 + 0.35 * u);
        }
        const vTop = -70, vBot = vTop + 70 * ease.outCubic(clamp(u / 0.75));
        p.line(0, vTop, 0, vBot, u > 0.55 ? C[6] : C[5], 0.8 + 0.2 * u);
        p.line(-1, vTop, -1, vBot, C[4], 0.65);
        p.glowLine(0, vTop + 6, 0, vBot, 5, C[4], 0.28 + 0.18 * u);
        const pr = 64 * (1.20 - 0.82 * ease.inQuad(u));
        const pSteps = 52;
        for (let i = 0; i < pSteps; i++) {
          const a = (i / pSteps) * TAU;
          p.dot(Math.cos(a) * pr, Math.sin(a) * pr * 0.20, u > 0.55 ? C[6] : C[5], 0.65 + 0.35 * u);
          if (i % 2 === 0) p.dot(Math.cos(a) * pr * 0.68, Math.sin(a) * pr * 0.14, C[4], 0.55 + 0.35 * u);
        }
        if (u > 0.28) {
          const pk = (u - 0.28) / 0.72;
          sparkle(p, gx, gy, 22 * pk, C[6], C[5], 0.9 * pk);
          p.glow(gx, gy, 10, C[4], 0.38 * pk);
        }
      }

      // ---- 空気の夕暮れ灼け ----
      const w1 = crit
        ? (t >= T1 + 0.015 && t < T1 + 0.22 ? Math.sin(clamp((t - (T1 + 0.015)) / 0.205) * Math.PI) * 1.15 : 0)
        : (t >= 0.018 && t < 0.145 ? Math.sin(clamp((t - 0.018) / 0.127) * Math.PI) : 0);
      if (w1 > 0) duskAirWash(p, C, w1, crit);

      // ---- 会心専用: 地面の大灼けと 12 本の深い灼熱亀裂 ----
      if (cracks && t >= T1 + 0.020) {
        drawScorchedGround(p, cracks, t, T1 + 0.020, 0.39, C);
      }

      // ---- 地面の衝撃波のみ (空中輪なし・会心は大きく 2 本に整理) ----
      const floorRings = crit
        ? [
          [T1 + 0.022, 0.28, 152, 7.6],
          [T1 + 0.068, 0.26, 94, 4.6],
        ]
        : [
          [0.032, 0.23, 74, 4.4],
          [0.068, 0.23, 50, 3.0],
        ];
      for (const [t0, dur, rMax, th] of floorRings) {
        const rk = (t - t0) / dur;
        if (rk >= 0 && rk < 1) {
          const r = 6 + rMax * ease.outCubic(rk);
          band(p, 0, 0, r, 0.19, Math.max(1, th * (1 - rk)), rk < 0.65 ? 1 : (1 - rk) / 0.35, [C[2], C[3], C[4], C[6]], () => true, rk < 0.18);
        }
      }

      // 会心のみ: 着弾 1 コマ (f7) に地面を左右へ貫く灼熱の水平閃光
      if (crit && t >= T1 + 0.015 && t < T1 + 0.045) {
        p.line(-102, -1, 102, -1, C[6], 1);
        p.line(-94, 1, 94, 1, C[5], 0.95);
        p.line(-86, 2, 86, 2, C[4], 0.8);
        p.glowLine(-90, 0, 90, 0, 8, C[4], 0.48);
      }

      // ---- 縦の剛斧 ----
      glowSlash(p, E, t, o.flow, crit ? 0.78 : 0.6);
      glowSlash(p, A, t, o.flow, crit ? 1.50 : 1.12);

      // ---- 長く吹き荒れる血の筋 (通常は v3 と完全同一・会心は 24 本＋地面噴き上げ 10 本で圧倒的迫力) ----
      for (const w of wisps) {
        const u = w.age / w.life, df = clamp((u - 0.45) / 0.55);
        for (let i = 1; i < w.pts.length; i++) {
          const f = i / w.pts.length;
          if (df > 0 && rand(i * 13 + w.id * 7, 5, 29) < Math.pow(df, 0.85) / (0.25 + 0.75 * f)) continue;
          const col = crit
            ? (f > 0.62 && u < 0.62 ? C[6] : f > 0.36 ? C[5] : C[4])
            : (f > 0.78 && u < 0.55 ? C[6] : f > 0.45 ? C[4] : C[3]);
          if (df > 0.3) {
            p.dot(w.pts[i][0], w.pts[i][1], col, Math.max(0.32, (1 - df * 0.6) * f));
          } else {
            const a = (1 - u * 0.4) * (0.45 + 0.55 * f);
            p.line(w.pts[i - 1][0], w.pts[i - 1][1], w.pts[i][0], w.pts[i][1], col, a);
            if (f > (crit ? 0.42 : 0.65)) p.line(w.pts[i - 1][0], w.pts[i - 1][1] + 0.7, w.pts[i][0], w.pts[i][1] + 0.7, C[4], a * (crit ? 0.85 : 0.7));
          }
        }
        if (u < 0.42) p.glow(w.x, w.y, crit ? 5.2 : 4.2, C[4], (crit ? 0.34 : 0.28) * (1 - u / 0.42));
      }

      const ua = t - T1;
      const g = ua - A.sweep * 0.6;
      if (g > 0 && g < 0.085) sparkle(p, endA.x, endA.y, (crit ? 15 : 8) * ease.outBack(Math.min(1, g / 0.025)) * (1 - g / 0.085), C[6], C[4], crit ? 0.72 : 0.6);
      if (ua > 0 && ua < 0.075) {
        const [x, y] = onSwing(A, A.R - 4, A.span * ease.outExpo(clamp(ua / A.sweep)));
        const k = 1 - ua / 0.075;
        p.glow(x, y, crit ? 14 : 11, C[4], (crit ? 0.48 : 0.42) * k);
        p.glow(x, y, crit ? 6.5 : 6, C[6], (crit ? 0.40 : 0.36) * k);
      }
      ps.draw(p);
    },
  };
}
