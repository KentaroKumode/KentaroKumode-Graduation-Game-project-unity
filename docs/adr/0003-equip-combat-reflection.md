---
title: 装備→戦闘反映は既存パッシブ機構と同経路（案B）で実装済み
status: accepted
date: 2026-06-05
opened: 2026-06-03
supersedes: []
superseded_by: null
related_specs: [inventory, combat]
related_adrs: []
---

# ADR-0003 — 装備→戦闘反映は既存パッシブ機構と同経路（案B）で実装済み

## Status

Accepted

> 元 open-question `equip-combat-reflection`（案A=戦闘ステータス合算 / 案B=既存パッシブ機構と同経路）
> を昇格。コード調査の結果、**既に案B方式で反映実装済み**であることを確認し、決定として記録する。

## Context

「インベントリで装備したアイテムが戦闘に反映されない」という保留（2026-06-03 起票）があったが、
その後の実装でコード上は反映されている。起票時点との鮮度差を解消し、決定を確定する。

## Decision

**案B（既存パッシブ機構と同経路で反映）を採用済みとして確定。** 専用の戦闘ステータス合算層（案A）は作らない。

実反映経路（正本＝実コード）:

| 反映対象 | 経路 |
|---|---|
| 武器 → ダイス個数・会心率 | `GameManager.GatherPlayerCombatStats`（`ItemEquipHandler.GetCurrentEquipment` 優先・`RunState.equippedWeaponId` フォールバック） |
| ダイス → 面・最大出目 | 同上（`equippedDiceId` / `diceFaces`） |
| 防具(Armor)・装備/所持パッシブ | `RunPassiveSync.RefreshFromRun`（equip＋`ownedPassiveItems` を結合 → `PassiveSkillManager`） |
| weaponPlus / 業物Lv(limitBreakStage) | `CombatManager.ApplyWinDamageModifiers`（`outgoing += 0.2×lbStage`）／`MasterworkNotes`／`RunPassiveSync` |
| 刻印(passiveSigils) | `PassiveSkillManager` / `CombatManager` で参照 |

「防具」は独立スロットではなく**防御系パッシブ（頑強等）として既存機構に内包**される（＝案Bの帰結）。

## Consequences

- 仕組みが一本化され、装備＝パッシブ写像で拡張容易（案Bのメリット通り）。
- inventory / combat の装備節を「反映済み」に更新。phase-1 の F2（1-4）を完了扱いに。
- 新規コードは不要。今後の戦闘リワーク（ダメージ分離・希望システム等）はこの一本化経路の上に乗せる。

## Alternatives Considered

| Option | Why rejected |
|--------|--------------|
| 案A（戦闘開始時にステータスを別途合算） | 既存パッシブ機構と二系統化。すでに案B経路で動作しており不要 |
