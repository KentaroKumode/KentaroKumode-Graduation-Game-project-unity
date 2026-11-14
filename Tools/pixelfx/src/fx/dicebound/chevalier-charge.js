// DICE BOUND 用 (2026-11-14): シュヴァリエの大技のチャージ (攻撃しないターン)。 画面全体 (480×270)・原点はチャージの中心 (胸の前)。
// 運びは灰燼の王・審判官と同じ (画面じゅうの光の筋と粒が一点へ吸い込まれる)。 違いは完了の姿: 柔らかい光は一切出さず、
// **鋭い光** (細く長い十字のきらめき) だけが残る。 ゲーム側は、 完了と同時に画面全体をかなり暗く落とす (2026-11-14 作者の指定) ── 暗い画面に、 一点だけが鋭く光る。
// 完了後は待機 (dicebound/chevalier-charged) と同じ関数 glintHeld() で描く。
// 作り: 吸い込み = explosions/implosion (王・審判官のチャージと同じ)・きらめき = 剣の切っ先の光 (transitions/slash-wipe の tip glint = sparkle を、 細い線で長く引いた物)。
// The gathering streaks, grit and rings are adapted from this repository's explosions/implosion (after Sprite Fusion's open-source energy charge,
// https://www.spritefusion.com/pixel-art-vfx/energy), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, hex, mix, clamp, ease, TAU, WHITE } from "../../core/index.js";

export const STEEL = ["#151a3d", "#212f6b", "#2c58aa", "#3a8bd8", "#62c8f4", "#b8c9e8", "#e8f1ff", "#ffffff"];   // transitions/slash-wipe の色

export const meta = {
  name: "Chevalier charge (DICE BOUND)",
  desc: "The Chevalier readies the decisive thrust: light from the whole screen streaks and spirals to a point before his chest, and a single hard, thin glint is left there.",
  tags: ["blade", "dicebound", "boss", "charge", "screen", "one-shot"],
  size: [480, 270],
  origin: [240, 130],    // 敵の足もと (240,196) の 66 上
  duration: 2.75,        // 1 秒 8 コマで 23 コマ (最後のコマが 2.75 秒)
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: STEEL, seed: 5 },
};

// 待機へ渡す時刻: 最後のコマの 1 コマ後 (1 秒 8 コマ) が、 待機の 0 秒 (library/dicebound/judge-flow.md の「道具の罠」)
const HANDOFF = 2.75 + 1 / 8;
const SUCK = 1.6, VOID = hex("#05060c");
export const HELD = 1.5, REST = 36;   // 待機の 1 周 (秒) と、 きらめきの長さ (横の片側・px)

/** 鋭いきらめき: 1 ドット幅の細い十字 (横が長く、 縦は 7 割)、 根もとだけ 3 ドット幅、 短い斜め、 小さな白い芯。 まわりに薄い青白いブルーム。 L = 横の片側の長さ。 */
export function glint(p, C, x0, y0, L, k = 1) {
  if (L < 1) return;
  for (const [dx, dy, m] of [[1, 0, 1], [-1, 0, 1], [0, 1, 0.7], [0, -1, 0.7]]) {
    const len = L * m;
    for (let d = 0; d <= len; d++) {
      const u = d / len, x = x0 + dx * d, y = y0 + dy * d;
      p.dot(x, y, u < 0.5 ? WHITE : u < 0.8 ? C[6] : C[5], k * (u < 0.7 ? 1 : 1 - (u - 0.7) / 0.3));
      if (u < 0.16) { p.dot(x + dy, y + dx, C[6], k); p.dot(x - dy, y - dx, C[6], k); }
    }
  }
  const dl = Math.round(L * 0.22);
  for (let d = 1; d <= dl; d++) for (const sx of [1, -1]) for (const sy of [1, -1]) p.dot(x0 + sx * d, y0 + sy * d, d < dl * 0.6 ? C[6] : C[5], k);
  p.circle(x0, y0, 2, WHITE);
  // 青白いブルームを薄く (2026-11-14 作者の指定: 線だけだとベタ塗りで、 光に見えない)。 芯のまわりと、 縦横の腕に沿って
  p.glow(x0, y0, 9 + L * 0.3, C[5], 0.26 * k);
  p.glowLine(x0 - L * 0.75, y0, x0 + L * 0.75, y0, 4, C[5], 0.2 * k);
  p.glowLine(x0, y0 - L * 0.5, x0, y0 + L * 0.5, 4, C[5], 0.16 * k);
  p.glow(x0, y0, 5, WHITE, 0.5 * k);
}

/** 溜まった状態の姿 (待機のループ 1 コマぶん)。 チャージの終わりと待機の両方がこれを描く。 位置は動かず、 長さだけが脈を打つ。 boost = 完了の瞬間の強さ。 */
export function glintHeld(p, C, ti, boost = 0) {
  const t = ((ti % HELD) + HELD) % HELD, pulse = 0.5 + 0.5 * Math.sin((t / HELD) * TAU * 2);
  glint(p, C, 0, 0, REST + 6 * pulse + 150 * boost * boost);
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
        // 芯: 集まるほど白く。 小さいまま (膨らませない ── 鋭さを保つ)
        const cr = 1 + 2 * k * k;
        p.disc(0, 0, cr + 3, VOID, 0.8);
        p.circle(0, 0, cr + 1, k > 0.6 ? C[5] : C[3]);
        p.circle(0, 0, cr, k > 0.35 ? WHITE : C[6]);
        p.glow(0, 0, 10 + 14 * k, C[4], 0.2 + 0.3 * k);
      } else {
        // チャージ完了: きらめきが長く走って (横は画面の幅近く)、 0.3 秒で待機の長さに落ち着く。 ここからは待機と同じ絵
        glintHeld(p, C, t - HANDOFF, 1 - clamp((t - SUCK) / 0.3));
      }
      ps.draw(p);
    },
  };
}
