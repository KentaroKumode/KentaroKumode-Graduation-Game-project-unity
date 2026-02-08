# プロジェクト包括引き継ぎメモ

## このファイルの使い方
新しいチャットセッションの最初にこのファイルを添付して
「この引き継ぎメモを読んで、前の作業の続きから開始して」と伝えてください。

---

## プロジェクト概要
- **Unity 2022.3.22f1** / **Built-in Render Pipeline**（URP/HDRP不使用）
- ボードゲーム支援ツール：コイン物理シミュレーション＋グリッドインベントリ＋ダイス戦闘
- パス：`c:\Users\kumod\My project\`

---

## システム全体構成

```
Assets/Scripts/
├── CoinSystem/         (~5,700行/19ファイル) コイン物理・チケット・ディスプレイ
├── InventorySystem/    (~8,000行/63ファイル) グリッドインベントリ・パッシブスキル
│   ├── ItemHoldingArea.cs  (~450行) アイテム一時保持エリア（カード重ね表示）
│   └── ItemShredder.cs     (~250行) アイテムシュレッダー（未使用・予備）
├── CombatSystem/       (~1,175行/4ファイル)  ダイス戦闘ロジック
│   └── DiceLED/        (~1,600行/5ファイル)  LED演出システム
├── CameraMouseFollow.cs    (~250行) WASDビューポイント切替カメラ
├── CompleteDarknessMode.cs  (82行) 完全暗闇描画モード
├── MemoryLeakPreventionFramework.cs (193行) メモリ監視
└── TLSAllocatorErrorMonitor.cs       (72行) TLSエラー監視
```

---

# 1. CoinSystem（コインシステム）

## アーキテクチャ

```
CoinSystemManager (934行) ← 一元設定ファクトリー（100+個のSerializeField）
  └─ CoinSystemController (862行) ← 統合コントローラー（8サブマネージャー統括）
       ├── CoinDispenser (248行) ← 排出制御・WaitForSecondsキャッシュ
       ├── CoinPoolManager (117行) ← オブジェクトプール（Queue<GameObject>）
       ├── CoinStackManager (544行) ← スタック管理（10枚/束）＋アニメーション
       ├── CoinPhysicsManager (67行) ← 排出物理力（インパルス＋トルク）
       ├── CoinAudioManager (213行) ← ラウンドロビン音声再生
       ├── TicketSystemManager (326行) ← チケット生成/管理
       ├── PaymentManager (149行) ← 支払いロジック（大口/小口分岐）
       ├── CoinTicketConversionManager (209行) ← コイン⇔チケット自動変換
       └── TiledPixelDisplay (977行) ← 液晶ディスプレイ表示
```

## 主要クラス詳細

### CoinSystemController.cs（統合コントローラー）
- **役割**: 全サブマネージャーの統括。排出→整列→チケット変換を自動シーケンス
- **主要API**: `DispenseCoins(int)`, `SortCoinsInternal()`, `ConsumeCoins(int)`, `UpdateDisplay(int)`, `ReturnAllCoinsToPool()`
- **イベント**: `OnDispenseComplete`, `OnSortComplete`
- **ディスプレイ**: 排出中はランダム数字演出、`GetTotalCoinCount()`はチケット換算（1チケット=10コイン）

### CoinDispenser.cs（排出制御）
- **役割**: コイン排出のAPI提供、可変速度モード対応
- **主要API**: `DispenseCoins(int)`, `DispenseChangeCoins(int, bool)`, `ConsumeCoins(int)`
- **最適化**: `WaitForSeconds`配列キャッシュでGC Alloc削減

### CoinPoolManager.cs（オブジェクトプール）
- **パターン**: `Queue<GameObject>`プール、`maxConcurrentCoins = 300`
- **API**: `GetCoinFromPool()`, `ReturnCoinToPool(GameObject)`, `ReturnAllCoinsToPool()`
- **メモリ安全**: `OnDestroy()`で`DestroyImmediate`全行

### CoinStackManager.cs（スタック管理）
- **内部クラス**: `StackState`（coins, basePosition, stackIndex, maxCoins）
- **配置**: `stackDirection`（縦）+ `stackGroupDirection`（横）の2軸
- **アニメーション**: `Mathf.SmoothStep`、`isKinematic`切り替え、可変速度対応
- **API**: `AddCoinToStack()`, `RemoveCoinFromStack()`, `GetCoinsFromStacks(int)`, `AnimateStackSequentially()`

### CoinBehavior.cs（個別コイン）
- **静止判定**: Rigidbody速度/角速度 < `settleThreshold` を `settleTime`秒継続で`IsSettled = true`
- `OnEnable()`でリセット（プール復帰対応）

### CoinPhysicsSettings.cs（ScriptableObject）
- `[CreateAssetMenu]`で物理マテリアル設定を外部化
- `OnValidate()`でInspector変更即反映

### PaymentManager.cs（支払い）
- **分岐**: 10枚超→チケット優先消費、10枚以下→チケット崩し判定
- 支払い後の`coinThresholdLow`未満チェックで自動チケット→コイン変換

### CoinTicketConversionManager.cs（変換）
- 閾値ベース自動変換: `coinThresholdHigh=60`→チケット化、`coinThresholdLow=50`→コイン化
- ベジェ曲線吸い込みアニメーション

### TicketSystemManager.cs（チケット）
- 印刷プロセス段階アニメーション（隠し位置→出現→物理有効化）
- 印刷中はコライダー/detectCollisions無効化

### TiledPixelDisplay.cs（ディスプレイ）
- **レンダリング**: 4×4グリッド・8×8pxタイルのソフトウェアレンダリング
- **エフェクト**: ピクセルギャップ、スキャンライン、エッジグラデーション、色温度、アウトライングロー
- **API**: `DisplayNumber(int)`, `DisplayText(string)`, `ClearDisplay()`, `SetDisplayColor(Color)`
- Standard Shader (Emissive) or Unlit/Texture

### その他ユーティリティ
- **AnimationUtility.cs**: 静的イージング関数（Bounce, Linear, Quad In/Out/InOut）
- **FixedLogicFrameController.cs**: 60Hz固定ロジック＋可変描画補間（accumulator方式）、spiral-of-death防止
- **StableFrameController.cs**: 同上の代替実装（FPS計測機能付き）
- **CoinDispenserGizmos.cs**: エディタGizmo可視化（排出方向矢印・スタック位置・チケット位置）
- **CoinDispenserTest.cs**: テストUI（現在`Update()`/`OnGUI()`ともに`return;`で無効化）
- **CoinSystemSetupManager.cs**: 旧版セットアップ（`CoinSystemManager`に移行済み）
- **Shaders/NoLighting.shader**: Unlit透過シェーダー

---

# 2. InventorySystem（インベントリシステム）

## アーキテクチャ

```
InventoryManager (230行, シングルトン) ← 中央制御・10イベント
├── GridManager (905行) ← 5×8グリッド管理
│   ├── GridCell (539行) ← セル状態・インジケータ
│   ├── GridCellItemDisplay (100行) ← セル上テキスト表示
│   └── GridExpansionManager (160行) ← 行アンロック演出
├── ItemDatabase (280行, ScriptableObject シングルトン) ← JSON→アイテムDB
│   ├── ItemDataV2/CompleteItemData (300行) ← コアデータモデル
│   └── ItemData.cs (50行) ← JSONデシリアライズ中間クラス
├── Actions/ ← アイテム操作
│   ├── ItemEquipHandler (130行) ← 装備（武器/防具排他）
│   ├── ItemSellHandler (100行) ← 売却（CoinSystem連携TODO）
│   ├── ItemUseHandler (75行) ← 使用（効果適用TODO）
│   └── ItemDiscardHandler (55行) ← 破棄（確認Dialog TODO）
├── PassiveSkills/ ← 戦闘スキル
│   ├── PassiveSkillManager (613行, シングルトン) ← トリガー発火・ダメージパイプライン
│   ├── PassiveSkillRegistry (120行) ← 静的レジストリ（38スキル）
│   ├── CombatContext (170行) ← 戦闘ステートコンテナ
│   ├── IPassiveSkillEffect (25行) ← スキルインターフェース
│   ├── PassiveSkillTrigger (60行) ← 22種トリガー列挙型
│   └── Effects/
│       ├── AllPassiveSkillEffects (480行) ← 22プレイヤースキル
│       └── EnemyPassiveSkillEffects (280行) ← 16敵スキル
├── Interaction/ ← UI操作
│   ├── DragDropHandler (1250行) ← D&D全ワークフロー
│   ├── CameraLockController (110行) ← カメラ移動抑制
│   ├── RightClickHandler (65行) ← 右クリック
│   └── DoubleClickDetector (45行) ← ダブルクリック
├── UI/ (15ファイル) ← 3D空間UI
│   ├── ItemPreviewStatusUI (954行) ← FBXカード表示
│   ├── ItemPreview3DTextDisplay (240行) ← 3Dテキスト表示
│   ├── ItemPreviewTextRenderer (190行) ← テキスト生成
│   ├── TextRenderer3D (378行) ← RenderTexture 3Dテキスト
│   ├── Optimized3DTextRenderer (340行) ← TMP 3D直接
│   ├── ItemTooltip (160行) ← ツールチップ
│   ├── PlacementIndicator (90行) ← 配置表示
│   ├── FilterPanel (145行) ← カテゴリフィルタ
│   ├── WarningDialog (165行) ← 確認ダイアログ
│   ├── BackGroundPlane (208行) ← 背景フェード
│   ├── ItemSlot (100行) ← アイテムスロット
│   └── ...
├── DetailView/ ← 詳細表示
│   ├── ItemDetailView (200行) ← アイテム詳細
│   ├── ItemCompareView (120行) ← 装備比較
│   ├── BackgroundBlurEffect (335行) ← RenderTextureブラー
│   └── ...
├── Placement/ ← 配置
│   ├── PlacementValidator (160行) ← 配置バリデーション
│   └── AutoPlacementManager (70行) ← 自動配置（最適配置TODO）
├── Save/ ← セーブ
│   └── InventorySaveManager (150行) ← JSON保存
├── Effects/ ← 演出
│   ├── InventorySoundManager (110行, シングルトン) ← サウンド
│   ├── UnlockEffectController (55行) ← アンロックパーティクル
│   └── FilterHighlightEffect (60行) ← フィルタハイライト
├── Integration/ ← 外部連携
│   └── QuestItemDetector (90行) ← クエストアイテム追跡
├── Utilities/
│   └── TextureGenerator (155行) ← 配置インジケータテクスチャ動的生成
└── Editor/ (3ファイル) ← エディタ拡張
```

## 主要クラス詳細

### InventoryManager.cs（中央マネージャー・シングルトン）
- **イベント**: `OnItemAdded`, `OnItemRemoved`, `OnItemEquipped`, `OnItemUnequipped`, `OnItemUsed`, `OnItemDiscarded`, `OnGridExpanded`, `OnFilterChanged`, `OnInventoryOpened`, `OnInventoryClosed`
- **API**: `AddItem()`, `TryAddItemAuto()`, `RemoveItem()`, `EquipItem()`, `UnequipItem()`, `ExpandGrid()`, `OpenInventory()`, `CloseInventory()`
- `MemoryLeakPreventionFramework`統合済み

### GridManager.cs（グリッド管理）
- **サイズ**: 5列×8行、左上原点座標系（X+ = 左, Z+ = 下）
- **API**: `InitializeGrid()`, `UnlockRow()`, `GetCell(x,y)`, `CanPlaceItem()`, `PlaceItem()`, `RemoveItem()`, `HighlightCells()`, `ClearAllHighlights()`
- 3Dオブジェクト0.5xスケール、Gizmo可視化、インジケータプレハブ管理

### ItemDataV2.cs / CompleteItemData（データモデル）
- **列挙型**: `ItemCategory`（Weapon/Armor/Accessory/Consumable/Material/Quest/Misc/Passive/PassiveItem）
- **列挙型**: `ItemRarity`（BRONZE/SILVER/GOLD/LEGENDARY/MYTHIC）
- **CompleteItemData**: 後方互換エイリアス付き（managementId, sizeX/Y, cardModel, modelPrefab, itemIcon）

### ItemDatabase.cs（ScriptableObject シングルトン）
- `Resources/items.json`からロード、FBXアサイン保持
- 価格計算: buy=100-125%, sell=50-75% of basePrice
- **API**: `GetItem()`, `GetAllItems()`, `GetItemsByCategory()`, `GetCardModel()`, `ConvertToCompleteItemData()`

### DragDropHandler.cs（1250行、最大ファイル）
- 3Dオブジェクトピッキング（RaycastAll）
- 右クリックプレビュースピン（画面中央回転表示）
- カメラロック、背景ブラー、Emission有効化
- PreviewCardレイヤー除外
- プレースホルダー自動生成＋マウス追従アニメーション

### ItemPreviewStatusUI.cs（954行）
- カメラ子要素としてFBXモデル＋背景プレーン＋3Dテキスト配置
- カードサイズ別スケール倍率（1x1〜3x3）
- スライドアニメーション、AudioListener重複修正
- 従来2Dから3D表示に移行済み

---

# 3. CombatSystem（戦闘システム）

## アーキテクチャ

```
CombatManager (634行, シングルトン) ← 戦闘ループ
├── EnemyDatabase (125行, static) ← enemies.json
│   └── EnemyData (57行) ← 敵データモデル
├── PassiveSkillManager ← (InventorySystem内、共有)
└── DiceLED/ ← LED演出
    ├── DiceLEDManager (634行) ← 10ダイス管理
    ├── SingleDiceLED (344行) ← 1ダイス（9LED）制御
    ├── DiceLEDShader.shader ← GPU Instancingシェーダー
    ├── DiceLEDTest (278行) ← テストコントローラー
    └── Editor/DiceLEDAutoSetup (338行) ← エディタ自動セットアップ
```

### CombatManager.cs（戦闘コア・シングルトン）
- **構造体**: `TurnResult`（双方ダイス値・合計・ダメージ・勝敗）, `CombatResult`（勝敗・ターン数・残HP）
- **イベント**: `OnCombatStart`, `OnTurnEnd`, `OnCombatEnd`
- **API**: `StartCombat(string enemyId)`, `StartCombat(EnemyData)`, `ExecuteTurn()`, `ExecuteFullCombat()`
- **戦闘ルール**:
  1. 双方ダイス全振り→合計値比較
  2. 勝者がダイス合計差 = メインダメージ
  3. ダイス数差 → 差分ダイスを追撃/反撃リロール
  4. クリティカル判定（X/9確率）
  5. PassiveSkillManagerへのトリガー発火

### 4武器ロール（items.json）
| ロール | ダイス構成 | 説明 |
|--------|-----------|------|
| Shield/タンク | 2d3→3d6 | 低火力・高安定 |
| Sword/ナイト | 2d4→3d7 | バランス型 |
| Axe/バーサーカー | 1d9→2d9 | 高火力・不安定 |
| Dagger/アサシン | 1d8 | 特殊効果特化 |

### EnemyDatabase.cs（敵データベース）
- `Resources/enemies.json`から遅延ロード
- **API**: `Get(id)`, `GetByFloor(floor)`, `GetNewOnFloor(floor)`, `GetRandom()`
- **EnemyData**: id, displayName, floor(1–7), maxHP, diceCount, diceMaxValue, criticalNumerator(0–9), passiveSkills

---

# 4. パッシブスキルシステム

## PassiveSkillManager.cs（613行・シングルトン）
- `activeSkillsByTrigger` (Dictionary<trigger, List<effect>>)
- **API**: `RefreshActiveSkills()`, `AddItemSkills()`, `RemoveItemSkills()`, `BeginCombat()`, `EndCombat()`, `BeginTurn()`, `FireTrigger()`, `FireEnemyTrigger()`
- **ProcessPostRoll()**: ダイス処理＋バフ適用＋勝敗トリガー
- **ProcessDamage()**: 8段階ダメージパイプライン
- **敵スキル実行**: 視点スワップ（player⇔enemy入替）で発火

## CombatContext.cs（170行）
- accumulatedValues（累積値辞書）, nextTurnBuffs→currentBuffs, fixedDamageToEnemy, nullifyAllDamage
- bleedStacks, consecutiveWins/Losses, diceOverrideRequests
- **API**: `BeginNewTurn()`, `GetAccumulated()`, `AddAccumulated()`, `GetBuff()`

## 22トリガー種別
BattleStart/End, TurnStart/End, PreRoll/PostRoll, Win/Lose/Draw, Pre/PostDealDamage, Pre/PostReceiveDamage, Pursuit, Critical, StatusEffect, Equip/Unequip, etc.

## プレイヤースキル22種
| 武器 | スキル名 | 効果 |
|------|---------|------|
| Shield | Breakfall | ダメージ2軽減 |
| Shield | SpikeArmor | 被ダメージ→敵に3固定ダメ |
| Shield | Endurance | 連敗で防御+2累積 |
| Shield | DivineShield | 5%で全ダメージ無効 |
| Shield | DawnBlessing | 最終ダメージ50%軽減 |
| Sword | BasicSword | 攻撃+2 |
| Sword | Recovery | 勝利時HP回復（※バグ:diceMax未使用→固定d6） |
| Sword | WandererWit | 5ターンごとに攻撃+5 |
| Sword | DragonSlayer | 連勝で攻撃+3累積 |
| Sword | VoidStance | 差≤3でダメ無効+敵に3固定 |
| Axe | PainRevert | 被ダメ→次ターン攻撃+50%蓄積 |
| Axe | Warcry | 戦闘開始→3ターン攻撃+3 |
| Axe | BloodPact | 出血スタック2付与/ターン |
| Axe | ApexPredator | 追撃ダメージ2倍 |
| Axe | BloodDecree | ゾロ目→敵10固定+全ダメ無効+クリ率UP |
| Dagger | Ambush | 初回+5 |
| Dagger | FatalStab | クリティカルダメージ1.5倍 |
| Dagger | Sting | 出血スタック1付与/ターン |
| Dagger | Execution | HP25%以下でダメ2倍 |
| Dagger | BlindJustice | 被ダメ→次ターン攻撃+10 |
| Dagger | Nightfall | 3連敗→次3ターン攻撃2倍 |

## 敵スキル16種（3階層グループ）
| 階層 | スキル名 | 効果 |
|------|---------|------|
| 1-3 | Trapper | 負け→次ターン相手攻撃-3 |
| 1-3 | Undying | 致死ダメージ1回だけHP1で耐え |
| 1-3 | Sprint | 戦闘開始→攻撃+3/3ターン |
| 1-3 | BruteForce | 追撃ダメ1.5倍 |
| 1-3 | Flight | 20%で全ダメ無効 |
| 4-5 | HardScales | 常時ダメ2軽減 |
| 4-5 | TailStrike | 勝利→追加4ダメ |
| 4-5 | Rampage | 連勝で攻撃+2累積 |
| 4-5 | Ethereal | ダイス差≤2で無効 |
| 4-5 | Curse | 3ターンごとに相手攻撃-5 |
| 4-5 | Immovable | 10以下のダメージ無効 |
| 4-5 | CounterStance | 被ダメ→50%跳ね返し |
| 6-7 | MultiHead | ダイス+1個追加 |
| 6-7 | Regeneration | ターン開始HP+3 |
| 6-7 | DemonAura | 常時攻撃+5 |
| 6-7 | Hellfire | 勝利→出血3スタック |
| 6-7 | Lifesteal | 与ダメの50%HP回復 |
| 6-7 | DeathSentence | 10ターン目に即死 |

---

# 5. DiceLEDシステム

## シェーダー（DiceLEDShader.shader）
- **パス**: `CombatSystem/DiceLED`
- Unlit + Emission + GPU Instancing
- `_MainTex`/`_Color`（アルベド）、`_BaseColor`/`_EmissionColor`（per-instance）
- フラグメント: `albedo * (baseCol + emission)` → テクスチャ＋発光両立

## SingleDiceLED.cs（1ダイス = 9LED）
- 3×3グリッド配置、LED[0]～[8]（左上→右下）
- **座標ベース自動マッピング**: Z昇順=上列、X降順=左列（名前に依存しない）
- `SetValue(0-9)`, `SetRandomValue(max)`, `ApplyVisuals()`
- `isDirty`フラグ：値変更時のみMaterialPropertyBlockを更新

### 出目パターン（0-9）
```
0: ○○○  1: ○○○  2: ○○○  3: ●○○  4: ●○●
   ○○○     ○●○     ●○●     ○●○     ○○○
   ○○○     ○○○     ○○○     ○○●     ●○●

5: ●○●  6: ●○●  7: ●○●  8: ●●●  9: ●●●
   ○●○     ●○●     ●●●     ●○●     ●●●
   ●○●     ●○●     ●○●     ●●●     ●●●
```

## DiceLEDManager.cs（10ダイス管理）
- `playerDice[5]`, `enemyDice[5]`（Inspector設定）
- **ローリングアニメーション4段階**:
  1. Phase1: 高速ランダム表示
  2. Phase2: 段階的に確定（stagger settle）
  3. Phase3: 確定フラッシュ（on→off→on）
  4. Phase4: 全最大値チェック→Celebration
- **Max Celebration**: ゴールド色パターンフラッシュ→ウェーブ復元→ゴールドブースト→元色復帰
- **イベント**: `OnRollingComplete`, `OnAllMax(bool isPlayer)`
- **命名規則**: DICE_1〜5=プレイヤー、DICE_6〜10=エネミー
- **ContextMenu**: "Auto-Assign All Dice"、"Auto-Assign All (Dice + LEDs)"

## DiceLEDTest.cs（テストコントローラー）
- **キー**: Space=ロール, M=全最大値テスト, 0-9=パターン直接, R=リセット, C=色変更, ↑↓=Pダイス数, ←→=Eダイス数
- OnGUI: ステータス、ダイス設定、ロール結果、勝敗、MAXインジケータ

## Editor/DiceLEDAutoSetup.cs（エディタ自動セットアップ）
- **メニュー**: Tools → DiceLED Auto Setup
- シーン内DICE_1〜10を自動スキャン
- SingleDiceLEDコンポーネント追加＋座標ベースLEDマッピング
- DiceLEDManager自動作成＋ダイス割当
- 完全Undo対応

---

# 6. 共通インフラ

### CameraMouseFollow.cs（~251行）→ WASDビューポイントシステム
- **旧**: マウスX座標で5エリア分割→Left/Center/Right 3状態のラッチ遷移
- **現**: WASD入力でInspector設定済みビューポイント間を切り替え
  - A = viewpoint_inv（インベントリ表示）
  - D = viewpoint_pot（ポット表示）
  - W = viewpoint_base（ベース表示）
- **フレームレート非依存補間**: `1 - Mathf.Exp(-moveSpeed * Time.deltaTime)` 指数減衰（旧: `Lerp(pos, target, 0.1f)` = フレーム依存）
  - `moveSpeed`（float, デフォルト8f, 範囲1-20）でInspector調整可能
- `LockCamera()` / `UnlockCamera()` 公開API（CameraLockControllerから利用）
- `OnGUI()`無効化（TLS Allocatorエラー防止）

### CompleteDarknessMode.cs（82行）
- `Awake()`でAmbient=black, Skybox=null, Reflection=null, Camera=SolidColor(black), Fog=off
- `[ContextMenu]`対応

### MemoryLeakPreventionFramework.cs（193行・シングルトン）
- 5秒ごとに`GC.GetTotalMemory`監視、1MB以上増加で警告
- 3回連続で`PerformEmergencyCleanup()`（孤立オーナー除去 + GC.Collect）
- **API**: `RegisterStaticInstance()`, `RegisterCoroutineOwner()`, `SafeRemoveAllListeners()`, `SafeStopCoroutine()`

### TLSAllocatorErrorMonitor.cs（72行）
- `Application.logMessageReceived`でTLS Allocatorエラーをカウント
- 100回/60秒で緊急警告、5段階重篤度表示

---

# 7. 設計パターンまとめ

| パターン | 使用箇所 |
|---------|---------|
| **シングルトン** | CombatManager, InventoryManager, ItemDatabase, PassiveSkillManager, InventorySoundManager, MemoryLeakPreventionFramework |
| **オブジェクトプール** | CoinPoolManager (Queue\<GameObject>) |
| **イベント駆動** | InventoryManager(10), CoinSystemController, CombatManager(3), DiceLEDManager(2) |
| **ScriptableObject DB** | ItemDatabase, ItemLibrary, CoinPhysicsSettings |
| **コルーチン非同期** | CoinDispenser, CoinStackManager, DragDropHandler, GridExpansionManager |
| **MaterialPropertyBlock** | SingleDiceLED (GPU Instancing、draw call最小化) |
| **リフレクション設定注入** | CoinSystemManager, CoinSystemSetupManager, InventoryUISetup |
| **固定タイムステップ** | FixedLogicFrameController (60Hz accumulator) |
| **3D空間UI** | ItemPreviewStatusUI, TextRenderer3D, Optimized3DTextRenderer |
| **メモリ安全** | MemoryLeakPreventionFramework, OnDestroy全面クリーンアップ |

---

# 8. データファイル

| ファイル | 場所 | 内容 |
|---------|------|------|
| items.json | Resources/ | 武器20+消耗4（minor_healing_potion, healing_potion, greater_healing_potion, full_heal_elixir）、roleName/roleDescription付き |
| enemies.json | Resources/ | 敵データ（floor 1-7） |

---

# 9. 既知のバグ・未実装

## バグ（修正済み）
1. ~~**Recovery スキル**: `Random.Range(1,7)` ハードコード~~ → ✅修正済: `ctx.playerDiceMax` 使用
2. **enemyDiceDebuff**: `ProcessPostRoll()`内に未使用の敵ダイスデバフコード残骸

## 実装済み（前セッション完了）
1. ✅ **CombatManager↔DiceLEDManager統合**: 戦闘開始でLED初期化、ダイスロール時にLEDアニメーション、戦闘終了でリセット
2. ✅ **ItemSellHandler↔CoinSystem連携**: 売却時にCoinSystemController.DispenseCoins()でコイン排出
3. ✅ **ItemUseHandler効果実装**: 消費アイテム効果（minor_healing_potion, healing_potion, greater_healing_potion, full_heal_elixir）+ CombatManager.HealPlayer/BoostPlayerMaxHP API追加
4. ✅ **Recovery バグ修正**: CombatContext.playerDiceMax/enemyDiceMax追加、PassiveSkillManager.BeginCombat拡張
5. ✅ **ItemDiscardHandler↔WarningDialog確認ダイアログ**: 破棄前に確認ダイアログ表示
6. ✅ **WASDカメラ**: CameraMouseFollow.csを完全リライト（マウス追従→WASD入力ビューポイント切替）
7. ✅ **ItemHoldingArea**: 一時保持エリア（面積降順カード重ね表示、最大5枚、D&D対応）
8. ✅ **ItemShredder**: アイテムシュレッダー（未使用・予備コード）
9. ✅ **DragDropHandler統合**: HoldingAreaカードのD&Dピックアップ
10. ✅ **InventoryManager.TryAddItemAutoフォールバック**: グリッド満杯時にItemHoldingAreaへ自動一時保持
11. ✅ **プレビュー時アイテム削除UI**: 右クリックプレビュー中にゴミ箱アイコンPlane表示→クリックで燃え尽き演出（BurnDissolveシェーダー）→インベントリから削除
12. ✅ **BurnDissolveシェーダー + ItemBurnEffect**: PerlinNoise多層ディゾルブ、黄→赤エッジグロー、火の粉パーティクル自動生成
13. ✅ **フレームレート非依存カメラ補間**: CameraMouseFollow.csの`Lerp`を指数減衰`1-exp(-moveSpeed*dt)`に変更（moveSpeed=8f）
14. ✅ **WASDキー競合解消**: InventoryTestController D→F2、InventoryVisualTester A→F3にリマップ
15. ✅ **InventoryVisualTester動的アイテムロード**: ハードコード`testItemIds`廃止→`ItemDatabase.Instance.GetAllItems()`で動的取得
16. ✅ **図鑑プレビュー背景（Book）**: DragDropHandler.csに`previewBookPrefab`追加、カメラ子オブジェクトとして生成、`SlideInPreview()`で背景＋カード同時スライドイン
  - SerializeField: `bookLocalOffset`(Vector3), `bookSlideDistance`(3f), `bookSlideInDuration`(0.4f), `bookSlideCurve`(AnimationCurve)
  - 回転補正: `Quaternion.Euler(90f, 180f, 0f)`
17. ✅ **ゴミ箱アイコンをカメラ子に変更**: ワールド空間配置→`inventoryCamera.transform`の子`localPosition = trashIconOffset`に変更（ブラーRenderTextureの背面に隠れる問題を解決）
18. ✅ **プレビューサイズ個別設定**: `previewScale1x1`(3.0)～`previewScale5x5`(0.7) + `previewScaleDefault`(1.0)で各サイズ毎のスケール調整
  - `GetPreviewScaleForSize(sizeX, sizeY)`: max(sizeX,sizeY)で正規化
19. ✅ **TextMeshPro 3Dアイテム名表示**: `CreatePreviewNameText()`でカメラ子にTMP生成（Canvas不使用）
  - SerializeField: `previewNameFont`(TMP_FontAsset), `previewNameColor`(white), `previewNameFontSize`(5f), `previewNameOffset`(0,-0.8,0)
20. ✅ **cardPositionOffset**: `previewSpinHeightOffset`(float,Y軸のみ)→`cardPositionOffset`(Vector3,3軸)にリネーム・拡張
21. ✅ **ItemBurnEffect ParticleSystem修正**: `AddComponent<ParticleSystem>`後に`Stop()`+`playOnAwake=false`を設定してからduration変更（実行時エラー防止）

## 注意: 二重プレビューシステム
- **DragDropHandler.SpinPreviewCoroutine**: 右クリックプレビュー（背景Book、スライドイン、サイズ別スケール、TMP名前表示を含む）← **現在の主システム**
- **ItemPreviewStatusUI** (954行): 独立したプレビューシステム（独自の`cardPositionOffset`、`scale1x1`～`scale3x3`、スライドアニメーション持ち）← DragDropHandlerとは**未連携**
- 両システムはそれぞれ独立しており、DragDropHandlerはItemPreviewStatusUIを一切参照していない

## 未実装（TODO）
1. **AutoPlacementManager.TryFindOptimalPlacement()**: 最適配置アルゴリズム
2. **QuestItemDetector**: OnItemRemoved座標→アイテム逆引き
3. **InventorySaveManager**: 暗号化（プレースホルダーのみ）
4. **HoldingArea/Shredder Unityシーン設定**: holdingAreaAnchor, shredderCollider等のInspector設定

---

# 10. 次のセッションでの推奨作業

1. HoldingArea/ShredderのUnityシーン配置（GameObjectにコンポーネント追加、Anchor/Collider設定）
2. FBXダイスモデルへのLEDセットアップ（DICE_1〜10命名→エディタ自動セットアップ）
3. Post-Processing / Bloom との DiceLED Emission 連携
4. AutoPlacementManager最適配置アルゴリズム
5. InventorySaveManager暗号化
6. **二重プレビューシステムの統合検討**: DragDropHandler.SpinPreviewCoroutineとItemPreviewStatusUIの役割整理・統合または分離の明確化
7. **図鑑プレビューBookプレハブ作成**: previewBookPrefabのUnityプレハブ作成（3Dモデル or Quad + テクスチャ）
8. **プレビュー名前フォント設定**: previewNameFontにTMP_FontAssetをInspectorで割当
