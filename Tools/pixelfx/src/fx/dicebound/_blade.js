// DICE BOUND 用 (2026-11-14): 武器の斬撃の塗り (共通)。 melee/_lib の slash() と同じ動き・同じ色の段に、 作者の指定を足した物:
//   縁を強く光らせる (外の縁の帯を白に寄せ、 光を乗せる)・通った跡を明るい線で残して光らせる・跡と刃のまわりにブルーム・流れに沿った筋を少し。
// s は melee/_lib の swing に { span, W, t0, sweep, hold, trail, ramp, lens }。 FLOW = この効果を何倍に広げて使うか (筋の細かさを画面のドットに合わせる)。 BLOOM = ブルームの強さ。
// The crescent recipe is adapted from Sprite Fusion's open-source slash (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { clamp, smooth, ease, noise2 } from "../../core/index.js";
import { smear, trace, glowAlong, onSwing } from "../melee/_lib.js";

export function glowSlash(p, s, t, FLOW = 2.5, BLOOM = 1) {
  const u = t - s.t0;
  if (u < 0) return;
  const R = s.ramp, n = R.length, life = s.sweep + s.hold + s.trail;
  const head = ease.outExpo(clamp(u / s.sweep)), tail = smooth(s.sweep + s.hold, life, u), age = clamp(u / life);
  const U = s.span * head, T = s.span * tail;
  const endTaper = s.snap ? (1 - smooth(0.45, 1, tail) * 0.78) : 1;
  const W = s.snap
    ? s.W * (1 - clamp((u - s.sweep - s.hold) / (s.snapDur ?? 0.055), 0, 1) * (s.snapCut ?? 0.78)) * endTaper
    : s.W * (1 - tail * 0.55);
  // 光の広さと強さは、 斬撃の太さに合わせる (細い連撃に、 太い斬撃と同じ光を乗せると、 光の塊に埋もれる)
  const g = clamp(s.W / 13, 0.3, 1.2), gk = clamp(s.W / 11, 0.35, 1), gs = s.glowScale ?? 1;
  if (tail < 1 && U - T > 0.01) {
    smear(p, s, T, U, W, (uu, r, out) => {
      const v = (uu - T) / (U - T), w = W * Math.pow(Math.sin(Math.PI * Math.pow(v, s.lens ? 1 : 2.2)), s.lens ? 0.75 : 0.6);
      if (w < 0.35) return;
      const q = (s.R - r) / w;
      if (q < 0 || q > 1) return;
      const strand = noise2(uu * s.R * 0.04 * FLOW - age * 10, r * 0.5 * FLOW, 31);
      const ci = q < 0.22 && age < 0.5 ? 0 : Math.round(q * (n - 1) * 0.75 + age * 2.4 - v * 0.8 + 0.4 + (strand > 0.45 ? -1 : strand < -0.55 ? 0.9 : 0));
      out.c = R[n - 1 - clamp(ci, 0, n - 1)];
      out.a = Math.min(1, v / (s.lens ? 0.1 : 0.4)) * (s.snap ? (1 - smooth(0.8, 1, age) * 0.55) : 1);
      const dr = s.R - r;
      if (dr < 1.6 * g + 0.4 && age < 0.8) { if (dr < 0.8) out.c = R[n - 1]; out.l = R[n - 1]; out.k = 1.2 * (1 - age * 0.8); }
      else if (dr < 3.5 * g && age < 0.6) { out.l = R[n - 2]; out.k = 0.4 * gk * (1 - age); }
    });
    if (s.snap) {
      const wr = clamp(W / s.W, 0.15, 1), wg = (0.45 + 0.55 * wr) * gs, wk = (0.42 + 0.58 * wr) * gs;
      glowAlong(p, s, s.R - W * 0.35, T, U, (4 + W * 0.65) * g * wg, R[n - 3], (uu) => 0.32 * gk * wk * (1 - age) * ((uu - T) / (U - T)), 0, 0.3);
      glowAlong(p, s, s.R - 0.5, T, U, 4.5 * g * wg, R[n - 1], (uu) => 0.55 * gk * wk * (1 - age) * Math.min(1, ((uu - T) / (U - T)) * 2), 0, 0.25);
      glowAlong(p, s, s.R - W * 0.25, T, U, (6 + 4.5 * BLOOM) * g * wg, R[n - 3], () => 0.22 * BLOOM * gk * wk * (1 - age * 0.6), 0, 0.4);
    } else {
      glowAlong(p, s, s.R - W * 0.4, T, U, (4 + W) * g, R[n - 3], (uu) => 0.3 * gk * (1 - age) * ((uu - T) / (U - T)), 0, 0.3);
      glowAlong(p, s, s.R - 0.5, T, U, 4 * g, R[n - 1], (uu) => 0.5 * gk * (1 - age) * Math.min(1, ((uu - T) / (U - T)) * 2), 0, 0.25);
      glowAlong(p, s, s.R - W * 0.3, T, U, (7 + 5 * BLOOM) * g, R[n - 3], () => 0.2 * BLOOM * gk * (1 - age * 0.6), 0, 0.4);
    }
  }
  const f = clamp((u - s.sweep - s.hold) / (s.trail + (s.traceFade ?? 0.4)));
  const th = 0.85 * (1 - f);
  if (tail > 0.02 && th > 0) {
    if (s.dotFade) {
      // 固定格子の点を端から欠けさせつつ間引いて消す (明るさより先に点の数で余韻を落とす)
      const baseStep = 1.1 / Math.max(4, s.R), uStart = T * smooth(0.2, 0.95, f) * 0.72;
      for (let idx = 0; ; idx++) {
        const uu = idx * baseStep;
        if (uu > T) break;
        if (uu < uStart) continue;
        const h = (((idx * 2654435761) >>> 0) % 1000) / 1000;
        const pos = uu / s.span;
        // 始端側ほど早く欠け、先端側ほど最後まで数点残る
        const keep = 0.25 + 0.75 * pos;
        const thresh = Math.pow(f, 0.85) / keep;
        if (h < thresh) continue;
        const [x, y] = onSwing(s, s.R - 0.5, uu);
        const col = f < 0.55 ? R[n - 2] : R[n - 3];
        p.dot(x, y, col, Math.max(0.28, (1 - f * 0.65) * (0.55 + 0.45 * pos)));
      }
      // 細い弧が残っている間だけ跡に沿ってブルームを乗せ、純粋な点線になったら光らせない
      if (tail < 1) {
        const tb = (1 - tail) * gs;
        glowAlong(p, s, s.R - 0.5, uStart, T, (3.5 + 3 * BLOOM) * g * gs, R[n - 2], (uu) => Math.min(1, 0.45 * BLOOM) * gk * tb * (0.4 + 0.6 * uu / s.span), 0, 0.25);
      }
    } else {
      trace(p, s, s.R - 0.5, 0, T, R[n - 2], (uu) => Math.min(1, th * 1.4) * (0.5 + 0.5 * uu / s.span), 0, 0.3);
      glowAlong(p, s, s.R - 0.5, 0, T, (3 + 3 * BLOOM) * g * gs, R[n - 2], (uu) => Math.min(1, 0.5 * BLOOM) * gk * gs * th * (0.5 + 0.5 * uu / s.span), 0, 0.25);
      glowAlong(p, s, s.R - 0.5, 0, T, (6 + 6 * BLOOM) * g * gs, R[n - 3], (uu) => 0.2 * BLOOM * gk * gs * th * (0.5 + 0.5 * uu / s.span), 0, 0.4);
    }
  }
}
