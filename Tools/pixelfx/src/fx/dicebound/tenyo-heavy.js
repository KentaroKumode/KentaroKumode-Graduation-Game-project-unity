// DICE BOUND 用 (2026-11-14): ヴェスカの大技 = 天与の剣 / 盾 / 指輪 (たたき台)。 gift で出し分ける。 画面全体 (480×270)・原点は左上・金色。
// (作者: 天与を全 4 段共通の大技に。 取り消さないと、 膨大な攻撃 / シールド / 強力な妨害。 固有のチャージと、 もっと派手な絵が要る)
// 型は王・審判官と同じ: チャージ (1.4 秒・1 回きり) → 待機 (1.5 秒のループ・プレイヤーの手番) → 溜め → 白飛び → 全画面。
// 見分けは「待機の絵がどこに出るか」: 剣 = 敵の頭上・盾 = 敵の体・指輪 = こちらの盤の上。 チャージの時点で、 どれが来るか分かる。
//   剣:   金の粒が頭上へ集まり、 切っ先をこちらへ向けた光の剣が伸びる → 剣が落ちる → 白飛び → 天から金の光 (moves の tenyo-sword) + 左右の光の柱 + 盤を走る輪
//   盾:   足もとから金の環が 1 枚ずつ積み上がる → 環が白く光って球が閉じる → 外へ衝撃の輪 → 厚い金の球 (moves の tenyo-shield)。 攻撃しないので白飛びは弱い
//   指輪: 盤の上に、 大きな金の輪が走って描かれ、 宝石が灯る → 輪が白く光る → 落ちて盤を締める (moves の tenyo-ring) + 締まる瞬間に下半分が金に飛ぶ
// part = "flow" (見本用: 全部を続けて) / "charge" / "charged" / "heavy" (ゲーム用に切り分ける時)。
import { Particles, P, Rng, resolveOptions, clamp, ease, rand, TAU, WHITE, fit } from "../../core/index.js";
import { held as coreHeld, HELD_SEED } from "./gouka-charge.js";   // 王のチャージの「溜まった芯」(白い玉・十字の光・渦を巻いて寄る火の粉)。 式はそのまま、 色を金に
import { GOLD } from "../holy/_lib.js";
import { W, H, BOARD, beam, oval, diamond, wash } from "./_kit.js";
import * as MOVES from "./moves.js";

export const T_CHARGE = 1.4, T_HELD = 1.5, T_LEAD = 0.45;   // チャージ・待機の 1 周・発動の溜め (光る → 落ちる → 白)
const BODY = [240, 136], RING_Y = 96, SUCK = 1.05;   // SUCK = 吸い込みが終わる時刻 (その後 0.35 秒で、 待機の姿に落ち着く)
const GUARD_Y = 24, BLADE = 118, SR = 88;   // 剣のつばの高さ・刃の長さ・盾の球の半径

export const meta = {
  name: "Tenyo heavy (DICE BOUND)",
  desc: "Vesca's heavy moves, the three divine gifts in gold: a sword of light forms over her head and falls; rings stack into a sphere round her; a great ring is drawn over the viewer's board and clamps down.",
  tags: ["dicebound", "boss", "heavy", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: T_CHARGE + T_HELD + T_LEAD + 2.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { gift: "sword", part: "flow", palette: GOLD, seed: 5 },
};

// 光の剣: 切っ先が下 (こちら向き)。 (x, y0) = つばの位置・len = 刃の長さ・w = 刃の幅の半分。 hot = 白く燃える
function sword(p, C, x, y0, len, w, hot = 0, k = 1) {
  if (len < 2) return;
  p.shade(x - w * 3 - 2, y0 - 12, x + w * 3 + 2, y0 + len + 2, (px, py, o) => {
    const dx = Math.abs(px - x), dy = py - y0;
    let hw = -1;
    if (dy >= 0) { const v = dy / len; hw = v > 1 ? -1 : w * (v < 0.78 ? 1 - 0.18 * v : (1 - 0.18 * 0.78) * (1 - (v - 0.78) / 0.22)); }   // 刃: ゆっくり細り、 最後に切っ先へ
    else if (dy >= -3) hw = w * 2.8 * (1 - Math.abs(dy + 1.5) / 6);   // つば
    else if (dy >= -11) hw = Math.max(1, w * 0.45);   // 柄
    if (hw < 0 || dx > hw + 0.5) return;
    const q = dx / Math.max(0.6, hw);
    o.c = hot > 0.5 || q < 0.34 ? WHITE : q < 0.7 ? C[6] : C[5]; o.a = k;
    o.l = C[5]; o.k = 0.5 * k;
  });
  p.glowLine(x, y0, x, y0 + len, 7 + 10 * hot, C[5], (0.3 + 0.4 * hot) * k);
}

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed), gift = o.gift, part = o.part;
  const T0 = part === "charged" ? T_CHARGE : part === "heavy" ? T_CHARGE + T_HELD : 0;   // どこから流すか
  const TH = T_CHARGE + T_HELD, TP = TH + T_LEAD;   // 発動の始まり・全画面の始まり
  const pay = MOVES.create(resolveOptions(MOVES.meta, { move: "tenyo-" + gift, seed: o.seed }));
  // 集まる粒: 行き先へ、 外から吸い込まれる (王のチャージと同じ考え方。 数は少なく、 ゆっくり)
  const F = fit(o.palette, 9);
  const [gx, gy] = gift === "sword" ? [240, GUARD_Y] : gift === "shield" ? BODY : [240, RING_Y];   // 芯 (白い玉と十字) の位置: 剣 = つば・盾 = 体の中心・指輪 = 輪の中心
  const ps = new Particles({ rng: R, sparks: [WHITE, C[6], C[5], C[3]] });
  // 渦を巻いて寄る粒 (王のチャージと同じ作り・数も同じくらい)
  const grit = Array.from({ length: 70 }, () => { const t0 = R.range(0, SUCK - 0.4); return { a: R.range(0, TAU), d: R.range(70, 250), t0, T: R.range(0.4, Math.max(0.45, SUCK - 0.05 - t0)), spin: R.sign() * R.range(0.4, 1.2), c: R.pick([C[3], C[4], C[5], C[6]]) }; });
  // 盾の環: 球 (中心 BODY・半径 74) を輪切りにした 5 枚。 足もとから順に
  const rings = [58, 30, 2, -26, -54, -78].map((dy, i) => ({ y: BODY[1] + dy, rx: Math.sqrt(Math.max(1, SR * SR - dy * dy)), at: 0.1 + i * 0.16, dir: i % 2 ? 1 : -1 }));
  const gems = Array.from({ length: 8 }, (_, i) => (i / 8) * TAU + 0.2);
  let t = T0, payT = 0;

  // 芯 (王の held): 白い玉・十字の光・渦を巻いて寄る火の粉。 原点を芯へ移して、 王の式をそのまま呼ぶ
  function core(p, ti, boost) { const ox = p.tx, oy = p.ty; p.tx += gx; p.ty += gy; coreHeld(p, F, 0, HELD_SEED, ti, boost); p.tx = ox; p.ty = oy; }
  // チャージ (王と同じ 4 つ): 画面じゅうから芯へ走る光の筋 (update で出す)・渦を巻いて寄る粒・芯へ縮む輪・潰れて白くなる芯
  function gather(p) {
    const k = clamp(t / SUCK);
    for (let i = 0; i < 4; i++) {
      const u = (t - i * 0.22) / 0.5;
      if (u <= 0 || u >= 1) continue;
      const r = (210 - i * 24) * (1 - ease.inQuad(u)) + 16, c = u > 0.7 ? WHITE : u > 0.4 ? C[5] : C[3];
      oval(p, gx, gy, r, r * (gift === "ring" ? 0.22 : 0.55), u > 0.3 ? 2 : 1, c, 0.35 + 0.65 * u);
    }
    for (const q of grit) {
      const u = (t - q.t0) / q.T;
      if (u <= 0 || u >= 1) continue;
      const d = q.d * (1 - ease.inCubic(u)), a = q.a + q.spin * ease.inQuad(u) * 2, x = gx + Math.cos(a) * d, y = gy + Math.sin(a) * d * 0.7, d2 = d + 5 * u * u + 0.5;
      p.line(gx + Math.cos(a - q.spin * 0.1) * d2, gy + Math.sin(a - q.spin * 0.1) * d2 * 0.7, x, y, q.c);
    }
    const cr = 1.5 + 5 * k * k;
    p.circle(gx, gy, cr + 1.6, k > 0.6 ? C[5] : C[3]); p.circle(gx, gy, cr, k > 0.35 ? WHITE : C[6]);
    p.glow(gx, gy, 14 + 40 * k, C[4], 0.25 + 0.5 * k);
  }
  // 待機の姿 (チャージの終わりと、 待機のループの両方がこれを描く。 位置は動かない)。 ti = ループの時刻・boost = 完了の瞬間・hot = 発動の溜め
  function held(p, ti, boost = 0, hot = 0, grow = 1) {
    const ph = ((ti % T_HELD) + T_HELD) % T_HELD / T_HELD, pulse = 0.5 + 0.5 * Math.sin(ph * TAU * 2);
    if (gift === "sword") {
      const len = BLADE * grow, y0 = GUARD_Y;
      if (grow >= 1) {   // 切っ先から盤へ光が落ち、 盤の上に狙いの輪が脈を打つ (ここへ落ちる)
        beam(p, C, 240, y0 + len, 240, 250, 3, 16, 0.2 + 0.12 * pulse + 0.4 * hot);
        for (const [rr, kk] of [[70, 1], [46, 0.7]]) { const r = rr * (0.9 + 0.1 * pulse); oval(p, BOARD[0], BOARD[1], r, r * 0.2, 2, kk > 0.8 ? C[6] : C[4], 0.5 + 0.4 * pulse); }
        for (let i = 0; i < 12; i++) { const g = (i / 12) * TAU + ph * TAU; p.dot(BOARD[0] + Math.cos(g) * 70, BOARD[1] + Math.sin(g) * 14, WHITE); p.dot(BOARD[0] + Math.cos(g) * 70 + 1, BOARD[1] + Math.sin(g) * 14, WHITE); }
        p.glow(BOARD[0], BOARD[1], 60, C[4], 0.14 + 0.1 * pulse);
      }
      sword(p, C, 240, y0, len, 9 + 3 * hot, hot + boost, 1);
      for (let i = 0; i < 14; i++) { const a = (ph + i / 14) % 1, x = 240 + (rand(i, 3) - 0.5) * 34, y = y0 + len - a * (len + 14); p.dot(x, y, a < 0.5 ? WHITE : C[6], (1 - a) * grow); }   // 刃に沿って昇るきらめき
    } else if (gift === "shield") {
      rings.forEach((r, i) => {
        const on = clamp((grow * 1.1 - r.at) / 0.1);
        if (on <= 0) return;
        const fresh = grow < 1 && grow * 1.1 - r.at < 0.22, ry = r.rx * 0.2;
        oval(p, 240, r.y + 2, r.rx, ry, 2, C[3], on); oval(p, 240, r.y, r.rx, ry, 3, fresh || hot > 0.4 || boost > 0.3 ? WHITE : C[5 + (pulse > 0.6 ? 1 : 0)], on);
        for (let j = 0; j < 14; j++) { const g = (j / 14) * TAU + r.dir * ph * TAU; p.dot(240 + Math.cos(g) * r.rx, r.y + Math.sin(g) * ry, WHITE, on); }   // 環の上を回る点 (1 周で元に戻る)
        p.glow(240 - r.rx, r.y, 6, C[5], 0.4 * on); p.glow(240 + r.rx, r.y, 6, C[5], 0.4 * on);
      });
      if (grow >= 1) {
        for (let m = 0; m < 6; m++) { const g = (m / 6) * Math.PI + ph * Math.PI / 6, rx = Math.abs(Math.cos(g)) * SR; if (rx > 3) oval(p, 240, BODY[1], rx, SR, 1, Math.cos(g) > 0 ? C[5] : C[3], 0.5 + 0.3 * pulse); }   // 縦の線 (球が回って見える)
        for (let i = 0; i < 18; i++) { const a = (ph + i / 18) % 1, g = rand(i, 5) * TAU, rr = SR * (0.5 + 0.5 * rand(i, 7)); p.dot(240 + Math.cos(g) * rr, BODY[1] + SR - a * SR * 2, a < 0.5 ? WHITE : C[6], 1 - a); }   // 昇る粒
        oval(p, 240, BODY[1], SR + 2, SR + 2, 2, C[5], 0.5 + 0.3 * pulse + 0.4 * hot); p.glow(240, BODY[1], SR * (1.1 + 0.6 * boost), C[4], 0.14 + 0.1 * pulse + 0.3 * boost + 0.3 * hot); }
    } else {
      const a1 = TAU * clamp(grow / 0.7), y = RING_Y, rx = 216, ry = 13.5, hotc = hot > 0.4 || boost > 0.3;
      oval(p, 240, y + 3, rx, ry, 2, C[2], 1, -Math.PI / 2, -Math.PI / 2 + a1); oval(p, 240, y, rx, ry, 4, hotc ? WHITE : C[5], 1, -Math.PI / 2, -Math.PI / 2 + a1); oval(p, 240, y - 2, rx, ry, 1, WHITE, 1, -Math.PI / 2, -Math.PI / 2 + a1);
      if (grow < 0.7) { const g = -Math.PI / 2 + a1; p.glow(240 + Math.cos(g) * rx, y + Math.sin(g) * ry, 12, WHITE, 0.8); }   // 輪を描いて走る頭
      else {
        for (let i = 0; i < 36; i++) { const g = (i / 36) * TAU + ph * TAU / 3; p.dot(240 + Math.cos(g) * (rx - 8), y + Math.sin(g) * (ry - 1.5), i % 3 ? C[3] : WHITE); }
        gems.forEach((g, i) => { const on = clamp((grow - 0.7 - i * 0.03) / 0.05); if (on <= 0) return; const x = 240 + Math.cos(g) * rx, yy = y + Math.sin(g) * ry; diamond(p, x, yy, 4, WHITE, C[3]); p.glow(x, yy, 8 + 5 * pulse + 14 * boost, C[6], 0.5 + 0.3 * boost); });
        for (let i = 0; i < 24; i++) { const g = (i / 24) * TAU, x = 240 + Math.cos(g) * rx, yy = y + Math.sin(g) * ry, h = (10 + 14 * rand(i, 9)) * (0.6 + 0.4 * Math.sin(ph * TAU + i)); if (Math.sin(g) > -0.2) beam(p, C, x, yy, x, yy + h, 2, 1, 0.22 + 0.3 * hot); }   // 輪から下へ垂れる光
      }
      if (grow >= 1) { oval(p, BOARD[0], BOARD[1], 200 * (0.96 + 0.04 * pulse), 22, 1, C[4], 0.4 + 0.3 * pulse); for (let i = 0; i < 16; i++) { const g = (i / 16) * TAU - ph * TAU / 2; p.dot(BOARD[0] + Math.cos(g) * 200, BOARD[1] + Math.sin(g) * 22, WHITE); } }   // 盤の上に、 締まる先の輪がうっすら
      p.glow(240, y, 60 + 80 * boost, C[4], 0.1 + 0.3 * boost + 0.2 * hot);
    }
    if (grow >= 1) core(p, ti, boost);
  }

  return {
    update(dt) {
      t += dt;
      if (t < SUCK - 0.15) { const k = t / SUCK; for (let n = 0; n < 1 + Math.floor(k * 5); n++) { if (!R.chance(0.85)) continue; const a = R.range(0, TAU), D = R.range(80, 240) * (1 - 0.25 * k), T = R.range(0.2, 0.36), v = (D * 3) / (Math.exp(3 * T) - 1); ps.spawn(P.STREAK, gx + Math.cos(a) * D, gy + Math.sin(a) * D * 0.7, -Math.cos(a) * v, -Math.sin(a) * v * 0.7, T, R.chance(0.5) ? C[6] : C[4]); } }
      ps.update(dt);
      if (t > TP) { const d = Math.min(dt, t - TP); payT += d; pay.update(d); }
    },
    draw(p) {
      if (t < T_CHARGE) {
        // 吸い込み (SUCK まで) → 完了: 待機と同じ絵を、 完了の瞬間だけ強く (十字が長く走る)。 形 (剣・環・輪) は吸い込みの間に伸びていく
        const grow = ease.outCubic(clamp((t / SUCK - 0.1) / 0.85));
        if (t < SUCK) { gather(p); held(p, t - T_CHARGE, 0, 0, Math.min(0.999, grow)); }
        else held(p, t - T_CHARGE, 1 - clamp((t - SUCK) / 0.3), 0, 1);
        ps.draw(p);
        return;
      }
      if (t < TH) { held(p, t - T_CHARGE); return; }
      const a = t - TH;
      if (a < T_LEAD) {
        // 溜め: 白く燃える (0.25 秒) → 落ちる / 閉じる (0.1 秒) → 白 (0.1 秒〜)
        const hot = clamp(a / 0.25), go = clamp((a - 0.25) / 0.1), f = p.fade;
        for (let y = 0; y < H; y++) for (let x = (y & 1); x < W; x += 2) p.add(x, y, C[5], 0.35 * hot * hot);
        if (go <= 0) held(p, a, 0, hot);
        else if (gift === "sword") beam(p, C, 240, -10, 240, 14 + (250 - 14) * ease.inQuad(go), 16, 34, 1);
        else if (gift === "shield") { held(p, a, 0, 1); oval(p, 240, BODY[1], SR + 60 * go, SR + 60 * go, 3, WHITE, 1 - go * 0.5); }
        else held(p, a, 0, 1);
        p.fade = f;
      }
      // 白飛び: 剣は全面を白く (0.15 秒)・指輪は締まる瞬間に下半分・盾は弱く
      const wt = a - 0.33, wk = gift === "sword" ? clamp(1 - (wt - 0.15) / 0.2) : gift === "shield" ? 0.55 * clamp(1 - wt / 0.3) : 0;
      if (a >= T_LEAD) {
        pay.draw(p);
        const b = a - T_LEAD;
        if (gift === "sword") {
          // 左右にも光の柱が落ち、 盤を輪が走る
          for (const [x, d] of [[128, 0.12], [352, 0.2], [64, 0.32], [416, 0.4]]) { const q = b - d; if (q > 0 && q < 0.7) beam(p, C, x, -10, x, 250, 10 * (1 - q / 0.7) + 2, 22 * (1 - q / 0.7) + 3, 1 - q / 0.7); }
          for (const d of [0, 0.14, 0.3]) { const q = (b - d) / 0.6; if (q > 0 && q < 1) oval(p, BOARD[0], BOARD[1], 40 + 260 * ease.outCubic(q), 6 + 30 * ease.outCubic(q), 2, q < 0.4 ? WHITE : C[5], 1 - q); }
        } else if (gift === "shield") {
          for (const d of [0, 0.12, 0.26]) { const q = (b - d) / 0.55; if (q > 0 && q < 1) oval(p, 240, BODY[1], SR + 240 * ease.outCubic(q), SR + 240 * ease.outCubic(q), 3 * (1 - q) + 1, q < 0.35 ? WHITE : C[5], 1 - q); }
          for (let i = 0; i < 12; i++) { const g = (i / 12) * TAU + 0.26, q = b / 0.5; if (q < 1) beam(p, C, 240 + Math.cos(g) * SR, BODY[1] + Math.sin(g) * SR, 240 + Math.cos(g) * (SR + 150 * ease.outCubic(q)), BODY[1] + Math.sin(g) * (SR + 150 * ease.outCubic(q)), 4, 1, 1 - q); }
        } else {
          const q = (b - 0.26) / 0.35;   // 輪が盤に着いて締まる瞬間 (moves の tenyo-ring の 0.26 秒)
          if (q > 0 && q < 1) for (let y = 150; y < H; y++) for (let x = 0; x < W; x++) { const k = (1 - q) * (1 - q) * clamp((y - 150) / 60); p.put(x, y, WHITE, k); p.add(x, y, WHITE, k); }
          for (let i = 0; i < 10; i++) { const x = 60 + i * 40, q2 = (b - 0.3 - i * 0.02) / 0.9; if (q2 > 0 && q2 < 1) beam(p, C, x, 250, x, 250 - 230 * ease.outCubic(Math.min(1, q2 * 3)), 3, 1, (1 - q2) * 0.8); }   // 盤から上へ立つ光の格子 (縛られる)
        }
      }
      if (wt > 0 && wk > 0) for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) { p.put(x, y, WHITE, wk * wk); p.add(x, y, WHITE, wk * wk); }
    },
  };
}
