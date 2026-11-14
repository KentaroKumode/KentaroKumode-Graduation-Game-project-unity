#!/usr/bin/env node
// 炎の部品を、 パラメータを振って並べる (部品の「加工」の見本)。
//   node lab/vary_fire.mjs [--out DIR]
// 1 枚の絵 = 横に値を振り、 縦に時間 (4 コマ)。 出力: DIR/_vary/fire/*.png と index.html

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { ROOT } from "./setup.mjs";

const args = process.argv.slice(2), flags = {};
for (let i = 0; i < args.length; i++) if (args[i].startsWith("--")) flags[args[i].slice(2)] = args[++i];
const OUT = join(flags.out ?? join(ROOT, "records", "lib"), "_vary", "fire");
mkdirSync(OUT, { recursive: true });

const core = await import(pathToFileURL(join(ROOT, "src", "core", "index.js")).href);
const lib = await import(pathToFileURL(join(ROOT, "src", "fx", "fire", "_lib.js")).href);
const fxlib = await import(pathToFileURL(join(ROOT, "tools", "lib", "fxlib.mjs")).href);
const { encodePNG } = await import(pathToFileURL(join(ROOT, "tools", "lib", "png.mjs")).href);
const { createFrame, clearFrame, composite, painterFor, fit, mix, hex, WHITE, noise } = core;
const { flameTongue, firePuff, smokePuff, lick, ember, spark, hotFirst, LAYERS, BIG_LAYERS, BACK_LAYERS, floorLight } = lib;

const PAL = ["#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff"];
const H = hotFirst(fit(PAL, 9)), GLOW = mix(mix(H[3], H[4], 0.5), WHITE, 0.25), BG = 0xff140f0c, ZOOM = 3;
const TIMES = [0, 0.11, 0.23, 0.37];
const N = (t) => (rate, seed = 0, off = 0) => noise(t * rate + off, seed);
const pages = [];

/** cells: [{ label, draw(p, t) }]。 1 マス CW×CH・原点はマスの (CW/2, CH−6)。 */
function sheet(name, title, note, cells, CW = 44, CH = 56, times = TIMES) {
  const W = cells.length * CW, Hh = times.length * CH, out = new Uint8ClampedArray(W * Hh * 4);
  times.forEach((t, r) => {
    cells.forEach((c, k) => {
      const f = createFrame(CW, CH), p = painterFor(f);
      clearFrame(f);
      p.place(CW / 2, CH - 6);
      c.draw(p, t);
      const px = composite(f, new Uint8ClampedArray(CW * CH * 4), BG);
      for (let y = 0; y < CH; y++) out.set(px.subarray(y * CW * 4, (y + 1) * CW * 4), ((r * CH + y) * W + k * CW) * 4);
    });
  });
  writeFileSync(join(OUT, name + ".png"), encodePNG(W * ZOOM, Hh * ZOOM, fxlib.upscale(out, W, Hh, ZOOM)));
  pages.push({ name, title, note, labels: cells.map((c) => c.label), cw: CW * ZOOM });
}

const tongue = (layers, o = {}) => (p, t) => flameTongue(p, 0, 0, o.h ?? 30, o.w ?? 3.4, o.sx ?? 2, N(t), o.n ?? 77, H, layers, o.jit ?? 0.5, o.wob ?? 1.1);

// 1) 層: 1 層だけ (平塗り) から、 道具の層まで
sheet("tongue_layers", "炎の舌: 色の層", "左の 3 つは層を減らした物 (1 層 = 平らな三角)。 4 層あって初めて炎に見える。 右は道具が持っている層の組。", [
  { label: "1 層 (赤だけ)", draw: tongue([[1, 1, 6]]) },
  { label: "1 層 (橙だけ)", draw: tongue([[1, 1, 4]]) },
  { label: "2 層 (赤 + 黄)", draw: tongue([[1, 1, 6], [0.54, 0.68, 2]]) },
  { label: "LAYERS (4 層)", draw: tongue(LAYERS) },
  { label: "BIG_LAYERS (6 層)", draw: tongue(BIG_LAYERS, { w: 4.6 }) },
  { label: "BACK_LAYERS (奥)", draw: tongue(BACK_LAYERS) },
  { label: "EMBER (消えかけ)", draw: tongue([[1, 1, 7], [0.7, 0.8, 6], [0.4, 0.55, 5]]) },
  { label: "CALM (ろうそく)", draw: tongue([[1, 1, 5], [0.8, 0.88, 3], [0.6, 0.72, 2], [0.38, 0.52, 1], [0.18, 0.3, 0]], { jit: 0.25, wob: 0.6 }) },
  { label: "GHOST (層を逆に)", draw: tongue([[1, 1, 1], [0.8, 0.92, 3], [0.6, 0.78, 6], [0.3, 0.42, 2], [0.14, 0.22, 0]]) },
]);
// 2) 縁のちらつき jit
sheet("tongue_jit", "炎の舌: 縁のちらつき (jit)", "0 = つるつるの涙形。 0.5 = 道具の既定。 2 = 荒れた火。 縦は時間 (0.11 秒おき) ── 縁がコマごとに変わる。",
  [0, 0.25, 0.5, 1, 2, 3].map((j) => ({ label: "jit " + j, draw: tongue(LAYERS, { jit: j }) })));
// 3) 先端のゆらぎ wob と横流れ sx
sheet("tongue_sweep", "炎の舌: 先端の横流れ (sx) とゆらぎ (wob)", "sx = 先端を横へ流す px (風・動き)。 wob = 先端がさまよう幅。",
  [[-10, 1.1], [-4, 1.1], [0, 1.1], [4, 1.1], [10, 1.1], [0, 0], [0, 3], [0, 6]].map(([sx, wob]) => ({ label: `sx ${sx} / wob ${wob}`, draw: tongue(LAYERS, { sx, wob }) })));
// 4) 太さと高さ
sheet("tongue_shape", "炎の舌: 太さ (w) と高さ (h)", "細い = 松明・ろうそく。 太い = 焚き火の中央。 太い舌は BIG_LAYERS にしないと白飛びする (右 2 つを比べる)。",
  [[1.2, 30], [2, 30], [3.4, 30], [5, 30], [3.4, 12], [3.4, 44], [6, 36, "L"], [6, 36, "B"]].map(([w, h, L]) => ({ label: `w ${w} / h ${h}${L ? (L === "B" ? " BIG" : " LAYERS") : ""}`, draw: tongue(L === "B" ? BIG_LAYERS : LAYERS, { w, h }) })), 44, 62);
// 5) 火の塊: 寿命 (横) × 大きさ (縦は使わず、 3 段を別のマスに)
const KS = [1, 0.85, 0.7, 0.55, 0.4, 0.25, 0.1];
for (const size of [2, 4, 7])
  sheet("puff_size" + size, `ちぎれる火の塊: 大きさ ${size} の一生`, "左が生まれた時・右が消える直前 (残りの寿命 k)。 白 → 黄 → 橙 → 赤 → 半透明の焦げ茶、 と冷めながら、 一度膨らんで縮む。",
    KS.map((k) => ({ label: "k " + k, draw: (p) => firePuff(p, 0, -14, k, size, 0.5, H) })), 36, 34, [0]);
// 6) 煙: 寿命
sheet("smoke_life", "煙: 一生 (ふつう / 大きい)", "上 = ふつう・下 = 大きい (big)。 左が生まれた時。 膨らみながら薄くなる。 若い間だけ、 左上に明るい帽子。",
  KS.map((k) => ({ label: "k " + k, draw: (p, t) => smokePuff(p, 0, -16, k, 6, 0, t > 0) })), 40, 40, [0, 1]);
// 7) 部品を足していく: 舌だけ → + ちぎれ → + 火の塊 → + 残り火 → + 光 (小さな火を組む)
const mini = (parts) => (p, t) => {
  const n = N(t);
  if (parts.includes("light")) { p.glow(0, -12, 30, GLOW, 0.34); p.glow(0, -4, 8, H[0], 0.14); floorLight(p, 0, 0.5, 18, 4, GLOW, 0.3); }
  if (parts.includes("smoke")) for (let i = 0; i < 4; i++) smokePuff(p, Math.sin(i * 2.1 + t * 3) * 3, -30 - i * 5 - t * 8, 0.8 - i * 0.18, 3.2, 0);
  if (parts.includes("back")) [[-4, 0.6], [4, 0.66]].forEach(([dx, h], i) => flameTongue(p, dx, -1, 22 * h * (1 + 0.2 * n(3.3, i)), 2, n(2.2, i + 9) * 2, n, 30 + i, H, BACK_LAYERS));
  [[-6, 0.4, 1.8], [6, 0.45, 1.8], [0, 1, 3.2]].forEach(([dx, h, w], i) => flameTongue(p, dx, 0, 22 * h * (1 + 0.2 * n(3.3, i + 3)), w, n(2.2, i + 5) * 2, n, 50 + i, H, w > 2.2 ? BIG_LAYERS : LAYERS));
  if (parts.includes("licks")) for (let i = 0; i < 3; i++) lick(p, (i - 1) * 4 + n(5, i) * 2, -22 - i * 3 - ((t * 20 + i * 5) % 8), 0.9 - ((t * 3 + i * 0.3) % 1) * 0.8, 1.3, H);
  if (parts.includes("puffs")) for (let i = 0; i < 5; i++) { const a = ((t * 2.2 + i * 0.21) % 1); firePuff(p, (i - 2) * 3 + n(4, i + 20) * 2, -20 - a * 16, 1 - a, 1.5, 0.3 + i * 0.1, H); }
  if (parts.includes("embers")) for (let i = 0; i < 5; i++) { const a = ((t * 0.9 + i * 0.2) % 1); ember(p, (i - 2) * 5 + Math.sin(a * 6 + i) * 4, -14 - a * 30, 1 - a, i === 2 ? 2 : 1, [H[1], H[2], H[4], H[6]], a * 40 + i); }
};
sheet("build_up", "部品を足していく (小さな火を組む)", "舌だけ → 奥の舌 → 舌先のちぎれ → 火の塊 → 残り火 → 煙 → 光。 右へ行くほど「燃えている」になる。 光を足した所で、 貼り絵から火に変わる。", [
  { label: "手前の舌 3 本", draw: mini([]) },
  { label: "+ 奥の舌", draw: mini(["back"]) },
  { label: "+ ちぎれ", draw: mini(["back", "licks"]) },
  { label: "+ 火の塊", draw: mini(["back", "licks", "puffs"]) },
  { label: "+ 残り火", draw: mini(["back", "licks", "puffs", "embers"]) },
  { label: "+ 煙", draw: mini(["back", "licks", "puffs", "embers", "smoke"]) },
  { label: "+ 光", draw: mini(["back", "licks", "puffs", "embers", "smoke", "light"]) },
], 64, 76);

const E = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
writeFileSync(join(OUT, "index.html"), `<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>炎の部品の加工</title>
<style>body{margin:0;background:#14110e;color:#e9e1d2;font:14px/1.6 "Yu Gothic UI","Hiragino Sans",sans-serif}main{max-width:1500px;margin:0 auto;padding:16px 16px 80px}
h1{font-size:20px;margin:0 0 4px}h2{font-size:16px;margin:26px 0 4px}p{margin:2px 0 8px;color:#a59c8b}a{color:#e0a04a}
.s{overflow-x:auto;background:#0b0908;border:1px solid #38322a;border-radius:6px;padding:6px}.s img{display:block;image-rendering:pixelated;max-width:none}
.l{display:flex;font-size:12px;color:#e0a04a}.l span{flex:0 0 auto;text-align:center;overflow:hidden;white-space:nowrap}</style></head><body><main>
<p><a href="../../index.html">← 一覧</a></p><h1>炎の部品の加工</h1><p>部品 1 つを、 パラメータを振って並べた物。 横 = 値・縦 = 時間 (0.11 秒おき)。 3 倍に拡大。</p>
${pages.map((g) => `<h2>${E(g.title)}</h2><p>${E(g.note)}</p><div class="s"><div class="l">${g.labels.map((x) => `<span style="width:${g.cw}px">${E(x)}</span>`).join("")}</div><img loading="lazy" src="${g.name}.png"></div>`).join("\n")}
</main></body></html>`);
console.log("sheets", pages.length, "->", OUT);
