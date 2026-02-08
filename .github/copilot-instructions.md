# Unity ボードゲームプロジェクト - AI コーディングガイド

## プロジェクト概要
Unity 2022.3.22f1で開発されたボードゲーム。コインシステム、グリッドインベントリ、ダイスLED演出、ターンベース戦闘を含む。

## システム構成

### 1. CoinSystem（コイン物理シミュレーション）
- 名前空間: `CoinSystem`
- 場所: `Assets/Scripts/CoinSystem/`
- オブジェクトプール、コルーチン非同期、イベント駆動
- CoinDispenser→CoinBehavior→CoinPhysicsSettings

### 2. InventorySystem（グリッドインベントリ＋装備）
- 名前空間: `InventorySystem`
- データ: `Assets/Data/InventorySystem/items.json`
- スクリプト: `Assets/Scripts/InventorySystem/`

### 3. DiceLED（ダイス演出）
- DiceLEDManager: ダイスロール演出制御（~730行）
- DiceMonitorDisplay: ダイス合計値モニター表示（~530行）
  - アスペクト比安全レンダリング、ベベル/角丸、LCD効果
- 機能: MAX値延長ローリング（サイド独立）、モニター連動

### 4. PassiveSkillSystem（パッシブスキル）
- `Assets/Scripts/InventorySystem/PassiveSkills/`
  - `IPassiveSkillEffect` — インターフェース（SkillId, Triggers[], Execute）
  - `PassiveSkillTrigger` — 22種のトリガーenum
  - `PassiveSkillManager` — シングルトン、トリガー発火管理
  - `PassiveSkillRegistry` — internalName→実装の静的マッピング
  - `CombatContext` — 戦闘状態コンテナ（187行）
  - `Effects/AllPassiveSkillEffects.cs` — 全スキル実装（~664行）

## 戦闘システム仕様

### 会心判定
- ダイス1個を振り、criticalRate以上なら会心発動

### ダメージフロー
1. OnPreRoll → OnPostRoll → 勝敗判定
2. OnRollWin/OnRollLose → OnPreDealDamage/OnPreReceiveDamage
3. OnCriticalCheck → OnCriticalDamage
4. OnPostDealDamage/OnPostReceiveDamage → OnPrePursuitDamage
5. OnTurnEnd

### CombatContext主要フィールド
- playerDice[], enemyDice[], playerDiceTotal, enemyDiceTotal, diceDifference
- finalDamage, pursuitDamage, fixedDamageToEnemy
- criticalBonus, criticalMultiplier, isCritical
- nullifyAllDamage, nullifyPursuitDamage
- accumulatedValues（スキル蓄積）, nextTurnBuffs/currentBuffs
- pendingDiceOverrides（敵ダイス固定）
- enemyBleedStacks, overDamageAccumulated
- playerCurrentHP, playerMaxHP, enemyCurrentHP, enemyMaxHP

## 武器・スキルデータ（items.json）

### 武器ツリー（各Lv1-5、スキル累積）
| 系統 | ロール | Lv5ステータス |
|---|---|---|
| 盾(shield) | タンク | 3d6, crit2 |
| 剣(sword) | ナイト | 3d7, crit3 |
| 斧(Axe) | バーサーカー | 2d9, crit9 |
| 短剣(dagger) | アサシン | 2d8, crit7 |

### 合成武器（Lv5同士の合成、スキル1個、サイズ3×3）
| ID | 名前 | ダイス | crit | スキル |
|---|---|---|---|---|
| shield_sword | セレナ・ドーンブレイカー | 4d7 | 3 | DawnBreker — ダイス差≤4でダメ0化+10固定+HP10回復 |
| shield_axe | ブラッドドーン・インペリウム | 3d9 | 4 | BloodMoon — 撃破カウント×2の毎ターンダメ+回復 |
| shield_dagger | エクリプス | 4d6 | 5 | Eclipse — 奇数ターンHP20回復/偶数+勝利時20固定ダメ |
| sword_axe | 黙血終王 | 3d9 | 5 | LoadEmperor — 敗北→次ターンダイス+差値/勝利→会心+500% |
| sword_dagger | 沈黙の余白 | 4d7 | 6 | Silence — 敵全ダイス1固定+勝利時大出血+3 |
| axe_dagger | 見えざる戴冠者 | 3d8 | 7 | Coronation — 被ダメ記録+踏みとどまり→狂戦士化(ダイス+10/蓄積×3固定ダメ毎ターン/会心確定) |

### 最上位武器（サイズ4×4）
| ID | 名前 | ダイス | crit | スキル |
|---|---|---|---|---|
| All_weapon | 終局 | 4d9 | 3 | TheEnd — 戦闘開始時9999軽減不可ダメ |

### サイズルール
- 通常武器: 2×3
- 合成武器: 3×3
- 最上位: 4×4

## 実装済みプレイヤースキル一覧

### 盾系
- Breakfall: 被ダメ-2
- SpikeArmor: 毎ターン敵に軽減不可2ダメ
- Endurance: 敗北時MaxHP+1(上限20)
- DivineShield: ターン終了時HP+2
- DawnBlessing: 敗北時被ダメ50%

### 剣系
- BasicSword: ダイス合計≥ダイス数×2保証
- Recovery: 最低ダイス振り直し
- WandererWit: ダイス差≤1で追撃無効
- DragonSlayer: ダイス差≤2で会心ダイス+2
- VoidStance: ダイス差≤3で両者ダメ0+3固定ダメ

### 斧系
- PainRevert: 勝利時、減少HP/2ダメ追加
- Warcry: 敗北でダイス+1蓄積、勝利でリセット
- BloodPact: 敗北時、次ターンダメ+3
- ApexPredator: 敗北時、追撃無効
- BloodDecree: ゾロ目→合計値を固定ダメ+会心+200%+会心ダイス+5

### 短剣系
- Ambush: 初回ロールダイス+5
- FatalStab: 会心ダメ+100%
- Sting: ダメ付与時出血+1(1ターン1回)
- Execution: 勝利時、次ターン敵最小ダイス1固定
- BlindJustice: 反撃被ダメ時、次ターンダメ+10
- Nightfall: オーバーダメ×2蓄積→戦闘開始時に放出

## 開発規約
- 新スキル追加: AllPassiveSkillEffects.csにクラス追加→PassiveSkillRegistry.csにRegister1行
- 新機能はそれぞれの名前空間内に追加
- SerializeField + [Header]でInspector整理
- ダイス最大値の上限は9