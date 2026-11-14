// DICE BOUND 用 (2026-11-14): ヴェスカの大技のチャージ (攻撃しないターン)。 画面全体 (480×270)・原点はチャージの中心 (胸の前)。
// 運びはほかの 3 体と同じ (画面じゅうの光の筋と粒が一点へ吸い込まれる)。 完了の姿が違う: **虚の玉 (shadow/void-orb) の上に、 時計の文字盤**が開く (〈停滞する時間〉)。
// 待機 (dicebound/vesca-charged) では、 玉のまわりで渦が巻き、 粒が吸い込まれ続け、 文字盤の長針が逆に、 短針が順に、 滑らかに回る。 完了後は待機と同じ関数 held() で描く。
// 作り: 吸い込み = explosions/implosion・玉と渦と粒 = shadow/void-orb を**そのまま呼ぶ** (位置だけずらす)・文字盤 = arcane/time-stop の時計を描き直した物。
// 文字盤の作り (2026-11-14 作者の指摘で直した): 線を 1 ドットで揃えない (輪 2 ドット・大きい目盛りは太い・針は根もとが太く先が細い)・針は中心から対称に描く・
//   針はカチカチ進めず滑らかに回す・まわりの粒を止めない (止まった粒は安っぽい)。
// The gathering streaks, grit and rings are adapted from this repository's explosions/implosion; the orb, vortex and motes are its shadow/void-orb, drawn as is;
// the clock face follows its arcane/time-stop (after Sprite Fusion's open-source effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, hex, mix, clamp, ease, TAU, WHITE } from "../../core/index.js";
import * as ORB from "../shadow/void-orb.js";

export const VIOLET = ["#0a0614", "#1c1236", "#3a2470", "#5a3aa8", "#8a64e0", "#b89cf8", "#e4d8ff", "#ffffff"];

export const meta = {
  name: "Vesca charge (DICE BOUND)",
  desc: "Vesca gathers stopped time: light from the whole screen streaks and spirals to a point before her chest, and a void orb opens there under a clock face.",
  tags: ["time", "void", "dicebound", "boss", "charge", "screen", "one-shot"],
  size: [480, 270],
  origin: [240, 130],    // 敵の足もと (240,196) の 66 上
  duration: 2.75,        // 1 秒 8 コマで 23 コマ (最後のコマが 2.75 秒)
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: VIOLET, seed: 6 },
};

const HANDOFF = 2.75 + 1 / 8;   // 待機の 0 秒 = 最後のコマの 1 コマ後 (library/dicebound/judge-flow.md の「道具の罠」)
const SUCK = 1.6, VOID = hex("#05040a");
const ORB_Y = 62, ORB_D = 3.2;   // void-orb は、 原点の 62 上に玉を描く・3.2 秒で 1 周
export const HELD = 3, RC = 27, NOON = -Math.PI / 2;   // 待機の 1 周 (秒・1 秒 8 コマで 24 コマ)・文字盤の半径・12 時の向き

/** 虚の玉 (渦・曲がる光・吸い込まれる粒と筋・玉) を、 原点が玉の中心になるように描く。 tv = 玉の時刻 (待機の秒)。 k = 濃さ (網点)。 */
export function orb(p, C, tv, k = 1) {
  if (k <= 0.02) return;
  const ty = p.ty, f = p.fade, o = { palette: C, seed: 5 }, t = ((((tv * ORB_D) / HELD) % ORB_D) + ORB_D) % ORB_D;
  p.ty = ty + ORB_Y; p.fade = f * k;
  ORB.drawBack(p, t, o); ORB.draw(p, t, o);
  p.ty = ty; p.fade = f;
}

// 針 / 目盛り: 中心 (x0,y0) から向き ang へ。 a0〜a1 の範囲・幅は w0 (a0 側) から w1 (a1 側) へ細る。 中心線に対称に塗る
function bar(p, x0, y0, ang, a0, a1, w0, w1, col) {
  const ca = Math.cos(ang), sa = Math.sin(ang), r = Math.max(Math.abs(a0), Math.abs(a1)) + 3;
  p.shade(x0 - r, y0 - r, x0 + r, y0 + r, (x, y, out) => {
    const dx = x - x0, dy = y - y0, al = dx * ca + dy * sa;
    if (al < a0 || al > a1) return;
    if (Math.abs(-dx * sa + dy * ca) > (w0 + ((w1 - w0) * (al - a0)) / (a1 - a0)) / 2) return;
    out.c = col;
  });
}

/** 時計の文字盤: 2 ドットの輪・12 の目盛り (3 時間ごとは太く長い)・長針と短針 (根もとが太く、 先が細い・短い尾)・中心の留め。 open = 開き具合。 col = 全部この色で (白く抜く時)。 */
export function clock(p, C, x0, y0, aLong, aShort, open = 1, col = null) {
  const R = RC * open;
  if (R < 4) return;
  p.ring(x0, y0, R, 2, col ?? C[5]);
  p.ring(x0, y0, R + 4, 1, col ?? C[3], (v, g) => (Math.floor((g / TAU) * 60) % 2 ? 0.9 : 0));   // 外の破線の輪
  for (let i = 0; i < 12; i++) {
    const big = i % 3 === 0;
    bar(p, x0, y0, (i * TAU) / 12, R - (big ? 7 : 4), R - 1.5, big ? 3 : 1.2, big ? 3 : 1.2, col ?? (big ? C[6] : C[4]));
  }
  bar(p, x0, y0, aShort, -3, R * 0.5, 4, 2.2, col ?? C[6]);
  bar(p, x0, y0, aLong, -5, R * 0.84, 3.2, 1.2, WHITE);
  p.circle(x0, y0, 2.2, col ?? C[4]);
  p.circle(x0, y0, 1.1, WHITE);
}

/** 溜まった状態の姿 (待機のループ 1 コマぶん)。 玉の上に文字盤。 長針は逆回り・短針は順回りで、 1 周 (3 秒) に 1 回転ずつ ── 滑らかに回り続ける。 */
export function held(p, C, tv, open = 1, col = null) {
  orb(p, C, tv, Math.min(1, open));
  const a = (TAU * tv) / HELD;
  clock(p, C, 0, 0, NOON - a, NOON + a, open, col);
}

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed);
  const ps = new Particles({ rng: R, sparks: [C[7], C[6], C[5], C[4]] });
  const grit = Array.from({ length: 100 }, () => {
    const t0 = R.range(0, SUCK - 0.5);
    return { a: R.range(0, TAU), d: R.range(70, 260), t0, T: R.range(0.45, Math.max(0.5, SUCK - 0.05 - t0)), spin: R.sign() * R.range(0.4, 1.2), c: R.pick([C[3], C[4], C[5], C[6]]) };
  });
  let t = 0;
  return {
    update(dt) {
      t += dt;
      if (t < SUCK - 0.2) {
        const k = t / SUCK;
        for (let n = 0; n < 1 + Math.floor(k * 5); n++) {
          if (!R.chance(0.85)) continue;
          const a = R.range(0, TAU), D = R.range(80, 250) * (1 - 0.25 * k), T = R.range(0.2, 0.36), v = (D * 3) / (Math.exp(3 * T) - 1);
          ps.spawn(P.STREAK, Math.cos(a) * D, Math.sin(a) * D * 0.75, -Math.cos(a) * v, -Math.sin(a) * v * 0.75, T, R.chance(0.5) ? C[6] : C[5]);
        }
      }
      ps.update(dt);
    },
    draw(p) {
      const k = clamp(t / SUCK);
      if (t <= SUCK) {
        for (let i = 0; i < 4; i++) {
          const u = (t - i * 0.32) / 0.62;
          if (u <= 0 || u >= 1) continue;
          p.ring(0, 0, (150 - i * 18) * (1 - ease.inQuad(u)) + 8, 1, u > 0.7 ? C[6] : u > 0.4 ? C[5] : C[3], 0.35 + 0.65 * u);
        }
        for (const q of grit) {
          const u = (t - q.t0) / q.T;
          if (u <= 0 || u >= 1) continue;
          const d = q.d * (1 - ease.inCubic(u)), a = q.a + q.spin * ease.inQuad(u) * 2, d2 = d + 5 * u * u + 0.5;
          p.line(Math.cos(a - q.spin * 0.1) * d2, Math.sin(a - q.spin * 0.1) * d2 * 0.75, Math.cos(a) * d, Math.sin(a) * d * 0.75, (v) => (v < 0.5 ? mix(q.c, VOID, 0.4) : q.c));
        }
        // 芯: 集まるほど大きい、 黒い玉 (縁だけ明るい)。 完了で虚の玉になる
        const cr = 2 + 9 * k * k;
        p.disc(0, 0, cr + 1, VOID, 1);
        p.ring(0, 0, cr + 1, 1, k > 0.6 ? C[6] : C[4]);
        p.glow(0, 0, 14 + 26 * k, C[4], 0.2 + 0.3 * k);
      } else {
        // チャージ完了: 玉が濃くなりながら、 文字盤が開く (0.3 秒・少し行き過ぎて戻る)。 白い輪が 1 つ広がって消える。 ここからは待機と同じ絵
        const q = clamp((t - SUCK) / 0.3);
        held(p, C, t - HANDOFF, q >= 1 ? 1 : ease.outBack(q) * 0.999);
        if (q < 1) p.ring(0, 0, RC + 80 * q, 2, WHITE, 1 - q);
      }
      ps.draw(p);
    },
  };
}
