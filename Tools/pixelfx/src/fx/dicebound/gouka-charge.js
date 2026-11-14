// DICE BOUND 用 (2026-11-14): 灰燼の王の大技〈業火〉のチャージ (攻撃しないターン)。 画面全体 (480×270)・原点は王の足もとの中心。
// 王のまわりの火が小さくなり、 輪が足もとへ縮み、 画面じゅうの火の粉と灰が王の胸へ吸い込まれて、 白く焼けた一点が震えて残る。
// 作り (library/dicebound/gouka-charge.md):
//   吸い込まれる光の筋・渦を巻いて寄る粒・縮む輪・潰れて白くなる芯と「静けさ」の拍 = explosions/implosion
//   足もとの炎の舌 = fire/_lib の flameTongue (fire/fire-ring の並べ方: 床の楕円の上・奥から手前の順)
// The gathering streaks, spiralling grit, contracting rings and the crushed core are adapted from this repository's explosions/implosion
// (after Sprite Fusion's open-source energy charge, https://www.spritefusion.com/pixel-art-vfx/energy), and the flames from its fire
// (https://www.spritefusion.com/pixel-art-vfx/fire), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, fit, cycle, hex, mix, clamp, smooth, ease, noise, rand, sparkle, TAU, WHITE } from "../../core/index.js";
import { flameTongue, floorLight, LAYERS, BIG_LAYERS } from "../fire/_lib.js";

export const meta = {
  name: "Gouka charge (DICE BOUND)",
  desc: "The Ash King gathers his inferno: the fire round him shrinks, rings contract onto his feet, embers and ash from the whole screen spiral into his chest, and a white-hot point is left trembling in the hush.",
  tags: ["fire", "dicebound", "boss", "charge", "screen", "one-shot"],
  size: [480, 270],
  origin: [240, 196],
  duration: 2.7,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff"],
    ashes: ["#a39cab", "#7b7484", "#5c5664", "#433e4b"],
    chest: 58,          // 芯の高さ (足もとから)
    flames: false,      // 足もとを囲む炎の輪を出すか (2026-11-14 作者の決定: 出さない)
    ash: 3,             // 烈炎の数。 多いほど灰が多く寄る
    seed: 4,
  },
};

const SUCK = 1.6, SY = 0.22, VOID = hex("#07050a");
export const REST = 44;   // 溜まった状態の十字の長さ (横の片側・px)

/** 芯の十字の光: 横が長く、 縦は 6 割。 根もとは白く太く、 先へ細く薄く。 L = 横の片側の長さ。 */
export function cross(p, F, x0, y0, L, diag = 0) {
  const arms = [[1, 0, 1], [-1, 0, 1], [0, 1, 0.6], [0, -1, 0.6]];
  if (diag > 0) for (const sx of [1, -1]) for (const sy of [1, -1]) arms.push([sx * 0.707, sy * 0.707, diag]);
  for (const [dx, dy, m] of arms) {
    const len = L * m;
    for (let d = 0; d <= len; d += 0.5) {
      const u = d / len, x = x0 + dx * d, y = y0 + dy * d;
      p.dot(x, y, u < 0.35 ? WHITE : u < 0.7 ? F[7] : F[5], u < 0.6 ? 1 : 1 - (u - 0.6) / 0.4);
      if (u < 0.3 && m >= 0.6) { p.dot(x + dy, y + dx, F[7], 1); p.dot(x - dy, y - dx, F[7], 1); }
    }
  }
}

export const HELD = 1.5, HELD_SEED = 6;   // 待機の 1 周 (秒) と、 火の粉の種

/**
 * 溜まった状態の姿 (待機のループ 1 コマぶん)。 チャージの終わりと、 待機 (gouka-charged) の両方がこれを描くので、 見た目が必ず一致する。
 * ti = 待機の時刻 (負でもよい)。 boost = 0〜1: チャージ完了の瞬間の強さ (十字が長く、 光が強く、 芯が大きい)。
 */
export function held(p, F, CY, seed, ti, boost = 0) {
  const t = ((ti % HELD) + HELD) % HELD, pulse = 0.5 + 0.5 * Math.sin((t / HELD) * TAU * 2);
  // 6 コマに 1 回、 1 ドット震える (溜まり切っていて不安定)
  const f = Math.floor(t * 8 + 1e-4), jit = f % 6 === 2 ? 1 : f % 6 === 3 ? -1 : 0;
  // 火の粉: まばらに、 ゆっくり、 渦を巻いて芯へ
  cycle(t, HELD, 16, seed * 3 + 1, (g) => g.range(0.8, 1.3), (age, life, R) => {
    const a0 = R.range(0, TAU), d0 = R.range(50, 130), spin = R.sign() * R.range(0.5, 1.2), c = R.pick([F[4], F[5], F[6]]);
    const pos = (q) => { const a = a0 + spin * q, d = d0 * (1 - ease.inQuad(q)) + 4; return [Math.cos(a) * d, CY + Math.sin(a) * d * 0.7]; };
    const q = age / life, [x, y] = pos(q), [x2, y2] = pos(Math.max(0, q - 0.05));
    p.line(x2, y2, x, y, c, Math.min(1, q * 4) * (1 - boost));
  });
  ball(p, F, jit, CY, 3 + 0.7 * pulse + 3 * boost);
  cross(p, F, jit, CY, REST + 7 * pulse + 110 * boost * boost);
  p.glow(0, CY, 30 + 8 * pulse + 40 * boost, F[5], 0.3 + 0.12 * pulse + 0.4 * boost);
  p.glow(0, CY, 12, WHITE, 0.4 + 0.15 * pulse);
}

/** 白く焼けた芯: 暗い縁・橙の輪・白い玉。 r = 玉の半径。 */
export function ball(p, F, x, y, r) {
  p.disc(x, y, r + 4, VOID, 0.8);
  p.circle(x, y, r + 1.6, F[5]);
  p.circle(x, y, r, WHITE);
}


export function create(o) {
  const F = fit(o.palette, 9), H = [...F].reverse(), ASHC = fit(o.ashes, 4), R = new Rng(o.seed), ASH = Math.max(0, o.ash), CY = -o.chest;
  const ps = new Particles({ rng: R, sparks: [F[8], F[6], F[5], F[3]] }), lights = new Lights();
  // 渦を巻いて寄る粒 (灰と残り火)。 だんだん速く
  const grit = Array.from({ length: 60 + ASH * 22 }, () => {
    const t0 = R.range(0, SUCK - 0.5);
    return { a: R.range(0, TAU), d: R.range(70, 250), t0, T: R.range(0.45, Math.max(0.5, SUCK - 0.05 - t0)), spin: R.sign() * R.range(0.4, 1.2), c: R.chance(0.6) ? R.pick(ASHC) : R.pick([F[3], F[4], F[5]]) };
  });
  // 足もとを囲む炎 (床の楕円の上に 26 本)
  const flames = Array.from({ length: 34 }, (_, i) => ({ a: (i / 34) * TAU + R.range(-0.08, 0.08), h: R.range(24, 44), w: R.range(4.5, 7.5), n: R.int(1, 900) }));
  let t = 0;
  return {
    update(dt) {
      t += dt;
      if (t < SUCK - 0.2) {
        // 光の筋: 画面じゅうから芯へ。 進むほど本数が増える
        const k = t / SUCK;
        for (let n = 0; n < 1 + Math.floor(k * 5); n++) {
          if (!R.chance(0.85)) continue;
          const a = R.range(0, TAU), D = R.range(80, 240) * (1 - 0.25 * k), T = R.range(0.2, 0.36), v = (D * 3) / (Math.exp(3 * T) - 1);
          ps.spawn(P.STREAK, Math.cos(a) * D, CY + Math.sin(a) * D * 0.7, -Math.cos(a) * v, -Math.sin(a) * v * 0.7, T, R.chance(0.5) ? F[6] : F[4]);
        }
      }
      ps.update(dt);
      lights.update(dt);
    },
    draw(p) {
      const k = clamp(t / SUCK), still = t > SUCK, N = (rate, n, off = 0) => noise(t * rate + off, n);
      // 床の明かり: 輪と一緒に縮む
      const RR = 104 * (1 - ease.inCubic(k)) + 20;
      floorLight(p, 0, 0, RR * 1.5, RR * 1.5 * SY + 4, F[4], 0.34 * (1 - 0.7 * k) * (still ? Math.max(0, 1 - (t - SUCK) / 0.3) : 1));
      // 輪が足もとへ縮む。 4 本、 順に。 寄るほど明るい
      if (!still) for (let i = 0; i < 4; i++) {
        const u = (t - i * 0.32) / 0.62;
        if (u <= 0 || u >= 1) continue;
        const r = (200 - i * 22) * (1 - ease.inQuad(u)) + 18, n = Math.ceil(r * 5), c = u > 0.7 ? F[7] : u > 0.4 ? F[5] : F[3];
        for (let j = 0; j < n; j++) { const g = (j / n) * TAU, y = Math.sin(g) * r * SY; if (y < 0 && Math.abs(Math.cos(g) * r) < 46) continue; const x = Math.cos(g) * r; p.dot(x, y, c, 0.35 + 0.65 * u); if (u > 0.3) p.dot(x, y + 1, c, 0.35 + 0.65 * u); if (j % 6 === 0) p.glow(x, y, 6, F[4], 0.12 * u); }
      }
      // 渦を巻いて寄る粒
      if (!still) for (const q of grit) {
        const u = (t - q.t0) / q.T;
        if (u <= 0 || u >= 1) continue;
        const d = q.d * (1 - ease.inCubic(u)), a = q.a + q.spin * ease.inQuad(u) * 2, x = Math.cos(a) * d, y = CY + Math.sin(a) * d * 0.7;
        const d2 = d + 5 * u * u + 0.5, x2 = Math.cos(a - q.spin * 0.1) * d2, y2 = CY + Math.sin(a - q.spin * 0.1) * d2 * 0.7;
        p.line(x2, y2, x, y, (v) => (v < 0.5 ? mix(q.c, VOID, 0.4) : q.c));
      }
      // 足もとの炎: 輪になって王を囲み、 縮みながら小さくなる。 奥から手前の順に描く
      const fl = flames.map((f) => ({ f, x: Math.cos(f.a) * RR, y: Math.sin(f.a) * RR * SY })).sort((A, B) => A.y - B.y);
      if (o.flames) for (const { f, x, y } of fl) {
        if (y < 0 && Math.abs(x) < 46) continue;   // 体の後ろは描かない (効果は 1 枚で上に重なる)
        const flick = 0.65 + 0.6 * rand(Math.floor(t * 8), f.n), near = 0.75 + 0.5 * (y / (RR * SY) + 1) / 2, h = f.h * near * (1 - 0.8 * ease.inQuad(k)) * flick * (still ? 0.55 : 1), w = f.w * near * (1 - 0.6 * k);
        // 先が芯の方へ引かれる
        flameTongue(p, x, y, h, w, -Math.sign(x) * (1.5 + 6 * k) * (h / 20), N, f.n, H, w > 3.2 ? BIG_LAYERS : LAYERS, 0.5, 1.1);
      }
      if (!still) {
        // 芯: 潰されるほど熱く
        const cr = 1.5 + 5 * k * k;
        p.disc(0, CY, cr + 4, VOID, 0.8);
        p.circle(0, CY, cr + 1.6, k > 0.6 ? F[5] : F[3]);
        p.circle(0, CY, cr, k > 0.35 ? WHITE : F[7]);
        p.glow(0, CY, 14 + 40 * k, F[4], 0.25 + 0.5 * k);
      } else {
        // チャージ完了: ここからは待機 (gouka-charged) と同じ絵を描く。 完了の瞬間だけ、 十字が長く走り、 光が強い (0.3 秒で待機の姿に落ち着く)。
        // 待機の時刻は、 このチャージの終わりが待機の 0 秒になるように合わせる (つないだ時に、 脈も火の粉も途切れない)
        held(p, F, CY, HELD_SEED, t - meta.duration, 1 - clamp((t - SUCK) / 0.3));
      }
      ps.draw(p);
      lights.draw(p);
    },
  };
}
