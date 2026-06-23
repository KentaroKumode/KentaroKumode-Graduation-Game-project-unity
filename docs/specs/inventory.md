---
title: インベントリ・パッシブ・武器強化
description: グリッドインベントリ、パッシブアイテム／スキル／刻印、武器装備と限界突破
status: provisional
related: [combat, items, shop-economy]
---

# インベントリ・パッシブ・武器強化

## Purpose

グリッド状インベントリにアイテムを配置し、パッシブ／装備を通じて戦闘に影響させる。
正本: [InventorySystem/](../../Assets/Scripts/InventorySystem/)、ラン状態は [RunState.cs](../../Assets/Scripts/GameLoop/RunState.cs)。

## Definition

### グリッド（`InventorySystem/Grid/`, `InventoryConstants`）

- `GRID_WIDTH = 5`。`inventoryUnlockedRows` 初期 4（`INITIAL_UNLOCKED_ROWS`）→ ショップ拡張で +1 ずつ、
  最大 8。セル容量 = `inventoryUnlockedRows × 5`。

### パッシブ

- **パッシブアイテム**（`PassiveItems/`）：`ownedPassiveItems`（重複可）。名前付き固有あり。
  追加は `Helpers/PassiveAddHelper` 経由。
- **パッシブスキル**（`PassiveSkills/`）：戦闘パイプラインに介入する効果群
  （プレイヤー `AllPassiveSkillEffects.cs` / 敵 `EnemyPassiveSkillEffects.cs`、[combat](combat.md)）。
- **刻印（Sigil）**（`Sigils/`）：`passiveSigils`（`ownedPassiveItems` と並列配列）。取得時に1回
  ロールされ、ラン中不変の付加特性。

### 武器・装備・強化

- 装備：`equippedWeaponId`（空=デフォルト 2d6）／ `equippedDiceId`（空=武器ダイス）。取得時
  `Loadout.TryAutoEquip` で更新。
- 武器の "+" 段階 `weaponPlus`（0/1）：休憩強化で 0→1、1の状態で次Tier武器へ置換し0に戻る。
- 限界突破（業物）`limitBreakStage`（0-10）：T4+ 到達後に休憩で上昇。1lv ごとダイス合計+2・与ダメ+2（累積）。

### 装備の戦闘反映（実装済み・案B）

- 装備は**既存パッシブ機構と同経路で戦闘に反映済み**（[ADR-0003](../adr/0003-equip-combat-reflection.md)）。
- 武器→ダイス個数・会心率／ダイス→面・最大出目は `GameManager.GatherPlayerCombatStats`、
  防具・装備/所持パッシブは `RunPassiveSync.RefreshFromRun` → `PassiveSkillManager`、
  weaponPlus/業物Lv・刻印は `CombatManager`/`PassiveSkillManager` が参照。
- 「防具」は独立スロットでなく防御系パッシブ（頑強等）として内包。

### その他

- `ItemHoldingArea`（一時保持）、`ItemShredder`（シュレッダー・予備）、`Save/`（永続化）、
  カメラフィルタ（`CameraFilter.cs` / Shaders、Bloom+Vignette+Grain+Grading）。

## Constraints

- MUST：`passiveSigils` の要素数は `ownedPassiveItems` と一致させる（並列配列）。
- MUST：`inventoryUnlockedRows` は 4〜8 で clamp。
- MUST：ピクセル要素の状態表現に `localScale` 倍率を使わない（[non-goals](non-goals.md)）。

## Open Questions

- [equip-combat-reflection](../open-questions/equip-combat-reflection.md)

## See Also

- Specs: [combat](combat.md), [items](items.md), [shop-economy](shop-economy.md)
