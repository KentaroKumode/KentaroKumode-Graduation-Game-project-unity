// DICE BOUND 用 (2026-11-14): 審判官の大技を放つ直前 (大技のターンの溜め)。 待機 (dicebound/judge-charged) のループの頭から続く。
// 光の帯がだんだん増えて濃くなる → 日輪が芯へ縮む → 画面全体が白く飛ぶ (2026-11-14 作者の指定)。 白のコマで、 祝福の光 (dicebound/judge-verdict) へつなぐ。
// 画面全体 (480×270)・原点は日輪の中心。 絵は judge-charge.js の sun() (more = 帯の増え具合・open = 日輪の開き具合)。
import { fit, clamp, ease, WHITE } from "../../core/index.js";
import { meta as CHARGE, sun } from "./judge-charge.js";

export const meta = {
  name: "Judge release (DICE BOUND)",
  desc: "The Judge passes sentence: the shafts of light round his sun multiply and brighten, the sun draws in to a point, and the whole screen whites out.",
  tags: ["holy", "dicebound", "boss", "charge", "flash", "one-shot"],
  size: CHARGE.size,
  origin: CHARGE.origin,
  duration: 1.75,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, rays: 8, scale: 1, lead: 1.4, shrink: 0.4 },   // shrink = 白く飛ぶ前、 日輪が縮むのにかける秒
};

export function draw(p, t, o) {
  const C = fit(o.palette, 8), LEAD = o.lead, [W, H] = meta.size;
  if (t < LEAD) {
    const more = clamp(t / (LEAD - o.shrink * 0.5)), q = clamp((t - (LEAD - o.shrink)) / o.shrink);
    // 日輪は、 最後の shrink 秒で芯へ縮む (速く始まらず、 最後に一気に)
    sun(p, C, t, o.rays, o.scale, 1 - 0.92 * ease.inCubic(q), 1, Math.max(0.001, more));
    if (q > 0) p.glow(0, 0, 16 + 30 * q, WHITE, 0.3 + 0.5 * q);
    // 放つ直前: 画面全体が明るむ
    const pre = clamp((t - (LEAD - 0.25)) / 0.25);
    if (pre > 0) for (let y = 0; y < H; y++) for (let x = (y & 1); x < W; x += 2) p.add(x, y, C[5], 0.5 * pre * pre);   // add / put は画面の左上からの座標
  } else {
    // 白く飛ぶ: 0.13 秒は真っ白 (1 秒 8 コマでも必ず 1 コマは白い)、 そのあと 0.2 秒で抜ける
    const a = Math.sqrt(1 - clamp((t - LEAD - 0.13) / 0.2));
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) { p.put(x, y, WHITE, a * a); p.add(x, y, WHITE, a * a); }
  }
}
