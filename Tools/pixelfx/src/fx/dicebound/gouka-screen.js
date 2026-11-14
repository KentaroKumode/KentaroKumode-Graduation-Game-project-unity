// DICE BOUND 用 (2026-11-14): 灰燼の王の大技〈業火〉が、 こちら (画面の手前にいるプレイヤー) へ届く。 画面全体の効果。
// 画面の縁 ── とくに下から ── 火が焼け込んで来て、 白く焼けた線の上に炎が立ち、 火の粉と灰が画面を上へ抜け、 焦げた縁が冷めて消える。
// 敵の側の効果 (dicebound/gouka-impact) と同時に流す。 これは画面 (480×270) の左上が原点。
//
// 作りは transitions/burn-away の「縁から焼け込む」半分を借りた (library/dicebound/gouka-screen.md):
//   焼ける順を決める場 (縁から内へ・2 つの大きさのノイズでぎざぎざに)・白く焼けた線から焦げへ冷める色の段・線の上に立つ炎の舌・
//   線が通った所から出る火の塊 / 残り火 / 灰 / 煙 / 光。 変えたのは、 画面を覆い切らずに途中で止まって冷めること、 下ほど深く速く焼けること、 画面の大きさ。
// The burn field, ember line, standing tongues and what the edge sheds are adapted from this repository's transitions/burn-away,
// which follows Sprite Fusion's open-source fire (https://www.spritefusion.com/pixel-art-vfx/fire), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Lights, P, Rng, fit, hex, mix, clamp, smooth, ease, fbm, noise, noise2, rand, WHITE } from "../../core/index.js";

const W = 480, H = 270;

export const meta = {
  name: "Gouka screen (DICE BOUND)",
  desc: "The Ash King's inferno reaches the viewer: fire burns in from the screen's edges, deepest from below, flames standing on a white-hot line, embers and ash streaming up across the screen, then the scorched rim cools and clears.",
  tags: ["fire", "dicebound", "boss", "screen", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 6.0,
  loop: false,
  fps: 30,
  bg: "sky",
  options: {
    palette: ["#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff"],
    ashes: ["#a39cab", "#7b7484", "#5c5664", "#433e4b"],
    color: "#0b0a10",   // 焼けた跡 (焦げ) の色
    mode: "in",         // "in" = 縁から焼け込んで、 止まって冷める / "out" = 当たった時にはもう燃えていて、 外へ燃え尽きる (白く飛んだコマから続ける)
    bias: 1,            // 下への偏り (1 = 下が深い・0 = 上下が均等)
    depth: 1,           // どこまで焼け込むか (1 = 既定。 下は画面の 1/3 ほど・左右と上は縁だけ)
    lead: 0.9,          // 溜め (秒): 当たる前に、 縁がじわっと赤く焼け始め、 火の粉がゆっくり上がる
    rush: 0.8,          // 焼け込みにかける秒
    hold: 0.9,          // 焼け込んだまま燃えている秒 (線はゆっくり進み続ける)
    cool: 1.3,          // 冷めて消えるまでの秒
    ash: 3,             // 烈炎の数。 多いほど、 灰が舞い、 焦げが長く残る
    seed: 3,
  },
};

const SC = 90, HALO = 30, POOL = 60;
// 炎の舌の層: [幅, 高さ, 色の段]。 外 (赤) から白い芯へ (burn-away と同じ・色の段は暗い順の番号)
const LAYERS = [[1, 1, 2], [0.76, 0.86, 4], [0.54, 0.68, 6], [0.3, 0.42, 8]];

/** 画面の上の値の場と、 その勾配 (burn-away の field を、 画面の大きさを引数にした物)。 */
function field(fn, gmin = 0.45, gmax = 3) {
  const f = new Float32Array(W * H), g = new Float32Array(W * H), ny = new Float32Array(W * H);
  let lo = Infinity, hi = -Infinity;
  for (let y = 0; y < H; y++)
    for (let x = 0; x < W; x++) {
      const v = fn(x + 0.5, y + 0.5);
      f[y * W + x] = v;
      if (v < lo) lo = v;
      if (v > hi) hi = v;
    }
  for (let y = 0; y < H; y++)
    for (let x = 0; x < W; x++) {
      const xa = Math.max(0, x - 1), xb = Math.min(W - 1, x + 1), ya = Math.max(0, y - 1), yb = Math.min(H - 1, y + 1);
      const gx = (f[y * W + xb] - f[y * W + xa]) / (xb - xa), gy = (f[yb * W + x] - f[ya * W + x]) / (yb - ya), L = Math.hypot(gx, gy);
      g[y * W + x] = 1 / clamp(L, gmin, gmax);
      ny[y * W + x] = L > 1e-6 ? gy / L : 0;
    }
  const order = Uint32Array.from({ length: W * H }, (_, i) => i).sort((a, b) => f[a] - f[b]);
  const sorted = Float32Array.from(order, (i) => f[i]);
  const below = (v) => {
    let a = 0, b = sorted.length;
    while (a < b) { const m = (a + b) >> 1; if (sorted[m] < v) a = m + 1; else b = m; }
    return a;
  };
  return { f, g, ny, lo, hi, order, below };
}

function tongue(p, F, x, y, h, w, sway, t, n) {
  for (const [lw, lh, ri] of LAYERS) {
    const Ht = h * lh, col = F[ri];
    for (let r = 0; r <= Ht; r++) {
      const u = r / Math.max(1, Ht), half = w * lw * Math.pow(1 - u, 0.85) * Math.min(1, (r + 1.5) / 3.5) + noise(t * 9 + r * 0.45, n) * 0.5;
      if (half < 0.3) continue;
      const cx = x + sway * u * u * lh + noise(t * 4 + r * 0.2, n + 7) * u * 1.1;
      for (let px = Math.round(cx - half); px <= Math.round(cx + half); px++) p.px(px, Math.round(y - r), col);
    }
  }
}

export function create(o) {
  const F = fit(o.palette, 9), ASHC = fit(o.ashes, 4), ink = hex(o.color), R = new Rng(o.seed), seed = o.seed * 13, ASH = Math.max(0, o.ash), BIAS = o.bias;
  const CHAR = mix(ink, F[0], 0.45), SCORCH = mix(ink, hex("#3a2216"), 0.6), WARM = mix(F[4], F[5], 0.4), SMOKE = mix(hex("#8a7276"), F[2], 0.2);
  // 焼ける順: 縁から内へ (角が先)。 下ほど値が小さい = 先に・深く焼ける (火は下から来る)。 2 つの大きさのノイズで線をぎざぎざに
  const A = field((x, y) => {
    const ux = (x - W / 2) / (W / 2), uy = (y - H / 2) / (H / 2), m = Math.pow(ux ** 4 + uy ** 4, 0.25);
    return (1 - m) * SC + 14 * fbm(x / 44, y / 44, seed, 3) + 2.4 * fbm(x / 7, y / 7, seed + 9, 2) + BIAS * (-46 * smooth(0.35, 1, y / H) + 10 * smooth(0.5, 0, y / H));
  });
  const heat = new Float32Array(W * H), ph = new Uint8Array(W * H), fleck = new Float32Array(W * H), jit = new Float32Array(W * H);
  for (let i = 0; i < W * H; i++) {
    const x = i % W, y = (i / W) | 0;
    heat[i] = 0.5 + 0.5 * noise2(x / 13, y / 13, seed + 41);
    ph[i] = Math.min(63, Math.floor((0.5 + 0.5 * noise2(x / 23, y / 23, seed + 43)) * 64));
    fleck[i] = rand(x, y, seed + 45) < 0.04 ? 9 + 22 * rand(x, y, seed + 47) : 0;
    jit[i] = (rand(x, y, seed + 49) - 0.5) * 1.5;
  }
  const FL = new Float32Array(64), HV = new Float32Array(64), TW = new Uint8Array(64);
  // 前線の位置: 0.3 秒で一気に焼け込み (速く始まって止まる)、 そこで止まる。 そのあと全体が冷めて消える
  const BURST = Math.max(0.05, o.lead), RUSH = o.rush, HOLD = BURST + RUSH + o.hold, END = HOLD + o.cool + 0.06 * ASH;
  const PEAK = A.lo + (22 + 20 * o.depth), BASE = A.lo + 5;
  const OUT = o.mode === "out", GONE = A.lo - 12, T_OUT = BURST + o.hold + RUSH + o.cool;
  // out: 白く飛んでいる間に、 もう深くまで燃えている。 しばらく燃えてから、 火の線が外 (縁) へ退いて、 燃え尽きる
  const kOut = (t) => PEAK + 3 * Math.sin((t - BURST) * 3) * (1 - smooth(0, 1, clamp((t - BURST - o.hold) / (RUSH + o.cool)))) - (PEAK - GONE) * smooth(0, 1, clamp((t - BURST - o.hold) / (RUSH + o.cool)));
  const kIn = (t) => t < BURST ? A.lo - 8 + 13 * smooth(0, BURST, t) : BASE + (PEAK - BASE) * ease.outCubic(clamp((t - BURST) / RUSH)) + 7 * clamp((t - BURST - RUSH) / Math.max(0.01, o.hold + o.cool));
  const kAt = (t) => (OUT && t >= BURST ? kOut(t) : kIn(t));
  const fadeAt = (t) => (OUT ? 1 - smooth(T_OUT - 0.35, T_OUT, t) : 1 - smooth(HOLD, END, t));

  const ps = new Particles({ rng: R, sparks: [F[7], F[6], F[4], F[2]] }), ash = new Particles({ rng: R, sparks: ASHC }), lights = new Lights();
  const line = [];
  let t = 0, pin = 0, burst = false, pre = 0, jumped = false;
  const left = (life) => Math.min(life, meta.duration - 0.05 - t);
  // 前線が通った画素が出す物: [火の塊, 残り火, 煙, 灰, 光] (画素 1 つあたりの確率)
  const RATE = [0.01, 0.0035, 0.0012, 0.0009 * ASH, 0.0005];
  const shed = (i) => {
    const x = (i % W) + 0.5, y = ((i / W) | 0) + 0.5;
    let r = R.next();
    if ((r -= RATE[0]) < 0) { const life = left(R.range(0.18, 0.42)); if (life > 0.08) ps.spawn(P.FIRE, x, y - 3, R.range(-10, 10), -R.range(60, 140), life, 0, R.range(1.2, 2.8)); }
    else if ((r -= RATE[1]) < 0) { const life = left(R.range(0.5, 1.2)); if (life > 0.2) ps.spawn(P.EMBER, x, y - 2, R.range(-40, 40), -R.range(80, 220), life, 0, R.chance(0.25) ? 2 : 1); }
    else if ((r -= RATE[2]) < 0) {
      const n = R.int(3, 5), vx = R.range(-6, 6);
      for (let j = 0; j < n; j++) { const life = left(R.range(0.45, 0.9)); if (life > 0.3) ps.spawn(P.SMOKE, x + R.range(-3.5, 3.5), y - 4 - j * 1.2, vx + R.range(-3, 3), -R.range(30, 60), life, SMOKE, R.range(3, 5.5), j * 0.05); }
    } else if ((r -= RATE[3]) < 0) { const life = left(R.range(0.9, 1.6)); if (life > 0.3) ash.spawn(P.EMBER, x, y - 2, R.range(-30, 30), -R.range(60, 170), life, 0, R.chance(0.3) ? 2 : 1); }
    else if ((r -= RATE[4]) < 0) lights.add(x, y - 4, R.range(26, 44), WARM, R.range(0.14, 0.26), R.range(0.18, 0.4));
  };

  return {
    update(dt) {
      t += dt;
      // 溜めの間: 下の縁から、 火の粉がゆっくり、 だんだん多く上がる
      if (t < BURST && BURST > 0.2) { pre += dt * (8 + 50 * t / BURST); while (pre >= 1) { pre--; ps.spawn(P.EMBER, R.range(0, W), H + R.range(0, 6), R.range(-12, 12), -R.range(40, 110), R.range(0.8, 1.6), 0, 1); } }
      if (!burst && t >= BURST) {
        burst = true;
        // 下の縁から、 火の粉と灰が画面を上へ抜ける (こちらへ吹き付ける熱風)
        for (let i = 0; i < 70; i++) ps.spawn(P.EMBER, R.range(0, W), H + R.range(0, 20), R.range(-60, 60), -R.range(220, 520), R.range(0.5, 1.2), 0, R.chance(0.3) ? 2 : 1, R.range(0, 0.25));
        for (let i = 0; i < ASH * 14; i++) ash.spawn(P.EMBER, R.range(0, W), H + R.range(0, 30), R.range(-50, 50), -R.range(140, 380), R.range(0.8, 1.6), 0, R.chance(0.35) ? 2 : 1, R.range(0, 0.4));
      }
      const k = kAt(t);
      if (OUT && t >= BURST) {
        // 線が外へ退く: 通り過ぎた画素から、 火の粉や灰が出る (切り替わった瞬間のぶんは出さない)
        if (!jumped) { jumped = true; pin = A.below(k); }
        while (pin > 0 && A.f[A.order[pin - 1]] > k) shed(A.order[--pin]);
        while (pin < A.order.length && A.f[A.order[pin]] <= k) pin++;
      } else while (pin < A.order.length && A.f[A.order[pin]] <= k) shed(A.order[pin++]);
      ps.update(dt);
      ash.update(dt);
      lights.update(dt);
    },
    draw(p) {
      line.length = 0;
      const k = kAt(t), fade = fadeAt(t), calm = t < BURST ? 0.35 : 1, f = A.f, g = A.g;
      if (fade <= 0) { ash.draw(p, "front"); ps.draw(p, "front"); return; }
      // 当たった瞬間: 画面全体が熱で明るむ (光だけ・1〜2 コマ)
      const flash = t < BURST ? 0 : 1 - clamp((t - BURST) / 0.14);
      if (flash > 0) for (let y = 0; y < H; y += 1) for (let x = (y & 1); x < W; x += 2) p.add(x, y, WARM, 0.5 * flash * (0.35 + 0.65 * y / H));
      for (let j = 0; j < 64; j++) {
        const q = j / 63;
        FL[j] = 0.75 + 0.25 * Math.sin(t * 7 + q * 9);
        HV[j] = 0.3 * Math.sin(t * 5.5 + q * 11);
        TW[j] = Math.sin(t * 13 + q * 40) > -0.5 ? 1 : 0;
      }
      const ord = A.order, hi = A.below(k + POOL);
      for (let j = 0; j < hi; j++) {
        const i = ord[j], x = i % W, y = (i / W) | 0, e = k - f[i], s = e * g[i];
        // 冷める時は、 画素ごとの乱数でばらばらに消える (網点で薄くなるのではなく、 欠けていく)
        const gone = fade < 1 && fade <= jit[i] / 1.5 + 0.5;
        if (e < 0) {
          // 火のすぐ前: 焦げ始め、 火に照らされる
          if (s > -2.5 && !gone) p.blendPx(x, y, SCORCH, 0.5 * (1 + s / 2.5));
          const u = Math.max(0, 1 + e / HALO), v = 1 + e / POOL;
          p.add(x, y, WARM, (0.3 * u * u + 0.12 * v * v) * FL[ph[i]] * fade);
          continue;
        }
        if (gone) continue;
        const hv = clamp(heat[i] + HV[ph[i]]), th = 0.6 + 0.6 * hv, u = s / th;
        if (u < 6.4) {
          // 白く焼けた線 → 段を下って焦げへ。 冷める間は、 線が白から赤へ落ちる
          const cool = Math.round((1 - fade) * 5), c = u < 0.9 ? (hv > 0.6 ? 8 : 7) : u < 1.7 ? 6 : u < 2.4 ? 5 : u < 3.1 ? 4 : u < 3.8 ? 3 : u < 4.6 ? 2 : u < 5.5 ? 1 : 0;
          if (u < 0.9) line.push(i);
          p.put(x, y, F[Math.max(0, c - cool)]);
          if (u < 4.6) p.add(x, y, WARM, 0.45 * (1 - u / 4.6) * (0.7 + 0.3 * hv) * fade);
          continue;
        }
        const L = fleck[i] * (0.8 + 0.4 * hv);
        if (L > 0 && s < L) {
          const v = (s - 6.4 * th) / Math.max(1, L - 6.4 * th);
          if (v < 1 && TW[(ph[i] * 5) & 63]) { p.put(x, y, v < 0.3 ? F[4] : v < 0.55 ? F[3] : v < 0.8 ? F[2] : F[1]); p.add(x, y, WARM, 0.3 * (1 - v) * fade); continue; }
        }
        if (e < 22) p.add(x, y, WARM, 0.2 * (1 - e / 22) ** 2 * fade);
        p.put(x, y, u - 6.4 < 1.6 * th + jit[i] ? CHAR : ink);
      }
      // 白く焼けた線の上に、 炎が立つ。 下の縁では高く多く。 コマごとに高さを大きく変える
      for (const i of line) {
        const x = i % W, y = (i / W) | 0, dn = -A.ny[i], h0 = rand(x, y >> 3, seed + 51);
        if (h0 > 0.16 + 0.6 * Math.max(0, dn)) continue;
        const n = (x * 7 + (y >> 4) * 131) % 977, hv = heat[i];
        const leap = 1 + Math.max(0, noise(t * 2.6 + n * 3.1, n + 70) - 0.5) * 1.8;
        const h = (9 + 13 * rand(x, 1, seed + 53)) * (0.6 + 0.55 * rand(Math.floor(t * 8), n)) * (0.6 + 0.6 * hv) * leap * fade * calm * (dn > 0 ? 0.75 + 0.7 * dn : 0.5);
        if (h >= 1.5) tongue(p, F, x + 0.5, y + 1, h, (1.4 + 0.9 * hv) * (0.8 + h / 16), noise(t * 2.2 + n, n + 40) * 1.5, t, n);
      }
      ps.draw(p);
      ash.draw(p, "front");
      lights.draw(p);
    },
  };
}
