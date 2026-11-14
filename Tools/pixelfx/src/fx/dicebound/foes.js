// DICE BOUND 用 (2026-11-14): ボス以外の敵の、 汎用の技 (たたき台)。 move で出し分ける。 画面全体 (480×270)・原点は左上。
// 敵の攻撃はこちらが受けるので、 プレイヤーの攻撃の絵より手前 = 大きく出す (作者の指定)。 作り: 元の効果を、 式を書き直さずに、 この画面の大きさで流す (_scaled.js)。
import * as CLAW from "../melee/claw-swipe.js";
import * as HAMMER from "../melee/hammer-smash.js";
import * as CURSE from "../shadow/curse.js";
import * as SCYTHE from "../melee/scythe-reap.js";
import * as SUMMON from "../spawn/summon-rise.js";
import * as FIRERING from "../fire/fire-ring.js";
import * as HEALRING from "../holy/heal-aoe.js";
import * as FREEZE from "../ice/freeze.js";
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

// 鎖 (天与の指輪の鎖と同じ描き方: 環を 1 つずつ): 上から各点へ降りて来て、 張る。 最後に消える
function chains(spots, t0, t1) {
  const A = hex("#e4e0ea"), B = hex("#b8b2c0");
  let t = 0;
  return {
    update(dt) { t += dt; },
    draw(p) {
      const f = p.fade, k = 1 - clamp((t - t1) / 0.3);
      if (k <= 0) return;
      p.fade = f * k;
      spots.forEach(([x, y, d], i) => {
        const a = t - t0 - d;
        if (a < 0) return;
        const n = Math.ceil((y - 4) / 8), grow = clamp(a / 0.18);
        for (let s = 0; s < n * grow; s++) { const ly = s * 8 + ((y - 4) % 8), wob = Math.sin(s * 0.5 + t * 4 + i) * 2 * (1 - s / n); oval(p, x + wob, ly, s % 2 ? 2 : 3.5, s % 2 ? 4 : 2.5, 1, s % 2 ? B : A); }
        if (grow >= 1) p.glow(x, y - 2, 12, A, 0.5 * (1 - clamp((a - 0.18) / 0.4)));
      });
      p.fade = f;
    },
  };
}
const hexPlan = (speed) => [...SPOTS.map(([x, y, d]) => [PILLAR, { K: 0.75, at: [x, y], anchor: [0, 0], speed, delay: d, opts: { shafts: 3 } }]), ["chains", [SPOTS, 0.3, 1.3]]];

// 技の中身: [元の効果, { K 倍率, at 画面の位置, anchor 元の座標のどこをそこへ合わせるか, speed, delay, start, end, life, drop, opts }] の並び (重ねて流す)
const PLAN = {
  // ---- 攻撃 (こちらが受けるので、 大きく・画面の下寄りに)
  "longsword": [[VSLASH, { K: 1, at: [182, 250], anchor: [0, 0], drop: ["STAR"], opts: {} }]],   // 騎士・スケルトン・蛇鱗の戦士: ヴェスカの斬撃を、 きらめき抜きで武骨に
  "dagger": [[DAGGER, { K: 5, at: [240, 196], anchor: [28, -27], speed: 0.75, opts: { palette: IRON, sparks: ["#ffffff", "#e4e0ea", "#b8b2c0", "#8a8494"], flow: 5 } }]],   // その他の刃物: 短剣の通常を大きく
  "claw": [[CLAW, { K: 4.2, at: HIT, anchor: [40, -24], speed: 0.7, opts: {} }]],
  "blunt": [[HAMMER, { K: 3, at: [240, 228], anchor: [26, 0], speed: 1, drop: DEBRIS, opts: {} }]],
  "missile": [[MISSILES, { K: 1, at: [0, 0], anchor: [0, 0], opts: {} }]],
  "fire": [[SUMMON, { K: 2.4, at: [240, 244], anchor: [0, 0], speed: 2.6, end: 1.35, opts: {} }], [FIRERING, { K: 1, at: [240, 240], anchor: [0, 0], speed: 1.5, delay: 0.4, opts: { reach: 200 } }]],
  // ---- 13 番目の死
  "decree": [[CURSE, { K: 2, at: [240, 180], anchor: [0, 0], speed: 1.3, opts: {} }]],
  "decree-reap": [[SCYTHE, { K: 3.4, at: HIT, anchor: [36, -9], speed: 0.8, opts: {} }]],
  // ---- 補助
  "heal": [[HEALRING, { K: 1.5, at: FEET, anchor: [0, 0], speed: 1.6, opts: {} }]],
  "drain": [[DRAIN, { K: 3, at: [240, 238], anchor: [0, 0], speed: 1.1, opts: {} }]],
  "hex-gray": hexPlan(2.4),
  "hex-curse": hexPlan(2.0),
  "guard": [[GUARD, { K: 1, at: FEET, anchor: [0, 0], opts: { rx: 64, ry: 136 } }]],
  "armor-break": [[FREEZE, { K: 2.4, at: FEET, anchor: [0, 0], start: 2, speed: 1, opts: {} }]],
  "mirror": [[MIRROR, { K: 2.6, at: [240, 205], anchor: [0, 0], life: 1.5, opts: {} }]],
  "poison": [[PUDDLE, { K: 1, at: [240, 246], anchor: [0, 0], life: 2, opts: { width: 150 } }]],   // 点で打つ作りなので、 広げずに、 幅だけ大きく
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
  const parts = (PLAN[o.move] ?? PLAN.claw).map(([SRC, cfg]) => (SRC === "chains" ? chains(...cfg) : scaledFx(SRC, cfg)));
  return {
    update(dt) { for (const q of parts) q.update(dt); },
    draw(p) { for (const q of parts) q.draw(p); },
  };
}
