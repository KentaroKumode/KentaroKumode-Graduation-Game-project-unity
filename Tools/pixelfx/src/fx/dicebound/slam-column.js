// DICE BOUND 用 (2026-11-14): 盾を上から叩きつける時の、 落ちて来る重い塊の軌跡 (太い光の柱)。 原点 = 着く所。 画面のドットで描く (_scaled.js では K = 1 で使う)。
// melee/thrust を下向きにした筋の束だけでは、 線が細くて軽い (作者: もっと鈍重に)。 その後ろに、 上が細く下が太い柱を 1 本、 ゆっくり落とす (melee/hammer-smash の「動きの楔」の考え方)。
import { fit, clamp, ease, noise2, rand, WHITE } from "../../core/index.js";
import { beam } from "./_kit.js";

export const meta = {
  name: "Slam column (DICE BOUND)",
  desc: "The heavy wedge of motion behind a shield slammed straight down: a column of light, narrow above and broad below, that drops onto the mark and thins away.",
  tags: ["dicebound", "player", "shield", "one-shot"],
  size: [160, 260],
  origin: [80, 250],
  duration: 0.7,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: ["#212f6b", "#2c58aa", "#3a8bd8", "#62c8f4", "#b8c9e8", "#e8f1ff", "#ffffff", "#ffffff"], height: 230, width: 46, drop: 0.14, smear: false, crit: false, shaded: false, seed: 1 },
};

// 剣や斧の太い面と同じ 3〜4 色の陰影＋縦の流れ筋＋外縁の白い刃と縁光で塗る太柱
function shadedPillar(p, C, y0, y1, w0, w1, t, k = 1, crit = false, edgeGlow = true) {
  const L = y1 - y0;
  if (L < 2 || k <= 0.02) return;
  const maxHw = Math.max(w0, w1) * 0.6 + 3;
  p.shade(-maxHw, y0, maxHw, y1, (x, y, out) => {
    const al = (y - y0) / L;
    if (al < 0 || al > 1) return;
    const hw = Math.max(0.6, (w0 + (w1 - w0) * al) / 2);
    const ad = Math.abs(x), d = ad / hw;
    if (d > 1.06) return;
    const rimPx = hw - ad;
    const strand = noise2(x * 0.30, y * 0.038 - t * 24, 31);
    let ci;
    if (rimPx < 2.2) ci = 6;
    else if (rimPx < 5.0) ci = 5;
    else {
      const bias = x / hw;
      const base = 3.2 + (1 - d) * 1.15 - bias * 0.55 + (strand > 0.38 ? -0.9 : strand < -0.42 ? 0.85 : 0);
      ci = clamp(Math.round(6 - base), 1, 5);
    }
    out.c = ci >= 6 ? WHITE : C[ci];
    out.a = k * (al < 0.08 ? al / 0.08 : 1);
    if (rimPx < 2.4) { out.l = WHITE; out.k = 1.05 * k; }
    else if (rimPx < 5.5) { out.l = C[5]; out.k = 0.42 * k; }
  });
  if (edgeGlow) {
    p.glowLine(-w0 * 0.5, y0, -w1 * 0.5, y1, crit ? 10 : 8, C[5], (crit ? 0.34 : 0.28) * k);
    p.glowLine(w0 * 0.5, y0, w1 * 0.5, y1, crit ? 10 : 8, C[5], (crit ? 0.34 : 0.28) * k);
    p.glowLine(-w0 * 0.45, y0, -w1 * 0.45, y1, 4, WHITE, 0.25 * k);
    p.glowLine(w0 * 0.45, y0, w1 * 0.45, y1, 4, WHITE, 0.25 * k);
  }
}

export function draw(p, t, o) {
  const C = fit(o.palette, 8);
  if (o.smear) {
    // 24fps 同期: f2 (0.042s) = 細い落下芯 / f3 (0.083s) = 太い柱ちょうど 1 コマ / f4 (0.125s〜) = 即 1/5 細い芯へ絞り点間引きで消す
    if (t <= 0.01 || t > 0.34) return;
    const crit = !!o.crit;
    if (t < 0.065) {
      // f2: 落下中の鋭い先行芯 (太い面にはしない)
      const u = clamp(t / 0.065);
      const y1 = -o.height * (1 - ease.inQuad(u)) - 22;
      beam(p, C, 0, y1 - o.height, 0, y1, 1.5, 4.5, 0.85);
      return;
    }
    if (t < 0.105) {
      // f3: 太い光の柱・ちょうど 1 コマ
      const w0 = o.width * 0.42, w1 = o.width * 1.08;
      if (o.shaded) {
        shadedPillar(p, C, -o.height, 0, w0, w1, t, 1, crit, true);
      } else {
        beam(p, C, 0, -o.height, 0, 0, w0, w1, 1);
        p.glowLine(0, -o.height, 0, 0, crit ? 18 : 13, C[5], crit ? 0.36 : 0.28);
      }
      return;
    }
    // f4 以降: 即座に 1/5 幅の細い芯へ絞り、終盤は点間引きで消す (純点線では発光させない)
    const f = clamp((t - 0.105) / 0.145);
    if (f < 1) {
      const thin = Math.pow(1 - f, 1.35);
      const w0 = Math.max(0.8, 2.2 * thin), w1 = Math.max(1.2, o.width * 0.19 * thin + 0.6);
      const yTop = -o.height * (1 - f * 0.35);
      if (o.shaded) shadedPillar(p, C, yTop, 0, w0, w1, t, 0.95 * (1 - f * 0.45), crit, false);
      else beam(p, C, 0, yTop, 0, 0, w0, w1, 0.95 * (1 - f * 0.45));
      p.glowLine(0, yTop, 0, 0, (crit ? 8 : 6) * (0.45 + 0.55 * thin), C[5], (crit ? 0.26 : 0.20) * (1 - f));
    }
    // 点間引きの縦残光 (f5〜f8)
    const df = clamp((t - 0.14) / 0.20);
    if (df > 0 && df < 1) {
      const yStart = -o.height * (1 - df * 0.72);
      for (let idx = 0; ; idx++) {
        const y = -idx * 3.5;
        if (y < yStart) break;
        const pos = 1 - Math.abs(y) / o.height; // 下側ほど最後まで残る
        const keep = 0.25 + 0.75 * pos;
        if (rand(idx * 13 + 5, 19, 31) < Math.pow(df, 0.85) / keep) continue;
        const col = df < 0.5 ? C[5] : C[4];
        p.dot((rand(idx, 3, 7) - 0.5) * 3, y, col, Math.max(0.28, (1 - df * 0.65) * (0.5 + 0.5 * pos)));
      }
    }
    return;
  }
  const reach = ease.inQuad(clamp(t / o.drop)), fade = 1 - clamp((t - o.drop) / 0.3);
  if (fade <= 0) return;
  // 落ちて来る間は、 柱の下の端が着く所へ近づく。 着いた後は、 細って消える
  const y1 = -o.height * (1 - reach);
  beam(p, C, 0, y1 - o.height, 0, y1, o.width * 0.35 * fade + 1, o.width * fade + 2, 1);
}
