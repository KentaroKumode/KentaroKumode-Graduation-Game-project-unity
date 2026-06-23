---
title: 戦闘システム（ダイス＋パッシブ）
description: ダイスロール勝敗判定と4段階ダメージパイプライン。計算の正本は DAMAGE_CALC_REFERENCE.md
status: provisional
related: [items, inventory, boss, lambda-layer]
---

# 戦闘システム（ダイス＋パッシブ）

## Purpose

戦闘はプレイヤーと敵がダイスを振り、合計の大小で勝敗を決め、差分等からダメージを算出する
ターン制。多数のパッシブスキルが各フェーズに介入する。**計算式の唯一の正本は実コードと
[DAMAGE_CALC_REFERENCE.md](../../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)**。
本 spec は計算式を再記述せず、構造とコード参照のみを示す（憶測禁止の規約）。

## Definition

### 正本ファイル

| 対象 | ファイル |
|---|---|
| ダメージ計算の正本（4段階パイプライン・適用順・ctx 用語集） | [DAMAGE_CALC_REFERENCE.md](../../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md) |
| 戦闘オーケストレーション | [CombatManager.cs](../../Assets/Scripts/CombatSystem/CombatManager.cs) |
| 敵データ／DB | [EnemyData.cs](../../Assets/Scripts/CombatSystem/EnemyData.cs), `EnemyDatabase.cs` |
| 戦闘修飾 | `BattleModifierManager.cs` |
| LED 演出 | `CombatSystem/DiceLED/` |
| パッシブ効果（プレイヤー／敵） | `InventorySystem/PassiveSkills/Effects/AllPassiveSkillEffects.cs`, `EnemyPassiveSkillEffects.cs` |

### パイプラインの骨子（詳細は正本参照）

0. **ダイス振り直しフェーズ（#1）** — 初回ロール後・合計算出前に、希望（`HopeSystem.RerollCost`）を払って
   期待値割れの出目を毎ターン最大1回振り直す（`CombatManager.MaybeRerollPlayerDice`）。低希望だと払えず振り直せない。
   UI 未実装のため当面は自動ポリシー。詳細は正本 §1 ⓪。
1. **ダイス合計フェーズ** — 各自の出目を合算し、各種バフ／`enemyDiceTotalBonus` 等を加味。
   `diceDifference = playerDiceTotal − enemyDiceTotal` は読み取り専用（視点スワップで符号自動反転）。
2. **勝敗判定** — `sign(diceDifference + consDiceRoll)`。引き分けは勝敗が付くまで再ロール。
3. **ProcessDamage** — base（勝利=|差|／敗北=max(|差|, enemyThreat)）→ 与ダメ/被ダメ改変 →
   追撃 → 会心（分子/9、既定倍率 2.0）。
4. **確定** — 勝利は `ApplyWinDamageModifiers`、敗北は `ApplyLossDamageModifiers`（適用順は厳守）。

### 敵パッシブの視点スワップ（重要前提）

敵スキルは `FireEnemyTrigger` で視点を入れ替えて実行される。コード上 `ctx.playerXXX` は
**敵自身**、`ctx.enemyXXX` は **実プレイヤー**を指す。スワップされない共有フィールド
（`enemyDiceTotalBonus` / `enemyDamageReductionPct` 等）は敵が書くと実値に直接効く。
（一覧は正本 §0 を参照）

### 他システムからの戦闘介入

- **装備（武器/ダイス/防具/パッシブ/刻印）**：既存パッシブ機構と同経路で戦闘に反映済み
  （`GatherPlayerCombatStats` ＋ `RunPassiveSync.RefreshFromRun`、`weaponPlus`/`limitBreakStage` 含む）。
  詳細・経路表は [ADR-0003](../adr/0003-equip-combat-reflection.md)。
- **消費アイテム**：戦闘開始時に `RunState.pendingCons*` を `CombatContext` へコピー（1戦のみ）。
- **Λデバフ**：会心上限・初手ダイス・即死閾値等に作用（[lambda-layer](lambda-layer.md)）。

## Constraints

- MUST：計算式・パッシブ効果を記述／変更する前に正本（リファレンス or 実コード）を確認する。
- MUST：コードを変更したら `DAMAGE_CALC_REFERENCE.md` も同時更新する。
- MUST NOT：本 spec に計算式を複製しない（pointer over copy）。

## Open Questions

（装備→戦闘反映は解決済 → [ADR-0003](../adr/0003-equip-combat-reflection.md)）

実装済み：[ADR-0002 希望システム](../adr/0002-hope-system.md) — 戦闘フックは配線済み（`HopeSystem` ＋
`CombatManager`）。苦悩=会心倍率−0.5（`ctx.criticalMultiplier +=`）／迷妄=戦闘開始時パッシブ
1–3個無効（`psm.DisableRandomPlayerSkills`）／疲労=攻撃15%で0ダメ。HP収支損・移動損も
`GameManager`/`HopeSystem` に接続済み。※Λ「注意散漫」は会心**分子**上限、希望は会心**倍率**で別軸＝非競合。
数値チューニングは BOT オートラン（phase-3 タスク3-8）で継続。

## See Also

- Specs: [items](items.md), [inventory](inventory.md), [boss](boss.md), [lambda-layer](lambda-layer.md)
- ADR: [0002-hope-system](../adr/0002-hope-system.md)
- 正本: [DAMAGE_CALC_REFERENCE.md](../../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)
