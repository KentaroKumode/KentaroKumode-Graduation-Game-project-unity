// 分解の実験室を組み立てる。
//
// pixelfx の src/ を records/_lab/ へ複写し、 描画に「札 (tag)」を付けられるように core を書き換える。
// 効果の側は、 lab/annotated/<分類>/<名前>.js があればそれで上書きする (元の効果に p.tag = "…" と ps.src = "…" を足した物)。
// 元の src/ は触らない。 書き換えは文字列の置き換えで、 当たらなければ止まる (本家が変わったら気づけるように)。
//
// 札の仕組み:
//   - Painter に tag (今描いている部品の名前) を持たせ、 画素を書く 3 つの口 (put = 不透明・add = 光・blendPx = 半透明) で
//     LAB.only(tag) が false なら書かない / LAB.rec(tag, 口) へ数える。
//   - 粒 (Particles) は出どころ src と種類で札を付ける: "<src>/<種類>" (例 "core/FIRE"・"blaze/EMBER")。
//     炎が消えて煙になる・燃える塊が尾を引く、 は出どころを引き継ぐ ("<src>>trail")。
//   - 光 (Lights)・破片 (Bits)・足もとの火 (Blazes) も同じ。
//   シミュレーションと乱数には触らないので、 札で描く物を絞っても、 動きは元と 1 画素も変わらない。

import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const HERE = dirname(fileURLToPath(import.meta.url));
export const ROOT = join(HERE, "..");
export const LABDIR = join(ROOT, "records", "_lab");

function patch(file, edits) {
  let s = readFileSync(file, "utf8").replace(/\r\n/g, "\n");
  for (const [from, to, n = 1] of edits) {
    const parts = s.split(from);
    if (parts.length - 1 !== n) throw new Error(`${file}: 置き換えの対象が ${parts.length - 1} か所 (想定 ${n}): ${from.slice(0, 60)}`);
    s = parts.join(to);
  }
  writeFileSync(file, s);
}

/** 実験室を作り直す (元の src より古ければ)。 戻り値は実験室の src の場所。 */
export function prepare({ force = false } = {}) {
  const src = join(ROOT, "src"), dst = join(LABDIR, "src"), stamp = join(LABDIR, "stamp.json");
  const newest = (d) => readdirSync(d, { withFileTypes: true }).reduce((m, e) => Math.max(m, e.isDirectory() ? newest(join(d, e.name)) : statSync(join(d, e.name)).mtimeMs), 0);
  const want = Math.max(newest(src), newest(HERE));
  if (!force && existsSync(stamp) && JSON.parse(readFileSync(stamp, "utf8")).at >= want) return dst;
  rmSync(LABDIR, { recursive: true, force: true });
  mkdirSync(LABDIR, { recursive: true });
  cpSync(src, dst, { recursive: true });

  // ---- Painter: 札と、 書く口の見張り
  patch(join(dst, "core", "painter.js"), [
    ["const call = (v, ...args) => (typeof v === \"function\" ? v(...args) : v);",
     "const call = (v, ...args) => (typeof v === \"function\" ? v(...args) : v);\n/** 実験室: only(tag) が false の札は描かない。 rec(tag, 口: 0 不透明 / 1 光 / 2 半透明) で数える。 */\nexport const LAB = { only: null, rec: null };"],
    ["this.fade = 1; this.lightK = 1; this.layer = \"front\";", "this.fade = 1; this.lightK = 1; this.layer = \"front\"; this.tag = \"\";"],
    ["    this.C[y * this.w + x] = c;\n  }\n  add(", "    if (LAB.only && !LAB.only(this.tag)) return;\n    if (LAB.rec) LAB.rec(this.tag, 0);\n    this.C[y * this.w + x] = c;\n  }\n  add("],
    ["    const i = y * this.w + x, d = this.L[i];\n", "    if (LAB.only && !LAB.only(this.tag)) return;\n    if (LAB.rec) LAB.rec(this.tag, 1);\n    const i = y * this.w + x, d = this.L[i];\n"],
    ["    const i = y * this.w + x, d = this.C[i], da = (d >>> 24) / 255, oa = a + da * (1 - a), k = a / oa;\n",
     "    if (LAB.only && !LAB.only(this.tag)) return;\n    if (LAB.rec) LAB.rec(this.tag, 2);\n    const i = y * this.w + x, d = this.C[i], da = (d >>> 24) / 255, oa = a + da * (1 - a), k = a / oa;\n"],
  ]);

  // ---- sim: 粒・光・破片・足もとの火に、 出どころと種類の札
  patch(join(dst, "core", "sim.js"), [
    ["    this.onLand = opts.onLand ?? null; // (x, y) when a BRAND hits the floor\n", "    this.onLand = opts.onLand ?? null; // (x, y) when a BRAND hits the floor\n    this.src = \"\";\n"],
    ["col: col ? hex(col) : 0, size, seed: this.R.next() * 100, delay, flag });", "col: col ? hex(col) : 0, size, seed: this.R.next() * 100, delay, flag, src: this.src });"],
    ["          this.spawn(P.SMOKE, p.x, p.y, p.vx * 0.6,", "          const _s = this.src; this.src = p.src;\n          this.spawn(P.SMOKE, p.x, p.y, p.vx * 0.6,"],
    ["0, big ? PF.BIG : 0);\n        }\n        this.list.splice(i, 1);", "0, big ? PF.BIG : 0);\n          this.src = _s;\n        }\n        this.list.splice(i, 1);"],
    ["          if (R.chance(0.9 * Math.min(1, dt * 60))) this.spawn(P.FIRE,", "          if (R.chance(0.9 * Math.min(1, dt * 60))) this._s = this.src, this.src = p.src + \">trail\", this.spawn(P.FIRE,"],
    ["R.chance(0.4) ? PF.SMOKES : 0);\n          const g = this.ground(p.x), ny = p.y + p.vy * dt;\n          p.x += p.vx * dt;\n          if (p.vy > 0 && p.y <= g && ny >= g) {\n            this.onLand?.(p.x, g);",
     "R.chance(0.4) ? PF.SMOKES : 0), this.src = this._s;\n          const g = this.ground(p.x), ny = p.y + p.vy * dt;\n          p.x += p.vx * dt;\n          if (p.vy > 0 && p.y <= g && ny >= g) {\n            this.onLand?.(p.x, g);"],
    ["      const k = q.life / q.max, x = q.x, y = q.y, sp = Math.hypot(q.vx, q.vy) || 1, ux = q.vx / sp, uy = q.vy / sp;\n",
     "      p.tag = (q.src || \"ps\") + \"/\" + KIND[q.k];\n      const k = q.life / q.max, x = q.x, y = q.y, sp = Math.hypot(q.vx, q.vy) || 1, ux = q.vx / sp, uy = q.vy / sp;\n"],
    ["    this.list.push({ x, y, life, max: life, acc: this.ps.R.next(), power });", "    this.list.push({ x, y, life, max: life, acc: this.ps.R.next(), power, src: this.src ?? \"blaze\" });"],
    ["        ps.spawn(P.FIRE, b.x + R.range(-1.5, 1.5),", "        ps.src = b.src;\n        ps.spawn(P.FIRE, b.x + R.range(-1.5, 1.5),"],
    ["      const k = b.life / b.max;\n      p.px(b.x, b.y, k > 0.4", "      const k = b.life / b.max;\n      p.tag = b.src + \"/foot\";\n      p.px(b.x, b.y, k > 0.4"],
    ["life, max: life });\n  }\n  get count() {\n    return this.list.length;\n  }\n  update(dt) {\n    for (let i = this.list.length - 1; i >= 0; i--) {\n      const b = this.list[i];",
     "life, max: life, src: b.src ?? this.src ?? \"\" });\n  }\n  get count() {\n    return this.list.length;\n  }\n  update(dt) {\n    for (let i = this.list.length - 1; i >= 0; i--) {\n      const b = this.list[i];"],
    ["      const a = Math.min(1, b.life / 0.4), ca = Math.cos(b.a), sa = Math.sin(b.a);\n", "      p.tag = (b.src || \"bits\") + \"/\" + [\"SHARD\", \"CHUNK\", \"DRIP\"][b.kind];\n      const a = Math.min(1, b.life / 0.4), ca = Math.cos(b.a), sa = Math.sin(b.a);\n"],
    ["    this.list.push({ x, y, r, color: hex(color), k, life, max: life });", "    this.list.push({ x, y, r, color: hex(color), k, life, max: life, src: this.src ?? \"\" });"],
    ["      const k = l.life / l.max;\n      p.glow(l.x, l.y,", "      const k = l.life / l.max;\n      p.tag = l.src || \"lights\";\n      p.glow(l.x, l.y,"],
  ]);
  // 描いた後は札を元へ戻す (効果の側が次に札を付け忘れても、 粒の札を引きずらない)。 足もとの火が出す粒は、 出どころを足もとの火に
  writeFileSync(join(dst, "core", "sim.js"), readFileSync(join(dst, "core", "sim.js"), "utf8") + `
const KIND = Object.fromEntries(Object.entries(P).map(([k, v]) => [v, k]));
for (const [cls, name] of [[Particles, "draw"], [Lights, "draw"], [Bits, "draw"], [Blazes, "drawBack"]]) {
  const f = cls.prototype[name];
  cls.prototype[name] = function (p, ...a) { const t = p.tag; try { return f.call(this, p, ...a); } finally { p.tag = t; } };
}
{
  const f = Blazes.prototype.update;
  Blazes.prototype.update = function (dt) { const s = this.ps.src; try { return f.call(this, dt); } finally { this.ps.src = s; } };
}
`);

  // ---- 札を付けた効果で上書き
  const ann = join(HERE, "annotated");
  const walk = (d, rel = "") => existsSync(d) ? readdirSync(d, { withFileTypes: true }).flatMap((e) => e.isDirectory() ? walk(join(d, e.name), join(rel, e.name)) : [join(rel, e.name)]) : [];
  const over = walk(ann).filter((f) => f.endsWith(".js"));
  for (const f of over) cpSync(join(ann, f), join(dst, "fx", f));
  writeFileSync(stamp, JSON.stringify({ at: Date.now(), annotated: over }));
  return dst;
}
