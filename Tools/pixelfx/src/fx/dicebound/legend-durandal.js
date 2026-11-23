// DICE BOUND 段 4 LEGENDARY 剣「デュランダル」v5:
//   通常版 (crit=false): v4 から 1 バイトも変更なし (作者合格済み)。
//   会心版 (crit=true):
//     - 円形の衝撃波 (輪・楕円・ドーム) を完全に撤去 (溜め・締めの両方から円形リングを全廃)。
//     - 溜めを 6 コマ → 7 コマ (f2〜f8) へ延長し、空気が暗藍に沈む中、光の点と鋭い筋が左上起点と敵中心の 2 点の芯へ凝縮 (剣シルエットなし)。
//     - 1 太刀目 (f9・1 コマ太面: R=136, W=76) → 切り返し間 (f10〜f11) → 2 太刀目 (f12・1 コマ太面: 1 太刀目より大きく厚い R=152, W=92)。
//     - 十字が交わった瞬間 (f12 の 1 コマ): 1 太刀目の弧も白く再フラッシュして重なりが白く飛び、交点から 2 本の斬撃の向きに沿って画面端まで突き抜ける「万軍を断つ光」の細い光筋クロス＋斬撃方向の鋭い飛散片が走る。
//     - 尻尾: 2 本の巨大な弧と光の筋が通常より長い余韻で点を間引きながら静かに消える (全体 ~24 コマ)。
import { Particles, P, Rng, fit, hex, sparkle, flare, clamp, smooth, ease, noise2, rand, TAU, WHITE } from "../../core/index.js";

export const meta = {
  name: "Durandal v5 (DICE BOUND Legendary Greatsword)",
  desc: "Heavy frontal greatsword smear crescent: Normal unchanged from v4; Critical removes all circular shockwaves and amplifies pure slash spectacle with a 7-frame dual-core windup, an even larger/thicker second crescent forming a blazing white-hot X cross, and long slashing light beams along both cut directions.",
  tags: ["dicebound", "player", "legend", "sword", "greatsword", "smear", "one-shot"],
  size: [460, 260],
  origin: [230, 130],
  duration: 1.35,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1a1d32", "#515689", "#a91816", "#d93343", "#eb5f76", "#ffb9ab", "#ffffff"],
    sparks: ["#ffffff", "#ffb9ab", "#eb5f76", "#d93343"],
    crit: false,
    seed: 11,
  },
};

// lcd_frames.mjs の 6 段 durandal インクへ正確に各段を当てるための輝度調整済み色 (hex() で不透明 0xFF を付与)
// 0:暗藍(#1a1d32) / 1:鋼藍(#515689・鋼の暗い面!) / 2:深紅(#d93343) / 3:桃(#eb5f76) / 4:薄桜(#ffb9ab) / 5:白芯(#ffffff)
const INK_DARK  = hex("#101018"); // lum ~0.07 -> step 0 (#1a1d32 暗藍)
const INK_STEEL = hex("#363b48"); // lum ~0.23 -> step 1 (#515689 鋼の暗い面!)
const INK_CRIM  = hex("#904440"); // lum ~0.37 -> step 2 (#d93343 深紅)
const INK_ROSE  = hex("#eb5f76"); // lum ~0.58 -> step 3 (#eb5f76 桃)
const INK_PEACH = hex("#ffb9ab"); // lum ~0.78 -> step 4 (#ffb9ab 薄桜)

function onArc(s, r, a) {
  const lx = Math.cos(a) * r, ly = Math.sin(a) * r * s.sy;
  const c = Math.cos(s.rot), sn = Math.sin(s.rot);
  return [s.cx + lx * c - ly * sn, s.cy + lx * sn + ly * c];
}

function tipOf(s) {
  const [x, y] = onArc(s, s.R - s.W * 0.22, s.a1);
  const [x2, y2] = onArc(s, s.R - s.W * 0.22, s.a1 + Math.sign(s.a1 - s.a0) * 0.05);
  const l = Math.hypot(x2 - x, y2 - y) || 1;
  return { x, y, dx: (x2 - x) / l, dy: (y2 - y) / l };
}

// 鋼の暗い面 ＋ 光の明るい芯 のくっきり 2 層塗りによる巨大な大剣三日月スミア (通常版は v4 と完全同一)
function drawGreatswordCrescent(p, s, t, C, crit, whiteFlash = false) {
  const u = t - s.t0;
  if (u < 0) return;
  const span = s.a1 - s.a0;
  const life = s.sweep + s.hold + s.trail;
  const head = ease.outExpo(clamp(u / s.sweep));
  const tail = smooth(s.sweep + s.hold, life, u);
  const age = clamp(u / life);
  const thick = u < s.sweep + s.hold;
  const snapF = clamp((u - s.sweep - s.hold) / s.snapDur, 0, 1);
  const endTaper = 1 - smooth(0.45, 1, tail) * 0.78;
  const wNow = thick ? s.W : s.W * (1 - snapF * s.snapCut) * endTaper;

  const ta = s.a0 + span * tail;
  const L = span * (head - tail);
  const dir = Math.sign(L), absL = Math.abs(L);

  if (tail < 1 && absL > 0.02 && wNow >= 0.35) {
    const c = Math.cos(s.rot), sn = Math.sin(s.rot);
    const B = s.R + 4;
    p.shade(s.cx - B, s.cy - B, s.cx + B, s.cy + B, (x, y, out) => {
      const dx = x - s.cx, dy = y - s.cy;
      const lx = dx * c + dy * sn, ly = (-dx * sn + dy * c) / s.sy;
      const r = Math.hypot(lx, ly);
      if (r > s.R + 0.5 || r < s.R - wNow - 1.5) return;
      let uu = (dir * (Math.atan2(ly, lx) - ta)) % TAU;
      if (uu < 0) uu += TAU;
      if (uu > absL) return;
      const v = uu / absL;
      const w = wNow * Math.pow(Math.sin(Math.PI * Math.pow(v, 1.38)), 0.52);
      if (w < 0.35) return;
      const q = (s.R - r) / w;
      if (q < 0 || q > 1) return;

      const strand = noise2(uu * s.R * 0.032 - age * 12, r * 0.14, s.noiseSeed ?? 31);
      if (thick) {
        // 会心 2 太刀目の白飛び (whiteFlash): 十字が交わった瞬間の 1 コマだけ光の芯を一段広く白く飛ばす
        const coreCut = whiteFlash ? 0.34 : (crit ? 0.22 : 0.18);
        const lightCut = whiteFlash ? 0.50 : (crit ? 0.38 : 0.32);
        const crimCut = lightCut + 0.14;
        if (q < coreCut) {
          out.c = WHITE;
          out.l = WHITE;
          out.k = whiteFlash ? 1.45 : (crit ? 1.25 : 1.05);
        } else if (q < lightCut) {
          out.c = strand < -0.55 ? INK_ROSE : INK_PEACH;
          out.l = C[5];
          out.k = whiteFlash ? 0.95 : (crit ? 0.72 : 0.52);
        } else if (q < crimCut) {
          out.c = strand > 0.48 ? INK_ROSE : INK_CRIM;
        } else {
          const sq = (q - crimCut) / (1 - crimCut);
          if (sq > 0.86) out.c = INK_DARK;
          else if (strand > 0.56 && sq < 0.45) out.c = INK_CRIM;
          else out.c = INK_STEEL;
        }
        out.a = Math.min(1, v / 0.16);
      } else {
        if (q < 0.45) {
          out.c = age < 0.42 ? WHITE : INK_PEACH;
          if (age < 0.55) { out.l = C[5]; out.k = 0.60 * (1 - age * 0.7); }
        } else {
          out.c = age < 0.50 ? INK_ROSE : INK_CRIM;
        }
        out.a = Math.min(1, v / 0.22) * (1 - smooth(0.75, 1, age) * 0.55);
      }
    });

    const wr = clamp(wNow / s.W, 0.16, 1);
    const gs = s.glowScale ?? 1;
    const wg = (0.45 + 0.55 * wr) * gs, wk = (0.42 + 0.58 * wr) * gs;
    for (let uu = 0; uu <= absL; uu += 0.22) {
      const fU = uu / absL;
      const [rx, ry] = onArc(s, s.R - 1, ta + dir * uu);
      p.glow(rx, ry, (thick ? 11 : 6) * wg, C[4], (crit ? 0.34 : 0.25) * wk * (1 - age * 0.82) * Math.min(1, fU * 1.6));
      p.glow(rx, ry, (thick ? 6 : 4) * wg, WHITE, (crit ? 0.42 : 0.32) * wk * (1 - age * 0.82) * Math.min(1, fU * 1.8));
    }
  }

  // 尻尾の点間引き (明るさより先に点の間隔を広げ、端から欠く・純点線は無発光)
  const f = clamp((u - s.sweep - s.hold) / (s.trail + s.traceFade));
  const th = 0.8 * (1 - f);
  if (tail > 0.02 && th > 0) {
    const baseStep = 1.6 / (Math.abs(span) * s.R);
    const kStart = tail * smooth(0.2, 0.95, f) * 0.72;
    for (let idx = 0; ; idx++) {
      const k = idx * baseStep;
      if (k > tail) break;
      if (k < kStart) continue;
      const h = (((idx * 2654435761) >>> 0) % 1000) / 1000;
      const keep = 0.25 + 0.75 * k;
      if (h < Math.pow(f, 0.85) / keep) continue;
      const [x, y] = onArc(s, s.R - 1, s.a0 + span * k);
      const col = f < 0.45 ? INK_PEACH : f < 0.76 ? INK_ROSE : INK_STEEL;
      p.dot(x, y, col, Math.max(0.28, (1 - f * 0.65) * (0.45 + 0.55 * k)));
    }
    if (tail < 1) {
      const gs = s.glowScale ?? 1, tb = (1 - tail) * gs;
      for (let k = kStart; k <= tail; k += 0.24 / Math.abs(span)) {
        const [x, y] = onArc(s, s.R - 1, s.a0 + span * k);
        p.glow(x, y, 5.5 * gs, C[4], 0.20 * tb * (0.4 + 0.6 * k));
      }
    }
  }
}

// 会心専用: 十字が交わった瞬間の 1 コマ (f12) に 1 太刀目の弧も白く再フラッシュさせて重なりを飛ばす
function drawArcWhiteFlash(p, s, C) {
  const span = s.a1 - s.a0;
  const steps = 90;
  let prev = null;
  for (let i = 0; i <= steps; i++) {
    const k = i / steps;
    const arch = Math.sin(Math.PI * k);
    const a = s.a0 + span * k;
    const [x, y] = onArc(s, s.R - 1, a);
    const [xi, yi] = onArc(s, s.R - 5.5 * Math.pow(arch, 0.6), a);
    if (prev) {
      p.line(prev[0], prev[1], x, y, WHITE, 1.0);
      p.line(prev[2], prev[3], xi, yi, INK_PEACH, 0.92);
    }
    if (i % 7 === 0 && arch > 0.25) {
      p.glow(x, y, 8, WHITE, 0.48 * arch);
    }
    prev = [x, y, xi, yi];
  }
}

// 溜め (通常版は v4 と 100% 同一 / 会心版は円形リングを廃止し、2 つの光の芯へ光の筋と粒が凝縮する 7 コマ溜め)
function drawHeavyWindup(p, C, sx, sy, u, crit) {
  if (!crit) {
    // ---- 通常版 (v4 と完全一致) ----
    const va = 0.32 * Math.sin(clamp(u) * Math.PI * 0.85);
    const vx = 140, vy = 85;
    for (let y = -vy; y <= vy; y += 3) {
      const ny = y / vy;
      for (let x = -vx + (y & 2 ? 1 : 0); x <= vx; x += 3) {
        const nx = x / vx, d = Math.hypot(nx, ny);
        if (d > 1) continue;
        p.dot(x, y, INK_DARK, va * (1 - d * d));
      }
    }
    const nRays = 6;
    for (let i = 0; i < nRays; i++) {
      const a = (i / nRays) * TAU + 0.35 + u * 0.45;
      const d1 = 50 * (1 - ease.inQuad(u)) + 3;
      const d0 = d1 + 15 * (1 - u * 0.35);
      const col = u > 0.62 ? WHITE : u > 0.3 ? INK_PEACH : INK_ROSE;
      p.line(sx + Math.cos(a) * d0, sy + Math.sin(a) * d0, sx + Math.cos(a) * d1, sy + Math.sin(a) * d1, col, 0.58 + 0.42 * u);
    }
    const rPt = 24 * (1.15 - 0.85 * ease.inQuad(u));
    const pSteps = 32;
    for (let i = 0; i < pSteps; i++) {
      const a = (i / pSteps) * TAU;
      p.dot(sx + Math.cos(a) * rPt, sy + Math.sin(a) * rPt, u > 0.65 ? WHITE : INK_PEACH, 0.58 + 0.42 * u);
    }
    if (u > 0.38) {
      const pk = (u - 0.38) / 0.62;
      sparkle(p, sx, sy, 16 * pk, WHITE, C[5], 0.9 * pk);
      p.glow(sx, sy, 7.5, C[4], 0.32 * pk);
    }
    return;
  }

  // ---- 会心版 v5 (7 コマ溜め・円形リング一切なし・2 つの光の芯へ凝縮) ----
  // 1. 空気が暗藍に深く沈む
  const va = 0.58 * Math.sin(clamp(u) * Math.PI * 0.88);
  const vx = 215, vy = 122;
  for (let y = -vy; y <= vy; y += 3) {
    const ny = y / vy;
    for (let x = -vx + (y & 2 ? 1 : 0); x <= vx; x += 3) {
      const nx = x / vx, d = Math.hypot(nx, ny);
      if (d > 1) continue;
      p.dot(x, y, INK_DARK, va * (1 - d * d));
    }
  }

  // 2. 光の点 1 (左上起点 sx, sy) と 光の点 2 (敵中心 0, 0) へそれぞれ鋭い光の筋と粒が吸い込まれて凝縮
  for (const [gx, gy, nRays, reach, delay] of [[sx, sy, 10, 86, 0.0], [0, 0, 8, 96, 0.16]]) {
    const cu = clamp((u - delay) / (1 - delay));
    if (cu <= 0) continue;
    for (let i = 0; i < nRays; i++) {
      const a = (i / nRays) * TAU + 0.32 + cu * 0.55;
      const d1 = reach * (1 - ease.inQuad(cu)) + 2.5;
      const d0 = d1 + (26 - 10 * cu) * (i % 2 === 0 ? 1.0 : 0.65);
      const col = cu > 0.68 ? WHITE : cu > 0.34 ? INK_PEACH : INK_ROSE;
      p.line(gx + Math.cos(a) * d0, gy + Math.sin(a) * d0, gx + Math.cos(a) * d1, gy + Math.sin(a) * d1, col, 0.52 + 0.48 * cu);
      // 収束粒の頭を白点で際立たせる
      if (cu > 0.25) p.px(gx + Math.cos(a) * d1, gy + Math.sin(a) * d1, WHITE);
    }
  }

  // 3. 溜め後半 (f6〜f8): 2 つの光の芯が白く凝縮し、2 点を結ぶ極細の斜め閃光筋が走る (刃の形は描かない)
  if (u > 0.32) {
    const pk = (u - 0.32) / 0.68;
    sparkle(p, sx, sy, 28 * ease.outCubic(pk), WHITE, C[5], 0.95 * pk);
    p.glow(sx, sy, 10, C[4], 0.36 * pk);
  }
  if (u > 0.48) {
    const ck = (u - 0.48) / 0.52;
    sparkle(p, 0, 0, 24 * ease.outCubic(ck), WHITE, C[5], 0.90 * ck);
    p.glow(0, 0, 9, C[5], 0.34 * ck);
  }
  // 臨界 (f7〜f8): 左上起点から中心を突き抜けて右下へ、極細の予兆光筋が一筋だけ走る
  if (u > 0.72) {
    const lk = (u - 0.72) / 0.28;
    const dx = -sx, dy = -sy, len = Math.hypot(dx, dy) || 1;
    const ux = dx / len, uy = dy / len;
    const reach = (85 + 75 * ease.outCubic(lk));
    p.line(sx, sy, sx + ux * reach * 1.45, sy + uy * reach * 1.45, lk > 0.5 ? WHITE : INK_PEACH, 0.85 * lk);
    p.glowLine(sx, sy, sx + ux * reach * 1.2, sy + uy * reach * 1.2, 4.5, C[5], 0.30 * lk);
  }
}

// 会心専用・締め: 円形の輪を一切使わず、「万軍を断つ光」の長い斬撃光筋クロス＋交点の白飛び閃光＋斬撃方向の鋭い飛散片のみ！
function drawImperialLightBeams(p, C, t, t0, dur, shards) {
  const u = (t - t0) / dur;
  if (u < 0 || u >= 1) return;
  const thick = t < t0 + 0.039; // f12 の 1 コマだけ強く白飛び！
  const df = clamp((u - 0.30) / 0.70);

  // 2 本の斬撃の向き (左上↔右下: ang1, 右上↔左下: ang2) に沿って画面端近くまで突き抜ける「万軍を断つ光」の筋！
  const angles = [0.68, Math.PI - 0.68];
  const grow = ease.outCubic(clamp(u / 0.14));
  const halfLen = (thick ? 225 : 210) * grow;

  for (let ai = 0; ai < angles.length; ai++) {
    const a = angles[ai];
    const ca = Math.cos(a), sa = Math.sin(a);
    const nx = -sa, ny = ca;

    if (df > 0.28) {
      // 終盤は点間引き (純点線・無発光)
      const f = clamp((df - 0.28) / 0.72);
      const steps = 90;
      for (let i = -steps; i <= steps; i++) {
        const s = i / steps; // -1..1
        const keep = 0.22 + 0.78 * (1 - Math.abs(s));
        if (rand(ai * 97 + (i + steps) * 17, 13, 31) < Math.pow(f, 0.85) / keep) continue;
        const dist = s * halfLen;
        p.dot(ca * dist, sa * dist, f < 0.48 ? INK_PEACH : f < 0.76 ? INK_ROSE : INK_STEEL, Math.max(0.28, (1 - f * 0.65) * (1 - Math.abs(s) * 0.5)));
      }
    } else {
      // 太い 1 コマ (f12): 真っ白な主光筋＋両脇の薄桜・深紅の並走光線 / 2 コマ目以降: 極細の鋭い光の筋へ急落
      const mainA = 1 - df * 0.45;
      p.line(-ca * halfLen, -sa * halfLen, ca * halfLen, sa * halfLen, thick || u < 0.25 ? WHITE : INK_PEACH, mainA);
      if (thick) {
        // 1 コマのみ太い光の帯＋平行な万軍を断つ副光筋
        for (const [off, lenMul, col, al] of [
          [1.2, 0.96, WHITE, 0.98], [-1.2, 0.96, WHITE, 0.98],
          [2.6, 0.86, INK_PEACH, 0.92], [-2.6, 0.86, INK_PEACH, 0.92],
          [4.2, 0.72, INK_ROSE, 0.80], [-4.2, 0.72, INK_ROSE, 0.80],
          [10.0, 0.62, INK_PEACH, 0.75], [-10.0, 0.62, INK_PEACH, 0.75],
        ]) {
          const L = halfLen * lenMul;
          p.line(-ca * L + nx * off, -sa * L + ny * off, ca * L + nx * off, sa * L + ny * off, col, al);
        }
        p.glowLine(-ca * halfLen * 0.88, -sa * halfLen * 0.88, ca * halfLen * 0.88, sa * halfLen * 0.88, 10, WHITE, 0.50);
        p.glowLine(-ca * halfLen * 0.94, -sa * halfLen * 0.94, ca * halfLen * 0.94, sa * halfLen * 0.94, 15, C[4], 0.36);
      } else {
        // 細い光筋が残っている間だけ控えめな沿線グロー
        p.line(-ca * halfLen * 0.72 + nx * 1.1, -sa * halfLen * 0.72 + ny * 1.1, ca * halfLen * 0.72 + nx * 1.1, sa * halfLen * 0.72 + ny * 1.1, INK_ROSE, mainA * 0.65);
        p.glowLine(-ca * halfLen * 0.7, -sa * halfLen * 0.7, ca * halfLen * 0.7, sa * halfLen * 0.7, 5, C[5], 0.24 * (1 - df / 0.28));
      }
    }
  }

  // 交点から斬撃方向へ飛ぶ鋭い直線の光の飛散片 (円弧や輪は一切使わない)
  if (u < 0.58) {
    const su = u / 0.58;
    const sDf = clamp((su - 0.45) / 0.55);
    for (let i = 0; i < shards.length; i++) {
      const sh = shards[i];
      const ca = Math.cos(sh.a), sa = Math.sin(sh.a);
      const d0 = sh.r0 + sh.v * (t - t0) * (1 - 0.32 * su);
      const len = sh.len * (1 - 0.55 * su);
      const x0 = ca * d0, y0 = sa * d0, x1 = ca * (d0 + len), y1 = sa * (d0 + len);
      if (sDf > 0) {
        const n = Math.max(2, Math.ceil(len * 0.45));
        for (let k = 0; k <= n; k++) {
          if (rand(i * 29 + k * 11, 7, 29) < Math.pow(sDf, 0.85)) continue;
          const f = k / n;
          p.dot(x0 + (x1 - x0) * f, y0 + (y1 - y0) * f, INK_ROSE, Math.max(0.28, 1 - sDf * 0.65));
        }
      } else {
        p.line(x0, y0, x1, y1, su < 0.22 ? WHITE : INK_PEACH, 1 - su * 0.45);
      }
    }
  }

  // 交点の鋭い白飛び十字閃光 (f12 で極大に輝き、丸いグローは小さく締める)
  if (u < 0.34) {
    const fk = 1 - u / 0.34;
    flare(p, 0, 0, (thick ? 68 : 42) * Math.pow(fk, 1.3), fk, C);
    sparkle(p, 0, 0, (thick ? 44 : 26) * fk, WHITE, C[5], 0.96 * fk);
    p.glow(0, 0, thick ? 14 : 9, WHITE, (thick ? 0.52 : 0.34) * fk);
  }
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), crit = !!o.crit;
  // speed=1 → 1 コマ = 0.04167s
  // 通常 (v4 と完全同一): 溜め f2〜f4 (3 コマ: 0.02..0.146s) → 1 コマ太面 f5 (0.146..0.186s) → f6 即 1/5 細弧 → f7〜f14 点間引き
  // 会心 (v5): 溜め f2〜f8 (7 コマ: 0.02..0.312s) → 1 太刀目 1 コマ太面 f9 (0.312..0.352s) → f10〜f11 切り返し間 → 2 太刀目(より大きく厚い正面×＋1 太刀目白飛び＋万軍を断つ光筋クロス) f12 (0.438..0.478s) → f13 即極細× → f14〜f25 長めの点間引き余韻
  const T1 = crit ? 0.312 : 0.146;
  const T2 = crit ? 0.438 : -1;

  const R1 = crit ? 136 : 122;
  const SY = 0.80;
  const ROT1 = 0.72;
  const offY1 = R1 * SY * 0.46;
  const cx1 = -Math.sin(ROT1) * offY1;
  const cy1 = Math.cos(ROT1) * offY1;
  const A = {
    cx: cx1, cy: cy1, R: R1, sy: SY, rot: ROT1,
    a0: -3.02, a1: -0.08,
    W: crit ? 76 : 64,
    t0: T1, sweep: 0.022, hold: 0.018, trail: crit ? 0.30 : 0.23,
    snapDur: 0.018, snapCut: 0.84, glowScale: crit ? 1.22 : 1.0, traceFade: crit ? 0.30 : 0.18,
    noiseSeed: 31,
  };

  // 会心 2 太刀目: 1 太刀目よりさらに大きく (R=152)・厚く (W=92) して、鏡写しの正面「×」で圧倒的に締める！
  const R2 = 152;
  const ROT2 = -ROT1;
  const offY2 = R2 * SY * 0.44;
  const cx2 = -Math.sin(ROT2) * offY2;
  const cy2 = Math.cos(ROT2) * offY2;
  const B = crit ? {
    cx: cx2, cy: cy2, R: R2, sy: SY, rot: ROT2,
    a0: -0.10, a1: -3.08,
    W: 92,
    t0: T2, sweep: 0.022, hold: 0.018, trail: 0.30,
    snapDur: 0.018, snapCut: 0.85, glowScale: 1.32, traceFade: 0.32,
    noiseSeed: 53,
  } : null;

  const startA = onArc(A, A.R - 10, A.a0);
  const tipA = tipOf(A);
  const startB = B ? onArc(B, B.R - 10, B.a0) : null;
  const tipB = B ? tipOf(B) : null;

  // 交点から斬撃方向寄りに飛び散る鋭い直線光片 10 本 (会心専用・通常では乱数を一切消費しない)
  const crossShards = crit
    ? [0.68, -2.46, 2.46, -0.68, 0.50, 0.86, 2.28, 2.64, -0.50, -2.64].map((a) => ({
      a: a + Rg.range(-0.06, 0.06),
      r0: Rg.range(14, 30),
      v: Rg.range(220, 420),
      len: Rg.range(18, 36),
    }))
    : null;

  let t = 0, doneA = false, doneB = false;
  return {
    update(dt) {
      t += dt;
      if (!doneA && t >= T1 + A.sweep) {
        doneA = true;
        for (let i = 0; i < (crit ? 10 : 7); i++) {
          const a = Math.atan2(tipA.dy, tipA.dx) + Rg.range(-0.5, 0.5), sp = Rg.range(180, crit ? 420 : 340);
          ps.spawn(P.SPARK, tipA.x, tipA.y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.07, 0.16));
        }
        const lives = [0.16, 0.21, 0.26, 0.31], fracs = [0.45, 0.64, 0.82, 0.96];
        for (let i = 0; i < lives.length; i++) {
          const a = A.a0 + (A.a1 - A.a0) * fracs[i];
          const [x, y] = onArc(A, A.R - 2, a), [x2, y2] = onArc(A, A.R - 2, a + 0.05);
          const l = Math.hypot(x2 - x, y2 - y) || 1, sp = Rg.range(75, 130);
          ps.spawn(P.MOTE, x, y, ((x2 - x) / l) * sp, ((y2 - y) / l) * sp, lives[i], i & 1 ? INK_PEACH : INK_ROSE, 0.65);
        }
      }
      if (B && !doneB && t >= T2 + B.sweep) {
        doneB = true;
        for (let i = 0; i < 16; i++) {
          const baseA = i % 2 === 0 ? 0.68 : Math.PI - 0.68;
          const a = baseA + (i % 4 < 2 ? 0 : Math.PI) + Rg.range(-0.28, 0.28);
          const sp = Rg.range(200, 460);
          ps.spawn(P.SPARK, Math.cos(a) * 12, Math.sin(a) * 12, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.08, 0.20));
        }
        const lives = [0.20, 0.27, 0.34, 0.41], fracs = [0.42, 0.62, 0.80, 0.96];
        for (let i = 0; i < lives.length; i++) {
          const a = B.a0 + (B.a1 - B.a0) * fracs[i];
          const [x, y] = onArc(B, B.R - 2, a), [x2, y2] = onArc(B, B.R - 2, a - 0.05);
          const l = Math.hypot(x2 - x, y2 - y) || 1, sp = Rg.range(80, 145);
          ps.spawn(P.MOTE, x, y, ((x2 - x) / l) * sp, ((y2 - y) / l) * sp, lives[i], i & 1 ? WHITE : INK_PEACH, 0.72);
        }
      }
      ps.update(dt);
      for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 6.0 * dt; m.vy -= m.vy * 6.0 * dt; }
    },
    draw(p) {
      if (t <= 0.01) return;

      // ---- 段 1: 溜め ----
      if (t < T1) {
        const u = clamp((t - 0.015) / (T1 - 0.015));
        drawHeavyWindup(p, C, startA[0], startA[1], u, crit);
        return;
      }

      // ---- 1 太刀目: 巨大な正面斜め大剣三日月 ----
      drawGreatswordCrescent(p, A, t, C, crit, false);
      const ua = t - T1;
      const ga = ua - A.sweep * 0.6;
      if (ga > 0 && ga < 0.09) {
        sparkle(p, tipA.x, tipA.y, (crit ? 15 : 12) * ease.outBack(Math.min(1, ga / 0.025)) * (1 - ga / 0.09), WHITE, C[4], 0.72);
      }
      if (ua > 0 && ua < 0.085) {
        const [hx, hy] = onArc(A, A.R - 5, A.a0 + (A.a1 - A.a0) * ease.outExpo(clamp(ua / A.sweep)));
        p.glow(hx, hy, crit ? 13 : 11, C[4], (crit ? 0.38 : 0.32) * (1 - ua / 0.085));
      }

      // ---- 会心のみ: 切り返し閃光 (f10〜f11) → 2 太刀目 (f12: より大きく厚い正面×＋1 太刀目白飛び＋万軍を断つ光筋クロス) ----
      if (B) {
        if (t >= T1 + 0.055 && t < T2) {
          const hu = clamp((t - (T1 + 0.055)) / (T2 - (T1 + 0.055)));
          sparkle(p, startB[0], startB[1], 22 * Math.sin(hu * Math.PI), WHITE, C[5], 0.90);
          p.glow(startB[0], startB[1], 9, C[4], 0.32 * Math.sin(hu * Math.PI));
        }
        if (t >= T2) {
          const isCrossFlash = t < T2 + 0.039; // f12 の 1 コマだけ交差する 2 本が白く飛ぶ！
          if (isCrossFlash) drawArcWhiteFlash(p, A, C);
          drawGreatswordCrescent(p, B, t, C, true, isCrossFlash);
          drawImperialLightBeams(p, C, t, T2, 0.44, crossShards);
          const ub = t - T2;
          const gb = ub - B.sweep * 0.6;
          if (gb > 0 && gb < 0.095) {
            sparkle(p, tipB.x, tipB.y, 18 * ease.outBack(Math.min(1, gb / 0.025)) * (1 - gb / 0.095), WHITE, C[4], 0.78);
          }
        }
      }

      ps.draw(p);
    },
  };
}
