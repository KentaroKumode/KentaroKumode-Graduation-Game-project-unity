// DICE BOUND 段 4 LEGENDARY 短剣「ノクタリア」v3:
//   1. 黒蝕円の外側に出ていた放射状の短い棒線 (ツノ) を通常・会心の両方から完全撤去。
//   2. 斬撃 1 本ごとに、黒蝕円の縁〜内側に鋭い切り傷 (斬撃痕) を刻んで蓄積させる。
//   3. 最後の一撃で、溜まった斬撃痕ごと黒蝕円が砕け散って爆発する (破片弧・傷破片・衝撃輪・星屑)。
//   4. 会心は通常と明確な差を付ける: 5 コマの溜め (夜が深く沈み星が中心へ集束＋二重蝕環) → 大月弧一閃＋7 連斬 (計 10 太刀・8 傷蓄積) → 巨大な皆既蝕十字断ち＋円の超爆発 (二重衝撃輪・16 片破片・星屑の滝)。
import { Particles, Lights, P, Rng, fit, sparkle, flare, clamp, ease, rand, TAU, WHITE } from "../../core/index.js";
import { swing, slashEnd, onSwing, travel, glowAlong, sliver } from "../melee/_lib.js";
import { glowSlash } from "./_blade.js";
import { ringSlash } from "./cross-slash.js";

export const meta = {
  name: "Noctaria v3 (DICE BOUND Legendary Dagger)",
  desc: "A clean moonlit eclipse orb without outer spikes receives glowing slash scars with every dagger cut, then shatters and explodes along its accumulated scars on the final strike. Critical adds a 5-frame night-gathering windup, 10 cuts, and a massive double-ring cross shatter.",
  tags: ["dicebound", "player", "legend", "dagger", "night", "one-shot"],
  size: [188, 128],
  origin: [52, 92],
  duration: 1.65,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#12133c", "#291186", "#50487f", "#9b75ff", "#31c9ff", "#6cf1ff", "#f3feff"],
    sparks: ["#f3feff", "#6cf1ff", "#31c9ff", "#9b75ff"],
    crit: false,
    flow: 2.8,
    seed: 23,
  },
};

// 連斬配置 (通常 5 連 / 会心 7 連) と、それぞれに対応する円上の斬撃痕 (中心からの角度・オフセット・長さ比)
const CUTS = [
  [18, -32, -0.50, 1, -0.45, -0.22, 0.88],
  [36, -20,  0.88, -1, 0.82,  0.24, 0.86],
  [28, -39,  2.35, 1, -0.82,  0.10, 0.90],
  [39, -30, -1.75, -1, 1.38, -0.18, 0.85],
  [16, -21,  0.28, 1,  0.26,  0.28, 0.88],
  [33, -27,  1.50, -1, 1.55,  0.04, 0.92],
  [22, -35, -1.15, 1, -1.12, -0.10, 0.90],
];

// 夜の帳＋黒蝕円 (ツノ棒線なし・斬撃痕の刻み込み＋最後の一撃で円が爆散)
function drawNightOrbAndExplosion(p, cx, cy, R, t, t0, expT, scars, shards, C, crit, chargeEnd) {
  if (t < t0) return;

  // ---- 爆発前: 夜の帳＋黒蝕円＋蓄積する斬撃痕 ----
  if (t < expT) {
    const inCharge = crit && t < chargeEnd;
    const uOpen = ease.outCubic(clamp((t - t0) / (crit ? 0.14 : 0.045)));
    const rNow = inCharge
      ? R * (1.28 - 0.28 * ease.inQuad(clamp((t - t0) / (chargeEnd - t0))))
      : R * (0.86 + 0.14 * uOpen);

    // 夜の帳: 周囲が暗藍に沈む (会心は溜めから濃く広く沈む)
    const va = (crit ? 0.62 : 0.42) * uOpen;
    const vx = crit ? 72 : 52, vy = crit ? 46 : 33;
    for (let y = Math.floor(cy - vy); y <= Math.ceil(cy + vy); y += 2) {
      const dy = (y - cy) / vy;
      for (let x = Math.floor(cx - vx) + (y & 2 ? 1 : 0); x <= Math.ceil(cx + vx); x += 2) {
        const dx = (x - cx) / vx, d = Math.hypot(dx, dy);
        if (d > 1) continue;
        p.dot(x, y, C[0], va * (1 - d * d));
      }
    }

    // 会心のみ・溜め (f2〜f6): 外周から星十字が静かに渦巻きながら黒蝕円へ集束し、二重蝕環が脈打つ (放射棒線/ツノは一切描かない)
    if (inCharge) {
      const cu = clamp((t - t0) / (chargeEnd - t0));
      // 12 個の冷たい星十字 (+) が円の外から中心へ向かって静かに集まる
      for (let i = 0; i < 12; i++) {
        const a = (i / 12) * TAU + cu * 0.95;
        const dist = rNow * (1.72 - 1.12 * ease.inQuad(cu)) + (i % 2 ? 3 : -2);
        const sx = cx + Math.cos(a) * dist, sy = cy + Math.sin(a) * dist;
        const arm = (1.6 + 1.6 * cu) * (i % 2 === 0 ? 1.0 : 0.72);
        const col = cu > 0.62 ? WHITE : cu > 0.3 ? C[5] : C[4];
        p.line(sx - arm, sy, sx + arm, sy, col, 0.55 + 0.45 * cu);
        p.line(sx, sy - arm, sx, sy + arm, col, 0.55 + 0.45 * cu);
        p.px(sx, sy, WHITE);
      }
      // 収束する外側の二重蝕環 (純粋な円点)
      const rOut = rNow * (1.44 - 0.44 * ease.inQuad(cu));
      const oSteps = Math.ceil(rOut * TAU * 1.15);
      for (let i = 0; i < oSteps; i++) {
        const a = (i / oSteps) * TAU;
        p.dot(cx + Math.cos(a) * rOut, cy + Math.sin(a) * rOut, cu > 0.65 ? C[5] : C[3], 0.45 + 0.5 * cu);
      }
    }

    // 黒蝕の円本体 (深い夜藍・縁のわずかな紫陰影)
    const da = (crit ? 0.76 : 0.62) * uOpen;
    p.shade(cx - rNow, cy - rNow, cx + rNow, cy + rNow, (x, y, out) => {
      const d = Math.hypot(x - cx, y - cy);
      if (d >= rNow - 0.5) return;
      out.c = d > rNow - 2.3 && x < cx - rNow * 0.28 ? C[1] : C[0];
      out.a = da;
    });

    // 極細の月光リング (ツノなし！純粋に美しい円環)
    const steps = Math.ceil(rNow * TAU * 1.6);
    for (let i = 0; i < steps; i++) {
      const a = (i / steps) * TAU;
      const x = cx + Math.cos(a) * rNow, y = cy + Math.sin(a) * rNow;
      const nearBead = Math.abs(Math.atan2(Math.sin(a + 0.72), Math.cos(a + 0.72))) < 0.55;
      p.dot(x, y, nearBead ? WHITE : C[5], 0.92);
    }

    // ---- 円に刻まれた斬撃痕 (太刀ごとに 1 本ずつ増え、爆発直前には白く脈打つ！) ----
    let activeScars = 0;
    for (const sc of scars) {
      if (t < sc.t0) continue;
      activeScars++;
      const age = t - sc.t0;
      const fresh = age < 0.045; // 刻まれた瞬間 1 コマだけ白熱
      const ca = Math.cos(sc.ang), sa = Math.sin(sc.ang);
      const nx = -sa, ny = ca;
      const off = sc.off * rNow;
      const halfL = Math.sqrt(Math.max(4, rNow * rNow - off * off)) * sc.lenMul;
      const mx = cx + nx * off, my = cy + ny * off;
      const x0 = mx - ca * halfL, y0 = my - sa * halfL;
      const x1 = mx + ca * halfL, y1 = my + sa * halfL;
      // 切り傷の縁 (暗い亀裂 + 白〜氷青の裂け目芯)
      p.line(x0, y0, x1, y1, fresh ? WHITE : C[5], fresh ? 1.0 : 0.92);
      // 中央 60% は少し太く光らせて「円が裂けている」感を見せる
      const hx0 = mx - ca * halfL * 0.62, hy0 = my - sa * halfL * 0.62;
      const hx1 = mx + ca * halfL * 0.62, hy1 = my + sa * halfL * 0.62;
      p.line(hx0 + nx * 0.7, hy0 + ny * 0.7, hx1 + nx * 0.7, hy1 + ny * 0.7, fresh ? C[6] : C[4], fresh ? 0.95 : 0.75);
      // 傷が円の縁を切った 2 点に小さな切断面ハイライト
      p.px(x0, y0, WHITE);
      p.px(x1, y1, WHITE);
      if (fresh) {
        p.glowLine(x0, y0, x1, y1, 4.5, C[5], 0.42);
      } else {
        p.glowLine(hx0, hy0, hx1, hy1, 3.0, C[4], 0.22);
      }
    }

    // 溜めピーク (会心 f6) の中心星光
    if (inCharge) {
      const cu = clamp((t - t0) / (chargeEnd - t0));
      if (cu > 0.45) {
        const pk = (cu - 0.45) / 0.55;
        sparkle(p, cx, cy, 11.5 * pk, WHITE, C[5], 0.85 * pk);
        p.glow(cx, cy, 10, C[5], 0.40 * pk);
      }
    }

    // ダイヤモンド・ビード (縁の 1 点に瞬く冷たい星光・傷が増えるほど不安定に強まる)
    const bx = cx + Math.cos(-0.72) * rNow, by = cy + Math.sin(-0.72) * rNow;
    const bk = Math.min(1, 0.55 + activeScars * 0.07);
    sparkle(p, bx, by, (crit ? 8.5 : 6.5) * bk, WHITE, C[5], 0.65 * bk);
    p.glow(bx, by, crit ? 8.5 : 6.5, C[5], 0.34 * bk);
    return;
  }

  // ---- 爆発 (最後の一撃で溜まった傷ごと黒蝕円が砕け散る！) ----
  const eu = (t - expT) / (crit ? 0.29 : 0.21);
  if (eu >= 1) return;

  // 爆発の瞬間 1 コマ (eu < 0.14): 夜の円全体が白熱して膨張→爆散する
  const expThick = t < expT + 0.028;
  // 1. 爆散する衝撃輪 (1 コマ太帯 → 即 1/5 極細輪 → 点間引き)
  const rWave = R * (0.95 + (crit ? 1.55 : 0.95) * ease.outCubic(eu));
  const wDf = clamp((eu - 0.36) / 0.64);
  if (wDf < 1) {
    if (expThick) {
      const th = crit ? 10.5 : 6.5;
      p.shade(cx - rWave - 2, cy - rWave - 2, cx + rWave + 2, cy + rWave + 2, (x, y, out) => {
        const d = Math.hypot(x - cx, y - cy);
        const q = (rWave - d) / th;
        if (q < 0 || q > 1) return;
        out.c = q < 0.35 ? WHITE : q < 0.7 ? C[5] : C[4];
        out.a = 1;
        if (q < 0.4) { out.l = WHITE; out.k = crit ? 1.15 : 0.85; }
      });
      // 円弧に沿って光らせる (丸い塊にしない)
      for (let i = 0; i < 10; i++) {
        const a = (i / 10) * TAU;
        p.glow(cx + Math.cos(a) * rWave, cy + Math.sin(a) * rWave, crit ? 7 : 5, C[5], crit ? 0.38 : 0.28);
      }
    } else if (wDf < 0.35) {
      const steps = Math.ceil(rWave * TAU * 1.4);
      for (let i = 0; i < steps; i++) {
        const a = (i / steps) * TAU;
        p.dot(cx + Math.cos(a) * rWave, cy + Math.sin(a) * rWave, eu < 0.3 ? C[6] : C[5], 1 - eu * 0.7);
      }
      for (let i = 0; i < 8; i++) {
        const a = (i / 8) * TAU;
        p.glow(cx + Math.cos(a) * rWave, cy + Math.sin(a) * rWave, 4, C[4], 0.22 * (1 - wDf / 0.35));
      }
    } else {
      // 純点線で無発光
      const f = (wDf - 0.35) / 0.65;
      const steps = Math.ceil(rWave * TAU * 1.3);
      for (let i = 0; i < steps; i++) {
        const a = (i / steps) * TAU;
        if (rand(i * 19 + 7, 11, 23) < Math.pow(f, 0.85)) continue;
        p.dot(cx + Math.cos(a) * rWave, cy + Math.sin(a) * rWave, f < 0.5 ? C[4] : C[3], Math.max(0.28, 1 - f * 0.65));
      }
    }
  }

  // 会心のみ: 内側にもう 1 つ遅れて広がる第二の紫青衝撃輪
  if (crit && eu > 0.08 && eu < 0.88) {
    const u2 = (eu - 0.08) / 0.80;
    const r2 = R * (0.55 + 1.25 * ease.outCubic(u2));
    const df2 = clamp((u2 - 0.35) / 0.65);
    const steps = Math.ceil(r2 * TAU * 1.2);
    for (let i = 0; i < steps; i++) {
      const a = (i / steps) * TAU;
      if (df2 > 0 && rand(i * 13 + 3, 17, 29) < Math.pow(df2, 0.85)) continue;
      p.dot(cx + Math.cos(a) * r2, cy + Math.sin(a) * r2, u2 < 0.35 ? C[5] : C[3], Math.max(0.28, 1 - u2 * 0.75));
    }
  }

  // 2. 砕け散る黒蝕円の弧破片 (通常 8 片 / 会心 16 片・点間引き消滅)
  const sDf = clamp((eu - 0.38) / 0.62);
  for (let si = 0; si < shards.length; si++) {
    const sh = shards[si];
    const dist = R * 0.85 + sh.v * (t - expT) * (1 - 0.35 * eu);
    const arcNow = sh.arc * (1 - 0.35 * eu);
    if (sDf > 0.25) {
      const f = (sDf - 0.25) / 0.75;
      const n = Math.max(3, Math.ceil(dist * arcNow * 0.8));
      for (let i = 0; i <= n; i++) {
        if (rand(si * 23 + i * 11, 7, 31) < Math.pow(f, 0.85)) continue;
        const a = sh.a + (i / n - 0.5) * arcNow;
        p.dot(cx + Math.cos(a) * dist, cy + Math.sin(a) * dist, f < 0.5 ? C[4] : C[3], Math.max(0.28, 1 - f * 0.65));
      }
    } else {
      const col = eu < 0.18 ? WHITE : eu < 0.45 ? C[5] : C[4];
      sliver(p, cx, cy, sh.a, dist, arcNow, col, 1 - eu * 0.65);
      // 破片の内側に黒い欠片の影を一瞬だけ添える
      if (eu < 0.28) {
        sliver(p, cx, cy, sh.a, Math.max(2, dist - 2.2), arcNow * 0.82, C[1], 0.7 * (1 - eu / 0.28));
      }
    }
  }

  // 3. 刻まれていた斬撃痕そのものが外へ弾け飛ぶ
  if (eu < 0.62) {
    const su = eu / 0.62;
    for (let i = 0; i < scars.length; i++) {
      const sc = scars[i];
      if (expT < sc.t0) continue;
      const ca = Math.cos(sc.ang), sa = Math.sin(sc.ang), nx = -sa, ny = ca;
      const push = (crit ? 26 : 16) * ease.outCubic(su);
      const dirSign = (i % 2 === 0 ? 1 : -1);
      const off = sc.off * R + dirSign * push * 0.55;
      const halfL = R * 0.55 * (1 - su * 0.65);
      const mx = cx + nx * off + ca * dirSign * push * 0.45;
      const my = cy + ny * off + sa * dirSign * push * 0.45;
      if (su > 0.45) {
        const f = (su - 0.45) / 0.55;
        const n = Math.max(2, Math.ceil(halfL));
        for (let k = -n; k <= n; k++) {
          if (rand(i * 19 + k * 7, 5, 29) < Math.pow(f, 0.85)) continue;
          const u = k / n;
          p.dot(mx + ca * halfL * u, my + sa * halfL * u, C[4], Math.max(0.28, 1 - f * 0.65));
        }
      } else {
        p.line(mx - ca * halfL, my - sa * halfL, mx + ca * halfL, my + sa * halfL, su < 0.2 ? WHITE : C[5], 1 - su * 0.5);
      }
    }
  }

  // 4. 爆心の締まった星十字閃光 (丸い光は小さく・会心は大きく鋭い星閃光)
  if (eu < 0.35) {
    const fk = 1 - eu / 0.35;
    flare(p, cx, cy, (crit ? 26 : 14) * Math.pow(fk, 1.3), fk, C);
    sparkle(p, cx, cy, (crit ? 18 : 11) * fk, WHITE, C[5], 0.85 * fk);
    p.glow(cx, cy, crit ? 7.5 : 6, C[5], (crit ? 0.36 : 0.26) * fk);
  }
}

export function create(o) {
  const C = fit(o.palette, 7), Rg = new Rng(o.seed), ps = new Particles({ rng: Rg, ground: () => 0, sparks: o.sparks }), lights = new Lights();
  const crit = !!o.crit;
  // speed=0.72 → 1 コマ = 0.0300s
  // 通常: f2 降下 → f3,5,7,9,11 (5 連斬・各 1 コマ太面・5 傷蓄積) → f14 締め月弧＋円爆発 (1 コマ太面) → 余韻 f15〜f21
  // 会心: f2〜f6 溜め (5 コマ) → f7 大月弧一閃 (1 コマ太面・1 傷) → f9,11,13,15,17,19,21 (7 連斬・計 8 傷蓄積) → f23 巨大皆既蝕十字断ち＋円超爆発 (1 コマ太面) → 余韻 f24〜f31
  const count = crit ? 7 : 5;
  const chargeEnd = crit ? 0.190 : 0.045;
  const wideT0 = 0.192; // f7 = 0.210s に 1 コマ太面
  const T_OFF = crit ? 0.240 : 0.060; // 会心 f9=0.270s / 通常 f3=0.090s から 2 コマ刻み
  const ev = 0.060;

  const WIDE = crit ? swing({
    cx: 27, cy: -27, R: 54, sy: 0.28, k: 0.18, rot: -0.06, a0: 0.14, dir: 1, span: 3.0,
    W: 22, t0: wideT0, sweep: 0.020, hold: 0.012, trail: 0.14, ramp: C, sharp: true, wideSnap: true, flareDur: 0.125,
  }) : null;

  const small = CUTS.slice(0, count).map(([x, y, rot, dir], i) => swing({
    cx: x, cy: y, R: Rg.range(15, 19), sy: Rg.range(0.55, 0.72), rot, a0: Rg.range(-2.8, -2.2), dir, span: Rg.range(2.4, 2.9),
    W: Rg.range(5.6, 6.8) * (crit ? 1.65 : 1.5),
    t0: T_OFF + i * ev,
    sweep: 0.014, hold: 0.016, trail: 0.06, ramp: C,
    snap: true, snapDur: 0.006, snapCut: 0.82, glowScale: crit ? 1.12 : 0.96, traceFade: 0.025,
  }));

  const finT0 = T_OFF + count * ev + 0.012; // 通常: 0.372 → f14=0.390s / 会心: 0.672 → f24=0.690s
  const big = swing({
    cx: 26, cy: -26, R: crit ? 46 : 36, sy: 0.34, k: 0.22, rot: 0.10, a0: 3.0, dir: -1, span: 2.95,
    W: crit ? 28 : 22, t0: finT0, sweep: 0.013, hold: 0.017, trail: 0.16, ramp: C,
    snap: true, snapDur: 0.006, snapCut: 0.83, glowScale: crit ? 1.25 : 1.05, traceFade: 0.15, dotFade: true,
  });
  const shadowArc = swing({
    cx: 26, cy: -23, R: crit ? 42 : 32, sy: 0.34, k: 0.18, rot: 0.10, a0: 2.95, dir: -1, span: 2.8,
    W: crit ? 13 : 11, t0: finT0 + 0.003, sweep: 0.013, hold: 0.014, trail: 0.09, ramp: [C[0], C[0], C[1], C[2], C[3], C[4], C[5]],
    snap: true, snapDur: 0.006, snapCut: 0.85, glowScale: 0.65, traceFade: 0.04, dotFade: false,
  });
  const vert = crit ? swing({
    cx: 27, cy: -27, R: 43, sy: 0.34, k: 0.10, rot: Math.PI / 2 - 0.10, a0: 2.95, dir: -1, span: 2.9,
    W: 24, t0: finT0, sweep: 0.013, hold: 0.017, trail: 0.155, ramp: C,
    snap: true, snapDur: 0.006, snapCut: 0.84, glowScale: 1.25, traceFade: 0.15, dotFade: true,
  }) : null;

  // 各斬撃と同時に円へ刻まれる斬撃痕リスト
  const scars = [];
  if (WIDE) {
    scars.push({ t0: wideT0 + 0.010, ang: -0.06, off: 0.0, lenMul: 1.0 });
  }
  for (let i = 0; i < count; i++) {
    const [, , , , ang, off, lenMul] = CUTS[i];
    scars.push({ t0: small[i].t0 + 0.008, ang, off, lenMul });
  }
  const expT = finT0 + 0.012; // 最後の一撃の着弾と同時に円が爆発！
  const nShards = crit ? 16 : 8;
  const shards = Array.from({ length: nShards }, (_, i) => ({
    a: (i / nShards) * TAU + Rg.range(-0.14, 0.14),
    v: Rg.range(crit ? 115 : 80, crit ? 220 : 145),
    arc: Rg.range(0.24, crit ? 0.44 : 0.52),
  }));

  const all = vert ? [...small, shadowArc, big, vert] : [...small, shadowArc, big];
  const ends = all.map(slashEnd), done = all.map(() => false);

  const stars = [];
  const spawnStars = (s, n, lifeRange) => {
    for (let k = 0; k < n; k++) {
      const tr = travel(s, s.span * Rg.range(0.15, 0.98));
      stars.push({
        x: tr.x + Rg.range(-3, 3), y: tr.y + Rg.range(-3, 3),
        vx: tr.dx * Rg.range(8, 26) + Rg.range(-8, 8),
        vy: Rg.range(-6, 14),
        t0: t, life: Rg.range(lifeRange[0], lifeRange[1]),
        sz: Rg.range(2.2, 4.6), bright: k % 2 === 0,
      });
    }
  };
  const spawnExplosionStars = (cx, cy, n) => {
    for (let k = 0; k < n; k++) {
      const a = (k / n) * TAU + Rg.range(-0.2, 0.2), sp = Rg.range(crit ? 60 : 35, crit ? 165 : 105);
      stars.push({
        x: cx + Math.cos(a) * Rg.range(4, 18), y: cy + Math.sin(a) * Rg.range(4, 18),
        vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
        t0: t, life: Rg.range(crit ? 0.16 : 0.12, crit ? 0.30 : 0.22),
        sz: Rg.range(crit ? 2.8 : 2.2, crit ? 5.8 : 4.2), bright: k % 2 === 0,
      });
    }
  };

  let t = 0, exploded = false;
  return {
    update(dt) {
      t += dt;
      if (!exploded && t >= expT) {
        exploded = true;
        spawnExplosionStars(27, -27, crit ? 22 : 10);
        for (let i = 0; i < (crit ? 14 : 6); i++) {
          const a = (i / (crit ? 14 : 6)) * TAU + Rg.range(-0.15, 0.15), sp = Rg.range(crit ? 120 : 80, crit ? 260 : 180);
          ps.spawn(P.SPARK, 27 + Math.cos(a) * 10, -27 + Math.sin(a) * 10, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.06, 0.14));
        }
      }
      all.forEach((s, i) => {
        if (done[i] || t < s.t0 + s.sweep || s === shadowArc) return;
        done[i] = true;
        const e = ends[i], last = s === big;
        lights.add(e.x, e.y, last ? 11 : 7, C[4], last ? 0.34 : 0.22, last ? 0.065 : 0.035);
        if (!last && s !== vert) {
          spawnStars(s, 2, [0.09, 0.15]);
        }
        if (last) {
          lights.add(e.x, e.y, 6, WHITE, 0.44, 0.035);
          spawnStars(s, crit ? 8 : 5, [0.14, 0.25]);
          if (vert) spawnStars(vert, 6, [0.13, 0.24]);
          const lives = [0.13, 0.17, 0.21, 0.25], fracs = [0.45, 0.65, 0.82, 0.96];
          for (let k = 0; k < lives.length; k++) {
            const tr = travel(s, s.span * fracs[k]), sp = Rg.range(38, 72);
            ps.spawn(P.MOTE, tr.x, tr.y, tr.dx * sp, tr.dy * sp, lives[k], k & 1 ? C[5] : C[4], 0.55);
          }
        }
      });
      for (let i = stars.length - 1; i >= 0; i--) {
        const st = stars[i];
        if (t - st.t0 > st.life) { stars.splice(i, 1); continue; }
        st.x += st.vx * dt;
        st.y += st.vy * dt;
        st.vx -= st.vx * 5 * dt;
        st.vy -= st.vy * 5 * dt;
      }
      ps.update(dt);
      for (const m of ps.list) if (m.k === P.MOTE) { m.vx -= m.vx * 6.5 * dt; m.vy -= m.vy * 6.5 * dt; }
      lights.update(dt);
    },
    draw(p) {
      // 夜の帳＋黒蝕円 (ツノなし・斬撃痕刻み込み・最後の一撃で爆発)
      drawNightOrbAndExplosion(p, 27, -27, crit ? 28 : 23, t, 0.015, expT, scars, shards, C, crit, chargeEnd);

      if (WIDE) {
        ringSlash(p, WIDE, t, wideT0 + 0.036);
        const f = (t - (wideT0 + 0.036)) / WIDE.flareDur;
        if (t > wideT0 + 0.008 && f > -0.3 && f < 1) {
          glowAlong(p, WIDE, WIDE.R - 1, 0, WIDE.span, 5.5, C[6], (uu) => 0.34 * (1 - Math.max(0, f)) * Math.pow(Math.sin(Math.PI * uu / WIDE.span), 1.6), 0, 0.12);
        }
      }

      for (const s of all) glowSlash(p, s, t, o.flow, crit ? 1.22 : 1.0);

      // 静かに瞬く冷たい星屑
      for (const st of stars) {
        const u = (t - st.t0) / st.life;
        const k = Math.sin(u * Math.PI);
        if (u > 0.68 || k < 0.25) {
          p.dot(st.x, st.y, C[4], Math.max(0.3, (1 - u) * 2.2));
        } else {
          const arm = Math.max(1.2, st.sz * 0.72 * k);
          const col = st.bright ? WHITE : C[5];
          p.line(st.x - arm, st.y, st.x + arm, st.y, col, k);
          p.line(st.x, st.y - arm, st.x, st.y + arm, col, k);
          p.px(st.x, st.y, WHITE);
          p.glow(st.x, st.y, 2.4, C[4], 0.26 * k);
        }
      }

      all.forEach((s, i) => {
        if (s === shadowArc) return;
        const e = ends[i], g = t - s.t0 - s.sweep * 0.6, last = s === big, life = last ? 0.08 : 0.04;
        if (g > 0 && g < life) sparkle(p, e.x, e.y, (last ? 9.5 : 3.5) * ease.outBack(Math.min(1, g / 0.02)) * (1 - g / life), WHITE, C[4], last ? 0.65 : 0.35);
        const u = t - s.t0, lDur = s.sweep + (last ? 0.05 : 0.03);
        if (u > 0 && u < lDur) {
          const h = slashEnd({ ...s, span: s.span * ease.outExpo(clamp(u / s.sweep)) });
          p.glow(h.x, h.y, last ? 11 : 7.5, C[4], (last ? 0.32 : 0.22) * (1 - u / lDur));
        }
      });
      ps.draw(p);
      lights.draw(p);
    },
  };
}
