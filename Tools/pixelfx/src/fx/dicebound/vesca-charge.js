// DICE BOUND 用 (2026-11-14): ヴェスカの大技のチャージ (攻撃しないターン)。 画面全体 (480×270)・原点はチャージの中心 (胸の前)。
// 運びは灰燼の王・審判官・シュヴァリエと同じ (画面じゅうの光の筋と粒が一点へ吸い込まれる)。 違いは完了の姿: **時計の文字盤**が開く (ヴェスカの〈停滞する時間〉)。
// 待機 (dicebound/vesca-charged) では、 針が 1 目盛り進んでは戻る (時が引っかかっている)。 まわりの粒は宙に止まったまま、 動かない。 ほかの 3 体の待機は脈を打つが、 ここは「止まっている」のが差。
// 完了後は待機と同じ関数 clockHeld() で描く。
// 作り: 吸い込み = explosions/implosion (ほかの 3 体のチャージと同じ)・文字盤 = arcane/time-stop の時計 (輪・12 の目盛り・1 本の針・宙に止まる粒) を、 線と点だけで描き直した物。
// The gathering streaks, grit and rings are adapted from this repository's explosions/implosion and the clock face from its arcane/time-stop
// (after Sprite Fusion's open-source energy charge, https://www.spritefusion.com/pixel-art-vfx/energy), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, hex, mix, clamp, ease, rng, TAU, WHITE } from "../../core/index.js";

export const VIOLET = ["#0a0614", "#1c1236", "#3a2470", "#5a3aa8", "#8a64e0", "#b89cf8", "#e4d8ff", "#ffffff"];

export const meta = {
  name: "Vesca charge (DICE BOUND)",
  desc: "Vesca gathers stopped time: light from the whole screen streaks and spirals to a point before her chest, and a clock face springs open there.",
  tags: ["time", "dicebound", "boss", "charge", "screen", "one-shot"],
  size: [480, 270],
  origin: [240, 130],    // 敵の足もと (240,196) の 66 上
  duration: 2.75,        // 1 秒 8 コマで 23 コマ (最後のコマが 2.75 秒)
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: VIOLET, seed: 6 },
};

const HANDOFF = 2.75 + 1 / 8;   // 待機の 0 秒 = 最後のコマの 1 コマ後 (library/dicebound/judge-flow.md の「道具の罠」)
const SUCK = 1.6, VOID = hex("#05040a");
export const HELD = 1.5, RC = 24, NOON = -Math.PI / 2, TICK = TAU / 12;   // 待機の 1 周 (秒)・文字盤の半径・12 時の向き・1 目盛り

/** 時計の文字盤: 輪・12 の目盛り (3 時間ごとは長い)・針 1 本・中心の点。 hand = 針の向き (rad)・open = 開き具合 (0〜1)・x0 = 横のずれ。 */
export function clock(p, C, x0, y0, hand, open = 1, col = null) {
  const R = RC * open, hi = col ?? C[6], mid = col ?? C[5], lo = col ?? C[4];
  if (R < 2) { p.circle(x0, y0, 2, WHITE); return; }
  p.disc(x0, y0, R, VOID, 0.55);
  p.ring(x0, y0, R, 1, mid);
  p.ring(x0, y0, R + 4, 1, lo, (v, g) => (Math.floor((g / TAU) * 48) % 2 ? 0.8 : 0));   // 外の破線の輪
  for (let i = 0; i < 12; i++) {
    const a = i * TICK, r0 = R - (i % 3 === 0 ? 6 : 3), c = Math.cos(a), s = Math.sin(a);
    p.line(x0 + c * r0, y0 + s * r0, x0 + c * (R - 1), y0 + s * (R - 1), i % 3 === 0 ? hi : lo);
  }
  p.line(x0, y0, x0 + Math.cos(hand) * R * 0.78, y0 + Math.sin(hand) * R * 0.78, WHITE);
  p.circle(x0, y0, 1.5, WHITE);
  p.glow(x0, y0, R * 0.9, C[4], 0.22);
}

/** 宙に止まった粒 (動かない)。 1 つだけ、 ごくゆっくり明滅する。 */
export function hung(p, C, seed, k = 1, tw = 1) {
  for (let i = 0; i < 16; i++) {
    const R = rng(seed, i), a = R.range(0, TAU), d = R.range(RC + 12, 110), x = Math.cos(a) * d, y = Math.sin(a) * d * 0.75, c = R.pick([C[4], C[5], C[6]]);
    if (R.next() > k) continue;
    // 吸い込まれる途中で止まった筋: 中心を向いた短い線
    p.line(x, y, x - Math.cos(a) * R.range(2, 6), y - Math.sin(a) * 0.75 * R.range(2, 6), i === 3 ? (tw > 0.5 ? WHITE : c) : c);
  }
}

/** 溜まった状態の姿 (待機のループ 1 コマぶん)。 針は 12 時から 1 目盛り進んで (0〜0.75 秒)、 戻る (0.75〜1.5 秒)。 行き過ぎて止まる (カチッ)。 */
export function clockHeld(p, C, ti, open = 1) {
  const t = ((ti % HELD) + HELD) % HELD, half = HELD / 2;
  const a = t < half ? ease.outBack(clamp(t / 0.09), 3) : 1 - ease.outBack(clamp((t - half) / 0.09), 3);
  hung(p, C, 11, Math.min(1, open), t < half ? 1 : 0);
  clock(p, C, 0, 0, NOON + a * TICK, open);
}

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed);
  const ps = new Particles({ rng: R, sparks: [C[7], C[6], C[5], C[4]] });
  const grit = Array.from({ length: 100 }, () => {
    const t0 = R.range(0, SUCK - 0.5);
    return { a: R.range(0, TAU), d: R.range(70, 260), t0, T: R.range(0.45, Math.max(0.5, SUCK - 0.05 - t0)), spin: R.sign() * R.range(0.4, 1.2), c: R.pick([C[3], C[4], C[5], C[6]]) };
  });
  let t = 0;
  return {
    update(dt) {
      t += dt;
      if (t < SUCK - 0.2) {
        const k = t / SUCK;
        for (let n = 0; n < 1 + Math.floor(k * 5); n++) {
          if (!R.chance(0.85)) continue;
          const a = R.range(0, TAU), D = R.range(80, 250) * (1 - 0.25 * k), T = R.range(0.2, 0.36), v = (D * 3) / (Math.exp(3 * T) - 1);
          ps.spawn(P.STREAK, Math.cos(a) * D, Math.sin(a) * D * 0.75, -Math.cos(a) * v, -Math.sin(a) * v * 0.75, T, R.chance(0.5) ? C[6] : C[5]);
        }
      }
      ps.update(dt);
    },
    draw(p) {
      const k = clamp(t / SUCK);
      if (t <= SUCK) {
        for (let i = 0; i < 4; i++) {
          const u = (t - i * 0.32) / 0.62;
          if (u <= 0 || u >= 1) continue;
          p.ring(0, 0, (150 - i * 18) * (1 - ease.inQuad(u)) + 8, 1, u > 0.7 ? C[6] : u > 0.4 ? C[5] : C[3], 0.35 + 0.65 * u);
        }
        for (const q of grit) {
          const u = (t - q.t0) / q.T;
          if (u <= 0 || u >= 1) continue;
          const d = q.d * (1 - ease.inCubic(u)), a = q.a + q.spin * ease.inQuad(u) * 2, d2 = d + 5 * u * u + 0.5;
          p.line(Math.cos(a - q.spin * 0.1) * d2, Math.sin(a - q.spin * 0.1) * d2 * 0.75, Math.cos(a) * d, Math.sin(a) * d * 0.75, (v) => (v < 0.5 ? mix(q.c, VOID, 0.4) : q.c));
        }
        const cr = 1.2 + 2.5 * k * k;
        p.disc(0, 0, cr + 3, VOID, 0.8);
        p.circle(0, 0, cr + 1, k > 0.6 ? C[5] : C[3]);
        p.circle(0, 0, cr, k > 0.35 ? WHITE : C[6]);
        p.glow(0, 0, 10 + 18 * k, C[4], 0.2 + 0.35 * k);
      } else {
        // チャージ完了: 文字盤が開く (0.3 秒・少し行き過ぎて戻る)。 白い輪が 1 つ広がって消える。 ここからは待機と同じ絵
        const q = clamp((t - SUCK) / 0.3);
        clockHeld(p, C, t - HANDOFF, q >= 1 ? 1 : ease.outBack(q) * 0.999);
        if (q < 1) p.ring(0, 0, RC + 70 * q, 1, WHITE, 1 - q);
      }
      ps.draw(p);
    },
  };
}
