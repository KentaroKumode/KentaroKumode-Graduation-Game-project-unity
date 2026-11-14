// DICE BOUND 用 (2026-11-14): 灰燼の王の〈業火〉を放つ直前 (大技のターンの溜め)。 待機 (dicebound/gouka-charged) から続き、
// 芯が膨らみ、 十字が伸び、 脈が速くなり、 輪が芯へ落ち、 最後に画面全体が白く飛ぶ。 そのコマで着弾 (gouka-impact) と画面の火 (gouka-screen) へつなぐ。
// 画面全体 (480×270)・原点は王の足もとの中心。 作り: 速くなる脈と、 拍ごとに芯へ落ちる輪 = lightning/charge-up。
// The quickening pulse and the rings that fall onto the core on every beat are adapted from this repository's lightning/charge-up
// (after Sprite Fusion's open-source energy charge, https://www.spritefusion.com/pixel-art-vfx/energy), MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, stream, clamp, ease, rand, TAU, WHITE } from "../../core/index.js";
import { meta as CHARGE, cross, ball, REST } from "./gouka-charge.js";

export const meta = {
  name: "Gouka release (DICE BOUND)",
  desc: "The Ash King's gathered inferno breaks loose: the core swells, its cross of light stretches, the pulse quickens with rings falling onto it, and the whole screen whites out.",
  tags: ["fire", "dicebound", "boss", "charge", "flash", "one-shot"],
  size: [480, 270],
  origin: [240, 196],
  duration: 1.75,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, chest: 58, lead: 1.4, seed: 7 },
};

export function draw(p, t, o) {
  const F = fit(o.palette, 9), CY = -o.chest, LEAD = o.lead, k = clamp(t / LEAD), e = k * k, f = Math.floor(t * 8 + 1e-4);
  if (t < LEAD) {
    const jit = k > 0.4 ? (f % 2 ? 1 : -1) * (k > 0.8 ? 2 : 1) : 0;
    // 火の粉: 進むほど多く、 速く
    stream(t, 0, LEAD, 30 + 90 * k, o.seed * 3 + 1, (g) => g.range(0.3, 0.6), (age, life, R) => {
      const a = R.range(0, TAU), d0 = R.range(60, 200), q = age / life, d = d0 * (1 - ease.inCubic(q)) + 4, d2 = d + 10 * q + 2;
      p.line(Math.cos(a) * d2, CY + Math.sin(a) * d2 * 0.7, Math.cos(a) * d, CY + Math.sin(a) * d * 0.7, R.pick([F[5], F[6], F[7]]), Math.min(1, q * 4));
    });
    // 脈が速くなる: 位相 = ∫(1.5 + 9 k²) dt。 拍ごとに、 輪が芯へ落ちる
    const phase = (x) => 1.5 * x + (3 * x * x * x) / (LEAD * LEAD), now = phase(t);
    for (let n = Math.floor(now); n >= 1; n--) {
      let lo = 0, hi = t;
      for (let i = 0; i < 20; i++) { const m = (lo + hi) / 2; if (phase(m) < n) lo = m; else hi = m; }
      const q = (t - hi) / 0.25;
      if (q >= 1) break;
      p.ring(jit, CY, 6 + (26 + 60 * k) * (1 - ease.inQuad(q)), q > 0.6 ? 2 : 1, q > 0.6 ? F[7] : F[5], 0.4 + 0.6 * q);
    }
    const pr = 0.5 + 0.5 * Math.sin(now * TAU);
    ball(p, F, jit, CY, 3 + 8 * e + pr * (0.7 + 1.5 * k));
    cross(p, F, jit, CY, REST + 150 * e + 10 * k * rand(f, 3) + 7 * pr, k > 0.45 ? 0.25 + 0.3 * k : 0);
    p.glow(0, CY, 30 + 190 * e, F[5], 0.3 + 0.5 * k);
    p.glow(0, CY, 12 + 40 * e, WHITE, 0.4 + 0.4 * k);
    // 放つ直前: 画面全体が熱で明るむ
    const pre = clamp((t - (LEAD - 0.25)) / 0.25);
    if (pre > 0) for (let y = 0; y < 270; y++) for (let x = (y & 1); x < 480; x += 2) p.add(x, y, F[6], 0.5 * pre * pre);   // add は画面の左上からの座標
  } else {
    // 激しい発光: 画面全体が白く飛び、 0.25 秒で抜ける
    const a = Math.sqrt(1 - clamp((t - LEAD - 0.13) / 0.2));   // 0.13 秒は真っ白のまま (1 秒 8 コマでも、 必ず 1 コマは白い)
    for (let y = 0; y < 270; y++) for (let x = 0; x < 480; x++) { p.put(x, y, WHITE, a * a); p.add(x, y, WHITE, a * a); }   // 色として白を置く (光だけだと液晶の色の段で橙に落ちる)。 抜ける時は網点で薄くなる
    cross(p, F, 0, CY, 240 * a, 0.5);
  }
}
