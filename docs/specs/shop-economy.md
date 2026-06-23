---
title: ショップと経済（CoinSystem）
description: 7スロットショップ・Tier重み・価格倍率・強盗交渉、CoinSystem 経済の実体
status: provisional
related: [items, inventory, meta-progression, map-run]
---

# ショップと経済（CoinSystem）

## Purpose

ショップはランの主な購買口。経済の実体は **CoinSystem**（精密なコイン物理＋チケット換算）で、
これは旧ツール由来ではなく**現役コア**。正本: [InventorySystem/Shop/](../../Assets/Scripts/InventorySystem/Shop/),
[CoinSystem/](../../Assets/Scripts/CoinSystem/)。

## Definition

### ショップ構成（[ShopManager.cs](../../Assets/Scripts/InventorySystem/Shop/ShopManager.cs) ほか）

- シングルトン（`_shuttingDown` + `[RuntimeInitializeOnLoadMethod]`）。
- 7スロット：パッシブ×2 / 消費×2 / 武器×1 / ダイス×1 / 武器強化素材×1。
- 在庫・スロットは `ShopInventory.cs` / `ShopSlot.cs`。API: TryBuy / TrySell / Close / Generate(floor)。
- 強化素材は在庫無限・価格 = base × 2^N × priceMultiplier。
- フィルタ：`WeaponShopFilter`（武器枠で LEGENDARY 除外＝強化最終形保護）、
  `EventOnlyItemFilter`（イベント限定固有・連番フラグ系を除外）。
- 価格倍率：フロアモディファイア由来で 4層 +20% / 5層 +40%。
- Tier 重み・レアリティ確率の具体値は `ShopInventory.cs` を正本とする（数値はコード参照）。

### 値下げ交渉（＝強盗）

- メタ恒久バフ `ShopRobberyUnlock` で解禁。実行すると「怪しい商人戦」へ（`shopRobberyInProgress`）。
  勝利で `robberyPendingItems` を獲得、以降ショップは全スキップ（`shopsBlocked`）。

### CoinSystem（[CoinSystem/](../../Assets/Scripts/CoinSystem/)）

- `CoinSystemController` が8サブマネージャー（排出・プール・スタック・物理・音声・チケット・支払い・表示）を統括。
- 換算：コイン10枚 = 1チケット（`CoinTicketConversionManager`）。
- 支払い分岐：10枚超→チケット優先消費、10枚以下→チケット崩し（`PaymentManager`）。
- プール上限 `maxConcurrentCoins = 300`、`OnDestroy` で全 `DestroyImmediate`（メモリ安全）。

### ショップビジュアル（未完成）

- `ShopVisualizer` / `ShopPurchaseDialog` / `MapTransitionController`（巻物ロールアップ演出）は存在するが、
  シーンセットアップ未完で**到達時のビジュアルが未開通**。
  → [shop-visual](../open-questions/shop-visual.md)。ロジック（購買処理）は可動。

## Constraints

- MUST：武器枠で LEGENDARY を排出しない（強化最終形保護）。
- MUST：シングルトンは `_shuttingDown` パターンを守る（シーン破棄時の再生成警告防止）。

## Open Questions

- [shop-visual](../open-questions/shop-visual.md)

## See Also

- Specs: [items](items.md), [inventory](inventory.md), [meta-progression](meta-progression.md)
