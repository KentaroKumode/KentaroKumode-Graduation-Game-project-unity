// DICE BOUND 用 (2026-11-14): ボス以外の敵の、 汎用の技 (たたき台)。 move で出し分ける。 画面全体 (480×270)・原点は左上。
// 敵の攻撃はこちらが受けるので、 プレイヤーの攻撃の絵より手前 = 大きく出す (作者の指定)。 作り: 元の効果を、 式を書き直さずに、 この画面の大きさで流す (_scaled.js)。
import * as CLAW from "./foe-claw.js";
import * as HAMMER from "../melee/hammer-smash.js";
import * as CURSE from "./foe-decree.js";
import * as SCYTHE from "./foe-reap.js";
import * as SUMMON from "../spawn/summon-rise.js";
import * as FIRERING from "../fire/fire-ring.js";
import * as HEALRING from "../holy/heal-aoe.js";
import * as FREEZE from "./foe-crack.js";
import * as HEXSIGIL from "../shadow/hex-sigil.js";
import * as CROSSES from "./foe-crosses.js";
import * as WIRES from "./foe-wires.js";
import * as PUDDLE from "../nature/toxic-puddle.js";
import * as PILLAR from "../holy/holy-pillar.js";
import * as DAGGER from "./dagger.js";
import * as VSLASH from "./vesca-slash.js";
import * as MISSILES from "./foe-missiles.js";
import * as GUARD from "./foe-guard.js";
import * as DRAIN from "./foe-drain.js";
import * as RAGE from "./foe-rage.js";
import * as MIRROR from "./foe-mirror.js";
import { oval } from "./_kit.js";
import { hex, clamp } from "../../core/index.js";
import { scaledFx } from "./_scaled.js";

const E = [240, 136], BOARD = [240, 247], FEET = [240, 197], HIT = [240, 205];   // 敵の体の中心・こちらの盤・敵の足もと・こちらが受ける所 (画面の下寄り)
const DEBRIS = ["CHUNK", "GRIT"];
const IRON = ["#1d1620", "#3a3440", "#5c5664", "#8a8494", "#b8b2c0", "#e4e0ea", "#ffffff"];
const SPOTS = [[96, 246, 0.1], [168, 236, 0], [240, 250, 0.2], [312, 236, 0.05], [384, 246, 0.15]];   // 弱体が落ちる所 (こちらの盤の上の 5 点) と、 それぞれの遅れ

// 技の中身: [元の効果, { K 倍率, at 画面の位置, anchor 元の座標のどこをそこへ合わせるか, speed, delay, start, end, life, drop, opts }] の並び (重ねて流す)
const PLAN = {
  // ---- 攻撃 (こちらが受けるので、 大きく・画面の下寄りに)
  // ロングソード (騎士・スケルトン・蛇鱗の戦士): ヴェスカの斬撃を、 武骨に。 筋と光を弱め、 残像ときらめきを外し、 弧を短く・太く・少し遅く (上手すぎて見えないように)
  "longsword": [[VSLASH, { K: 1, at: [196, 250], anchor: [0, 0], drop: ["STAR", "MOTE"], opts: { noise: 0.3, glow: 0.3, echo: false, glints: false, a0: -2.15, a1: 0.5, sy: -0.48, rot: 0.52, width: 54, sweep: 0.1, radius: 136 } }]],
  // ダガー (その他の刃物): 短剣の通常を大きく。 3 太刀だけ・間がばらつく・振りが遅い・締めの一太刀なし・光は弱く (達人に見えないように)
  "dagger": [[DAGGER, { K: 5, at: [240, 196], anchor: [28, -27], speed: 0.75, drop: ["STAR"], opts: { palette: IRON, sparks: ["#ffffff", "#e4e0ea", "#b8b2c0", "#8a8494"], flow: 5, bloom: 0.25, count: 3, every: 0.15, finish: false, clumsy: 1 } }]],
  "claw": [[CLAW, { K: 4.2, at: HIT, anchor: [40, -24], speed: 0.7, opts: {} }]],
  "blunt": [[HAMMER, { K: 3, at: [240, 228], anchor: [26, 0], speed: 1, start: 0.15, drop: DEBRIS, opts: {} }]],   // 振りかぶりは流さず、 当たった所から
  "missile": [[MISSILES, { K: 1, at: [0, 0], anchor: [0, 0], opts: {} }]],
  "fire": [[SUMMON, { K: 2.4, at: FEET, anchor: [0, 0], speed: 2.6, end: 1.35, opts: {} }], [FIRERING, { K: 1, at: FEET, anchor: [0, 0], speed: 1.5, delay: 0.4, opts: { reach: 150 } }]],   // 敵の足もと
  // ---- 13 番目の死
  "decree": [[CURSE, { K: 1.6, at: FEET, anchor: [0, 0], speed: 1.3, opts: { width: 62, height: 75 } }]],   // どくろは、 敵の体の高さ (height × K) の上に出る
  "decree-reap": [[SCYTHE, { K: 3.4, at: [240, 222], anchor: [30, -14], speed: 0.8, opts: {} }]],
  // ---- 補助
  "heal": [[HEALRING, { K: 1.5, at: FEET, anchor: [0, 0], speed: 1.6, opts: {} }]],
  "drain": [[DRAIN, { K: 3, at: [240, 238], anchor: [0, 0], speed: 1.1, opts: {} }], [CROSSES, { K: 3, at: FEET, anchor: [0, 0], opts: { t0: 0.7, count: 16, width: 40, height: 42 } }]],
  "hex-gray": [[WIRES, { K: 1, at: [0, 0], anchor: [0, 0], opts: {} }]],
  "hex-curse": [[HEXSIGIL, { K: 1.2, at: [240, 108], anchor: [0, 0], speed: 2.2, opts: {} }]],   // 敵の頭上
  "guard": [[GUARD, { K: 1, at: FEET, anchor: [0, 0], opts: { rx: 104, ry: 132 } }]],
  "armor-break": [[FREEZE, { K: 2.4, at: FEET, anchor: [0, 0], start: 2.15, speed: 0.9, opts: {} }]],
  "mirror": [[MIRROR, { K: 2.6, at: [240, 205], anchor: [0, 0], life: 1.5, opts: {} }]],
  "poison": [[PUDDLE, { K: 1, at: [240, 199], anchor: [0, 0], life: 2, opts: { width: 84 } }]],   // 敵の足もと。 点で打つ作りなので、 広げずに、 幅だけ大きく
  "rage": [[RAGE, { K: 3, at: FEET, anchor: [0, 0], life: 1.8, opts: { width: 26, height: 40 } }]],
};

export const meta = {
  name: "Foe moves (DICE BOUND)",
  desc: "The ordinary enemies' generic moves, drawn at the game's screen size.",
  tags: ["dicebound", "enemy", "one-shot"],
  size: [480, 270],
  origin: [0, 0],
  duration: 3.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { move: "claw", seed: 3 },
};

export function create(o) {
  const parts = (PLAN[o.move] ?? PLAN.claw).map(([SRC, cfg]) => scaledFx(SRC, cfg));
  return {
    update(dt) { for (const q of parts) q.update(dt); },
    draw(p) { for (const q of parts) q.draw(p); },
  };
}
