// DICE BOUND 用 (2026-11-14): 審判官の大技のチャージ (攻撃しないターン)。 画面全体 (480×270)・原点は審判官の頭上の一点 (日輪の中心)。
// 灰燼の王のチャージ (dicebound/gouka-charge) と同じ運び ── 画面じゅうの光の筋と粒が一点へ吸い込まれる ── だが、
// 集まる先は胸ではなく頭上で、 完了すると十字の光ではなく日輪 (holy/sunburst) が開く (2026-11-14 作者の決定)。
// 完了後は待機 (dicebound/judge-charged) と同じ関数 sun() で描くので、 チャージからループへ見た目が途切れない。
// 作り: 吸い込み = explosions/implosion (王のチャージと同じ)・日輪 = holy/sunburst (光条・糸・冠・芯・きらめき)。
// The gathering streaks, grit and rings are adapted from this repository's explosions/implosion and the sun from its holy/sunburst
// (after Sprite Fusion's open-source energy charge and light effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, hex, mix, cycle, sparkle, clamp, ease, TAU, WHITE } from "../../core/index.js";
import { GOLD, ASH } from "../holy/_lib.js";

export const meta = {
  name: "Judge charge (DICE BOUND)",
  desc: "The Judge gathers a verdict: light from the whole screen streaks and spirals to a point above his head, rings fall onto it, and a sun opens there.",
  tags: ["holy", "dicebound", "boss", "charge", "screen", "one-shot"],
  size: [480, 270],
  origin: [240, 52],     // 敵の足もと (240,196) の 144 上 = 頭 (高さ 120) の少し上
  duration: 2.7,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: GOLD, rays: 8, scale: 1, seed: 4 },
};

const SUCK = 1.6, VOID = hex("#0a0806");
export const SUN_T = 4, SUN_SEED = 3;   // 日輪の 1 周 (秒・1 秒 8 コマで 32 コマ) と、 きらめきの種
const frac = (x) => x - Math.floor(x);

// 光条 1 本 (holy/sunburst の ray): 根もとは白、 先へ金、 細く薄く。 R0 = 根もとの半径
function ray(p, C, a, R0, L, w0, k) {
  if (L < 1) return;
  const ca = Math.cos(a), sa = Math.sin(a), x1 = ca * (R0 + L), y1 = sa * (R0 + L), pad = w0 + 1;
  p.shade(Math.min(0, x1) - pad, Math.min(0, y1) - pad, Math.max(0, x1) + pad, Math.max(0, y1) + pad, (x, y, out) => {
    const along = x * ca + y * sa - R0, u = along / L;
    if (u < 0 || u > 1) return;
    const across = Math.abs(-x * sa + y * ca), w = (w0 / 2) * (1 - u * 0.85) + 0.25;
    if (across > w) return;
    const spine = across < 0.5, rim = across > w - 0.6 && w > 0.9;
    out.c = spine ? (u < 0.4 ? WHITE : u < 0.75 ? C[6] : C[5]) : rim && u > 0.15 ? C[3] : u < 0.3 ? C[6] : u < 0.62 ? C[5] : C[4];
    out.a = u < 0.72 ? k : k * (1 - (u - 0.72) / 0.28) * 1.2;
  });
  p.glowLine(ca * R0, sa * R0, ca * (R0 + L * 0.8), sa * (R0 + L * 0.8), 5, C[5], 0.16 * k);
}

/**
 * 日輪 (待機のループ 1 コマぶん)。 チャージの終わりと待機の両方がこれを描く。 原点が中心。
 * ti = 待機の時刻 (負でもよい)。 open = 0〜1: 開き具合 (0 = 芯だけ・1 = 開き切った姿)。 S = 大きさの倍率。
 */
export function sun(p, C, ti, rays, S = 1, open = 1) {
  const T = SUN_T, t = ((ti % T) + T) % T, N = rays * 2, rot = (t / T) * ((2 * TAU) / N), CORE = 4.5 * S, RING = 7.5 * S, R0 = 9 * S;
  p.glow(0, 0, 62 * S * (0.4 + 0.6 * open), C[4], 0.3 + 0.06 * Math.sin((t / T) * TAU * 2));
  // 光条の間の細い糸
  for (let i = 0; i < N; i++) {
    const a = rot + ((i + 0.5) * TAU) / N, L = (14 + 6 * Math.sin(a * 3 + (t / T) * TAU)) * S * open;
    if (L > 1) p.line(Math.cos(a) * (R0 + 1), Math.sin(a) * (R0 + 1), Math.cos(a) * (R0 + L), Math.sin(a) * (R0 + L), (u) => (u < 0.3 ? C[6] : C[5]), (u) => 0.7 - u * 0.8);
  }
  // 光条: 長いのと短いのが交互。 波が回って伸び縮みする
  for (let i = 0; i < N; i++) {
    const a = rot + (i * TAU) / N, long = i % 2 === 0, breathe = 0.86 + 0.14 * Math.sin(a * 3 - (t / T) * TAU * 2), L = (long ? 36 : 21) * breathe * S * open;
    ray(p, C, a, R0, L, (long ? 4 : 2.6) * S, 1);
    // 長い光条の上を、 きらめきが先へ走る
    if (long && open >= 1) {
      const g = frac((t / T) * 2 + (a * 3) / TAU), r = R0 + g * L;
      if (g < 0.9) { p.px(Math.cos(a) * r, Math.sin(a) * r, WHITE); p.glow(Math.cos(a) * r, Math.sin(a) * r, 5, C[6], 0.45 * (1 - g)); }
      else sparkle(p, Math.cos(a) * (R0 + L), Math.sin(a) * (R0 + L), 1 + 3 * Math.sin(((g - 0.9) / 0.1) * Math.PI), WHITE, C[6], 0.5);
    }
  }
  // 冠と芯
  p.shade(-RING - 2, -RING - 2, RING + 2, RING + 2, (x, y, out) => {
    const d = Math.hypot(x, y);
    if (d > RING) return;
    const flick = 0.5 + 0.5 * Math.sin(Math.atan2(y, x) * 5 + (t / T) * TAU * 3);
    out.c = d < CORE ? WHITE : d < CORE + 1.5 ? (flick > 0.6 ? WHITE : C[6]) : flick > 0.5 ? C[5] : C[4];
  });
  p.glow(0, 0, 16 * S, WHITE, 0.28);
  // まわりのきらめき
  if (open >= 1) cycle(t, T, 10, SUN_SEED, (g) => g.range(0.35, 0.7), (age, life, R) => {
    const a = R.range(0, TAU), d = R.range(16, 44) * S, k = Math.sin((age / life) * Math.PI);
    sparkle(p, Math.cos(a) * d, Math.sin(a) * d, 1 + 2.4 * k * R.range(0.5, 1), WHITE, C[5], 0.4 * k);
  });
}

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed), S = o.scale;
  const ps = new Particles({ rng: R, sparks: [C[7], C[6], C[5], C[4]] });
  // 渦を巻いて寄る粒 (金の光と、 石の塵)
  const grit = Array.from({ length: 110 }, () => {
    const t0 = R.range(0, SUCK - 0.5);
    return { a: R.range(0, TAU), d: R.range(70, 260), t0, T: R.range(0.45, Math.max(0.5, SUCK - 0.05 - t0)), spin: R.sign() * R.range(0.4, 1.2), c: R.chance(0.4) ? hex(R.pick(ASH.slice(2))) : R.pick([C[4], C[5], C[6]]) };
  });
  let t = 0;
  return {
    update(dt) {
      t += dt;
      if (t < SUCK - 0.2) {
        // 光の筋: 画面じゅうから頭上の一点へ。 進むほど本数が増える
        const k = t / SUCK;
        for (let n = 0; n < 1 + Math.floor(k * 5); n++) {
          if (!R.chance(0.85)) continue;
          const a = R.range(0, TAU), D = R.range(80, 250) * (1 - 0.25 * k), T = R.range(0.2, 0.36), v = (D * 3) / (Math.exp(3 * T) - 1);
          ps.spawn(P.STREAK, Math.cos(a) * D, Math.sin(a) * D * 0.8, -Math.cos(a) * v, -Math.sin(a) * v * 0.8, T, R.chance(0.5) ? C[6] : C[5]);
        }
      }
      ps.update(dt);
    },
    draw(p) {
      const k = clamp(t / SUCK);
      if (t <= SUCK) {
        // 輪が一点へ落ちる。 4 本、 順に。 寄るほど明るい
        for (let i = 0; i < 4; i++) {
          const u = (t - i * 0.32) / 0.62;
          if (u <= 0 || u >= 1) continue;
          p.ring(0, 0, (150 - i * 18) * (1 - ease.inQuad(u)) + 10, u > 0.7 ? 2 : 1, u > 0.7 ? C[6] : u > 0.4 ? C[5] : C[3], 0.35 + 0.65 * u);
        }
        for (const q of grit) {
          const u = (t - q.t0) / q.T;
          if (u <= 0 || u >= 1) continue;
          const d = q.d * (1 - ease.inCubic(u)), a = q.a + q.spin * ease.inQuad(u) * 2, d2 = d + 5 * u * u + 0.5;
          p.line(Math.cos(a - q.spin * 0.1) * d2, Math.sin(a - q.spin * 0.1) * d2 * 0.8, Math.cos(a) * d, Math.sin(a) * d * 0.8, (v) => (v < 0.5 ? mix(q.c, VOID, 0.4) : q.c));
        }
        // 芯: 集まるほど大きく、 白く
        const cr = 1.5 + 3 * k * k * S;
        p.disc(0, 0, cr + 4, VOID, 0.8);
        p.circle(0, 0, cr + 1.6, k > 0.6 ? C[5] : C[3]);
        p.circle(0, 0, cr, k > 0.35 ? WHITE : C[6]);
        p.glow(0, 0, 14 + 40 * k, C[4], 0.25 + 0.5 * k);
      } else {
        // チャージ完了: 日輪が開く (0.35 秒・少し行き過ぎて戻る)。 ここからは待機と同じ絵。 待機の時刻は、 このチャージの終わりが 0 秒になるように合わせる
        const q = clamp((t - SUCK) / 0.35);
        sun(p, C, t - meta.duration, o.rays, S, q >= 1 ? 1 : ease.outBack(q) * 0.999);
        if (q < 1) p.glow(0, 0, 90 * (1 - q) + 20, WHITE, 0.6 * (1 - q));
      }
      ps.draw(p);
    },
  };
}
