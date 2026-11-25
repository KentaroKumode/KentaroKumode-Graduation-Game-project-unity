// DICE BOUND 用 (2026-11-24): impacts/heavy-landing の写し。 smear: false (既定) は元と 1 バイトも変わらない。
// smear: true では、土煙・輪の残存を詰め、床のひび・凹みを点間引きで滑らかに消す。
// The rocks, grit and dust are adapted from Sprite Fusion's open-source explosion
// (https://www.spritefusion.com/pixel-art-vfx/explosion), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Bits, P, B, Rng, fit, band, clamp, smooth, ease, rand, TAU, WHITE, mix, READY } from "../../core/index.js";
import { stone, makeCracks, drawCracks, dustRing, rock, rockShape, SOOT } from "../impacts/_lib.js";

export const meta = {
  name: "Heavy landing (DICE BOUND)",
  desc: "A character lands hard: the floor dents and cracks under their feet, two rings of dust roll out along the floor one after the other, and pebbles hop away.",
  tags: ["dicebound", "impact", "landing", "ground", "stomp", "dust", "one-shot"],
  size: [176, 80],
  origin: [88, 60],
  duration: 1.6,
  loop: false,
  fps: 30,
  bg: "ground",
  actor: 2,
  options: { palette: ["#1c1a24", "#2f2a3a", "#4a4258", "#6b6280", "#9088a4", "#bdb6cc", "#e6e1ee"], stone: "#4a3f69", smear: false, seed: 14 },
};

function dent(p, st, k, dotFade = false) {
  for (let x = -10; x <= 9; x++) {
    const q = Math.abs(x + 0.5) / 10, dep = Math.round(1.8 * (1 - q * q));
    if (dotFade) {
      const keep = 0.35 + 0.65 * (1 - q);
      if (rand(x + 31, 7, 13) > k * keep * 1.15) continue;
    }
    if (q < 0.82) {
      for (let y = 0; y < dep; y++) p.dot(x, y, SOOT, dotFade ? Math.max(0.35, k) : k);
      p.dot(x, dep, st.lip, dotFade ? Math.max(0.35, k) : k);
    } else p.dot(x, -1, q > 0.92 ? st.light : st.lip, dotFade ? Math.max(0.35, k) : k);
  }
}

function drawCracksDotFade(p, cracks, g, dark, lip, f) {
  if (f >= 1) return;
  for (let ci = 0; ci < cracks.length; ci++) {
    const c = cracks[ci], G = g(c);
    if (G <= 0) continue;
    for (const path of [c.pts, ...c.kids]) {
      for (let i = 1; i < path.length; i++) {
        const [x0, y0, d0] = path[i - 1], [x1, y1, d1] = path[i];
        if (d0 >= G) break;
        const frac = d1 > G ? (G - d0) / (d1 - d0) : 1, ex = x0 + (x1 - x0) * frac, ey = y0 + (y1 - y0) * frac;
        const steps = Math.max(1, Math.ceil(Math.hypot(ex - x0, ey - y0) * 1.2));
        for (let s = 0; s <= steps; s++) {
          const u = s / steps, x = x0 + (ex - x0) * u, y = y0 + (ey - y0) * u;
          const d = d0 + (d1 - d0) * frac * u;
          const h = rand(Math.round(x * 3) + ci * 17, Math.round(y * 3) + i * 7, 19);
          const keep = 0.25 + 0.75 * clamp(1 - d / Math.max(4, c.len));
          if (h < Math.pow(f, 0.85) / keep) continue;
          const a = Math.max(0.32, 1 - f * 0.6);
          p.dot(x, y, dark, a);
          if (f < 0.65 && rand(Math.round(x), Math.round(y), 5) < 0.28) p.dot(x, y + 1, lip, a * 0.75);
        }
      }
    }
  }
}

export function create(o) {
  const C = fit(o.palette, 7), st = stone(o.stone), R = new Rng(o.seed), floor = () => 0, sm = !!o.smear, D = sm ? 0.84 : meta.duration;
  const back = new Particles({ rng: R, ground: floor }), front = new Particles({ rng: R, ground: floor });
  const bits = new Bits({ ground: floor }), cracks = makeCracks(R, 6, { len: [8, 20], dive: 0.7, maxY: 12, forks: 0.4, x0: [2, 6] });
  const DUST = mix(st.dust, C[5], 0.4), LIT = mix(DUST, WHITE, 0.3), PH = R.range(0, TAU);
  const ring = (n, v0, v1, size, delay, life = [0.45, 0.8]) => {
    for (let i = 0; i < n; i++) {
      const g = (i / n) * TAU + R.range(-0.1, 0.1), v = R.range(v0, v1), far = Math.sin(g) < 0;
      (far ? back : front).spawn(P.DUST, Math.cos(g) * 4, Math.sin(g) * 0.7 - 1, Math.cos(g) * v, Math.sin(g) * v * 0.16 - R.range(2, 10), R.range(life[0], life[1]), R.chance(0.45) ? LIT : DUST, R.range(size[0], size[1]), delay + R.range(0, 0.03));
    }
  };
  if (sm) {
    ring(14, 150, 200, [2.0, 3.0], 0.01, [0.22, 0.38]);
    ring(10, 75, 115, [2.8, 4.4], 0.07, [0.26, 0.44]);
    for (let i = 0; i < 6; i++) {
      const s = R.sign(), up = i < 3;
      (i % 2 ? front : back).spawn(P.DUST, s * R.range(2, 9), -R.range(1, 4), s * R.range(10, 32) * (up ? 0.6 : 1), -(up ? R.range(22, 50) : R.range(4, 10)), R.range(0.26, 0.44), R.chance(0.5) ? LIT : DUST, R.range(2.6, 4.2), R.range(0.01, 0.06));
    }
  } else {
    ring(20, 140, 190, [2.2, 3.4], 0.01);
    ring(18, 70, 110, [3.4, 5.4], 0.12);
    for (let i = 0; i < 12; i++) {
      const s = R.sign(), up = i < 6;
      (i % 2 ? front : back).spawn(P.DUST, s * R.range(2, 10), -R.range(1, 4), s * R.range(10, 35) * (up ? 0.6 : 1), -(up ? R.range(25, 60) : R.range(4, 12)), R.range(0.55, 0.9), R.chance(0.5) ? LIT : DUST, R.range(3, 5), R.range(0.02, 0.1));
    }
    for (let i = 0; i < 12; i++) {
      const s = R.sign(), v = R.range(50, 130);
      bits.add({ kind: B.CHUNK, x: s * R.range(3, 8), y: -1, vx: s * v, vy: -R.range(140, 230), g: 1, drag: 0.5, bounce: 0.55, life: R.range(1, 1.45), a: R.range(0, TAU), va: s * R.range(8, 16), s: R.chance(0.3) ? 2 : 1, pts: rockShape(R) });
    }
    for (let i = 0; i < 24; i++) {
      const s = R.sign(), a = -Math.PI / 2 + s * R.range(0.3, 1.2), v = R.range(60, 170);
      front.spawn(P.GRIT, s * R.range(2, 7), -1, Math.cos(a) * v, Math.sin(a) * v, R.range(0.6, 1.1), R.pick([st.light, st.body, st.lip]), R.chance(0.3) ? 2 : 1);
    }
  }

  let t = 0;
  const ringSpec = sm ? [[0.01, 0.28, 54, 4.0, 0.72, 1], [0.07, 0.42, 38, 4.8, 0.65, 0]] : [[0.01, 0.45, 58, 4.5, 0.75, 1], [0.12, 0.85, 42, 6, 0.8, 0]];
  const rings = (p, far) => {
    for (const [t0, dur, r1, w, k0, edge] of ringSpec) {
      const g = (t - t0) / dur;
      if (g <= 0 || g >= 1) continue;
      const r = 6 + r1 * ease.outCubic(g);
      dustRing(p, 0, 0, r, w * (1 + g), k0 * Math.pow(1 - g, 1.2) * Math.min(1, g / 0.06), DUST, { sy: 0.2, ph: PH + t0 * 10, brk: g * 0.8, half: far ? -1 : 1 });
      if (edge && g < 0.75) band(p, 0, 0.5, r + 2, 0.2, Math.max(1, 2.5 * (1 - g)), 1.1 - g / 0.75, [st.body, st.light, LIT, WHITE], (a) => Math.sin(a) < 0 === far);
    }
  };
  return {
    update(dt) {
      t += dt;
      for (const b of bits.list) b.pvy = b.vy;
      bits.update(dt);
      for (const b of bits.list)
        if (b.pvy > 80 && b.vy <= 0) {
          front.spawn(P.DUST, b.x, -0.5, R.sign() * R.range(6, 20), -R.range(2, 10), R.range(0.25, 0.45), DUST, R.range(1, 1.8));
          b.pvy = 0;
        }
      back.update(dt);
      front.update(dt);
    },
    drawBack(p) {
      if (sm) {
        if (t > D) return;
        const f = clamp((t - 0.34) / (D - 0.34));
        if (f < 0.92) dent(p, st, 1 - f, f > 0);
        if (f > 0) drawCracksDotFade(p, cracks, (c) => c.len * ease.outCubic(clamp((t - c.t0) / 0.07)), SOOT, st.light, f);
        else drawCracks(p, cracks, (c) => c.len * ease.outCubic(clamp((t - c.t0) / 0.07)), () => 0, [WHITE, WHITE, WHITE, WHITE], SOOT, st.light, 1);
        rings(p, true);
        back.draw(p);
        return;
      }
      const fade = 1 - smooth(D - 0.5, D - 0.1, t);
      dent(p, st, fade);
      drawCracks(p, cracks, (c) => c.len * ease.outCubic(clamp((t - c.t0) / 0.08)), () => 0, [WHITE, WHITE, WHITE, WHITE], SOOT, st.light, fade);
      rings(p, true);
      back.draw(p);
    },
    draw(p) {
      if (sm && t > D) return;
      rings(p, false);
      front.draw(p);
      for (const b of bits.list) rock(p, b, st);
      if (sm) {
        if (t < 0.035) p.ellipse(0, 0, 14, 2.2, (d) => (d < 0.55 ? WHITE : C[6]));
        else if (t < 0.065) for (let x = -20; x <= 20; x++) p.dot(x, 0, Math.abs(x) < 9 ? WHITE : C[5], 1 - Math.abs(x) / 22);
        if (t < 0.09) p.glowLine(-18, 0, 18, 0, 5, C[6], 0.36 * (1 - t / 0.09));
        return;
      }
      if (t < 0.035) p.ellipse(0, 0, 15, 2.5, (d) => (d < 0.55 ? WHITE : C[6]));
      else if (t < 0.07) for (let x = -22; x <= 22; x++) p.dot(x, 0, Math.abs(x) < 10 ? WHITE : C[5], 1 - Math.abs(x) / 24);
      if (t < 0.12) p.glowLine(-20, 0, 20, 0, 8, C[6], 0.45 * (1 - t / 0.12));
    },
  };
}
