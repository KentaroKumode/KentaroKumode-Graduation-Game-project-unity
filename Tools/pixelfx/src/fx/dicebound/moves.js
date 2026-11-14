// DICE BOUND 用 (2026-11-14): ボスの、 必殺以外の技 (たたき台)。 move で出し分ける。 画面全体 (480×270)・原点は左上・1.25 秒・1 回きり。
// 毎ターン出るので短い。 暗転や白飛びは使わない (それは大技だけ)。 形は _kit.js の部品で、 この画面の大きさで描く。 粒は、 技ごとに複数の種類を組み合わせる。
//   ヴェスカ:   relic-strike (遺物・攻撃) / relic-guard (遺物・防御) / relic-hex (遺物・妨害) / tenyo-sword (天与の剣) / tenyo-shield (天与の盾) / tenyo-ring (天与の指輪)
//   灰燼の王:   king-attack (通常攻撃) / king-regen (灰の再生)
//   審判官:     judge-flame (審判の炎)
//   シュヴァリエ: chev-thrust (通常攻撃) / chev-stance (構え)
// 色は palette で渡す (ボスごと)。 既定は move から決まる。
// The shield bubble is this repository's holy/divine-shield, drawn as is; the shapes follow its slash, crit-hit, hex-sigil and fire effects
// (after Sprite Fusion's open-source effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, clamp, ease, noise, rand, TAU, WHITE } from "../../core/index.js";
import * as SHIELD from "../holy/divine-shield.js";
import { flameTongue, LAYERS, BIG_LAYERS } from "../fire/_lib.js";
import { GOLD } from "../holy/_lib.js";
import { VIOLET } from "./vesca-charge.js";
import { STEEL, glint } from "./chevalier-charge.js";
import { W, H, CHEST, FOOT, BOARD, beam, starburst, oval, crescent, sigil, wash, onces } from "./_kit.js";

const FIRE = ["#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#fff3a6", "#ffffff"];
const PAL = { relic: VIOLET, tenyo: VIOLET, king: FIRE, judge: GOLD, chev: STEEL };

export const meta = {
  name: "Boss moves (DICE BOUND)",
  desc: "The bosses' ordinary moves, drawn at the game's screen size: Vesca's relics (strike, guard, hex) and her three divine gifts (sword, shield, ring), the Ash King's fire cleave and ash regeneration, the Judge's flame, the Chevalier's thrust and stance.",
  tags: ["dicebound", "boss", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 1.25,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { move: "relic-strike", palette: null, seed: 3 },
};

export function create(o) {
  const move = o.move, C = fit(o.palette ?? PAL[move.split("-")[0]], 8), HOT = [...C].reverse(), R = new Rng(o.seed), once = onces();
  const ps = new Particles({ rng: R, sparks: [C[7], C[6], C[5], C[4]] });
  const [bx, by] = BOARD, [cx, cy] = CHEST, [fx, fy] = FOOT;
  let t = 0;
  const N = (rate, n, off = 0) => noise(t * rate + off, n);
  const sp = (n, x, y, a0, spread, v0, v1, l0, l1) => { for (let i = 0; i < n; i++) { const a = a0 + R.range(-spread, spread), v = R.range(v0, v1); ps.spawn(P.SPARK, x(), y(), Math.cos(a) * v, Math.sin(a) * v, R.range(l0, l1)); } };
  const UP = -Math.PI / 2;
  // 床を走るひび (relic-strike / tenyo-sword): 盤の中心から左右へ、 ぎざぎざの線
  const cracks = Array.from({ length: 9 }, (_, i) => { let x = bx, y = by + R.range(-2, 4), a = (i % 2 ? 0 : Math.PI) + R.range(-0.35, 0.35); const pts = [[x, y]]; for (let s = 0; s < 22; s++) { a += R.range(-0.5, 0.5); x += Math.cos(a) * R.range(6, 13); y += Math.sin(a) * R.range(1.5, 4); y = clamp(y, 228, 266); pts.push([x, y]); } return pts; });
  // 炎の舌 (king / judge): [x のずれ, 高さ, 半幅, 種]
  const tongues = Array.from({ length: 13 }, () => [R.range(-1, 1), R.range(0.6, 1), R.range(0.7, 1.2), R.int(1, 900)]);
  return {
    update(dt) {
      t += dt;
      if (move === "relic-strike") once("hit", t >= 0.1, () => {
        sp(80, () => bx + R.range(-12, 12), () => by, UP, 1.25, 140, 460, 0.25, 0.6);
        sp(46, () => bx, () => by + R.range(-3, 3), 0, 0.12, 200, 560, 0.2, 0.45); sp(46, () => bx, () => by + R.range(-3, 3), Math.PI, 0.12, 200, 560, 0.2, 0.45);
        for (let i = 0; i < 14; i++) ps.spawn(P.SMOKE, bx + R.range(-70, 70), by - R.range(0, 10), R.range(-40, 40), -R.range(20, 70), R.range(0.5, 0.9), C[2], R.range(7, 13), R.range(0, 0.1));   // 紫の煙 (dark-explosion の出し方)
        for (let i = 0; i < 16; i++) ps.spawn(P.STAR, bx + R.range(-150, 150), by - R.range(0, 50), R.range(-10, 10), -R.range(10, 40), R.range(0.3, 0.7), R.chance(0.5) ? WHITE : C[6], R.range(1.2, 2.6), R.range(0, 0.25));   // きらめき
        for (let i = 0; i < 26; i++) ps.spawn(P.FLAKE, bx + R.range(-90, 90), by - R.range(0, 20), R.range(-90, 90), -R.range(120, 300), R.range(0.6, 1.1), R.pick([C[4], C[5], C[3]]), R.range(1, 2));   // 砕けた欠片 (ground-slam の出し方)
      });
      if (move === "relic-hex") {
        once("flame", t >= 0.62, () => {
          for (let i = 0; i < 60; i++) { const g = R.range(0, TAU); ps.spawn(P.FIRE, bx + Math.cos(g) * 108, 222 + Math.sin(g) * 44, R.range(-14, 14), -R.range(40, 130), R.range(0.3, 0.6), 0, R.range(3, 7), R.range(0, 0.12)); }   // 輪から立つ炎 (shadow-flames の出し方)
          sp(70, () => bx + R.range(-108, 108), () => 222 + R.range(-44, 44), UP, 0.5, 80, 300, 0.25, 0.55);
        });
        once("crumble", t >= 0.8, () => { for (let i = 0; i < 70; i++) { const g = R.range(0, TAU), q = R.range(0.3, 1); ps.spawn(P.FLAKE, bx + Math.cos(g) * 108 * q, 222 + Math.sin(g) * 44 * q, R.range(-30, 30), -R.range(0, 60), R.range(0.4, 0.8), R.pick([C[4], C[5], C[6]]), R.range(1, 2)); } });   // 崩れて落ちる (hex-sigil の崩れ方)
      }
      if (move === "tenyo-sword") {
        once("land", t >= 0.14, () => { sp(60, () => bx + R.range(-16, 16), () => by, UP, 1.3, 160, 520, 0.25, 0.6); for (let i = 0; i < 18; i++) ps.spawn(P.STAR, bx + R.range(-120, 120), R.range(40, 220), 0, -R.range(5, 25), R.range(0.3, 0.7), WHITE, R.range(1.5, 3), R.range(0, 0.2)); });
        once("cut", t >= 0.34, () => { sp(90, () => R.range(30, 450), () => 246 + R.range(-3, 3), UP, 0.9, 80, 320, 0.2, 0.5); for (let i = 0; i < 40; i++) ps.spawn(P.FLAKE, R.range(40, 440), 244, R.range(-40, 40), -R.range(60, 200), R.range(0.6, 1.1), R.pick([C[5], C[6], WHITE]), R.range(1, 2.2)); });
      }
      if (move === "tenyo-ring") once("lock", t >= 0.26, () => { for (let i = 0; i < 8; i++) { const g = (i / 8) * TAU + 0.2; sp(9, () => bx + Math.cos(g) * 212, () => by + Math.sin(g) * 28, UP, 1.4, 60, 240, 0.2, 0.45); } for (let i = 0; i < 20; i++) ps.spawn(P.STAR, bx + R.range(-210, 210), by + R.range(-28, 28), 0, -R.range(5, 20), R.range(0.3, 0.6), WHITE, R.range(1.2, 2.4), R.range(0, 0.2)); });
      if (move === "tenyo-shield" || move === "relic-guard") {
        const big = move === "tenyo-shield";
        if (t < 0.9 && R.chance((big ? 60 : 24) * dt)) ps.spawn(P.MOTE, fx + R.range(-70, 70), fy + R.range(-4, 6), R.range(-6, 6), -R.range(40, 110), R.range(0.5, 0.9), R.chance(0.5) ? C[6] : C[5], R.range(0.8, 1.6));
        if (big) once("stars", t >= 0.12, () => { for (let i = 0; i < 22; i++) { const g = R.range(0, TAU); ps.spawn(P.STAR, fx + Math.cos(g) * 92, fy - 66 + Math.sin(g) * 92, 0, -6, R.range(0.4, 0.9), WHITE, R.range(1.4, 3), R.range(0, 0.5)); } });
      }
      if (move === "king-attack") {
        // 斬撃が通った所から、 火の塊・残り火・灰 (fire-ring の shed + meteor の burst の出し方)
        const a0 = 2.35, a1 = 0.8, pr = clamp(t / 0.18), prev = clamp((t - dt) / 0.18);
        for (let u = prev; u < pr; u += 0.012) {
          const g = a0 + (a1 - a0) * u, x = 240 + Math.cos(g) * 285, y = -45 + Math.sin(g) * 285;
          ps.spawn(P.FIRE, x, y, R.range(-20, 20), -R.range(30, 110), R.range(0.3, 0.65), 0, R.range(5, 11), R.range(0, 0.05));
          if (R.chance(0.7)) ps.spawn(P.EMBER, x, y, R.range(-70, 70), -R.range(80, 260), R.range(0.5, 1), 0, R.chance(0.3) ? 2 : 1);
          if (R.chance(0.5)) ps.spawn(P.FLAKE, x, y, R.range(-50, 50), -R.range(40, 160), R.range(0.7, 1.2), R.pick(["#a39cab", "#7b7484", "#5c5664"]), R.range(1, 2));
        }
        once("hit", t >= 0.12, () => { sp(50, () => bx + R.range(-40, 40), () => by, UP, 1.2, 120, 400, 0.25, 0.55); for (let i = 0; i < 8; i++) ps.spawn(P.SMOKE, bx + R.range(-120, 120), by - R.range(0, 14), R.range(-20, 20), -R.range(20, 50), R.range(0.6, 1), 0, R.range(8, 14), R.range(0.1, 0.3)); });
      }
      if (move === "king-regen") {
        // 灰が体へ集まり (FLAKE を内向きに)、 足もとから残り火が上がる (campfire の出し方)
        if (t < 0.7) for (let i = 0; i < 2; i++) { const g = R.range(0, TAU), d = R.range(90, 170), x = cx + Math.cos(g) * d, y = cy + Math.sin(g) * d * 0.7, life = R.range(0.35, 0.55); ps.spawn(P.FLAKE, x, y, (cx - x) / life * 0.9, (cy - y) / life * 0.9, life, R.pick(["#a39cab", "#7b7484", "#c8c0cc"]), R.range(1, 2)); }
        if (t > 0.2 && t < 1 && R.chance(70 * dt)) ps.spawn(P.EMBER, fx + R.range(-52, 52), fy - R.range(0, 6), R.range(-10, 10), -R.range(50, 150), R.range(0.5, 1), 0, R.chance(0.3) ? 2 : 1);
        if (t > 0.3 && t < 0.9 && R.chance(26 * dt)) ps.spawn(P.FIRE, fx + R.range(-46, 46), fy - 2, R.range(-6, 6), -R.range(40, 90), R.range(0.3, 0.55), 0, R.range(3, 6));
      }
      if (move === "judge-flame") once("hit", t >= 0.08, () => {
        sp(40, () => bx + R.range(-20, 20), () => by, UP, 1.1, 90, 300, 0.25, 0.55);
        for (let i = 0; i < 20; i++) ps.spawn(P.MOTE, bx + R.range(-60, 60), by - R.range(0, 10), R.range(-8, 8), -R.range(40, 130), R.range(0.5, 1), R.chance(0.5) ? WHITE : C[6], R.range(0.8, 1.8), R.range(0, 0.3));   // 立ちのぼる光の粒 (blessing / heal の出し方)
        for (let i = 0; i < 10; i++) ps.spawn(P.STAR, bx + R.range(-90, 90), by - R.range(10, 70), 0, -R.range(5, 20), R.range(0.3, 0.7), WHITE, R.range(1.2, 2.4), R.range(0.1, 0.5));
      });
      if (move === "chev-thrust") once("hit", t >= 0.07, () => { sp(26, () => bx, () => by, UP, 0.5, 220, 620, 0.12, 0.3); sp(14, () => bx, () => by, 0, 0.1, 300, 700, 0.1, 0.25); sp(14, () => bx, () => by, Math.PI, 0.1, 300, 700, 0.1, 0.25); });
      if (move === "chev-stance") once("ring", t >= 0.22, () => { sp(30, () => cx, () => cy, 0, Math.PI, 160, 420, 0.12, 0.3); for (let i = 0; i < 8; i++) ps.spawn(P.STAR, cx + R.range(-60, 60), cy + R.range(-60, 60), 0, 0, R.range(0.2, 0.45), WHITE, R.range(1.5, 3), R.range(0, 0.15)); });
      ps.update(dt);
    },
    draw(p) {
      if (move === "relic-strike") {
        // 太い光の槍 (0.1 秒で届く・0.3 秒で細る)。 撃ち出す所に閃光
        const reach = ease.outCubic(clamp(t / 0.1)), fade = 1 - clamp((t - 0.1) / 0.3);
        if (fade > 0) { beam(p, C, cx, cy, cx, cy + (by - cy) * reach, 7 * fade + 1, 30 * fade + 2, 1); starburst(p, C, cx, cy, 30 * fade, 4, Math.PI / 4, 1, 1); p.glow(cx, cy, 34 * fade + 6, C[5], 0.6 * fade); }
        if (t >= 0.1) {
          const a = t - 0.1, k = 1 - clamp(a / 0.4);
          for (const pts of cracks) for (let i = 1; i < pts.length * clamp(a / 0.12); i++) { const c = a < 0.25 ? WHITE : a < 0.5 ? C[6] : C[4]; p.line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], c, 1 - clamp((a - 0.5) / 0.4)); }
          for (const [d, s] of [[0, 1], [0.07, 0.74], [0.14, 0.5]]) { const u = (a - d) / 0.45; if (u > 0 && u < 1) { const e = ease.outCubic(u); oval(p, bx, by, 24 + 216 * e * s, 5 + 32 * e * s, u < 0.3 ? 4 : 2, u < 0.25 ? WHITE : u < 0.6 ? C[6] : C[5], 1.1 - u); } }
          starburst(p, C, bx, by - 6, 96 * ease.outBack(clamp(a / 0.08)) * k + 4, 9, 0.2, k > 0 ? 1 : 0, 0.8);
          starburst(p, C, bx, by - 6, 60 * k, 6, 0.75, k > 0 ? 1 : 0, 0.8);
          if (a < 0.3) p.glow(bx, by, 110, WHITE, 0.6 * (1 - a / 0.3));
          wash(p, C[5], 30, 214, 450, 268, 0.45 * (1 - clamp(a / 0.7)));
        }
      } else if (move === "relic-guard" || move === "tenyo-shield") {
        const big = move === "tenyo-shield", r = big ? 88 : 72, k = Math.min(clamp(t / 0.15), clamp((1.2 - t) / 0.25)), f = p.fade, so = { palette: big ? [C[2], C[3], C[4], C[5], C[6], C[7], C[7], C[7]] : o.palette ?? VIOLET, radius: r, seed: 8 };
        // 足もとの輪 (広がって定まる) と、 立ちのぼる光の柱 (天与だけ)
        oval(p, fx, fy + 2, r * 1.05 * ease.outBack(clamp(t / 0.2)), r * 0.27 * ease.outBack(clamp(t / 0.2)), big ? 3 : 2, big ? WHITE : C[6], k);
        if (big) for (let i = 0; i < 6; i++) { const g = (i / 6) * TAU + t * 1.2, x = fx + Math.cos(g) * r * 1.05, y = fy + 2 + Math.sin(g) * r * 0.27, hgt = 150 * clamp(t / 0.3) * k; beam(p, C, x, y, x, y - hgt, 5, 1, k); }
        p.fade = f * k;
        const tx = p.tx, ty = p.ty; p.tx = tx + fx; p.ty = ty + fy - 44;
        SHIELD.drawBack(p, t, so); SHIELD.draw(p, t, so);
        p.tx = tx; p.ty = ty; p.fade = f;
        if (big) { for (const [tilt, sgn] of [[0.35, 1], [0.6, -1]]) oval(p, cx, cy, r * 1.22, r * tilt, 2, WHITE, k, sgn * t * 3, sgn * t * 3 + TAU * 0.7); p.glow(cx, cy, r * 1.3, WHITE, 0.35 * k); }
      } else if (move === "relic-hex") {
        // 盤を覆う大きな印 (半径 110・寝かせる)。 0.5 秒で描かれ、 0.62 秒で白く燃え、 崩れる
        const prog = clamp(t / 0.5), hot = clamp(1 - Math.abs(t - 0.66) / 0.14), gone = clamp((t - 0.8) / 0.25);
        if (gone < 1) { const f = p.fade; p.fade = f * (1 - gone); sigil(p, C, bx, 222, 110, 0.4, prog, hot, 7, 3); oval(p, bx, 222, 124, 50, 1, C[4], 1, t * 2, t * 2 + TAU * 0.8 * prog); p.fade = f; }
        // 印から伸びる鎖 (shadow-tendrils の伸び方): 6 本、 上へうねりながら伸びて、 引っ込む
        const ch = clamp((t - 0.35) / 0.25) * (1 - clamp((t - 0.75) / 0.2));
        if (ch > 0) for (let i = 0; i < 6; i++) { const g = (i / 6) * TAU + 0.4, x0 = bx + Math.cos(g) * 96, y0 = 222 + Math.sin(g) * 38; let px = x0, py = y0; for (let s = 1; s <= 14 * ch; s++) { const x = x0 + Math.sin(s * 0.7 + t * 9 + i) * 6, y = y0 - s * 7; p.line(px, py, x, y, s % 2 ? C[6] : C[4]); p.line(px + 1, py, x + 1, y, C[3]); px = x; py = y; } p.glow(px, py, 6, C[6], 0.5); }
        wash(p, C[5], 30, 170, 450, 268, 0.5 * hot);
      } else if (move === "tenyo-sword") {
        // 天から、 巨大な光の刃が盤へ落ちる (0.14 秒)。 残って細る。 そのあと盤を横一文字に断つ (0.34 秒)
        const drop = ease.inCubic(clamp(t / 0.14)), fade = 1 - clamp((t - 0.3) / 0.35);
        if (fade > 0) {
          const tip = -40 + (by + 40) * drop;
          beam(p, C, bx, tip - 300, bx, tip, 34 * fade + 2, 46 * fade + 2, 1);
          starburst(p, C, bx, tip, 30 * fade, 4, Math.PI / 4, drop >= 1 ? fade : 0, 1);   // 切っ先
          beam(p, C, bx - 70 * fade, tip - 150, bx + 70 * fade, tip - 150, 8 * fade + 1, 8 * fade + 1, drop);   // 鍔
        }
        if (t >= 0.14) { const a = t - 0.14; for (const [d, s] of [[0, 1], [0.08, 0.7]]) { const u = (a - d) / 0.45; if (u > 0 && u < 1) { const e = ease.outCubic(u); oval(p, bx, by, 30 + 210 * e * s, 6 + 30 * e * s, u < 0.3 ? 4 : 2, u < 0.3 ? WHITE : C[6], 1.1 - u); } } if (a < 0.25) p.glow(bx, by, 120, WHITE, 0.6 * (1 - a / 0.25)); }
        if (t >= 0.34) {
          const a = t - 0.34, pr = ease.outExpo(clamp(a / 0.08)), k = 1 - clamp((a - 0.2) / 0.4);
          if (k > 0) { beam(p, C, 240 - 236 * pr, 247, 240 + 236 * pr, 245, 12 * k + 1, 12 * k + 1, 1); for (const pts of cracks) for (let i = 1; i < pts.length * clamp(a / 0.15); i++) p.line(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], a < 0.3 ? WHITE : C[5], k); }
          wash(p, C[6], 20, 214, 460, 268, 0.5 * k);
        }
      } else if (move === "tenyo-ring") {
        // 大きな輪が上から降りて来て、 盤を囲んで締まる (0.26 秒)。 8 本の楔が打たれ、 鎖が上へ張る。 しばらく留まって消える
        const dn = ease.inQuad(clamp(t / 0.26)), k = 1 - clamp((t - 0.95) / 0.25), y = 120 + (by - 120) * dn, lock = clamp((t - 0.26) / 0.08), hot = 1 - clamp((t - 0.26) / 0.25);
        if (k > 0) {
          const f = p.fade; p.fade = f * k;
          oval(p, bx, y, 216, 30 * (0.5 + 0.5 * dn), 4, t >= 0.26 && hot > 0.5 ? WHITE : C[6]); oval(p, bx, y - 3, 216, 30 * (0.5 + 0.5 * dn), 1, C[4]); oval(p, bx, y, 204, 25 * (0.5 + 0.5 * dn), 1, C[5], lock);
          for (let i = 0; i < 24; i++) { const g = (i / 24) * TAU; p.dot(bx + Math.cos(g) * 210, y + Math.sin(g) * 27.5 * (0.5 + 0.5 * dn), i % 3 ? C[4] : WHITE); }
          if (lock > 0) for (let i = 0; i < 8; i++) { const g = (i / 8) * TAU + 0.2, x = bx + Math.cos(g) * 212, yy = by + Math.sin(g) * 28; beam(p, C, x, yy - 26 * (2 - lock), x, yy, 2, 6, 1); if (i % 2 === 0) for (let s = 0; s < 22; s++) p.dot(x + Math.sin(s * 0.9 + i) * 1.5, yy - 26 - s * 6, s % 2 ? C[5] : C[3], lock * (1 - s / 22)); }
          p.fade = f;
          if (t >= 0.26) { wash(p, C[5], 20, 212, 460, 268, 0.5 * hot); if (hot > 0) p.glow(bx, by, 140, WHITE, 0.4 * hot); }
        }
      } else if (move === "king-attack") {
        // 画面を横切って盤を薙ぐ、 大きな火の三日月 (0.18 秒)。 通った跡に炎が立って、 縮む
        const pr = clamp(t / 0.18), k = 1 - clamp((t - 0.22) / 0.35);
        if (k > 0) crescent(p, C, 240, -45, 285, 2.35, 0.8, 30 * k + 2, 1, pr);
        const burn = clamp((t - 0.1) / 0.1) * (1 - clamp((t - 0.45) / 0.5));
        if (burn > 0) tongues.forEach(([q, hh, ww, n], i) => { const u = (i + 0.5) / tongues.length, g = 2.35 + (0.8 - 2.35) * u, x = 240 + Math.cos(g) * 280 + q * 8, y = -45 + Math.sin(g) * 280, h = (26 + 26 * hh) * burn * (0.7 + 0.5 * rand(Math.floor(t * 8), n)); flameTongue(p, x, y, h, 5 * ww * (0.6 + 0.4 * burn), q * 3, N, n, HOT, h > 30 ? BIG_LAYERS : LAYERS); });
        if (t >= 0.12 && t < 0.4) { const a = t - 0.12; starburst(p, C, bx, by - 4, 70 * (1 - a / 0.28), 7, 0.3, 1, 0.7); p.glow(bx, by, 120, C[5], 0.5 * (1 - a / 0.28)); }
        wash(p, C[4], 20, 200, 460, 268, 0.5 * (1 - clamp((t - 0.1) / 0.8)) * clamp(t / 0.1));
      } else if (move === "king-regen") {
        // 足もとを囲む炎が立ち上がって、 落ち着く。 体が内から赤く照る
        const up = ease.outBack(clamp((t - 0.15) / 0.3)) * (1 - clamp((t - 0.85) / 0.35));
        oval(p, fx, fy + 2, 66, 14, 2, C[5], up);
        if (up > 0) tongues.forEach(([q, hh, ww, n], i) => { const u = (i + 0.5) / tongues.length, x = fx - 60 + 120 * u, y = fy + 3 + Math.sin(u * Math.PI) * 6, h = (18 + 22 * hh) * up * (0.7 + 0.5 * rand(Math.floor(t * 8), n)); flameTongue(p, x, y, h, 4.5 * ww, q * 2, N, n, HOT, LAYERS); });
        const glow = Math.sin(clamp((t - 0.1) / 0.9) * Math.PI);
        p.glow(cx, cy, 80, C[4], 0.5 * glow); p.glow(cx, cy + 20, 40, C[6], 0.35 * glow);
        wash(p, C[4], 150, 150, 330, 206, 0.35 * glow);
      } else if (move === "judge-flame") {
        // 天から細い光が盤へ落ち (0.08 秒)、 盤の上に金の炎が灯って、 縮んで消える。 光の輪
        const reach = clamp(t / 0.08), fade = 1 - clamp((t - 0.08) / 0.3);
        if (fade > 0) beam(p, C, bx, -4, bx, -4 + (by + 4) * reach, 3 * fade + 1, 14 * fade + 1, 1);
        if (t >= 0.08) {
          const a = t - 0.08, burn = ease.outBack(clamp(a / 0.15)) * (1 - clamp((a - 0.5) / 0.5));
          for (const [d, s] of [[0, 1], [0.1, 0.6]]) { const u = (a - d) / 0.5; if (u > 0 && u < 1) oval(p, bx, by, 20 + 130 * ease.outCubic(u) * s, 4 + 20 * ease.outCubic(u) * s, u < 0.3 ? 3 : 2, u < 0.3 ? WHITE : C[6], 1.1 - u); }
          if (burn > 0) tongues.slice(0, 9).forEach(([q, hh, ww, n], i) => { const x = bx - 56 + 14 * i + q * 3, h = (30 + 34 * hh) * burn * (1 - Math.abs(i - 4) / 7) * (0.7 + 0.5 * rand(Math.floor(t * 8), n)); flameTongue(p, x, by + 2, h, 5 * ww, q * 2, N, n, HOT, h > 30 ? BIG_LAYERS : LAYERS); });
          if (a < 0.25) { starburst(p, C, bx, by - 4, 56 * (1 - a / 0.25), 8, 0, 1, 0.7); p.glow(bx, by, 90, WHITE, 0.5 * (1 - a / 0.25)); }
          wash(p, C[5], 60, 206, 420, 268, 0.45 * (1 - clamp(a / 0.9)));
        }
      } else if (move === "chev-thrust") {
        // 胸の前できらめき (1 コマ) → 細い一突きが、 こちらへ一直線に届く (0.06 秒)。 当たりは菱形 1 つと、 輪 1 つ。 すぐ消える
        if (t < 0.1) glint(p, C, cx, cy, 40 * (1 - t / 0.1) + 6);
        const reach = ease.outExpo(clamp((t - 0.02) / 0.05)), fade = 1 - clamp((t - 0.07) / 0.22);
        if (t >= 0.02 && fade > 0) beam(p, C, cx, cy, cx, cy + (by - cy) * reach, 1.5, 9 * fade + 1, 1);
        if (t >= 0.07) {
          const a = t - 0.07, k = 1 - clamp(a / 0.3);
          starburst(p, C, bx, by - 2, 62 * k + 2, 4, Math.PI / 4, k > 0 ? 1 : 0, 1);
          const u = a / 0.3; if (u < 1) oval(p, bx, by, 16 + 150 * ease.outCubic(u), 4 + 22 * ease.outCubic(u), 2, u < 0.3 ? WHITE : C[6], 1.1 - u);
          // 速さの線: 当たった所から放射に
          if (k > 0) for (let i = 0; i < 12; i++) { const g = UP + (i - 5.5) * 0.24, r0 = 30 + 70 * (1 - k), r1 = r0 + 46 * k; p.line(bx + Math.cos(g) * r0, by + Math.sin(g) * r0, bx + Math.cos(g) * r1, by + Math.sin(g) * r1, i % 2 ? WHITE : C[5], k); }
          wash(p, C[6], 80, 216, 400, 268, 0.4 * k);
        }
      } else if (move === "chev-stance") {
        // 刃を構え直す: 体の前で、 2 本の弧が交差する (上の弧 → 下の弧)。 交点にきらめき、 輪が 1 つ広がる
        const k = 1 - clamp((t - 0.3) / 0.3);
        if (k > 0) { crescent(p, C, cx - 14, cy + 26, 84, -2.5, -0.55, 9 * k + 1, 1, clamp(t / 0.1)); if (t > 0.08) crescent(p, C, cx + 14, cy - 26, 84, 0.65, 2.6, 9 * k + 1, 1, clamp((t - 0.08) / 0.1)); }
        if (t >= 0.18) { const a = t - 0.18; if (a < 0.4) glint(p, C, cx, cy, 70 * (1 - a / 0.4) + 4); const u = a / 0.4; if (u < 1) oval(p, cx, cy, 20 + 86 * ease.outCubic(u), 20 + 86 * ease.outCubic(u), 2, u < 0.3 ? WHITE : C[6], 1.1 - u); }
      }
      ps.draw(p);
    },
  };
}
