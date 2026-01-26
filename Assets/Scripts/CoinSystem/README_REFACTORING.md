# CoinSystem コンポーネント分割について

## 概要
CoinDispenser.cs (2594行) を以下の4つのコンポーネントに分割しました:

1. **CoinPoolManager.cs** (119行) - オブジェクトプール管理
2. **CoinAudioManager.cs** (159行) - 音声管理  
3. **CoinStackManager.cs** (209行) - 積み上げ管理
4. **TicketSystemManager.cs** (140行) - チケットシステム

## 設定の引き継ぎ
CoinSystemManagerが自動的に:
- 各コンポーネントをCoinDispenserと同じGameObjectに追加
- Inspectorで設定した値をリフレクションで各コンポーネントに反映

## 使用方法
既存のシーンは自動的にアップグレードされます:
1. Play モードに入る
2. CoinSystemManagerが新しいコンポーネントを自動追加
3. 設定値が自動的に引き継がれる

## CoinDispenser の今後の作業
以下のメソッドを新しいコンポーネントのラッパーに置き換える必要があります:

### オブジェクトプール関連
- `GetCoinFromPool()` → `poolManager.GetCoinFromPool()`
- `ReturnCoinToPool()` → `poolManager.ReturnCoinToPool()`
- `ReturnAllCoinsToPool()` → `poolManager.ReturnAllCoinsToPool()`

### 音声関連
- `PlayRandomCoinSound()` → `audioManager.PlayRandomCoinSound()`
- `PlayDispensingSound()` → `audioManager.PlayDispensingSound()`
- `PlayStackSound()` → `audioManager.PlayStackSound()`
- `PlayTicketSound()` → `audioManager.PlayTicketSound()`

### 積み上げ関連
- `CreateNewStack()` → `stackManager.CreateNewStack()`
- `GetTotalCoinCount()` → `stackManager.TotalStackedCoins`
- `UpdateCurrentStackIndex()` → `stackManager.UpdateCurrentStackIndex()`

### チケット関連
- `activeTickets` → `ticketManager.ActiveTickets`
- チケット作成/削除ロジック → `ticketManager`

## 利点
- コード可読性向上
- テストしやすさ向上
- 再利用性向上
- 責務の明確化
- 将来の拡張が容易

## 注意事項
現在のCoinDispenser.csには古いコードと新しいコンポーネント参照が混在しています。
段階的に移行することで、既存機能を破壊せずにリファクタリングできます。
