# Unity コインシステム プロジェクト - AI コーディングガイド

## プロジェクト概要
Unity 2022.3.22f1で開発されたコインの物理シミュレーションシステム。コインの排出、物理的動作、自動整列機能を提供するボードゲーム支援ツール。

## アーキテクチャパターン

### 名前空間設計
- `CoinSystem` 名前空間：すべてのコイン関連機能を分離
- 各クラスは単一責任の原則に従って設計

### 核心コンポーネント
```
CoinDispenser (主制御)
├── CoinBehavior (個別コイン動作)
├── CoinPhysicsSettings (物理設定)
└── CoinDispenserTest (テスト・デバッグ)
```

## 重要な開発パターン

### 1. オブジェクトプールパターン
```csharp
// CoinDispenser内でのプール管理
Queue<GameObject> coinPool = new Queue<GameObject>();
```
- パフォーマンス最適化のためInstantiate/Destroy回避
- `maxConcurrentCoins`で同時生成数制限

### 2. コルーチンベース非同期処理
```csharp
StartCoroutine(DispenseCoinsCoroutine(amount));
StartCoroutine(SortCoinsCoroutine());
```
- UI応答性確保のため長時間処理を分割
- `dispenseInterval`で段階的排出制御

### 3. イベント駆動アーキテクチャ
```csharp
public event Action<int> OnDispenseComplete;
public event Action<int> OnSortComplete;
```

### 4. 物理シミュレーション管理
- Rigidbodyの`isKinematic`切り替えで物理/アニメーション制御
- `CoinBehavior.IsSettled`で静止状態判定

## ファイル構成規則

### スクリプト配置
- `Assets/Scripts/CoinSystem/` - すべてのC#スクリプト
- `Assets/Prefabs/CoinSystem/` - コインプレハブとマテリアル

### 必須コンポーネント依存関係
1. **CoinDispenser** - メインコントローラー
   - `coinPrefab` - コインのプレハブ参照必須
   - `dispenserPoint`, `potTarget`, `stackStartPoint` - Transform参照

2. **CoinBehavior** - 個別コイン制御
   - Rigidbodyコンポーネント必須
   - 自動的に静止判定を実行

## テストとデバッグ

### テスト実行方法
```csharp
// CoinDispenserTest.cs使用
// Space キー: コイン排出テスト
// R キー: リセット
```

### デバッグパターン
- `Debug.Log`でコイン状態追跡
- GUIでリアルタイム状態表示
- `OnGUI()`メソッドでランタイム情報表示

## Unity固有の注意点

### SerializeField使用規則
```csharp
[Header("コイン設定")]
[SerializeField] private GameObject coinPrefab;
```
- privateフィールドをInspectorで編集可能にする
- `[Header]`でInspector整理

### Physics Material設定
- `coin_mat.physicMaterial`でコイン物理特性定義
- 摩擦係数、反発係数をゲームバランスに合わせて調整

### アニメーション制御
```csharp
// 物理とアニメーションの切り替え
rb.isKinematic = true;  // アニメーション時
rb.isKinematic = false; // 物理シミュレーション時
```

## 開発時の重要な考慮事項

1. **パフォーマンス**: オブジェクトプールで最適化済み
2. **物理精度**: `settleThreshold`と`settleTime`で静止判定調整
3. **UI応答性**: コルーチンで長時間処理を分割
4. **モジュラー設計**: 機能ごとに分離されたコンポーネント

## 拡張時の指針
- 新機能は`CoinSystem`名前空間内に追加
- イベントシステムを活用したルーズカップリング
- ScriptableObjectパターンで設定の外部化検討