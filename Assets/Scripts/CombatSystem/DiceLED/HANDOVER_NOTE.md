# DiceLED システム - 開発引き継ぎメモ

## このファイルの使い方
新しいチャットセッションの最初に、このファイルの内容をコピー＆ペーストして
「この引き継ぎメモを読んで、前の作業の続きから開始して」と伝えてください。
作業完了後は削除してOK。

---

## プロジェクト概要
- **Unity 2022.3.22f1** / C# / Built-in Render Pipeline
- **パス**: `c:\Users\kumod\My project\`
- **リポジトリ**: KentaroKumode/KentaroKumode-Graduation-Game-project-unity (branch: main)

## システム構成

### 1. 戦闘システム (CombatSystem)
ダイスベースのターン制戦闘。プレイヤーと敵がダイスを振り、合計値で勝敗判定。

**主要ファイル:**
- `Assets/Scripts/CombatSystem/CombatManager.cs` (634行) - 戦闘制御シングルトン
- `Assets/Scripts/CombatSystem/CombatTestController.cs` (359行) - F1-F4キーテスト用
- `Assets/Scripts/CombatSystem/EnemyData.cs`, `EnemyDatabase.cs` - 敵データ

### 2. インベントリ・パッシブスキルシステム (InventorySystem)
4武器種（盾/剣/斧/短剣）× Lv1-5、各武器に累積パッシブスキル。

**主要ファイル:**
- `Assets/Scripts/InventorySystem/PassiveSkills/PassiveSkillManager.cs` (606行)
- `Assets/Scripts/InventorySystem/PassiveSkills/Effects/AllPassiveSkillEffects.cs` (411行)
- `Assets/Scripts/InventorySystem/PassiveSkills/CombatContext.cs` (190行)
- `Assets/Scripts/InventorySystem/Items/ItemData.cs` - JSON逆シリアライズ
- `Assets/Scripts/InventorySystem/Items/ItemDataV2.cs` - ランタイムデータ(roleName/roleDescription付き)
- `Assets/Scripts/InventorySystem/Items/ItemDatabase.cs` (340行) - ScriptableObject+JSON
- `Assets/Data/InventorySystem/items.json` (410行) - 全武器・アイテムデータ

### 3. DiceLED システム (CombatSystem/DiceLED) ← 今回作成
3×3 LEDグリッドでサイコロの出目を表示する視覚演出システム。

**ファイル構成:**
```
Assets/Scripts/CombatSystem/DiceLED/
├── DiceLEDShader.shader      ← Unlit+Emission+GPU Instancing、アルベドテクスチャ対応
├── SingleDiceLED.cs           ← 1ダイス(9LED)の制御、座標ベース自動マッピング
├── DiceLEDManager.cs          ← 全10ダイス(90LED)統合管理、ローリング演出
├── DiceLEDTest.cs             ← デバッグテストコントローラー
└── Editor/
    └── DiceLEDAutoSetup.cs    ← エディタウィンドウ(Tools→DiceLED Auto Setup)
```

### 4. コインシステム (CoinSystem)
コインの物理シミュレーション。別システム。

---

## DiceLED: 設計と実装済み機能

### パフォーマンス対策（3つの懸念点を解決済み）
| 懸念 | 解決策 |
|------|--------|
| 90個LED明滅の負荷 | MaterialPropertyBlock + isDirtyフラグ + 更新レート制御 |
| ドローコール激増 | 全90個が1マテリアル共有 + GPU Instancing |
| FBX内LED個別制御 | MaterialPropertyBlock.SetColor("_EmissionColor") で個別制御 |

### シェーダー (DiceLEDShader.shader)
- Unlit + Emission + GPU Instancing
- Properties: `_MainTex`(アルベド), `_Color`(メインカラー), `_BaseColor`(消灯色), `_EmissionColor`(発光色)
- 描画: `albedo × (baseCol + emission)` → テクスチャが発光時にも見える

### 出目パターン (SingleDiceLED.cs)
```
0: 全消灯          5: TFT/FTF/TFT(四隅+中央)
1: FFF/FTF/FFF     6: TFT/TFT/TFT(左右列)
2: FFF/TFT/FFF     7: TFT/TTT/TFT(左右列+中央)
3: TFF/FTF/FFT     8: TTT/TFT/TTT(全-中央)
4: TFT/FFF/TFT     9: TTT/TTT/TTT(全点灯)
```

### LED 自動マッピング（座標ベース）
- LED名に依存しない。子Rendererのローカル座標で3×3グリッドを判定
- Z昇順(小Z=上段) → 各行内でX降順(大X=左列)
- `[0][1][2] / [3][4][5] / [6][7][8]` にマッピング

### 命名規則
- サイコロ親: `DICE_1`～`DICE_5`(プレイヤー), `DICE_6`～`DICE_10`(敵)
- LED子: 任意の名前(座標で自動判定)

### エディタ自動セットアップ (Tools→DiceLED Auto Setup)
ワンクリックで:
1. DICE_1～10を検索
2. SingleDiceLEDコンポーネント自動追加
3. 子Rendererを座標ソートでLED割り当て
4. DiceLEDManagerにPlayer/Enemy登録
5. Undo対応

### ローリングアニメーション
1. **高速ローリング**: ランダム出目を高速切替（スローダウンカーブ）
2. **段階的確定**: 1個ずつ順に最終出目を表示
3. **確定フラッシュ**: 消灯↔点灯を繰り返す
4. **全最大値演出**: 出目パターンのまま金色点滅 → ウェーブ復帰 → 金色ブースト

### イベント
- `OnRollingComplete` - アニメーション完了
- `OnAllMax(bool isPlayer)` - 全ダイス最大値検出

### テストコントローラー (DiceLEDTest.cs)
| キー | 機能 |
|------|------|
| Space | ダイスロール（ローリング→確定→勝敗表示） |
| M | 全最大値テスト（Player全MAX演出確認） |
| 0-9 | パターン即時表示 |
| R | リセット |
| C | プレイヤー色切替 |
| ↑↓ | プレイヤーダイス数 増減 |
| ←→ | 敵ダイス数 増減 |

---

## パッシブスキル現状（主要なもの）
- **Ambush(不意打ち)**: 初回ロールでダイス合計+5
- **BlindJustice**: 反撃ダメージ受けた次ターン、ダメージ+10
- **BloodDecree**: ゾロ目で固定ダメージ(軽減不可)+通常ダメージ無効+クリティカル強化
- **VoidStance**: ダイス差≤3で全ダメージ無効+固定3ダメージ
- **DawnBlessing**: 被ダメージ×0.5(切り捨て)
- 全21スキル実装済み

## 既知の未修正バグ
- **Recovery**: reroll時に `Random.Range(1,7)` がハードコード。武器の実際の diceMax を使うべき。
- **enemyDiceDebuff処理**: ProcessPostRollに残っているが現在どのスキルも使用していない(無害)。

---

## 次に考えられる作業
- CombatManagerからDiceLEDManager.PlayRollingAnimationを呼ぶ実際の統合
- FBXモデルとの実際のセットアップ・動作確認
- Bloom(Post Processing)との連携でLED光り感向上
- Recovery バグ修正
- その他バランス調整やUI作業
