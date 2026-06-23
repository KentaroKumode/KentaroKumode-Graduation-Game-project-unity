---
title: ボス固有ダイス（署名ダイス・面構成で性格づけ＋面の各値を学習）の採用
status: accepted
date: 2026-06-06
opened: 2026-06-06
supersedes: []
superseded_by: null
related_specs: [boss, combat]
related_adrs: [4, 5]
---

# ADR-0007 — ボス固有ダイス（署名ダイス）

## Status

Accepted（実装済み。BOT較正前提）

## Context

ボスのロール強化は「プレイヤー平均出目 + diceOffset」を期待値とし、`BestDiceConfig` で**均一ダイス**へ落とす方式
だった。均一ダイスは構成上 **E25 (5d9) が上限**。一方プレイヤーは高平均ダイス（完全 [4-9] 平均6.5）を
4D で回すと基礎 E26、持続フラット加算で E33、1ターン目スパイクで E42 に達する（[ADR-0004] 武器ダメージ分離後も
ロール勝負は合計差で決まる）。

結果、boss_layer5 等は期待値が E25 に飽和し、チューナーが動かせる唯一のレバーとして **HP を水増し**し続けた
（1293→1716 等）。ロールで追いつけないのに HP だけ膨張する悪循環で、長期殴り合い化し設計意図から乖離していた。

「単に +N するのは芸がない」（ユーザー）。フラット加算（真我）や HP 水増しではなく、**ボスごとに固有の面構成ダイス**
を与え、面の形（分散・尖り）で個性を出しつつ E25 天井を撤廃する。

## Decision

対象4ボスに**固有面ダイス（署名ダイス）**を付与する。5個振り・面6つ・各値∈**[1,9]**（11等はダイスの
グラフィック表現不可のため上限9）。

| ボス | 署名ダイス | 面 | 1個平均 | ×5 期待値 | 形＝性格 |
|---|---|---|---|---|---|
| 5層 業火の審判官 | 裁火のダイス | `[2,3,3,6,7,8]` | 4.83 | E24.2 | 二極（赦免/断罪）・荒い |
| 5層裏 シュヴァリエ | 決闘のダイス | `[4,5,5,6,6,7]` | 5.50 | E27.5 | 低分散・規律 |
| 6層 灰燼の王 | 薄火のダイス | `[4,6,7,7,8,8]` | 6.67 | E33.3 | 上寄り（+灰塵の威圧+3で実効E36） |
| 7層 覚者 | 往生呪のダイス | `[7,8,8,9,9,9]` | 8.50 | E42.5 | 高い床・逃れられない |

- 期待値は[BossTuning.cs](../../Assets/Scripts/AutoTest/BossTuning.cs) の層別**目標プレイヤーロール勝率**
  （5層0.80／5裏0.60／6層0.40）に沿う。プレイヤー実効（4D完全 E26〜33、1T目E42）に対し正規近似で算出。
- passive 加算（真我／灰塵の威圧+3／星火燎原）は**固有ダイスの上に従来どおり乗る**（二重計上なし）。

### 学習（L3）

**面の各値そのもの**を実数値で増減する（隠し倍率なし＝[ADR は本プロジェクト規約]に準拠）。
`BossBalanceTuner.AdjustSignatureDice`：

- err>0（難しすぎ）→ボス弱体＝**高い面を1下げて天井を削る**。
- err<0（易しすぎ）→ボス強化＝**低い面を1上げて床を持ち上げる**。
- 各値 [1,9] にクランプ・昇順維持。1面±1 = EV 変化 `5/6≈0.83`。全面飽和（全9/全1）時のみ HP に回す。
- 既定面 `BossTuning._signatureDefaults`、学習値 `boss_tuning.json` の `Knob.diceFaces`。

5層/5裏/6層は面を学習。**7層覚者**は往生呪ダイスを**ベースロール**に使いつつ、上限超えの引き締めは従来の
「真我」（素ロール固定加算・uncapped）が担う＝多段戦＋ロール勝率制御の既存設計を壊さない。

## Consequences

### Positive

- E25 天井を撤廃。プレイヤーの基礎ロール（E26〜42）に固有ダイスで追従でき、HP 水増しの軍拡が止まる。
- 面の形でボスに個性（二極／規律／上寄り／高床）。均一ダイスより読み応え・テレグラフ性が増す。
- 実数値直接調整の規約を保ったまま、学習軸が「面の各値」へ自然拡張。

### Negative / Neutral

- 均一前提のコード（`EstimateWinProbability` の敵モーメント等）は `diceMaxValue=最大面` で近似（やや粗い）。
  必要なら将来、敵側にも面配列を渡して厳密化。
- 面学習は1バッチ1面程度で収束が緩やか。初期面は較正の出発点。
- 署名ダイス上限は 5d9 相当 E45。god-build に対しては 7層の真我（uncapped）が引き続き受け皿。

### 実装（完了）

- `CombatSystem/EnemyData.cs`：`int[] diceFaces` 追加・`RollDice`/`DiceNotation`/`Clone` 対応。
- `CombatSystem/CombatManager.cs`：敵ロールが `currentEnemy.diceFaces` から抽選（弱ロールは `WeakRollFaces`）。
- `CombatSystem/EnemyStance.cs`：`WeakRollFaces`（各面×0.65・最低1）。
- `AutoTest/BossTuning.cs`：`Knob.diceFaces`、`_signatureDefaults`、`IsSignatureDiceBoss`/`SignatureFaces`/
  `ClampSignature`/`SignatureExpected`、定数 `SignatureDiceCount=5`/`SignatureFaceMin=1`/`Max=9`、`Apply` で注入。
- `AutoTest/BossBalanceTuner.cs`：`AdjustSignatureDice`（面の各値を学習）。
- `AutoTest/AutoRunner.cs`：周回サマリに固有面 E の差分（`SigE`）。

## Alternatives Considered

| Option | Why rejected |
|--------|--------------|
| 真我（フラット+N）を全ボスへ展開 | 「単に+は芸がない」。形が出ず、均一+定数のまま |
| DiceEMax(25) 引き上げ | 対症療法。均一のまま個性なし、加算が伸びれば再飽和 |
| プレイヤー基礎ロールに上限 | アイテム価値へ波及・大規模再較正。今回はボス側で解決 |
| 1面の上限を11+ | ダイスのグラフィック表現不可（ユーザー制約）→ 上限9 |
