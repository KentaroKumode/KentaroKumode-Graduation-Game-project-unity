// DICE BOUND 用 (2026-11-18・精鋭のオーラの候補): arcane/rune-circle の写し。 式は元のまま、 形を決めている数に倍率 K (`scale`) を掛けた。
//   掛けた数: 3 つの輪の半径・鋲の位置と間隔・文字の位置と大きさ (文字の点は 2 倍 = 敵の絵と同じ 2 ドットの格子)・光の半径・粒の上る高さと速さと数・きらめきの腕。
//   輪の線の太さは 1 → round(K × 0.8) ドット (元の floorRing の「線の半幅 0.5」に掛けた)。 正方形の線は 2 本並べて 2 ドット。
//   尺は元の 4 秒 (24 コマで 96 コマ)。 走る光のまたたき sin(t × 30) は 4 秒で戻らないので sin(t / D × 2π × 19) (ほぼ同じ速さ) にした。 手前の半分 (draw) は敵の足の前に出る。
// The floor glow and the specks rising off it follow Sprite Fusion's open-source mana heal
// (https://www.spritefusion.com/pixel-art-vfx/heal), MIT License, Copyright (c) 2026 Hugo Duprez.
import { PAL, fit, cycle, smooth, ease, sparkle, TAU, WHITE } from "../../core/index.js";
import { GLYPHS, mote } from "../arcane/_lib.js";

const D = 4, SY = 0.3, NR = 12;

export const meta = {
  name: "Elite rune circle (DICE BOUND)",
  desc: "The rune circle scaled up under an elite foe's feet: a turning carousel of runes, ticks and a square, motes rising off it.",
  tags: ["dicebound", "elite", "aura", "rune", "loop"],
  size: [480, 270],
  origin: [240, 204],
  duration: D,
  loop: true,
  fps: 24,
  bg: "dark",
  options: { palette: PAL.arcane, seed: 8, scale: 1.45, rise: 1.6 },   // rise = 粒の上る高さの倍率 (K に掛ける・敵の背が高いため)
};

const gap = (a, b) => Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));

// arcane/_lib の floorRing の写し。 線の半幅 0.5 に hw を掛けられるようにした
function floorRing(p, cx, cy, r, sy, c, a = 1, keep = null, hw = 1) {
  if (r < 0.5) return;
  const fc = typeof c === "function", fa = typeof a === "function", ry = r * sy;
  p.shade(cx - r - 1 - hw, cy - ry - 1 - hw, cx + r + 1 + hw, cy + ry + 1 + hw, (x, y, o) => {
    const nx = (x - cx) / r, ny = (y - cy) / Math.max(0.3, ry), d = Math.hypot(nx, ny);
    if (d < 1e-6) return;
    const g = Math.hypot(nx / r, ny / Math.max(0.3, ry)) / d;
    if (Math.abs(d - 1) / g > 0.5 * hw) return;
    const ang = Math.atan2(ny, nx);
    if (keep && !keep(ang)) return;
    o.c = fc ? c(ang) : c;
    o.a = fa ? a(ang) : a;
  });
}
// arcane/_lib の glyph の写し。 点を s 倍 (2 = 敵の絵の 1 画素と同じ大きさ)
function glyph(p, i, x, y, c, a = 1, under = 0, s = 2) {
  const g = GLYPHS[((i % GLYPHS.length) + GLYPHS.length) % GLYPHS.length], X = Math.round(x) - s, Y = Math.round(y) - (g.length - 1) * s;
  if (under) p.sprite(g, {}, X, Y + s, { tint: under, a, scale: s });
  p.sprite(g, {}, X, Y, { tint: c, a, scale: s });
}

function circle(p, C, t, far, K) {
  const R = 46 * K, RR = 39 * K, RI = 23 * K, RC = 7 * K, hw = Math.max(1, Math.round(K * 0.8));
  const at = (g, r) => [Math.cos(g) * r, Math.sin(g) * r * SY];
  const ph = t / D, spin = (ph * TAU) / 4, keep = far ? (g) => Math.sin(g) < 0 : (g) => Math.sin(g) >= 0;
  const chase = ph * TAU * 2, lit = (g, w) => gap(g, chase) < w || gap(g, chase + Math.PI) < w;
  const tone = (g, hi, lo) => (Math.sin(g) < 0 ? lo : hi);
  floorRing(p, 0, 0, R, SY, (g) => (lit(g, 0.22) ? WHITE : lit(g, 0.5) ? C[6] : tone(g, C[6], C[5])), 1, keep, hw);
  floorRing(p, 0, 0, RI, SY, (g) => tone(g, C[5], C[4]), 1, keep, hw);
  floorRing(p, 0, 0, RC, SY, (g) => tone(g, C[6], C[5]), 1, keep, hw);
  for (let j = 0; j < 16; j++) {
    const g = spin + ((j + 0.5) * TAU) / 16;
    if (Math.sin(g) < 0 !== far) continue;
    const [x, y] = at(g, R + 3.5 * K), hot = lit(g, 0.3), c0 = hot ? WHITE : tone(g, j % 2 ? C[5] : C[6], C[4]), c1 = hot ? C[6] : tone(g, C[4], C[3]);
    p.rect(Math.round(x) - 1, Math.round(y) - 1, 2, 2, c0);
    if (j % 2 === 0) for (const dx of [-1, 1]) p.rect(Math.round(x + dx * K) - 1, Math.round(y) - 1, 2, 2, c1);
  }
  const pts = [0, 1, 2, 3].map((k) => at(-spin + (k * TAU) / 4 + Math.PI / 4, RI));
  for (let k = 0; k < 4; k++) {
    let [ax, ay] = pts[k], [bx, by] = pts[(k + 1) % 4];
    if ((ay < 0) !== (by < 0)) {
      const u = ay / (ay - by), mx = ax + (bx - ax) * u;
      if ((ay < 0) === far) (bx = mx), (by = 0);
      else (ax = mx), (ay = 0);
    } else if ((ay < 0) !== far) continue;
    p.line(ax, ay, bx, by, far ? C[4] : C[5]);
    p.line(ax, ay + 1, bx, by + 1, far ? C[4] : C[5]);
  }
  const gs = [...Array(NR).keys()].map((k) => [k, spin + ((k + 0.5) * TAU) / NR]).filter(([, g]) => Math.sin(g) < 0 === far);
  gs.sort((a, b) => Math.sin(a[1]) - Math.sin(b[1]));
  for (const [k, g] of gs) {
    const [x, y] = at(g, RR), hot = lit(g, 0.2), bob = Math.sin(ph * TAU * 4 + 4 * g) * 0.5 * K;
    glyph(p, [0, 3, 5][k % 3], x, y - K + bob, hot ? WHITE : far ? C[5] : C[6], 1, hot ? C[5] : far ? C[2] : C[3]);
    p.glow(x, y - 3 * K, (hot ? 10 : 6) * K, C[5], hot ? 0.5 : far ? 0.12 : 0.22);
    p.glow(x, y, 5 * K, C[4], 0.15);
  }
}

function motes(p, C, t, S, far, K, rise) {
  const R = 46 * K, RR = 39 * K, RI = 23 * K;
  cycle(t, D, Math.round(76 * K), S * 5 + 1, (g) => g.range(1.1, 1.9), (age, life, r) => {
    const g0 = r.range(0, TAU), r0 = r.pick([R, R, RR, RR, RI]), q = age / life, g = g0 + q * r.range(0.6, 1.4);
    if (Math.sin(g) < 0 !== far) return;
    const rr = r0 * (1 - 0.35 * q), h = (2 + ease.outQuad(q) * r.range(26, 52)) * K * rise, x = Math.cos(g) * rr, y = Math.sin(g) * rr * SY - h;
    const a = Math.min(1, q * 5) * (1 - smooth(0.65, 1, q)), big = r.chance(0.2);
    mote(p, x, y, -Math.sin(g) * rr * 0.8, (-36 * (1 - q) - 10) * K * rise, r.chance(0.3) ? WHITE : r.chance(0.6) ? C[6] : C[5], (big ? 1.1 : r.range(0.55, 0.9)) * K, a, big ? 0.5 : 0.3);
    if (big) mote(p, x + 1, y, -Math.sin(g) * rr * 0.8, (-36 * (1 - q) - 10) * K * rise, C[5], 1.1 * K, a, 0);
  });
  cycle(t, D, Math.round(14 * K), S * 5 + 2, 0.45, (age, life, r) => {
    const g = r.range(0, TAU);
    if (Math.sin(g) < 0 !== far) return;
    const rr = r.range(8, 46) * K, x = Math.cos(g) * rr, y = Math.sin(g) * rr * SY - r.range(6, 34) * K * rise, k = Math.sin((age / life) * Math.PI);
    sparkle(p, x, y, (1 + 2.4 * k) * K, C[6], C[5], 0.35 * k);
  });
}

export function drawBack(p, t, o) {
  const K = o.scale, R = 46 * K, C = fit(o.palette, 8), pulse = 0.85 + 0.15 * Math.sin((t / D) * TAU * 4);
  p.glow(0, 0, R * 1.4, C[4], 0.24 * pulse);
  for (const s of [-1, 1]) p.glow(s * R * 0.55, 1, R * 0.7, C[5], 0.12 * pulse);
  for (let h = 0; h < 66 * K * o.rise; h += 6 * K) p.glow(0, -h, 26 * K, C[4], 0.11 * pulse * (1 - h / (80 * K * o.rise)));
  circle(p, C, t, true, K);
  motes(p, C, t, o.seed, true, K, o.rise);
}

export function draw(p, t, o) {
  const K = o.scale, R = 46 * K, C = fit(o.palette, 8), chase = (t / D) * TAU * 2;
  circle(p, C, t, false, K);
  motes(p, C, t, o.seed, false, K, o.rise);
  for (const g of [chase, chase + Math.PI]) {
    const x = Math.cos(g) * R, y = Math.sin(g) * R * SY;
    sparkle(p, x, y, (2.5 + Math.sin((t / D) * TAU * 19) * 0.8) * K, C[6], C[5], 0.5);
    p.glow(x, y, 14 * K, C[5], 0.4);
  }
}
