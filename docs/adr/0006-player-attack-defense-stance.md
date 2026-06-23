---
title: プレイヤー攻撃/防御スタンス（毎ターン・ロール前選択）の採用
status: accepted
date: 2026-06-06
opened: 2026-06-06
supersedes: []
superseded_by: null
related_specs: [combat]
related_adrs: [4, 5]
---

# ADR-0006 — プレイヤー攻撃/防御スタンス

## Status

Accepted（実装済み。BOT較正前提）

## Context

戦闘に能動的な防御行動が無かった（防御は完全に受動・ビルド由来）。敵スタンス(ADR-0005)・振り直し(#1)と
合わせ、**ロール前に攻撃/防御を選ぶ**能動判断を足す。

## Decision

ターン頭・**ロール前**にプレイヤーが二択を選ぶ（出目を見てからは不可＝壊れるため厳守）:

- **攻撃優先**：現状の計算式そのまま。
- **防御優先**：
  - 与ダメージ **-90%**（×0.1）。主ダメージのみ。**反撃/業火/血令 等の固定ダメ(fixedDamageToEnemy)は対象外**。
  - 受ける最終ダメージ **-50%**（×0.5）。**全軽減/シールドの後、最後に適用**（ユーザー指定）。

＝防御は「勝っても与ダメほぼ0／負けても半減」＝**負け前提の耐えターン**。盾+反撃（敗北時固定反射はフル）の亀ビルドが成立。

- 順序：スタンス選択（敵スタンス提示後）→ ロール →（振り直し#1）→ 解決。振り直し後に勝敗が変わってもスタンスは固定。
- **強制ロール中は不適用**（妙覚自由攻撃/各サドンデス＝攻撃扱い）。
- **BOT判定＝学習方式**（ADR-0006 改）：ロール前の**推定勝率**（CombatManager が正規近似で算出。Might等のロール後フラットは無視＝学習が吸収）と**学習閾値**で決める。
  実効閾値 = `stanceDefendWinProb` + `stanceDefendHpBias`×(1−HP割合)。推定勝率がこれ未満なら防御。
  2軸は `PolicyParameters`（L2学習）→ `PolicyExplorer.Axes` で composite勝率最適化。AutoRunner が `PlayerStance.DefendWinProb/HpBiasProvider` 経由で `Current` を結線（CombatSystemはAutoTest非依存）。
  ＝「勝率2割→防御／8割→攻撃／中間や瀕死での無理はBOTが学習」。防御中は振り直し(#1)もしない（×0.1の勝ちに価値が無い）。UI実装後は人間の選択へ差し替え。

## Consequences

### Positive

- 受動戦闘に能動防御を追加。敵スタンス+振り直しと三すくみの読み合い層。
- 役割分担：**振り直し=勝ちを取りに行く／防御=どうせ負けるから被害最小化**（補完的・冗長でない）。

### Negative / Neutral

- 防御スタンスと敵スタンスは同じ勝敗軸に乗り部分的にアンチ相関（敵高ダメ=低ロール=自分が勝ちやすい）。
  防御の本当の出番は「自分の勝率が低い／HP瀕死で中ダメも危険」な局面。人間の読み合いはテレグラフUI後に本領。
- 与ダメ-90%/受け-50% は暫定。BOT較正。

### 実装（完了）

- `CombatSystem/PlayerStance.cs`：`Kind{Attack,Defense}`、`DefenseWinDamageMult=0.1`/`DefenseLossDamageMult=0.5`、`Choose(ctx,hp,maxHp,estWinProb)`、`DefendWinProb/HpBiasProvider`、`OnChoose`。
- `CombatManager.EstimateWinProbability`/`DiceMoments`：両者ダイスの平均・分散から正規近似＋ロジスティックCDFで P(勝) を概算（敵弱ロールの面縮小を反映）。
- `AutoTest/PolicyParameters`：`stanceDefendWinProb`(0.35)/`stanceDefendHpBias`(0.30)＋clamp/clone/summary。`PolicyExplorer.Axes` に2軸（step0.05）。`AutoRunner` が Provider を結線。
- `CombatContext.playerStanceDefense`（毎Tリセット）。
- `CombatManager`：ターン頭で `PlayerStance.Choose`（敵スタンス提示後・強制ロール中除外）。与ダメ=`ApplyWinDamageModifiers`末尾×0.1、受けダメ=`ApplyLossDamageModifiers`**末尾**×0.5。`MaybeRerollPlayerDice` は防御中スキップ。

## Alternatives Considered

| Option | Why rejected |
|--------|--------------|
| 与ダメ-50%（柔らかいトレード） | ユーザーは「耐えターン」(-90%)を採用 |
| ロール後にスタンス選択 | 勝敗を見てから選べて壊れる（タダ半減） |
| 受けダメ-50%を軽減前に適用 | ユーザー指定で「最後に受けるダメージから-50%」 |
