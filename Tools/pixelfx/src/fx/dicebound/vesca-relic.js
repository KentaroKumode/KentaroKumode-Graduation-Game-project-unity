// DICE BOUND 用 (2026-11-14): ヴェスカが遺物を使う時の、 汎用の 3 つ (毎ターン、 抽選された遺物の種類で出し分ける)。
//   kind: "strike" = 攻撃の遺物 (握斧・破城槌・殺戮 など) ── 胸の前から、 こちらの盤へまっすぐ突き刺す光の槍 + 当たりの炸裂。 (作者: 星が降るような回りくどい物でなく、 もっと直接の攻撃)
//         "guard"  = 防御の遺物 (手甲・城盾・真守 など) ── ヴェスカを包む光の球 (holy/divine-shield)。 現れて、 しばらく回って、 消える
//         "hex"    = 妨害の遺物 (塗り薬・麻痺毒・刻 など) ── こちらの盤の上に、 呪いの印が描かれて、 白く燃えて崩れる (shadow/hex-sigil)
// 画面全体 (480×270)・原点は左上。 毎ターン出るので短い (0.9〜1.25 秒)。 暗転や白飛びは使わない (それは大技だけ)。
// 作り: 元の効果を**そのまま呼んで**、 位置・大きさ・色・速さだけ変える (中身は書き直さない)。
//   strike = 自前の槍 (dicebound/chevalier-charge の glint と同じ、 細い線の描き方) + impacts/crit-hit (当たり)
//   guard  = holy/divine-shield (半径を 29 → 72 に。 ループの効果を、 現れて消える 1 回きりに)
//   hex    = shadow/hex-sigil (4.2 秒の効果を 3.6 倍の速さで流す)
// The impact, the shield bubble and the sigil are this repository's impacts/crit-hit, holy/divine-shield and shadow/hex-sigil, drawn as is
// (after Sprite Fusion's open-source effects, https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
import { fit, clamp, ease, resolveOptions, WHITE } from "../../core/index.js";
import * as HIT from "../impacts/crit-hit.js";
import * as SHIELD from "../holy/divine-shield.js";
import * as HEX from "../shadow/hex-sigil.js";
import { VIOLET } from "./vesca-charge.js";

const W = 480, H = 270, CHEST = [240, 130], FOOT = [240, 196], BOARD = [240, 247];

export const meta = {
  name: "Vesca relic (DICE BOUND)",
  desc: "Vesca uses a relic: a lance of light stabbed straight down onto the viewer's board (strike), a bubble of light round her (guard), or a cursed sigil burned onto the viewer's board (hex).",
  tags: ["dicebound", "boss", "relic", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 1.25,
  loop: false,
  fps: 30,
  bg: "dark",
  options: { palette: VIOLET, kind: "strike", seed: 3 },
};

/** 元の効果を、 画面の (x, y) に原点を置いて描く。 */
function at(p, x, y, fn) {
  const tx = p.tx, ty = p.ty;
  p.tx = tx + x; p.ty = ty + y;
  fn();
  p.tx = tx; p.ty = ty;
}

export function create(o) {
  const C = fit(o.palette, 8), kind = o.kind;
  let t = 0;
  // strike: 当たり (crit-hit)。 当たる点は、 元の効果の原点の height 上
  const HIT_T = 0.1, hit = kind === "strike" ? HIT.create(resolveOptions(HIT.meta, { palette: o.palette, height: 26, seed: o.seed })) : null;
  // hex: 印 (hex-sigil)。 印の中心は、 元の効果の原点の 50 上。 3.6 倍の速さで流す
  const SPEED = 3.6, hex = kind === "hex" ? HEX.create(resolveOptions(HEX.meta, { palette: o.palette, seed: o.seed })) : null;
  return {
    update(dt) {
      t += dt;
      if (hit && t >= HIT_T) hit.update(dt);
      if (hex) hex.update(dt * SPEED);
    },
    draw(p) {
      if (kind === "strike") {
        // 槍: 胸の前から盤へ、 0.1 秒で届く。 届いた後は細りながら 0.2 秒で消える。 白い芯・両脇に淡い縁・先は鋭い
        const reach = ease.outCubic(clamp(t / HIT_T)), fade = 1 - clamp((t - HIT_T) / 0.2), [x0, y0] = CHEST, y1 = y0 + (BOARD[1] - y0) * reach;
        if (fade > 0) {
          const w = 3 * fade + 0.6;
          for (let y = y0; y <= y1; y++) {
            const u = (y - y0) / Math.max(1, BOARD[1] - y0), hw = w * (0.35 + 0.65 * u);   // こちらへ近いほど太い (手前へ迫る)
            for (let x = Math.round(x0 - hw - 1); x <= Math.round(x0 + hw + 1); x++) {
              const d = Math.abs(x + 0.5 - x0);
              if (d <= hw * 0.5) p.put(x, y, WHITE); else if (d <= hw) p.put(x, y, C[6]); else p.put(x, y, C[4], 0.6 * fade);
              p.add(x, y, C[5], 0.5 * fade);
            }
          }
          at(p, x0, y0, () => p.glow(0, 0, 14 * fade + 4, C[5], 0.5 * fade));
        }
        if (t >= HIT_T) at(p, BOARD[0], BOARD[1] + 26, () => hit.draw(p));
      } else if (kind === "guard") {
        // 球: 0.15 秒で現れ (網点で濃くなる)、 回り、 最後の 0.25 秒で消える。 中心は胸
        const k = Math.min(clamp(t / 0.15), clamp((meta.duration - 0.05 - t) / 0.25)), f = p.fade, so = { palette: o.palette, radius: 72, seed: 8 };
        p.fade = f * k;
        at(p, FOOT[0], FOOT[1] - 44, () => { SHIELD.drawBack(p, t, so); SHIELD.draw(p, t, so); });   // 元の球の中心は原点の 22 上 → 胸 (足もとの 66 上) に合わせる
        p.fade = f;
      } else if (hex) {
        at(p, BOARD[0], BOARD[1] + 50 - 8, () => hex.draw(p));   // 印の中心を、 盤の少し上に
      }
    },
  };
}
