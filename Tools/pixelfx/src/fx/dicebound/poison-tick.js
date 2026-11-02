// DICE BOUND 用 (2026-11-01): 毒が効いた瞬間の 1 回の演出 (繰り返さない)。 傷が光る → 血管が這い出す → 水ぶくれが 2 度はじけて床に輪
// → 毒の汗が垂れて煙る → 頭上に泡が湧いて弾ける → 血管が引いて消える。
// pixelfx の status/poisoned (MIT) を元に、 体の塗り替えと揺れを外し、
// 粒 (血管・水ぶくれ・毒の汗・床の輪・泡) だけを敵の形の上に重ねる。 敵の絵そのものはゲーム (C#) が描く。
// 大きさは敵 (縦 120 前後) に合わせて広げた。 形は o.actor (mask / pixels / x / y) で受け取る。
import { fit, hex, luma, Rng, clamp, smooth, band, TAU, WHITE } from "../../core/index.js";
import { shape } from "../status/_lib.js";
import { rowNear, pickFrom } from "./_lib.js";

export const meta = {
  name: "Poison tick (DICE BOUND)",
  desc: "Poison on a big enemy: dark veins creep from a wound, blisters swell and pop on the tick, toxic sweat drips and hisses, a ring pulses across the floor.",
  tags: ["status", "poison", "dicebound", "one-shot"],
  size: [220, 210],
  origin: [110, 200],      // 頭上の泡が上ってもはみ出さない高さ
  duration: 1.6,
  loop: false,
  fps: 30,
  bg: "ground",
  actor: 2,
  options: { palette: ["#0e1d14", "#173320", "#23552e", "#318232", "#55b92f", "#8eea3f", "#cfff63", "#f6ffc2"], veins: "#2c1a3a", seed: 5 },
};

const D = meta.duration, TICK = 0.8, G = 760;
const since = (t) => (((t % TICK) + TICK) % TICK);

const BODIES = new WeakMap();
function body(a, seed) {
  let m = BODIES.get(a.mask);
  if (!m) BODIES.set(a.mask, (m = new Map()));
  if (m.has(seed)) return m.get(seed);
  const S = shape(a), H = S.h, W = S.w, R = new Rng(seed * 7 + 3);
  // 傷: 体の右寄り・中ほど。 そこから血管が這い出す (敵が大きいので長く・本数も多く)。 その高さに体が無ければ近い行
  const [wy, l, r] = rowNear(S, H * 0.55), wx = Math.round(l + (r - l) * 0.68);
  const veins = [];
  const walk = (x, y, ang, len, depth) => {
    const pts = [];
    for (let s = 0; s < len; s++) {
      ang += R.range(-0.45, 0.45);
      x += Math.cos(ang); y += Math.sin(ang);
      if (!S.on(Math.round(x), Math.round(y))) break;
      pts.push([Math.round(x), Math.round(y), s / len]);
      if (depth < 2 && s > 3 && R.chance(0.12)) walk(x, y, ang + R.sign() * R.range(0.6, 1.1), len * 0.55, depth + 1);
    }
    veins.push(pts);
  };
  for (let i = 0; i < 6; i++) walk(wx, wy, (i / 6) * TAU + R.range(-0.3, 0.3), 26, 0);
  const inner = S.body.filter(([x, y]) => y > H * 0.15 && y < H * 0.9 && S.on(x - 2, y) && S.on(x + 2, y) && S.on(x, y - 2));
  const pick = (list) => pickFrom(R, list, S.body);      // 細い形で「内側」が無くても体のどこかから選ぶ
  const blisters = [0, 1].map(() => [0, 1, 2, 3, 4].map(() => { const [x, y] = pick(inner); return { x, y, r: R.range(1.6, 2.8) }; }));
  // 毒の汗: 体の中ほどに散らす (元は人形の目と手から探していた)
  const sweat = [0, 1, 2, 3, 4].map(() => pick(inner.filter(([, y]) => y > H * 0.3 && y < H * 0.7)));
  // 頭上の泡: 形のてっぺん (上に何も無い画素) のうち、 上から 1/4 までの所から湧く
  const tops = S.up.filter(([, y]) => y < H * 0.25);
  const bubbles = Array.from({ length: 7 }, (_, i) => {
    const [x, y] = pickFrom(R, tops, S.up);
    return { x, y, born: 0.12 + i * 0.13 + R.range(0, 0.05), r: R.range(1.6, 3.4), sway: R.range(0, TAU), rise: R.range(26, 40) };
  });
  const out = { veins, wound: [wx, wy], blisters, sweat, bubbles };
  m.set(seed, out);
  return out;
}

function ring(p, P, ph, front) {
  const k = ph / 0.55;
  if (k < 1) band(p, 0, 0, 10 + 64 * Math.sqrt(k), 0.18, Math.max(1, 3 * (1 - k)), 1 - k, [P[3], P[4], P[5], P[6]], front ? (g) => Math.sin(g) >= 0 : (g) => Math.sin(g) < 0);
}

export function drawBack(p, t, o) {
  ring(p, fit(o.palette, 8), since(t), false);
}

export function draw(p, t, o) {
  const P = fit(o.palette, 8), a = o.actor, S = shape(a), H = S.h, B = body(a, o.seed), ph = since(t), tick = Math.floor(t / TICK) % 2;
  const vein = hex(o.veins);
  // 傷から血管が這い出して、 また引いていく
  const grow = smooth(0, D * 0.55, t) * (1 - smooth(D * 0.62, D, t)) * 1.15;
  for (const pts of B.veins)
    for (const [x, y, u] of pts) if (u < grow) p.px(a.x + x, a.y + y, u > grow - 0.12 ? P[2] : vein);
  const [wx, wy] = B.wound;
  p.px(a.x + wx, a.y + wy, P[6]); p.px(a.x + wx - 1, a.y + wy, P[3]); p.px(a.x + wx, a.y + wy + 1, P[4]);
  p.glow(a.x + wx, a.y + wy, 10, P[5], 0.3);
  // 水ぶくれ: 毒が効く拍子に膨らんで光り、 はじける
  for (const bl of B.blisters[tick]) {
    const x = a.x + bl.x, y = a.y + bl.y, grow2 = clamp(ph / 0.16), pop = ph > 0.38;
    if (!pop) {
      const r = bl.r * grow2;
      p.circle(x, y, r + 0.6, P[3]);
      p.circle(x - 0.2, y - 0.2, r, P[5]);
      if (r > 0.8) p.px(x - 0.5, y - 0.8, WHITE);
      p.glow(x, y, 5, P[5], 0.3 * grow2);
    } else if (ph < 0.56) {
      const k = (ph - 0.38) / 0.18;
      for (let j = 0; j < 8; j++) {
        const an = (j / 8) * TAU + bl.x, d = bl.r + 0.5 + k * 5;
        p.dot(x + Math.cos(an) * d, y + Math.sin(an) * d - k * 3, j % 2 ? P[6] : P[5], 1.2 - k);
      }
      p.glow(x, y, 8, P[6], 0.4 * (1 - k));
    }
  }
  // 毒の汗: 玉になり、 垂れ、 床に落ちてじゅっと煙る
  B.sweat.forEach(([sx, sy], i) => {
    const age = t - (0.15 + i * 0.12), gather = 0.35;      // 1 回ずつ、 少しずつずらして垂らす
    if (age < 0) return;
    const x = a.x + sx + 0.5, y0 = a.y + sy;
    if (age < gather) {
      const k = age / gather, run = Math.round(k * 3);
      p.px(x, y0 + run, k > 0.5 ? P[6] : P[5]);
      if (k > 0.5) p.px(x, y0 + run - 1, P[4]);
      return;
    }
    const f = age - gather, fallH = -1 - (y0 + 3), tf = Math.sqrt((2 * Math.max(1, fallH)) / G);
    if (f < tf) {
      const y = y0 + 3 + 0.5 * G * f * f;
      p.line(x, y - Math.min(4, G * f * 0.012), x, y, P[4]);
      p.px(x, y, P[6]);
      return;
    }
    const h = f - tf;
    if (h > 0.4) return;
    const k = h / 0.4;
    p.line(x - 1 - k * 3, -1, x + 1 + k * 3, -1, P[k < 0.4 ? 6 : 4], 1.2 - k);
    for (let j = 0; j < 3; j++) p.dot(x + (j - 1) * (2 + 4 * k), -2 - j % 2 - 6 * k * (1 - k), P[6], 1 - k);
    p.disc(x + Math.sin(k * 6) * 2, -3 - k * 8, 1 + 2 * k, P[5], 0.35 * (1 - k));
    p.glow(x, -1.5, 6, P[5], 0.35 * (1 - k));
  });
  ring(p, P, ph, true);
  // 頭上の泡: てっぺんから膨らんで離れ、 揺れながら上り、 弾けて散る
  for (const b of B.bubbles) {
    const u = t - b.born, life = 0.7;
    if (u < 0 || u > life) continue;
    const k = u / life, rise = b.rise * (1 - (1 - k) * (1 - k));
    const x = a.x + b.x + Math.sin(b.sway + u * 11) * 2 * k, y = a.y + b.y - 2 - rise;
    if (k < 0.8) {
      const r = b.r * Math.min(1, u / 0.12);
      p.disc(x, y, r, P[2], 0.5);                 // 中: 半透明 (書き出しで網点になる)
      p.ring(x, y, r + 0.6, 1, P[5]);             // 縁
      if (r > 1.2) p.px(x - r * 0.4, y - r * 0.4, WHITE);      // 光
    } else {
      const q = (k - 0.8) / 0.2;                  // 弾ける: 小さな粒が輪に散って消える
      for (let j = 0; j < 6; j++) {
        const an = (j / 6) * TAU + b.sway, d = b.r + 1 + q * 4;
        p.dot(x + Math.cos(an) * d, y + Math.sin(an) * d, j % 2 ? P[6] : P[5], 1 - q);
      }
    }
  }
}
