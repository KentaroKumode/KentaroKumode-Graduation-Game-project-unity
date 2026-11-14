// DICE BOUND 用 (2026-11-14): 元の効果を、 **式を 1 行も書き直さずに**、 この画面の大きさで描く仕組み。
// 元の効果は横スクロールの小さい人物用 (高さ 30〜60 ドット)。 写しを作って数を掛け直す方法 (vesca-hex・vesca-slash・tenyo-sword) は 1 本ごとに手間がかかる。
// ここでは、 描画の座標の変換 (Painter の toX / toY / fromX / fromY) に倍率 K を通す。
//   面を塗る描き方 (shade / each / circle / ring / disc …) は、 画面の画素を 1 つずつ回って、 元の座標へ戻して色を決める作りなので、 K 倍でも**画面の解像度で描き直される** (拡大して貼るのと違い、 ドットは大きくならない)。
//   線 (line) は、 両端を K 倍の位置へ移してから、 画面の上で 1 ドットの線を引く。 点 (px / dot) は 1 ドットのまま、 K 倍の位置に出る。
//   光 (glow / glowLine / glowRing) は半径を画面のドットで持っているので、 半径に K を掛ける。
// 形の比率は元のまま大きくなる (色の段の幅も K 倍)。 べた塗りが目立つ物は、 写しを作って縁と筋を直す (library/dicebound/moves.md の決まり 2)。
import { resolveOptions } from "../../core/index.js";

const KEYS = ["toX", "toY", "fromX", "fromY", "cellX", "cellY", "glow", "glowLine", "glowRing", "rect"];

/** fn の中の描画を、 画面の (x, y) を原点に、 K 倍の大きさで行う。 回転と左右反転は考えない。 */
export function withScale(p, K, x, y, fn) {
  const tx = p.tx + x, ty = p.ty + y, P = Object.getPrototypeOf(p);
  p.toX = (lx) => tx + lx * K; p.toY = (lx, ly) => ty + ly * K;
  p.fromX = (sx) => (sx - tx) / K; p.fromY = (sx, sy) => (sy - ty) / K;
  p.cellX = (lx) => Math.floor(tx + lx * K); p.cellY = (lx, ly) => Math.floor(ty + ly * K);
  p.glow = (gx, gy, r, c, k) => P.glow.call(p, gx, gy, r * K, c, k);
  p.glowLine = (x0, y0, x1, y1, r, c, k) => P.glowLine.call(p, x0, y0, x1, y1, r * K, c, k);
  p.glowRing = (gx, gy, R, r, c, k) => P.glowRing.call(p, gx, gy, R * K, r * K, c, k);
  p.rect = (rx, ry, w, h, c, a = 1) => p.shade(rx, ry, rx + w, ry + h, (_x, _y, o) => { o.c = typeof c === "function" ? c(0, 0) : c; o.a = typeof a === "function" ? a(0, 0) : a; });
  try { fn(); } finally { for (const k of KEYS) delete p[k]; }
}

/**
 * 元の効果 SRC を、 K 倍・画面の at に (元の座標の anchor が来るように)・speed 倍の速さで流す物を作る。
 * opts = 元の効果へ渡す設定 (palette など)。 返り値は create() が返す形 ({ update, draw })。
 */
export function scaledFx(SRC, { K = 3, at = [240, 136], anchor = [0, 0], speed = 1, delay = 0, life = 0, opts = {} } = {}) {   // life = ループの効果を、 何秒で消すか (最後の 0.25 秒で網点で薄くなる)
  const o = resolveOptions(SRC.meta, opts), inner = SRC.create ? SRC.create(o) : null, x = at[0] - anchor[0] * K, y = at[1] - anchor[1] * K, D = SRC.meta.duration;
  let t = -delay;
  return {
    done: () => t > D,
    update(dt) { const was = t; t += dt * speed; if (t > 0 && inner && inner.update) inner.update(was < 0 ? t : dt * speed); },
    draw(p) {
      if (t < 0 || (!SRC.meta.loop && t > D) || (life && t > life)) return;
      const f = p.fade;
      if (life) p.fade = f * Math.min(1, t / 0.15, (life - t) / 0.25);
      withScale(p, K, x, y, () => {
        if (inner) { if (inner.drawBack) inner.drawBack(p); inner.draw(p); }
        else { if (SRC.drawBack) SRC.drawBack(p, t, o); SRC.draw(p, t, o); }
      });
      p.fade = f;
    },
  };
}
