// DICE BOUND 用 (2026-11-14): 天与の剣の発動で、 頭上の日食から下へ広がる光の筋。 weather/god-rays の写し。
// 元は画面の左上の外から右下へ差す光で、 行ごとにまとめて書き込む作り (広げて置けない)。 ここでは: 出どころを日食にして真下へ扇に広げ、 画素ごとに描く。
// 光の筋の式 (6 本・呼吸・揺れ・細い筋)・段の切り方・光を横切る時だけ見える塵は、 元のまま。 _scaled.js で 1.5 倍にして画面全体に置く (tenyo-heavy.js)。
// The banded light (posterized steps from a hot core to the edges, fine strands through it, light laid over the top)
// is adapted from Sprite Fusion's open-source holy light (https://www.spritefusion.com/pixel-art-vfx/holy-light),
// MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, clamp, rand, mix, TAU, WHITE } from "../../core/index.js";

export const meta = {
  name: "Tenyo rays (DICE BOUND)",
  desc: "Shafts of light slant down from high on the left, breathing and swaying, pooling on the floor, as drifting dust motes light up whenever they cross a beam.",
  tags: ["weather", "god rays", "light", "sunbeam", "dust", "ambient", "loop"],
  size: [320, 180],
  origin: [160, 170],
  duration: 10,
  loop: true,
  fps: 30,
  bg: "sky",
  options: {
    palette: ["#1a1208", "#3a2810", "#7a5a24", "#c89a44", "#f2d27a", "#fff2c4", "#ffffff"],
    seed: 3,
  },
};

const D = 10, SX = 0, SY = -170, FAN = 2.6;   // 光の出どころ = 画面の一番上の真ん中 (元は画面の左上の外。 作者: 画面の一番上からでいい)・FAN = 扇を何倍に広げるか (元は ±15 度)
// the fan's axis (down and to the right) and its normal; a pixel's place across the fan is its angle from it
const AX = 0, AY = 1, NX = -AY, NY = AX;   // 扇の軸は真下 (元は右下)
// the shafts: angle off the axis, half width (radians), strength, breathing (cycles a loop), sway
const SHAFTS = [
  { c: -0.27, h: 0.03, k: 0.65, b: 1, sw: 0.006 },
  { c: -0.17, h: 0.022, k: 0.55, b: 2, sw: 0.005 },
  { c: -0.07, h: 0.05, k: 1, b: 1, sw: 0.008 },
  { c: 0.03, h: 0.018, k: 0.6, b: 3, sw: 0.004 },
  { c: 0.12, h: 0.042, k: 0.85, b: 2, sw: 0.007 },
  { c: 0.23, h: 0.026, k: 0.6, b: 1, sw: 0.005 },
];
const STR = new Float32Array(512);

// light at a point: the shafts it's in, softened at their edges, fading with distance, threaded with strands
function lightAt(x, y, sh) {
  const dx = x - SX, dy = y - SY, r = dx * AX + dy * AY, ang = (dx * NX + dy * NY) / r / FAN;
  if (r < 6) return 0;
  let e = 0;
  for (const s of sh) {
    const q = Math.abs(ang - s.c) / s.h;
    if (q < 1) e += s.k * (1 - q * q) ** 1.5;
  }
  if (e <= 0) return 0;
  const strand = STR[clamp(Math.floor((ang + 0.36) * 700), 0, 511)], fall = 1;
  return e * fall * (0.7 + 0.45 * strand);
}

export function drawBack(p, t, o) {
  const q = p, S = o.seed, C = fit(o.palette, 7), sh = shafts(t, S), L = C.map((c) => mix(C[0], c, 0.75));
  // 光の筋: 明るさの式と、 白い芯 / 金の胴 / 淡い縁の 5 段の切り方は、 元のまま。 違うのは ──
  //   画素を 1 つずつ回って描く (広げて置ける)・暗い空気は塗らない (暗さはゲームの側で掛ける)・濃さを段で決める (淡い半透明は、 この液晶では網点に潰れる)
  p.shade(-160, -170, 160, 10, (x, y, out) => {
    const e0 = lightAt(x, y, sh);
    if (e0 <= 0) return;
    const floor = y > 0, e = e0 * (floor ? 1.6 : 1);
    if (e < 0.06) return;
    const lv = e > 1.05 ? 6 : e > 0.78 ? 5 : e > 0.52 ? 4 : e > 0.3 ? 3 : 2;
    out.c = floor ? C[lv] : L[lv];
    out.a = lv >= 5 ? 1 : lv === 4 ? 0.75 : lv === 3 ? 0.5 : 0.3;
    out.l = C[4]; out.k = e * 0.12;
  });
  motes(q, t, S, C, sh, false);
}

export function draw(p, t, o) {
  const q = p, S = o.seed, C = fit(o.palette, 7);
  motes(q, t, S, C, shafts(t, S), true);
}

// each shaft breathes and sways on whole cycles; the strands drift slowly across the beams
function shafts(t, S) {
  for (let i = 0; i < 512; i++) {
    const a = i / 512;
    STR[i] = 0.5 + 0.5 * Math.sin(a * 190 + Math.sin((TAU * t) / D) * 2 + S) * Math.sin(a * 77 - (TAU * t) / D + S * 2);
  }
  return SHAFTS.map((s, i) => ({ c: s.c + s.sw * Math.sin((TAU * t) / D + i * 1.3), h: s.h * (1 + 0.15 * Math.sin((TAU * s.b * t) / D + i)), k: s.k * (0.72 + 0.28 * Math.sin((TAU * s.b * t) / D + i * 2.1)) }));
}

// dust motes drifting slowly, invisible in the dark, catching the light as they cross a beam
function motes(q, t, S, C, sh, front) {
  for (let i = front ? 0 : 50; i < (front ? 50 : 160); i++) {
    const R = (k) => rand(S, i, k), n1 = 1 + Math.floor(R(1) * 2), n2 = 1 + Math.floor(R(2) * 2);
    const x = -170 + R(3) * 340 + (8 + R(4) * 14) * Math.sin((TAU * n1 * t) / D + R(5) * TAU), y = -170 + R(6) * 172 + (6 + R(7) * 10) * Math.sin((TAU * n2 * t) / D + R(8) * TAU) + 4 * Math.sin((TAU * 3 * t) / D + i);
    const e = lightAt(x, y, sh);
    if (e < 0.12) continue;
    const big = front && R(9) < 0.5, k = Math.min(1, e);
    q.px(x, y, k > 0.6 ? WHITE : k > 0.3 ? C[5] : C[4]);
    if (big && k > 0.5) (q.px(x + 1, y, C[4]), q.px(x, y + 1, C[4]));
    q.glow(x, y, big ? 6 : 4, C[4], 0.4 * k);
  }
}
