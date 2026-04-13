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

---

## 調整履歴（2026-04-14）

### バグ修正
1. **fixedDamageToEnemy逆適用バグ** — 敵勝利ブランチで`fixedDamageToEnemy`がプレイヤーHPから引かれていた → 敵HPから引くよう修正
2. **enemyDiceDebuff二重適用バグ** — ProcessPostRollとCombatManagerの両方で適用されていた → CombatManager側の重複コードを削除
3. **SwapPerspective不完全** — `fixedDamageToPlayer`フィールドを新設し、SwapPerspectiveで`fixedDamageToEnemy ↔ fixedDamageToPlayer`を入れ替えるよう修正。CombatManagerの全ブランチ（勝利/敗北/引分）で両フィールドを正しく適用
4. **ApplyScratchModifiers未呼び出し** — ScratchAuraがセットしたscratchDamageにCursedFog(呪霧)の2倍補正が適用されていなかった → CombatManagerのscratch適用直前にApplyScratchModifiers()呼び出しを追加
5. **ApplyBleedModifiers未呼び出し** — 出血ダメージにBloodTide(血潮)の2倍補正が適用されていなかった → 全3ブランチにApplyBleedModifiers()呼び出しを追加

### Scratch（威圧）システム移行
- Scratchをコア戦闘メカニクスから**敵専用パッシブ「ScratchAura（威圧）」**に移管
- CombatManagerの自動scratch計算(`ctx.scratchDamage = Max(0, threat - diff)`)を削除
- ScratchAuraは`OnRollLose`（敵視点 = プレイヤー勝利時）に発火し、`scratch = max(0, enemyThreat - |diceDiff|)`をセット
- **4層以上の敵**にのみScratchAuraを付与（1〜3層は威圧なし）
- 引き分け時のscratch計算も完全削除（A案採用）
- Parryの説明を「敵の威圧による削りダメージを無効化」に更新

### 出血システム整備
- **Sting（出血）パッシブ新設** — OnPostDealDamage時に`enemyBleedStacks++`（1ターン1回制限）
- 斧T2にStingを配置（FrenzyはT3以降に移動）、斧T4からStarFateを削除
- CombatContext.BeginNewTurn()に出血スタック減衰(`enemyBleedStacks--`)を追加

### パッシブ数値調整
- **追撃（Pursuit）を倍増**: I: +1→**+2**, II: +2→**+4**, III: +3→**+6**

### 名称変更
| 変更前 | 変更後 | 対象 |
|---|---|---|
| 不死身 (Undying) | **不死者** | 敵パッシブ名 |
| 受けるダメージ-1 | **毎ターンHP+1回復** | Undyingの効果（OnPreReceiveDamage→OnTurnStart） |
| ジャッジメント (Judgement) | **裁定** | ダイスパッシブ表示名 |
| 刺那の惜別 (Abyss) | **刹那の惜別** | 呪い武器パッシブ表示名（誤字修正） |

### CombatContext追加フィールド
- `fixedDamageToPlayer` — 敵→プレイヤーへの軽減不可固定ダメージ（BeginNewTurnで0リセット）

### 現在の実装済みパッシブ一覧（54個）

#### 汎用パッシブ（6種×3段階=18個）
| 内部名 | 表示名 | 効果 |
|---|---|---|
| PursuitI〜III | 追撃I〜III | 与ダメージ+2/+4/+6 |
| CounterI〜III | 反撃I〜III | 敗北時、敵に軽減不可1/2/3ダメ |
| MightI〜III | 筋力I〜III | 各ダイス出目+1/+2/+3 |
| FortitudeI〜III | 頑強I〜III | 被ダメージ-1/-2/-3 |
| InsightI〜III | 心眼I〜III | 会心ダイス+1/+2/+3 |
| VitalityI〜III | 活力I〜III | ターン開始時HP+1/+2/+3回復 |

#### 武器ユニーク（15個）
| 内部名 | 表示名 | 系統 | 効果 |
|---|---|---|---|
| Parry | パリィ | 盾 | 敵の威圧による削りダメージを無効化 |
| HolyShield | 聖なる守り | 盾 | 敗北時、被ダメ50%軽減 |
| Riposte | 切り返し | 剣 | 敗北時、受けたダメ50%を敵に反射 |
| VoidStance | 虚空 | 剣 | ダイス差≤3で双方ダメ0+軽減不可3ダメ |
| Frenzy | 復讐 | 斧 | 敗北でダイス+1蓄積、勝利でリセット |
| BloodDecree | 血令 | 斧 | ゾロ目→合計値固定ダメ+会心+200%+会心ダイス+5 |
| Sting | 出血 | 斧 | ダメ付与時、敵に出血+1（1ターン1回） |
| Execute | 処刑 | 短剣 | 勝利時、次ターン敵最小ダイス1固定 |
| Nightfall | 蝕夜 | 短剣 | オーバーダメ×2蓄積→次戦闘開始時放出 |
| Ignite | 業火 | デッドエンド | 戦闘開始時炎上（3T, 毎ターン3ダメ） |
| HolyMemory | 黎明の光 | 聖剣 | 初回ロール時ダイス+3 |
| HolyAura | 薄暮の光 | 聖剣 | ターン開始HP+2、敗北時被ダメ50% |
| Terminus | 終焉 | 聖剣 | 戦闘開始時、敵MaxHP30%軽減不可ダメ |
| CurseBind | 呪縛 | 呪い | 毎ターン自傷1、敵ダイス-1蓄積デバフ |
| Abyss | 刹那の惜別 | 呪い | 被ダメ記録+踏みとどまり→狂戦士化 |

#### ダイス固有（7個）
| 内部名 | 表示名 | 効果 |
|---|---|---|
| Shimmer | 煌玉 | 最大出目ダイスあり→会心+1 |
| ReversalFlame | 盟約 | 敗北時、次Tダイス合計+2 |
| Steadfast | 堅実 | 合計≤(ダイス数×3)で+2 |
| StarFate | 星命 | ゾロ目時、追撃+出目値 |
| Destiny | 運命 | 全最大出目→与ダメ×2、全最低→被ダメ0 |
| Starguide | 星導 | 全ダイス異なる値→合計+3 |
| Judgement | 裁定 | 自ダイス≥敵2倍→追撃+5、敵≥自2倍→被ダメ0 |

#### 敵専用（14個）
| 内部名 | 表示名 | 層 | 効果 |
|---|---|---|---|
| Trapper | 罠師 | 1-3 | 勝利時、次T相手ダイス-1 |
| Undying | 不死者 | 1-3 | 毎ターンHP+1回復 |
| Sprint | 疾駆 | 1-3 | 初回ロール時ダイス+2 |
| BruteForce | 剛力 | 1-3 | 勝利時ダメ+2 |
| Flight | 飛翔 | 1-3 | 追撃ダメ無効 |
| HardScales | 硬鱗 | 4-5 | 被ダメ-2 |
| TailStrike | 尾撃 | 4-5 | 敗北時、固定1ダメ |
| Rampage | 暴走 | 4-5 | 敗北時、次Tダイス+3 |
| Ethereal | 虚体 | 4-5 | 被ダメ50%軽減 |
| Curse | 呪縛 | 4-5 | 勝利時、次T相手ダイス-2 |
| Immovable | 不動 | 4-5 | 追撃ダメ無効 |
| CounterStance | 反撃態勢 | 4-5 | 敗北時、次Tダメ+3 |
| MultiHead | 多頭攻撃 | 6-7 | 勝利時、追撃ダイス+1個 |
| Regeneration | 再生 | 6-7 | 毎ターンHP+2回復 |
| DemonAura | 魔王の威圧 | 6-7 | 戦闘開始時、相手MaxHP-3 |
| Hellfire | 地獄の業火 | 6-7 | 勝利時、固定2ダメ |
| Lifesteal | 吸血 | 6-7 | 与ダメ50%HP回復 |
| NightLord | 夜の王 | 6-7 | 5T目以降、ダイス+1個 |
| DeathSentence | 死の宣告 | 6-7 | 10T超で999固定ダメ（即死） |
| ScratchAura | 威圧 | 4+ | プレイヤー勝利時、max(0,threat-\|diff\|)の削りダメ |

### 戦闘システム計算式

#### 勝敗判定
```
diff = (Σ playerDice + diceBonus) - (Σ enemyDice - enemyDiceDebuff)
diff > 0 → 勝利 / diff < 0 → 敗北 / diff == 0 → 引分
```

#### ダメージ計算
```
base = |diff| + damageBonus + パッシブ補正
total = base + pursuitDamage（勝利時のみ追撃あり）
会心確率 = min(9, critRate + critBonus) / 9
会心時: total × critMultiplier（既定2.0）
```

#### HP適用（勝利時）
```
enemyHP -= total + fixedDmgToEnemy + bleedDmg
playerHP -= fixedDmgToPlayer + scratchDmg
scratch = max(0, enemyThreat - |diff|) ※ScratchAura持ち敵のみ
```

#### HP適用（敗北時）
```
playerHP -= total + fixedDmgToPlayer
enemyHP -= fixedDmgToEnemy + bleedDmg
※ scratchなし、追撃なし
```

#### HP適用（引分時）
```
mainDamage = 0（固定ダメージ・出血のみ適用）
```