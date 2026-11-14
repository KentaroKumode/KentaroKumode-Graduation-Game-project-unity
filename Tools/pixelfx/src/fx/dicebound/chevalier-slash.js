// DICE BOUND 用 (2026-11-14): シュヴァリエの大技。 待機 (dicebound/chevalier-charged) の 0 / 6 コマ目から続く。
// きらめきが鋭く伸びる → 一点に消えて、 真っ暗な一拍 → 白く焼けた刃の縁が、 画面全体を斜めに切り裂いて抜ける (transitions/slash-wipe の前半)。
// 画面全体 (480×270)・原点は左上 (ここでは全部、 画面の左上からの座標)。
// 作り (transitions/slash-wipe の前半 = 1 太刀目): 斜めの刃の縁 (白い 1 ドット → 冷める色の段の尾)・縁の前の光・縁が噛む所から後ろへ噴く火花と削り屑・縁に乗る光・一拍遅れの点線。
// 変えた所: 画面を覆う黒を置かない (切られた後ろは素通し ── ゲーム側の暗転を、 縁の後ろから晴らす)・尾を太く (最大 8 → 46 ドット。 480 幅で 1 秒 8 コマでも読めるように)・2 太刀目と割れる所は使わない。
// The blade edge, its cooling smear, the light ahead of it, the sparks and shavings and the dotted echo are adapted from this repository's transitions/slash-wipe
// (after Sprite Fusion's open-source slash, https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, fit, clamp, ease, WHITE } from "../../core/index.js";
import { STEEL, glint, REST, HELD } from "./chevalier-charge.js";

const W = 480, H = 270, CX = 240, CY = 130;

export const meta = {
  name: "Chevalier slash (DICE BOUND)",
  desc: "The Chevalier's decisive cut: the glint before his chest stretches hard, snaps out into a beat of darkness, then a white-hot blade edge slices across the whole screen, sparks spraying back off it.",
  tags: ["blade", "slash", "dicebound", "boss", "screen", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 1.75,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: STEEL,
    sparks: ["#fff3a6", "#ffe26b", "#ff8a2a", "#f2561d"],
    lead: 0.75,     // 切る前の溜め (秒): きらめきが伸びて、 消えて、 暗い一拍
    sweep: 0.45,    // 刃が画面を渡る秒 (速く始まって、 抜け際に遅くなる)。 キレ優先で短く (元の slash-wipe は 0.62)
    tail: 46,       // 尾の太さの上限 (px)
    seed: 2,
  },
};

// 刃の縁は斜め (上が先に進む)。 SL = 傾き。 縁が行 y を横切る x = xAt(e, y)。 ゲーム側が暗転を縁の後ろから晴らす時も、 この式を使う (lab/chevalier_flow_preview.py)
export const SL = 0.55, E0 = -(H / 2) * SL - 4, E1 = W + (H / 2) * SL + 24;
const CS = 1 / Math.hypot(1, SL), HALO = 26;
const xAt = (e, y) => e + (H / 2 - y) * SL;

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed), T0 = o.lead, SWEEP = o.sweep;
  const ps = new Particles({ rng: R, sparks: o.sparks }), lights = new Lights();
  const edgeAt = (t) => E0 + (E1 - E0) * ease.outCubic(clamp((t - T0) / SWEEP));
  const speedAt = (t) => (t < T0 || t > T0 + SWEEP ? 0 : ((E1 - E0) * 3 * (1 - (t - T0) / SWEEP) ** 2) / SWEEP);
  let t = 0, acc = 0, entered = false;
  return {
    update(dt) {
      t += dt;
      const sp = speedAt(t), e = edgeAt(t);
      // 縁が噛む所から、 火花の束が後ろへ噴く。 削り屑も飛ぶ。 縁に光が乗る
      for (acc += sp * dt * 0.05; acc > 1; acc--) {
        const y = R.range(4, H - 4), x = xAt(e, y) - 1, a0 = Math.PI + R.range(-0.1, 0.6), n = R.int(6, 12);
        if (x < 0 || x >= W) continue;
        for (let i = 0; i < n; i++) {
          const a = a0 + R.range(-0.75, 0.75), v = R.range(0.2, 1) * (170 + 0.2 * sp);
          ps.spawn(P.SPARK, x, y + R.range(-2, 2), Math.cos(a) * v, Math.sin(a) * v, R.range(0.1, 0.3));   // 火花は短命に (切った後に長く残ると、 キレが鈍る)
          if (R.chance(0.4)) ps.spawn(P.MOTE, x, y, Math.cos(a) * v * 0.45, Math.sin(a) * v * 0.45 - 20, R.range(0.15, 0.35), R.chance(0.5) ? C[6] : C[5], 0.8);
        }
      }
      ps.update(dt);
      lights.update(dt);
    },
    draw(p) {
      // 溜め: きらめきが鋭く伸び (0.45 秒)、 一点へ消える (0.15 秒)。 残りは真っ暗な一拍
      if (t < T0) {
        const a = clamp(t / (T0 * 0.6)), b = clamp((t - T0 * 0.6) / (T0 * 0.2));
        const L = (REST + 3 + 110 * ease.inQuad(a)) * (1 - ease.inCubic(b));
        if (b < 1) glint(p, C, CX, CY, L);
        return;
      }
      const e = edgeAt(t), sp = speedAt(t), Ls = clamp(sp * 0.03, 3, o.tail), echo = clamp(sp / 900) * 0.6;
      if (sp > 0)
        for (let y = 0; y < H; y++) {
          const xe = xAt(e, y + 0.5), ia = clamp(Math.floor(xe - (Ls + 12) / CS), 0, W), ib = clamp(Math.ceil(xe + HALO / CS), 0, W);
          for (let x = ia; x < ib; x++) {
            const d = (xe - (x + 0.5)) * CS;
            if (d < 0) {
              // 縁のすぐ前: 光だけ
              const u = 1 + d / HALO;
              p.add(x, y, C[5], 0.32 * u * u);
              if (d > -5) p.add(x, y, WHITE, 0.3 * (1 + d / 5) ** 2);
              continue;
            }
            if (d < 2) p.put(x, y, WHITE);
            else if (d < 4) p.put(x, y, C[6]);
            else {
              // 尾: 縁から離れるほど冷める。 最後は網点でほどける (黒では覆わない)
              const q = (d - 4) / Ls;
              if (q < 1) p.put(x, y, q < 0.3 ? C[5] : q < 0.55 ? C[4] : q < 0.8 ? C[3] : C[2], q < 0.6 ? 1 : 1 - (q - 0.6) / 0.4);
              if (echo > 0 && Math.abs(d - Ls - 9) < 0.5 && (x + y) % 3 === 0) p.put(x, y, C[4], echo);   // 一拍遅れの点線
            }
            if (d < Ls + 4) p.add(x, y, C[5], 0.3 * (1 - d / (Ls + 4)));
          }
        }
      ps.draw(p);
      lights.draw(p);
    },
  };
}
