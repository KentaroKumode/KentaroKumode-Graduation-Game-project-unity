# 引き継ぎドキュメント (HANDOFF)

最終更新: 2026-05-04

Unity 3D ローグライク（ボードゲーム調）。本ドキュメントは直近の大規模実装の状況サマリ。

---

## 1. 完了済み実装

### 1.1 イベントシステム
- フォーマット: `イベント名:出現条件:フレーバー:選択肢-結果-選択後フレ/...`
- `EventDefinition` / `EventChoice` / `EventEffect` / `EventCondition` / `EventEffectType`
- `EventParser`（`パッシブ:[xxx]` 条件、`パッシブアイテム[xxx]を廃棄` などのヒューリスティクス）
- `EventDatabase`（`Resources/Events/event_list` から読み込み、priority/rare/onceOnly対応）
- `EventEffectExecutor`（`RunState`/`HungerSystem` に効果適用、戦闘後効果は `postCombatEffects` でキュー）
- `EventEncounter` シングルトン（auto-create + `_shuttingDown` + `[RuntimeInitializeOnLoadMethod]`）
- `Resources/Events/event_list.txt` に60+イベント。新規: 血に酔う者(4-5層) / 切り伏せる男(3-5層・ちいさな灯火必須) ほか、巡礼者の石碑・凍結した時計塔の選択肢追加

### 1.2 5層ボス・カルマ清算
- カルマに応じた毎ターン HP ダメージ（`CombatManager.karmaCurseDamagePerTurn`）
- 血の負債 / 影の代償 として常時デバフ化（影の代償は **50%でダイス-1**）

### 1.3 バフ/デバフ（TimedEffect）
- `ITimedEffect` + `TimedEffectTrigger`(CombatStart/OnRoll/OnTurnEnd/CombatEnd/OnMapMove)
- `TimedEffectRegistry` / `TimedEffectManager`
- 実装済み効果: Liberator / MutualAid / BeastBond / BeastFavor / Mission / CursedThirst / DeadInvitation / StarBlessing / Revelation / Sentiment / TimeGaze / Poison / SpringBlessing / SproutPrayer ほか
- `CombatContext` 拡張: `halveFirstEnemyAttack` / `playerDamageNegateCharges` / `nullifyFirstEnemyRoll` / `healHalved` / `receivedDamageBonus` / `outgoingDamageMultiplier`（`BeginNewTurn` でリセット）

### 1.4 フロアモディファイア統合
- `perTurnSelfDamage` / `enemyPerTurnHeal` / `defeatDamageReduction` / `coinRewardMultiplier` / `shopPriceMultiplier` を実フックに接続
- 4層 +20% / 5層 +40% のショップ価格倍率

### 1.5 トレジャー・トラップ
- トレジャー: ランダム G + アイテム1個（`EventOnlyItemFilter` でイベント限定除外）
- トラップ: 一旦効果なし（仕様保留）

### 1.6 ショップシステム
- `ShopManager` シングルトン (`_shuttingDown` + `[RuntimeInitializeOnLoadMethod]`)
- 7スロット: パッシブ×2 / 消費×2 / 武器×1 / ダイス×1 / 武器強化素材×1
- 強化素材は在庫無限・価格 = base × 2^N × priceMultiplier
- Tier重み: BRONZE 54% / SILVER 35% / GOLD 10% / LEGENDARY 1%（MYTHICは全カテゴリで除外）
- `WeaponShopFilter`: 武器枠でLEGENDARY除外（強化最終形保護）
- `EventOnlyItemFilter`: 6固有 + 4新規固有 + 連番フラグ系を除外
- 売却: パッシブ/消費/強化素材 すべて対応
- TryBuy / TrySell / Close / Generate(floor) API

### 1.7 名前付き固有パッシブアイテム
既存6 (ちいさな灯火 / 決意 / 英雄の意志 / 血の盟約 / 幸運の硬貨 / 相棒の魂) + 新規4:
- **巡礼者の杖**: 戦闘終了時 50% でハンガー+1
- **記憶の砂時計**: 1ターン目に最小ダイスを最大値化
- **激情の刃**: HP<50% で与ダメ +30%
- **希望の灯片**: 戦闘終了時 HP+3（フレーバー指定済）

`PassiveItemRegistry` / `PassiveItemManager` / `PassiveItemEffects.cs` に実装済み。

### 1.8 ショップビジュアル
- `MapTransitionController`: 巻物のように丸まる（Y軸回転 75° + Y方向スケール 0.001）`RollUp` / `Unroll` コルーチン
- `ShopVisualizer`: 背景プレーン + 7スロット枠（4+3レイアウト）
- `ShopSlotVisual`: SpriteRenderer アイコン + TextMesh 価格、`OnMouseDown` で購入ダイアログ
- `ShopPurchaseDialog`: IMGUI モーダル + 3D カードプレビュー（`itemData.fbxModel` を流用）

### 1.9 不具合修正
- `EnemyPassiveEntry` フィールド名修正（`internalName` / `skillName`）
- `ItemDatabase` の `Enum.TryParse` を `ignoreCase: true` に（4箇所）
- シーン破棄時の `ShopManager` 再生成警告 → `_shuttingDown` パターン適用（`EventEncounter` も同様）

---

## 2. 実装中・進行中

### 2.1 ショップビジュアル動作確認 ⚠ ブロック中
シーン側のセットアップ未完で、ショップマス到達時にビジュアルが起動していない。以下が必要:
- `GameDebugHUD` をシーンに追加（HUD非表示問題の根本）
- `MapTransitionController` をシーンに追加し `mapVisualizer` 参照を設定
- `ShopVisualizer` をシーンに追加（背景テクスチャ）
- `ShopPurchaseDialog` をシーンに追加
- `MapVisualTestDriver` を無効化（数字キーを横取りしてフロア再生成してしまう）

### 2.2 入力系問題（直近のブロッカー）
- 数字キーは `MapVisualTestDriver` の `Update` が拾うので「効いている」ように見えるが、実は `GameManager.Update` 自体が走っていない疑い
- G キー無反応のため、`GameManager.cs` の G ハンドラに `Debug.Log("[GameManager] G pressed (phase=...)")` を追加済み（Title 限定ガードも警告付きで残置）
- 次にユーザーが G を押した時のログで切り分け予定:
  - ログ出る → `phase=` で原因特定（autoStartRun か前回ラン残り）
  - ログ出ない → GameObject 非アクティブ / コンパイルエラー / Update 未実行
- 候補根本原因: `GameManager.Start()` で `CombatManager.Instance.OnCombatEnd += ...` が NullReferenceException を投げている可能性（Update 自体は走るはずだが要確認）
- ユーザー環境の Active Input Handling は要確認（Old / Both 必須）

---

## 3. 未着手・保留

- 既存6固有パッシブ (ちいさな灯火 / 決意 / 英雄の意志 / 血の盟約 / 幸運の硬貨 / 相棒の魂) の効果実装
- インベントリで装備したアイテムが戦闘ロジックに反映されない件（**保留**）
- トラップマスの効果デザイン
- ショップ背景テクスチャ・スロット枠プレハブの本実装（現状仮素材）
- 3Dカードプレビューのカメラ前配置の煮詰め

---

## 4. 設計上の決定事項（Why付き）

| 項目 | 決定 | 理由 |
|---|---|---|
| カルマ干渉アイテム | **作らない**（罪人の鈴 等は廃案） | ユーザー方針 |
| LEGENDARY 武器ショップ | **全除外** | 強化最終形を排出すると強化ルートが破綻 |
| Tier重み再調整 | LEGENDARY 1% / GOLD 10% | 元案がきつすぎたため緩和 |
| ショップ価格倍率 | 4層 +20% / 5層 +40% | 後半フロアで難度カーブ |
| 影の代償 | 50%確率でダイス-1 | 常時 -1 はデメリット過大だった |
| ショップ価格 | priceMultiplier ×フロア倍率 | 既存フィールドを活用 |
| 報酬バランス | バフより**アイテム寄り** | バフ報酬過多のフィードバック |

---

## 5. 重要なシングルトンパターン
シーン破棄時警告防止のため、以下を必ず守る:

```csharp
private static bool _shuttingDown;

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void ResetStatics() { _shuttingDown = false; _instance = null; }

public static T Instance {
    get {
        if (_shuttingDown) return null;
        // auto-create
    }
}

void OnApplicationQuit() { _shuttingDown = true; }
void OnDestroy() { if (_instance == this) _instance = null; }
```
適用済: `ShopManager`, `EventEncounter`。他のシングルトンも同様化推奨。

---

## 6. 主要ファイル参照

- [Assets/Scripts/GameLoop/GameManager.cs](Assets/Scripts/GameLoop/GameManager.cs) — 中心オーケストレータ。フェーズ管理・各マス処理
- [Assets/Scripts/GameLoop/RunState.cs](Assets/Scripts/GameLoop/RunState.cs) — ラン状態（karma/weaponMaterials/ownedFlags/timedBuffs等）
- [Assets/Scripts/CombatSystem/CombatManager.cs](Assets/Scripts/CombatSystem/CombatManager.cs) — 戦闘・カルマ呪い・各種フック
- [Assets/Scripts/EventSystem/EventEncounter.cs](Assets/Scripts/EventSystem/EventEncounter.cs)
- [Assets/Scripts/EventSystem/EventEffectExecutor.cs](Assets/Scripts/EventSystem/EventEffectExecutor.cs)
- [Assets/Scripts/EventSystem/TimedEffects/](Assets/Scripts/EventSystem/TimedEffects/)
- [Assets/Scripts/InventorySystem/Shop/ShopManager.cs](Assets/Scripts/InventorySystem/Shop/ShopManager.cs)
- [Assets/Scripts/InventorySystem/Shop/Visual/](Assets/Scripts/InventorySystem/Shop/Visual/)
- [Assets/Scripts/InventorySystem/PassiveItems/Effects/PassiveItemEffects.cs](Assets/Scripts/InventorySystem/PassiveItems/Effects/PassiveItemEffects.cs)
- [Assets/Scripts/MapSystem/Visual/MapTransitionController.cs](Assets/Scripts/MapSystem/Visual/MapTransitionController.cs)
- [Assets/Resources/Events/event_list.txt](Assets/Resources/Events/event_list.txt)

---

## 7. 次に着手すべき手順

1. G キー押下後の Console ログを確認 → 入力経路の切り分け
2. 入力復活後、`GameDebugHUD` / `MapTransitionController` / `ShopVisualizer` / `ShopPurchaseDialog` をシーンに配置
3. `MapVisualTestDriver` を無効化、または `autoStartRun = true` に変更してテスト簡略化
4. ショップマス到達 → ロールアップアニメ → ショップ表示 → 購入ダイアログ → 購入確定 の一連を通す
5. 既存6固有パッシブの効果実装に着手
