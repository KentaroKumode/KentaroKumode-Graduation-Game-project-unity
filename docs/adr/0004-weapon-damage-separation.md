---
title: 武器ダメージ分離（案A'・ダイス=勝負／武器=威力／差は小ボーナス）の採用
status: accepted
date: 2026-06-06
opened: 2026-06-05
supersedes: []
superseded_by: null
related_specs: [combat, items, inventory]
related_adrs: []
---

# ADR-0004 — 武器ダメージ分離（案A'）の採用

## Status

Accepted（モデル確定・実装済み。数値は BOT オートランで較正）

## Context

旧式：勝利 base ＝ `|diceDifference|`（敗北 base ＝ `max(|diceDifference|, enemyThreat)`）。
ダイス合計が「勝敗」と「火力」を兼務し、高分散ダイスが“勝ちやすく かつ 大ダメージ”の二重取り。
安定ダイス・カスタム面・武器個性が薄い（正本 [combat](../specs/combat.md)）。

検討の過程で次が判明（重要）:
- 「base に flat を足すだけ」は **追撃(Pursuit)等のパッシブと数学的に等価**（同じ ×会心×outgoing チェーン内）。
  ＝attackPower を「floor の足し算」にしても価値はほぼ無い。
- 「差→会心」案は **大差勝利が確定会心化** し、会心率という武器差別化軸を逆に潰す（却下）。
- 面の期待値は装備ダイスが供給＝武器では動かせない。武器側のロールレバーは実質「個数」だけで、
  個数は勝率(平均)と分散を同時に動かす＝そのまま「ロール力差」になる。

## Decision

**ダイス＝勝負（命中）／武器＝威力／会心＝独立** に役割分離する（案A'）。

- **勝利ダメージ base ＝ `attackPower + floor(|diceDifference| / 3)`**（`WeaponDiffPerBonus=3`・差3ごとに+1の小ボーナス）。
  差は**勝敗を主に決め**、ダメージ寄与は小。**会心には一切干渉しない**（critRate は独立のまま）。
- **attackPower**：武器の素火力（items.json `attackPower`・新フィールド）。武器性能差の主軸。
- **敗北側は不変**：`max(|diff|, enemyThreat)`（enemyThreat が敵側 floor＝既に対称構造）。scratch も不変。
- **ダイス個数は同Tier一律**（剣/斧/ダガー/盾の同Tierは個数同じ＝**勝率パリティ**）。個数は Tier 進行レバーに留め、
  「ロールの強さ」は装備ダイス＋Tierで決まる共有ベースライン＝武器選択では変えない。
- 武器の性能差は **attackPower（威力）＋critRate＋パッシブ** のみ。

### attackPower 初期値（実装済み・暫定／BOT較正前提）

| 武器 | B | S | G | L |
|---|---|---|---|---|
| 盾 | 1 | 2 | 3 | 4 |
| 剣 | 2 | 3 | 4 | 5 |
| 斧 | 3 | 4 | 6 | 8 |
| ダガー | 1 | 2 | 3 | 4 |
| 呪い系 | 2 | 3 | 4 | 5 |
| 竜閃=3（画竜点睛が勝利ダメ上書き）／炎の杖=3／デフォルト2d6=2 |

## Consequences

### Positive

- ダイスは「勝つ力」、武器は「効く力」、会心は独立倍率、と三者分離。差の二重取りが floor(/3) まで縮小。
- **パッシブ書き換えゼロ**（断罪=差×4%・虚空=差≤3・追撃・重畳/背水/利刃% 等は新baseの上に乗るだけ）。
- 大差勝利でも会心は確定化しない＝会心ビルド（ダガー/斧/心眼）の固有性を保つ。
- 同Tierロール力パリティ＝「どの武器か」が勝率を動かさない。#1振り直し（勝ちにいく動機）と整合。

### Negative / Neutral

- attackPower は数学的には flat パッシブと等価だが、**武器の第一級ステータスとして明示**する設計を採用（ユーザー決定）。
- オーバーロールの旨味は floor(差/3) の小ボーナスのみ（大差の爽快感は弱め）。
- attackPower 各値は暫定 → BOT オートランで較正（ティア再評価）。loss 側の対称化は将来オプション。

### 実装（完了）

- データ：`ItemDataJson.attackPower` / `ItemDataV2.attackPower` 追加、`ItemDatabase.ConvertToCompleteItemData` でマッピング、items.json 22武器へ付与。
- 戦闘：`CombatManager` に `playerAttackPower`（StartCombatInternal で装備武器から解決）＋ `WeaponDiffPerBonus=3`。勝利分岐で `winBase = attackPower + diceDiff/3` を `ProcessDamage` へ。loss/scratch 不変。
- 正本 `DAMAGE_CALC_REFERENCE.md` §1 ② の base 定義を更新。

## Alternatives Considered

| Option | Why rejected |
|--------|--------------|
| 案A（差→会心へ全面ルート） | 大差勝利が確定会心化し会心率軸を潰す |
| 案C（base=attackPower+k×差, k=0.5） | k が大きく差の二重取りが残る／flat部はパッシブ等価で価値薄 |
| attackPower を入れず武器に追撃パッシブ | 差の重みを下げられない＝二重取りが残る（分離にならない） |
| 現状維持（base=|差|） | ダイスと武器の役割兼務・個性希薄 |
