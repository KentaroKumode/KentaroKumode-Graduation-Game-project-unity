#!/usr/bin/env node
// 効果を、 このゲームの液晶の見た目 (dicebound/lcd_export.mjs と同じ処理: 光 → 網点・色 → 5 色の段) にして、 コマの帯で出す。
// lcd_export.mjs と違うのは、 効果のオプションを渡せること (--opts '{"ash":8}')。 見本を作るための道具で、 ゲーム用の書き出しは lcd_export.mjs。
//   node lab/lcd_frames.mjs dicebound/gouka-impact out.png [--ink warm] [--fps 8] [--opts JSON]
// 出力: out.png (透明の背景・コマを横に並べる) と out.json ({ w, h, n, fps, pivot })

import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { ROOT } from "./setup.mjs";

const [id, outFile, ...rest] = process.argv.slice(2), flags = {};
for (let i = 0; i < rest.length; i++) if (rest[i].startsWith("--")) flags[rest[i].slice(2)] = rest[++i];
const fxlib = await import(pathToFileURL(join(ROOT, "tools", "lib", "fxlib.mjs")).href);
const { encodePNG } = await import(pathToFileURL(join(ROOT, "tools", "lib", "png.mjs")).href);
const { hex } = await import(pathToFileURL(join(ROOT, "src", "core", "index.js")).href);

const rgb3 = (s) => { const c = hex(s); return [c & 255, (c >>> 8) & 255, (c >>> 16) & 255]; };
const INKS = {
  warm: ["#4a1c0c", "#a8461e", "#e0782c", "#ffb347", "#fff8e4"],
  red: ["#3a2424", "#8a3a2c", "#c8553f", "#d9a24a", "#f2ecd8"],
  // 灰燼の王: 灰 (暗い灰 → 明るい灰) と残り火の橙を 1 つの段に (案)
  ash: ["#2e2622", "#6e5a4e", "#c8612a", "#f0a850", "#fff4dc"],
  // 審判官: 石の灰色から金、 白へ (案)
  holy: ["#2a2622", "#6a5c48", "#b8923e", "#f0d68a", "#fffbe8"],
};
const RAMP = (INKS[flags.ink ?? "warm"]).map(rgb3), BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5], LIGHT_GAIN = 1.6;
const lum = (R, G, B) => (0.299 * R + 0.587 * G + 0.114 * B) / 255, step = (L) => Math.max(0, Math.min(RAMP.length - 1, Math.round(L * (RAMP.length - 1))));

const fx = await fxlib.loadEffect(id);
const r = fxlib.renderFrames(fx, { fps: +(flags.fps ?? 8), bg: "none", options: flags.opts ? JSON.parse(flags.opts) : {} });
const { w, h, n } = r, sheet = new Uint8ClampedArray(n * w * h * 4);
r.frames.forEach((f, fi) => {
  for (let y = 0; y < h; y++)
    for (let x = 0; x < w; x++) {
      const i = y * w + x, c = f.raw[i], l = f.light[i] & 0xffffff, ca = (c >>> 24) / 255, th = (BAYER[(y & 3) * 4 + (x & 3)] + 0.5) / 16;
      const li = Math.min(1, lum(l & 255, (l >>> 8) & 255, (l >>> 16) & 255) * LIGHT_GAIN);
      let col = null;
      if (ca > 0 && ca >= th) { let s = step(lum(c & 255, (c >>> 8) & 255, (c >>> 16) & 255)); if (li > 0.5) s = Math.min(RAMP.length - 1, s + 1); col = RAMP[s]; }
      else if (li > th) col = RAMP[Math.max(1, step(li * 0.85))];
      if (!col) continue;
      const j = (y * n * w + fi * w + x) * 4;
      sheet[j] = col[0]; sheet[j + 1] = col[1]; sheet[j + 2] = col[2]; sheet[j + 3] = 255;
    }
});
writeFileSync(outFile, encodePNG(n * w, h, sheet));
writeFileSync(outFile.replace(/\.png$/, ".json"), JSON.stringify({ id, w, h, n, fps: r.fps, pivot: fx.meta.origin }));
console.log(id, n, "frames", w + "x" + h, "->", outFile);
