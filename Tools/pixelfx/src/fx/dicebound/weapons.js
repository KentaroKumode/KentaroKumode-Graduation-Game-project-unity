// DICE BOUND 用 (2026-11-14): プレイヤーの武器の攻撃 (たたき台)。 剣・斧・短剣・盾 × 通常 / 会心 の 8 個。 weapon で出し分ける (例: "sword-normal"・"sword-crit")。
// 画面全体 (480×270)・原点は左上・1 回きり。 プレイヤーの攻撃なので、 絵は敵の体の上に出る。
// 作り: 元の効果を、 式を書き直さずに、 この画面の大きさで流す (_scaled.js)。 変えるのは、 大きさ・置く場所・色・速さだけ。 会心は、 大きく・手数が多く・色が強い。
//   剣:   通常 = 一太刀 (sword-slash + 流れの筋) / 会心 = ぐるりと一周 (spin-slash) + 交差する二太刀 (double-slash を中空に)・金
//   斧:   通常 = 縦の振り下ろし (chop) + 着地の土煙 (heavy-landing) / 会心 = 太い縦の振り下ろし + 地面が割れる (ground-slam)・血の色
//   短剣: 通常 = 小さな連撃 (dagger-flurry・縁が光る塗り) / 会心 = 真横の大きな斬撃 (中空) + 連撃・金
//   盾:   通常 = 上から叩きつける (重い柱 + thrust を下向きに・ゆっくり) + 足もとの土煙 (heavy-landing) / 会心 = 同じ形を大きく・金・地面が割れる (ground-slam)
// (最初は段 2 / 3 / 4 の 12 個で作った。 段 3 の絵 ── 交差する二太刀だけ・血の色の振り下ろし・突き・盤を包む球 ── は、 この版では使っていない)
import * as BLADE from "./blade-slash.js";   // melee/sword-slash の写し (流れの筋を少し足した版)
import * as DOUBLE from "./cross-slash.js";   // melee/double-slash の写し (中を抜いた版)
import * as SPIN from "./spin-slash.js";        // melee/spin-slash の写し (smear 時のみ短く 1 コマ化)
import * as CHOP from "./chop.js";
import * as LANDING_ORIG from "../impacts/heavy-landing.js";
import * as SLAM_ORIG from "../impacts/ground-slam.js";
import * as THRUST_ORIG from "../melee/thrust.js";
import * as LANDING from "./landing-dust.js";   // impacts/heavy-landing の写し (after6 比較・legend 用)
import * as SLAM from "./slam-impact.js";       // impacts/ground-slam の写し (after6 比較・legend 用)
import * as DAGGER from "./dagger.js";   // melee/dagger-flurry の写し (縁が光る塗り・会心は真横の大きな斬撃つき)
import * as THRUST from "./slam-thrust.js";     // melee/thrust の写し (after6 比較・legend 用)
import * as COLUMN from "./slam-column.js";
import * as DAGGER_V2 from "./dagger-crit-v2.js";
import * as AXE_V2 from "./axe-crit-v2.js";
import * as SHIELD_V2 from "./shield-crit-v2.js";
import * as L_DURANDAL from "./legend-durandal.js";
import * as L_KETSUTEI from "./legend-ketsutei.js";
import * as L_NOCTARIA from "./legend-noctaria.js";
import * as L_DAWN from "./legend-dawnbringer.js";
import { scaledFx } from "./_scaled.js";

const E = [240, 136], B = [240, 247], FEET = [240, 197];   // 敵の体の中心・こちらの盤・敵の足もと
const DEBRIS = ["CHUNK", "GRIT"];   // 飛び散る岩・小石・砂利は出さない (2026-11-14 作者の指定)
const STEEL = ["#212f6b", "#2c58aa", "#3a8bd8", "#62c8f4", "#b8c9e8", "#e8f1ff", "#ffffff"];
const GOLD = ["#3b1d2a", "#7a3a2c", "#c4652e", "#f0a040", "#ffd25e", "#fff0a0", "#ffffff"];
const BLOOD = ["#2a0510", "#5c0a1c", "#a3142a", "#e0302c", "#ff7a5a", "#ffd0c0", "#ffffff"];
const IRON = ["#1d1620", "#3a3440", "#5c5664", "#8a8494", "#b8b2c0", "#e4e0ea", "#ffffff"];
const VENOM = ["#0e2a24", "#14472f", "#1d6e3a", "#2f9a45", "#5cc654", "#c8f4a0", "#ffffff"];
const NIGHT = ["#0a0614", "#1c1236", "#3a2470", "#5a3aa8", "#8a64e0", "#d8c8ff", "#ffffff"];
// 段 4 LEGENDARY 4 本のアイコン色パレット (通常・会心共通)
const DURANDAL = ["#1a1d32", "#515689", "#a91816", "#d93343", "#eb5f76", "#ffb9ab", "#ffffff"];
const KETSUTEI = ["#1a1821", "#43404f", "#690612", "#8d0e1c", "#af1d2d", "#c4b7f0", "#e4def9"];
const NOCTARIA = ["#12133c", "#291186", "#50487f", "#9b75ff", "#31c9ff", "#6cf1ff", "#f3feff"];
const DAWN = ["#060e3f", "#111d6b", "#486db5", "#e09836", "#ffd072", "#fff2bc", "#ffffff"];

// 技の中身: [元の効果, { K 倍率, at 画面の位置, anchor 元の座標のどこをそこへ合わせるか, speed, delay, opts }] の並び (重ねて流す)
// 家系ごとに 2 つ (2026-11-14 作者の決定: 段ごとではなく、 通常と会心)。 通常 = もとの段 2 の絵・会心 = もとの段 4 の絵
const PLAN = {
  "sword-normal": [[BLADE, { K: 2.6, at: E, anchor: [10, -28], speed: 0.65, opts: { palette: STEEL, smear: true } }]],
  "sword-crit": [[SPIN, { K: 3.2, at: E, anchor: [0, -26], speed: 0.65, opts: { palette: GOLD, smear: true } }], [DOUBLE, { K: 3.2, at: E, anchor: [34, -23], speed: 0.65, delay: 0.138, opts: { palette: GOLD, smear: true } }]],
  // 斧・通常: 振り下ろしの軌跡 (chop) だけ高速 1 コマ化し、着地土煙・火花・削り屑は元の heavy-landing / chop のまま据え置き (着地コマに合わせて delay 前寄せ)
  "axe-normal": [[LANDING_ORIG, { K: 2.6, at: FEET, anchor: [0, 0], speed: 1.3, delay: 0.045, drop: DEBRIS, opts: { palette: STEEL } }], [CHOP, { K: 2.4, at: FEET, anchor: [-8, 0], speed: 0.65, opts: { palette: STEEL, flow: 2.4, sparks: ["#ffffff", "#e8f1ff", "#b8c9e8", "#62c8f4"], smear: true, keepParticles: true } }]],
  "axe-crit": [[SLAM, { K: 1.9, at: FEET, anchor: [0, 0], speed: 2.0, delay: 0.1, drop: DEBRIS, opts: { palette: GOLD, smear: true } }], [CHOP, { K: 2.8, at: FEET, anchor: [-8, 0], speed: 0.65, opts: { palette: GOLD, heavy: true, flow: 2.8, sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"], smear: true } }]],   // after6 比較用に据え置き
  "axe-crit-v2": [[SLAM_ORIG, { K: 1.9, at: FEET, anchor: [0, 0], speed: 2.0, delay: 0.56, drop: DEBRIS, opts: { palette: GOLD } }], [AXE_V2, { K: 2.6, at: FEET, anchor: [-4, 0], speed: 0.65, opts: { palette: GOLD, flow: 2.6, sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"] } }]],
  "dagger-normal": [[DAGGER, { K: 2.6, at: E, anchor: [28, -27], speed: 0.75, opts: { palette: STEEL, sparks: ["#ffffff", "#e8f1ff", "#b8c9e8", "#62c8f4"], flow: 2.6, smear: true } }]],   // 色は剣の通常と同じ
  "dagger-crit": [[DAGGER, { K: 3.0, at: E, anchor: [27, -27], speed: 0.72, opts: { palette: GOLD, sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"], wide: true, flow: 3, bloom: 1.7, smear: true } }]],   // after6 比較用に据え置き
  "dagger-crit-v2": [[DAGGER_V2, { K: 2.9, at: E, anchor: [0, 0], speed: 0.72, opts: { palette: GOLD, sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"], flow: 2.9 } }]],
  // 盾・通常: 落ちてくる光の柱 (slam-column) だけ 1 コマ＋陰影・縁光化し、足もと土煙と直線の束は元の heavy-landing / melee/thrust を据え置き (着地コマに合わせて delay 前寄せ)
  "shield-normal": [[LANDING_ORIG, { K: 2.8, at: FEET, anchor: [0, 0], speed: 1.2, delay: 0.09, drop: DEBRIS, opts: { palette: STEEL } }],
    [COLUMN, { K: 1, at: [240, 186], anchor: [0, 0], speed: 1, opts: { palette: [...STEEL, "#ffffff"], width: 44, smear: true, shaded: true } }],
    [THRUST_ORIG, { K: 3.4, at: [240, 186], anchor: [69, -20], rot: Math.PI / 2, speed: 0.5, opts: { palette: STEEL, sparks: ["#ffffff", "#e8f1ff", "#b8c9e8", "#62c8f4"], reach: 60 } }]],
  "shield-crit": [[SLAM, { K: 2.0, at: FEET, anchor: [0, 0], speed: 2.0, delay: 0.14, drop: DEBRIS, opts: { palette: GOLD, smear: true, shift: 0.09 } }],
    [COLUMN, { K: 1, at: [240, 190], anchor: [0, 0], speed: 1, opts: { palette: [...GOLD, "#ffffff"], width: 64, smear: true, crit: true } }],
    [THRUST, { K: 4.2, at: [240, 190], anchor: [69, -20], rot: Math.PI / 2, speed: 0.5, opts: { palette: GOLD, sparks: ["#ffffff", "#fff0a0", "#ffd25e", "#f0a040"], reach: 60, smear: true, crit: true } }]],   // after6 比較用に据え置き
  "shield-crit-v2": [[SLAM_ORIG, { K: 2.0, at: FEET, anchor: [0, 0], speed: 2.0, delay: 0.48, drop: DEBRIS, opts: { palette: GOLD } }],
    [SHIELD_V2, { K: 1, at: [240, 190], anchor: [0, 0], speed: 1, opts: { palette: [...GOLD, "#ffffff"], width: 68 } }]],
  // ---- 段 4 LEGENDARY 専用 (4 本 × 通常/会心 = 8 個・通常/会心とも同じアイコン色、会心は溜めと規模で格上げ) ----
  "sword-legend-normal": [[L_DURANDAL, { K: 1, at: E, anchor: [0, 0], speed: 1, opts: { palette: DURANDAL, sparks: ["#ffffff", "#ffb9ab", "#eb5f76", "#d93343"], crit: false } }]],
  "sword-legend-crit": [[L_DURANDAL, { K: 1, at: E, anchor: [0, 0], speed: 1, opts: { palette: DURANDAL, sparks: ["#ffffff", "#ffb9ab", "#eb5f76", "#d93343"], crit: true } }]],
  "axe-legend-normal": [[LANDING, { K: 2.6, at: FEET, anchor: [0, 0], speed: 1.3, delay: 0.1, drop: DEBRIS, opts: { palette: KETSUTEI, smear: true } }],
    [L_KETSUTEI, { K: 2.5, at: FEET, anchor: [-8, 0], speed: 0.65, opts: { palette: KETSUTEI, sparks: ["#e4def9", "#c4b7f0", "#af1d2d", "#8d0e1c"], crit: false, flow: 2.5 } }]],
  "axe-legend-crit": [[SLAM, { K: 2.35, at: FEET, anchor: [0, 0], speed: 2.0, delay: 0.52, drop: DEBRIS, opts: { palette: KETSUTEI, smear: true } }],
    [L_KETSUTEI, { K: 2.7, at: FEET, anchor: [-6, 0], speed: 0.65, opts: { palette: KETSUTEI, sparks: ["#e4def9", "#c4b7f0", "#af1d2d", "#8d0e1c"], crit: true, flow: 2.7 } }]],
  "dagger-legend-normal": [[L_NOCTARIA, { K: 2.8, at: E, anchor: [27, -27], speed: 0.72, opts: { palette: NOCTARIA, sparks: ["#f3feff", "#6cf1ff", "#31c9ff", "#9b75ff"], crit: false, flow: 2.8 } }]],
  "dagger-legend-crit": [[L_NOCTARIA, { K: 2.9, at: E, anchor: [27, -27], speed: 0.72, opts: { palette: NOCTARIA, sparks: ["#f3feff", "#6cf1ff", "#31c9ff", "#9b75ff"], crit: true, flow: 2.9 } }]],
  // ドーンブリンガー v2: 柱・筋の束の使い回しを全廃し、西門盾の溜め → 昇る朝日のドーム壁＋暁光条の一から新規モーション
  "shield-legend-normal": [[L_DAWN, { K: 1, at: FEET, anchor: [0, 0], speed: 1, opts: { palette: [...DAWN, "#ffffff"], sparks: ["#ffffff", "#fff2bc", "#ffd072", "#e09836"], crit: false } }]],
  "shield-legend-crit": [[L_DAWN, { K: 1, at: FEET, anchor: [0, 0], speed: 1, opts: { palette: [...DAWN, "#ffffff"], sparks: ["#ffffff", "#fff2bc", "#ffd072", "#e09836"], crit: true } }]],
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
