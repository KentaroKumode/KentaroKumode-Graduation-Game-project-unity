// DICE BOUND 用 (2026-11-14): ヴェスカの天与の剣。 holy/holy-pillar を、 **大きく派手にした**写し (作者: いっそ Holy pillar を派手にした感じで。 上で回っている物はいらない)。
// 天から、 金の光の扇がこちらの盤へ集まり、 脈を打ち、 盤の上に広い印が焼け、 最後に扇が閉じる。
// 変えた所: 上の日輪を外した・高さ 204 → 252 (画面いっぱい)・扇の開きを 0.4 → 0.66 rad・光の筋 7 → 11 本で太く・印の半径 26 → 66・粒と羽を増やした・
//   着いた瞬間に、 横に長い閃光と床を走る輪を足した。 元の 3.5 秒を speed 倍 (既定 1.75) で流して、 約 2 秒。
// 大きくする方法は dicebound/vesca-hex.js と同じ: 拡大して貼るのではなく、 形を決めている数を掛け直す。
import { Particles, Lights, P, Rng, fit, sparkle, clamp, smooth, seg, lerp, ease, rand, TAU, WHITE } from "../../core/index.js";
import { lens, oval as ring } from "./_kit.js";
import { GOLD, Feathers, oval } from "../holy/_lib.js";

export const meta = {
  name: "Tenyo sword (DICE BOUND)",
  desc: "Vesca's divine sword: a great fan of golden light breaks from above and converges on the viewer's board, pulsing, a wide sigil burning on the floor under it; then the fan folds shut on the mark.",
  tags: ["dicebound", "boss", "relic", "holy", "screen", "one-shot"],
  size: [480, 270],
  origin: [240, 250],   // 光が集まる点 = こちらの盤
  duration: 2.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: GOLD, shafts: 11, speed: 1.75, seed: 3 },   // speed = 元の効果 (3.5 秒) を何倍の速さで流すか
};

const TOP = -252, PY = -1, SUN = TOP + 38, SY = 0.24, RX = 66, HALF = 240;
// the sun opens, the fan unfolds and pours down, it holds and pulses, folds shut, the light lifts away
const OPEN = 0.3, HIT = 0.66, FOLD = 2.05, SHUT = 2.45, LIFT = 2.75;
const frac = (x) => x - Math.floor(x);

export function create(o) {
  const inner = createRaw(o), sp = o.speed ?? 1, C = fit(o.palette, 8);
  let tt = 0;
  return {
    update: (dt) => { tt += dt * sp; inner.update(dt * sp); },
    drawBack: (p) => inner.drawBack(p),
    draw(p) {
      inner.draw(p);
      // 光が盤に着いた瞬間 (元の時刻で 0.66 秒): 横に長い閃光と、 床を走る輪 3 本 (元の効果には無い ── 派手にするために足した)
      const a = tt - HIT;
      if (a > 0 && a < 0.5) lens(p, C, 0, -4, 200 * (1 - a / 0.5), 22 * (1 - a / 0.5), 1);
      for (const [d, k] of [[0, 1], [0.12, 0.72], [0.24, 0.46]]) { const u = (a - d) / 0.8; if (u > 0 && u < 1) { const e = ease.outCubic(u); ring(p, 0, 0, 30 + 210 * e * k, 6 + 30 * e * k, u < 0.3 ? 4 : 2, u < 0.25 ? WHITE : u < 0.6 ? C[6] : C[5], 1.1 - u); } }
    },
  };
}

function createRaw(o) {
  const C = fit(o.palette, 8), N = Math.max(3, o.shafts | 0), R = new Rng(o.seed), mid = (N - 1) / 2;
  const WING = [C[2], C[3], C[4], C[5], C[6], WHITE];
  const ps = new Particles({ rng: R, ground: () => 0, sparks: [WHITE, C[6], C[5], C[3]] });
  const lights = new Lights(), feathers = new Feathers(R, 0);
  // the shafts: spread angle, how fast each widens, how bright, its pulse offset
  const shafts = Array.from({ length: N }, (_, i) => {
    const off = i - mid, far = Math.abs(off) / mid;
    return { th: off * (0.66 / mid) + R.range(-0.02, 0.02), spread: R.range(0.07, 0.125) * (1 - far * 0.25), b: (1 - far * 0.35) * R.range(0.8, 1), ph: R.range(0, 1), far };
  });
  const motes = Array.from({ length: 170 }, () => ({ x: R.range(-210, 210), y: R.range(0, 1), v: R.range(7, 18), amp: R.range(0.5, 2.5), f: R.range(0.6, 1.5), ph: R.range(0, TAU) }));
  for (let i = 0; i < 16; i++)
    feathers.add({ x: R.range(-160, 160), y: R.range(-230, -90), vx: R.range(-8, 8), vy: R.range(0, 8), a: R.range(-0.4, 0.4), len: R.range(7, 9), wid: R.range(1.9, 2.3), life: R.range(1.4, 1.7), sink: R.range(26, 36), back: i % 3 === 0, delay: HIT + 0.1 + i * 0.16 + R.range(0, 0.08) });
  const DMAX = PY - TOP, row = new Float32Array(HALF * 2), core = new Uint8Array(HALF * 2);
  let t = 0, hit = false, shut = false, glinted = false;

  // each shaft's angle now: unfolding from the middle with an overshoot, folding back shut at the end
  const angle = (s, tt) => {
    const st = s.far * 0.1, open = ease.outBack(seg(tt, OPEN + st, OPEN + st + 0.42), 1.5), fold = ease.inOutCubic(seg(tt, FOLD - s.far * 0.1, SHUT));
    return s.th * open * (1 - fold);
  };
  // how far down each shaft the light reaches (it pours from the sky), and where it lifts away at the end
  const frontD = (s, tt) => DMAX * (1 - ease.inQuad(seg(tt, OPEN + s.far * 0.06, HIT + s.far * 0.06)));
  const liftD = (tt) => DMAX * ease.inQuad(seg(tt, SHUT + 0.05, LIFT));
  const fade = (tt) => smooth(OPEN, OPEN + 0.1, tt) * (1 - smooth(LIFT - 0.05, LIFT + 0.05, tt));
  const width = (s, d, tt) => (4 + d * s.spread) * lerp(1, 0.25, smooth(SHUT, LIFT, tt));
  // brightness along a shaft: a pulse of light runs down each one, one after another
  const pulse = (s, d, tt) => {
    if (tt < HIT || tt > SHUT) return 1;
    const D = DMAX * (1 - frac((tt - HIT) / 0.6 + s.ph));
    return 1 + 0.9 * Math.exp(-(((d - D) / 12) ** 2));
  };
  // light of all the shafts at a point (for motes)
  const lightAt = (x, y) => {
    const dy = PY - y;
    if (dy <= 0) return 0;
    let I = 0;
    for (const s of shafts) {
      const a = angle(s, t), d = dy / Math.cos(a), xc = Math.tan(a) * dy, hw = width(s, d, t) / 2 / Math.cos(a);
      if (d < liftD(t) || d < frontD(s, t)) continue;
      const v = (x - xc) / hw;
      if (Math.abs(v) < 1) I += (1 - v * v) * s.b;
    }
    return I * fade(t);
  };

  function fan(p) {
    const k = fade(t);
    if (k <= 0) return;
    const lift = liftD(t);
    for (let y = Math.ceil(TOP); y < PY; y++) {
      const dy = PY - (y + 0.5);
      row.fill(0);
      core.fill(0);
      let any = false;
      for (const s of shafts) {
        const a = angle(s, t), ca = Math.cos(a), d = dy / ca;
        if (d < lift || d < frontD(s, t) - 6) continue;
        const xc = Math.tan(a) * dy, hw = width(s, d, t) / 2 / ca, soft = smooth(frontD(s, t) - 6, frontD(s, t) + 4, d) * smooth(lift, lift + 6, d);
        const I0 = s.b * pulse(s, d, t) * soft * k;
        for (let x = Math.floor(xc - hw); x <= Math.ceil(xc + hw); x++) {
          const v = (x + 0.5 - xc) / hw, i = x + HALF;
          if (i < 0 || i >= HALF * 2 || Math.abs(v) >= 1) continue;
          row[i] += I0 * Math.pow(1 - v * v, 1.3);
          any = true;
        }
        const ci = Math.round(xc - 0.5) + HALF;
        if (ci >= 0 && ci < HALF * 2) core[ci] = Math.max(core[ci], Math.min(255, I0 * 200));
      }
      if (!any) continue;
      for (let i = 0; i < HALF * 2; i++) {
        const I = row[i];
        if (I < 0.02) continue;
        const x = i - HALF;
        p.blend(x, y, C[6], Math.min(0.6, I * 0.38));
        p.light(x, y, C[4], Math.min(0.9, I * 0.32));
        // a fine bright line runs down the middle of each shaft
        if (core[i]) { p.dot(x, y, core[i] > 150 ? WHITE : C[6], Math.min(1, core[i] / 180)); p.dot(x + 1, y, C[6], Math.min(1, core[i] / 220)); }
      }
    }
    // where the shafts converge, the light gathers to a point
    if (t > HIT - 0.05) p.glow(0, PY - 3, 60, C[5], 0.5 * k);
  }

  // the sun: a halo that opens at the top like an iris, rays ticking round it, then closes with a glint
  function sun(p) {
    const r = 15 * ease.outBack(seg(t, 0.02, 0.45), 1.8) * (1 - ease.inBack(seg(t, SHUT, LIFT + 0.1), 1.5));
    if (r > 0.5) {
      const spin = t * 0.6;
      p.glow(0, SUN, Math.min(32, r * 3), C[4], 0.5);
      p.glow(0, SUN, r * 1.4, WHITE, 0.35);
      p.circle(0, SUN, r * 0.45, WHITE);
      oval(p, 0, SUN, r * 0.62, r * 0.62, C[6], 1);
      oval(p, 0, SUN, r, r, (g) => (Math.sin(g * 6 + spin * 4) > 0.5 ? WHITE : C[5]), 1);
      for (let j = 0; j < 16; j++) {
        const g = spin + (j * TAU) / 16, l = j % 2 ? 3 : 6;
        p.line(Math.cos(g) * (r + 2), SUN + Math.sin(g) * (r + 2), Math.cos(g) * (r + 2 + l), SUN + Math.sin(g) * (r + 2 + l), (u) => (u < 0.4 ? C[6] : C[4]), (u) => 1.1 - u);
      }
    }
    if (t > LIFT && t < LIFT + 0.3) sparkle(p, 0, SUN, 10 * (1 - (t - LIFT) / 0.3), WHITE, C[5], 0.6);
  }

  // the sigil burning on the floor: two rings and a turning band of marks; white-hot, then gold, then cooling
  function sigil(p, back) {
    if (t < HIT) return;
    const u = seg(t, HIT, HIT + 0.2), heat = 1 - smooth(SHUT, SHUT + 0.8, t), out = 1 - smooth(SHUT + 0.4, SHUT + 1, t);
    if (out <= 0) return;
    const keep = back ? (g) => Math.sin(g) < 0 : (g) => Math.sin(g) >= 0;
    // it burns in round from the front, both ways
    const burnt = (g) => Math.abs(((g - Math.PI / 2 + Math.PI) % TAU + TAU) % TAU - Math.PI) <= u * Math.PI + 0.01;
    const col = (g, hot) => {
      if (!burnt(g)) return 0;
      const flick = rand(Math.round(g * 40), Math.floor(t * 20)) < 0.18;
      return t < HIT + 0.08 || (flick && heat > 0.4) ? WHITE : heat > 0.6 ? (hot ? C[6] : C[5]) : heat > 0.3 ? C[4] : heat > 0.1 ? C[3] : C[2];
    };
    oval(p, 0, 0, RX, RX * SY, (g) => col(g, true), out, (g) => keep(g) && burnt(g));
    oval(p, 0, 0, RX * 0.72, RX * 0.72 * SY, (g) => col(g, false), out, (g) => keep(g) && burnt(g));
    // marks between the rings, turning slowly
    for (let j = 0; j < 12; j++) {
      const g = t * 0.35 + (j * TAU) / 12;
      if (!keep(g) || !burnt(g)) continue;
      const c = col(g, j % 2 === 0), r0 = RX * 0.78, r1 = RX * (j % 3 === 0 ? 0.95 : 0.88);
      if (c) p.line(Math.cos(g) * r0, Math.sin(g) * r0 * SY, Math.cos(g) * r1, Math.sin(g) * r1 * SY, c, out);
    }
    if (!back) {
      for (let i = -2; i <= 2; i++) p.glow(i * RX * 0.45, -2, 40, C[4], (0.34 - Math.abs(i) * 0.05) * out * (0.5 + 0.5 * heat));
      p.glow(0, -1, 30, WHITE, 0.3 * heat * out);
    }
  }

  return {
    update(dt) {
      t += dt;
      if (!hit && t >= HIT) {
        hit = true;
        lights.add(0, -40, 170, C[5], 0.85, 0.5);
        lights.add(0, -8, 90, WHITE, 0.8, 0.14);
        for (let i = 0; i < 12; i++) ps.spawn(P.STAR, R.range(-160, 160), -R.range(10, 200), 0, R.range(2, 10), R.range(0.4, 0.9), WHITE, R.range(1.5, 3.2), R.range(0, 0.4));
        for (let i = 0; i < 110; i++) {
          const a = -Math.PI / 2 + R.range(-1.35, 1.35), sp = R.range(100, 460);
          ps.spawn(P.SPARK, R.range(-3, 3), -2, Math.cos(a) * sp, Math.sin(a) * sp, R.range(0.25, 0.6));
        }
      }
      // while it holds: embers off the burning sigil, a few feathers drifting down through the shafts
      if (t > HIT && t < SHUT + 0.1 && R.chance(0.8)) {
        const g = R.range(0, TAU), r = RX * (R.chance(0.5) ? 1 : 0.72);
        ps.spawn(P.EMBER, Math.cos(g) * r, Math.sin(g) * r * SY - 1, R.range(-6, 6), -R.range(12, 30), R.range(0.5, 0.8), C[5], 1, 0, Math.sin(g) < 0 ? 4 : 0);
      }
      if (t > HIT && t < FOLD && R.chance(0.3)) ps.spawn(P.STAR, R.range(-1, 1) * 170, -R.range(20, 230), 0, R.range(2, 8), R.range(0.3, 0.6), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.8));
      // the fan snaps shut on the mark: a flash where the shafts become one
      if (!shut && t >= SHUT) {
        shut = true;
        lights.add(0, -80, 160, C[5], 0.7, 0.35);
        for (let i = 0; i < 30; i++) ps.spawn(P.STAR, R.range(-6, 6), -R.range(10, 240), R.range(-25, 25), R.range(-10, 10), R.range(0.4, 0.8), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 2));
      }
      ps.update(dt);
      feathers.update(dt);
      lights.update(dt);
    },
    drawBack(p) {
      fan(p);
      sigil(p, true);
      feathers.draw(p, WING, "back");
      ps.draw(p, "back");
    },
    draw(p) {
      sigil(p, false);
      // dust motes drifting down, lit only where a shaft catches them
      for (const m of motes) {
        const y = TOP + ((((m.y * DMAX + t * m.v) % DMAX) + DMAX) % DMAX), x = m.x + Math.sin(t * m.f + m.ph) * m.amp, I = lightAt(x, y);
        if (I < 0.2) continue;
        p.dot(x, y, I > 0.7 ? WHITE : C[6], Math.min(1, I * 1.3));
        p.light(x, y, C[5], 0.4 * Math.min(1, I));
      }
      if (t > HIT - 0.02 && t < HIT + 0.25) sparkle(p, 0, PY - 2, 40 * (1 - (t - HIT) / 0.25), WHITE, C[5], 0.7);
      if (t > SHUT && t < SHUT + 0.2) sparkle(p, 0, PY - 2, 26 * (1 - (t - SHUT) / 0.2), WHITE, C[5], 0.5);
      feathers.draw(p, WING, "front");
      ps.draw(p, "front");
      lights.draw(p);
    },
  };
}
