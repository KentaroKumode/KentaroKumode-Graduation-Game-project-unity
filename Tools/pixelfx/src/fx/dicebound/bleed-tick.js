// DICE BOUND 用 (2026-11-01): 出血が効いた瞬間の 1 回の演出 (繰り返さない)。 pixelfx の status/bleeding (MIT) の傷・滴・しぶきを元に、
// 拍子を付けて派手にした: 傷が開いて光る → 扇状に噴き出す (赤い霧) → 床で跳ねる → 筋になって垂れる → 血だまりが広がって沈んで消える。
// 敵の絵はゲーム (C#) が描き、 これは上から塗る粒だけ。 形は o.actor (mask / x / y) で受け取る。
// The beads, drops, drying splats and crowns are adapted from Sprite Fusion's open-source blood
// (https://www.spritefusion.com/pixel-art-vfx/blood), MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, rand, smooth, clamp } from "../../core/index.js";
import { shape, travel } from "../status/_lib.js";
import { rowNear } from "./_lib.js";

export const meta = {
  name: "Bleed tick (DICE BOUND)",
  desc: "Bleed takes its toll in one beat: five wounds tear open with a flash, blood sprays out in fans with a red mist, rains onto the floor and bounces, runs down in streaks, and a pool spreads, darkens and fades.",
  tags: ["status", "bleed", "dicebound", "one-shot"],
  size: [260, 170],
  origin: [130, 160],
  duration: 1.8,
  loop: false,
  fps: 30,
  bg: "ground",
  actor: 2,
  options: { palette: ["#350711", "#5e0c1b", "#931327", "#c71e33", "#f2384a", "#ff8a7a"], seed: 5 },
};

const D = meta.duration, G = 900, N = 28;      // N = 1 つの傷から噴く粒

const WOUNDS = new WeakMap();
function wounds(a) {
  let w = WOUNDS.get(a.mask);
  if (w) return w;
  const S = shape(a), H = S.h, W = S.w;
  // その高さに体が無い (穴・切れ目) なら近い行を使う
  const edge = (q, side) => { const [my, l, r] = rowNear(S, H * q); return [side < 0 ? l + 1 : r - 1, my, side]; };
  const front = (fx, fy, side) => { const [my, l, r] = rowNear(S, H * fy); return [Math.round(l + (r - l) * fx), my, side]; };
  w = [edge(0.42, -1), front(0.64, 0.38, 1), edge(0.55, 1), front(0.3, 0.6, -1), edge(0.72, -1)].map(([mx, my, side], i) => {
    let run = 1;
    while (run < 18 && S.on(mx, my + run + 1)) run++;
    return { x: a.x + mx + 0.5, y0: a.y + my, run, side, delay: i * 0.05, seed: i * 17 + 3 };
  });
  WOUNDS.set(a.mask, w);
  return w;
}

// 粒 1 つの位置 (噴いた瞬間からの経過 tt)。 床 (y = -1) に着いたら着地の時刻と位置を返す
function particle(w, j, tt) {
  const spread = (rand(w.seed, j, 1) - 0.5) * 1.6, up = 0.35 + 0.9 * rand(w.seed, j, 2), sp = 50 + 100 * rand(w.seed, j, 3);
  const vx = w.side * sp * Math.cos(spread * 0.6) * (0.6 + 0.4 * rand(w.seed, j, 4)), vy = -sp * up;
  const at = (u) => [w.x + travel(vx, 0, 1.4, u), w.y0 + travel(vy, G, 0.5, u)];
  const [x, y] = at(tt);
  if (y < -1) return { x, y, land: null, at };
  let lo = 0, hi = tt;      // 着地の時刻を二分で探す
  for (let k = 0; k < 14; k++) { const m = (lo + hi) / 2; if (at(m)[1] < -1) lo = m; else hi = m; }
  return { x, y, land: hi, at, lx: at(hi)[0] };
}

function splat(p, C, x, age, big, seed) {
  const life = 0.9;
  if (age < 0 || age > life) return;
  const spread = smooth(0, 0.08, age), hw = (big ? 4 : 2.5) * (0.5 + 0.5 * spread), fade = 1 - smooth(life - 0.35, life, age);
  for (let xx = Math.floor(x - hw); xx <= Math.ceil(x + hw); xx++) p.dot(xx, -1, Math.abs(xx + 0.5 - x) > hw * 0.7 ? C[2] : C[3], fade);
  if (age < 0.25) p.px(x - 0.5, -2, C[5]);
  if (age < 0.4)      // 王冠: 跳ね返る小さな粒
    for (let j = 0; j < (big ? 4 : 2); j++) {
      const vx = (j % 2 ? 1 : -1) * (20 + 30 * rand(seed, j, 1)), vy = -(50 + 60 * rand(seed, j, 2)), tt = age;
      const dx = vx * tt, dy = vy * tt + 0.5 * G * tt * tt;
      if (dy <= 0) p.px(x + dx, -1 + dy, j ? C[3] : C[4]);
    }
}

export function drawBack(p, t, o) {
  const C = fit(o.palette, 6), W = wounds(o.actor);
  // 血だまり: 噴いた血が落ちてから広がり、 最後は暗く沈んで消える
  const xs = W.map((w) => w.x), px = (Math.min(...xs) + Math.max(...xs)) / 2;
  const grow = smooth(0.35, 1.0, t), sink = smooth(1.25, D, t), rx = 10 + 44 * grow;
  if (grow <= 0 || sink >= 1) return;
  for (let x = Math.floor(px - rx - 1); x <= px + rx + 1; x++) {
    const q = Math.abs(x + 0.5 - px) / rx;
    if (q > 1.05) continue;
    const a = 1 - sink;
    p.dot(x, -1, q > 0.85 ? C[2] : sink > 0.4 ? C[1] : C[2], a);
    if (q < 0.8) p.dot(x, 0, q < 0.5 ? C[1] : C[0], a);
    if (q < 0.5) p.dot(x, 1, C[0], a);
    if (q < 0.25) p.dot(x, 2, C[0], a);
    if (q < 0.6) p.dot(x, -2, q < 0.3 ? C[3] : C[2], a * 0.8);      // 盛り上がった縁
  }
  if (sink < 0.3) { p.px(px - 9, -1, C[4]); p.px(px + 12, -1, C[3]); }
}

export function draw(p, t, o) {
  const C = fit(o.palette, 6), W = wounds(o.actor);
  for (const w of W) {
    const u = t - w.delay;
    if (u < 0) continue;
    const close = smooth(1.3, D, t);      // 最後は傷も筋も消える
    // 1. 傷が開いて光る
    const open = clamp(u / 0.08), gl = Math.round(2 + 3 * open);
    if (close < 1) {
      for (let dx = -gl; dx <= gl; dx++) p.dot(w.x + dx, w.y0 - 1, Math.abs(dx) >= gl - 1 ? C[1] : C[0], 1 - close);
      for (let dx = -gl + 1; dx <= gl - 1; dx++) p.dot(w.x + dx, w.y0, u < 0.12 ? C[5] : C[3], 1 - close);
    }
    if (u < 0.15) p.glow(w.x, w.y0, 12, C[5], 0.6 * (1 - u / 0.15));
    // 2. 赤い霧: 噴いた瞬間に傷の外側へ散る (半透明は網点になる)
    if (u < 0.35) {
      const k = u / 0.35;
      for (let j = 0; j < 9; j++) {
        const an = -Math.PI / 2 + w.side * (0.4 + 0.9 * rand(w.seed, j, 9)), d = 4 + 18 * k * (0.6 + 0.4 * rand(w.seed, j, 10));
        p.disc(w.x + Math.cos(an) * d, w.y0 - 2 + Math.sin(an) * d * 0.7, 3 + 6 * k, C[3], 0.6 * (1 - k));      // (an は傷の向き側へ開いている)
      }
    }
    // 3. 扇状に噴き出す粒 → 床に落ちて跳ねる
    for (let j = 0; j < N; j++) {
      const born = 0.03 + 0.012 * j, tt = u - born;
      if (tt <= 0) continue;
      const q = particle(w, j, tt), big = j % 3 === 0;
      if (q.land === null) {
        const [x2, y2] = q.at(Math.max(0, tt - 0.025));
        p.line(x2, y2, q.x, q.y, j % 3 ? C[3] : C[2]); p.line(x2, y2 + 1, q.x, q.y + 1, C[2]);
        p.px(q.x, q.y, C[5]); p.px(q.x + 1, q.y, C[4]); p.px(q.x, q.y + 1, C[3]); if (big) { p.px(q.x + 1, q.y + 1, C[3]); p.px(q.x - 1, q.y, C[4]); }
      } else splat(p, C, q.lx, tt - q.land, big, w.seed * 7 + j);
    }
    // 4. 筋になって垂れる (幅 2)、 先から滴が 1 つ落ちる
    const run = Math.round(w.run * smooth(0.1, 0.6, u));
    if (close < 1)
      for (let y = 1; y <= run; y++) {
        const c = y === run ? C[3] : C[2];
        p.dot(w.x, w.y0 + y, c, 1 - close); p.dot(w.x + w.side * -1, w.y0 + y, y % 3 ? C[1] : c, 1 - close);
      }
    const dropAt = 0.65 + w.delay, f = t - dropAt, yb = w.y0 + w.run + 2, fallH = Math.max(1, -1 - yb), tf = Math.sqrt((2 * fallH) / G);
    if (f > 0 && f < tf) {
      const y = yb + 0.5 * G * f * f, tail = Math.min(9, G * f * 0.016);
      p.line(w.x, y - tail, w.x, y - 1, C[3]); p.px(w.x, y, C[5]); p.px(w.x + 1, y, C[4]); p.px(w.x, y + 1, C[3]);
    } else if (f >= tf) splat(p, C, w.x, f - tf, true, w.seed + 99);
  }
}
