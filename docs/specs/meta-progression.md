---
title: メタ進行（恒久バフ／挑戦デバフ）
description: ラン跨ぎの恒久バフトラックと、挑戦モードの段階デバフ・固有恒久デバフ
status: provisional
related: [overview, shop-economy, boss]
---

# メタ進行（恒久バフ／挑戦デバフ）

## Purpose

ラン跨ぎの恒久強化（トークンで段階解放）と、難易度を上げる挑戦モードのデバフ群。
正本: [MetaProgression/](../../Assets/Scripts/MetaProgression/)。
※本 spec はコード（enum/定数）から事実抽出済。効果量の適用箇所までの追跡は別途
（[meta-progression-scope](../open-questions/meta-progression-scope.md)）。

## Definition

### 恒久バフ（[MetaBuffKind.cs](../../Assets/Scripts/MetaProgression/MetaBuffKind.cs)）

小スキル（段階加算）と大スキル（昇格カウント）に分かれる。

- 小：Hp(+1HP) / Gold(+1開幕ゴールド) / DiceTotal(+1ダイス合計) / DamageReduce(−1被ダメ,最大−2) /
  HungerReduce(戦闘後の希望ゲージ減少−1,最大−3 ※飢餓は希望に統合ADR-0002) / StartMaterial(+1開幕素材) / CombatGoldBonus(+1戦闘勝利ゴールド) /
  FloorClearHeal(フロアクリア+1HP,最大+2)。
- 大：BossExtraNormal / BossExtraRare（ボス撃破の追加パッシブ報酬と昇格）／ RefundLevelUp(返金5/10/15%) /
  CritLevelUp(会心ダイス+1/+2/+3) / DivineProtect〈神の加護〉(1戦1回ロール敗北→引分) /
  StartingPassiveItem(開幕パッシブ1個) / TreasureChestGold(宝箱ゴールド復活) /
  ShopRobberyUnlock(ショップ「値下げ」=強盗 行動解禁)。
- 各段階効果は `MetaBuffTrack` 内で定義。適用は `MetaBuffApplicator`。

### 挑戦モード段階デバフ（[MetaDebuff.cs](../../Assets/Scripts/MetaProgression/MetaDebuff.cs)）

Lv1〜Lv10 を独立トグルで重複適用可。**全 ON＝バフトラック満開放が最低クリア条件**を想定。

| Lv | 名称 | 効果 |
|----|------|------|
| 1 | 困窮した商隊 | ショップ価格 +25% |
| 2 | 俊敏 | 敵が各戦闘の最初の1回の被弾を必ず回避 |
| 3 | 向かい風 | 敵への与ダメ −1 |
| 4 | 前途多難 | マップ視界 2マスで遮断 |
| 5 | 偽の商人 | ショップマスが30%で偽商人化（特殊エリート戦・3T後逃走で恒久アイテム1喪失／勝利でレア恒久1獲得） |
| 6 | 死神の影 | 3層突入時に固有恒久デバフ +1 |
| 7 | 補給断絶 | 前哨基地の回復上限 = 最大HPの50% |
| 8 | 飢餓の極地 | 飢餓ダメージ ×2 |
| 9 | 鋼の皮膚 | 敵が初回致命傷を HP1 で耐える |
| 10 | 天変地異 | 全戦闘で敵ダメ +100% / 1層突入時に恒久デバフ +1 / ラストスタンド不発 |

### 固有恒久デバフ（7つの大罪・[PermanentDebuffIds.cs](../../Assets/Scripts/MetaProgression/PermanentDebuffIds.cs)）

メタ層から付与され `run.permanentDebuffs` に格納。

| ID | 効果 |
|---|---|
| カイロスの傲慢 | 通常戦闘の勝利報酬0、エリート/ボスでは2倍 |
| ヤルノクの嫉妬 | 1ショップにつき1個しか購入不可 |
| ムシュファの強欲 | 5層突入時にゴールドが0になる |
| コルヴェンの憤怒 | ボス戦1T目：自ダイス全最大＋現在HP半減 |
| クェシナの怠惰 | 戦闘開始から3T間、消費アイテム使用不可 |
| トゥルハドの暴食 | 飢餓ダメージ ×2 |
| クァディルの色欲 | ショップ入店時、買える中で最も高価な品を強制購入 |

## Constraints

- MUST：段階上限を守る（DamageReduce 最大−2、HungerReduce 最大−3、FloorClearHeal 最大+2 等）。
- MUST：挑戦デバフ Lv は独立トグルで重複適用可能とする。

## Open Questions

- [meta-progression-scope](../open-questions/meta-progression-scope.md)

決定済（実装待ち）：[ADR-0002 希望システム](../adr/0002-hope-system.md) — 飢餓を希望へ統合。
Lv8「飢餓の極地」→「絶望的な進軍」（移動毎 希望−1）、恒久「トゥルハドの暴食」・メタ `HungerReduce` を希望文脈へ再マップ。

## See Also

- Specs: [shop-economy](shop-economy.md), [boss](boss.md), [overview](overview.md)
- ADR: [0002-hope-system](../adr/0002-hope-system.md)
- 正本: [MetaProgression/](../../Assets/Scripts/MetaProgression/)
