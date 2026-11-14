// DICE BOUND 用 (2026-11-14): 霊・呪いの敵の攻撃 (怨嗟の影・13 番目の死・鏡の双子)。 arcane/arcane-missiles の写し。
// (作者: Arcane missiles を土台に、 敵から四方へミサイルが出る → 大きくなりながら外へ向かい → 最後は全部、 こちら (画面の下) へ誘導されて着弾)
// 画面全体 (480×270) の座標で流す。 変えた所: 出どころ (敵の体の中心) と着く所 (こちらの盤)・出る向き (四方)・飛ぶほど大きくなる (b.s)・曲がり方 (初めは外へ流れる)・
//   着弾と的の印の大きさ (Z 倍)。 矢・尾・着弾の描き方は元の式のまま。
// The bolt and its pop (a layered head with spinning rays, a flash disc, a cross, a ring, plasma sparks and stars)
// are adapted from Sprite Fusion's open-source energy bolt (https://www.spritefusion.com/pixel-art-vfx/energy),
// MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, PAL, fit, sparkle, clamp, smooth, ease, TAU, WHITE } from "../../core/index.js";

const HX = 240, HY = 128, TX = 240, TY = 230, TRAIL = 26, Z = 2.2;   // 出どころ・着く所・尾の長さ・着弾の大きさ

export const meta = {
  name: "Foe missiles (DICE BOUND)",
  desc: "Bolts burst out of the enemy in every direction, swell as they fly outward, then all curl round and home in on the viewer's board at the bottom of the screen.",
  tags: ["dicebound", "enemy", "missile", "one-shot"],
  size: [480, 270],
  origin: [0, 0],
  duration: 1.9,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: PAL.arcane, seed: 3 },
};

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed);
  const ps = new Particles({ rng: R, ground: () => 268, sparks: [WHITE, C[6], C[5], C[4]] }), lights = new Lights();
  // each bolt leaves the hand on its own heading, then homes in harder and faster until it lands
  const fan = [-2.36, -0.79, 2.6, 0.54, Math.PI, 0];   // 四方へ: 左上・右上・左下・右下・左・右
  const bolts = fan.map((a, i) => {
    a += R.range(-0.12, 0.12);
    const sp = R.range(300, 360);
    return { t0: 0.05 + i * 0.05, s: 1, x: HX, y: HY, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, trail: [], hit: -1, on: false, tx: TX + (i - 2.5) * 34 + R.range(-8, 8), ty: TY + R.range(-6, 6), tw: R.range(0, TAU), acc: 0 };
  });
  const pops = [];
  let t = 0;

  function ribbon(p, b) {
    const pts = b.trail, n = pts.length, fade = b.hit < 0 ? 1 : 1 - clamp((t - b.hit) / 0.16);
    if (n < 2 || fade <= 0) return;
    for (let i = 0; i < n - 1; i++) {
      const [x0, y0] = pts[i], [x1, y1] = pts[i + 1], L = Math.hypot(x1 - x0, y1 - y0) || 1, nx = -(y1 - y0) / L, ny = (x1 - x0) / L;
      for (let d = 0; d < L; d += 0.5) {
        const u = (i + d / L) / (n - 1), x = x0 + ((x1 - x0) * d) / L, y = y0 + ((y1 - y0) * d) / L, a = (1 - u) * 1.7 * fade;
        // the body: a capsule that tapers down the tail, white along its middle near the head, cooling outward
        const w = b.s * (2.6 * Math.pow(1 - u, 0.8) + 0.3);
        for (let q = -w; q <= w; q += 0.5) {
          const v = Math.abs(q) / w;
          p.dot(x + nx * q, y + ny * q, v < 0.35 ? (u < 0.25 ? WHITE : C[6]) : v < 0.7 ? (u < 0.4 ? C[6] : C[5]) : u < 0.5 ? C[5] : C[4], a * (v < 0.7 ? 1 : 0.75));
        }
        // two strands twisting round it: bright where they come round the front, dim behind
        for (const s of [0, Math.PI]) {
          const ph = u * 10 - t * 26 + b.tw + s, off = Math.sin(ph) * (w + 1.4);
          if (Math.cos(ph) > -0.2) p.dot(x + nx * off, y + ny * off, Math.cos(ph) > 0.4 ? (u < 0.35 ? WHITE : C[6]) : C[4], a);
        }
      }
      if (i % 3 === 0) p.glow(x0, y0, (9 * (1 - i / n) + 3) * b.s, C[5], 0.3 * (1 - i / n) * fade);
    }
  }

  function head(p, b) {
    const x = b.x, y = b.y, s = b.s;   // s = 飛ぶほど大きくなる
    for (let i = 0; i < 4; i++) {
      const a = t * 24 + (i * TAU) / 4 + b.tw, l = (5.5 + 1.5 * Math.sin(t * 50 + i)) * s;
      p.line(x + Math.cos(a) * 3.2 * s, y + Math.sin(a) * 3.2 * s, x + Math.cos(a) * l, y + Math.sin(a) * l, C[6]);
    }
    p.circle(x, y, 3.8 * s, C[4]);
    p.circle(x, y, 3 * s, C[5]);
    p.circle(x, y, 2.1 * s, C[6]);
    p.circle(x, y, 1.2 * s, WHITE);
    p.glow(x, y, 24 * s, C[5], 0.6);
    p.glow(x, y, 7 * s, WHITE, 0.4);
  }

  function pop(p, q, x, y) {
    if (q < 0.16) {
      const r = (9 * (1 - q / 0.16) + 1.5) * Z;
      p.circle(x, y, r + 1.5, C[5]);
      p.circle(x, y, r, WHITE);
    }
    if (q < 0.4) {
      const l = (15 * (1 - q / 0.4) + 3) * Z;
      for (let d = -l; d <= l; d++) {
        const a = Math.abs(d) / l, c = a < 0.3 ? WHITE : a < 0.65 ? C[6] : C[5];
        p.dot(x + d, y, c, 1.15 - a);
        p.dot(x, y + d * 0.7, c, 1.15 - a);
      }
    }
    const rr = (3 + 18 * ease.outCubic(q)) * Z;
    p.ring(x, y, rr + 0.5, Math.max(1, 3 * (1 - q)), q < 0.3 ? WHITE : C[6], 1.2 - q);
  }

  // the lock: four brackets close on the target and pulse as each bolt lands
  function lock(p) {
    const k = smooth(0, 0.22, t) * (1 - smooth(1.15, 1.45, t));
    if (k <= 0) return;
    let kick = 0;
    for (const q of pops) kick = Math.max(kick, Math.exp(-(((t - q.t) / 0.06) ** 2)));
    const r = (9 + 10 * (1 - ease.outCubic(smooth(0, 0.22, t))) + 3 * kick) * Z * 1.6, spin = t * 1.5;
    for (let i = 0; i < 4; i++) {
      const a0 = spin + (i * TAU) / 4 - 0.38;
      for (let a = a0; a <= a0 + 0.76; a += 0.015) p.dot(TX + Math.cos(a) * r, TY + Math.sin(a) * r, kick > 0.4 ? WHITE : C[5], k);
    }
    p.glow(TX, TY, 12 * Z * 2, C[4], 0.2 * k);
  }

  return {
    update(dt) {
      t += dt;
      for (const b of bolts) {
        if (t < b.t0 || b.hit >= 0) continue;
        if (!b.on) {
          b.on = true;
          lights.add(HX, HY, 60, C[5], 0.7, 0.14);
          for (let i = 0; i < 4; i++) ps.spawn(P.MOTE, HX, HY, Math.cos(Math.atan2(b.vy, b.vx) + R.range(-0.6, 0.6)) * R.range(60, 140), Math.sin(Math.atan2(b.vy, b.vx) + R.range(-0.6, 0.6)) * R.range(60, 140), R.range(0.12, 0.25), C[6], 0.6);
        }
        const age = t - b.t0; b.s = 1 + 1.8 * clamp(age / 0.8); const dx = b.tx - b.x, dy = b.ty - b.y, d = Math.hypot(dx, dy) || 1;
        const sp = 260 + 1100 * age * age, steer = Math.min(1, (0.04 + 24 * Math.max(0, age - 0.16) ** 2) * dt);   // 初めは外へ流れ、 だんだん強くこちらへ曲がる
        b.vx += ((dx / d) * sp - b.vx) * steer;
        b.vy += ((dy / d) * sp - b.vy) * steer;
        b.x += b.vx * dt;
        b.y += b.vy * dt;
        b.trail.unshift([b.x, b.y]);
        if (b.trail.length > TRAIL) b.trail.pop();
        if ((b.acc += dt) > 0.012) {
          b.acc = 0;
          if (R.chance(0.6)) ps.spawn(P.MOTE, b.x, b.y, -b.vx * 0.08 + R.range(-25, 25), -b.vy * 0.08 + R.range(-25, 25), R.range(0.2, 0.4), R.chance(0.5) ? C[6] : C[5], 0.55);
        }
        if (d < 9 || age > 1.5) {
          b.hit = t;
          pops.push({ t, x: b.x, y: b.y });
          lights.add(b.x, b.y, 150, C[5], 0.9, 0.3);
          lights.add(b.x, b.y, 50, WHITE, 0.85, 0.09);
          for (let i = 0; i < 16; i++) {
            const a = R.range(0, TAU), v = R.range(160, 460);
            ps.spawn(P.SPARK, b.x, b.y, Math.cos(a) * v, Math.sin(a) * v - 40, R.range(0.15, 0.45));
          }
          for (let i = 0; i < 7; i++) {
            const a = R.range(-Math.PI, 0.3), v = R.range(80, 280);
            ps.spawn(i % 2 ? P.STAR : P.MOTE, b.x, b.y, Math.cos(a) * v, Math.sin(a) * v, R.range(0.25, 0.48), R.pick([WHITE, C[6], C[5]]), R.range(0.8, 1.6));
          }
        }
      }
      ps.update(dt);
      lights.update(dt);
    },
    drawBack(p) {
      // the floor catches the light of each pop
      for (const q of pops) {
        const k = 1 - clamp((t - q.t) / 0.5);
        if (k > 0) p.glow(q.x, q.y + 6, 50, C[5], 0.25 * k * k);
      }
    },
    draw(p) {
      lock(p);
      for (const b of bolts) if (b.on) ribbon(p, b);
      for (const b of bolts) if (b.on && b.hit < 0) head(p, b);
      for (const q of pops) {
        const k = (t - q.t) / 0.35;
        if (k < 1) pop(p, k, q.x, q.y);
      }
      if (t < 0.55) sparkle(p, HX, HY, (3 + 2 * Math.sin(t * 40)) * Z, C[6], C[5], 0.45 * (1 - t / 0.55));
      ps.draw(p);
      lights.draw(p);
    },
  };
}
