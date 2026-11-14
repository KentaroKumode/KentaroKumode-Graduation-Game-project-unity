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
  // シュヴァリエ: 鋼の青から白へ (案)
  // 一番暗い色は黒 (大技で、 黒い画面が 2 枚に割れて動く。 その黒)
  steel: ["#020308", "#3c4a6a", "#5a8ac0", "#b8d0ec", "#ffffff"],
  // 血 (斧の上の段)・毒 (短剣) ── 見本用の仮の色の組
  blood: ["#140608", "#5a1a20", "#b8342c", "#f08a6a", "#fff0e4"],
  poison: ["#0a1410", "#2c4a34", "#4e9a4a", "#b4e08a", "#f4ffe0"],
  // ヴェスカ: 黒から紫、 白へ (案)
  null: ["#05040a", "#3a2c5c", "#7c5cc4", "#c8b4f2", "#ffffff"],
  // 敵の汎用の技 (見本用の仮の色の組): 鉄の灰・青磁
  iron: ["#0c0c10", "#3a3840", "#77747e", "#b9b6c0", "#f2f0f6"],
  celadon: ["#0b1211", "#536862", "#93ada5", "#c9dad3", "#f7fdfa"],   // 鏡: 薄い青磁 (作者: 色味はもう少し薄く)
  earth: ["#1a120c", "#4a3626", "#8a6844", "#c8a878", "#f4e8d0"],   // 土
};
const RAMP = (INKS[flags.ink ?? "warm"]).map(rgb3), BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5], LIGHT_GAIN = 1.6;
const lum = (R, G, B) => (0.299 * R + 0.587 * G + 0.114 * B) / 255, step = (L) => Math.max(0, Math.min(RAMP.length - 1, Math.round(L * (RAMP.length - 1))));

const fx = await fxlib.loadEffect(id);
const OPTS = { bg: "none", options: flags.opts ? JSON.parse(flags.opts) : {} };
let r;
if (flags.pfps) {
  // --pfps 24: 形 (斬撃・光・輪など) は --fps (既定 8) のコマで止め、 粒 (Particles・Bits) だけを pfps で動かす (2026-11-14 作者: 粒のコマの速さが合っていない)。
  // 作り: pfps で 2 回書き出す (粒あり / 粒なし)。 粒 = 2 つの差がある画素。 形は、 粒なしの方を、 fps のコマごとに止めて使う。 pfps は fps の整数倍。
  const core = await import(pathToFileURL(join(ROOT, "src", "core", "index.js")).href);
  const PF = +flags.pfps, HOLD = Math.round(PF / +(flags.fps ?? 8)), full = fxlib.renderFrames(fx, { fps: PF, ...OPTS });
  const pd = core.Particles.prototype.draw, bd = core.Bits.prototype.draw;
  core.Particles.prototype.draw = function () {}; core.Bits.prototype.draw = function () {};
  const bare = fxlib.renderFrames(fx, { fps: PF, ...OPTS });
  core.Particles.prototype.draw = pd; core.Bits.prototype.draw = bd;
  full.frames.forEach((f, fi) => {
    const b = bare.frames[fi], hold = bare.frames[fi - (fi % HOLD)], raw = new Uint32Array(hold.raw), light = new Uint32Array(hold.light);
    for (let i = 0; i < raw.length; i++) if (f.raw[i] !== b.raw[i] || f.light[i] !== b.light[i]) { raw[i] = f.raw[i]; light[i] = f.light[i]; }
    f.raw2 = raw; f.light2 = light;
  });
  full.frames.forEach((f) => { f.raw = f.raw2; f.light = f.light2; });
  r = full;
} else r = fxlib.renderFrames(fx, { fps: +(flags.fps ?? 8), ...OPTS });
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
