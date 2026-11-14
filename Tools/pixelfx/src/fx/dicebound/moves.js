// DICE BOUND 用 (2026-11-14): ボスの、 必殺以外の技 (たたき台・第 2 版)。 move で出し分ける。 画面全体 (480×270)・原点は左上・1 回きり。
// 毎ターン出るので短い (天与の 3 つだけ 1.75 秒と長く、 豪華)。 暗転や白飛びは使わない (それは大技だけ)。 形は _kit.js の部品で、 この画面の大きさで描く。
//   ヴェスカ:   relic-strike (遺物・攻撃) / relic-guard (遺物・防御) / tenyo-sword (天与の剣) / tenyo-shield (天与の盾) / tenyo-ring (天与の指輪)
//               (遺物・妨害は dicebound/vesca-hex ── shadow/hex-sigil を大きくした物)
//   灰燼の王:   king-attack (通常攻撃) / king-regen (灰の再生)
//   審判官:     judge-flame (審判の炎)
//   シュヴァリエ: chev-thrust (通常攻撃) / chev-stance (構え)
// 作者の指摘 (第 1 版 → 第 2 版):
//   遺物・攻撃 = 遺物は物の武器なので、 光の槍のような謎の力ではなく、 おとなしく斬撃。 天与の剣 = その強化版・金色。 天与は 3 つとも金色・長く・豪華に。
//   天与の指輪 = ぺらくて地味だった → 厚い輪・宝石・光の壁・鎖・締まる内輪。
//   王の通常攻撃 = 炎が均等すぎた → 高さも位置も間もばらばらに、 暴れさせる。 当たりは星形をやめて、 火の塊の炸裂。
//   灰の再生 = 足もとの炎ではなく、 毒や出血のように「体の内側から漏れ出る光」。
//   審判の炎 = 光を太く・盤の炎は無し・当たりは星形 (四角に見えた) をやめて、 holy/smite の着弾 (閃光・床を走る輪・光の棘・光って冷めるひび・欠片)。
//   シュヴァリエの突き = 当たりの×印 (星形) をやめて、 横に細い閃光。 構えは変えない。
// The shield bubble is this repository's holy/divine-shield, drawn as is; the shapes follow its slash, smite, ground-slam and fire effects
// (after Sprite Fusion's open-source effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, clamp, ease, noise, rand, TAU, WHITE } from "../../core/index.js";
import * as SHIELD from "../holy/divine-shield.js";
import { flameTongue, LAYERS, BIG_LAYERS } from "../fire/_lib.js";
import { GOLD } from "../holy/_lib.js";
import { VIOLET } from "./vesca-charge.js";
import { STEEL, glint } from "./chevalier-charge.js";
import { W, H, CHEST, FOOT, BOARD, beam, oval, crescent, slash, slashAt, lens, diamond, wash, onces } from "./_kit.js";

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
  const S1 = [64, 198, 416, 262, -22], SA = [36, 150, 444, 262, -30], SB = [444, 150, 36, 262, 30], SC_ = [8, 246, 472, 246, -8];
  const gems = Array.from({ length: 8 }, (_, i) => (i / 8) * TAU + 0.2);
  const curtain = Array.from({ length: 46 }, () => ({ g: R.range(0, TAU), h: R.range(34, 96), d: R.range(0, 0.1) }));
  return {
    update(dt) {
      t += dt;
      if (move === "relic-strike") once("hit", t >= 0.07, () => {
        along(S1, 110, 80, 360);
        for (let i = 0; i < 18; i++) { const [x, y] = slashAt(...S1, R.range(0.1, 0.9)); ps.spawn(P.STAR, x, y - R.range(0, 20), 0, -R.range(5, 30), R.range(0.3, 0.7), R.chance(0.5) ? WHITE : C[6], R.range(1.2, 2.6), R.range(0, 0.2)); }
        for (let i = 0; i < 34; i++) { const [x, y] = slashAt(...S1, R.range(0.1, 0.9)); ps.spawn(P.FLAKE, x, y, R.range(-80, 80), -R.range(100, 280), R.range(0.6, 1.1), R.pick([C[4], C[5], C[3]]), R.range(1, 2.2)); }   // 削り屑 (slash-wipe の shavings の出し方)
        for (let i = 0; i < 8; i++) ps.spawn(P.SMOKE, bx + R.range(-120, 120), by - R.range(0, 12), R.range(-30, 30), -R.range(15, 50), R.range(0.5, 0.9), C[2], R.range(6, 11), R.range(0, 0.15));
      });
      if (move === "tenyo-sword") {
        for (const [key, at, S] of [["a", 0.07, SA], ["b", 0.23, SB], ["c", 0.42, SC_]]) once(key, t >= at, () => {
          along(S, 120, 100, 440);
          for (let i = 0; i < 26; i++) { const [x, y] = slashAt(...S, R.range(0.05, 0.95)); ps.spawn(P.STAR, x, y - R.range(0, 30), 0, -R.range(5, 30), R.range(0.4, 0.9), R.chance(0.6) ? WHITE : C[6], R.range(1.5, 3.2), R.range(0, 0.3)); }
          for (let i = 0; i < 40; i++) { const [x, y] = slashAt(...S, R.range(0.05, 0.95)); ps.spawn(P.FLAKE, x, y, R.range(-70, 70), -R.range(80, 300), R.range(0.8, 1.5), R.pick([C[5], C[6], WHITE]), R.range(1, 2.4)); }
        });
        if (t > 0.45 && t < 1.2 && R.chance(70 * dt)) ps.spawn(P.MOTE, R.range(30, 450), 250 + R.range(-6, 6), R.range(-6, 6), -R.range(50, 140), R.range(0.6, 1.1), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.8));   // 立ちのぼる金の粒 (blessing の出し方)
      }
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
        for (const [key, at] of [["b1", 0.2], ["b2", 0.62]]) once(key, t >= at, () => { for (let i = 0; i < 26; i++) { const g = R.range(0, TAU), v = R.range(30, 110); ps.spawn(P.EMBER, cx + Math.cos(g) * 30, cy + 6 + Math.sin(g) * 40, Math.cos(g) * v, Math.sin(g) * v - 20, R.range(0.4, 0.8), 0, 1); } });
      }
      if (move === "judge-flame") once("hit", t >= 0.08, () => {
        sp(70, () => bx + R.range(-24, 24), () => by, UP, 1.3, 120, 420, 0.25, 0.6);
        for (let i = 0; i < 30; i++) ps.spawn(P.FLAKE, bx + R.range(-80, 80), by - R.range(0, 8), R.range(-90, 90), -R.range(120, 320), R.range(0.6, 1.1), R.pick([C[4], C[5], C[3]]), R.range(1, 2.2));   // 飛ぶ欠片 (smite の stone chips)
        for (let i = 0; i < 26; i++) ps.spawn(P.MOTE, bx + R.range(-110, 110), by - R.range(0, 10), R.range(-8, 8), -R.range(40, 140), R.range(0.5, 1), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.8), R.range(0, 0.35));
        for (let i = 0; i < 14; i++) ps.spawn(P.STAR, bx + R.range(-140, 140), by - R.range(10, 80), 0, -R.range(5, 20), R.range(0.3, 0.7), WHITE, R.range(1.2, 2.6), R.range(0.1, 0.5));
      });
      if (move === "chev-thrust") once("hit", t >= 0.07, () => { sp(26, () => bx, () => by, UP, 0.5, 220, 620, 0.12, 0.3); sp(16, () => bx, () => by, 0, 0.1, 300, 700, 0.1, 0.25); sp(16, () => bx, () => by, Math.PI, 0.1, 300, 700, 0.1, 0.25); });
      if (move === "chev-stance") once("ring", t >= 0.22, () => { sp(30, () => cx, () => cy, 0, Math.PI, 160, 420, 0.12, 0.3); for (let i = 0; i < 8; i++) ps.spawn(P.STAR, cx + R.range(-60, 60), cy + R.range(-60, 60), 0, 0, R.range(0.2, 0.45), WHITE, R.range(1.5, 3), R.range(0, 0.15)); });
      ps.update(dt);
    },
    draw(p) {
      const rings = (x, y, list, a, rx, ry, dur = 0.45) => { for (const [d, s] of list) { const u = (a - d) / dur; if (u > 0 && u < 1) { const e = ease.outCubic(u); oval(p, x, y, 24 + rx * e * s, 5 + ry * e * s, u < 0.3 ? 4 : 2, u < 0.25 ? WHITE : u < 0.6 ? C[6] : C[5], 1.1 - u); } } };
      const crackle = (a, grow, hot, life) => { for (const pts of cracks) for (let i = 1; i < pts.length * clamp(a / grow); i++) p.line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], a < hot ? WHITE : a < hot * 2 ? C[6] : C[4], 1 - clamp((a - life * 0.6) / (life * 0.4))); };
      if (move === "relic-strike") {
        // 盤を斜めに断つ斬撃 (0.1 秒で振り抜く)。 一拍遅れて、 細い残像がもう 1 本。 そのあと、 細い傷が光って残る
        const k = 1 - clamp((t - 0.16) / 0.3);
        if (k > 0) slash(p, C, ...S1, 36 * k + 2, 1, clamp(t / 0.1));
        if (t > 0.04 && k > 0) slash(p, C, S1[0] + 10, S1[1] - 16, S1[2] + 6, S1[3] - 14, -26, 9 * k, 0.9, clamp((t - 0.04) / 0.1));
        const scar = 1 - clamp((t - 0.5) / 0.3);
        if (t > 0.1 && scar > 0) slash(p, C, ...S1, 2.4, scar, 1);
        if (t >= 0.07) { const a = t - 0.07; lens(p, C, bx, by - 14, 96 * (1 - clamp(a / 0.22)), 12 * (1 - clamp(a / 0.22)), a < 0.22 ? 1 : 0); wash(p, C[5], 30, 196, 450, 268, 0.5 * (1 - clamp(a / 0.6))); }
      } else if (move === "tenyo-sword") {
        // 金の斬撃が 3 つ: 左上から右下・右上から左下 (×に交わる)・最後に盤を横一文字。 傷は光ったまま残り、 光の柱が立って、 ゆっくり消える
        const cuts = [[0, SA, 50], [0.16, SB, 50], [0.35, SC_, 34]];
        for (const [at, S, w] of cuts) {
          const a = t - at;
          if (a < 0) continue;
          const k = 1 - clamp((a - 0.14) / 0.3), scar = 1 - clamp((t - 1.15) / 0.4);
          if (k > 0) { slash(p, C, ...S, w * k + 2, 1, clamp(a / 0.09)); slash(p, C, S[0], S[1] - 20, S[2], S[3] - 20, S[4] * 1.3, w * 0.25 * k, 0.9, clamp((a - 0.04) / 0.09)); }
          if (a > 0.09 && scar > 0) slash(p, C, ...S, 3.4, scar, 1);
          if (a < 0.24) { const [x, y] = slashAt(...S, 0.5); lens(p, C, x, y, 120 * (1 - a / 0.24), 16 * (1 - a / 0.24), 1); }
        }
        if (t >= 0.42) {
          const a = t - 0.42, k = 1 - clamp((a - 0.5) / 0.6);
          rings(bx, by, [[0, 1], [0.08, 0.72], [0.16, 0.46]], a, 216, 32);
          // 光の柱: 交点の上へ立ちのぼる (太く立って、 細って消える)
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
        if (k > 0) { crescent(p, C, 240, -45, 285, 2.35, 0.8, 36 * k + 2, 1, pr); if (t > 0.05) crescent(p, C, 228, -58, 268, 2.3, 0.86, 12 * k + 1, 0.9, clamp((t - 0.05) / 0.18)); }
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
        // 胸の前できらめき (1 コマ) → 細い一突きが、 こちらへ一直線に届く (0.06 秒)。 当たりは、 横に細い閃光 1 つと、 輪 1 つ。 すぐ消える
        if (t < 0.1) glint(p, C, cx, cy, 40 * (1 - t / 0.1) + 6);
        const reach = ease.outExpo(clamp((t - 0.02) / 0.05)), fade = 1 - clamp((t - 0.07) / 0.22);
        if (t >= 0.02 && fade > 0) beam(p, C, cx, cy, cx, cy + (by - cy) * reach, 1.5, 9 * fade + 1, 1);
        if (t >= 0.07) {
          const a = t - 0.07, k = 1 - clamp(a / 0.3);
          lens(p, C, bx, by - 2, 70 * (1 - clamp(a / 0.16)), 5 * (1 - clamp(a / 0.16)), a < 0.16 ? 1 : 0);
          const u = a / 0.3; if (u < 1) oval(p, bx, by, 16 + 150 * ease.outCubic(u), 4 + 22 * ease.outCubic(u), 2, u < 0.3 ? WHITE : C[6], 1.1 - u);
          if (k > 0) for (let i = 0; i < 12; i++) { const g = UP + (i - 5.5) * 0.24, r0 = 30 + 70 * (1 - k), r1 = r0 + 46 * k; p.line(bx + Math.cos(g) * r0, by + Math.sin(g) * r0, bx + Math.cos(g) * r1, by + Math.sin(g) * r1, i % 2 ? WHITE : C[5], k); }   // 速さの線
          wash(p, C[6], 80, 216, 400, 268, 0.4 * k);
        }
      } else if (move === "chev-stance") {
        // 刃を構え直す: 体の前で、 2 本の弧が交差する (上の弧 → 下の弧)。 交点にきらめき、 輪が 1 つ広がる (作者: 満点 ── 変えない)
        const k = 1 - clamp((t - 0.3) / 0.3);
        if (k > 0) { crescent(p, C, cx - 14, cy + 26, 84, -2.5, -0.55, 9 * k + 1, 1, clamp(t / 0.1)); if (t > 0.08) crescent(p, C, cx + 14, cy - 26, 84, 0.65, 2.6, 9 * k + 1, 1, clamp((t - 0.08) / 0.1)); }
        if (t >= 0.18) { const a = t - 0.18; if (a < 0.4) glint(p, C, cx, cy, 70 * (1 - a / 0.4) + 4); const u = a / 0.4; if (u < 1) oval(p, cx, cy, 20 + 86 * ease.outCubic(u), 20 + 86 * ease.outCubic(u), 2, u < 0.3 ? WHITE : C[6], 1.1 - u); }
      }
      ps.draw(p);
    },
  };
}
