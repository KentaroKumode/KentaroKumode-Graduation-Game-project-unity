// DICE BOUND 用 (2026-11-14): シュヴァリエの大技。 待機 (dicebound/chevalier-charged) の 0 / 6 コマ目から続く。
// きらめきが鋭く伸びる → 一点に消えて、 真っ黒な一拍 → 白い一閃が画面を斜めに走る → **黒い画面が、 切り口から左右に割れて離れていく** (2026-11-14 作者の指定)。
// 画面全体 (480×270)・原点は左上 (ここでは全部、 画面の左上からの座標)。
// ゲーム側の段取り: 待機の暗転 (かなり暗い) → きらめきが消えたら真っ黒 (100%) → 一閃のコマで暗幕を外す (そこからは、 この効果が黒い 2 枚を自分で描いて動かす)。
// 作り (transitions/slash-wipe の 2 太刀目と、 割れる所): 線に沿って裂ける一閃 (頭のすぐ後ろが一番太い・白い芯・尾がほどける)・切っ先のきらめき・
//   切り口に沿ってずれてから、 左右へ離れる 2 枚 (少し回る)・白く焼けて冷める切り口・継ぎ目からこぼれる光・継ぎ目から両側へ噴く火花。
// 変えた所: 1 太刀目 (画面を黒で覆っていく刃) は使わない ── 黒は初めから画面を覆っている。 切り口を立てた (60 度。 左右に割れて見えるように)。 離れる加速を 2 倍に (キレ優先)。 火花を短命に。
// (最初の版は 1 太刀目だけを使い、 刃の前だけに黒があって、 後ろ側が欠けていた ── 「斜めに切ったなら黒は両側へ離れるはず」)
// The cut's smear, the tip glint, the two halves slipping and parting with white-hot cut faces, and the sparks out of the seam are adapted from this repository's
// transitions/slash-wipe (after Sprite Fusion's open-source slash, https://www.spritefusion.com/pixel-art-vfx/slash), MIT License, Copyright (c) 2026 Hugo Duprez.
import { Particles, P, Rng, fit, hex, clamp, smooth, ease, sparkle, rand, WHITE } from "../../core/index.js";
import { STEEL, glint, REST } from "./chevalier-charge.js";

const W = 480, H = 270, CX = 240, CY = 130;

export const meta = {
  name: "Chevalier slash (DICE BOUND)",
  desc: "The Chevalier's decisive cut: the glint before his chest stretches hard and snaps out into a beat of black; a white slash rips diagonally across the screen and the blackness splits along it, the two halves flying apart.",
  tags: ["blade", "slash", "dicebound", "boss", "screen", "one-shot"],
  size: [W, H],
  origin: [0, 0],
  duration: 1.75,
  loop: false,
  fps: 30,
  bg: "dark",
  options: {
    palette: STEEL,
    sparks: ["#ffffff", "#e8f1ff", "#b8c9e8", "#62c8f4"],   // 青白い火花 (元の slash-wipe は暖色。 鋼の色に合わせた)
    color: "#020308",   // 黒い 2 枚の色 (ゲームの暗幕 100% と同じ色に)
    lead: 0.75,         // 切る前の溜め (秒): きらめきが伸びて、 消えて、 黒い一拍
    slant: 60,          // 切り口の傾き (度。 90 = 垂直)。 左下から右上へ。 (名前を angle にしてはいけない ── 道具が「効果全体を回す角度 (ラジアン)」として使う名前で、 粒や光が画面の外へ回される)
    accel: 3000,        // 2 枚が離れる加速 (px/秒²。 元の slash-wipe は 1500)
    face: "bands",      // 切り口の面の描き方 (見比べ用): bands = 1 ドットずつ等間隔に冷める段 / hard = 白い 1 本だけ / bloom = 白い 1 本 + 黒の上ににじむ光 / streak = 所々だけ深く焼けた筋
    seed: 2,
  },
};

// 一閃が走り始めるのは、 lead の少し前 (1 秒 8 コマの lead のコマで、 もう画面を渡り切っているように)
const PRE = 0.06, CSW = 0.07, CHOLD = 0.05, CTRAIL = 0.12, SLIP = 0.12, SPLIT = CSW + CHOLD + CTRAIL;
/** 一閃が走り始める時刻と、 割れ始める時刻 (ゲーム側が暗幕を外す・揺らす時に使う)。 */
export const timing = (lead) => ({ cut: lead - PRE, split: lead - PRE + SPLIT });

export function create(o) {
  const C = fit(o.palette, 8), n = C.length, ink = hex(o.color), R = new Rng(o.seed), T0 = o.lead - PRE;
  const ps = new Particles({ rng: R, sparks: o.sparks });
  // 切り口: 画面の中央を通る線。 u = 線の向き (左下 → 右上)・nrm = 線に直角 (左上向き)
  const th = (-o.slant * Math.PI) / 180, ux = Math.cos(th), uy = Math.sin(th), nx = uy, ny = -ux, cx = W / 2, cy = H / 2;
  const [A, B] = [-1, 1].map((s) => {
    let lo = 0, hi = 600;
    for (let k = 0; k < 30; k++) { const m = (lo + hi) / 2, x = cx + s * ux * m, y = cy + s * uy * m; if (x >= 0 && x <= W && y >= 0 && y <= H) lo = m; else hi = m; }
    return [cx + s * ux * lo, cy + s * uy * lo];
  });
  const LC = Math.hypot(B[0] - A[0], B[1] - A[1]);
  // 2 枚: s = +1 が左上の側・−1 が右下の側。 重心のまわりに少し回る
  const halves = [1, -1].map((s) => ({ s, mx: cx + s * nx * 110, my: cy + s * ny * 110, spin: s * R.range(0.35, 0.6) }));
  let t = 0, tipped = false, split = false;
  const placeOf = (hf, u) => {
    const slip = 6 * ease.outCubic(clamp((u - SLIP) / 0.12)) * (hf.s > 0 ? 1 : -0.4), v = Math.max(0, u - SPLIT);
    const sep = 40 * v + 0.5 * o.accel * v * v, rot = hf.spin * v * v;
    return { dx: ux * slip + hf.s * nx * sep, dy: uy * slip + hf.s * ny * sep, c: Math.cos(rot), s: Math.sin(rot), sep };
  };
  return {
    update(dt) {
      t += dt;
      const u = t - T0;
      // 一閃が抜けた: 切っ先から先へ火花、 線に沿って削り屑
      if (!tipped && u >= CSW) {
        tipped = true;
        for (let i = 0; i < 16; i++) { const a = Math.atan2(uy, ux) + R.range(-0.5, 0.5), v = R.range(180, 460); ps.spawn(P.SPARK, B[0] - ux * 2, B[1] - uy * 2, Math.cos(a) * v, Math.sin(a) * v, R.range(0.25, 0.5)); }
        for (let i = 0; i < 26; i++) {
          const k = R.range(0.1, 1), s = R.sign(), v = R.range(30, 110);
          ps.spawn(P.MOTE, A[0] + (B[0] - A[0]) * k, A[1] + (B[1] - A[1]) * k, s * nx * v + ux * 20, s * ny * v - 10, R.range(0.3, 0.6), R.chance(0.5) ? C[6] : C[5], 0.8);
        }
      }
      // 割れる: 継ぎ目から、 両側へ青白い火花が噴く (元の slash-wipe と同じ出し方・色だけ鋼の青白に)
      if (!split && u >= SPLIT) {
        split = true;
        for (let i = 0; i < 110; i++) {
          const k = R.range(0, 1), s = R.sign(), v = R.range(80, 340), a = Math.atan2(s * ny, s * nx) + R.range(-0.8, 0.8);
          ps.spawn(P.SPARK, A[0] + (B[0] - A[0]) * k, A[1] + (B[1] - A[1]) * k, Math.cos(a) * v, Math.sin(a) * v, R.range(0.15, 0.45));
          if (i % 3 === 0) ps.spawn(P.MOTE, A[0] + (B[0] - A[0]) * k, A[1] + (B[1] - A[1]) * k, Math.cos(a) * v * 0.5, Math.sin(a) * v * 0.5 - 15, R.range(0.25, 0.5), R.chance(0.5) ? C[6] : C[5], 0.8);
        }
      }
      ps.update(dt);
    },
    draw(p) {
      const u = t - T0;
      // 溜め: きらめきが鋭く伸び、 一点へ消える。 残りは黒い一拍 (ここは何も描かない ── 黒はゲーム側の暗幕)
      if (u < 0) {
        const L0 = o.lead, a = clamp(t / (L0 * 0.6)), b = clamp((t - L0 * 0.6) / (L0 * 0.2));
        if (b < 1) glint(p, C, CX, CY, (REST + 3 + 110 * ease.inQuad(a)) * (1 - ease.inCubic(b)));
        return;
      }
      // 黒い 2 枚。 切り口の面は、 白く焼けて、 冷めて、 氷の色の縁になる。 離れるほど面が見える
      const heat = 1 - clamp((u - 0.05) / 0.45), pl = halves.map((hf) => placeOf(hf, u));
      const rim = [heat > 0.55 ? WHITE : heat > 0.25 ? C[6] : C[5], heat > 0.4 ? C[6] : C[4], heat > 0.3 ? C[5] : C[3], heat > 0.3 ? C[4] : C[2], C[2], C[1]];
      if (pl[0].sep < 700)
        for (let y = 0; y < H; y++)
          for (let x = 0; x < W; x++)
            for (let h = 0; h < 2; h++) {
              // 画素を、 その 1 枚が動く前の位置へ戻して、 切り口のどちら側かを見る
              const hf = halves[h], q = pl[h], px = x + 0.5 - q.dx - hf.mx, py = y + 0.5 - q.dy - hf.my, lx = hf.mx + px * q.c + py * q.s, ly = hf.my - px * q.s + py * q.c;
              const d = ((lx - cx) * nx + (ly - cy) * ny) * hf.s;
              if (d < 0) continue;
              const deep = clamp(1 + q.sep * 0.12, 1, 6);
              const hot = heat > 0.55 ? WHITE : heat > 0.25 ? C[6] : C[5];
              if (o.face === "hard") p.put(x, y, d < 1 ? hot : ink);
              else if (o.face === "bloom") {
                // 白い 1 本。 その奥は黒のまま、 光だけがにじむ (色の段を置かない ── 液晶では網点の淡い光になる)
                p.put(x, y, d < 1 ? hot : ink);
                if (d < 14) p.add(x, y, C[5], 0.75 * heat * (1 - d / 14) ** 2);
              } else if (o.face === "streak") {
                // 切り口に沿って、 所々だけ深く焼けている (3 ドット幅の筋ごとに深さが違う)。 浅い所は白い 1 本だけ
                const along = Math.floor(((lx - cx) * ux + (ly - cy) * uy) / 3), r = rand(along, hf.s + 7, o.seed), dep = 1 + (r > 0.62 ? (r - 0.62) * 26 * (0.3 + 0.7 * heat) : 0);
                if (d < 1) p.put(x, y, hot);
                else if (d < dep) { const q = (d - 1) / (dep - 1); p.put(x, y, q < 0.25 ? C[6] : q < 0.6 ? C[4] : C[3]); p.add(x, y, C[5], 0.3 * heat * (1 - q)); }
                else p.put(x, y, ink);
              } else if (d < deep) { p.put(x, y, rim[Math.floor(d)]); if (d < 3) p.add(x, y, C[5], 0.4 * heat * (1 - d / 3)); }
              else p.put(x, y, ink);
              break;
            }
      // 継ぎ目からこぼれる光
      if (heat > 0)
        for (let h = 0; h < 2; h++) for (let i = 0; i < 12; i++) {
          const k = (i + 0.5) / 12, q = pl[h];
          p.glow(A[0] + (B[0] - A[0]) * k + q.dx, A[1] + (B[1] - A[1]) * k + q.dy, 14 + Math.min(40, q.sep * 0.15), C[5], 0.3 * heat);
        }
      // 一閃: 線に沿って裂ける。 頭のすぐ後ろが一番太く、 芯は白、 尾はほどける。 割れるまで
      if (u < SPLIT) {
        const head = LC * ease.outExpo(clamp(u / CSW)), tail = LC * smooth(CSW + CHOLD, SPLIT, u), age = clamp(u / SPLIT), span = head - tail, Wc = 6 * (1 - (tail / LC) * 0.55);
        if (span > 1)
          for (let s = tail; s <= head; s += 0.5) {
            const v = (s - tail) / span, w = Wc * Math.pow(Math.sin(Math.PI * Math.pow(v, 2.2)), 0.6);
            if (w < 0.35) continue;
            for (let c = -w; c <= w; c += 0.5) {
              const q = Math.abs(c) / w, ci = q < 0.34 && age < 0.5 ? 0 : Math.round(q * (n - 1) * 0.75 + age * 2.4 - v * 0.8 + 0.4);
              const x = Math.round(A[0] + ux * s + nx * c), y = Math.round(A[1] + uy * s + ny * c);
              p.put(x, y, C[n - 1 - clamp(ci, 0, n - 1)], Math.min(1, v / 0.4));
              if (q < 0.5) p.add(x, y, C[5], 0.4 * (1 - age));
            }
          }
        // 走り去った後に残る点線
        for (let s = 0; s < tail; s += 1.6) p.put(Math.round(A[0] + ux * s), Math.round(A[1] + uy * s), C[5], 0.7 * (0.4 + (0.6 * s) / LC));
        if (u < 0.3) p.glow(A[0] + ux * head, A[1] + uy * head, 60, C[4], 0.55 * (1 - u / 0.3));
      }
      // 切っ先のきらめき
      const g = u - CSW * 0.6;
      if (g > 0 && g < 0.24) sparkle(p, B[0] - ux * 4, B[1] - uy * 4, 16 * ease.outBack(Math.min(1, g / 0.05)) * (1 - g / 0.24), C[6], C[4], 0.7);
      ps.draw(p);
    },
  };
}
