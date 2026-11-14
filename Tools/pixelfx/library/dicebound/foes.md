# ボス以外の敵の、 汎用の技 17 個 — 2026-11-14 たたき台 (作者の確認 2 回・ゲームへの組み込みはまだ)

効果は 1 本 `dicebound/foes` で、 `move` で出し分ける。 画面全体 (480×270)・原点は左上・1 秒 8 コマ。 見本: `lab/foes_preview.py` → `Captures/fxlib/_foes/index.html`。
流用はしない (作者: 安っぽくなる)。 敵の攻撃はこちらが受けるので、 プレイヤーの攻撃の絵より手前 = 大きく・画面の下寄りに出す。

| `move` | 使う敵 | 土台 | 作り |
|---|---|---|---|
| `longsword` | 騎士系・スケルトン・蛇鱗の戦士 | `dicebound/vesca-slash` | 筋と光を 3 割に・残像ときらめきなし・弧を短く太く遅く (上手すぎて見えないように) |
| `dagger` | その他の刃物 | `dicebound/dagger` | 3 太刀を真ん中に寄せて重ねる・間がばらつく・締めなし・光は 4 分の 1 (達人に見えないように) |
| `claw` | 狼・ハーピィ・キマイラ・血の眷属・蟲 | `foe-claw` (melee/claw-swipe の写し) | 芯 / 中間 / 輪郭の 3 つの帯・残る傷は太く明るく長く |
| `blunt` | スライム・オーク・暴牛・ゴーレム・番兵・毒沼の主 | melee/hammer-smash | 振りかぶりは流さず当たった所から・土色 |
| `missile` | 怨嗟の影・13 番目の死・鏡の双子 | `foe-missiles` (arcane/arcane-missiles の写し) | 敵から 6 方向 → 大きくなりながら外へ → こちらの盤へ誘導 |
| `fire` | 業火の眷属 | spawn/summon-rise (頭だけ) + fire/fire-ring | 敵の足もと |
| `decree` | 13 番目の死・落日の騎士 | `foe-decree` (shadow/curse の写し) | どくろの高さと粒の範囲を、 敵の体の幅・高さで決める |
| `decree-reap` | 13 番目の死 | `foe-reap` (melee/scythe-reap の写し) | 胴に流れの筋 |
| `heal` | スケルトン・黎明騎士・キマイラ | holy/heal-aoe | 大きさは `reach` と倍率で敵に合わせる |
| `drain` | 血の眷属 | `foe-drain` (shadow/life-steal の写し) + `foe-crosses` | こちらの盤 → 敵の胸。 吸い終わりに赤い十字 |
| `hex-gray` | コボルド | `foe-wires` (自前) | 盤のまわりの別々の点から別々の角度で、 細い灰色の線が張られる |
| `hex-curse` | 怨嗟の影 | shadow/hex-sigil | 敵の頭上 |
| `guard` | 大盾の番兵・石喰いの蟲 | `foe-guard` (impacts/shield-block の写し) | 敵をまるごと包む、 横に広い半球・網目は荒く線はか細く |
| `armor-break` | 落日の騎士・脱走兵 | `foe-crack` (ice/freeze の写し) | 氷の塊は描かず、 ひび → 白い光 → 破片 |
| `mirror` | 鏡の双子 | `foe-mirror` (arcane/portal の写し) | 中をべた塗り・薄い青磁・流れる反射の帯 |
| `poison` | 毒沼の主 | nature/toxic-puddle | 敵の足もと |
| `rage` | ゴブリン王・暴牛 | `foe-rage` (status/rage の写し) | 怒りマークと体の描き直しなし |

## 決まり (moves.md・weapons.md に加えて)

- **点を 1 つずつ打つ作りの効果は、 `_scaled.js` で広げない** (点が間引かれて、 まばらになる)。 広げずに置き、 幅や半径の設定だけ大きくする (shield-block・fire-ring・toxic-puddle)。
- `_scaled.js` に足した物: `start` / `end` (元の効果の途中から流す・途中で止める)・`drop` に粒の名前 (`"STAR"` `"MOTE"` など)。
- 敵が拙く見えるように: 手数を減らす・間をばらつかせる・弧を短く太く・光を弱く・きらめきを消す。
- 色の組 (見本用の仮): `iron` (鉄の灰)・`earth` (土)・`celadon` (薄い青磁)。

## 残り

- ゲームへの組み込み (敵ごとの割り当て・パッシブの発動との対応)。 炎と毒は、 細部が小さく弱い。
