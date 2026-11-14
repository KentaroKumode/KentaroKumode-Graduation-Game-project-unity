// DICE BOUND 用 (2026-11-14): 羽根を、 広げて置いても細かく・ぱきっと描くための部品。
// holy/_lib の feather() は、 芯の線・縁の線の太さを「元の座標の 1」で決めている。 効果を 2.6 倍や 3.6 倍に広げて置くと、 その線が 3 ドット前後の太さになり、 面もべたに広がって安っぽい (作者の指摘)。
// ここでは同じ形・同じ色の段のまま、 線の太さを「画面の 1 ドット」で決め直し (PX = 1 ÷ 倍率)、 面に、 芯から先へ斜めに走る細い筋 (羽枝) を 3 ドットおきに入れる。 先の淡い所も、 半透明ではなく色で置く。
import { clamp, WHITE } from "../../core/index.js";
import { Feathers } from "../holy/_lib.js";

/** holy/_lib の feather() と同じ引数 + PX (画面の 1 ドットが、 元の座標でいくつか)。 */
export function fineFeather(p, x, y, ang, len, wid, R, a = 1, side = 1, hot = 0, quill = 0.02, PX = 1) {
  if (len < 1.5 * PX || a <= 0.02) return;
  const n = R.length, ca = Math.cos(ang), sa = Math.sin(ang), x1 = x + ca * len, y1 = y + sa * len, pad = wid + 1.2, h = hot > 0.5;
  const edge = h ? R[n - 3] : R[n - 5], lead = h ? WHITE : R[n - 2], B = h ? [WHITE, WHITE, R[n - 2]] : [R[n - 2], R[n - 3], R[n - 4]];
  p.each(Math.min(x, x1) - pad, Math.min(y, y1) - pad, Math.max(x, x1) + pad, Math.max(y, y1) + pad, (px, py, sx, sy) => {
    const dx = px - x, dy = py - y, u = (dx * ca + dy * sa) / len;
    if (u < 0 || u > 1.05) return;
    const v = (-dx * sa + dy * ca) * side, av = Math.abs(v);
    const w = wid * Math.sqrt(clamp((u - quill) / 0.22)) * Math.sqrt(Math.max(0, 1 - Math.pow(Math.max(0, (u - 0.55) / 0.47), 2)));
    const lim = v < 0 ? w * 0.42 : w, fade = u < 0.72 ? 1 : 1 - (u - 0.72) * 1.6;
    if (av < 0.5 * PX && u < 0.9) return p.put(sx, sy, u < 0.5 ? WHITE : B[0], a);   // 芯: 1 ドット
    if (av > lim + 0.05 * PX || w < 0.5 * PX) return;
    if (lim - av < 1 * PX) return v > 0 ? p.put(sx, sy, u > 0.85 ? B[2] : edge, a * fade) : p.put(sx, sy, lead, a);   // 縁: 1 ドット (後ろの縁は暗く、 前の縁は明るい)
    const barb = ((((u * len - av * 0.8) / PX) % 3) + 3) % 3 < 1;   // 羽枝: 芯から先へ斜めに、 3 ドットおきの細い筋
    if (u < 0.72) return p.put(sx, sy, barb ? (u < 0.38 ? B[1] : B[2]) : u < 0.38 ? B[0] : B[1], a);
    p.put(sx, sy, barb ? edge : B[2], a * fade);
  });
}

/** fn の間だけ、 散る羽根 (holy/_lib の Feathers) を fineFeather で描かせる (元のコードは触らない)。 size = 羽根の大きさの倍率 (飛ぶ範囲は変えずに、 羽根だけ小さく)。 */
export function fineFeathers(PX, size, fn) {
  const draw = Feathers.prototype.draw;
  Feathers.prototype.draw = function (p, R, layer) {
    for (const f of this.list) {
      if (f.delay > 0 || (layer && (f.back ? "back" : "front") !== layer)) continue;
      const k = f.life / f.max, a = Math.min(1, f.life / 0.45, f.age / 0.06), ca = Math.cos(f.a), sa = Math.sin(f.a), L = f.len * size, Wd = f.wid * size;
      fineFeather(p, f.x - (ca * L) / 2, f.y - (sa * L) / 2, f.a, L, Wd, R, a, f.side, f.age < 0.06 ? 1 : 0, 0.2, PX);
      p.glow(f.x, f.y, (4 + f.len * 0.5) * size, R[R.length - 3], 0.22 * a * (0.4 + 0.6 * k));
    }
  };
  try { fn(); } finally { Feathers.prototype.draw = draw; }
}
