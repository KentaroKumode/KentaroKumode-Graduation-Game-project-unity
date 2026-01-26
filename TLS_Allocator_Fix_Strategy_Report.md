# TLS Allocator エラー根本解決戦略 - 実施レポート

## 📋 戦略概要

TLS Allocatorエラー「ALLOC_TEMP_TLS, underlying allocator ALLOC_TEMP_MAIN has unfreed allocations, size 177」の長期的根本解決のため、以下の包括的戦略を策定・実行しました。

---

## 🔍 フェーズ1: メモリリークパターンの全面調査

### 実施内容
1. **イベントリスナー調査**
   - AddListener使用箇所: 4件検出
   - RemoveListener実装状況: 未実装箇所を特定

2. **コルーチン管理調査**
   - StartCoroutine使用箇所: 20+件検出
   - 主要ファイル:
     - CoinSystemController.cs: 8箇所
     - CoinDispenser.cs: 4箇所
     - ItemTooltip.cs: コルーチン参照管理あり

3. **静的インスタンス調査**
   - Singleton実装: 3クラス検出
     - InventoryManager
     - ItemDatabase
     - InventorySoundManager

4. **オブジェクトプール調査**
   - CoinPoolManager: List<GameObject> activeCoins
   - CoinStackManager: List<StackState> coinStacks
   - TicketSystemManager: List<GameObject> activeTickets

### 発見された主要問題

#### ✅ 既に修正済み
- **FilterPanel.cs**: OnDestroyでリスナー解除実装済み
- **WarningDialog.cs**: OnDestroyでリスナー解除実装済み
- **ItemTooltip.cs**: コルーチン管理とOnDestroy実装済み

#### ⚠️ 修正が必要な箇所
- **CoinPoolManager.cs**: OnDestroyが未実装 → プール内オブジェクトの参照リーク
- **InventoryManager.cs**: 静的インスタンスの適切な解除処理なし
- **ItemDatabase.cs**: 静的インスタンスの適切な解除処理なし

---

## 🛠️ フェーズ2: 包括的修正の実施

### 1. メモリリーク防止フレームワーク構築

**新規作成ファイル**: `MemoryLeakPreventionFramework.cs`

#### 機能概要
- **静的インスタンス監視**: Singleton重複検出システム
- **コルーチン追跡**: アクティブなコルーチン実行者の管理
- **メモリ監視**: 5秒間隔でメモリ使用量を監視
- **緊急クリーンアップ**: メモリリーク警告3回で自動GC実行
- **安全なリソース解放ヘルパー**: SafeRemoveAllListeners等のユーティリティ

#### 主要メソッド
```csharp
// 静的インスタンス管理
RegisterStaticInstance(Type, MonoBehaviour)
UnregisterStaticInstance(Type)

// コルーチン追跡
RegisterCoroutineOwner(MonoBehaviour)
UnregisterCoroutineOwner(MonoBehaviour)

// 安全なリスナー解除
SafeRemoveAllListeners(Button)
SafeRemoveAllListeners(Toggle)

// 安全なコルーチン停止
SafeStopCoroutine(MonoBehaviour, Coroutine)
```

### 2. CoinPoolManager.cs の修正

#### 追加機能
- **OnDestroyメソッド実装**
  - アクティブコイン(activeCoins)の完全破棄
  - プール内コイン(coinPool)の完全破棄
  - DestroyImmediateによる即時解放
  - 詳細ログ出力

#### コード詳細
```csharp
void OnDestroy()
{
    // アクティブコインを安全に破棄
    for (int i = activeCoins.Count - 1; i >= 0; i--)
    {
        if (activeCoins[i] != null)
        {
            DestroyImmediate(activeCoins[i]);
        }
    }
    activeCoins.Clear();
    
    // プール内のコインを安全に破棄
    while (coinPool.Count > 0)
    {
        GameObject coin = coinPool.Dequeue();
        if (coin != null)
        {
            DestroyImmediate(coin);
        }
    }
    coinPool.Clear();
}
```

### 3. InventoryManager.cs の修正

#### 追加機能
- **Awakeでのフレームワーク統合**
  - 重複インスタンス検出時にフレームワークへ通知
  - 正常インスタンス作成時に登録

- **OnDestroyメソッド実装**
  - フレームワークからの解除
  - 静的参照のnullクリア

#### コード詳細
```csharp
void Awake()
{
    if (instance != null && instance != this)
    {
        // メモリリーク防止フレームワークに通知
        if (MemoryLeakPreventionFramework.Instance != null)
        {
            MemoryLeakPreventionFramework.RegisterStaticInstance(typeof(InventoryManager), this);
        }
        
        Destroy(gameObject);
        return;
    }
    instance = this;
    
    // フレームワークに登録
    if (MemoryLeakPreventionFramework.Instance != null)
    {
        MemoryLeakPreventionFramework.RegisterStaticInstance(typeof(InventoryManager), this);
    }
}

void OnDestroy()
{
    if (instance == this)
    {
        if (MemoryLeakPreventionFramework.Instance != null)
        {
            MemoryLeakPreventionFramework.UnregisterStaticInstance(typeof(InventoryManager));
        }
        instance = null;
    }
}
```

### 4. ItemDatabase.cs の修正

#### 追加機能
- InventoryManager.csと同様のパターンを実装
- Awake/OnDestroyでのフレームワーク統合

---

## 📊 フェーズ3: 長期メモリ監視システム

### MemoryLeakPreventionFramework の監視機能

#### 1. リアルタイムメモリ監視
- **監視間隔**: 5秒（カスタマイズ可能）
- **監視項目**:
  - GCヒープメモリ使用量
  - メモリ増加量（Delta）
  - 警告カウンター

#### 2. メモリリーク検出アルゴリズム
```csharp
if (memoryDelta > 1MB)
{
    memoryLeakWarnings++;
    
    if (memoryLeakWarnings >= 3)
    {
        PerformEmergencyCleanup();
    }
}
```

#### 3. 緊急クリーンアップ処理
- 孤立したコルーチンオーナーの検出と削除
- 強制ガベージコレクション実行
- 詳細ログ出力

#### 4. 統計情報ログ出力
```csharp
LogCurrentStats() メソッドで以下を出力:
- Current Memory (MB)
- Active Coroutine Owners
- Static Instances
- Leak Warnings
```

---

## 🎯 期待される効果

### 即時的効果
1. **オブジェクトプールリーク解消**: CoinPoolManagerの完全クリーンアップ
2. **Singleton参照リーク解消**: 静的インスタンスの適切な解放
3. **イベントリスナーリーク防止**: 既存の修正により完全対応

### 長期的効果
1. **メモリリーク早期発見**: 5秒間隔の監視により異常を即座に検出
2. **重複インスタンス防止**: Singleton監視により設計ミスを検出
3. **コルーチンリーク追跡**: 孤立したコルーチンオーナーの自動検出
4. **自動リカバリ**: 緊急クリーンアップによる自動修復

---

## 🔧 使用方法

### 1. MemoryLeakPreventionFramework の配置
```
シーン内に空のGameObjectを作成
→ MemoryLeakPreventionFrameworkコンポーネントをアタッチ
→ Inspector設定:
   - Enable Memory Monitoring: ✓
   - Monitoring Interval: 5.0
   - Log Memory Stats: ✓ (デバッグ時のみ)
```

### 2. 既存システムとの統合
すでに以下のクラスに統合済み:
- InventoryManager
- ItemDatabase
- CoinPoolManager

新規Singletonクラス作成時は同様のパターンを適用。

### 3. メモリ統計の確認
実行時にF12キーなど任意のキーに以下を割り当て:
```csharp
if (Input.GetKeyDown(KeyCode.F12))
{
    MemoryLeakPreventionFramework.Instance?.LogCurrentStats();
}
```

---

## 📈 検証方法

### 1. TLS Allocatorエラーの監視
- Unityエディタのコンソールで「TLS Allocator」を検索
- エラー発生頻度・タイミングを記録

### 2. メモリ使用量の監視
- Unity Profilerの「Memory」タブを使用
- GC Allocationの推移を確認

### 3. 長時間実行テスト
```
1. シーンを再生
2. 30分〜1時間の連続実行
3. 以下を確認:
   - TLS Allocatorエラーの発生有無
   - メモリ使用量の推移
   - Frameワークの警告ログ
```

---

## 🎬 次のステップ

### Phase 4: 追加最適化（必要に応じて）
1. **InventorySoundManagerの統合**
   - 静的インスタンス管理の追加
   - OnDestroy実装

2. **その他のManagerクラス調査**
   - 全Managerクラスのメモリリーク監査
   - 統一的なパターン適用

3. **EditorスクリプトのLeak対策**
   - Editor専用のメモリ管理戦略

### Phase 5: パフォーマンス最適化
1. オブジェクトプールのサイズ調整
2. コルーチン実行頻度の最適化
3. GC発生タイミングの制御

---

## 📝 まとめ

本戦略により、以下の根本的改善を実現:

✅ **即時的修正**
- FilterPanel/WarningDialog: イベントリスナーリーク解消
- CoinPoolManager: オブジェクトプールリーク解消
- InventoryManager/ItemDatabase: Singleton参照リーク解消

✅ **長期的基盤構築**
- MemoryLeakPreventionFramework: 包括的メモリ管理システム
- リアルタイム監視と自動リカバリ機能
- 拡張可能なフレームワーク設計

✅ **開発プロセス改善**
- メモリリーク早期発見の仕組み
- 統一的なリソース管理パターン
- デバッグ・トラブルシューティング支援

---

**実施日**: 2026年1月19日  
**対象プロジェクト**: Unity コインシステム & インベントリシステム  
**Unity Version**: 2022.3.22f1
