# プレイヤーの武器の攻撃 8 個 — 2026-11-14 作者が「これでいい」と決めた形

家系ごとに、 **通常**と**会心**の 2 つ (最初は段 2 / 3 / 4 の 12 個で作り、 作者が計画を変えた)。 効果は 1 本 `dicebound/weapons` で、 `weapon` で出し分ける。
どれも画面全体 (480×270)・原点は左上・1 秒 8 コマ。 プレイヤーの攻撃なので、 絵は敵の体の上に出る。 見本: `lab/weapons_preview.py` → `Captures/fxlib/_weapons/index.html`。

| 家系 (`items.json` の `family`) | `weapon` | 絵 | 色の組 |
|---|---|---|---|
| 剣 `sword` | `sword-normal` | 一太刀 (三日月)。 流れの筋を少し | steel |
| | `sword-crit` | ぐるりと一周 → 交差する二太刀 (光った一瞬のあと中空)・金 | holy |
| 斧 `axe` | `axe-normal` | 縦に振り下ろす刃 + 足もとの土煙と輪 | steel |
| | `axe-crit` | 太い縦の刃 (残像つき) + 地面が割れる (衝撃の三日月・ひび・高い土煙)・金 | holy |
| 短剣 `dagger` | `dagger-normal` | 小さな連撃 6 つ + 締めの一太刀。 縁が光る | steel |
| | `dagger-crit` | 真横の大きな斬撃 (両端が尖り、 中空) → 連撃・金 | holy |
| 盾 `shield` | `shield-normal` | 上から叩きつける (重い光の柱 + 下向きの筋の束) + 足もとの土煙 | steel |
| | `shield-crit` | 同じ形を大きく・金・地面が割れる | holy |

- `meta.duration` は 2.0。 書き出したら、 後ろの空のコマは捨てる (実際は 0.8〜1.6 秒)。
- 色: **通常は 4 家系とも鋼の青白・会心は 4 家系とも金** (作者の指定で揃えた)。
- 飛び散る岩・小石・砂利は出さない (`drop: ["CHUNK", "GRIT"]`)。

## 作り

元の効果を、 **式を書き直さずに**この画面の大きさで流す (`dicebound/_scaled.js`)。 描画の座標の変換に倍率 (と回転) を通すだけなので、 面は画面の解像度で描き直され、 ドットは大きくならない。
`weapons.js` の `PLAN` が、 技ごとに [元の効果, { K 倍率, at 置く場所, anchor, speed, delay, rot, drop, opts }] を並べて重ねる。

| 部品 | 元 | 変えた所 |
|---|---|---|
| `blade-slash.js` | `melee/sword-slash` の写し | 塗りに流れの筋を少し |
| `cross-slash.js` | `melee/double-slash` の写し | 中空 (`ringSlash` ── `melee/spin-slash` の作り: 頭の後ろだけ太い → 閉じると白い帯で光る → 細って点線に)・2 太刀目を 1 太刀目の鏡写しに (正面から見て×が歪まないように) |
| `chop.js` | 自前 (melee の swing / smear) | 斧の縦の刃。 縁の発光・跡のブルーム (会心は強い)・内側の残像 |
| `dagger.js` | `melee/dagger-flurry` の写し | 塗りを `glowSlash` に・会心は最初に真横の斬撃 (`ringSlash`・`sharp` で両端を尖らせる) |
| `_blade.js` | `melee/_lib` の slash() の写し | `glowSlash`: 縁の発光・跡を明るい線で残す・ブルーム・流れの筋。 光の広さは斬撃の太さに合わせる |
| `slam-column.js` | 自前 | 盾の、 落ちて来る重い柱 |
| そのまま流す物 | `melee/spin-slash`・`melee/thrust` (90 度回して下向き)・`impacts/heavy-landing`・`impacts/ground-slam` | 大きさ・色・速さだけ |

## 作者の指摘で決まったこと (moves.md の決まりに加えて)

- **中空 (抜き感)** は「縁だけ残す」ではなく、 Spin slash の作りそのまま。 べたに塗るのは、 振り抜く一瞬と白く光る一瞬だけ。
- **正面から見る構図に、 横から見た絵を置かない**: Double slash の×は左右非対称 (斬り下ろしと斬り上げ)・Heavy cleave は右から殴って見える。 コマ送り (1/30 秒ずつ) にして形を確かめる。
- **縁と、 縁の残像は強く光らせてよい**。 斬った跡にブルーム。 細い斬撃には、 太さに合わせて弱く (同じ光を乗せると塊に埋もれる)。
- **端は尖らせる** (丸い端はシャープさが足りない)。 形だけでなく、 光も端で絞る。
- **鈍重に** = 遅く・大きく・太い塊を足す。
- **瓦礫は出さない**。 `impacts/blunt-hit` の白い塊と棘も使わない。

## 残り

- ゲームへの組み込み (会心かどうかと、 武器の `family` で出し分ける)。
