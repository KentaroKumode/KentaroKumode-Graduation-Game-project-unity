---
title: アイテム・パッシブカタログ
description: アイテムの正本カタログ（カテゴリ・レアリティ・固有アイテム）への索引
status: provisional
related: [inventory, shop-economy, combat]
---

# アイテム・パッシブカタログ

## Purpose

全アイテム（パッシブ／消費／武器／ダイス／素材／フラグ／固有）の一次情報への索引。
**正本: [items.json](../../Assets/Data/InventorySystem/items.json)**。
実装は [InventorySystem/Items/](../../Assets/Scripts/InventorySystem/Items/), `InventorySystem/Data/`。

## Definition

### カテゴリ

- パッシブ（恒常効果。名前付き固有を含む）／ 消費（戦闘内外で使用）／ 武器・ダイス（装備）／
  武器強化素材／ フラグアイテム（イベント分岐用）。

### レアリティ（[EventRarity](../../Assets/Scripts/MapSystem/TileType.cs) ／ ショップ Tier）

- Bronze / Silver / Gold / Legendary。ショップ武器枠は Legendary を除外、全カテゴリで Mythic を除外
  （[shop-economy](shop-economy.md)）。

### 固有パッシブアイテム（例）

- 既存5：ちいさな灯火 / 決意 / 英雄の意志 / 幸運の硬貨 / 相棒の魂。
- 追加例：巡礼者の杖（戦闘終了時50%でハンガー+1）／ 記憶の砂時計（1T目に最小ダイスを最大化）／
  激情の刃（HP<50% で与ダメ+30%）／ 希望の灯片（戦闘終了時 HP+3）。
- 効果の実装は `InventorySystem/PassiveItems/Effects/PassiveItemEffects.cs`。

> 個別アイテムの数値・効果は **`items.json` と実コードを正本**とし、本 spec で個別に複製しない。
> （バランス Tier の自動学習結果は `BALANCE_TIER_LIST_*.md` にあるが BOT 自動生成＝参照のみ。）

## Constraints

- MUST：個別アイテムの効果数値は `items.json` / 実コードを正本とし、本 docs に複製しない。

## Open Questions

（なし）

## See Also

- Specs: [inventory](inventory.md), [shop-economy](shop-economy.md), [combat](combat.md)
- 正本: [items.json](../../Assets/Data/InventorySystem/items.json)
