// DICE BOUND 用 (2026-11-14): 灰燼の王の大技〈業火〉の着弾。 敵を正面から見る構図 ── 王の足もとで爆ぜ、 火が床を輪になって
// 手前 (画面の下) まで走り、 縁で燃え残って燻る。 烈炎 (o.ash = 積もった灰の数) が多いほど、 灰が噴き、 舞い、 煙が残る。
//
// 部品は pixelfx の炎の効果から借りて組んだ (どの部品をどこから借りたかは Tools/pixelfx/library/dicebound/gouka-impact.md):
//   床を走る火 = fire/fire-ring の sites (小さな炎の舌を床に並べ、 前線が来た順に点火して、 白 → 橙 → 赤の層へ冷ます)
//   投げ上げる火球・火の柱・煙の傘・決めの閃光と光条 = fire/meteor の着弾
//   火花・砂利・衝撃の輪・一瞬の光 = fire/flame-pillar の噴き上がり
// The floor-ring sites, flame puffs, smoke, sparks, flash, flare and shock ring are adapted from this repository's fire-ring,
// meteor and flame-pillar, whose recipes are adapted from Sprite Fusion's open-source fire and explosion
// (https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Bits, Lights, P, PF, B, Rng, fit, band, flare, clamp, smooth, ease, noise, rand, TAU, WHITE, hex, mix, rgb } from "../../core/index.js";
import { hotFirst, flameTongue, simDraw, floorLight, LAYERS, BACK_LAYERS, BIG_LAYERS } from "../fire/_lib.js";

export const meta = {
  name: "Gouka impact (DICE BOUND)",
  desc: "The Ash King's inferno lands: a white flash at his feet, fire races out across the floor in a ring towards the viewer, burns on at the rim and smoulders; the more ash has piled up, the more ash bursts, drifts and smokes.",
  tags: ["fire", "dicebound", "boss", "impact", "one-shot"],
  size: [440, 300],
  origin: [220, 200],
  duration: 2.75,
  loop: false,
  fps: 30,
  bg: "ground",
  options: {
    palette: ["#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff"],
    ashes: ["#2a2630", "#4b4650", "#77717a", "#a39c9f", "#cfc8c4"],
    ash: 3,          // 烈炎の数 (0 = 灰なし・火だけ)
    reach: 176,      // 火が床を走る半径 (横)。 縦は SY 倍に潰れる
    body: 52,        // 敵の体の半幅。 奥の火は、 体の後ろ (この幅の内側) には描かない
    seed: 7,
  },
};

const SY = 0.17, BURST = 0.05, SPREAD = 0.55, SOOT = hex("#0d0a12"), SOOT2 = hex("#17121d"), WARM = rgb(255, 176, 96);
// 消えかけの炎: 赤と朱だけ・白い芯なし (fire-ring の EMBER_LAYERS)
const EMBER_LAYERS = [[1, 1, 7], [0.7, 0.8, 6], [0.4, 0.55, 5]];

export function create(o) {
  const F = fit(o.palette, 9), H = hotFirst(F), A = fit(o.ashes, 5).map(hex), REACH = o.reach, ASH = Math.max(0, o.ash), R = new Rng(o.seed), floor = () => 0, S = o.seed * 7;
  const ps = new Particles({ rng: R, ground: floor, sparks: [H[1], H[2], H[4], H[5]] }), bits = new Bits({ ground: floor }), lights = new Lights();
  const GLOW = mix(mix(H[3], H[4], 0.5), WHITE, 0.25);
  const front = (t) => REACH * ease.outCubic(clamp((t - BURST) / SPREAD));
  const reachedAt = (d) => BURST + SPREAD * (1 - Math.cbrt(1 - Math.min(1, d / REACH)));
  // 奥行き: 床の楕円の上で、 奥 (z = −1) は小さく暗く、 手前 (z = +1) は大きく
  const depth = (y) => clamp(y / (REACH * SY), -1, 1), near = (z) => 0.85 + 0.2 * (z + 1);

  // 床の炎の置き場所: 輪を 11 px おきに。 前線が来た時に点火し、 中心は一瞬で消え、 縁は長く燃える
  const sites = [];
  for (let r = 22; r <= REACH; r += 13) {
    const per = TAU * Math.sqrt((r * r + (r * SY) ** 2) / 2), n = Math.max(6, Math.round(per / 12));
    for (let k = 0; k < n; k++) {
      const a = ((k + R.range(0.15, 0.85)) / n) * TAU, rr = r + R.range(-3.5, 3.5), q = Math.min(1, rr / REACH);
      const x = Math.cos(a) * rr, y = Math.sin(a) * rr * SY, z = depth(y), s = near(z);
      if (y < 0 && Math.abs(x) < o.body) continue;      // 敵の体の後ろ
      if (R.chance(0.45 * (1 - q))) continue;           // 中心に近いほど、 まばらに
      sites.push({ x, y, z, far: y < 0, t0: reachedAt(rr) + R.range(0, 0.04), life: (q > 0.88 ? R.range(0.75, 1.15) * (1 + 0.05 * ASH) : R.range(0.14, 0.22)),      // 走る間は一瞬だけ燃え、 いちばん外の輪だけが燃え残る (輪を薄く)
        h: R.range(10, 17) * (0.6 + 0.55 * q) * s, w: R.range(1.9, 2.8) * (0.75 + 0.3 * q) * s, n: R.int(0, 1e5) });
    }
  }
  sites.sort((a, b) => a.y - b.y);
  // 噴き上がる炎の壁: 王の足もとから立つ大きな舌。 体の左右が高く、 正面は低い (体を隠さない)。 0.12 秒で行き過ぎて立ち、 0.45 秒まで燃え、 0.95 秒で消える
  const walls = [[-82, 58, 9], [-62, 104, 15], [-44, 82, 12], [-26, 40, 8], [-9, 30, 7], [10, 34, 7], [27, 44, 8], [44, 88, 12], [62, 110, 15], [82, 54, 9]]
    .map(([x, h, w], i) => ({ x: x + R.range(-3, 3), h: h * R.range(0.9, 1.1), w, n: (S + i) * 31, d: R.range(0, 0.05) }));
  let t = 0, burst = false;
  const eruption = (p) => {
    const NZ = (rate, seed = 0, off = 0) => noise(t * rate + off, seed);
    for (const g of walls) {
      const a = t - BURST - g.d;
      if (a < 0 || a > 0.95) continue;
      const rise = ease.outBack(clamp(a / 0.12), 2.2), fall = 1 - Math.pow(clamp((a - 0.42) / 0.53), 1.5);
      const h = g.h * rise * fall * (0.9 + 0.12 * NZ(3.3, g.n)) * (1 + 0.35 * Math.max(0, NZ(2.6, g.n + 70) - 0.5));
      if (h < 2) continue;
      const L = a < 0.5 ? BIG_LAYERS : a < 0.72 ? BACK_LAYERS : EMBER_LAYERS;
      flameTongue(p, g.x, 0, h, g.w * (0.65 + 0.35 * fall), NZ(2.2, g.n + 40) * 3 + g.x * 0.06, NZ, g.n, H, L, 0.7, 1.3 + h * 0.012);
      if (a < 0.6) p.glow(g.x, -h * 0.45, 26 + h * 0.3, GLOW, 0.2 * fall);
    }
  };

  const flames = (p, far) => {
    const NZ = (rate, seed = 0, off = 0) => noise(t * rate + off, seed);
    for (const s of sites) {
      if (s.far !== far) continue;
      const age = t - s.t0;
      if (age < 0 || age > s.life) continue;
      const u = age / s.life, rise = ease.outBack(clamp(age / 0.08), 2), fade = 1 - Math.pow(Math.max(0, (u - 0.1) / 0.9), 1.3);
      const h = s.h * rise * fade * (0.85 + 0.15 * noise(t * 3.3, s.n)) * (1 + 0.5 * Math.max(0, noise(t * 2.6, s.n + 70) - 0.55));
      if (h < 1) continue;
      const w = s.w * (0.6 + 0.4 * fade);
      const L = u < 0.3 ? (far ? BACK_LAYERS : w > 2.2 ? BIG_LAYERS : LAYERS) : u < 0.65 ? BACK_LAYERS : EMBER_LAYERS;
      // 先端は外へ流れる (中心から吹かれている)
      flameTongue(p, s.x, s.y, h, w, NZ(2.2, s.n + 40) * 1.5 + s.x * 0.015 * h * 0.2, NZ, s.n, H, L);
    }
  };
  // 焦げ跡: 火が通った床に半透明の煤。 まばらな残り火が冷めていく。 灰が多いほど、 灰色の点が混ざり、 長く燻る
  const scorch = (p) => {
    const fr = front(t), end = 1 - smooth(meta.duration - 0.9, meta.duration - 0.1, t);
    if (fr < 1 || end <= 0) return;
    for (let y = Math.floor(-fr * SY); y <= Math.ceil(fr * SY); y++)
      for (let x = Math.floor(-fr); x <= Math.ceil(fr); x++) {
        if (y < 0 && Math.abs(x) < o.body) continue;
        const d = Math.hypot(x + 0.5, (y + 0.5) / SY);
        if (d > fr) continue;
        const since = t - reachedAt(d), h = rand(x, y, S), hot = 1 - since / (1.1 + h * 0.9 + 0.12 * ASH);
        if (hot > 0 && h < 0.004 + 0.016 * hot) {
          p.px(x, y, hot > 0.7 ? H[3] : hot > 0.45 ? H[4] : hot > 0.2 ? H[5] : H[6]);
          p.light(x, y, H[4], 0.3 * hot);
          continue;
        }
        p.blend(x, y, h < 0.5 ? SOOT : SOOT2, Math.min(1, since * 3) * (0.42 - (d / REACH) * 0.2) * end);
      }
  };
  const ring = (p, far) => {
    const k = (t - BURST) / (SPREAD + 0.1);
    if (k < 0 || k >= 1) return;
    band(p, 0, 0, front(t) + 2, SY, Math.max(1, 3 * (1 - k)), k < 0.75 ? 1 : (1 - k) / 0.25, [H[6], H[4], H[2], H[1]], (g) => (Math.sin(g) < 0) === far, k < 0.14);
  };
  // 灰の塊: 光の当たる面と影の面のある、 角ばった灰色の破片
  const crust = (p, b) => {
    const r = 0.7 + b.s * 0.8, fade = Math.min(1, b.life / 0.5), lx = Math.cos(b.a), ly = Math.sin(b.a);
    p.shade(b.x - r - 1, b.y - r - 1, b.x + r + 1, b.y + r + 1, (x, y, out) => {
      const dx = x - b.x, dy = y - b.y, d = Math.abs(dx * lx + dy * ly) + Math.abs(-dx * ly + dy * lx) * 1.3;
      if (d > r + 0.9) return;
      out.c = d > r ? A[0] : dx * 0.6 + dy * 0.8 < -0.3 ? A[4] : dx * 0.6 + dy * 0.8 > 0.5 ? A[1] : A[3];
      out.a = fade;
    });
  };

  return {
    update(dt) {
      t += dt;
      if (!burst && t >= BURST) {
        burst = true;
        lights.add(0, -34, 130, WARM, 0.95, 0.85);
        lights.add(0, -10, 56, WHITE, 0.9, 0.12);
        // 投げ上げる火球: 王の足もとの幅いっぱいから、 上と外へ (meteor の fireball)
        for (let i = 0; i < 30; i++) {
          const a = -Math.PI / 2 + R.range(-1.25, 1.25), sp = R.range(90, 270);
          ps.spawn(P.FIRE, R.range(-52, 52), -R.range(2, 14), Math.cos(a) * sp, Math.sin(a) * sp * 0.8 - 40, R.range(0.5, 1.1), 0, R.range(8, 15), 0, R.chance(0.5) ? PF.SMOKES | PF.BIG : 0);
        }
        // 立ちのぼる火: 左右の肩の外へ 2 本の柱 (体の正面を埋めない)
        for (let i = 0; i < 16; i++) {
          const s = i % 2 ? 1 : -1;
          ps.spawn(P.FIRE, s * R.range(44, 70), -6, s * R.range(2, 22), -R.range(170, 300), R.range(0.75, 1.2), 0, R.range(8, 12.5), R.range(0, 0.14), PF.SMOKES | PF.BIG);
        }
        for (let i = 0; i < 80; i++) {
          const a = R.range(0, TAU), sp = R.range(90, 280);
          ps.spawn(P.SPARK, Math.cos(a) * 10, -3, Math.cos(a) * sp, Math.sin(a) * sp * 0.45 - R.range(40, 150), R.range(0.2, 0.65));
        }
        for (let i = 0; i < 36; i++) {
          const a = R.range(0, TAU), sp = R.range(40, 150);
          ps.spawn(P.EMBER, Math.cos(a) * 14, -8 + Math.sin(a) * 6, Math.cos(a) * sp, Math.sin(a) * sp * 0.5 - 70, R.range(1.1, 2.4), H[3], R.chance(0.2) ? 2 : 1);
        }
        // 灰 (烈炎): 噴き上がる灰の粒・灰色の煙・灰の塊
        for (let i = 0; i < ASH * 26; i++) {
          const a = R.range(0, TAU), sp = R.range(50, 230);
          ps.spawn(P.FLAKE, Math.cos(a) * R.range(4, 40), -R.range(2, 30), Math.cos(a) * sp, Math.sin(a) * sp * 0.45 - R.range(60, 190), R.range(1.2, 2.5), R.pick([A[2], A[3], A[3], A[4]]), R.chance(0.3) ? 1.6 : 1, R.range(0, 0.1));
        }
        for (let i = 0; i < ASH * 4; i++) {
          const s = R.sign();
          ps.spawn(P.SMOKE, s * R.range(8, 70), -R.range(2, 40), s * R.range(30, 120), -R.range(30, 120), R.range(1.5, 2.4), A[2], R.range(10, 16), R.range(0.1, 0.45), PF.BIG);
        }
        for (let i = 0; i < ASH * 3; i++)
          bits.add({ kind: B.CHUNK, x: R.range(-40, 40), y: -4, vx: R.range(-150, 150), vy: -R.range(150, 330), g: 1, drag: 0.4, bounce: 0.3, life: R.range(1.4, 2.2), a: R.range(0, TAU), va: R.range(-14, 14), s: R.int(1, 3) });
      }
      // 燃えている床の炎から、 火がちぎれて上る。 一部は煙へ。 灰が多いほど、 煙が灰色で大きい
      if (t > BURST && t < BURST + SPREAD + 1.1)
        for (let i = 0; i < 5; i++) {
          if (!R.chance(0.8 * dt * 60)) continue;
          const s = R.pick(sites), age = t - s.t0;
          if (age < 0 || age > s.life * 0.7) continue;
          const k = near(s.z);
          ps.spawn(P.FIRE, s.x + R.range(-1, 1), s.y - s.h * 0.6, R.range(-5, 5) + s.x * 0.04, -R.range(20, 42), R.range(0.2, 0.42), 0, R.range(1.3, 2.6) * k, 0, (R.chance(0.5) ? PF.SMOKES | (ASH >= 5 ? PF.BIG : 0) : 0) | (s.far ? PF.BACK : 0));
          if (R.chance(0.14)) ps.spawn(P.EMBER, s.x, s.y - s.h * 0.5, R.range(-15, 15), -R.range(30, 70), R.range(0.6, 1.3), H[3], 1, 0, s.far ? PF.BACK : 0);
          if (ASH > 0 && R.chance(0.05 * ASH)) ps.spawn(P.FLAKE, s.x, s.y - s.h * 0.4, R.range(-12, 12), -R.range(20, 50), R.range(0.9, 1.8), R.pick([A[2], A[3]]), 1, 0, s.far ? PF.BACK : 0);
        }
      bits.update(dt);
      ps.update(dt);
      lights.update(dt);
    },
    drawBack(p) {
      scorch(p);
      ring(p, true);
      flames(p, true);
      simDraw(p, ps, H, "back");
      // 前線と一緒に床を走る光
      const fr = front(t), k = t < BURST ? 0 : 1 - smooth(BURST + SPREAD, BURST + SPREAD + 1.3, t);
      if (k > 0) for (let i = 0; i < 20; i++) p.glow(Math.cos((i / 20) * TAU) * fr, Math.sin((i / 20) * TAU) * fr * SY - 3, 20, GLOW, 0.18 * k);
    },
    draw(p) {
      ring(p, false);
      eruption(p);
      flames(p, false);
      for (const b of bits.list) crust(p, b);
      simDraw(p, ps, H, "front");
      // 決めのコマ: 足もとの白い閃光と、 8 方向の光条
      const f = (t - BURST) / 0.1;
      if (f > 0 && f < 1) {
        p.ellipse(0, -2, 56 * (0.7 + 0.3 * (1 - f)), 9, H[2]);
        p.ellipse(0, -2, 44 * (0.7 + 0.3 * (1 - f)), 6, f < 0.5 ? WHITE : H[1]);
      }
      const fl = (t - BURST) / 0.26;
      if (fl > 0 && fl < 1) flare(p, 0, -8, 70 * Math.pow(1 - fl, 1.4), 1 - fl, [H[6], H[4], H[2], H[1]]);
      const k = smooth(BURST - 0.04, BURST, t) * (1 - smooth(BURST + SPREAD * 0.5, BURST + SPREAD + 1.1, t));
      if (k > 0) {
        p.glow(0, -10, 150, GLOW, 0.3 * k);
        floorLight(p, 0, 0, REACH, REACH * SY, GLOW, 0.22 * k);
      }
      lights.draw(p);
    },
  };
}
