// DICE BOUND 用 (2026-11-14): ヴェスカの遺物・攻撃 (握斧・破城槌・殺戮 など)。 melee/sword-slash を、 **大きさだけ 3.6 倍**にした写し (中身はそのまま)。
// (作者: 攻撃の遺物は、 本当に Sword slash / Spear sweep を土台に改変する形でいい。 自前で描いた弧は、 塗りが等比の縞・残像が 2 つ目の斬撃・反りの無い毛束、 と 3 回外した)
// 傾いた三日月が、 ヴェスカの前を大きくまわって、 こちらの盤へ振り下ろされる。 白から深い紫へ冷めながら尾を引き、 切っ先がきらめいて、 火花が飛ぶ。
// 変えた所: 大きさ (弧の中心・半径・太さ・きらめき・光・火花の速さと数)・色 (紫)・残る時間 (1 秒 8 コマで読めるように hold / trail を約 2 倍・通った跡の点線を 2 ドットで長く)・
//   盤に着く所で欠片が跳ね上がる (spear-sweep の、 床から巻き上がる屑)。 形・塗り (色の段の割合)・残像の位置は、 元の式のまま。
// The crescent, echo, trail, glint and spark recipe is adapted from Sprite Fusion's open-source slash
// (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, sparkle, clamp, smooth, ease, noise2, TAU } from "../../core/index.js";

const K = 3.6;   // 元の何倍の大きさか

export const meta = {
  name: "Vesca slash (DICE BOUND)",
  desc: "Vesca uses a relic weapon: a great tilted crescent whips round in front of her and down onto the viewer's board, cools from white to deep violet as it trails off, glints at the tip and throws sparks.",
  tags: ["dicebound", "boss", "relic", "slash", "one-shot"],
  size: [480, 270],
  origin: [240, 322],   // 元の効果の原点 (人物の足もと)。 弧の中心はここから (6, −26) × K 上。 切っ先がこちらの盤 (300,246 あたり) に着く位置に合わせた
  duration: 1.25,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#1c1236", "#3a2470", "#5a3aa8", "#8a64e0", "#b89cf8", "#e4d8ff", "#ffffff"],
    sparks: ["#ffffff", "#e4d8ff", "#b89cf8", "#8a64e0"],
    radius: 42 * K,
    seed: 2,
  },
};

// a point on the slash's tilted ellipse
function onArc(s, r, a) {
  const lx = Math.cos(a) * r, ly = Math.sin(a) * r * s.sy, c = Math.cos(s.rot), n = Math.sin(s.rot);
  return [s.cx + lx * c - ly * n, s.cy + lx * n + ly * c];
}

// the smear: thickest just behind the leading edge, tapering to a hair at the tail, white on the outer rim,
// cooling down the ramp with age and towards the tail; the tail dissolves through the dither
function crescent(p, s, ta, L, W, age) {
  if (Math.abs(L) < 0.02 || W < 0.3) return;
  const R = s.ramp, n = R.length, dir = Math.sign(L), span = Math.abs(L), c = Math.cos(s.rot), sn = Math.sin(s.rot), B = s.R + 2;
  p.shade(s.cx - B, s.cy - B, s.cx + B, s.cy + B, (x, y, out) => {
    const dx = x - s.cx, dy = y - s.cy, lx = dx * c + dy * sn, ly = (-dx * sn + dy * c) / s.sy, r = Math.hypot(lx, ly);
    if (r > s.R + 0.5 || r < s.R - W - 1) return;
    let u = (dir * (Math.atan2(ly, lx) - ta)) % TAU;
    if (u < 0) u += TAU;
    if (u > span) return;
    const v = u / span, w = W * Math.pow(Math.sin(Math.PI * Math.pow(v, 2.2)), 0.6);
    if (w < 0.35) return;
    // ---- ここだけ元と違う (2026-11-14 作者: 大きくすると、 べた塗りが目立って安っぽい。 流れに合った筋で速さを出し、 縁をぱきっと光らせる)
    // 流れに沿った筋: 弧に沿っては長く、 太さの向きには細かいノイズ。 時間で頭の方へ流れる。 明るい筋は 1 段明るく、 暗い筋は 1 段暗く
    const d = s.R - r, along = u * s.R * 0.04 - age * 10, strand = noise2(along, r * 0.5, 31), fine = noise2(along * 2.4 + 7, r * 1.15, 57);
    const wi = w * (0.84 + 0.16 * fine);   // 内の縁は、 筋に沿ってぎざぎざに
    if (d < 0 || d > wi) return;
    const q = d / wi, young = age < 0.6;
    // 外の縁: 白 2 ドット + いちばん明るい色 1 ドット (太さの割合ではなく、 ドットで決める ── 大きくしても細く鋭いまま)。 その内側は元の段に、 筋を足す
    const ci = d < 2.2 && young ? 0 : d < 3.4 && young ? 1 : Math.round(q * (n - 1) * 0.75 + age * 2.4 - v * 0.8 + 0.4 + (strand > 0.22 ? -1.3 : strand < -0.28 ? 1.1 : 0));
    out.c = R[n - 1 - clamp(ci, 0, n - 1)];
    // 尾の方では、 内側が筋にほどけて抜ける (べたの面を残さない)
    out.a = q > 0.28 && fine < -0.1 - v * 0.7 ? 0 : Math.min(1, v / 0.4);
    if (d < 3 && young) { out.l = R[n - 1]; out.k = 0.7 * (1 - age); }   // 縁の光
  });
  for (let u = 0; u <= span; u += 0.3) {
    const [x, y] = onArc(s, s.R - W * 0.4, ta + dir * u);
    p.glow(x, y, 5 + W, R[n - 3], 0.22 * (1 - age) * (u / span));
  }
}

function drawSlash(p, s, t) {
  const u = t - s.t0;
  if (u < 0) return;
  const span = s.a1 - s.a0, head = ease.outExpo(clamp(u / s.sweep)), tail = smooth(s.sweep + s.hold, s.sweep + s.hold + s.trail, u);
  const age = clamp(u / (s.sweep + s.hold + s.trail));
  if (tail < 1) crescent(p, s, s.a0 + span * tail, span * (head - tail), s.W * (1 - tail * 0.55), age);
  // a dotted trace of the blade's path lingers where the smear has already gone
  const th = 0.85 * (1 - clamp((u - s.sweep - s.hold) / (s.trail + 0.6)));
  if (tail > 0.02 && th > 0)
    for (let k = 0; k <= tail; k += 0.6 / (Math.abs(span) * s.R)) {
      const [x, y] = onArc(s, s.R - 0.5, s.a0 + span * k);
      p.dot(x, y, s.ramp[2], th * (0.4 + 0.6 * k)); p.dot(x, y + 1, s.ramp[1], th * (0.4 + 0.6 * k));
    }
}

function tipOf(s) {
  const [x, y] = onArc(s, s.R - s.W * 0.3, s.a1), [x2, y2] = onArc(s, s.R - s.W * 0.3, s.a1 + Math.sign(s.a1 - s.a0) * 0.05);
  const l = Math.hypot(x2 - x, y2 - y) || 1;
  return { x, y, dx: (x2 - x) / l, dy: (y2 - y) / l };
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, sparks: o.sparks });
  const R0 = o.radius, a0 = -2.55, a1 = 0.95;
  // the main smear, and a thinner echo just inside it a beat behind
  const A = { cx: 6 * K, cy: -26 * K, R: R0, sy: 0.6, rot: -0.38, a0, a1, W: 12 * K, t0: 0, sweep: 0.07, hold: 0.12, trail: 0.5, ramp: C };
  const E = { ...A, R: R0 * (33 / 42), W: R0 * (4.5 / 42), a0: a0 + (a1 - a0) * 0.1, a1: a1 - (a1 - a0) * (0.15 / 3.5), t0: 0.017, hold: 0.08, trail: 0.42, ramp: C.slice(0, 6) };
  const tip = tipOf(A);
  let t = 0, landed = false;
  return {
    update(dt) {
      t += dt;
      if (!landed && t >= A.sweep) {
        landed = true;
        // sparks off the edge where it stops, and bright shavings flung off the arc
        for (let i = 0; i < 40; i++) {
          const a = Math.atan2(tip.dy, tip.dx) + Rg.range(-0.5, 0.5), sp = Rg.range(200, 700);
          ps.spawn(P.SPARK, tip.x, tip.y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.15, 0.45));
        }
        for (let i = 0; i < 70; i++) {
          const a = a0 + (a1 - a0) * Rg.range(0.3, 1), [x, y] = onArc(A, A.R, a);
          const ox = x - A.cx, oy = y - A.cy, l = Math.hypot(ox, oy) || 1, sp = Rg.range(60, 260);
          ps.spawn(P.MOTE, x, y, (ox / l) * sp, (oy / l) * sp - 10, Rg.range(0.3, 0.7), C[4 + Rg.int(0, 1)], 1);
          // 輝き: 弧の上に、 遅れてまたたく星 (作者: 輝きの粒は自己判断で足してよい)
          if (i % 3 === 0) ps.spawn(P.STAR, x + Rg.range(-4, 4), y + Rg.range(-4, 4), 0, -Rg.range(4, 18), Rg.range(0.3, 0.7), i % 2 ? C[6] : C[5], Rg.range(1.5, 3.4), Rg.range(0, 0.45));
          // 盤に切っ先が着く所では、 欠片が跳ね上がる (spear-sweep の、 床から巻き上がる屑)
          if (y > 205 && i % 2 === 0) ps.spawn(P.FLAKE, x, y, Rg.range(-90, 90), -Rg.range(80, 260), Rg.range(0.5, 1), C[3 + Rg.int(0, 2)], Rg.range(1, 2.2));
        }
      }
      ps.update(dt);
    },
    draw(p) {
      drawSlash(p, E, t);
      drawSlash(p, A, t);
      // the tip glints as the edge arrives
      const g = t - A.sweep * 0.6;
      if (g > 0 && g < 0.3) sparkle(p, tip.x, tip.y, 9 * K * ease.outBack(Math.min(1, g / 0.05)) * (1 - g / 0.3), C[5], C[3], 0.6);
      // 輝き: 縁の上の 4 か所で、 十字のきらめきが順にまたたく (頭が通った直後から)
      for (const [k, d] of [[0.42, 0.05], [0.6, 0.09], [0.76, 0.13], [0.9, 0.17]]) { const g2 = t - d; if (g2 > 0 && g2 < 0.3) { const [x, y] = onArc(A, A.R - 1, A.a0 + (A.a1 - A.a0) * k); sparkle(p, x, y, 14 * Math.sin((g2 / 0.3) * Math.PI), C[6], C[4], 0.6); } }
      // light rides the head of the swing
      if (t < 0.3) {
        const [x, y] = onArc(A, A.R - 4, A.a0 + (A.a1 - A.a0) * ease.outExpo(clamp(t / A.sweep)));
        p.glow(x, y, 40 * K * 0.7, C[3], 0.5 * (1 - t / 0.3));
      }
      ps.draw(p);
    },
  };
}
