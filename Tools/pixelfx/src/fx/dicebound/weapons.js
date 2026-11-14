// DICE BOUND 用 (2026-11-14): プレイヤーの武器の攻撃 (たたき台)。 剣・斧・短剣・盾 × 通常 / 会心 の 8 個。 weapon で出し分ける (例: "sword-normal"・"sword-crit")。
// 画面全体 (480×270)・原点は左上・1 回きり。 プレイヤーの攻撃なので、 絵は敵の体の上に出る。
// 作り: 元の効果を、 式を書き直さずに、 この画面の大きさで流す (_scaled.js)。 変えるのは、 大きさ・置く場所・色・速さだけ。 会心は、 大きく・手数が多く・色が強い。
//   剣:   通常 = 一太刀 (sword-slash + 流れの筋) / 会心 = ぐるりと一周 (spin-slash) + 交差する二太刀 (double-slash を中空に)・金
//   斧:   通常 = 重い振り下ろし (heavy-cleave) / 会心 = 廻天 (spin-slash) + 振り下ろし・血の色
//   短剣: 通常 = 小さな連撃 (dagger-flurry)・毒の緑 / 会心 = 刈り取る大きな三日月 (scythe-reap) + 連撃・夜の紫
//   盾:   通常 = 盾で殴る (blunt-hit) / 会心 = 受けた重さを返す (parry + blunt-hit)・金
// (最初は段 2 / 3 / 4 の 12 個で作った。 段 3 の絵 ── 交差する二太刀だけ・血の色の振り下ろし・突き・盤を包む球 ── は、 この版では使っていない)
import * as BLADE from "./blade-slash.js";   // melee/sword-slash の写し (流れの筋を少し足した版)
import * as DOUBLE from "./cross-slash.js";   // melee/double-slash の写し (中を抜いた版)
import * as SPIN from "../melee/spin-slash.js";
import * as CLEAVE from "../melee/heavy-cleave.js";
import * as FLURRY from "../melee/dagger-flurry.js";
import * as REAP from "../melee/scythe-reap.js";
import * as BLUNT from "../impacts/blunt-hit.js";
import * as PARRY from "../impacts/parry.js";
import { scaledFx } from "./_scaled.js";

const E = [240, 136], B = [240, 247];   // 敵の体の中心・こちらの盤
const STEEL = ["#212f6b", "#2c58aa", "#3a8bd8", "#62c8f4", "#b8c9e8", "#e8f1ff", "#ffffff"];
const GOLD = ["#3b1d2a", "#7a3a2c", "#c4652e", "#f0a040", "#ffd25e", "#fff0a0", "#ffffff"];
const BLOOD = ["#2a0510", "#5c0a1c", "#a3142a", "#e0302c", "#ff7a5a", "#ffd0c0", "#ffffff"];
const IRON = ["#1d1620", "#3a3440", "#5c5664", "#8a8494", "#b8b2c0", "#e4e0ea", "#ffffff"];
const VENOM = ["#0e2a24", "#14472f", "#1d6e3a", "#2f9a45", "#5cc654", "#c8f4a0", "#ffffff"];
const NIGHT = ["#0a0614", "#1c1236", "#3a2470", "#5a3aa8", "#8a64e0", "#d8c8ff", "#ffffff"];

// 技の中身: [元の効果, { K 倍率, at 画面の位置, anchor 元の座標のどこをそこへ合わせるか, speed, delay, opts }] の並び (重ねて流す)
// 家系ごとに 2 つ (2026-11-14 作者の決定: 段ごとではなく、 通常と会心)。 通常 = もとの段 2 の絵・会心 = もとの段 4 の絵
const PLAN = {
  "sword-normal": [[BLADE, { K: 2.6, at: E, anchor: [10, -28], speed: 0.65, opts: { palette: STEEL } }]],
  "sword-crit": [[SPIN, { K: 3.2, at: E, anchor: [0, -26], speed: 0.65, opts: { palette: GOLD } }], [DOUBLE, { K: 3.2, at: E, anchor: [34, -23], speed: 0.65, delay: 0.3, opts: { palette: GOLD } }]],
  "axe-normal": [[CLEAVE, { K: 2.2, at: [240, 196], anchor: [34, 0], speed: 1.3, opts: { palette: IRON } }]],
  "axe-crit": [[SPIN, { K: 3.4, at: E, anchor: [0, -26], speed: 0.65, opts: { palette: BLOOD } }], [CLEAVE, { K: 2.9, at: [240, 196], anchor: [34, 0], speed: 1.4, delay: 0.25, opts: { palette: BLOOD } }]],
  "dagger-normal": [[FLURRY, { K: 2.4, at: E, anchor: [34, -28], speed: 0.75, opts: { palette: VENOM } }]],
  "dagger-crit": [[REAP, { K: 2.6, at: E, anchor: [20, -30], speed: 1.5, opts: { palette: NIGHT } }], [FLURRY, { K: 2.8, at: E, anchor: [34, -28], speed: 0.8, delay: 0.35, opts: { palette: NIGHT } }]],
  "shield-normal": [[BLUNT, { K: 3.4, at: E, anchor: [0, -22], speed: 0.75, opts: {} }]],
  "shield-crit": [[PARRY, { K: 3.4, at: E, anchor: [0, -30], speed: 0.75, opts: { palette: GOLD } }], [BLUNT, { K: 3.8, at: E, anchor: [0, -22], speed: 0.8, delay: 0.12, opts: {} }]],
};

export const meta = {
  name: "Player weapons (DICE BOUND)",
  desc: "The player's weapon attacks landing on the enemy: sword, axe, dagger and shield, tiers 2 to 4, each the repository's own melee and impact effects drawn at the game's screen size.",
  tags: ["dicebound", "player", "weapon", "one-shot"],
  size: [480, 270],
  origin: [0, 0],
  duration: 2.0,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { weapon: "sword-normal", seed: 3 },
};

export function create(o) {
  const parts = (PLAN[o.weapon] ?? PLAN["sword-normal"]).map(([SRC, cfg]) => scaledFx(SRC, cfg));
  return {
    update(dt) { for (const q of parts) q.update(dt); },
    draw(p) { for (const q of parts) q.draw(p); },
  };
}
