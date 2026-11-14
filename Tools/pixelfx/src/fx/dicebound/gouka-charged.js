// DICE BOUND 用 (2026-11-14): 灰燼の王の〈業火〉が溜まった状態の待機 (チャージのターンの後、 大技のターンまで出し続けるループ)。
// 王の胸に、 白く焼けた芯と十字の光が脈を打ち、 火の粉がまばらに、 ゆっくり吸い寄せられる。 画面全体 (480×270)・原点は王の足もとの中心。
// 絵は gouka-charge.js の held() ── チャージの終わりも同じ関数で描くので、 チャージからこのループへ、 見た目も脈も途切れずに続く。
// 1.5 秒で継ぎ目なく回る (1 秒 8 コマで 12 コマ)。
import { fit } from "../../core/index.js";
import { meta as CHARGE, held, HELD, HELD_SEED } from "./gouka-charge.js";

export const meta = {
  name: "Gouka charged (DICE BOUND)",
  desc: "The Ash King holds his gathered inferno: a white-hot point and a cross of light pulse at his chest while a few embers drift slowly in.",
  tags: ["fire", "dicebound", "boss", "charge", "idle", "loop"],
  size: [480, 270],
  origin: [240, 196],
  duration: HELD,
  loop: true,
  fps: 30,
  bg: "dark",
  options: { palette: CHARGE.options.palette, chest: 58 },
};

export function draw(p, t, o) {
  held(p, fit(o.palette, 9), -o.chest, HELD_SEED, t, 0);
}
