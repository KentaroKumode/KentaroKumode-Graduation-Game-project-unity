// DICE BOUND 盾・会心リワーク案 (shield-crit-v2・holy 金):
//   通常とは別の技に見える 2 段構え (三日月は足さない):
//     段 1 (f2〜f5): 構えて光を溜める (impacts/shield-block 着想の菱形格子バリア環が収縮＋収束光線＋頂点フラッシュ)
//     段 2 (f6): 3〜4 色の陰影＋流れ筋＋縁の光で塗った極太の金の柱をちょうど 1 コマ叩きつける
//     反発 (f7〜f12): 柱が即 1/5 細芯へ落ち、元の impacts/ground-slam が炸裂すると同時に、跳ね上がる衝撃の輪と上昇光針が天へ返る
import { fit, clamp, ease, noise2, rand, sparkle, TAU, WHITE } from "../../core/index.js";

export const meta = {
  name: "Shield crit v2 (DICE BOUND)",
  desc: "Two-stage critical shield slam: a golden lattice barrier gathers light inward, drops a shaded 1-frame heavy pillar, then rebounds skyward with a rising shock ring and light needles.",
  tags: ["dicebound", "player", "shield", "crit", "one-shot"],
  size: [320, 260],
  origin: [160, 250],
  duration: 0.75,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#3b1d2a", "#7a3a2c", "#c4652e", "#f0a040", "#ffd25e", "#fff0a0", "#ffffff", "#ffffff"],
    height: 230,
    width: 68,
    seed: 37,
  },
};

// 3〜4 色の陰影＋流れの筋＋白熱の縁で塗る太柱 (べた塗りの白い台形にしない)
function shadedColumn(p, C, y0, y1, w0, w1, t, k = 1, edgeGlow = true) {
  const L = y1 - y0;
  if (L < 2 || k <= 0.02) return;
  const maxHw = Math.max(w0, w1) * 0.6 + 3;
  p.shade(-maxHw, y0, maxHw, y1, (x, y, out) => {
    const al = (y - y0) / L;
    if (al < 0 || al > 1) return;
    const hw = Math.max(0.6, (w0 + (w1 - w0) * al) / 2);
    const ad = Math.abs(x), d = ad / hw;
    if (d > 1.08) return;
    // 外縁の白い刃 (2〜2.5px)＋流れに沿った縦筋＋内側の深い陰影 (3〜4 段)
    const rimPx = hw - ad;
    const strand = noise2(x * 0.28, y * 0.035 - t * 24, 31);
    let ci;
    if (rimPx < 2.4) ci = 6;                          // 外縁: 白芯
    else if (rimPx < 5.5) ci = 5;                     // 縁内側: 明るい淡金
    else {
      // 中央の重厚な 3 段陰影 (左寄りハイライト・中央深み・縦の流れ筋)
      const bias = x / hw;
      const base = 3.2 + (1 - d) * 1.1 - bias * 0.55 + (strand > 0.38 ? -0.9 : strand < -0.42 ? 0.85 : 0);
      ci = clamp(Math.round(6 - base), 1, 5);
    }
    out.c = ci >= 6 ? WHITE : C[ci];
    out.a = k * (al < 0.08 ? al / 0.08 : 1);
    if (rimPx < 2.6) { out.l = WHITE; out.k = 1.05 * k; }
    else if (rimPx < 6) { out.l = C[5]; out.k = 0.42 * k; }
  });
  if (edgeGlow) {
    // 両側の縁に沿って光らせる (中央をべたに光らせない)
    p.glowLine(-w0 * 0.5, y0, -w1 * 0.5, y1, 9, C[5], 0.34 * k);
    p.glowLine(w0 * 0.5, y0, w1 * 0.5, y1, 9, C[5], 0.34 * k);
    p.glowLine(-w0 * 0.45, y0, -w1 * 0.45, y1, 4.5, WHITE, 0.28 * k);
    p.glowLine(w0 * 0.45, y0, w1 * 0.45, y1, 4.5, WHITE, 0.28 * k);
  }
}

export function draw(p, t, o) {
  const C = fit(o.palette, 8);
  if (t <= 0.01 || t > 0.62) return;

  // ---- 段 1 (f2〜f5: 0.02..0.188s): 盾を構えて光を溜める (菱形格子バリア環の収縮＋収束光線) ----
  if (t < 0.188) {
    const u = clamp((t - 0.02) / 0.165);
    const cy = -72;
    const R = 64 * (1 - 0.72 * ease.inQuad(u)) + 8;
    // 収縮する盾の六角/菱形バリア輪 (impacts/shield-block 着想)
    const sides = 6;
    const pts = Array.from({ length: sides }, (_, i) => {
      const a = -Math.PI / 2 + (i * TAU) / sides;
      return [Math.cos(a) * R, cy + Math.sin(a) * R * 1.08];
    });
    const colRim = u > 0.72 ? WHITE : C[5];
    for (let i = 0; i < sides; i++) {
      const [ax, ay] = pts[i], [bx, by] = pts[(i + 1) % sides];
      p.line(ax, ay, bx, by, colRim, 0.95);
      p.line(ax * 0.78, cy + (ay - cy) * 0.78, bx * 0.78, cy + (by - cy) * 0.78, C[4], 0.65);
    }
    // 菱形格子の十字線 (溜めが進むほど明るく締まる)
    p.line(-R * 0.85, cy, R * 0.85, cy, C[4], 0.55 * (1 - u * 0.4));
    p.line(0, cy - R * 0.95, 0, cy + R * 0.95, C[5], 0.7);
    // 外側から中心へ吸い込まれる鋭い収束直線 8 本
    for (let i = 0; i < 8; i++) {
      const a = (i * TAU) / 8 + 0.2;
      const d1 = R + 42 * (1 - u), d0 = d1 + 24 * (1 - u * 0.4);
      p.line(Math.cos(a) * d0, cy + Math.sin(a) * d0, Math.cos(a) * d1, cy + Math.sin(a) * d1, u > 0.6 ? WHITE : C[5], 0.85);
    }
    if (u > 0.62) {
      // f5: 圧縮完了の鋭い閃光＋下向き先行直線束
      const k = (u - 0.62) / 0.38;
      sparkle(p, 0, cy, 18 * k, WHITE, C[4], 0.75 * k);
      p.glow(0, cy, 14, C[4], 0.38 * k);
      for (const dx of [-24, -14, -6, 6, 14, 24]) {
        p.line(dx, -o.height, dx * 0.7, -30, Math.abs(dx) < 10 ? WHITE : C[5], 0.85 * k);
      }
    }
    return;
  }

  // ---- 段 2 (f6: 0.188..0.228s): 陰影と縁光のある極太の金の柱・ちょうど 1 コマ ----
  if (t < 0.228) {
    shadedColumn(p, C, -o.height, 0, o.width * 0.46, o.width * 1.12, t, 1, true);
    // 両脇の鋭い縦直線の束 (点々にしない)
    for (const dx of [-52, -42, -34, 34, 42, 52]) {
      const len = 130 + (Math.abs(dx) % 3) * 35;
      p.line(dx, -o.height + 10, dx, -o.height + 10 + len, Math.abs(dx) < 40 ? WHITE : C[5], 0.9);
    }
    return;
  }

  // ---- f7 (0.250s) 以降: 柱を即 1/5 幅の細い芯へ絞り、地面から衝撃が返って天へ跳ね上がる ----
  const f = clamp((t - 0.228) / 0.15);
  if (f < 1) {
    const thin = Math.pow(1 - f, 1.35);
    const w0 = Math.max(1.2, 3.0 * thin), w1 = Math.max(1.6, o.width * 0.19 * thin + 0.8);
    const yTop = -o.height * (1 - f * 0.32);
    shadedColumn(p, C, yTop, 0, w0, w1, t, 0.95 * (1 - f * 0.4), false);
    p.glowLine(0, yTop, 0, 0, 7 * (0.45 + 0.55 * thin), C[4], 0.28 * (1 - f));
  }

  // 反発 (f8〜f13: 0.265..0.52s): 地面の衝撃が上へ跳ね上がる輪＋上昇する鋭い光針
  const rb = clamp((t - 0.265) / 0.255);
  if (t >= 0.265 && rb < 1) {
    const ry = -16 - 145 * ease.outCubic(rb);
    const rx = 28 + 64 * ease.outCubic(rb);
    const sy = 0.24;
    const thickRing = rb < 0.18; // f8 のみ太い反発リング 1 コマ → f9 で即細輪＋点間引き
    const df = clamp((rb - 0.35) / 0.65);
    const steps = Math.ceil(rx * TAU * 1.1);
    const bandTh = thickRing ? 6 : 1;
    for (let i = 0; i < steps; i++) {
      const g = (i / steps) * TAU;
      if (df > 0 && rand(i * 19 + 5, 13, 31) < Math.pow(df, 0.85)) continue;
      const x = Math.cos(g) * rx, y = ry + Math.sin(g) * rx * sy;
      if (thickRing) {
        for (let dy = 0; dy < bandTh; dy++) {
          const c = dy < 2 ? WHITE : dy < 4 ? C[5] : C[3];
          p.dot(x, y + dy - 2, c, 1);
        }
      } else {
        p.dot(x, y, rb < 0.45 ? C[6] : C[4], Math.max(0.3, 1 - df * 0.65));
      }
    }
    if (rb < 0.45) {
      for (let i = 0; i < 8; i++) {
        const g = (i / 8) * TAU;
        p.glow(Math.cos(g) * rx, ry + Math.sin(g) * rx * sy, thickRing ? 10 : 5, C[4], (thickRing ? 0.30 : 0.18) * (1 - rb / 0.45));
      }
    }
    // 天へ跳ね上がる鋭い光針 8 本 (終盤は点間引き)
    const needles = [-54, -36, -20, -8, 8, 20, 36, 54];
    for (let idx = 0; idx < needles.length; idx++) {
      const nx = needles[idx] * (0.7 + 0.5 * rb);
      const headY = -20 - (125 + (idx % 3) * 32) * ease.outCubic(rb);
      const len = (52 - (idx % 2) * 14) * (1 - rb * 0.55);
      const tailY = headY + len;
      if (df > 0.3) {
        const nf = clamp((df - 0.3) / 0.7);
        for (let s = 0; s <= 10; s++) {
          if (rand(s * 17 + idx * 11, 7, 23) < nf) continue;
          p.dot(nx, tailY - (len * s) / 10, C[4], Math.max(0.3, 1 - nf * 0.65));
        }
      } else {
        p.line(nx, tailY, nx, headY + len * 0.35, C[4], 0.8 * (1 - rb));
        p.line(nx, headY + len * 0.35, nx, headY, rb < 0.3 ? WHITE : C[5], 1 - rb * 0.6);
      }
    }
  }

  // 縦芯の点間引き残光 (f8〜f11・純点線で無発光)
  const df = clamp((t - 0.27) / 0.22);
  if (df > 0 && df < 1) {
    const yStart = -o.height * (1 - df * 0.72);
    for (let idx = 0; ; idx++) {
      const y = -idx * 3.5;
      if (y < yStart) break;
      const pos = 1 - Math.abs(y) / o.height;
      if (rand(idx * 13 + 5, 19, 31) < Math.pow(df, 0.85) / (0.25 + 0.75 * pos)) continue;
      p.dot((rand(idx, 3, 7) - 0.5) * 3, y, df < 0.5 ? C[5] : C[4], Math.max(0.28, (1 - df * 0.65) * (0.5 + 0.5 * pos)));
    }
  }
}
