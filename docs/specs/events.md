---
title: イベントシステム
description: テキスト定義のイベント（出現条件・選択肢・効果）のパース・抽選・効果適用
status: provisional
related: [map-run, items, inventory]
---

# イベントシステム

## Purpose

Event マスで発生するテキスト駆動のイベント。出現条件・選択肢・結果効果を1行フォーマットで
定義し、パースして実行する。正本: [EventSystem/](../../Assets/Scripts/EventSystem/)、
定義データは [Assets/Resources/Events/event_list.txt](../../Assets/Resources/Events/event_list.txt)（60+ イベント）。

## Definition

### フォーマット

```
イベント名:出現条件:フレーバー:選択肢-結果-選択後フレ/選択肢-結果-選択後フレ/...
```

- パーサ `EventParser.cs` が条件（例 `パッシブ:[xxx]`）や効果文字列を解釈する。
- データ型：`EventDefinition` / `EventChoice` / `EventEffect` / `EventCondition` / `EventEffectType`。
- `EventDatabase.cs`：`Resources/Events/event_list` から読み込み、priority / rare / onceOnly に対応。
- `EventEffectExecutor.cs`：効果を `RunState` / `HungerSystem` に適用。戦闘後効果は `postCombatEffects` でキュー。
- `EventEncounter.cs`：シングルトン（auto-create + `_shuttingDown` + `[RuntimeInitializeOnLoadMethod]`）。

### 効果種別（[EventEffectType.cs](../../Assets/Scripts/EventSystem/EventEffectType.cs)）

HpDelta / HpFullHeal / HpSetTo / MaxHpDelta / GoldDelta / HungerDelta / KarmaGain /
MaterialDelta / ArmorDurabilityLoss / TimedBuff / TimedDebuff / PermanentDebuff /
GainPassiveItem / GainConsumableItem / GainSpecificItem / GainFlag / DiscardFlag /
DiscardPassiveItem / EnterCombat / EnterEliteCombat / RandomEvent /
Probability（確率分岐の親）/ None。

### 関連メカニクス

- 「災厄の予兆」イベントで〈根拠のない確信〉を取得し `convictionStage` が始動（[overview](overview.md)）。
- 一度のみイベントは `RunState.seenOnceEvents` で既出管理。

## Constraints

- MUST：`onceOnly` イベントは `seenOnceEvents` で再出現を防ぐ。
- MUST：戦闘後にしか適用できない効果は `postCombatEffects` にキューする。

## Open Questions

（なし）

## See Also

- Specs: [map-run](map-run.md), [items](items.md), [inventory](inventory.md)
- データ: [event_list.txt](../../Assets/Resources/Events/event_list.txt)
