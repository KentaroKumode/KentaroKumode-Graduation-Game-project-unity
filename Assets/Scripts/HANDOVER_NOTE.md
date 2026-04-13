# プロジェクト包括引き継ぎメモ

## このファイルの使い方
新しいチャットセッションの最初にこのファイルを添付して
「この引き継ぎメモを読んで、前の作業の続きから開始して」と伝えてください。

---

## プロジェクト概要
- **Unity 2022.3.22f1** / **Built-in Render Pipeline**（URP/HDRP不使用）
- **Post Processingパッケージ未使用** — 全ポスト処理はカスタム`OnRenderImage`
- ボードゲーム支援ツール：コイン物理シミュレーション＋グリッドインベントリ＋ダイス戦闘
- パス：`c:\Users\kumod\My project\`

---

## システム全体構成

```
Assets/Scripts/
├── CoinSystem/         (~5,700行/19ファイル) コイン物理・チケット・ディスプレイ
├── InventorySystem/    (~9,000行/65+ファイル) グリッドインベントリ・パッシブスキル・カメラフィルタ
│   ├── Shaders/
│   │   ├── CameraFilter.shader (220行) — 5パス: Bloom+Vignette+Grain+Grading
│   │   └── TMP_SDF_Unlit.shader (170行) — Unlit SDF（TMP色化け解決）
│   ├── CameraFilter.cs (235行) — Bloomパイプライン+フィルタ制御
│   ├── ItemHoldingArea.cs (~450行) — アイテム一時保持エリア
│   └── ItemShredder.cs   (~250行) — アイテムシュレッダー（予備）
├── CombatSystem/       (~1,200行/4ファイル) ダイス戦闘ロジック
│   └── DiceLED/        (~1,600行/5ファイル) LED演出システム
├── CameraMouseFollow.cs    (~251行) WASDビューポイント切替カメラ
├── CompleteDarknessMode.cs  (82行) 完全暗闘描画モード
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
│   ├── ItemSellHandler (100行) ← 売却（CoinSystem連携済み）
│   ├── ItemUseHandler (75行) ← 使用（消費アイテム効果実装済み）
│   └── ItemDiscardHandler (55行) ← 破棄（WarningDialog確認済み）
├── PassiveSkills/ ← 戦闘スキル
│   ├── PassiveSkillManager (613行, シングルトン) ← トリガー発火・ダメージパイプライン
│   ├── PassiveSkillRegistry (120行) ← 静的レジストリ（38スキル）
│   ├── CombatContext (170行) ← 戦闘ステートコンテナ
│   ├── IPassiveSkillEffect (25行) ← スキルインターフェース
│   ├── PassiveSkillTrigger (60行) ← 22種トリガー列挙型
│   └── Effects/
│       ├── AllPassiveSkillEffects (588行) ← プレイヤースキル全種
│       └── EnemyPassiveSkillEffects (280行) ← 16敵スキル
├── Interaction/ ← UI操作
│   ├── DragDropHandler (2567行) ← D&D全ワークフロー+プレビュー+削除UI
│   ├── CameraLockController (110行) ← カメラ移動抑制
│   ├── RightClickHandler (65行) ← 右クリック
│   └── DoubleClickDetector (45行) ← ダブルクリック
├── Shaders/ ← カスタムシェーダー
│   ├── CameraFilter.shader (220行) ← 5パスポスト処理
│   └── TMP_SDF_Unlit.shader (170行) ← TMP Unlit SDF
├── CameraFilter.cs (235行) ← Bloomパイプライン制御
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
│   ├── BurnDissolve.shader (96行) ← 燃え尽きディゾルブ
│   ├── ItemBurnEffect.cs ← 燃え尽き演出制御
│   ├── ItemDisintegrationEffect.cs ← 分解演出
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
- **RarityColorUtility**: BRONZE=(0.8,0.5,0.2), SILVER=white, GOLD=yellow, LEGENDARY=(1,0.5,0), MYTHIC=cyan
- **CompleteItemData**: 後方互換エイリアス付き（managementId, sizeX/Y, cardModel, modelPrefab, itemIcon）

### ItemDatabase.cs（ScriptableObject シングルトン）
- `Resources/items.json`からロード、FBXアサイン保持
- `System.Enum.TryParse(jsonItem.rarity, out entry.rarity)` でレアリティパース
- 価格計算: buy=100-125%, sell=50-75% of basePrice
- **API**: `GetItem()`, `GetAllItems()`, `GetItemsByCategory()`, `GetCardModel()`, `ConvertToCompleteItemData()`

### DragDropHandler.cs（2567行、最大ファイル）
- 3Dオブジェクトピッキング（RaycastAll）
- 右クリックプレビュースピン（画面中央回転表示）
- カメラロック、背景ブラー、Emission有効化
- PreviewCardレイヤー除外
- プレースホルダー自動生成＋マウス追従アニメーション
- **図鑑プレビューBook背景**: `previewBookPrefab`をカメラ子に生成、`SlideInPreview()`でスライドイン演出
- **プレビュー名テキスト**: TMPでカメラ子に生成（Canvas不使用）、サイズ反比例スケーリング
- **レアリティグラデーション名前表示**: 5段階レアリティ別に上半分/下半分の色グラデーション
  - SerializeField: `nameBronzeTop/Bottom`, `nameSilverTop/Bottom`, `nameGoldTop/Bottom`, `nameLegendaryTop/Bottom`, `nameMythicTop/Bottom`
  - `GetRarityGradientColors(ItemRarity, out Color top, out Color bottom)` → `VertexGradient`適用
  - デフォルト: Bronze(銅色光沢), Silver(白→クールグレー), Gold(明→深金), Legendary(オレンジ光→深オレンジ), Mythic(白シアン→深シアン)
- **詳細情報パネル**: RichTextで レアリティ・ロール・ステータス・スキル・フレーバーテキスト表示
  - SerializeField: detailFontSize(0.35), detailTextColor, detailLabelColor(gold), detailSkillNameColor(cyan), detailRarityColor(orange), detailRoleColor(green)
- **スキルツールチップ**: TMP `<link>`タグでホバー時にスキル説明ツールチップ
- **WrapLine**(13文字改行): RichTextタグ除外カウント、句読点行末ねじ込み
- **アイテム削除UI**: ゴミ箱アイコンPlane→クリックで確認テキスト→承認で燃え尽き演出→インベントリ削除
  - 確認テキスト: 質問文＋はい/いいえの3つのTMP、アイテム名表示付き
- **サウンド連携**: プレビュー開始/確認ホバー/Yes・Noクリック音
- **preview世界照明**: プレビューアイテムはワールドライティングを受ける（`SetReceiveShadowsRecursively`除去済み）
- **sortingOrder=10**: 名前・詳細テキストのBook背景前面描画

### ItemPreviewStatusUI.cs（954行）
- カメラ子要素としてFBXモデル＋背景プレーン＋3Dテキスト配置
- カードサイズ別スケール倍率（1x1〜3x3）
- スライドアニメーション、AudioListener重複修正
- ⚠️ **注意: DragDropHandler.SpinPreviewCoroutineとは未連携の独立システム**

---

# 3. カメラフィルタシステム（ポスト処理）

## 概要
Built-in Render Pipelineのカスタム`OnRenderImage`ポスト処理。Post Processingパッケージ不使用。

## CameraFilter.shader（220行・5パス）

| パス | 名前 | 機能 |
|------|------|------|
| Pass 0 | Composite | Bloom合成 + Vignette + Film Grain + Color Grading |
| Pass 1 | Bloom Extract | HDR閾値抽出（ソフトニー付き） |
| Pass 2 | Gaussian Blur | 9タップ方向別ガウシアンブラー |
| Pass 3 | Downsample | 4タップバイリニアダウンサンプル |
| Pass 4 | Upsample | 加算ブレンドアップサンプル |

- `Shader "Hidden/CameraFilter"` / `ZTest Always` / `Cull Off` / `ZWrite Off`
- `_MainTex_TexelSize`でブラーオフセット計算
- `_BlurDirection` float2 でH/V切替

## CameraFilter.cs（235行）

- `[ExecuteInEditMode]` / `[RequireComponent(typeof(Camera))]`
- **Bloomパイプライン**: 閾値抽出 → ダウンサンプルピラミッド → 各レベルH+Vガウシアンブラー → アップサンプルチェーン → コンポジット
- **パス定数**: `PASS_COMPOSITE=0`, `PASS_EXTRACT=1`, `PASS_BLUR=2`, `PASS_DOWNSAMPLE=3`, `PASS_UPSAMPLE=4`
- Shader property IDは`static readonly`キャッシュ

### Inspector パラメータ
| カテゴリ | パラメータ | デフォルト |
|---------|-----------|-----------|
| Bloom | bloomEnabled | true |
| Bloom | bloomThreshold | 0.9 |
| Bloom | bloomSoftKnee | 0.5 |
| Bloom | bloomIntensity | 1.5 |
| Bloom | bloomIterations | 4 |
| Vignette | vignetteIntensity | 0.25 |
| Vignette | vignetteSmoothness | 0.4 |
| Film Grain | grainIntensity | 0.04 |
| Film Grain | grainScale | 3.0 |
| Color Grading | contrast | 1.05 |
| Color Grading | saturation | 1.1 |
| Color Grading | temperature | 0.05 |

### Bloomが有効に機能する条件
- カメラのHDRが有効（`OnEnable()`でHDR無効時に警告ログ出力）
- マテリアルがHDR値（>1.0）のエミッションを出力していること
- DiceLEDShader.shaderは`_EmissionColor`で最大20倍のHDR値を出力済み

---

# 4. カスタムシェーダー一覧

| シェーダー | 場所 | 行数 | 用途 |
|-----------|------|------|------|
| CameraFilter.shader | InventorySystem/Shaders/ | 220行 | 5パスポスト処理（Bloom+Vignette+Grain+Grading） |
| TMP_SDF_Unlit.shader | InventorySystem/Shaders/ | 170行 | Unlit SDF（TMP 3Dテキスト色化け解決） |
| BurnDissolve.shader | InventorySystem/Effects/ | 96行 | PerlinNoise多層ディゾルブ+エッジグロー |
| DiceLEDShader.shader | CombatSystem/DiceLED/ | 86行 | Unlit+Emission+GPU Instancing |
| NoLighting.shader | CoinSystem/Shaders/ | - | Unlit透過（プレビューBook背景等） |

### TMP_SDF_Unlit.shader の経緯
- **問題**: 標準TMP SDFシェーダーのPremultiplied Alpha（`faceColor.rgb *= faceColor.a`）により、3Dテキストの色が白く薄まる
- **解決**: カスタムUnlit SDFシェーダーで`Blend SrcAlpha OneMinusSrcAlpha`（標準アルファブレンド）を使用
- **適用**: プレビュー名テキスト等の3D TMP全般に使用

### DiceLEDShader.shader のHDR Emission
- `_EmissionColor`がMaterialPropertyBlockで個別制御（1〜20倍のHDR値）
- CameraFilter.csのBloomと連携して光り感を実現
- フラグメント: `albedo * (baseCol + emission)` → テクスチャ＋発光両立

---

# 5. CombatSystem（戦闘システム）

## アーキテクチャ

```
CombatManager (644行, シングルトン) ← 戦闘ループ
├── EnemyDatabase (125行, static) ← enemies.json
│   └── EnemyData (57行) ← 敵データモデル
├── PassiveSkillManager ← (InventorySystem内、共有)
└── DiceLED/ ← LED演出
    ├── DiceLEDManager (628行) ← 10ダイス管理
    ├── SingleDiceLED (344行) ← 1ダイス（9LED）制御
    ├── DiceMonitorDisplay (565行) ← ダイス合計値モニター
    ├── DiceLEDShader.shader (86行) ← GPU Instancingシェーダー
    ├── DiceLEDTest (278行) ← テストコントローラー
    └── Editor/DiceLEDAutoSetup (338行) ← エディタ自動セットアップ
```

### CombatManager.cs（戦闘コア・シングルトン）
- **構造体**: `TurnResult`（双方ダイス値・合計・ダメージ・勝敗）, `CombatResult`（勝敗・ターン数・残HP）
- **イベント**: `OnCombatStart`, `OnTurnEnd`, `OnCombatEnd`
- **API**: `StartCombat(string enemyId)`, `StartCombat(EnemyData)`, `ExecuteTurn()`, `ExecuteFullCombat()`, `HealPlayer()`, `BoostPlayerMaxHP()`
- **戦闘ルール**:
  1. 双方ダイス全振り→合計値比較
  2. 勝者がダイス合計差 = メインダメージ
  3. ダイス数差 → 差分ダイスを追撃/反撃リロール
  4. クリティカル判定（X/9確率）
  5. PassiveSkillManagerへのトリガー発火

### DiceLEDManager.cs（628行・10ダイス管理）
- `playerDice[5]`, `enemyDice[5]`（Inspector設定）
- **ローリングアニメーション4段階**:
  1. Phase1: 高速ランダム表示（各サイド独立スローダウン）
  2. Phase2: 段階的に確定（stagger settle）
  3. Phase3: 確定フラッシュ（on→off→on）
  4. Phase4: 全最大値チェック→Celebration
- **Max Celebration**: ゴールド色パターンフラッシュ→ウェーブ復元→ゴールドブースト→元色復帰
- **MAX値延長ローリング**: サイド独立で最大値到達時にローリング延長
- **イベント**: `OnRollingComplete`, `OnAllMax(bool isPlayer)`
- **ContextMenu**: "Auto-Assign All Dice"、"Auto-Assign All (Dice + LEDs)"
- CombatManagerと統合済み

### DiceMonitorDisplay.cs（565行）
- ダイス合計値をモニター表示
- アスペクト比安全レンダリング、ベベル/角丸、LCD効果

### SingleDiceLED.cs（1ダイス = 9LED）
- 3×3グリッド配置、座標ベース自動マッピング（LED名に非依存）
- Z昇順=上列、X降順=左列
- `isDirty`フラグで値変更時のみMaterialPropertyBlock更新

### 出目パターン（0-9）
```
0: ○○○  1: ○○○  2: ○○○  3: ●○○  4: ●○●
   ○○○     ○●○     ●○●     ○●○     ○○○
   ○○○     ○○○     ○○○     ○○●     ●○●

5: ●○●  6: ●○●  7: ●○●  8: ●●●  9: ●●●
   ○●○     ●○●     ●●●     ●○●     ●●●
   ●○●     ●○●     ●○●     ●●●     ●●●
```

### 命名規則
- サイコロ親: `DICE_1`～`DICE_5`(プレイヤー), `DICE_6`～`DICE_10`(敵)
- LED子: 任意の名前（座標で自動判定）

### エディタ自動セットアップ (Tools→DiceLED Auto Setup)
- シーン内DICE_1〜10を自動スキャン
- SingleDiceLEDコンポーネント追加＋座標ベースLEDマッピング
- DiceLEDManager自動作成＋ダイス割当、完全Undo対応

### テストコントローラー (DiceLEDTest.cs)
| キー | 機能 |
|------|------|
| Space | ダイスロール |
| M | 全最大値テスト |
| 0-9 | パターン即時表示 |
| R | リセット |
| C | 色変更 |
| ↑↓ | プレイヤーダイス数増減 |
| ←→ | 敵ダイス数増減 |

---

# 6. パッシブスキルシステム

## PassiveSkillManager.cs（613行・シングルトン）
- `activeSkillsByTrigger` (Dictionary<trigger, List<effect>>)
- **API**: `RefreshActiveSkills()`, `AddItemSkills()`, `RemoveItemSkills()`, `BeginCombat()`, `EndCombat()`, `BeginTurn()`, `FireTrigger()`, `FireEnemyTrigger()`
- **ProcessPostRoll()**: ダイス処理＋バフ適用＋勝敗トリガー
- **ProcessDamage()**: 8段階ダメージパイプライン
- **敵スキル実行**: 視点スワップ（player⇔enemy入替）で発火

## CombatContext.cs（170行）
- accumulatedValues（累積値辞書）, nextTurnBuffs→currentBuffs, fixedDamageToEnemy, nullifyAllDamage
- bleedStacks, consecutiveWins/Losses, diceOverrideRequests
- playerDiceMax, enemyDiceMax
- **API**: `BeginNewTurn()`, `GetAccumulated()`, `AddAccumulated()`, `GetBuff()`

## 22トリガー種別
BattleStart/End, TurnStart/End, PreRoll/PostRoll, Win/Lose/Draw, Pre/PostDealDamage, Pre/PostReceiveDamage, Pursuit, Critical, StatusEffect, Equip/Unequip, etc.

## プレイヤースキル（AllPassiveSkillEffects.cs — 588行）

### 盾系 (Shield)
| スキル名 | 効果 |
|---------|------|
| Breakfall | 被ダメージ-2 |
| SpikeArmor | 毎ターン敵に軽減不可2ダメ |
| Endurance | 敗北時MaxHP+1(上限20) |
| DivineShield | ターン終了時HP+2 |
| DawnBlessing | 敗北時被ダメ50% |

### 剣系 (Sword)
| スキル名 | 効果 |
|---------|------|
| BasicSword | ダイス合計≥ダイス数×2保証 |
| Recovery | 最低ダイス振り直し |
| WandererWit | ダイス差≤1で追撃無効 |
| DragonSlayer | ダイス差≤2で会心ダイス+2 |
| VoidStance | ダイス差≤3で両者ダメ0+3固定ダメ |

### 斧系 (Axe)
| スキル名 | 効果 |
|---------|------|
| PainRevert | 勝利時、減少HP/2ダメ追加 |
| Warcry | 敗北でダイス+1蓄積、勝利でリセット |
| BloodPact | 敗北時、次ターンダメ+3 |
| ApexPredator | 敗北時、追撃無効 |
| BloodDecree | ゾロ目→合計値を固定ダメ+会心+200%+会心ダイス+5 |

### 短剣系 (Dagger)
| スキル名 | 効果 |
|---------|------|
| Ambush | 初回ロールダイス+5 |
| FatalStab | 会心ダメ+100% |
| Sting | ダメ付与時出血+1(1ターン1回) |
| Execution | 勝利時、次ターン敵最小ダイス1固定 |
| BlindJustice | 反撃被ダメ時、次ターンダメ+10 |
| Nightfall | オーバーダメ×2蓄積→戦闘開始時に放出 |

### 合成武器スキル
| スキル名 | 武器 | 効果 |
|---------|------|------|
| DawnBreaker | セレナ・ドーンブレイカー (shield_sword) | ダイス差≤4でダメ0化+10固定+HP10回復 |
| BloodMoon | ブラッドドーン・インペリウム (shield_axe) | 撃破カウント×2の毎ターンダメ+回復 |
| Eclipse | エクリプス (shield_dagger) | 奇数ターンHP20回復/偶数+勝利時20固定ダメ |
| LoadEmperor | 黙血終王 (sword_axe) | 敗北→次ターンダイス+差値/勝利→会心+500% |
| Silence | 沈黙の余白 (sword_dagger) | 敵全ダイス1固定+勝利時大出血+3 |
| Coronation | 見えざる戴冠者 (axe_dagger) | 被ダメ記録+踏みとどまり→狂戦士化(ダイス+10/蓄積×3固定ダメ毎ターン/会心確定) |
| TheEnd | 終局 (All_weapon) | 戦闘開始時9999軽減不可ダメ |

## 敵スキル16種（EnemyPassiveSkillEffects.cs — 280行）

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

# 7. 武器データ（items.json — 585行）

## レアリティ体系
`ItemRarity` enumとitems.jsonは完全に一致済み:
- `_lv1` = **BRONZE**, `_lv2` = **SILVER**, `_lv3` = **GOLD**, `_lv4` = **LEGENDARY**, `_lv5` = **MYTHIC**
- 合成武器 = **LEGENDARY**, 最上位武器(All_weapon) = **MYTHIC**

## 4武器ロール
| ロール | ID接頭辞 | ダイス構成(Lv1→Lv5) | 特徴 |
|--------|---------|---------------------|------|
| タンク(Shield) | shield_ | 2d3→3d6, crit2 | 低火力・高安定 |
| ナイト(Sword) | sword_ | 2d4→3d7, crit3 | バランス型 |
| バーサーカー(Axe) | Axe_ | 1d5→2d9, crit9 | 高火力・不安定 |
| アサシン(Dagger) | dagger_ | 1d4→2d8, crit7 | 特殊効果特化 |

## 合成武器（Lv5同士の合成、サイズ3×3）
| ID | 名前 | ダイス | crit | スキル |
|---|---|---|---|---|
| shield_sword | セレナ・ドーンブレイカー | 4d7 | 3 | DawnBreaker |
| shield_axe | ブラッドドーン・インペリウム | 3d9 | 4 | BloodMoon |
| shield_dagger | エクリプス | 4d6 | 5 | Eclipse |
| sword_axe | 黙血終王 | 3d9 | 5 | LoadEmperor |
| sword_dagger | 沈黙の余白 | 4d7 | 6 | Silence |
| axe_dagger | 見えざる戴冠者 | 3d8 | 7 | Coronation |

## 最上位武器（サイズ4×4）
| ID | 名前 | ダイス | crit | スキル |
|---|---|---|---|---|
| All_weapon | 終局 | 4d9 | 3 | TheEnd |

## 消費アイテム
| ID | レアリティ | 効果 |
|---|---|---|
| minor_healing_potion | BRONZE | 小回復 |
| healing_potion | SILVER | 回復 |
| greater_healing_potion | GOLD | 大回復 |
| magic_scroll | LEGENDARY | 魔法スクロール |
| full_heal_elixir | LEGENDARY | 全回復エリクサー |

## サイズルール
- 通常武器: 2×3
- 合成武器: 3×3
- 最上位: 4×4

---

# 8. 共通インフラ

### CameraMouseFollow.cs（~251行）→ WASDビューポイントシステム
- WASD入力でInspector設定済みビューポイント間を切り替え
  - A = viewpoint_inv（インベントリ表示）
  - D = viewpoint_pot（ポット表示）
  - W = viewpoint_base（ベース表示）
- **フレームレート非依存補間**: `1 - Mathf.Exp(-moveSpeed * Time.deltaTime)` 指数減衰
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

# 9. 設計パターンまとめ

| パターン | 使用箇所 |
|---------|---------|
| **シングルトン** | CombatManager, InventoryManager, ItemDatabase, PassiveSkillManager, InventorySoundManager, MemoryLeakPreventionFramework |
| **オブジェクトプール** | CoinPoolManager (Queue\<GameObject>) |
| **イベント駆動** | InventoryManager(10), CoinSystemController, CombatManager(3), DiceLEDManager(2) |
| **ScriptableObject DB** | ItemDatabase, ItemLibrary, CoinPhysicsSettings |
| **コルーチン非同期** | CoinDispenser, CoinStackManager, DragDropHandler, GridExpansionManager |
| **MaterialPropertyBlock** | SingleDiceLED (GPU Instancing、draw call最小化) |
| **カスタムOnRenderImage** | CameraFilter.cs（Bloomパイプライン＋コンポジット） |
| **リフレクション設定注入** | CoinSystemManager, CoinSystemSetupManager |
| **固定タイムステップ** | FixedLogicFrameController (60Hz accumulator) |
| **3D空間UI** | ItemPreviewStatusUI, TextRenderer3D, Optimized3DTextRenderer |
| **メモリ安全** | MemoryLeakPreventionFramework, OnDestroy全面クリーンアップ |

---

# 10. データファイル

| ファイル | 場所 | 内容 |
|---------|------|------|
| items.json | Assets/Data/InventorySystem/ (Resources/) | 武器27+消耗5、レアリティはenum完全準拠 |
| enemies.json | Resources/ | 敵データ（floor 1-7） |

---

# 11. 実装済み機能（完了一覧）

### コアシステム
1. ✅ CoinSystem全体（排出・プール・スタック・チケット・変換・支払い・ディスプレイ）
2. ✅ InventorySystem全体（グリッド・D&D・配置・セーブ）
3. ✅ CombatSystem（ダイス戦闘・パッシブスキル全種）
4. ✅ DiceLED（10ダイスLED演出・ローリング・MAX演出・モニター表示）
5. ✅ CombatManager↔DiceLEDManager統合
6. ✅ ItemSellHandler↔CoinSystem連携
7. ✅ ItemUseHandler消費アイテム効果実装
8. ✅ ItemDiscardHandler↔WarningDialog確認ダイアログ
9. ✅ Recovery バグ修正（`ctx.playerDiceMax` 使用に修正済み）

### UI・演出
10. ✅ WASDカメラビューポイント切替（フレームレート非依存補間）
11. ✅ 図鑑プレビューBook背景（スライドイン演出）
12. ✅ プレビュー名TMPテキスト（サイズ反比例スケーリング）
13. ✅ **レアリティグラデーション名前表示**（BRONZE〜MYTHIC 5段階、上半分/下半分色分け）
14. ✅ 詳細情報パネル（RichText、個別カラー、WrapLine）
15. ✅ スキルホバーツールチップ（TMP linkタグ）
16. ✅ アイテム削除UI（ゴミ箱→確認テキスト（アイテム名表示付き）→燃え尽き演出）
17. ✅ BurnDissolve（PerlinNoiseディゾルブ＋エッジグロー＋火の粉パーティクル）
18. ✅ ItemHoldingArea（一時保持、面積降順カード重ね、最大5枚）
19. ✅ **プレビューアイテム世界照明対応**（SetReceiveShadowsRecursively除去）
20. ✅ プレビューサイズ個別設定（1x1〜5x5対応）
21. ✅ ゴミ箱アイコンのカメラ子配置（ブラーRenderTexture背面問題解決）
22. ✅ 削除確認グリッド上シェイクアニメーション
23. ✅ InventoryVisualTester動的アイテムロード

### ポスト処理・シェーダー
24. ✅ **CameraFilter（Vignette + Film Grain + Color Grading）**
25. ✅ **CameraFilter Bloom統合**（5パス: 閾値抽出→ダウンサンプル→ガウシアンブラー→アップサンプル→コンポジット）
26. ✅ **DiceLED HDR Emission連携**（Bloomで光り感向上）
27. ✅ TMP_SDF_Unlit.shader（Premultiplied Alpha色化け解決）

### データ整合性
28. ✅ **items.jsonレアリティ修正**（Common→BRONZE等、ItemRarity enumと完全一致）

---

# 12. 既知のバグ・注意事項

## 未修正バグ
- **enemyDiceDebuff**: `ProcessPostRoll()`内に未使用の敵ダイスデバフコード残骸（無害）

## アーキテクチャ注意
- **二重プレビューシステム**: DragDropHandler.SpinPreviewCoroutine（現在の主システム）と ItemPreviewStatusUI（独立システム、954行）が共存。未連携。統合または役割分離の明確化が必要。

---

# 13. 未実装（TODO）

| 優先度 | 項目 | 詳細 |
|-------|------|------|
| 中 | AutoPlacementManager最適配置 | `TryFindOptimalPlacement()`アルゴリズム未実装 |
| 中 | QuestItemDetectorアイテム逆引き | OnItemRemoved座標→アイテム逆引き |
| 低 | InventorySaveManager暗号化 | プレースホルダーのみ |
| 中 | HoldingArea/Shredder シーン設定 | holdingAreaAnchor, shredderCollider等のInspector設定 |
| 中 | 二重プレビューシステム統合 | DragDropHandler vs ItemPreviewStatusUI の役割整理 |
| 低 | 図鑑プレビューBookプレハブ | previewBookPrefabの3Dモデル/Quad作成 |
| 低 | プレビュー名フォント設定 | previewNameFontにTMP_FontAssetをInspectorで割当 |

---

# 14. 次のセッションでの推奨作業

1. **HoldingArea/ShredderのUnityシーン配置**（GameObjectにコンポーネント追加）
2. **図鑑プレビューBookプレハブ作成**（previewBookPrefab用）
3. **二重プレビューシステムの統合検討**
4. **AutoPlacementManager最適配置アルゴリズム**
5. **バランス調整・テストプレイ**
6. **UI/UXブラッシュアップ**（フォント設定、エフェクト調整等）

---

# 15. 技術メモ

## Shadow Acne（影のストライプ問題）
- **原因**: スポットライトとプレーン面が斜め角でシャドウマップ精度不足
- **解決**: Light → Shadow Type → Normal Bias を 0.4〜1.0 に設定

## ダイス最大値上限
- ダイス最大値の上限は **9**（出目パターンが0-9の10段階）

## HDR Bloomの前提条件
- カメラのHDR=有効
- エミッシブマテリアルが1.0超のカラー値を出力
- CameraFilter.csがカメラにアタッチ済み

## 新スキル追加手順
1. `AllPassiveSkillEffects.cs` にクラス追加（`IPassiveSkillEffect`実装）
2. `PassiveSkillRegistry.cs` に `Register("internalName", new ClassName())` 1行追加
3. `items.json` の武器データに `passiveSkills` 配列でスキル名を追加

## 新機能追加規約
- 新機能はそれぞれの名前空間内に追加
- `[SerializeField]` + `[Header]`でInspector整理

---

*最終更新: 現セッション完了時点*
