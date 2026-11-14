// DICE BOUND 用 (2026-11-14): ヴェスカの大技 = 天与の剣 / 盾 / 指輪 (たたき台)。 gift で出し分ける。 画面全体 (480×270)・原点は左上・金色。
// (作者: 天与を全 4 段共通の大技に。 取り消さないと、 膨大な攻撃 / シールド / 強力な妨害。 固有のチャージと、 もっと派手な絵が要る)
// 型は王・審判官と同じ: チャージ (1.4 秒・1 回きり) → 待機 (1.5 秒のループ・プレイヤーの手番) → 溜め → 白飛び → 全画面。
// 見分けは「待機の絵がどこに出るか」: 剣 = 敵の頭上・盾 = 敵の体・指輪 = こちらの盤の上。 チャージの時点で、 どれが来るか分かる。
//   剣:   金の粒が頭上へ集まり、 切っ先をこちらへ向けた光の剣が伸びる → 剣が落ちる → 白飛び → 天から金の光 (moves の tenyo-sword) + 左右の光の柱 + 盤を走る輪
//   盾:   足もとから金の環が 1 枚ずつ積み上がる → 環が白く光って球が閉じる → 外へ衝撃の輪 → 厚い金の球 (moves の tenyo-shield)。 攻撃しないので白飛びは弱い
//   指輪: 盤の上に、 大きな金の輪が走って描かれ、 宝石が灯る → 輪が白く光る → 落ちて盤を締める (moves の tenyo-ring) + 締まる瞬間に下半分が金に飛ぶ
// part = "flow" (見本用: 全部を続けて) / "charge" / "charged" / "heavy" (ゲーム用に切り分ける時)。
import { Particles, P, Rng, resolveOptions, clamp, smooth, ease, rand, hex, TAU, WHITE, fit } from "../../core/index.js";
import { held as coreHeld, cross as coreCross, HELD_SEED } from "./gouka-charge.js";   // 王のチャージの「溜まった芯」(白い玉・十字の光・渦を巻いて寄る火の粉)。 式はそのまま、 色を金に
import { GOLD } from "../holy/_lib.js";
import { W, H, BOARD, beam, oval, diamond, wash } from "./_kit.js";
import * as MOVES from "./moves.js";
import * as ECLIPSE from "../shadow/eclipse.js";
import * as GODRAYS from "./tenyo-rays.js";   // weather/god-rays の写し (日食から下へ広がる)
import * as SMITE from "../holy/smite.js";          // moves/holy-smite の中身 (術者の動きを除いた、 落ちる光)
import * as SLAM from "../impacts/ground-slam.js";  // moves/ground-pound の中身 (着地の衝撃)
import * as IMPACT from "../impacts/impact-frame.js";
import { glint } from "./chevalier-charge.js";
import { scaledFx } from "./_scaled.js";

export const T_CHARGE = 1.4, T_HELD = 1.5, T_LEAD = 0.45;   // チャージ・待機の 1 周・発動の溜め (光る → 落ちる → 白)
const BODY = [240, 136], RING_Y = 96, SUCK = 1.05;   // SUCK = 吸い込みが終わる時刻 (その後 0.35 秒で、 待機の姿に落ち着く)
const GUARD_Y = 24, BLADE = 118, SR = 88;
// 剣の発動の時刻 (待機の終わりからの秒)。 ゲームの側の暗転も、 この表で掛ける (SWORD.dark)
export const SWORD = {
  idleDark: 0.5,        // チャージが完了してから待機の間の、 画面の暗さ (作者: チャージ完了後はもう少し暗転させて)。 完了の 0.35 秒 (吸い込みの終わり〜チャージの終わり) で 0 → この値
  dark: [[0, 0.5], [0.5, 1], [1.3, 1], [1.8, 0]],   // 画面の暗さ (0〜1・1 = 真っ黒。 作者: 本物の暗転に): 日食で真っ暗になる → God rays が出そろうまで保つ → 解ける。 日食と注ぐ光も一緒に消える (効果の側で消す)。 God rays は暗くしない
  rays: [0.5, 1.5],     // God rays が出て来る区間 (この間に 0 → 1)
  mark: 1.8,            // 落ちる所に印 (Holy smite の頭)
  land: 1.8 + 0.47,     // 光が落ちて着く (Holy smite の BOLT)。 ここで Ground pound の衝撃と、 インパクトフレーム
  white: 1.8 + 0.47 + 0.3,   // インパクトフレームの後、 白飛び
  end: 1.8 + 0.47 + 0.3 + 2.4,
};
// 盾の発動の時刻 (待機の終わりからの秒) と、 円の絞り。 絞りの外を黒くするのは、 ゲームの側の処理 (この液晶の色の段には真っ黒が無いので、 絵としては描けない)
export const SHIELD = {
  idleDark: 0.65,       // チャージ完了から待機の間の、 画面の暗さ (作者: もっと強く)
  close: 0.98,          // 円が閉じ切る (transitions/star-iris の CLOSE)
  open: 1.16,           // 真っ暗の拍の終わり (star-iris の OPEN0)。 ここでインパクトフレームと、 発動の絵
  rmax: 290,            // 円の初めの半径 (画面の隅まで届く)
  rim: 4,               // 金の縁の太さ (黒くするのは、 半径 + この太さ より外)
};
/** 絞りの円の半径 (待機の終わりからの秒 a)。 式は star-iris の starAt の半径と同じ。 閉じ切った後は 0。 */
export function irisR(a) { if (a >= SHIELD.close) return 0; const u = clamp(a / SHIELD.close), e = u < 0.5 ? 2 * u * u : 1 - 2 * (1 - u) * (1 - u); return SHIELD.rmax * Math.pow(1 - e, 1.3); }
const SUN = [240, 42], SUN_K = 1.15;   // 剣: 頭上の日食の中心と倍率 (元の月の半径 19 → 22。 作者: もう少し小さく)   // 剣のつばの高さ・刃の長さ・盾の球の半径

export const meta = {
  name: "Tenyo heavy (DICE BOUND)",
  desc: "Vesca's heavy moves, the three divine gifts in gold: a sword of light forms over her head and falls; rings stack into a sphere round her; a great ring is drawn over the viewer's board and clamps down.",
  tags: ["dicebound", "boss", "heavy", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: T_CHARGE + 3 + 1.8 + 0.47 + 0.3 + 2.4,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { gift: "sword", part: "flow", palette: GOLD, seed: 5 },
};

export function create(o) {
  const C = fit(o.palette, 8), R = new Rng(o.seed), gift = o.gift, part = o.part;
  const HELD_T = gift === "sword" ? 3 : T_HELD;   // 待機の 1 周: 剣は 3 秒 (Blessing の 1 周 3 秒・Eclipse は 4 秒を 3 秒に速めて揃える)
  const T0 = part === "charged" ? T_CHARGE : part === "heavy" ? T_CHARGE + HELD_T : 0;   // どこから流すか
  const OFF = gift === "shield" ? SHIELD.open - 0.25 : 0;   // 盾: 今の発動の絵 (白く燃える 0.25 秒は絞りの間に済むので飛ばす) を、 真っ暗の拍の後ろへずらす
  const TH = T_CHARGE + HELD_T, TP = TH + T_LEAD + OFF;   // 発動の始まり・全画面の始まり
  const pay = gift === "sword" ? null : MOVES.create(resolveOptions(MOVES.meta, { move: "tenyo-" + gift, seed: o.seed }));
  // 剣の発動の部品 (元の効果を、 式を触らずに置く): God rays (全画面)・落ちる光 (Holy smite)・着地の衝撃 (Ground pound)・インパクトフレーム (全画面)
  // 瓦礫 (跳ねる岩・砂利・欠片) は出さない。 煙は粒を小さく分ける (作者: 瓦礫はいらない・煙は野暮ったいので粒度を下げて)
  const RUBBLE = ["CHUNK", "GRIT", "FLAKE"], FINE = { SMOKE: [0.45, 3], DUST: [0.6, 2] }, FINER = { SMOKE: [0.32, 4], DUST: [0.5, 2] };
  const shImpact = gift === "shield" ? scaledFx(IMPACT, { K: 1.5, at: [240, 135], delay: TH + SHIELD.open - T0, opts: {} }) : null;
  const sw = gift !== "sword" ? null : {
    rays: scaledFx(GODRAYS, { K: 1.5, at: [240, 255], glow: 0.4, opts: {} }),   // 出どころ (0,−170) × 1.5 = 画面の一番上の真ん中
    // 落ちる光と着地の衝撃は、 God rays とインパクトフレームに負けないよう、 大きく・重ねて (作者の指定): 光は 3.6 倍・衝撃は 2.8 倍 + 一拍遅れてもっと大きい 2 つ目
    smite: scaledFx(SMITE, { K: 3.6, at: [BOARD[0], BOARD[1]], delay: TH + SWORD.mark - T0, glow: 1.4, drop: RUBBLE, puff: FINE, opts: { palette: GOLD } }),
    slam: scaledFx(SLAM, { K: 2.8, at: [BOARD[0], BOARD[1]], delay: TH + SWORD.land - T0, glow: 1.3, drop: RUBBLE, puff: FINE, opts: { palette: GOLD } }),
    slam2: scaledFx(SLAM, { K: 4.2, at: [BOARD[0], BOARD[1] + 6], delay: TH + SWORD.land + 0.14 - T0, drop: RUBBLE, puff: FINER, opts: { palette: GOLD, seed: 11 } }),
    impact: scaledFx(IMPACT, { K: 1.5, at: [240, 135], delay: TH + SWORD.land - T0, opts: {} }),
  };
  // 集まる粒: 行き先へ、 外から吸い込まれる (王のチャージと同じ考え方。 数は少なく、 ゆっくり)
  const F = fit(o.palette, 9);
  const [gx, gy] = gift === "sword" ? SUN : gift === "shield" ? BODY : [240, RING_Y];   // 粒が集まる先: 剣 = 頭上の日食・盾 = 体の中心・指輪 = 輪の中心
  // 剣のチャージ (作者の指定): ヴェスカに holy/blessing (上から注ぐ光の円錐) + 頭上に shadow/eclipse (色は Blessing の金を基準) + 粒が集まる。 2 つとも式はそのまま、 大きさと色だけ (_scaled.js)
  // 色は、 この液晶の 5 つの段に 1 つずつ落ちるように、 明るさを大きく離して選ぶ (作者: 縁の発光が、 このゲームの色遣いにすると潰れる)。
  //   月 = 一番暗い段 / 冠の外 = 2 段目 / 冠の中ほど = 3 段目 / 冠の根もと = 4 段目 / 月の縁の細い輪と光の玉 = 白
  const SUNPAL = ["#0b0608", "#2a1810", "#3a2414", "#4a3018", "#5c3c1c", "#8a5c26", "#f0d68a", "#ffffff"];
  const ecl = gift === "sword" ? scaledFx(ECLIPSE, { K: SUN_K, at: SUN, speed: 4 / 3, glow: 0.3, opts: { palette: SUNPAL, pearl: ["#6a4a20", "#b8862e", "#f0d68a", "#ffffff"] } }) : null;
  // 注ぐ光 (holy/blessing の円錐): 明るさの式 (4 本の光の筋が、 それぞれ 1 周に整数回ゆらぐ) は元のまま。 塗り方だけ変えた ──
  // 元は淡い半透明の重ね塗りで、 この液晶の 5 つの段では、 まだらな網点に潰れる (作者: もっとめりはりを付けてシャープに)。 ここでは明るさを 4 つの段に切って、 べたの色で塗る:
  //   筋の芯 = 白 / 筋 = 明るい金 / 筋のまわり = 金 (半分の網点) / 円錐の地 = 暗い金 (まばらな網点)。 きらめきは、 にじみ無しの 1 ドットの十字。
  const RAYS = [[-0.62, 0.16, 1, 0.3], [-0.2, 0.2, 2, 1.7], [0.22, 0.14, 1, 4.1], [0.6, 0.18, 2, 2.6]], CT = 3, CONE_TOP = SUN[1] + 20, CONE_BOT = 199;
  const halfW = (y) => 9 + 39 * ((y - CONE_TOP) / (CONE_BOT - CONE_TOP));
  const rayI = (q, tt) => { let I = 0.3 * Math.max(0, 1 - q * q); for (const [o2, w, k, ph] of RAYS) I += Math.exp(-(((q - o2) / w) ** 2)) * (0.55 + 0.45 * Math.sin((tt / CT) * TAU * k + ph)) * 0.6; return I * smooth(1.25, 0.75, Math.abs(q)); };
  const CG = [hex("#6a5c48"), hex("#b8923e"), hex("#f0d68a")];
  function cone(p, tt, k) {
    if (k <= 0.02) return;
    const yb = CONE_TOP + (CONE_BOT - CONE_TOP) * clamp(k * 1.4);   // 上から降りて来る
    p.shade(240 - 50, CONE_TOP, 240 + 50, yb, (x, y, o2) => {
      const I = rayI((x - 240) / halfW(y), tt) * (0.7 + 0.3 * smooth(CONE_TOP, CONE_BOT, y)) * smooth(CONE_TOP, CONE_TOP + 10, y) * (y > yb - 8 ? (yb - y) / 8 : 1);
      if (I < 0.2) return;
      if (I > 0.86) { o2.c = WHITE; o2.a = 1; } else if (I > 0.6) { o2.c = CG[2]; o2.a = 1; } else if (I > 0.4) { o2.c = CG[1]; o2.a = 0.5; } else { o2.c = CG[0]; o2.a = 0.3; }
    });
    // 筋の上を、 光の粒が下へ走る (元の「2 本の筋に沿って光が流れる」)
    for (const [o2, , kk, ph] of [RAYS[1], RAYS[2]]) for (let y = CONE_TOP + 6; y < yb; y++) { const pulse = 0.5 + 0.5 * Math.sin(y * 0.11 - (tt / CT) * TAU * 3 + ph); if (pulse > 0.9) p.dot(240 + o2 * halfW(y), y, WHITE); }
    // 降りて来るきらめき: 1 ドット、 またたく時だけ十字 (にじみは付けない)
    for (let i = 0; i < 16; i++) {
      const a = ((tt / CT) + rand(i, 1)) % 1, q = (rand(i, 2) - 0.5) * 1.7, y = CONE_TOP + 8 + a * (CONE_BOT - CONE_TOP - 10), x = 240 + q * halfW(y) + Math.sin(a * 9 + i) * 2, tw = Math.sin(a * TAU * (3 + (i % 3)) + i);
      if (y > yb) continue;
      p.dot(x, y, WHITE);
      if (tw > 0.5) { p.dot(x - 1, y, CG[2]); p.dot(x + 1, y, CG[2]); p.dot(x, y - 1, CG[2]); p.dot(x, y + 1, CG[2]); }
    }
    // 足もと: 光の当たる線
    if (k >= 0.7) for (let x = -44; x <= 44; x++) { const u = Math.abs(x) / 44; p.dot(240 + x, CONE_BOT, u < 0.35 ? WHITE : u < 0.7 ? CG[2] : CG[1]); if (u < 0.5) p.dot(240 + x, CONE_BOT + 1, CG[1]); }
  }
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
    if (gift !== "sword") { p.circle(gx, gy, cr + 1.6, k > 0.6 ? C[5] : C[3]); p.circle(gx, gy, cr, k > 0.35 ? WHITE : C[6]); }
    p.glow(gx, gy, 14 + 40 * k, C[4], 0.25 + 0.5 * k);
  }
  // 待機の姿 (チャージの終わりと、 待機のループの両方がこれを描く。 位置は動かない)。 ti = ループの時刻・boost = 完了の瞬間・hot = 発動の溜め
  function held(p, ti, boost = 0, hot = 0, grow = 1) {
    const ph = ((ti % HELD_T) + HELD_T) % HELD_T / HELD_T, pulse = 0.5 + 0.5 * Math.sin(ph * TAU * 2);
    if (gift === "sword") {
      // ヴェスカに注ぐ光 (Blessing): 日食が出来てから、 後半で降りて来る。 頭上の日食 (Eclipse): 吸い込みと一緒に現れる。 完了の瞬間と発動の溜めでは、 縁の光の玉 (ダイヤモンドリング) が強く走る
      const f = p.fade;
      cone(p, ph * HELD_T, clamp((grow - 0.45) / 0.5));
      p.fade = f * clamp(grow * 1.6); ecl.draw(p);
      p.fade = f;
      const bxx = SUN[0] + Math.cos(-0.72) * 19.4 * SUN_K, byy = SUN[1] + Math.sin(-0.72) * 19.4 * SUN_K;
      if (boost > 0.02 || hot > 0.02) glint(p, C, bxx, byy, 12 + 120 * boost * boost + 40 * hot);
      p.glow(SUN[0], SUN[1], 70 + 60 * boost + 40 * hot, C[4], 0.12 * grow + 0.35 * boost + 0.3 * hot);
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
    if (grow >= 1 && gift === "shield") { p.glow(BODY[0], BODY[1], 96 + 10 * pulse, C[5], 0.3 + 0.1 * pulse); p.glow(BODY[0], BODY[1], 44 + 8 * pulse, WHITE, 0.4 + 0.15 * pulse); }   // 盾: 中心の光を強調 (作者の指定)
    if (grow >= 1 && gift !== "sword") core(p, ti, gift === "shield" ? Math.max(boost, 0.5) : boost);
    if (grow >= 1 && gift === "shield") { coreCross(p, F, BODY[0], BODY[1], 128 + 14 * pulse + 60 * boost, 0.38); coreCross(p, F, BODY[0], BODY[1] + 1, 128 + 14 * pulse, 0); coreCross(p, F, BODY[0] + 1, BODY[1], 128 + 14 * pulse, 0); p.glowLine(BODY[0] - 120, BODY[1], BODY[0] + 120, BODY[1], 6, C[5], 0.4); p.glowLine(BODY[0], BODY[1] - 76, BODY[0], BODY[1] + 76, 6, C[5], 0.35); }   // 盾: 光の十字を大きく・太く (作者: もっと目立っていい)。 斜めの短い腕も   // 剣は、 芯の代わりに日食がある。 盾は、 芯を待機の間も大きく保つ (十字が長く・玉が大きい)
  }

  return {
    update(dt) {
      t += dt;
      if (t < SUCK - 0.15) { const k = t / SUCK; for (let n = 0; n < 1 + Math.floor(k * 5); n++) { if (!R.chance(0.85)) continue; const a = R.range(0, TAU), D = R.range(80, 240) * (1 - 0.25 * k), T = R.range(0.2, 0.36), v = (D * 3) / (Math.exp(3 * T) - 1); ps.spawn(P.STREAK, gx + Math.cos(a) * D, gy + Math.sin(a) * D * 0.7, -Math.cos(a) * v, -Math.sin(a) * v * 0.7, T, R.chance(0.5) ? C[6] : C[4]); } }
      ps.update(dt);
      if (ecl) ecl.update(dt);
      if (sw) for (const k in sw) sw[k].update(dt);
      if (shImpact) {
        shImpact.update(dt);
        const a = t - TH, r = irisR(a);   // 縁から金の粒がこぼれる (star-iris の、 先からこぼれる粒)
        if (a > 0 && a < SHIELD.close && r > 6 && r < 280) for (let n = 0; n < 3; n++) if (R.chance(0.5)) { const g = R.range(0, TAU), x = BODY[0] + Math.cos(g) * r, y = BODY[1] + Math.sin(g) * r; if (x > 0 && y > 0 && x < W && y < H) ps.spawn(R.chance(0.6) ? P.STAR : P.MOTE, x, y, -Math.cos(g) * R.range(20, 70), -Math.sin(g) * R.range(20, 70), R.range(0.25, 0.5), R.chance(0.5) ? C[6] : C[5], R.range(0.6, 1.8)); }
      }
      if (pay && t > TP) { const d = Math.min(dt, t - TP); payT += d; pay.update(d); }
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
      let a = t - TH;
      if (sw) {
        // 剣: 日食が強まって画面が暗くなる (暗さはゲームの側で掛ける) → God rays が徐々に出る → 暗さが解ける → 印 → 光が落ちる + 着地の衝撃 + インパクトフレーム → 白飛び
        const f = p.fade, hot = clamp(a / 0.5), rk = clamp((a - SWORD.rays[0]) / (SWORD.rays[1] - SWORD.rays[0])), gone = clamp((a - SWORD.land) / 0.5);
        p.fade = f * rk * (1 - clamp((a - SWORD.land) / 0.25)); sw.rays.draw(p);   // 光が着いたら、 God rays は引く (着弾を主役に)
        // 暗転は、 日食も含めて全部を黒くする (作者の指定): 日食と注ぐ光も、 画面と同じ暗さで消える。 黒の中に God rays だけが出て来る
        let dk = 0; for (let i = 1; i < SWORD.dark.length; i++) { const [t0, v0] = SWORD.dark[i - 1], [t1, v1] = SWORD.dark[i]; if (a >= t0 && a <= t1) dk = v0 + (v1 - v0) * (a - t0) / (t1 - t0); }
        p.fade = f * (1 - dk); if (a < SWORD.dark[1][0]) held(p, a, 0, hot);   // 真っ黒になった後は、 日食も注ぐ光も出さない (作者: 暗転後は日食を消してよい)
        p.fade = f;
        sw.impact.draw(p); sw.slam2.draw(p); sw.slam.draw(p); sw.smite.draw(p);   // インパクトフレームは下に敷き、 落ちる光と衝撃を上に
        const wt = a - SWORD.white, wk = wt < 0 ? 0 : clamp(1 - (wt - 0.15) / 0.3);
        if (wk > 0) for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) { p.put(x, y, WHITE, wk * wk); p.add(x, y, WHITE, wk * wk); }
        return;
      }
      if (gift === "shield" && a < SHIELD.open) {
        // 盾の発動の頭 (transitions/star-iris を、 星ではなく円で): 金の縁の円が、 体の中心へ閉じていく。 外は黒 (ゲームの側)。 閉じ切ると真っ暗の拍、 中心にきらめき
        const r = irisR(a);
        if (a < SHIELD.close) {
          held(p, a, 0, clamp(a / 0.5));
          p.shade(BODY[0] - r - 6, BODY[1] - r - 6, BODY[0] + r + 6, BODY[1] + r + 6, (x, y, o2) => {
            const dx = x - BODY[0], dy = y - BODY[1], d = Math.hypot(dx, dy) - r;
            if (d < 0 || d >= SHIELD.rim) return;
            const lit = (dx * -0.6 + dy * -0.8) / (r + d + 0.01) > 0.1;   // 光の当たる側 (左上) の縁は白
            o2.c = d < 1 ? (lit ? WHITE : C[6]) : d < 2 ? C[5] : d < 3 ? C[4] : C[3]; o2.a = 1; o2.l = C[5]; o2.k = 0.3;
          });
          if (r < 140) p.glow(BODY[0], BODY[1], r * 0.7 + 8, C[5], 0.35 * (1 - r / 140));
          ps.draw(p);
        }
        const u = a - SHIELD.close;
        if (u > -0.05 && u < 0.2) glint(p, C, BODY[0], BODY[1], 30 * (1 - Math.abs(u) / 0.2));
        return;
      }
      if (shImpact) shImpact.draw(p);   // インパクトフレームを下に敷き、 今の発動の絵をその上に
      a -= OFF;
      if (a < T_LEAD) {
        // 溜め: 白く燃える (0.25 秒) → 落ちる / 閉じる (0.1 秒) → 白 (0.1 秒〜)
        const hot = clamp(a / 0.25), go = clamp((a - 0.25) / 0.1), f = p.fade;
        for (let y = 0; y < H; y++) for (let x = (y & 1); x < W; x += 2) p.add(x, y, C[5], 0.35 * hot * hot);
        if (go <= 0) held(p, a, 0, hot);
        else if (gift === "shield") { held(p, a, 0, 1); oval(p, 240, BODY[1], SR + 60 * go, SR + 60 * go, 3, WHITE, 1 - go * 0.5); }
        else held(p, a, 0, 1);
        p.fade = f;
      }
      // 白飛び: 剣は全面を白く (0.15 秒)・指輪は締まる瞬間に下半分・盾は弱く
      const wt = a - 0.33, wk = gift === "shield" ? 0.55 * clamp(1 - wt / 0.3) : 0;
      if (a >= T_LEAD) {
        pay.draw(p);
        const b = a - T_LEAD;
        if (gift === "shield") {
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
