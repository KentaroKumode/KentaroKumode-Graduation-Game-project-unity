# インベントリシステム - 3Dカード統合 簡潔セットアップガイド

## 🚀 自動セットアップ（1クリック）

### STEP 1: 自動セットアップ実行
1. Unity Editor で `Tools` → `Inventory System` → `Auto Setup`
2. 表示されたウィンドウで `🚀 完全自動セットアップ実行` をクリック
3. 完了ダイアログが表示されるまで待機

**自動実行される内容:**
- フォルダ構造作成
- items.json作成  
- ItemAssetDatabase作成
- テストシーン作成
- システムオブジェクト配置
- テストスクリプト配置
- メモリ監視システム配置

---

## 🎴 手動作業（必須）

### STEP 2: 3Dカードプレハブの作成

**各サイズごとに実行（9回繰り返し）:**

1. **Empty作成:** Hierarchy で右クリック → `Create Empty` → 名前を `ItemCard_1x1` に変更
2. **3Dモデル追加:** 準備した3Dモデルを子にドラッグ&ドロップ
3. **Collider追加:** `Add Component` → `Physics` → `Mesh Collider`
   - `Mesh` に子の3Dモデルのメッシュを設定
   - `Convex` をチェック
4. **プレハブ化:** `Assets/Prefabs/InventorySystem/Cards/` にドラッグ&ドロップ
5. **削除:** Hierarchyから削除

**作成するプレハブ:**
| サイズ | プレハブ名 | Box Collider Size (等倍基準) |
|--------|------------|------------------------------|
| 1x1 | ItemCard_1x1 | (1.0, 0.1, 1.0) |
| 1x2 | ItemCard_1x2 | (1.0, 0.1, 2.0) |
| 1x3 | ItemCard_1x3 | (1.0, 0.1, 3.0) |
| 2x1 | ItemCard_2x1 | (2.0, 0.1, 1.0) |
| 3x1 | ItemCard_3x1 | (3.0, 0.1, 1.0) |
| 2x2 | ItemCard_2x2 | (2.0, 0.1, 2.0) |
| 2x3 | ItemCard_2x3 | (2.0, 0.1, 3.0) |
| 3x2 | ItemCard_3x2 | (3.0, 0.1, 2.0) |
| 3x3 | ItemCard_3x3 | (3.0, 0.1, 3.0) |

### STEP 3: ItemAssetDatabaseマッピング設定

1. `Assets/Data/InventorySystem/ItemAssetDatabase` を選択
2. Inspector で各要素の `Card Model` フィールドに対応するプレハブをドラッグ&ドロップ:

| Index | Item Id | Card Model |
|-------|---------|------------|
| 0 | sword_small | ItemCard_1x1 |
| 1 | sword_long | ItemCard_1x2 |
| 2 | spear | ItemCard_1x3 |
| 3 | hammer | ItemCard_2x1 |
| 4 | greatsword | ItemCard_3x1 |
| 5 | shield | ItemCard_2x2 |
| 6 | tower_shield | ItemCard_2x3 |
| 7 | plate_armor | ItemCard_3x2 |
| 8 | magic_scroll | ItemCard_3x3 |

---

## 🎮 テスト実行

### STEP 4: 動作確認
1. `InventoryTestScene` を開く
2. `Play` ボタンをクリック
3. キーボード操作でテスト:
   ```
   SPACE  : 次のアイテムを順番に生成
   A      : 全アイテムを一度に生成
   1-9    : 特定のアイテムを生成
   R      : 全クリア
   ```

### ✅ 完了チェック
- [ ] 自動セットアップ実行済み
- [ ] 9個のプレハブ作成済み
- [ ] ItemAssetDatabaseマッピング設定済み
- [ ] Play時にエラーなく動作

---

## 💡 補足情報

**プレハブ作成のコツ:**
- 全て等倍(1,1,1)で作成
- ピボットは左上角に設定済みの前提
- MeshColliderの場合は必ずConvexをチェック

**トラブルシューティング:**
- エラーが出る場合：コンソールを確認
- アイテムが表示されない場合：ItemAssetDatabaseのマッピングを再確認
- TLS Allocatorエラー：画面左上のモニターで頻度確認

これで完了です！