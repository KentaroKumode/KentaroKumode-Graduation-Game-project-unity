---
title: 用語集
description: 本プロジェクト固有のドメイン用語と対応するコード上の概念
status: provisional
related: [overview, combat, lambda-layer, meta-progression]
---

# 用語集

| 用語 | 意味 | コード上の対応 |
|---|---|---|
| ラン | 1回のプレイ。1層〜最大7層。 | `RunState` |
| 前哨基地 (Outpost) | 各層の開始マス。休憩＋秘宝＋層バフ/デバフ。 | `TileType.Outpost` |
| 〈確信〉 | エリート勝利で上がる段階値。3=〈決意〉, 6+=〈真理〉。6F/7F 進入ゲート。 | `RunState.convictionStage` |
| SinDebuff | 6層 SinAltar の儀式不払いで付く永続デバフ（6層ボス戦のみ作用）。 | `SinDebuff` enum |
| Λ層（時間の狭間） | 5層ボス撃破＋〈決意〉で強制突入する周回エリア。 | `RunState.inLambda` |
| 次元の乱れ | Λ層で踏んだマスの累積。3 毎に Λ恒久デバフを付与。 | `dimensionalDisturbance` |
| Λデバフ | Λ層由来の恒久デバフ（7種・段階1〜3）。 | `lambdaDebuffs`, `LambdaDebuffIds` |
| 業物／限界突破 | T4+ 到達後に休憩で上がる段階(0-10)。1lvごとダイス合計+2・与ダメ+2。 | `limitBreakStage` |
| 武器の "+" 段階 | T_n と T_n+ を表す(0/1)。休憩強化で 0→1、次に次Tierへ置換。 | `weaponPlus` |
| 刻印 (Sigil) | パッシブアイテム取得時に1回ロールされ、ラン中不変の付加特性。 | `PassiveSigil`, `passiveSigils` |
| チケット | コイン10枚=1チケットの換算単位。 | `CoinTicketConversionManager` |
| 希望 | 飢餓・カルマを統合した精神ゲージ (ADR-0002)。0で発狂。イベント表記 "空腹度±N" / "希望±N" は等価。 | `HopeSystem`, `RunState.hope` |
| メタ恒久バフ | ラン跨ぎの永続強化（トークンで段階解放）。 | `MetaBuffKind` |
| メタ挑戦デバフ | Lv1〜10 の挑戦モード負荷（独立トグル）。 | `MetaDebuffLevel` |
| 固有恒久デバフ | メタ由来の7つの大罪デバフ。 | `PermanentDebuffIds` |
| ラストスタンド | ラン中1回のみの救済発動。 | `RunState.lastStandActive`, `LastStand.cs` |
| 解脱 | 覚者の最終形態（妙覚）のサドンデス勝利による特殊エンディング。 | `gedatsuVictory` |

## 表記規約（効果説明の正規化・正本=items.json）

アイテム/パッシブの効果説明は以下の正本表記で統一する（揺れを作らない）。
データの正本は [Assets/Data/InventorySystem/items.json](../../Assets/Data/InventorySystem/items.json)。

| 概念 | 正本表記 | 廃止/誤用（使わない） |
|---|---|---|
| ダイス出目の総和への加算 | **ダイス合計+N** | ダイス合計値+N／裸の「ダイス+N」 |
| 振るダイスの本数 | **ダイス個数**（playerDiceCount） | （合計と混同しない） |
| 会心分子(/9)への加算 | **会心ダイス合計値+N**（=会心率+N/9・criticalBonus+N） | 会心率+N/9／会心ダイス+N |
| 与ダメージ（与える側） | **与ダメージ**（+N / 倍率 / の% / 最低保証 / 計算前） | 与ダメ（略さない） |
| 被ダメージ（受ける側） | **被ダメージ**（-N / の% / 記録 / 0） | 被ダメ（略さない） |
| 軽減を無視するダメージ | **軽減不可** | 軽減不能 |
| 会心の確定 | **会心確定** | 必ず会心ヒット |

注: 「会心ダイス合計値」は会心分子（X/9 の X）への加算を指す（出目の合計ではない）。
「最終与ダメージ」は全補正適用後の値を指す意図的な区別であり、`与ダメージ` とは別扱いで残す。

### スキル表示名（internalName ごとに1名で統一）

同一 internalName は武器・パッシブアイテムで同じ skillName を使う（以前は武器/パッシブで分裂していた）。

| internalName | 正本 skillName | 効果 | 旧・別名（廃止） |
|---|---|---|---|
| Might I–IV | **筋力** | ダイス合計+N | 剛力 |
| Fortitude I–IV | **頑強** | 被ダメージ-N（常時） | 堅忍 |
| Insight I–IV | **心眼** | 会心ダイス合計値+N | 慧眼 |
| Steadfast | **堅忍** | ロール敗北時の被ダメージ-3（条件付き・Fortitude とは別効果） | — |

注: `堅忍` は Steadfast 専用（敗北時の条件付き軽減）。Fortitude の常時軽減は `頑強` で、両者は別効果なので名前を分ける。

## See Also

- [overview](overview.md)
- 正規化対象データ: [items.json](../../Assets/Data/InventorySystem/items.json)
