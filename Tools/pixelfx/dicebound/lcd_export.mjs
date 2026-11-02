// pixelfx の効果を、 液晶の見た目へ寄せて書き出す (2026-11-01)。 本家のコード (../src・../tools) には手を入れない。
//   ・色は明るさの段で指定の 5 色へ寄せる ── 効果が中に持つ別の色の組や直書きの白もまとめて揃う
//   ・半透明は使わない: 4×4 の網点で描く / 描かないに分ける (航行の描き方と同じ)
//   ・光の層 (なめらかなぼかし) は捨て、 強さに応じた網点の点にする (にじみは液晶の側が掛ける)
// 使い方: node lcd_export.mjs <効果の id> <出力先> <色の組: ink|red|warm|poison|blood> [fps=8] [--tofu | --actor 形.json]
//   出力: <出力先>/<id>/<名前>.png と .json (Aseprite 形式・meta.pixelfx に fps と基準点)。 ゲームへ入れる手順は ../README.md
import { join } from "node:path";
import { mkdirSync, writeFileSync } from "node:fs";

const SRC = new URL("../tools/lib/", import.meta.url);
const { loadEffect, renderFrames } = await import(new URL("fxlib.mjs", SRC).href);
const { encodePNG } = await import(new URL("png.mjs", SRC).href);

const [id, out, inkName = "ink", fpsArg = "8"] = process.argv.slice(2);
const hex = (h) => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
const INKS = {
  ink: ["#283346", "#56657a", "#a59f8a", "#e2dcc6", "#f2ecd8"].map(hex),   // 航行の線・文字のインク (暗 → 明)
  red: ["#3a2424", "#8a3a2c", "#c8553f", "#d9a24a", "#f2ecd8"].map(hex),
  warm: ["#4a1c0c", "#a8461e", "#e0782c", "#ffb347", "#fff8e4"].map(hex),   // 戦闘画面の白熱灯の暖色 (会心の数字 #ffb347・火花 #fff8e4)
  poison: ["#1a2210", "#3a5420", "#6f8f2c", "#a9c84a", "#e6f2a8"].map(hex),  // 毒: 黄みのくすんだ緑 (原色を避ける)
  blood: ["#2a0c0e", "#5c1618", "#922626", "#c8483a", "#f0a088"].map(hex),   // 出血: 暗い赤
};
const RAMP = INKS[inkName];
const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
const LIGHT_GAIN = 1.6;   // 光の強さ → 網点の濃さ

// --tofu: 戦闘の仮の敵 (Game2DBattleFx.Tofu: 前面 100×110・上面 103×10・側面 7×110) と同じ形を人物として渡す。
//   足もと中心が原点・左端は中心から 50 左 (C# の Tofu(cx, feet, 100, 120) と同じ位置)
async function tofuActor() {
  const { hex } = await import(new URL("../../src/core/index.js", SRC).href);
  const w = 107, h = 120, mask = new Uint8Array(w * h), pixels = new Uint32Array(w * h);
  const front = hex("#96b487"), top = hex("#bcd6ae"), side = hex("#3c5037");
  for (let y = 0; y < h; y++)
    for (let x = 0; x < w; x++) {
      const isTop = y < 10 && x >= 4, isFront = y >= 10 && x < 100, isSide = y >= 10 && x >= 100;
      if (!(isTop || isFront || isSide)) continue;
      mask[y * w + x] = 1; pixels[y * w + x] = isTop ? top : isSide ? side : front;
    }
  return { mask: { w, h, data: mask }, pixels, x: -50, y: -h, w, h, k: 2, head: [0, -h], chest: [0, -Math.round(h * 0.6)] };
}

// --actor 形.json: 敵の絵から作った形 ({ w, h, x, y, data: [0/1...], colors: ["#rrggbb" | null...] })。 足もと中心が原点
async function fileActor(path) {
  const { hex } = await import(new URL("../../src/core/index.js", SRC).href);
  const { readFileSync } = await import("node:fs");
  const j = JSON.parse(readFileSync(path, "utf8")), n = j.w * j.h, mask = new Uint8Array(n), pixels = new Uint32Array(n);
  for (let i = 0; i < n; i++) if (j.data[i]) { mask[i] = 1; pixels[i] = hex(j.colors[i]); }
  return { mask: { w: j.w, h: j.h, data: mask }, pixels, x: j.x, y: j.y, w: j.w, h: j.h, k: 2, head: [0, j.y], chest: [0, Math.round(j.y * 0.6)] };
}

const fx = await loadEffect(id);
const ai = process.argv.indexOf("--actor");
const actor = ai > 0 ? await fileActor(process.argv[ai + 1]) : process.argv.includes("--tofu") ? await tofuActor() : null;
const r = renderFrames(fx, { fps: +fpsArg, bg: "none", ...(actor ? { options: { actor } } : {}) });
const { w, h, n } = r;
const cols = Math.min(n, Math.max(1, Math.ceil(Math.sqrt((n * h) / w)))), rows = Math.ceil(n / cols);
const sheet = new Uint8ClampedArray(cols * w * rows * h * 4);

const lum = (R, G, B) => (0.299 * R + 0.587 * G + 0.114 * B) / 255;
const step = (L) => Math.max(0, Math.min(RAMP.length - 1, Math.round(L * (RAMP.length - 1))));

r.frames.forEach((f, fi) => {
  const ox = (fi % cols) * w, oy = Math.floor(fi / cols) * h;
  for (let y = 0; y < h; y++)
    for (let x = 0; x < w; x++) {
      const i = y * w + x, c = f.raw[i], l = f.light[i] & 0xffffff;
      const ca = (c >>> 24) / 255, th = (BAYER[(y & 3) * 4 + (x & 3)] + 0.5) / 16;
      const lr = l & 255, lg = (l >>> 8) & 255, lb = (l >>> 16) & 255, li = Math.min(1, lum(lr, lg, lb) * LIGHT_GAIN);
      let col = null;
      if (ca > 0 && ca >= th) {
        // 色: 明るさの段でインクへ。 光が強く当たっている画素は 1 段明るく
        let s = step(lum(c & 255, (c >>> 8) & 255, (c >>> 16) & 255));
        if (li > 0.5) s = Math.min(RAMP.length - 1, s + 1);
        col = RAMP[s];
      } else if (li > th) {
        // 光だけの画素: 網点の点。 強いほど点が詰まり、 色も明るい段
        col = RAMP[Math.max(1, step(li * 0.85))];
      }
      if (!col) continue;
      const j = ((oy + y) * cols * w + (ox + x)) * 4;
      sheet[j] = col[0]; sheet[j + 1] = col[1]; sheet[j + 2] = col[2]; sheet[j + 3] = 255;
    }
});

const name = id.split("/")[1], dir = join(out, id);
mkdirSync(dir, { recursive: true });
writeFileSync(join(dir, name + ".png"), encodePNG(cols * w, rows * h, sheet));
const json = { frames: [], meta: { app: "pixelfx + lcd_export", image: name + ".png", size: { w: cols * w, h: rows * h }, pixelfx: { id, fps: r.fps, frames: n, frameSize: [w, h], pivot: fx.meta.origin, loop: fx.meta.loop } } };
for (let i = 0; i < n; i++) json.frames.push({ filename: `${name} ${i}`, frame: { x: (i % cols) * w, y: Math.floor(i / cols) * h, w, h }, sourceSize: { w, h }, duration: Math.round(1000 / r.fps) });
writeFileSync(join(dir, name + ".json"), JSON.stringify(json, null, 1));
console.log(`${id}  ${n} frames ${w}x${h}  -> ${dir}`);
