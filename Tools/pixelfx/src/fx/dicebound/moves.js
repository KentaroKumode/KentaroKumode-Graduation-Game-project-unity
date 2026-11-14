// DICE BOUND 用 (2026-11-14): ボスの、 必殺以外の技 (たたき台・第 2 版)。 move で出し分ける。 画面全体 (480×270)・原点は左上・1 回きり。
// 毎ターン出るので短い (天与の 3 つだけ 1.75 秒と長く、 豪華)。 暗転や白飛びは使わない (それは大技だけ)。 形は _kit.js の部品で、 この画面の大きさで描く。
//   ヴェスカ:   relic-strike (遺物・攻撃) / relic-guard (遺物・防御) / tenyo-sword (天与の剣) / tenyo-shield (天与の盾) / tenyo-ring (天与の指輪)
//               (遺物・妨害は dicebound/vesca-hex ── shadow/hex-sigil を大きくした物)
//   灰燼の王:   king-attack (通常攻撃) / king-regen (灰の再生)
//   審判官:     judge-flame (審判の炎)
//   シュヴァリエ: chev-thrust (通常攻撃) / chev-stance (構え)
// 第 3 版 (2026-11-14): 斬撃を melee/sword-slash・spear-sweep の部品に替えた (自前の弧は「ベースの斬撃がダメ」)・灰の再生に崩れ落ちる古い灰・シュヴァリエの突きを三連にして鋭く。
// 作者の指摘 (第 1 版 → 第 2 版):
//   遺物・攻撃 = 遺物は物の武器なので、 光の槍のような謎の力ではなく、 おとなしく斬撃。 天与の剣 = その強化版・金色。 天与は 3 つとも金色・長く・豪華に。
//   天与の指輪 = ぺらくて地味だった → 厚い輪・宝石・光の壁・鎖・締まる内輪。
//   王の通常攻撃 = 炎が均等すぎた → 高さも位置も間もばらばらに、 暴れさせる。 当たりは星形をやめて、 火の塊の炸裂。
//   灰の再生 = 足もとの炎ではなく、 毒や出血のように「体の内側から漏れ出る光」。
//   審判の炎 = 光を太く・盤の炎は無し・当たりは星形 (四角に見えた) をやめて、 holy/smite の着弾 (閃光・床を走る輪・光の棘・光って冷めるひび・欠片)。
//   シュヴァリエの突き = 当たりの×印 (星形) をやめて、 横に細い閃光。 構えは変えない。
// The shield bubble is this repository's holy/divine-shield, drawn as is; the shapes follow its slash, smite, ground-slam and fire effects
// (after Sprite Fusion's open-source effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, clamp, ease, noise, rand, sparkle, TAU, WHITE } from "../../core/index.js";
import { swing, through, slashEnd, shavings, onSwing, travel } from "../melee/_lib.js";
import * as SHIELD from "../holy/divine-shield.js";
import { flameTongue, LAYERS, BIG_LAYERS } from "../fire/_lib.js";
import { GOLD } from "../holy/_lib.js";
import { VIOLET } from "./vesca-charge.js";
import { STEEL, glint } from "./chevalier-charge.js";
import { W, H, CHEST, FOOT, BOARD, beam, oval, crescent, slash, slashAt, lens, diamond, wash, onces, bladeSlash } from "./_kit.js";

const FIRE = ["#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#fff3a6", "#ffffff"];
const PAL = { relic: VIOLET, tenyo: GOLD, king: FIRE, judge: GOLD, chev: STEEL };
const ASHC = ["#a39cab", "#7b7484", "#5c5664", "#c8c0cc"];

export const meta = {
  name: "Boss moves (DICE BOUND)",
  desc: "The bosses' ordinary moves, drawn at the game's screen size: Vesca's relics (a slash, a shield) and her three divine gifts (sword, shield, ring) in gold, the Ash King's wild fire cleave and the light leaking out of him as he regenerates, the Judge's falling light, the Chevalier's thrust and stance.",
  tags: ["dicebound", "boss", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 2.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { move: "relic-strike", palette: null, seed: 3 },
};

export function create(o) {
  const move = o.move, C = fit(o.palette ?? PAL[move.split("-")[0]], 8), HOT = [...C].reverse(), R = new Rng(o.seed), once = onces();
  const ps = new Particles({ rng: R, sparks: [C[7], C[6], C[5], C[4]] });
  const ash = new Particles({ rng: R, ground: () => 199 });   // 崩れ落ちる灰 (床で跳ねて止まる)
  const [bx, by] = BOARD, [cx, cy] = CHEST, [fx, fy] = FOOT, UP = -Math.PI / 2;
  let t = 0;
  const N = (rate, n, off = 0) => noise(t * rate + off, n);
  const sp = (n, x, y, a0, spread, v0, v1, l0, l1) => { for (let i = 0; i < n; i++) { const a = a0 + R.range(-spread, spread), v = R.range(v0, v1); ps.spawn(P.SPARK, x(), y(), Math.cos(a) * v, Math.sin(a) * v, R.range(l0, l1)); } };
  // 斬撃に沿って、 両側へ火花・欠片・きらめきを出す
  const along = (S, n, v0, v1) => { for (let i = 0; i < n; i++) { const [x, y] = slashAt(S[0], S[1], S[2], S[3], S[4], R.range(0.08, 0.92)), a = Math.atan2(S[3] - S[1], S[2] - S[0]) + R.sign() * Math.PI / 2 + R.range(-0.6, 0.6), v = R.range(v0, v1); ps.spawn(P.SPARK, x, y, Math.cos(a) * v, Math.sin(a) * v - 40, R.range(0.2, 0.5)); } };
  // 床を走るひび: 盤の中心から左右へ、 ぎざぎざの線
  const cracks = Array.from({ length: 9 }, (_, i) => { let x = bx, y = by + R.range(-2, 4), a = (i % 2 ? 0 : Math.PI) + R.range(-0.35, 0.35); const pts = [[x, y]]; for (let s = 0; s < 22; s++) { a += R.range(-0.5, 0.5); x += Math.cos(a) * R.range(6, 13); y += Math.sin(a) * R.range(1.5, 4); y = clamp(y, 228, 266); pts.push([x, y]); } return pts; });
  // 王の炎の舌: 位置も高さも太さも出る間も、 ばらばら (高さは 2 乗でばらつかせる ── 少数だけとても高い)
  const tongues = Array.from({ length: 26 }, () => ({ u: R.range(0.04, 0.96), h: 14 + 86 * R.range(0, 1) ** 2.2, w: R.range(0.7, 1.5), n: R.int(1, 900), d: R.range(0, 0.16), life: R.range(0.3, 0.75), sway: R.range(-4, 4) }));
  // 王の体の中を走る光の筋 (灰の再生): 胸から外へ、 体の幅の中で
  const veins = Array.from({ length: 10 }, (_, i) => { let x = cx + R.range(-6, 6), y = cy + 6 + R.range(-8, 8), a = (i / 10) * TAU + R.range(-0.3, 0.3); const pts = [[x, y]]; for (let s = 0; s < 9; s++) { a += R.range(-0.6, 0.6); x = clamp(x + Math.cos(a) * R.range(4, 9), 196, 284); y = clamp(y + Math.sin(a) * R.range(5, 11), 84, 192); pts.push([x, y]); } return pts; });
  // 斬撃 (melee/sword-slash・spear-sweep の作り: 傾いた円の弧を、 頭が一気に走り、 尾が遅れて追う。 頭のすぐ後ろが一番太く、 外の縁が白く、 時間と尾へ向けて色が冷める。
  //   一拍遅れの細い残像・通った跡の点線・切っ先のきらめき・頭に乗る光・切っ先から飛ぶ火花・弧の外へ飛ぶ削り屑)。 through(通る点, 向き, 回る向き, 半径, 弧の長さ, 通る点が弧の何割目か)
  const RAMP = fit(o.palette ?? PAL[move.split("-")[0]], 7);
  const mk = (x, y, th, dir, Rr, span, at, Wd, t0, sweep = 0.07) => { const A = through(x, y, th, dir, Rr, span, at, { W: Wd, t0, sweep, hold: 0.12, trail: 0.5, linger: 0.75, ramp: RAMP }); return { A, E: { ...A, R: Rr - Wd * 0.75, W: Wd * 0.38, t0: t0 + 0.017, hold: 0.08, trail: 0.42, linger: 0.75, ramp: RAMP.slice(0, 6) }, end: slashEnd(A) }; };
  // 残像 (E): 元の sword-slash では、 本体の一番太い所の内側 3/4 にあって、 本体の下に描かれる ── 太い所では隠れ、 細くなる頭と尾でだけ内側に覗く (刃の内側の光)。
  //   半径の割合 (元は 33/42) で置くと、 大きい斬撃では 70 ドットも離れて「2 つ目の斬撃」に見えた (作者の指摘)。 太さの割合で置く。
  // 残る時間: 元は 30 コマ/秒で、 斬撃が 10 コマほど残って冷め、 縁の点線がさらに残る。 1 秒 8 コマで同じ長さ (0.39 秒) だと 3 コマで消える (作者: 一瞬で消える) ── hold / trail を約 2 倍・点線 (linger) を 0.75 秒に
  // 遺物・攻撃: 左上から右下へ、 盤を通る大きな一太刀
  // 遺物・攻撃: 元の sword-slash と同じ形 ── 傾いた楕円 (縦 0.6・傾き −0.38) の上を、 左の細い尾から、 上をまわって、 右下の切っ先まで 200 度。 半径だけ 42 → 165 に。
  //   三日月に見えるのは、 弧が大きくまわるから。 大きな半径の短い弧では、 反りの無い「毛束」になった。 残像 (内側の細い 2 本目) は出さない ── この大きさでは二俣に見える (作者の指摘)
  const moon = () => { const A = swing({ cx: 232, cy: 196, R: 165, sy: 0.6, rot: -0.38, a0: -2.55, dir: 1, span: 3.5, W: 40, t0: 0, sweep: 0.07, hold: 0.12, trail: 0.5, linger: 0.75, ramp: RAMP }); return { A, E: null, end: slashEnd(A) }; };
  const CUTS = move === "relic-strike" ? [moon()]
    // 天与の剣: ×に交わる 2 太刀と、 盤を横に払う低い一太刀 (spear-sweep の「低く平たい払い」)
    : move === "tenyo-sword" ? [mk(240, 192, 0.52, -1, 700, 0.74, 0.55, 32, 0), mk(240, 192, Math.PI - 0.52, 1, 700, 0.74, 0.55, 32, 0.18),   /* 半径を大きく・弧を短く (小さい半径で長い弧だと、 斜めの一太刀ではなく U 字になる) */ mk(240, 246, 0, -1, 1100, 0.44, 0.5, 26, 0.38, 0.09)] : [];
  const THR = [[0, 196, false], [0.09, 286, false], [0.19, 240, true]];   // 突き: [始まる秒, 届く x, 決めの一突きか]
  const gems = Array.from({ length: 8 }, (_, i) => (i / 8) * TAU + 0.2);
  const curtain = Array.from({ length: 46 }, () => ({ g: R.range(0, TAU), h: R.range(34, 96), d: R.range(0, 0.1) }));
  return {
    update(dt) {
      t += dt;
      CUTS.forEach((c, i) => {
        const A = c.A, big = move === "tenyo-sword", u = t - A.t0;
        // 刃の風が、 通った所の床から塵と欠片を巻き上げる (spear-sweep の wake)。 盤の高さを通る間だけ
        if (u > 0 && u < A.sweep * 1.2) { const U = A.span * ease.outExpo(clamp(u / A.sweep)), U0 = A.span * ease.outExpo(clamp((u - dt) / A.sweep)); for (let q = U0; q < U; q += 0.012) { const tr = travel(A, q); if (tr.y < 205 || tr.x < 0 || tr.x > W) continue; if (R.chance(0.7)) ps.spawn(P.FLAKE, tr.x, tr.y, tr.dx * R.range(60, 160) + R.range(-30, 30), -R.range(60, 220), R.range(0.5, 1), R.pick([C[4], C[5], C[3]]), R.range(1, 2.2)); } }
        once("land" + i, u >= A.sweep, () => {
          // 切っ先から先へ飛ぶ火花・弧の外へ飛ぶ削り屑 (sword-slash)・きらめき
          for (let k = 0; k < (big ? 46 : 30); k++) { const a = Math.atan2(c.end.dy, c.end.dx) + R.range(-0.5, 0.5), v = R.range(180, 560); ps.spawn(P.SPARK, c.end.x, c.end.y, Math.cos(a) * v, Math.sin(a) * v, R.range(0.15, 0.4)); }
          shavings(ps, R, A, big ? 90 : 60, [C[5], C[6]], 0.15);
          for (let k = 0; k < (big ? 30 : 16); k++) { const [x, y] = onSwing(A, A.R, A.span * R.range(0.1, 0.95)); ps.spawn(P.STAR, x, y, 0, -R.range(5, 30), R.range(0.3, 0.8), R.chance(0.6) ? WHITE : C[6], R.range(1.4, 3), R.range(0, 0.25)); }
        });
      });
      if (move === "tenyo-sword" && t > 0.5 && t < 1.25 && R.chance(70 * dt)) ps.spawn(P.MOTE, R.range(30, 450), 250 + R.range(-6, 6), R.range(-6, 6), -R.range(50, 140), R.range(0.6, 1.1), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.8));   // 立ちのぼる金の粒 (blessing の出し方)
      if (move === "tenyo-ring") {
        once("lock", t >= 0.26, () => { for (const g of gems) sp(12, () => bx + Math.cos(g) * 212, () => by + Math.sin(g) * 28, UP, 1.4, 80, 300, 0.2, 0.5); for (let i = 0; i < 30; i++) ps.spawn(P.STAR, bx + R.range(-210, 210), by + R.range(-28, 28), 0, -R.range(5, 20), R.range(0.3, 0.8), WHITE, R.range(1.4, 3), R.range(0, 0.4)); });
        once("tight", t >= 0.6, () => { sp(60, () => bx + R.range(-150, 150), () => by + R.range(-16, 16), UP, 0.8, 60, 240, 0.2, 0.5); });
        if (t > 0.26 && t < 1.3 && R.chance(60 * dt)) { const g = R.range(0, TAU); ps.spawn(P.MOTE, bx + Math.cos(g) * 212, by + Math.sin(g) * 28, 0, -R.range(40, 120), R.range(0.5, 1), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.6)); }
      }
      if (move === "tenyo-shield" || move === "relic-guard") {
        const big = move === "tenyo-shield", end = big ? 1.45 : 0.9;
        if (t < end && R.chance((big ? 70 : 24) * dt)) ps.spawn(P.MOTE, fx + R.range(-80, 80), fy + R.range(-4, 6), R.range(-6, 6), -R.range(40, 120), R.range(0.5, 0.9), R.chance(0.5) ? C[7] : C[6], R.range(0.8, 1.8));
        if (big && t < 1.3 && R.chance(22 * dt)) { const g = R.range(0, TAU); ps.spawn(P.STAR, fx + Math.cos(g) * 94, fy - 66 + Math.sin(g) * 94, 0, -6, R.range(0.3, 0.7), WHITE, R.range(1.4, 3)); }
      }
      if (move === "king-attack") {
        // 斬撃が通った所から、 火の塊・残り火・灰 (fire-ring の shed + meteor の burst の出し方)。 量は場所でむらを付ける
        const a0 = 2.35, a1 = 0.8, pr = clamp(t / 0.18), prev = clamp((t - dt) / 0.18);
        for (let u = prev; u < pr; u += 0.008) {
          const g = a0 + (a1 - a0) * u, x = 240 + Math.cos(g) * 285, y = -45 + Math.sin(g) * 285, lump = 0.3 + 1.4 * Math.abs(noise(u * 9, 77));
          if (R.chance(0.7 * lump)) ps.spawn(P.FIRE, x + R.range(-8, 8), y + R.range(-6, 6), R.range(-50, 50), -R.range(20, 190), R.range(0.25, 0.8), 0, R.range(4, 16), R.range(0, 0.08));
          if (R.chance(0.9 * lump)) ps.spawn(P.EMBER, x, y, R.range(-140, 140), -R.range(60, 380), R.range(0.5, 1.2), 0, R.chance(0.35) ? 2 : 1);
          if (R.chance(0.5)) ps.spawn(P.FLAKE, x, y, R.range(-70, 70), -R.range(40, 200), R.range(0.7, 1.3), R.pick(ASHC), R.range(1, 2));
        }
        once("hit", t >= 0.11, () => {
          // 当たり: 火の塊が盤から爆ぜる (fireball / meteor の炸裂の出し方)。 大きい塊 + 四方へ飛ぶ残り火 + 火花
          for (let i = 0; i < 34; i++) { const a = UP + R.range(-1.4, 1.4), v = R.range(60, 300); ps.spawn(P.FIRE, bx + R.range(-30, 30), by - R.range(0, 8), Math.cos(a) * v, Math.sin(a) * v, R.range(0.3, 0.7), 0, R.range(9, 22), R.range(0, 0.06)); }
          for (let i = 0; i < 70; i++) { const a = R.range(Math.PI, TAU), v = R.range(150, 560); ps.spawn(P.EMBER, bx + R.range(-20, 20), by, Math.cos(a) * v, Math.sin(a) * v, R.range(0.5, 1.1), 0, R.chance(0.4) ? 2 : 1); }
          sp(60, () => bx + R.range(-30, 30), () => by, UP, 1.4, 160, 520, 0.2, 0.5);
          for (let i = 0; i < 10; i++) ps.spawn(P.SMOKE, bx + R.range(-150, 150), by - R.range(0, 16), R.range(-30, 30), -R.range(20, 60), R.range(0.6, 1.1), 0, R.range(9, 16), R.range(0.12, 0.35));
        });
        for (const [key, at, u] of [["p1", 0.2, 0.22], ["p2", 0.27, 0.74], ["p3", 0.36, 0.45]]) once(key, t >= at, () => { const g = a0 + (a1 - a0) * u, x = 240 + Math.cos(g) * 285, y = -45 + Math.sin(g) * 285; for (let i = 0; i < 12; i++) { const a = UP + R.range(-1.3, 1.3), v = R.range(60, 240); ps.spawn(P.FIRE, x, y, Math.cos(a) * v, Math.sin(a) * v, R.range(0.25, 0.55), 0, R.range(6, 14)); } for (let i = 0; i < 24; i++) { const a = R.range(Math.PI, TAU), v = R.range(100, 380); ps.spawn(P.EMBER, x, y, Math.cos(a) * v, Math.sin(a) * v, R.range(0.4, 0.9), 0, 1); } });   // 遅れて爆ぜる所が 3 つ
      }
      if (move === "king-regen") {
        // 体の縁から、 光の粒がにじみ出て、 ゆっくり漂う (status/poisoned の汗・bleeding の雫の、 向きを外と上にした物)
        if (t > 0.12 && t < 1.0 && R.chance(90 * dt)) { const side = R.int(0, 3), x = side === 0 ? 192 : side === 1 ? 288 : R.range(192, 288), y = side < 2 ? R.range(84, 190) : side === 2 ? 80 : R.range(120, 190), out = side === 0 ? -1 : side === 1 ? 1 : R.sign() * 0.3; ps.spawn(R.chance(0.6) ? P.EMBER : P.MOTE, x, y, out * R.range(8, 40), -R.range(10, 60), R.range(0.6, 1.1), R.chance(0.5) ? C[6] : C[5], R.chance(0.3) ? 2 : 1); }
        // 古い灰が、 体の表から剥がれて崩れ落ちる (作者の指定)。 脈を打つたびにまとめて剥がれ、 その間もぱらぱら落ちる。 床で小さく跳ねて、 灰の煙が立つ
        const crumble = (n) => { for (let i = 0; i < n; i++) { const x = R.range(194, 286), y = R.range(80, 176); ash.spawn(R.chance(0.45) ? P.GRIT : P.FLAKE, x, y, R.range(-26, 26), R.range(-20, 30), R.range(0.7, 1.3), R.pick(ASHC), R.chance(0.3) ? 2 : 1, R.range(0, 0.15)); } };
        if (t > 0.1 && t < 1.0 && R.chance(50 * dt)) crumble(1);
        for (const [key, at] of [["c1", 0.2], ["c2", 0.62]]) once(key, t >= at, () => { crumble(46); for (let i = 0; i < 9; i++) ash.spawn(P.SMOKE, fx + R.range(-56, 56), 197 - R.range(0, 4), R.range(-16, 16), -R.range(4, 14), R.range(0.7, 1.1), "#5c5664", R.range(4, 8), R.range(0.35, 0.6)); });
        for (const [key, at] of [["b1", 0.2], ["b2", 0.62]]) once(key, t >= at, () => { for (let i = 0; i < 26; i++) { const g = R.range(0, TAU), v = R.range(30, 110); ps.spawn(P.EMBER, cx + Math.cos(g) * 30, cy + 6 + Math.sin(g) * 40, Math.cos(g) * v, Math.sin(g) * v - 20, R.range(0.4, 0.8), 0, 1); } });
      }
      if (move === "judge-flame") once("hit", t >= 0.08, () => {
        sp(70, () => bx + R.range(-24, 24), () => by, UP, 1.3, 120, 420, 0.25, 0.6);
        for (let i = 0; i < 30; i++) ps.spawn(P.FLAKE, bx + R.range(-80, 80), by - R.range(0, 8), R.range(-90, 90), -R.range(120, 320), R.range(0.6, 1.1), R.pick([C[4], C[5], C[3]]), R.range(1, 2.2));   // 飛ぶ欠片 (smite の stone chips)
        for (let i = 0; i < 26; i++) ps.spawn(P.MOTE, bx + R.range(-110, 110), by - R.range(0, 10), R.range(-8, 8), -R.range(40, 140), R.range(0.5, 1), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.8), R.range(0, 0.35));
        for (let i = 0; i < 14; i++) ps.spawn(P.STAR, bx + R.range(-140, 140), by - R.range(10, 80), 0, -R.range(5, 20), R.range(0.3, 0.7), WHITE, R.range(1.2, 2.6), R.range(0.1, 0.5));
      });
      // シュヴァリエの突き: 三連 (左・右・正面)。 当たるたびに、 鋭い火花が細い扇で上へ抜け、 左右へ走る
      if (move === "chev-thrust") THR.forEach(([at, x, big], i) => once("hit" + i, t >= at + 0.04, () => { sp(big ? 34 : 14, () => x, () => by, UP, 0.35, 260, 760, 0.1, 0.28); sp(big ? 22 : 8, () => x, () => by, 0, 0.08, 340, 820, 0.08, 0.22); sp(big ? 22 : 8, () => x, () => by, Math.PI, 0.08, 340, 820, 0.08, 0.22); if (big) for (let k = 0; k < 10; k++) ps.spawn(P.STAR, x + R.range(-90, 90), by - R.range(0, 60), 0, 0, R.range(0.15, 0.4), WHITE, R.range(1.5, 3), R.range(0, 0.12)); }));
      if (move === "chev-stance") once("ring", t >= 0.22, () => { sp(30, () => cx, () => cy, 0, Math.PI, 160, 420, 0.12, 0.3); for (let i = 0; i < 8; i++) ps.spawn(P.STAR, cx + R.range(-60, 60), cy + R.range(-60, 60), 0, 0, R.range(0.2, 0.45), WHITE, R.range(1.5, 3), R.range(0, 0.15)); });
      ps.update(dt);
      ash.update(dt);
    },
    draw(p) {
      const rings = (x, y, list, a, rx, ry, dur = 0.45) => { for (const [d, s] of list) { const u = (a - d) / dur; if (u > 0 && u < 1) { const e = ease.outCubic(u); oval(p, x, y, 24 + rx * e * s, 5 + ry * e * s, u < 0.3 ? 4 : 2, u < 0.25 ? WHITE : u < 0.6 ? C[6] : C[5], 1.1 - u); } } };
      const crackle = (a, grow, hot, life) => { for (const pts of cracks) for (let i = 1; i < pts.length * clamp(a / grow); i++) p.line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], a < hot ? WHITE : a < hot * 2 ? C[6] : C[4], 1 - clamp((a - life * 0.6) / (life * 0.4))); };
      if (move === "relic-strike" || move === "tenyo-sword") {
        const big = move === "tenyo-sword";
        for (const c of CUTS) {
          const A = c.A, u = t - A.t0;
          if (u < 0) continue;
          if (c.E) bladeSlash(p, c.E, t);
          bladeSlash(p, A, t);
          // 切っ先のきらめき・頭に乗る光
          const g = u - A.sweep * 0.6;
          if (g > 0 && g < 0.24) sparkle(p, c.end.x, c.end.y, (big ? 30 : 20) * ease.outBack(Math.min(1, g / 0.05)) * (1 - g / 0.24), RAMP[5], RAMP[3], 0.7);
          if (u < 0.3) { const [x, y] = onSwing(A, A.R - 6, A.span * ease.outExpo(clamp(u / A.sweep))); p.glow(x, y, big ? 110 : 80, RAMP[3], 0.5 * (1 - u / 0.3)); }
        }
        if (!big && t >= 0.07) wash(p, C[5], 30, 196, 450, 268, 0.45 * (1 - clamp((t - 0.07) / 0.6)));
        if (big && t >= 0.47) {
          // 3 太刀目が払い切ったあと: 床を走る輪・交点から立つ光の柱・床のひび
          const a = t - 0.47, k = 1 - clamp((a - 0.5) / 0.6);
          rings(bx, by, [[0, 1], [0.08, 0.72], [0.16, 0.46]], a, 216, 32);
          if (k > 0) { beam(p, C, bx, by, bx, by - 250 * ease.outCubic(clamp(a / 0.2)), 46 * k + 2, 18 * k + 1, 1); for (const dx of [-70, 70, -140, 140]) beam(p, C, bx + dx, by, bx + dx, by - (150 - Math.abs(dx) * 0.4) * ease.outCubic(clamp((a - 0.06) / 0.2)), 12 * k + 1, 3 * k + 1, k); }
          crackle(a, 0.15, 0.3, 1.0);
          wash(p, C[6], 20, 180, 460, 268, 0.55 * k);
        }
      } else if (move === "relic-guard" || move === "tenyo-shield") {
        const big = move === "tenyo-shield", r = big ? 88 : 72, end = big ? 1.7 : 1.2, k = Math.min(clamp(t / 0.15), clamp((end - t) / 0.25)), f = p.fade;
        const so = { palette: big ? [C[2], C[3], C[4], C[5], C[6], C[7], C[7], C[7]] : (o.palette ?? VIOLET), radius: r, seed: 8 }, grow = ease.outBack(clamp(t / 0.2));
        // 足もとの輪 (広がって定まる)。 天与は、 まわりに光の柱が回り、 傾いた輪が 2 本回る
        oval(p, fx, fy + 2, r * 1.05 * grow, r * 0.27 * grow, big ? 3 : 2, big ? WHITE : C[6], k);
        if (big) { oval(p, fx, fy + 2, r * 1.3 * grow, r * 0.34 * grow, 1, C[5], k); for (let i = 0; i < 8; i++) { const g = (i / 8) * TAU + t * 1.2, x = fx + Math.cos(g) * r * 1.05, y = fy + 2 + Math.sin(g) * r * 0.27; if (Math.sin(g) < 0.2) beam(p, C, x, y, x, y - 170 * clamp(t / 0.3) * k, 6, 1, k); } }
        p.fade = f * k;
        const tx = p.tx, ty = p.ty; p.tx = tx + fx; p.ty = ty + fy - 44;
        SHIELD.drawBack(p, t, so); SHIELD.draw(p, t, so);
        p.tx = tx; p.ty = ty; p.fade = f;
        if (big) {
          for (let i = 0; i < 8; i++) { const g = (i / 8) * TAU + t * 1.2, x = fx + Math.cos(g) * r * 1.05, y = fy + 2 + Math.sin(g) * r * 0.27; if (Math.sin(g) >= 0.2) beam(p, C, x, y, x, y - 170 * clamp(t / 0.3) * k, 6, 1, k); }
          for (const [tilt, sgn] of [[0.35, 1], [0.6, -1], [0.18, 1]]) oval(p, cx, cy, r * 1.22, r * tilt, 2, WHITE, k, sgn * t * 3, sgn * t * 3 + TAU * 0.7);
          p.glow(cx, cy, r * 1.3, WHITE, 0.35 * k);
          if (t < 0.3) lens(p, C, cx, cy, 130 * (1 - t / 0.3), 20 * (1 - t / 0.3), 1);
        }
      } else if (move === "tenyo-ring") {
        // 厚い金の輪が、 残像を引いて降りて来て (0.26 秒)、 盤を囲んで締まる。 宝石が 8 つ光り、 輪から光の壁が立ち、 鎖が上へ張る。 0.6 秒で内側の輪がもう一段締まる
        const dn = ease.inQuad(clamp(t / 0.26)), k = 1 - clamp((t - 1.4) / 0.3), y = 96 + (by - 96) * dn, sq = 0.45 + 0.55 * dn, hot = 1 - clamp((t - 0.26) / 0.3), f = p.fade;
        if (k > 0) {
          p.fade = f * k;
          if (t < 0.3) for (let i = 1; i <= 4; i++) oval(p, bx, y - i * 22 * (1 - dn * 0.6), 216, 30 * sq, 1, i < 3 ? C[5] : C[3], 1 - i / 5);   // 残像
          // 光の壁: 輪の奥の半分 (先に描く)
          const wall = clamp((t - 0.26) / 0.12) * (1 - clamp((t - 0.8) / 0.5));
          if (wall > 0) for (const c of curtain) if (Math.sin(c.g) < 0) { const x = bx + Math.cos(c.g) * 214, yy = by + Math.sin(c.g) * 29; beam(p, C, x, yy, x, yy - c.h * wall * (0.7 + 0.3 * rand(Math.floor(t * 8), c.h | 0)), 3, 1, wall); }
          // 輪の本体: 上の縁が白く、 胴が金、 下の縁が暗い (厚みが見える)
          oval(p, bx, y + 5, 216, 30 * sq, 3, C[2]); oval(p, bx, y + 2, 216, 30 * sq, 5, C[4]); oval(p, bx, y, 216, 30 * sq, 4, t >= 0.26 && hot > 0.4 ? WHITE : C[5]); oval(p, bx, y - 2, 216, 30 * sq, 1, WHITE);
          oval(p, bx, y, 200, 24 * sq, 1, C[4]);
          // 輪の上を回る文様
          for (let i = 0; i < 36; i++) { const g = (i / 36) * TAU + t * 0.8; p.dot(bx + Math.cos(g) * 208, y + Math.sin(g) * 27 * sq, i % 3 ? C[3] : WHITE); }
          // 宝石
          for (const g of gems) { const x = bx + Math.cos(g) * 216, yy = y + Math.sin(g) * 30 * sq; diamond(p, x, yy, 4, t >= 0.26 ? WHITE : C[6], C[3]); p.glow(x, yy, 9 + 8 * hot, C[6], 0.5 + 0.4 * hot); }
          if (t >= 0.26) {
            const a = t - 0.26;
            // 内側の輪: 0.6 秒で、 もう一段締まる
            const tight = ease.inOutCubic ? ease.inOutCubic(clamp((t - 0.5) / 0.14)) : clamp((t - 0.5) / 0.14), rx = 190 - 44 * tight;
            oval(p, bx, by, rx, 22 - 4 * tight, 3, t > 0.6 && t < 0.75 ? WHITE : C[6]); oval(p, bx, by + 2, rx, 22 - 4 * tight, 1, C[3]);
            // 鎖: 4 本、 輪から上へ (環を 1 つずつ)
            for (let i = 0; i < 4; i++) { const g = gems[i * 2], x = bx + Math.cos(g) * 216, yy = by + Math.sin(g) * 30; for (let s = 0; s < 30 * clamp(a / 0.2); s++) { const ly = yy - 8 - s * 8, wob = Math.sin(s * 0.5 + t * 4 + i) * 2; if (ly < -6) break; oval(p, x + wob, ly, s % 2 ? 2 : 3.5, s % 2 ? 4 : 2.5, 1, s % 2 ? C[5] : C[6]); } }
            // 光の壁: 手前の半分
            if (wall > 0) for (const c of curtain) if (Math.sin(c.g) >= 0) { const x = bx + Math.cos(c.g) * 214, yy = by + Math.sin(c.g) * 29; beam(p, C, x, yy, x, yy - c.h * wall * (0.7 + 0.3 * rand(Math.floor(t * 8), c.h | 0)), 3, 1, wall); }
            if (a < 0.25) lens(p, C, bx, by, 220 * (1 - a / 0.25), 22 * (1 - a / 0.25), 1);
            rings(bx, by, [[0, 1], [0.1, 0.8]], a, 230, 34);
            wash(p, C[5], 20, 200, 460, 268, 0.3 + 0.3 * hot);
          }
          p.fade = f;
        }
      } else if (move === "king-attack") {
        // 画面を横切って盤を薙ぐ、 大きな火の三日月 (0.18 秒)。 一拍遅れて、 細い 2 本目。 通った跡に、 高さも間もばらばらの炎が立つ
        const pr = clamp(t / 0.18), k = 1 - clamp((t - 0.22) / 0.35);
        if (k > 0) { crescent(p, C, 240, -45, 285, 2.35, 0.8, 36 * k + 2, 1, pr, 1, 0.6, t * 10); if (t > 0.05) crescent(p, C, 228, -58, 268, 2.3, 0.86, 12 * k + 1, 0.9, clamp((t - 0.05) / 0.18)); }   // 流れに沿った筋を少し (作者の指定)
        for (const q of tongues) {
          const a = t - 0.06 - q.d - q.u * 0.12;
          if (a <= 0 || a >= q.life) continue;
          const g = 2.35 + (0.8 - 2.35) * q.u, x = 240 + Math.cos(g) * 281, y = -45 + Math.sin(g) * 281, env = ease.outBack(clamp(a / 0.08)) * (1 - clamp((a - q.life * 0.4) / (q.life * 0.6)));
          const h = q.h * env * (0.55 + 0.9 * rand(Math.floor(t * 8), q.n));   // 1 コマごとに大きく暴れる
          flameTongue(p, x, y, h, 4.5 * q.w * (0.6 + 0.4 * env) * (1 + h / 60), q.sway * (1 + h / 30), N, q.n, HOT, h > 30 ? BIG_LAYERS : LAYERS, 0.7, 2);
        }
        if (t >= 0.11) { const a = t - 0.11; lens(p, C, bx, by - 6, 130 * (1 - clamp(a / 0.2)), 18 * (1 - clamp(a / 0.2)), a < 0.2 ? 1 : 0); rings(bx, by, [[0, 1], [0.08, 0.7]], a, 220, 30); if (a < 0.35) p.glow(bx, by - 10, 150, C[5], 0.6 * (1 - a / 0.35)); }
        wash(p, C[4], 20, 190, 460, 268, 0.55 * (1 - clamp((t - 0.1) / 0.9)) * clamp(t / 0.1));
      } else if (move === "king-regen") {
        // 体の内側から、 光が漏れ出る: 胸から光の筋が体の中を走り (0.3 秒)、 2 回脈を打つ (0.2 秒・0.62 秒)。 筋の先から外へ、 短い光がこぼれる。 足もとの炎は無い
        const grow = clamp(t / 0.3), k = 1 - clamp((t - 0.95) / 0.3), beat = Math.max(Math.exp(-(((t - 0.2) / 0.07) ** 2)), Math.exp(-(((t - 0.62) / 0.07) ** 2)));
        if (k > 0) {
          p.glow(cx, cy + 6, 46 + 30 * beat, C[4], (0.35 + 0.4 * beat) * k); p.glow(cx, cy + 6, 16 + 14 * beat, C[6], (0.4 + 0.5 * beat) * k);
          for (const pts of veins) {
            const n = Math.floor((pts.length - 1) * grow);
            for (let i = 1; i <= n; i++) { const c = beat > 0.5 ? WHITE : i < 3 ? C[6] : C[5]; p.line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], c, k); if (i < 5) p.line(pts[i - 1][0] + 1, pts[i - 1][1], pts[i][0] + 1, pts[i][1], C[4], k); }
            if (n > 0) { const [ex, ey] = pts[n], [qx, qy] = pts[n - 1], L = Math.hypot(ex - qx, ey - qy) || 1, len = (8 + 16 * beat) * (0.6 + 0.4 * rand(Math.floor(t * 8), ex | 0)); beam(p, C, ex, ey, ex + ((ex - qx) / L) * len, ey + ((ey - qy) / L) * len, 3, 1, k); p.glow(ex, ey, 7 + 6 * beat, C[5], 0.5 * k); }
          }
          for (const at of [0.2, 0.62]) { const u = (t - at) / 0.4; if (u > 0 && u < 1) oval(p, cx, cy + 6, 24 + 70 * ease.outCubic(u), 30 + 80 * ease.outCubic(u), 2, u < 0.3 ? C[6] : C[4], 1 - u); }
          wash(p, C[4], 170, 70, 310, 200, 0.3 * k * (0.5 + beat));
        }
      } else if (move === "judge-flame") {
        // 天から太い光が盤へ落ちる (0.08 秒)。 着弾 (holy/smite の作り): 横に長い閃光・床を走る輪 3 本・上へ立つ光の棘・白く光って金へ冷めるひび
        const reach = clamp(t / 0.08), fade = 1 - clamp((t - 0.08) / 0.32);
        if (fade > 0) beam(p, C, bx, -6, bx, -6 + (by + 6) * reach, 10 * fade + 2, 34 * fade + 2, 1);
        if (t >= 0.08) {
          const a = t - 0.08;
          crackle(a, 0.14, 0.2, 0.9);
          rings(bx, by, [[0, 1], [0.07, 0.72], [0.14, 0.46]], a, 216, 32, 0.5);
          // 光の棘: 着いた所から、 上へ斜めに 11 本。 長さはばらばら。 0.3 秒で消える
          const k = 1 - clamp(a / 0.34);
          if (k > 0) for (let i = 0; i < 11; i++) { const g = UP + (i - 5) * 0.2 + 0.05 * Math.sin(i * 7), len = (34 + 70 * rand(i, 3, 5)) * ease.outCubic(clamp(a / 0.08)); beam(p, C, bx + (i - 5) * 9, by, bx + (i - 5) * 9 + Math.cos(g) * len, by + Math.sin(g) * len, 5 * k + 1, 1, k); }
          lens(p, C, bx, by - 4, 150 * (1 - clamp(a / 0.24)), 20 * (1 - clamp(a / 0.24)), a < 0.24 ? 1 : 0);
          if (a < 0.3) p.glow(bx, by - 10, 150, WHITE, 0.6 * (1 - a / 0.3));
          wash(p, C[5], 30, 190, 450, 268, 0.5 * (1 - clamp(a / 0.9)));
        }
      } else if (move === "chev-thrust") {
        // 三連の突き (0 / 0.09 / 0.19 秒): 胸の前のきらめきから、 細い線が一直線に届く (0.04 秒)。 線はすぐ消え、 当たった所に鋭い十字のきらめき (構えと同じ作り) が残る。
        // 決めの 3 本目だけ: 線が太く、 盤を突き抜けて画面の下まで走り、 きらめきが大きく、 輪が 2 本と、 横一文字の細い線が盤を走る。 星形や丸い閃光は使わない (鋭さが鈍る)
        for (const [at, x, big] of THR) {
          const a = t - at;
          if (a < 0) continue;
          if (a < 0.08) glint(p, C, cx + (x - bx) * 0.12, cy, (big ? 54 : 30) * (1 - a / 0.08) + 4);
          const reach = ease.outExpo(clamp(a / 0.04)), fade = 1 - clamp((a - 0.04) / (big ? 0.2 : 0.1)), x0 = cx + (x - bx) * 0.12, y1 = big ? H + 10 : by;
          if (fade > 0) { beam(p, C, x0, cy, x0 + (x - x0) * reach, cy + (by - cy) * reach, 1.2, (big ? 8 : 4) * fade + 1, 1); if (big && reach >= 1) beam(p, C, x, by, x, y1, 8 * fade + 1, 3 * fade + 1, fade); }
          if (a >= 0.04) {
            const h = a - 0.04, k = 1 - clamp(h / (big ? 0.34 : 0.18));
            if (k > 0) glint(p, C, x, by - 2, (big ? 120 : 46) * k + 4);
            if (big) {
              for (const [d, s] of [[0, 1], [0.05, 0.6]]) { const u = (h - d) / 0.28; if (u > 0 && u < 1) oval(p, x, by, 16 + 190 * ease.outCubic(u) * s, 4 + 26 * ease.outCubic(u) * s, 2, u < 0.3 ? WHITE : C[6], 1.1 - u); }
              const ln = ease.outExpo(clamp(h / 0.05)), lk = 1 - clamp((h - 0.1) / 0.25);
              if (lk > 0) beam(p, C, x - 232 * ln, by - 1, x + 232 * ln, by - 1, 3 * lk + 0.6, 3 * lk + 0.6, 1);   // 盤を走る横一文字
              if (k > 0) for (let i = 0; i < 16; i++) { const g = UP + (i - 7.5) * 0.2, r0 = 40 + 110 * (1 - k), r1 = r0 + 60 * k; p.line(x + Math.cos(g) * r0, by + Math.sin(g) * r0, x + Math.cos(g) * r1, by + Math.sin(g) * r1, i % 2 ? WHITE : C[5], k); }   // 速さの線
              wash(p, C[6], 60, 210, 420, 268, 0.45 * k);
            }
          }
        }
      } else if (move === "chev-stance") {
        // 刃を構え直す: 体の前で、 2 本の弧が交差する (上の弧 → 下の弧)。 交点にきらめき、 輪が 1 つ広がる (作者: 満点 ── 変えない)
        const k = 1 - clamp((t - 0.3) / 0.3);
        if (k > 0) { crescent(p, C, cx - 14, cy + 26, 84, -2.5, -0.55, 9 * k + 1, 1, clamp(t / 0.1)); if (t > 0.08) crescent(p, C, cx + 14, cy - 26, 84, 0.65, 2.6, 9 * k + 1, 1, clamp((t - 0.08) / 0.1)); }
        if (t >= 0.18) { const a = t - 0.18; if (a < 0.4) glint(p, C, cx, cy, 70 * (1 - a / 0.4) + 4); const u = a / 0.4; if (u < 1) oval(p, cx, cy, 20 + 86 * ease.outCubic(u), 20 + 86 * ease.outCubic(u), 2, u < 0.3 ? WHITE : C[6], 1.1 - u); }
      }
      ash.draw(p);
      ps.draw(p);
    },
  };
}
