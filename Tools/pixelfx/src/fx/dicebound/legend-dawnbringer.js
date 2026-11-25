// DICE BOUND 段 4 LEGENDARY 盾「ドーンブリンガー」v3:
//   通常版 (crit=false): v2 から 1 バイトも変更なし (作者評価「完璧」)。
//   会心版 (crit=true): 溜めを 4 コマ → 7 コマ (f2〜f8) へ延長し、序盤(外環の収束＋第1波紋) → 中盤(8 本の光筋が地平線核へ流れ込み盾が暁金へ燃え上がる＋第2波紋) → 臨界(白熱の二重十字紋章＋全幅白熱地平線) の 3 段階変化を追加。日の出の二重 (f9 第一解放・f11 第二水平コロナ) はそのまま維持しつつ、溜めの見返りとして芯の白さを強化。
import { Particles, P, Rng, fit, sparkle, band, clamp, ease, noise2, rand, TAU, WHITE } from "../../core/index.js";
import { makeCracks, SOOT } from "../impacts/_lib.js";

export const meta = {
  name: "Dawnbringer v3 (DICE BOUND Legendary Shield)",
  desc: "Completely original shield motion: a heraldic twilight barrier absorbs and gathers dawn light along the horizon, then erupts upward in a 1-frame shaded sunrise dome-wall and radiant fan of dawn rays. Critical extends the crest windup with 3 escalating stages.",
  tags: ["dicebound", "player", "legend", "shield", "dawn", "one-shot"],
  size: [420, 260],
  origin: [210, 230],
  duration: 1.15,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#060e3f", "#111d6b", "#486db5", "#e09836", "#ffd072", "#fff2bc", "#ffffff", "#ffffff"],
    sparks: ["#ffffff", "#fff2bc", "#ffd072", "#e09836"],
    crit: false,
    seed: 31,
  },
};

// 正面の盾紋章 (通常版は v2 完全一致・会心版は 3 段階の溜め変化を追加)
function drawShieldBastion(p, C, cy, S, u, crit) {
  // ヒーターシールド型の 6 頂点輪郭 (左右対称・正面構図)
  const pts = [
    [0, -1.05], [0.82, -0.78], [0.76, 0.12], [0, 1.05], [-0.76, 0.12], [-0.82, -0.78],
  ].map(([x, y]) => [x * S, cy + y * S]);
  const hot = u > 0.68;
  const rimCol = hot ? WHITE : u > 0.35 ? C[4] : C[2];
  // 会心のみ: 序盤〜中盤、外側の大きな収束円環が盾へ吸い込まれる
  if (crit && u < 0.85) {
    const cu = clamp(u / 0.85);
    const outerR = S * (1.85 - 0.85 * ease.inQuad(cu));
    const steps = 48;
    for (let i = 0; i < steps; i++) {
      const a = (i / steps) * TAU + cu * 0.4;
      const col = cu > 0.55 ? C[5] : cu > 0.25 ? C[4] : C[2];
      p.dot(Math.cos(a) * outerR, cy + Math.sin(a) * outerR, col, 0.55 + 0.45 * cu);
    }
  }
  // 会心のみ: 中盤〜臨界、盾の周囲 8 方向から地平線の核へ光筋が流れ込む
  if (crit && u > 0.28) {
    const fu = clamp((u - 0.28) / 0.72);
    for (let i = 0; i < 8; i++) {
      const a = (i / 8) * TAU + Math.PI / 8;
      const d1 = S * (1.65 - 1.15 * ease.inQuad(fu));
      const d0 = d1 + 14 + 10 * fu;
      p.line(
        Math.cos(a) * d0, cy + Math.sin(a) * d0,
        Math.cos(a) * d1, cy + Math.sin(a) * d1,
        fu > 0.65 ? WHITE : fu > 0.35 ? C[5] : C[4],
        0.5 + 0.5 * fu,
      );
    }
  }
  // 外枠と内枠
  for (let i = 0; i < pts.length; i++) {
    const [ax, ay] = pts[i], [bx, by] = pts[(i + 1) % pts.length];
    p.line(ax, ay, bx, by, rimCol, 0.95);
    p.line(ax * 0.78, cy + (ay - cy) * 0.78, bx * 0.78, cy + (by - cy) * 0.78, hot ? C[5] : C[2], 0.75);
    // 会心のみ: 臨界で盾の縁に強い暁グローを乗せる
    if (crit && u > 0.55) {
      const hk = (u - 0.55) / 0.45;
      p.glowLine(ax, ay, bx, by, 5.5, u > 0.8 ? WHITE : C[4], 0.28 * hk);
    }
  }
  // 中央の十字紋章と菱形格子 (受けて溜める)
  p.line(0, cy - S * 0.92, 0, cy + S * 0.92, hot ? WHITE : C[4], 0.85);
  p.line(-S * 0.68, cy - S * 0.15, S * 0.68, cy - S * 0.15, hot ? C[5] : C[3], 0.8);
  p.line(-S * 0.45, cy - S * 0.6, S * 0.45, cy + S * 0.35, C[2], 0.6);
  p.line(S * 0.45, cy - S * 0.6, -S * 0.45, cy + S * 0.35, C[2], 0.6);
  // 会心のみ: 臨界 (f7〜f8) で盾の中央十字が二重に白熱し、下端から地平線へ光の柱糸が繋がる
  if (crit && u > 0.68) {
    const lk = (u - 0.68) / 0.32;
    p.line(-1, cy - S * 0.95, -1, cy + S * 0.95, C[5], 0.85 * lk);
    p.line(1, cy - S * 0.95, 1, cy + S * 0.95, C[5], 0.85 * lk);
    p.line(-S * 0.72, cy - S * 0.15 - 1, S * 0.72, cy - S * 0.15 - 1, WHITE, 0.9 * lk);
    // 盾底から地平線核へ注ぎ込む光の糸
    p.line(0, cy + S * 1.05, 0, -2, WHITE, 0.95 * lk);
    p.glowLine(0, cy + S * 0.6, 0, -2, 6, C[4], 0.32 * lk);
  }
  // 受けて溜める波紋 (通常は v2 と完全同一 1 波・会心は 3 波へ増強)
  const waves = crit ? [u * 2.5, u * 2.5 - 0.65, u * 2.5 - 1.3] : [u * 1.4];
  for (const w of waves) {
    if (w <= 0 || w >= 1) continue;
    const r = S * 0.9 * ease.outCubic(w);
    const steps = 36;
    for (let i = 0; i < steps; i++) {
      const a = (i / steps) * TAU;
      p.dot(Math.cos(a) * r, cy + Math.sin(a) * r, w < 0.4 ? WHITE : C[4], 1 - w);
    }
  }
}

// 昇る朝日のドーム壁 (v2 完全一致)
function drawSunriseDomeWall(p, C, R, th, sy, t, thick, k = 1, df = 0) {
  if (R < 4 || th < 0.5 || k <= 0.02) return;
  if (df > 0.35) {
    const f = clamp((df - 0.35) / 0.65);
    const steps = Math.ceil(R * Math.PI * 1.2);
    for (let i = 0; i <= steps; i++) {
      const g = Math.PI + (i / steps) * Math.PI;
      const edge = Math.sin(g - Math.PI);
      if (rand(i * 19 + 7, 13, 31) < Math.pow(f, 0.85) / (0.25 + 0.75 * edge)) continue;
      p.dot(Math.cos(g) * R, Math.sin(g) * R * sy, f < 0.5 ? C[4] : C[2], Math.max(0.3, (1 - f * 0.65) * k));
    }
    return;
  }
  const rx = R + 2, ry = R * sy + 2;
  p.shade(-rx, -ry, rx, 2, (x, y, out) => {
    if (y > 0) return;
    const d = Math.hypot(x, y / sy);
    if (d > R + 0.5 || d < R - th - 1) return;
    const ang = Math.atan2(y / sy, x);
    const arch = Math.sin(-ang);
    const wNow = th * (0.35 + 0.65 * Math.pow(arch, 0.55));
    const q = (R - d) / Math.max(0.6, wNow);
    if (q < 0 || q > 1) return;
    const strand = noise2(ang * 14, d * 0.12 - t * 18, 31);
    let ci;
    if (q < 0.16) ci = 6;
    else if (thick) {
      const base = q * 4.2 + (strand > 0.4 ? -0.75 : strand < -0.45 ? 0.75 : 0);
      ci = clamp(Math.round(6 - base), 1, 6);
    } else {
      ci = q < 0.45 ? 6 : 4;
    }
    out.c = ci >= 6 ? WHITE : C[ci];
    out.a = k * (0.4 + 0.6 * Math.min(1, arch * 3)) * (1 - df * 0.5);
    if (q < 0.22) { out.l = WHITE; out.k = (thick ? 1.05 : 0.65) * k; }
    else if (thick && q < 0.45) { out.l = C[4]; out.k = 0.4 * k; }
  });
  const gCount = thick ? 11 : 7;
  for (let i = 1; i < gCount; i++) {
    const g = Math.PI + (i / gCount) * Math.PI;
    p.glow(Math.cos(g) * R, Math.sin(g) * R * sy, thick ? 11 : 5.5, C[4], (thick ? 0.34 : 0.20) * k * (1 - df));
  }
}

// holy/sunburst 着想: 地平線から扇形に広がる暁の光条 (v2 完全一致)
function dawnRay(p, C, a, L, w0, k, df, id) {
  if (L < 4 || k <= 0.02) return;
  const ca = Math.cos(a), sa = Math.sin(a), r0 = 12;
  const x0 = ca * r0, y0 = sa * r0, x1 = ca * (r0 + L), y1 = sa * (r0 + L);
  if (df > 0.38) {
    const f = clamp((df - 0.38) / 0.62);
    const n = Math.max(3, Math.ceil(L * 0.42));
    for (let i = 0; i <= n; i++) {
      const u = i / n;
      if (u < f * 0.62) continue;
      if (rand(i * 17 + id * 13, id * 5 + 3, 29) < Math.pow(f, 0.85) / (0.25 + 0.75 * (1 - u))) continue;
      const r = r0 + L * u;
      p.dot(ca * r, sa * r, u < 0.45 ? C[4] : C[2], Math.max(0.3, (1 - f * 0.65) * k));
    }
    return;
  }
  const pad = w0 + 2;
  p.shade(Math.min(x0, x1) - pad, Math.min(y0, y1) - pad, Math.max(x0, x1) + pad, Math.max(y0, y1) + pad, (x, y, out) => {
    const along = x * ca + y * sa - r0, u = along / L;
    if (u < 0 || u > 1) return;
    const across = Math.abs(-x * sa + y * ca), w = (w0 / 2) * (1 - u * 0.86) + 0.3;
    if (across > w) return;
    const spine = across < 0.8;
    out.c = spine ? (u < 0.35 ? WHITE : u < 0.7 ? C[5] : C[4]) : u < 0.32 ? C[5] : u < 0.68 ? C[4] : C[3];
    out.a = (u < 0.72 ? k : k * (1 - (u - 0.72) / 0.28)) * (1 - df * 0.45);
  });
  if (df < 0.3) p.glowLine(x0, y0, ca * (r0 + L * 0.7), sa * (r0 + L * 0.7), 5, C[4], 0.16 * k * (1 - df / 0.3));
}

// 地割れ (v2 完全一致)
function drawFloorCracks(p, cracks, t, t0, dur, C) {
  const u = (t - t0) / dur;
  if (u < 0 || u >= 1) return;
  const grow = ease.outCubic(clamp(u / 0.15));
  const heat = clamp(1 - u / 0.45);
  const df = clamp((u - 0.42) / 0.58);
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
            p.dot(x, y, df < 0.45 ? C[3] : C[1], Math.max(0.3, 1 - df * 0.65));
          }
        } else {
          const col = heat > 0.65 ? WHITE : heat > 0.35 ? C[4] : heat > 0.12 ? C[3] : SOOT;
          p.line(x0, y0, ex, ey, col, 1);
          p.line(x0, y0 + 1, ex, ey + 1, C[1], 0.6);
        }
      }
    }
  }
}

export function create(o) {
  const C = fit(o.palette, 8), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), crit = !!o.crit;
  const cracks = makeCracks(Rg, crit ? 9 : 6, { len: [32, crit ? 105 : 74], dive: 0.45, maxY: 26, forks: 0.55, x0: [4, 14] });
  // speed=1 → 1 コマ = 0.04167s
  // 通常 (v2 と完全同一): 溜め f2〜f3 (0.02..0.105s) → 解放 1 コマ太面 f4 (0.105..0.145s) → 細縁＋暁光条 f5〜f16
  // 会心 (v3 溜め延長): 溜め f2〜f8 の 7 コマ (0.02..0.312s) → 第一解放 1 コマ太面 f9 (0.312..0.352s) → 第二暁コロナ水平波 1 コマ f11 (0.400..0.438s) → f12〜f23
  const REL1 = crit ? 0.312 : 0.105;
  const REL1_END = REL1 + 0.040;
  const REL2 = crit ? 0.400 : -1;
  let t = 0, burst1 = false, burst2 = false;

  return {
    update(dt) {
      t += dt;
      if (!burst1 && t >= REL1) {
        burst1 = true;
        for (let i = 0; i < (crit ? 14 : 9); i++) {
          const a = -Math.PI + 0.12 + Rg.range(0, Math.PI - 0.24), sp = Rg.range(180, crit ? 420 : 320);
          ps.spawn(P.SPARK, Rg.range(-12, 12), -4, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.08, 0.18));
        }
        const lives = [0.16, 0.21, 0.26, 0.31, 0.36];
        for (let i = 0; i < lives.length; i++) {
          const a = -Math.PI + 0.25 + (i / (lives.length - 1)) * (Math.PI - 0.5), sp = Rg.range(90, 180);
          ps.spawn(P.MOTE, Math.cos(a) * 24, Math.sin(a) * 24, Math.cos(a) * sp, Math.sin(a) * sp, lives[i], i & 1 ? C[5] : C[4], 0.7);
        }
      }
      if (crit && !burst2 && t >= REL2) {
        burst2 = true;
        for (let i = 0; i < 12; i++) {
          const s = i % 2 ? 1 : -1;
          const a = (s > 0 ? 0 : Math.PI) + Rg.range(-0.4, 0.05), sp = Rg.range(220, 440);
          ps.spawn(P.SPARK, s * 20, -3, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.08, 0.18));
        }
      }
      ps.update(dt);
      for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 5.5 * dt; m.vy -= m.vy * 5.5 * dt; }
    },
    draw(p) {
      if (t <= 0.01) return;

      // ---- 段 1: 西門の盾紋章が受けて溜め、地平線に暁が灯る ----
      if (t < REL1) {
        const u = clamp((t - 0.015) / (REL1 - 0.015));
        // 会心のみ: 溜めが進むにつれて背後の夜藍が一瞬だけ深く沈み、盾と暁の白光が映える
        if (crit && u > 0.12) {
          const dk = Math.sin(clamp((u - 0.12) / 0.88) * Math.PI * 0.85) * 0.36;
          for (let y = -165; y <= 14; y += 3) {
            const vy = 1 - Math.abs((y + 72) / 96);
            if (vy <= 0) continue;
            for (let x = -180 + (y & 2 ? 1 : 0); x <= 180; x += 3) {
              const vx = 1 - Math.abs(x / 188);
              if (vx <= 0) continue;
              p.dot(x, y, C[0], dk * vx * vy);
            }
          }
        }
        const S = (crit ? 56 : 42) * (crit ? (1.10 - 0.32 * ease.inQuad(u)) : (1.06 - 0.24 * ease.inQuad(u)));
        drawShieldBastion(p, C, -58, S, u, crit);
        // 地平線の暁ラインが左右から伸び、会心の臨界では全幅が白熱する
        const hw = (crit ? 185 : 110) * (0.35 + 0.65 * ease.outCubic(u));
        p.line(-hw, 0, hw, 0, u > 0.65 ? WHITE : C[4], 0.9);
        p.line(-hw * 0.7, -1, hw * 0.7, -1, crit && u > 0.72 ? C[5] : C[2], 0.65);
        if (crit && u > 0.72) {
          const hk = (u - 0.72) / 0.28;
          p.line(-hw * 0.9, 1, hw * 0.9, 1, C[4], 0.8 * hk);
          p.glowLine(-hw * 0.8, 0, hw * 0.8, 0, 7, C[5], 0.34 * hk);
        } else {
          p.glowLine(-hw * 0.6, 0, hw * 0.6, 0, 6, C[4], 0.25 * u);
        }
        // 溜めの後半: 地平線中心に朝日の核が光る (丸光は小さく)
        if (u > (crit ? 0.38 : 0.45)) {
          const k = (u - (crit ? 0.38 : 0.45)) / (crit ? 0.62 : 0.55);
          sparkle(p, 0, -4, (crit ? 24 : 13) * k, WHITE, C[4], (crit ? 0.85 : 0.7) * k);
          p.glow(0, -4, crit ? 15 : 11, C[4], (crit ? 0.42 : 0.35) * k);
        }
        return;
      }

      // ---- 地割れ＆地平線衝撃帯 ----
      drawFloorCracks(p, cracks, t, REL1, crit ? 0.52 : 0.42, C);
      const rk = (t - REL1) / (crit ? 0.30 : 0.25);
      if (rk < 1) {
        const r = 14 + (crit ? 165 : 125) * ease.outCubic(rk);
        band(p, 0, 0, r, 0.18, Math.max(1, (crit ? 6 : 4.5) * (1 - rk)), rk < 0.62 ? 1 : (1 - rk) / 0.38, [C[2], C[3], C[4], WHITE], () => true, rk < 0.16);
      }

      // ---- 解放 1: 昇る朝日のドーム壁 (1 コマ太面 → 即 1/5 極細ドーム縁 → 点間引き) ----
      const u1 = clamp((t - REL1) / (crit ? 0.36 : 0.30));
      if (u1 < 1) {
        const thick = t < REL1_END;
        const R = (crit ? 52 : 42) + (crit ? 105 : 82) * ease.outCubic(u1);
        const th = thick ? (crit ? 36 : 26) : Math.max(1.2, (crit ? 6.5 : 5.0) * Math.pow(1 - u1, 1.35) + 0.6);
        drawSunriseDomeWall(p, C, R, th, 0.84, t, thick, 1 - u1 * 0.35, u1);
      }

      // ---- 暁の放射光条 (扇形に空へ広がるサンバースト: 通常 9 本 / 会心 15 本) ----
      const rf = clamp((t - REL1) / (crit ? 0.36 : 0.29));
      if (rf < 1) {
        const N = crit ? 15 : 9;
        const grow = ease.outCubic(clamp(rf / 0.16));
        for (let i = 0; i < N; i++) {
          const a = -Math.PI + 0.12 + (i / (N - 1)) * (Math.PI - 0.24);
          const long = i % 2 === 0;
          const baseL = (crit ? (long ? 176 : 108) : (long ? 122 : 76)) * grow;
          const w0 = (crit ? (long ? 7.0 : 4.0) : (long ? 5.2 : 3.0)) * Math.pow(1 - rf, 0.85);
          dawnRay(p, C, a, baseL, w0, 1 - rf * 0.4, rf, i + 1);
        }
      }

      // ---- 会心のみ・解放 2 (f11 に 1 コマ太面): 水平に広がる暁コロナの第二波 ----
      if (crit && t >= REL2) {
        const u2 = clamp((t - REL2) / 0.28);
        if (u2 < 1) {
          const thick2 = t < REL2 + 0.038;
          const R2 = 38 + 142 * ease.outCubic(u2);
          const th2 = thick2 ? 24 : Math.max(1.0, 4.4 * Math.pow(1 - u2, 1.35) + 0.5);
          drawSunriseDomeWall(p, C, R2, th2, 0.52, t, thick2, 1 - u2 * 0.35, u2);
          if (thick2) {
            p.line(-205, -2, 205, -2, WHITE, 1);
            p.line(-195, -4, 195, -4, WHITE, 0.95);
            p.line(-195, 0, 195, 0, C[5], 0.95);
            p.glowLine(-175, -2, 175, -2, 9, C[4], 0.42);
          }
        }
      }

      // 解放直後の締まった地平線フラッシュ (小粒)
      const q = t - REL1;
      if (q < 0.08) {
        const fk = 1 - q / 0.08;
        sparkle(p, 0, -6, (crit ? 24 : 16) * fk, WHITE, C[4], 0.75 * fk);
        p.glow(0, -6, crit ? 15 : 12, C[4], 0.38 * fk);
      }

      ps.draw(p);
    },
  };
}
