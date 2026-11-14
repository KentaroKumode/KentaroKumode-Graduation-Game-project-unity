#!/usr/bin/env node
// dicebound/vesca-shatter の、 コマごとの破片の形 (geom()) を JSON に出す。 ゲーム (と見本) が、 凍った画面の写しを破片の形に貼って動かすための物。
//   node lab/shard_dump.mjs out.json [--fps 8] [--opts JSON]
// 出力: { fps, frames: [ null (割れる前) | [{ src: [[x,y]...], dst: [[x,y]...] }] (奥から順) ] }。 計算は効果と同じ 1/120 秒刻み。
import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { ROOT } from "./setup.mjs";

const [outFile, ...rest] = process.argv.slice(2), flags = {};
for (let i = 0; i < rest.length; i++) if (rest[i].startsWith("--")) flags[rest[i].slice(2)] = rest[++i];
const core = await import(pathToFileURL(join(ROOT, "src", "core", "index.js")).href);
const fx = await import(pathToFileURL(join(ROOT, "src", "fx", "dicebound", "vesca-shatter.js")).href);
const fps = +(flags.fps ?? 8), opts = core.resolveOptions(fx.meta, flags.opts ? JSON.parse(flags.opts) : {}), inst = fx.create(opts);
const n = Math.round(fx.meta.duration * fps) + 1, sub = Math.round(120 / fps), frames = [], r1 = (v) => Math.round(v * 10) / 10;
for (let i = 0; i < n; i++) {
  if (i > 0) for (let k = 0; k < sub; k++) inst.update(1 / 120);
  const g = inst.geom();
  frames.push(g && g.map((s) => ({ src: s.src.map(([x, y]) => [r1(x), r1(y)]), dst: s.dst.map(([x, y]) => [r1(x), r1(y)]) })));
}
writeFileSync(outFile, JSON.stringify({ fps, frames }));
console.log("shards", n, "frames ->", outFile, "first broken frame", frames.findIndex((f) => f));
