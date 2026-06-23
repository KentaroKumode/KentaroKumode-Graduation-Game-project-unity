---
title: Λ層（時間の狭間）
description: 5層ボス撃破後に強制突入する周回エリア。次元の乱れと7種の恒久デバフ
status: provisional
related: [overview, combat, map-run]
---

# Λ層（時間の狭間）

## Purpose

Λ層は **5層ボス撃破かつ〈決意〉以上で強制突入**する周回エリア。周回するほど恒久デバフが
蓄積し、見返り（エリート報酬等）と引き換えに 6F 以降へ持ち込むリスクを負う。
正本: [GameLoop/Lambda/LambdaDebuffs.cs](../../Assets/Scripts/GameLoop/Lambda/LambdaDebuffs.cs)、
状態は [RunState.cs](../../Assets/Scripts/GameLoop/RunState.cs)、進行は `GameManager`。**ビルド済・稼働中。**

## Definition

### 突入・滞在・離脱

- 突入：5層ボス撃破後、`convictionStage` ≥ 3（〈決意〉）で `inLambda = true`。
- 滞在中 `currentFloor` は 5 のまま。
- 構造：環状線3マス（S→A→B→S）＋中央マス（離脱、Sスポークからのみ到達可）。
  - `LambdaRing`：踏む度にエリート/固有イベントを抽選（**再訪で再発火**）。
  - `LambdaExit`：中央マス。踏むと 6F 前哨基地へ着地（離脱）。

### 次元の乱れとデバフ付与

- `dimensionalDisturbance`：マス移動毎に +1。**3 毎に** `LambdaDebuffEffects.GrantRandom` で
  ランダムΛデバフを1つ付与。既存なら段階 +1（最大3）。全7種が lv3 なら付与なし。

### 7種の恒久Λデバフ（段階 lv1/lv2/lv3）

| ID | 効果（lv1 / lv2 / lv3） |
|---|---|
| 重い足取り | 1ターン目のダイス合計 −2 / −4 / −6 |
| 微妙な手応え | 敵への最終ダメージ −5% / −10% / −15% |
| 苛立つ強敵 | 経過 5 / 4 / 3 ターン毎に敵ダイス合計 +1 |
| 注意散漫 | 会心の出目上限（分子/9）が 8 / 6 / 4 に減少 |
| 慈悲の処刑 | 被弾後 HP が最大の 5% / 10% / 15% 以下なら即死 |
| 神経錯乱 | 戦闘開始から 3 / 5 / 7 ターン目開始まで消費アイテム使用不可 |
| 迫りくる死 | lv1・lv2 は無効、**lv3 で戦闘開始時 HP を 1 に** |

効果量は `LambdaDebuffEffects` の各 getter が `run.lambdaDebuffs` の段階を読むのみ（副作用なし）。

### 検証手段（別建て）

- Tools > AutoRun > Λファーム量スイープ。固定目標マス数でデバフ蓄積量を計測し、出力を
  `lambda_farm_sweep.txt` に書く。難易度再調整に用いる（[lambda-recalibration](../open-questions/lambda-recalibration.md)）。

## Constraints

- MUST：Λデバフ段階は 1〜3 で clamp。同IDの再付与は段階 +1（上限3）。
- MUST：`LambdaRing` は再訪で再発火、`LambdaExit` は Sスポークからのみ到達可能とする。

## Open Questions

- [lambda-recalibration](../open-questions/lambda-recalibration.md)

決定済（実装待ち）：[ADR-0002 希望システム](../adr/0002-hope-system.md) — 希望「絶望」帯の会心デバフが
Λ「注意散漫」（会心分子上限）と会心域で競合するため、フック重複に注意。

## See Also

- Specs: [combat](combat.md), [map-run](map-run.md), [overview](overview.md)
- ADR: [0002-hope-system](../adr/0002-hope-system.md)
- 正本: [LambdaDebuffs.cs](../../Assets/Scripts/GameLoop/Lambda/LambdaDebuffs.cs)
