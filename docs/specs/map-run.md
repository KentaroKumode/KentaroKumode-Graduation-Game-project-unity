---
title: マップ・ラン進行（分岐ボード）
description: フロアの3レーン分岐ボード生成、マス種別、フロアモディファイア、エリート化
status: provisional
related: [overview, boss, shop-economy, lambda-layer]
---

# マップ・ラン進行（分岐ボード）

## Purpose

各フロアは前哨基地から層ボスへ向かう **3レーンの分岐ボード**。プレイヤーはノード間接続を
辿って進み、踏んだマスの種別に応じた処理（戦闘・イベント・ショップ等）を解決する。
正本: [MapSystem/](../../Assets/Scripts/MapSystem/), 進行制御は [GameLoop/GameManager.cs](../../Assets/Scripts/GameLoop/GameManager.cs)。

## Definition

### ボード構造（[MapGenerator.cs](../../Assets/Scripts/MapSystem/MapGenerator.cs)）

- `LaneCount = 3`、`RowCount = 10`（Row 0 = 前哨基地、Row 9 = 休憩）。
- 接続：前方／斜め（`DiagonalChance = 0.6`）／同行横移動（`LateralChance = 0.3`）。
- 収束ノード（`lane = -1`）：前哨基地・ボス。
- **5層のみ** ボス手前に `karma_trap`（収束ノード）を挿入し、全レーンから接続。
- 6層は専用マップ（`laneCount = 1`, `rowCount = 4`）。

### 層タイトル／サブタイトル（層進入時表示）

各層の前哨基地に入った瞬間に表示する確定文言。浅層＝現実的／深層＝異質、という世界観の弧
（[lore-endings.md](lore-endings.md) §1）に沿う。ビジュアル演出は別途。

| 層 | ボス | タイトル | サブタイトル |
|---|---|---|---|
| 1 | トレジャーゴブリン | 第一層　陽の差す浅層 | 振り返れば、まだ入口の光が見える |
| 2 | ゴブリン王 | 第二層　群れの領分 | 石の通路に、無数の足音が反響する |
| 3 | 毒沼の主 | 第三層　毒の沼 | 空気が変わる。息をするたび、喉の奥が湿る |
| 4 | 鏡の双子 | 第四層　鏡像の界 | 影が、ひとつだけ多い |
| 5 | 業火の審判官 | 第五層　審きの淵 | 多くの者が、ここで引き返す。引き返せた者は |
| Λ | （時間の狭間） | 時間の狭間 | 同じ角を、もう何度曲がっただろう |
| 6 | 灰燼の王 | 第六層　灰の玉座 | 滅びを見届ける番人が、ここに座している |
| 7 | 覚者・初眼 | 終層　Null Point | Signal lost |

- 5層裏ボス（剣聖シュヴァリエ・サン=ジョリオラ）は5層内のため層タイトルは共通。必要なら専用差し込みを別途。
- 文言データ＝[LayerTitles.cs](../../Assets/Scripts/GameLoop/LayerTitles.cs)（本表が正本）。発火は `GameManager.EnterFloor`／`EnterLambda`（層進入時）。
  現状はログ出力＋`LayerTitles.OnShow`（(title, subtitle) イベント）まで。**バナー等ビジュアルUIは未実装**（OnShow を購読して後付け）。

### マス種別（[TileType.cs](../../Assets/Scripts/MapSystem/TileType.cs)）と抽選重み

| TileType | 内容 | 抽選重み |
|---|---|---|
| Battle | 戦闘 | 29 |
| Event | イベント（レアリティ抽選） | 25 |
| EliteBattle | 激戦（連続2戦＋ボーナス） | 14 |
| Trap | 罠 | 10 |
| Exchange | 交換（パッシブ1つ→上位Tierをランダム入手） | 7 |
| Shop | ショップ | 7 |
| Treasure | 秘宝 | 3 |
| Rest | 休憩（HP回復 or 強化） | 2 |

非抽選（固定配置）: Outpost / Boss / SinAltar（6層儀式祭壇）/ LambdaRing / LambdaExit。
`Mystery`(?マス) は廃止（生成されない。enum は互換のため残置）。

### トラップマスの現状（事実）

- ランダム `Trap` マスは抽選生成されるが、**`GameManager.HandleTrapTile` で通常罠は効果ゼロ**
  （コード上「それ以外の通常罠マスは現状効果なし」）。
- 例外は `karma_trap`（5層ボス前の専用ノード）のみで、**カルマ清算**を実行：
  最大HP −= カルマ×10（最低1）、清算後 `karma = 0`。
  → ただし [ADR-0002](../adr/0002-hope-system.md)（希望システム採用）により**カルマ廃止＝karma_trap は撤去予定**。
- **決定**：このランダム Trap は撤去予定（死にノードのため）。
  → [ADR-0001](../adr/0001-remove-trap-tiles.md) ／ 実装は [phase-1](../plans/phase-1-decided-changes.md)。

### ノード状態（[MapNode.cs](../../Assets/Scripts/MapSystem/MapNode.cs)）

- `visited` / `activated`（再訪時の再発火防止）／ `resolvedType`（Mystery 解決後・現状未使用）。
- `revealed`：難易度0では全可視。高難易度ではマスを隠せる（メタ挑戦デバフ Lv4「前途多難」=視界2マス）。
- `isFalseMerchant`：メタ挑戦デバフ Lv5「偽の商人」で Shop マスが偽商人化。

### エリート化（`GameManager.MaybeMakeElite`）

- 4層以降の EliteBattle マスで敵を「精鋭」化：DB 共有を汚さずクローンに HP×2 / threat+2 /
  精鋭パッシブ（3T毎ダイス+1）。スライム／ゴブリン等は対象外。

### フロアモディファイア（[FloorModifier.cs](../../Assets/Scripts/MapSystem/FloorModifier.cs)）

- `perTurnSelfDamage` / `enemyPerTurnHeal` / `defeatDamageReduction` / `coinRewardMultiplier` /
  `shopPriceMultiplier` を実フックに接続。ショップ価格は 4層 +20% / 5層 +40%。

### ハンガー（[HungerSystem.cs](../../Assets/Scripts/MapSystem/HungerSystem.cs)）

- 空腹度に応じた飢餓ダメージ。メタ挑戦デバフ／恒久デバフで ×2 になりうる。

## Constraints

- MUST：マス処理の追加・変更は `GameManager` のタイル dispatch（`EffectiveType` switch）に従う。
- MUST：再訪可能マス（LambdaRing 等）以外は `activated` で1回起動を保証する。

## Open Questions

（トラップ・カルマ／希望は決定済 → [ADR-0001](../adr/0001-remove-trap-tiles.md) / [ADR-0002](../adr/0002-hope-system.md)。実装は plans 参照）

## See Also

- Specs: [boss](boss.md), [shop-economy](shop-economy.md), [lambda-layer](lambda-layer.md)
- ADR: [0001-remove-trap-tiles](../adr/0001-remove-trap-tiles.md), [0002-hope-system](../adr/0002-hope-system.md)
