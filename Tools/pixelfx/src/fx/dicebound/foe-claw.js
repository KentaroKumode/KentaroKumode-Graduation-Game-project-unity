// DICE BOUND 用 (2026-11-14): 爪・牙の敵の攻撃 (ダイアウルフ・ハーピィ・キマイラ・血の眷属・這い虫・石喰いの蟲)。 melee/claw-swipe の写し。
// (作者: そのまま拡大すると安っぽい。 爪の中心の線と外側の線にめりはりを。 後に残る赤い線は、 もっと強く・太く)
// 変えた所は、 塗りの色の分け方と、 残る傷の線だけ。 3 本の爪の形・動き・火花・滴は元の式のまま。 _scaled.js で大きくして使う (foes.js)。
// The shaded smear, its light and the dotted trace follow Sprite Fusion's open-source slash
// (https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, Bits, Lights, P, B, Rng, fit, sparkle, clamp, smooth, ease } from "../../core/index.js";
import { through, onSwing, travel, smear, trace, glowAlong } from "../melee/_lib.js";

export const meta = {
  name: "Foe claw (DICE BOUND)",
  desc: "Three claw marks rake down: a white-hot core inside a dark red rim, then thick red scars that linger and glow.",
  tags: ["dicebound", "enemy", "claw", "one-shot"],
  size: [128, 96],
  origin: [36, 84],
  duration: 1.3,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: ["#2a0808", "#5c0f12", "#a51d1d", "#e2401f", "#ff8a2a", "#ffd166", "#fff3c4", "#ffffff"],
    sparks: ["#fff3c4", "#ffd166", "#ff8a2a", "#c42a1a"],
    seed: 8,
  },
};

const RAKE = 0.05; // each mark tears open
const STAGGER = 0.04;
const CLOSE0 = 0.16, CLOSE = 0.3; // then closes up from both ends

export function create(o) {
  const C = fit(o.palette, 8), n = C.length, Rg = new Rng(o.seed), floor = () => 0;
  const ps = new Particles({ rng: Rg, ground: floor, sparks: o.sparks }), drips = new Bits({ ground: floor }), lights = new Lights();
  // three marks raking down and back, each a little lower and further out than the last
  const marks = [0, 1, 2].map((i) => {
    const s = through(31 + i * 8, -23 + i * 2, 1.84, 1, 40, 0.84 - i * 0.05, 0.5);
    return { s, t0: i * STAGGER, W: 4.2 - i * 0.4, open: false, end: travel(s, s.span) };
  });
  let t = 0;
  const heatAt = (m) => clamp(1 - (t - m.t0) / 0.42);
  return {
    update(dt) {
      t += dt;
      for (const m of marks) {
        if (!m.open && t >= m.t0 + RAKE * 0.7) {
          m.open = true;
          lights.add(m.end.x, m.end.y - 6, 34, C[4], 0.45, 0.3);
          // hot flecks thrown on along the rake, and drips running off the wound
          for (let i = 0; i < 7; i++) {
            const a = Math.atan2(m.end.dy, m.end.dx) + Rg.range(-0.5, 0.5), sp = Rg.range(110, 260);
            ps.spawn(P.SPARK, m.end.x, m.end.y, Math.cos(a) * sp, Math.sin(a) * sp, Rg.range(0.12, 0.32));
          }
          for (let i = 0; i < 5; i++) {
            const [x, y] = onSwing(m.s, m.s.R, m.s.span * Rg.range(0.15, 0.9));
            ps.spawn(P.MOTE, x, y, Rg.range(-60, -10), Rg.range(-30, 30), Rg.range(0.2, 0.4), Rg.chance(0.5) ? C[5] : C[4], 0.5);
          }
          for (let i = 0; i < 2; i++) {
            const [x, y] = onSwing(m.s, m.s.R - 1, m.s.span * Rg.range(0.35, 0.75));
            drips.add({ kind: B.DRIP, x, y, vx: Rg.range(-8, 8), vy: Rg.range(10, 40), g: 0.55, life: 1.2, c: C[3] });
          }
        }
      }
      drips.update(dt);
      ps.update(dt);
      lights.update(dt);
    },
    draw(p) {
      for (const m of marks) {
        const u = t - m.t0;
        if (u < 0) continue;
        const S = m.s.span, head = S * ease.outExpo(clamp(u / RAKE)), c = smooth(CLOSE0, CLOSE0 + CLOSE, u), lo = (S / 2) * c, hi = Math.min(head, S - (S / 2) * c);
        const heat = heatAt(m);
        if (hi - lo > 0.02) {
          // a lens: sharp at both ends, thickest in the middle; white-hot at the core, cooling outward and with time
          smear(p, m.s, lo, hi, m.W, (uu, r, out) => {
            const v = uu / S, w = m.W * Math.pow(Math.sin(Math.PI * v), 1.15) * (1 - 0.55 * c), off = Math.abs(r - (m.s.R - m.W / 2)) / (w / 2 + 0.01);
            if (w < 0.5 || off > 1) return;
            // ---- ここが元と違う (作者: 爪の中心の線と外側の線に、 もっとめりはりを)。 元は芯から外へなだらかに冷める。 ここでは 3 つの帯にはっきり分ける:
            //   芯 (白 → 熱が引くと明るい色)・その外 (中間の赤)・一番外の縁 (暗い赤の輪郭)
            const ci = off < 0.36 ? Math.round((1 - heat) * 2.6) : off > 0.74 ? 5 + Math.round((1 - heat) * 1.2) : 3 + Math.round((1 - heat) * 1.6);
            out.c = C[n - 1 - clamp(ci, 0, n - 2)];
          }, 0, 2);
          glowAlong(p, m.s, m.s.R - m.W / 2, lo, hi, 11, C[4], 0.42 * heat * heat, 0, 0.2);
        }
        // where it has closed, a dotted red scar lingers and fades
        // ---- ここが元と違う (作者: 攻撃の後に残る赤い線は、 もっと強く・太く)。 元は細い点線 1 本。 ここでは、 明るい赤の芯 + 両脇の暗い赤 (3 本ぶんの太さ) を、 長く残し、 光らせる
        const k = 1 - clamp((u - CLOSE0 - CLOSE * 0.5) / 0.75);
        if (c > 0.05 && k > 0) {
          const rc = m.s.R - m.W / 2;
          for (const [d, col, a] of [[-0.28, C[2], 0.9], [0.28, C[2], 0.9], [0, C[4], 1]]) {
            trace(p, m.s, rc + d, 0.04 * S, lo, col, Math.min(1, k * 1.5) * a, 0, 0.12);
            trace(p, m.s, rc + d, S - lo, 0.96 * S, col, Math.min(1, k * 1.5) * a, 0, 0.12);
          }
          glowAlong(p, m.s, rc, 0.04 * S, lo, 6, C[3], 0.45 * k, 0, 0.2);
          glowAlong(p, m.s, rc, S - lo, 0.96 * S, 6, C[3], 0.45 * k, 0, 0.2);
        }
        // the claw tip glints as it tears in
        const g = u - RAKE * 0.4;
        if (g > 0 && g < 0.16) sparkle(p, m.end.x, m.end.y, 5 * ease.outBack(Math.min(1, g / 0.04)) * (1 - g / 0.16), C[6], C[4], 0.4);
      }
      drips.draw(p);
      ps.draw(p);
      lights.draw(p);
    },
  };
}
