// DICE BOUND 用 (2026-11-18・精鋭のオーラの候補): foe-rage (status/rage の写し・怒りマークと体の描き直しを外した物) を、
// _scaled.js の withScale で K 倍に描く (式は 1 行も書き直さない・foes.js の "rage" と同じ作り)。
//   体の大きさは width・height (元の単位) で敵に合わせる。 K × width ≒ 敵の幅、 K × height ≒ 敵の高さ。
//   尺: 元の 1.2 秒を 1.25 秒に引き伸ばす (速さ × 0.96・1 秒 24 コマで割り切れるように)。
//   注意: ゴブリン王・暴牛の技「自己強化」(foes.js の rage) と同じ素材。 選ぶなら色か形で分ける。
import { resolveOptions } from "../../core/index.js";
import { withScale } from "./_scaled.js";
import * as RAGE from "./foe-rage.js";

export const meta = {
  name: "Elite rage heat (DICE BOUND)",
  desc: "The foe-rage loop scaled up round an elite foe: red heat shimmering up, steam on each heartbeat, hot cracks underfoot.",
  tags: ["dicebound", "elite", "aura", "rage", "loop"],
  size: [480, 270],
  origin: [240, 204],
  duration: 1.25,
  loop: true,
  fps: 24,
  bg: "dark",
  options: { scale: 3, width: 32, height: 38, seed: 6 },
};

const SPEED = RAGE.meta.duration / meta.duration;
const inner = (o) => resolveOptions(RAGE.meta, { width: o.width, height: o.height, seed: o.seed });

export function drawBack(p, t, o) {
  const oo = inner(o);
  withScale(p, o.scale, 0, 0, () => RAGE.drawBack(p, t * SPEED, oo));
}

export function draw(p, t, o) {
  const oo = inner(o);
  withScale(p, o.scale, 0, 0, () => RAGE.draw(p, t * SPEED, oo));
}
