# コードベース全体リスク監査メモ（2026-11-27）

- **対象範囲**: `Assets/Scripts/CombatSystem/`, `GameLoop/`, `ItemSystem/`, `AutoTest/`, `UI/`（読み取り専用スキャン）
- **前提**: `SwapPerspective` 自体の廃止は `docs/perspective-swap-removal-approval.md` にて別途進行中のため、それ以外の構造・実装リスクを中心に整理。

## 評価基準（各 10 段階）

- **発生確率（1〜10）**
  - `10`: 高確率で発生する／既に発生している
  - `5`: 特定の条件・操作（途中リタイア、特定ボス×特定秘宝の組み合わせ等）で発生する
  - `1`: コード理論上発生する疑いがあるが、極小もしくは通常経路では理論上発生しない
- **被害規模（1〜10）**
  - `10`: ラン結果が変わる、もしくは同条件でも比較を破壊しかねない
  - `5`: 一部アイテムの仕様ズレ・計算誤差、または稀なクラッシュ／進行不能
  - `1`: ログや処理の遅延、エラー表示や warn にとどまる

---

## リスク一覧サマリ（優先度順・AutoTest 除くゲーム本体＋UI）

| # | 項目 | 発生確率 | 被害規模 | 積 (確×被) | 該当箇所 |
|---:|---|---:|---:|---:|---|
| 1 | `Kw.DamageEnemy` 直接経路でのヴェスカ踏みとどまり（66%/33%）漏れ | **6** | **9** | **54** | `Keywords.cs:254`, `CombatManager.Turn.cs` |
| 2 | HP 二重管理（`CombatManager` 手元値 vs `CombatContext` の手動同期） | **4** | **9** | **36** | `CombatManager.Turn.cs` 全般, `PoisonEffects.cs:50` |
| 3 | グローバル可変 `static` 状態の戦闘・ラン跨ぎ残留リスク | **3** | **10** | **30** | `TenyoHeavy.cs`, `BossDown.cs`, `LethalParry.cs`, `GameHooks.cs` |
| 4 | `RunSave` 厳格スキーマ一致によるパッチ時の中断セーブ全破棄 | **6** | **5** | **30** | `RunSave.cs:309, 316` |
| 5 | `#if UNITY_EDITOR` でメタ進行セーブ読込経路がバイパスされる（QA 盲点） | **10** | **3** | **30** | `MetaProgressManager.cs:96-102` |
| 6 | 画面遷移コールバックの例外完全握りつぶし（無ログ） | **3** | **8** | **24** | `LcdTransition.cs:114` |
| 7 | UI 静的ロックフラグの残留による入力不能リスク | **3** | **7** | **21** | `Game2DBattleFx.cs`, `Game2DLayerBanner.cs`, `Game2DModal.cs`, `Game2DPhaseScreens.cs` |
| 8 | シールド増加経路のバイパス（`StatModifierEffect` の `ctx.consShield +=`） | **5** | **4** | **20** | `StatModifierEffect.cs:400` |
| 9 | `CS1717` 自己代入警告の放置（4 箇所） | **10** | **1** | **10** | `CombatManager.cs:208`, `CombatManager.Turn.cs:1254, 1468, 1626` |
| 10 | 敵パッシブでの `ctx.enemyMaxHP` ゼロ除算ガード抜け | **1** | **7** | **7** | `EnemyPassiveSkillEffects.cs:808, 947, 1140, 1174` |
| 11 | `Debug.Log` の `logEnabled` ガード漏れ（一部） | **7** | **1** | **7** | `CombatManager.Turn.cs:1543` 等 |
| 12 | 手書き JSON パーサ（`FxJson`）の境界・書式チェック欠如 | **1** | **6** | **6** | `FxJson.cs:18-62` |
| 13 | 経路探索コストの定数 `1000` 暗黙上限（`% 1000` / `/ 1000`） | **1** | **6** | **6** | `FreeMapSim.cs:624-647`, `TerritoryBypass.cs:177` |

---

## 各項目の詳細

### 1. `Kw.DamageEnemy` 直接経路でのヴェスカ踏みとどまり（66%/33%）漏れ
- **発生確率**: `6 / 10`（ヴェスカ戦 × `OnTurnEnd` 反射・〈鏡返しの小盾〉・〈瘴気の外套〉等で `ResolvingKey == 0` のタイミングに閾値を跨ぐと発生）
- **被害規模**: `9 / 10`（踏みとどまりを貫通して形態移行や撃破ターンが変わり、ラン結果に直結）
- **該当**: `Assets/Scripts/ItemSystem/PassiveSkills/Keywords.cs:254`
- **対策**: `docs/perspective-swap-removal-approval.md` 項目 3 で対応予定（`Kw.DamageEnemy` の直接減算経路に `DivineTalent.ClampToThreshold` を通す）。

### 2. HP 二重管理（`CombatManager` 手元値 vs `CombatContext` の手動同期）
- **発生確率**: `4 / 10`（既存経路はほぼ同期済みだが、新規パッシブや処刑・特殊効果を追加した際に同期漏れが再発しやすい）
- **被害規模**: `9 / 10`（与えたダメージや回復が上書きされて消失、または撃破判定がズレてラン結果が変わる）
- **該当**: `Assets/Scripts/CombatSystem/CombatManager.Turn.cs`（`SyncHPFromContext` と `ctx.enemyCurrentHP = enemyHP` の往復）、`Assets/Scripts/ItemSystem/PassiveSkills/Effects/PoisonEffects.cs:50`
- **対策**: 敵 HP 減算・処刑は原則 `Kw.DamageEnemy` または専用ヘルパー 1 本に集約し、直接代入を禁止する。

### 3. グローバル可変 `static` 状態の戦闘・ラン跨ぎ残留リスク
- **発生確率**: `3 / 10`（通常の正常終了フローではリセットされるが、途中リタイア・セーブ＆ロード・例外中断・バッチ計測の異常終了時に残留し得る）
- **被害規模**: `10 / 10`（前ラン／前戦闘のフラグや倍率が次ランに持ち越されると、同条件・同一シード比較が静かに壊れる）
- **該当**:
  - `Assets/Scripts/CombatSystem/TenyoHeavy.cs:31-39`（`Pending`, `Last`, `QaForce`）
  - `Assets/Scripts/CombatSystem/BossDown.cs`（`ActiveNow`, `TakenMulNow`, `TakenBonusPctNow` 等）
  - `Assets/Scripts/CombatSystem/LethalParry.cs`（`InvincibleThisTurn` 等）
  - `Assets/Scripts/GameLoop/GameHooks.cs`（各種測定フック）
- **対策**: 戦闘開始時（`StartCombat`）およびラン開始時（`StartNewRun`）の冒頭で全 `static` 戦闘状態を一括初期化する単一リセット関数を置く。

### 4. `RunSave` 厳格スキーマ一致によるパッチ時の中断セーブ全破棄
- **発生確率**: `6 / 10`（リリース後に `RunState` へフィールドを 1 つでも追加・削除・改名すると必ず発生）
- **被害規模**: `5 / 10`（メタ進行は無事だが、プレイヤーの進行中ランの中断セーブがすべて読めなくなり破棄される）
- **該当**: `Assets/Scripts/GameLoop/RunSave.cs:309, 316`
- **対策**: 「半分戻ったランを作らない」安全設計自体は堅実。もしリリース後も中断セーブを維持したい場合は、新フィールドに既定値フォールバックを許すマイグレーション口を用意する。

### 5. `#if UNITY_EDITOR` でメタ進行セーブ読込経路がバイパスされる（QA 盲点）
- **発生確率**: `10 / 10`（Editor 起動時は毎回 `PlayerPrefs` を消去し、`BackedPrefs.Load` やマイグレーション経路を通らず `tokens = 3000` で開始）
- **被害規模**: `3 / 10`（実機挙動自体は正常だが、セーブ破損復帰やマイグレーションの不具合を実機ビルドまで検知できない）
- **該当**: `Assets/Scripts/MetaProgression/MetaProgressManager.cs:96-102`
- **対策**: リリース前チェックリストに「スタンドアロンビルドでのセーブ→再起動→ロード確認」を必ず入れる（または Editor 用トグルで実機経路をテスト可能にする）。

### 6. 画面遷移コールバックの例外完全握りつぶし（無ログ）
- **発生確率**: `3 / 10`（遷移先画面の初期化や `StartRun` 等で例外が起きた際に発生）
- **被害規模**: `8 / 10`（ログすら出ずに暗転明けが壊れた画面・進行不能になり、原因特定が極めて困難）
- **該当**: `Assets/Scripts/UI/Lcd/LcdTransition.cs:114`（`try { onMidpoint?.Invoke(); } catch { }`）
- **対策**: `catch (System.Exception e) { Debug.LogException(e); }` に変更し、最低限エラーログを残す。

### 7. UI 静的ロックフラグの残留による入力不能リスク
- **発生確率**: `3 / 10`（通常の `OnDestroy` では戻されるが、非アクティブ化・演出途中の例外・シーン強制切替などで `true` が残留し得る）
- **被害規模**: `7 / 10`（演出待ちやモーダル判定が永続 `true` になり、プレイヤーの操作を受け付けなくなる）
- **該当**: `Game2DBattleFx.Showing`, `Game2DLayerBanner.Busy`, `Game2DModal.Active`, `GameManager.EventKeysByScreen`
- **対策**: `OnDisable` でもリセットするか、インスタンス生存に紐づいたプロパティ（`_inst != null && _inst.gameObject.activeInHierarchy`）へ寄せる。

### 8. シールド増加経路のバイパス（`StatModifierEffect` の `ctx.consShield +=`）
- **発生確率**: `5 / 10`（`StatKeys.ShieldStart` を持つ秘宝・ステータス適用時に毎回通る）
- **被害規模**: `4 / 10`（`Rw.Shield` 経由の `shieldGainPct` 補正等を通らない。開幕シールドなので戦闘中 1.5 倍と競合しにくいが、仕様統一の観点でズレが生じ得る）
- **該当**: `Assets/Scripts/ItemSystem/PassiveSkills/Effects/StatModifierEffect.cs:400`
- **対策**: 意図的に `shieldGainPct` を乗せない仕様ならコメント明記、乗せるなら `Rw.Shield` 経由に統一。

### 9. `CS1717` 自己代入警告の放置（4 箇所）
- **発生確率**: `10 / 10`（毎ビルドで必ず警告が出る）
- **被害規模**: `1 / 10`（実行時動作への影響ゼロ。ただし警告ノイズで新規の重要警告を見落としやすくなる）
- **該当**:
  - `Assets/Scripts/CombatSystem/CombatManager.cs:208`（`amount = amount;`）
  - `Assets/Scripts/CombatSystem/CombatManager.Turn.cs:1254`（`reflect = reflect;`）
  - `Assets/Scripts/CombatSystem/CombatManager.Turn.cs:1468`（`takenDmg = takenDmg;`）
  - `Assets/Scripts/CombatSystem/CombatManager.Turn.cs:1626`（`secTaken = secTaken;`）
- **対策**: 4 行を削除。

### 10. 敵パッシブでの `ctx.enemyMaxHP` ゼロ除算ガード抜け
- **発生確率**: `1 / 10`（`enemies.json` の全敵は `maxHP > 0` のため通常プレイ・通常計測では発生しない。テスト用ダミー敵や異常データでのみ発生）
- **被害規模**: `7 / 10`（発生時は `DivideByZeroException` または `NaN` 伝播で該当スキルが失敗・誤動作）
- **該当**: `Assets/Scripts/ItemSystem/PassiveSkills/Effects/EnemyPassiveSkillEffects.cs:808, 947, 1140, 1174`（同じファイルの `L1091` は `> 0` ガード済み）
- **対策**: `System.Math.Max(1, ctx.enemyMaxHP)` で統一ガード。

### 11. `Debug.Log` の `logEnabled` ガード漏れ（一部）
- **発生確率**: `7 / 10`（該当効果が発動するたびに文字列生成・`Debug.Log` 呼び出しが走る）
- **被害規模**: `1 / 10`（ログ抑止時の微小な GC・CPU オーバーヘッドのみ。挙動への影響なし）
- **該当**: `Assets/Scripts/CombatSystem/CombatManager.Turn.cs:1543` 等
- **対策**: `if (Debug.unityLogger.logEnabled)` でガード。

### 12. 手書き JSON パーサ（`FxJson`）の境界・書式チェック欠如
- **発生確率**: `1 / 10`（正規のビルド済み FX JSON では発生しない。手編集や破損ファイル時のみ）
- **被害規模**: `6 / 10`（末尾切れや不正 JSON で `IndexOutOfRangeException` または無限ループ。呼び出し元 `FxSheet.Loading.cs:62` で例外は捕捉されているが無限ループは止まらない）
- **該当**: `Assets/Scripts/UI/Game2D/Battle/FxJson.cs:18-62`
- **対策**: `while (i < s.Length)` の境界チェックを足す。

### 13. 経路探索コストの定数 `1000` 暗黙上限（`% 1000` / `/ 1000`）
- **発生確率**: `1 / 10`（現状マップは ~50 ノードなので理論上到達しない）
- **被害規模**: `6 / 10`（将来マップ規模を大きくした場合、歩数が 1000 を超えると縄張り踏破数と歩数が桁あふれで壊れる）
- **該当**: `Assets/Scripts/MapSystem/FreeMove/FreeMapSim.cs:624-647`, `Assets/Scripts/MapSystem/FreeMove/TerritoryBypass.cs:177`
- **対策**: 現状サイズなら問題ないが、定数化＋ `Assert(n < 1000)` があると安全。

---

## 健全性が確認できた領域（問題なし・全 311 ファイル確認）
- **静的イベント購読リーク**: `OnCombatEnd`, `OnCue`, `OnShow`, `OnWeaponTierUpgraded` すべて `OnDisable` / `OnDestroy` で対の `-=` が実装済み。
- **GPU リソース（`Texture2D`）管理**: `Game2D` 全体で一時 `Texture2D` は即時 `Destroy`、常駐テクスチャも `OnDestroy` で破棄済み（リークなし）。
- **`Update` / `LateUpdate` / `OnGUI` の重処理**: `FindObjectOfType`・`Resources.Load`・`new Texture2D`・`new GUIStyle` の毎フレーム呼び出しは **0 件**。
- **`Time.timeScale = 0` でのコルーチンフリーズ罠**: `WaitForSeconds` は **0 件**（`Time.unscaledDeltaTime` を使用）。
- **シングルトン直参照**: Effects / UI ともに null チェック無し参照は **0 件**。
- **ゲーム本体の乱数決定論**: `GameRng.cs:102` のマスターシード生成以外に `UnityEngine.Random` / `new System.Random` の直呼びは **0 件**。

---

## 実コードでの確認結果（2026-11-27・Claude）

このメモは提言として扱い、各項目を実コードで確かめた。判定と根拠:

| # | 判定 | 根拠 |
|---:|---|---|
| 1 | **対応済み** | `ef15e007` で `Kw.DamageEnemy` の直接の口に踏みとどまりを掛けた（回復はさせない）。このメモはその前の状態を見ている |
| 2 | 将来の負債としては妥当・**今の不具合ではない**（被害 9・確率 4 は過大） | 例の〈遅効の毒刃〉は、解決中なら印だけ立てて `CombatManager` 側で処刑し、そうでなければ ctx を直接書いてターンの終わりの同期で拾われる。敵のトリガーの後の HP の同期も全経路で確かめた（視点の入れ替えの監査）。勧め: 「敵の HP を減らす・倒すのは `Kw.DamageEnemy` か `CombatManager` の口だけ」を方針として書き残す程度 |
| 3 | **根拠が弱い**（被害 10 は過大） | 名前の挙がった値は新しい戦闘の始めに戻している（`TenyoHeavy.Reset`・`LethalParry.ResetCombat`・`BossDown` は `ResetBossDown` / `EndBossDown`）。`GameHooks` は測定の設定で、残るのが仕様。別々にビルドした 8 つの版が同じシードの 1 万ランで全件一致し、同じ版の 300 ラン × 2 回も全件一致した（ランをまたぐ汚染があればずれる） |
| 4 | 記述は正しいが**今は仕様** | `RunSave.RestoreRun` は欄の過不足・型違いで例外を出し、半端な復元をしない。発売前は後方互換を持たない方針（CLAUDE.md）。発売後に欄を変える時に移行の口を作る、で足りる |
| 5 | **妥当・低** | エディタでは毎回メタ進行を初期化している（UI 開発の便宜）。実機の保存→再起動→読み込みは、リリース前の確認の手順に入れる |
| 6 | **妥当・修正済み** | `LcdTransition.cs:114` の `catch { }` はログも出さずに例外を捨てていた。`Debug.LogException` を出すように直した（2026-11-27・画面だけの経路なので BOT の測定には関係しない） |
| 7 | **一部妥当・低** | `Game2DBattleFx.Showing` は `OnDisable` でも戻している。`Game2DLayerBanner.Busy`・`Game2DModal` は `OnDestroy` だけで戻す。常駐の画面部品なので無効化だけされる経路は少ない |
| 8 | **誤り** | `consShield` の書き込み口（setter）の中で `shieldGainPct` を掛け、出来事も配っている。`+=` もこの口を通る |
| 9 | **妥当・修正済み** | 4 行を削除。同じシードの 1 万ランで基準と全件一致を確かめた |
| 10 | **誤り** | 4 か所とも直前の行に `enemyMaxHP > 0` / `<= 0 return` の守りがある（`EnemyPassiveSkillEffects.cs:806/946/1137/1173`） |
| 11 | 妥当・**優先度は最下位** | ほぼ守られている。測定の速さへの影響も小さい |
| 12 | **一部誤り** | 途中で切れた入力では添字が範囲外になって例外が出る（呼び元で捕まる）。各ループは添字を進めるので、無限ループにはならない。書き出し済みの FX の JSON だけを読むので、実害は無い |
| 13 | 妥当・**理論上のみ** | `FreeMapSim.cs:647` は歩数と縄張りの踏破数を 1 つの数に詰めている。マップは約 50 点なので届かない。マップを大きくする時に直す |
