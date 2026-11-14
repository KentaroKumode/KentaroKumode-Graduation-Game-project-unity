#!/usr/bin/env node
// 効果を部品 (札) ごとに分けて書き出す。
//
//   node lab/layers.mjs fire/flame-pillar [--out DIR] [--fps 12] [--zoom 2] [--cols 12] [--seed N]
//   node lab/layers.mjs fire              分類ごと
//
// 出す物 (DIR/<分類>/<名前>/):
//   full.png            元のまま (全部の札)。 コマを左上から右へ並べた帯
//   layer_<札>.png      その札だけを描いた物 (シミュレーションは元と同じ。 描く物だけ絞る)
//   stack_<n>.png       札を、 出てきた順に 1 つずつ足していった物 (これも元と同じ描き順なので、 最後は full と一致する)
//   recomposed.png      札ごとに別々に描いた絵を、 後から重ねた物 (不透明は後の札が上・光は足し算)
//   diff.png            recomposed と full の違い (違う画素が白)
//   manifest.json       札の一覧・出ている時間・山場・画素の数・重ね直した時の違い
//
// 「別々に再生して重ねても同じ見た目になるか」は recomposed と full の違いで分かる。 違いが出るのは、
// 半透明 (煙) が下の色に混ざる所と、 札どうしが交互に描かれて上下が入れ替わる所。

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { prepare, ROOT } from "./setup.mjs";

const args = process.argv.slice(2), flags = {}, ids = [];
for (let i = 0; i < args.length; i++) (args[i].startsWith("--") ? (flags[args[i].slice(2)] = args[++i]) : ids.push(args[i]));
if (!ids.length) { console.error("usage: node lab/layers.mjs <id|分類>... [--out DIR] [--fps N] [--zoom N] [--cols N]"); process.exit(1); }

const SRC = prepare();
const core = await import(pathToFileURL(join(SRC, "core", "index.js")).href);
const { LAB } = await import(pathToFileURL(join(SRC, "core", "painter.js")).href);
const fxlib = await import(pathToFileURL(join(ROOT, "tools", "lib", "fxlib.mjs")).href);
const { encodePNG } = await import(pathToFileURL(join(ROOT, "tools", "lib", "png.mjs")).href);
const { createFrame, clearFrame, createFx, composite, painterFor, stockActor, drawActor, actorAlphaOf } = core;

const OUT = flags.out ?? join(ROOT, "records", "lib");
const ZOOM = +(flags.zoom ?? 2), COLS = +(flags.cols ?? 12), BG = 0xff140f0c; // 暗い背景 (ABGR)
const safe = (t) => (t || "untagged").replace(/[^A-Za-z0-9_.\-]+/g, "_");

/** 1 回走らせる。 only = 描く札の集合 (null なら全部)。 rec = 数える。 戻り値: コマごとの { color, light } (効果だけ・背景なし)。 */
function run(fx, fps, only, rec, seed) {
  const { meta } = fx, [W, H] = meta.size, [OX, OY] = meta.origin;
  const n = Math.max(1, Math.round(meta.duration * fps) + (meta.loop ? 0 : 1));
  const frame = createFrame(W, H);
  const inst = createFx(fx.mod, { x: OX, y: OY, ...(seed !== undefined ? { seed } : {}) });
  const frames = [];
  for (let i = 0; i < n; i++) {
    LAB.only = null; LAB.rec = null;
    if (i > 0) inst.update(1 / fps);
    clearFrame(frame);
    LAB.only = only ? (t) => only.has(t || "") : null;
    LAB.rec = rec ? (t, k) => rec(i, t || "", k) : null;
    inst.draw(frame);
    frames.push({ color: frame.color.slice(), light: frame.light.slice() });
  }
  LAB.only = null; LAB.rec = null;
  return { W, H, n, frames };
}

const rgba = (W, H, f) => composite({ w: W, h: H, color: f.color, light: f.light }, new Uint8ClampedArray(W * H * 4), BG);

/** コマを並べた帯を PNG に。 */
function strip(W, H, list) {
  const cols = Math.min(COLS, list.length), rows = Math.ceil(list.length / cols), gap = 2;
  const w = cols * (W + gap) - gap, h = rows * (H + gap) - gap, out = new Uint8ClampedArray(w * h * 4);
  for (let i = 0; i < w * h; i++) out.set([5, 4, 6, 255], i * 4);
  list.forEach((px, k) => {
    const X = (k % cols) * (W + gap), Y = ((k / cols) | 0) * (H + gap);
    for (let y = 0; y < H; y++) out.set(px.subarray(y * W * 4, (y + 1) * W * 4), ((Y + y) * w + X) * 4);
  });
  const z = fxlib.upscale(out, w, h, ZOOM);
  return encodePNG(w * ZOOM, h * ZOOM, z);
}

for (const id of fxlib.resolveIds(ids, join(SRC, "fx"))) {
  const fx = await fxlib.loadEffect(id, join(SRC, "fx"));
  const fps = +(flags.fps ?? 12), seed = flags.seed !== undefined ? +flags.seed : undefined;
  const dir = join(OUT, id);
  mkdirSync(dir, { recursive: true });

  // 1) 全部描いて、 札を数える (札ごと・コマごとの画素の数。 口: 0 不透明 / 1 光 / 2 半透明)
  const stat = new Map(), order = [];
  const full = run(fx, fps, null, (i, t, k) => {
    let s = stat.get(t);
    if (!s) { stat.set(t, (s = { px: [], light: [], soft: [] })); order.push(t); }
    const a = k === 0 ? s.px : k === 1 ? s.light : s.soft;
    a[i] = (a[i] ?? 0) + 1;
  }, seed);
  const { W, H, n } = full;
  writeFileSync(join(dir, "full.png"), strip(W, H, full.frames.map((f) => rgba(W, H, f))));

  // 2) 札ごとに単独で
  const layers = {};
  for (const t of order) {
    layers[t] = run(fx, fps, new Set([t]), null, seed).frames;
    writeFileSync(join(dir, `layer_${safe(t)}.png`), strip(W, H, layers[t].map((f) => rgba(W, H, f))));
  }

  // 3) 出てきた順に足していく (描き順は元のまま)
  let acc = new Set();
  order.forEach((t, k) => {
    acc = new Set([...acc, t]);
    if (k === order.length - 1) return; // 最後は full と同じ
    const r = run(fx, fps, acc, null, seed).frames;
    writeFileSync(join(dir, `stack_${String(k + 1).padStart(2, "0")}_${safe(t)}.png`), strip(W, H, r.map((f) => rgba(W, H, f))));
  });

  // 4) 別々に描いた札を後から重ねる → 元と比べる
  let diffPx = 0, anyPx = 0, sumAbs = 0;
  const reco = [], diffs = [];
  for (let i = 0; i < n; i++) {
    const color = new Uint32Array(W * H), light = new Uint32Array(W * H);
    for (const t of order) {
      const f = layers[t][i];
      for (let j = 0; j < W * H; j++) {
        const c = f.color[j];
        if (c >>> 24) {
          const a = (c >>> 24) / 255;
          if (a >= 0.996) color[j] = c;
          else {
            // 半透明: 下の色へ混ぜる (本家の blendPx と同じ式)
            const d = color[j], da = (d >>> 24) / 255, oa = a + da * (1 - a), k = a / oa;
            const ch = (s) => ((((c >>> s) & 255) * k + ((d >>> s) & 255) * (1 - k)) & 255);
            color[j] = (((oa * 255) & 255) << 24) | (ch(16) << 16) | (ch(8) << 8) | ch(0);
          }
        }
        const l = f.light[j];
        if (l & 0xffffff) {
          const d = light[j];
          light[j] = (255 << 24) | (Math.min(255, ((d >>> 16) & 255) + ((l >>> 16) & 255)) << 16) | (Math.min(255, ((d >>> 8) & 255) + ((l >>> 8) & 255)) << 8) | Math.min(255, (d & 255) + (l & 255));
        }
      }
    }
    const a = rgba(W, H, { color, light }), b = rgba(W, H, full.frames[i]), dimg = new Uint8ClampedArray(W * H * 4);
    for (let j = 0; j < W * H; j++) {
      const e = Math.abs(a[j * 4] - b[j * 4]) + Math.abs(a[j * 4 + 1] - b[j * 4 + 1]) + Math.abs(a[j * 4 + 2] - b[j * 4 + 2]);
      const lit = (full.frames[i].color[j] >>> 24) || (full.frames[i].light[j] & 0xffffff);
      if (lit) anyPx++;
      if (e > 24) diffPx++;
      sumAbs += e / 3;
      const v = e > 24 ? 255 : Math.min(120, e * 3);
      dimg.set([v, v, v, 255], j * 4);
    }
    reco.push(a); diffs.push(dimg);
  }
  writeFileSync(join(dir, "recomposed.png"), strip(W, H, reco));
  writeFileSync(join(dir, "diff.png"), strip(W, H, diffs));

  // 5) 札の表
  const comps = order.map((t) => {
    const s = stat.get(t), act = [];
    let peak = 0, peakAt = 0, total = 0;
    for (let i = 0; i < n; i++) {
      const v = (s.px[i] ?? 0) + (s.soft[i] ?? 0) + (s.light[i] ?? 0) * 0.15;
      act.push(Math.round(v));
      total += v;
      if (v > peak) { peak = v; peakAt = i; }
    }
    const on = act.map((v, i) => (v > 0 ? i : -1)).filter((i) => i >= 0);
    const sum = (a) => a.reduce((x, y) => x + (y ?? 0), 0);
    return {
      tag: t, file: `layer_${safe(t)}.png`,
      first: +(on[0] / fps).toFixed(3), last: +(on[on.length - 1] / fps).toFixed(3), peak: +(peakAt / fps).toFixed(3),
      frames: on.length, opaquePx: sum(s.px), softPx: sum(s.soft), lightPx: sum(s.light),
      kind: sum(s.px) + sum(s.soft) === 0 ? "light" : sum(s.soft) > sum(s.px) ? "soft" : sum(s.light) > 0 ? "shape+light" : "shape",
      activity: act,
    };
  });
  const manifest = {
    id, name: fx.meta.name, desc: fx.meta.desc, size: fx.meta.size, origin: fx.meta.origin, duration: fx.meta.duration, loop: !!fx.meta.loop,
    fps, frames: n, zoom: ZOOM, cols: Math.min(COLS, n), annotated: comps.some((c) => c.tag && !/\//.test(c.tag) && c.tag !== "lights"),
    components: comps,
    recompose: { litPx: anyPx, differingPx: diffPx, differingPct: +(100 * diffPx / Math.max(1, anyPx)).toFixed(2), meanAbsPerPx: +(sumAbs / (W * H * n)).toFixed(3) },
  };
  writeFileSync(join(dir, "manifest.json"), JSON.stringify(manifest, null, 1));
  console.log(`${id}  札 ${order.length}  コマ ${n}  重ね直しの違い ${manifest.recompose.differingPct}%  ->  ${dir}`);
}
