---
title: 敵のロール力↔ダメージ二択スタンス（毎ターン・事前テレグラフ）の採用
status: accepted
date: 2026-06-06
opened: 2026-06-06
supersedes: []
superseded_by: null
related_specs: [combat, boss]
related_adrs: [4]
---

# ADR-0005 — 敵のロール力↔ダメージ二択スタンス（事前テレグラフ）

## Status

Accepted（実装済み。数値は BOT オートランで較正）

## Context

ADR-0004 でプレイヤー側は「ダイス＝勝負／武器＝威力」へ分離。敵にも分離の発想を持ち込みつつ、
**読み合い（カウンタープレイ）**を作りたい。元 open-question `enemy-roll-damage-stance` を昇格。

## Decision

敵は**毎ターン頭にランダムで二択スタンスを提示（テレグラフ）**する（ロール前に分かる）:

- **強ロール・低火力**（HighRollLowDmg）：**今のダイス＝基準**でロール、被ダメ ×0.5。勝ちやすいが痛くない。
- **弱ロール・高火力**（LowRollHighDmg）：**期待値が約0.65倍になるよう面を縮めて実際に振る**、被ダメ ×1.6。勝ちにくいが大きい。

アンチ相関で、ロール力とダメージ出力を独立に振る。プレイヤーは #1 ダイス振り直し（希望消費）と
組み合わせ「高火力提示の時だけ確実に勝ちにいく／低火力提示なら希望を温存」と読める。

- **ロールの弱体化は“実ロールを弱くする”方式**：強ロール=今のダイスを基準(ceiling)、弱ロール=最大出目を `WeakRollMax`（期待値≈`WeakRollRatio`=0.65倍）に縮めて RollDice。**結果の事後倍率（×0.65 等）は不可**（ユーザー指定）。固定±デルタ方式は小型敵が0に潰れるため廃止。
- **粒度**：毎ターン抽選・ターン頭テレグラフ（`CombatManager.ExecuteTurn` の `BeginTurn` 直後）。
- **対象**：通常・エリート・**ボスも含む**（ボスの自前ダイス操作と重畳＝数値は BOT 較正前提。7層は L3 調整対象外だが本機構は適用）。
- **強制ロール中は不適用**：妙覚自由攻撃／灰燼サドンデス／妙覚サドンデス。
- **テレグラフUIは後付け**：`EnemyStance.OnTelegraph` イベント＋ログ（層タイトル/振り直しと同じ暫定自動パターン）。

### 実装（完了）

- `CombatSystem/EnemyStance.cs`：`Kind{None,HighRollLowDmg,LowRollHighDmg}`、調整値（WeakRollRatio=0.65／LowDamageMult=0.5／HighDamageMult=1.6・暫定）、`WeakRollMax(baseMax)`、`Apply(ctx)`、`OnTelegraph`。
- `CombatContext`：`enemyStanceDamageMult`(既定1・毎T1) ／ `enemyStanceKind`(0/1強/2弱)。BeginNewTurn でリセット。（旧 `enemyStanceDiceDelta` は廃止）
- ロール力：`CombatManager` の敵 `RollDice` 時に、弱ロール(kind=2)なら最大出目を `WeakRollMax(diceMaxValue)` に縮めて振る（強制ロール中は素のまま）。
- ダメージ：`CombatManager.ApplyLossDamageModifiers` で `totalDmg ×= enemyStanceDamageMult`（メタ倍率の直後・以降の軽減はスタンス後に効く）。
- BOT 読み合い：`MaybeRerollPlayerDice` は低火力提示なら振り直さず希望温存（弱ロールは敵ダイスに既に反映済み＝そのまま比較）。

## Consequences

### Positive

- ロール前テレグラフ＋#1振り直しで**読んで対応**する戦闘判断が生まれる（受動戦闘からの脱却の一歩）。
- ロール力とダメージを独立に振れる＝敵の「速い/重い」の質感。

### Negative / Neutral

- ボス含むため、ボスの自前ダイス操作・L3自動チューナー（勝率調整）と重畳。**BOT 再較正が必須**。
- 二択のEV中立性・各倍率は暫定（BOT較正）。プレイヤーの能動的な読み合いはテレグラフUI実装後に本領（現状BOTは簡易織り込み）。

## Alternatives Considered

| Option | Why rejected |
|--------|--------------|
| 毎戦闘固定 | 読み合いが浅い |
| 確率発生（普段は素ロール） | 提示の常時性が薄れ、振り直しとの噛み合いが弱い |
| ボス除外 | ボス戦にも読み合いが欲しいとの判断（数値は較正で吸収） |
| 敵 attackPower での完全な loss 側分離 | スコープ過大。スタンス倍率で「ダメージ出力」軸は表現でき、まずはこちらを採用（loss 側完全分離は将来オプション） |
