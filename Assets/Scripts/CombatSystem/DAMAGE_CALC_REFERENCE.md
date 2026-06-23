# ダメージ計算リファレンス（最重要・実コード準拠／憶測禁止）

> このファイルは実コードから直接抽出した事実のみを記載する。
> 仕様変更時は**必ず**コードと突き合わせて更新すること。推測で書かない。
> 出典: `CombatManager.cs` / `PassiveSkillManager.cs` / `CombatContext.cs` /
> `AllPassiveSkillEffects.cs`（プレイヤー） / `EnemyPassiveSkillEffects.cs`（敵）
> 最終確認: 2026-05-29
>
> 【最適化リファクタ済み（2026-05-29）】
> - `diceDifference` は `playerDiceTotal − enemyDiceTotal` の**読み取り専用プロパティ**（手動代入は全廃。視点スワップ時も両合計入替で符号が自動反転）。
> - ダイス合計の組み立ては `PassiveSkillManager.RecomputeDiceTotals()` に集約（通常ロール／引分リロール共通）。
> - 勝利時の与ダメ修飾チェーンは `CombatManager.ApplyWinDamageModifiers()`、敗北時は `ApplyLossDamageModifiers()` に抽出（適用順は不変・厳守）。
> - VoidStance は OnPostRoll 中の鮮度問題回避のため自前で差を算出。

---

## 0. 敵パッシブの視点（最重要の前提）

敵スキルは `FireEnemyTrigger` で**視点を入れ替えて**実行される。敵パッシブのコード内では：

| コード上の表記 | 実際の意味（敵視点） |
|---|---|
| `ctx.playerDiceTotal` / `playerDice` | **敵自身**のダイス合計／出目 |
| `ctx.playerCurrentHP` / `playerMaxHP` | **敵自身**のHP |
| `ctx.playerWonRoll` / `OnRollWin` | **敵が**ロール勝利 |
| `ctx.enemyCurrentHP` / `enemyMaxHP` | **実プレイヤー**のHP |
| `ctx.enemyDiceTotal` | **実プレイヤー**のダイス合計 |
| `ctx.fixedDamageToEnemy` | **実プレイヤーへの**軽減無視ダメージ |
| `ctx.finalDamage` | そのターンの与/被ダメ（文脈による） |

**スワップされない共有フィールド**（敵パッシブが書くと実値に直接効く）:
`enemyDiceTotalBonus` / `bossDiceBonus` / `enemyDamageReductionPct` / `enemyThreat` /
`enemyDamageTakenMultiplier` / `healBlocked` / `healShieldReduction` / `accumulatedValues` /
`ashenSuddenDeath` / `myokakuSuddenDeath` / `pendingEnemySwapId`

---

## 1. 計算パイプライン（適用順・実コード準拠）

### ⓪ ダイス振り直しフェーズ（#1・`CombatManager.MaybeRerollPlayerDice`・ProcessPostRoll の前）

初回ロール後・各種補正（記憶の砂時計/コルヴェン/メタ補正）と ProcessPostRoll の**前**に、希望を払って
プレイヤーの「期待値割れの出目」を**毎ターン最大1回**振り直す（`playerDice` を in-place 更新）。

- コスト: `HopeSystem.RerollCost`（既定3・暫定）を `HopeSystem.TryPayReroll` で支払い。**払えない（低希望）と振り直せない**＝終盤ほど二度目が無い。
- 自動ポリシー（UI 未実装の暫定）: 現在 自合計 ≤ 敵合計（負け/拮抗）かつ 平均（面平均 or (max+1)/2）割れダイスがあるときのみ、それらを再ロール。明確に勝っていれば温存。
- スキップ: 強制ロール状態（`ashenSuddenDeath`/`myokakuSuddenDeath`/`myokakuFreeHit`/`player_contre`）。
- 将来 UI 配線時に自動判定を人間の選択へ差し替える。希望損は `HopeSystem.Stats.rerollLoss`（AutoRunner で発生源別計上）。

### ① ダイス合計フェーズ（`PassiveSkillManager.ProcessPostRoll` L434-463）

```
playerDiceTotal = Σ(プレイヤー出目)
                + buff "diceBonus"        ← rollPurity(無我無心)中は拒否
enemyDiceTotal  = Σ(敵出目)
                − buff "enemyDiceDebuff"
                − consEnemyDiceDebuff      ← 消費:敵弱体
                − enemyDiceTotalPenalty    ← 床なし減算(負値許容)。沈黙の剣帯=1T目-99
                + enemyDiceTotalBonus      ← 星火燎原/業火の遺志/狂暴化 等(累積)
                + bossDiceBonus            ← 強者/玉座/刹那(※現在enemies.jsonから撤廃・常時0)
   ※敵スタンス弱ロール(ADR-0005)は CombatManager の RollDice 時に敵ダイスの最大出目を縮めて実際に振る
     （期待値≈0.65倍・結果の事後倍率ではない）→ Σ(敵出目) に既に反映。強ロールは基準(縮小なし)。

→ FireTrigger(OnPostRoll)  ← ここで OnPostRoll パッシブが両合計を加算改変
   (軽量/熟練/技量/筋力/星導/永劫/ヘルメス/黄金卿/復讐/Abyss/画竜点睛 など、
    敵側: 号令/精鋭各種/号令/業の連鎖/永劫の燃焼 など)

diceDifference = playerDiceTotal − enemyDiceTotal
勝敗 = sign(diceDifference + consDiceRoll)   ← consDiceRoll(消費ダイス補正)は勝敗のみ・ダメ非加算
引き分けは勝敗が付くまでダイス再ロール（OnPostRollは再発火しない）
画竜点睛(garyoProc)時は敗北/引分でも即勝利に上書き
```

**注意**: 敵 `OnPostRoll` は ProcessPostRoll の**勝敗判定後**に発火するため、
敵がダイス合計を判定に乗せたい場合は `OnTurnStart` で `enemyDiceTotalBonus` に積む
（断絶した時間/業火の遺志/星火燎原/狂暴化 がこの方式）。

### ② ProcessDamage（`PassiveSkillManager` L541-607・勝敗共通）

```
base  = 勝利:attackPower + floor(|diceDifference|/3)  /  敗北:max(|diceDifference|, enemyThreat)
        ※勝利base は CombatManager で算出（#2 案A'・ADR-0004）。attackPower=装備武器の素火力(items.json)、
          差は WeaponDiffPerBonus=3 ごとに+1の小ボーナス。会心(critRate)には非干渉。敗北/scratch は不変。
final = base + buff "damageBonus"
[勝利] FireTrigger(OnPreDealDamage)      ← 与ダメ改変パッシブ
[敗北] FireTrigger(OnPreReceiveDamage)   ← 被ダメ軽減パッシブ（堅忍/鉄壁/頑強/天命 等）
FireTrigger(OnPrePursuitDamage)          ← 追撃無効化(飛翔/不動)
nullifyAllDamage → final=0,pursuit=0  /  nullifyPursuitDamage → pursuit=0
total = final + pursuitDamage
[会心] FireTrigger(OnCriticalCheck); criticalBonus += buff "criticalBonus"
       有効分子 = clamp(criticalNumerator + criticalBonus, 0, 9)
       会心成立 = Random.Range(0,9) < 有効分子      ← 会心率 = 有効分子/9
       成立かつ total>0 → FireTrigger(OnCriticalDamage); total ×= criticalMultiplier(既定2.0)
       【狩猟旅団 (契約)】armed状態の脆弱を持つ敵への会心時、 全段に ×(1 + 0.15/0.30/0.45) 倍率を適用 → 脆弱は consumed へ
                          (詳細: docs/specs/contracts.md)
FireTrigger(OnPostDealDamage / OnPostReceiveDamage)
【蒼白の槍騎士】fixedDamageMultiplier>1 なら fixedDamageToEnemy ×= 倍率（return前）
return (total, fixedDamageToEnemy, isCritical)
```

### ③-A 与ダメ確定（プレイヤー勝利・`CombatManager.ApplyWinDamageModifiers`）

```
total = ProcessDamage結果
(画竜点睛) total = ceil((garyoDieValue+10) × criticalMultiplier)   ※発動時上書き・会心確定
業物Lv: outgoingDamageMultiplier += 0.2 × lbStage （倍率に加算）
× outgoingDamageMultiplier        ← 激情の刃/業物/重畳/完全 等
+ ceil(total × consDmgMultPct/100) ← 消費:鬼火の油
+ consAtkBurst                     ← 消費:攻撃バースト(単発)
+ メタ会心ボーナス(会心時)
− メタデバフ向かい風(最低1)
─ FireEnemyTrigger(OnPreReceiveDamage): 灰塵の鎧/Ethereal/HardScales/各敵被ダメ軽減
  → total = max(0, ctx.finalDamage)
× 基礎防御%軽減【新】: effRate = max(0, enemyDamageReductionPct − armorPenPct)
                       total = ceil(total × (1 − effRate))
↑ 勝利時最低保証【新】: total = max(total, winMinDamage[既定1, 利刃で1〜4])
× enemyDamageTakenMultiplier       ← 狂暴化(50T後 ×3)
メタデバフ俊敏: 各戦闘の初撃を total=0,fixedDmg=0
プレイヤー防御スタンス(ADR-0006): 防御時 total ×=0.1（与ダメ-90%・末尾。fixedDmgは対象外）
→ enemyHP −= total + fixedDamageToEnemy
  鋼の皮膚(メタデバフ): 敵初回致命を1HPで耐える
  出血(enemyBleedStacks) / 貪欲lifesteal(HealPlayer) / fixedDamageToPlayer
  scratch: enemyThreat>0 → scratchDamage += max(0, enemyThreat − |diff|) をプレイヤーが被弾
```

### ③-B 被ダメ確定（プレイヤー敗北・`CombatManager.ApplyLossDamageModifiers`）

```
※堅忍/鉄壁/頑強/天命/Destiny/Perfection等の被ダメ軽減は②のProcessDamage内で適用済み
total = ProcessDamage結果
× メタデバフ天変地異(×2.0)
× enemyStanceDamageMult       ← 敵スタンス(ADR-0005・高ダメ>1/低ダメ<1)。以降の軽減はスタンス後の値に効く
− メタバフ被ダメ軽減(最大−2,最低1)
− playerFlatDamageReduction(不屈の鎧/苦難の刻印 合算, 最低1)
− floorMod.defeatDamageReduction(5層 地獄門 −2)
+ ceil(total × receivedDamageBonus)(亡者の招待 +30%)
÷2 halveFirstEnemyAttack(共助・T1のみ)
playerDamageNegateCharges>0 → total=0(獣の絆)
コントラタック(player_contre): total半減 + 軽減量×2を敵へ反射
− consShield(消費シールド吸収)
consReflect(鏡写し): 吸収後の被ダメ同量を敵へ反射
プレイヤー防御スタンス(ADR-0006): 防御時 total ×=0.5（受け最終-50%・全軽減/シールドの後＝**最後**。反撃等の固定反射は対象外）
→ playerHP −= total (+ fixedDamageToPlayer)
  fixedDamageToEnemy>0 は敗北時も敵へ(反撃/Counter/Riposte) / 出血
fixedDamageToPlayer ≥ 999 → playerHP = 0（死の宣告）
```

### ④ 引き分け（`CombatManager` L1134-1155）
メイン/scratchなし。`fixedDamageToEnemy` / `fixedDamageToPlayer` / 出血のみ適用。
`truceThisTurn`(停戦協定)中は出血など他効果を抑止。

---

## 2. ctx フィールド用語集（計算に効くもの）

| フィールド | 意味 / 効果 | リセット |
|---|---|---|
| `playerDiceTotal`/`enemyDiceTotal` | ロール合計。ダメージbase=｜差｜ | 毎ロール再計算(RecomputeDiceTotals) |
| `diceDifference` | playerDiceTotal − enemyDiceTotal の**読み取り専用プロパティ**（常に最新・代入不可） | — |
| `baseDamage`/`finalDamage`/`pursuitDamage` | ダメージ計算の作業値 | ProcessDamageで設定 |
| `fixedDamageToEnemy`/`fixedDamageToPlayer` | 軽減無視の固定ダメ | BeginNewTurnで0 |
| `criticalBonus`/`criticalMultiplier`/`isCritical` | 会心分子加算/倍率(既定2.0)/結果 | criticalBonus/isCriticalは毎T0、倍率はメタ値で毎T再取得 |
| `enemyThreat` | 脅威。敗北base下限＆勝利時scratch基準 | 戦闘保持(威圧で+) |
| `scratchDamage`/`nullifyScratchDamage` | 勝利時の削り被弾/無効化 | 毎T |
| `enemyBleedStacks` | 出血。毎ターン-1減衰、毎T末に敵へ | 戦闘保持 |
| `enemyBurnTurns`/`enemyBurnDamage` | 炎上（業火） | 戦闘保持 |
| `enemyDiceTotalBonus` | 敵合計への加算（判定前・累積） | **BeginNewTurnでリセットしない** |
| `enemyDiceTotalPenalty`【新】 | 敵合計への床なし減算（負値許容＝大差勝ち）。沈黙の剣帯=1T目99 | BeginNewTurnで0 |
| `enemyStanceDamageMult`【新】 | 敵スタンスの被ダメ倍率(高火力>1/低火力<1)。ApplyLossDamageModifiersで乗算 | BeginNewTurnで1.0 |
| `enemyStanceKind`【新】 | 0なし/1強ロール低火力/2弱ロール高火力。弱(=2)は RollDice時に敵ダイス最大出目を`EnemyStance.WeakRollMax`へ縮小。`OnTelegraph`でUI後付け | BeginNewTurnで0 |
| `playerStanceDefense`【新】 | プレイヤー防御スタンス(ADR-0006)。与ダメ×0.1(ApplyWin末尾)/受け最終×0.5(ApplyLoss末尾)。反撃等の固定反射は対象外 | BeginNewTurnでfalse |
| `bossDiceBonus` | 強者/玉座/刹那(現在未使用) | swapで0 |
| `enemyDamageReductionPct`【新】 | 敵の被ダメ%軽減。EnemyData.baseDefenseRate+EliteVigor0.10 | **戦闘保持**(start/swapで設定) |
| `armorPenPct`【新】 | 利刃の軽減剥がし(pt) | BeginNewTurnで0 |
| `winMinDamage`【新】 | 勝利時与ダメ最低保証(既定1,利刃1〜4) | BeginNewTurnで1 |
| `enemyHealReductionPct`【新】 | 敵回復の減衰率(治癒阻害0.5/遮断1.0)。`ReduceEnemyHeal()`経由で敵自己回復に適用 | 戦闘保持 |
| `fixedDamageMultiplier`【新】 | 軽減無視ダメ倍率(蒼白の槍騎士1.5)。ProcessDamage末＆引分分岐で fixedDamageToEnemy に乗算 | 戦闘保持(既定1.0) |
| `outgoingDamageMultiplier` | プレイヤー与ダメ倍率(激情の刃) | 毎T1.0 |
| `enemyDamageTakenMultiplier` | 敵被ダメ倍率(狂暴化×3) | 毎T1.0 |
| `healBlocked`/`healShieldReduction`/`lifestealPct` | 回復封印/回復・シールド減衰量/与ダメ回復% | healBlocked毎T,lifesteal毎T,reductionは戦闘保持 |
| `playerFlatDamageReduction` | 被ダメ定額減(不屈の鎧/苦難の刻印) | BeginNewTurn |
| `receivedDamageBonus` | 被ダメ増(亡者の招待+30%) | — |
| `halveFirstEnemyAttack`/`playerDamageNegateCharges`/`nullifyFirstEnemyRoll` | 共助/獣の絆/獣の恩義 | フラグ消費 |
| `truceThisTurn` | 停戦協定ターン(他効果抑止) | 毎T |
| `rollPurity` | 無我無心(diceBonus等拒否) | 戦闘保持 |
| `garyoProc`/`garyoDieValue` | 画竜点睛発動/出目 | — |
| `consShield`/`consReflect`/`consDmgMultPct`/`consAtkBurst`/`consCrit`/`consDiceRoll`/`consEnemyDiceDebuff` | 消費アイテム由来 | 各種 |
| buff `diceBonus`/`damageBonus`/`criticalBonus`/`enemyDiceDebuff` | currentBuffs/nextTurnBuffs経由 | nextTurn→current移行 |
| `accumulatedValues["extraDice"]` | そのターンのダイス数増減(NightLord/精鋭/0d0等) | — |
| `accumulatedValues["extraPursuitDice"]` | 追撃ダイス追加(多頭) | — |
| `accumulatedValues["enemyMaxHPReduction"]` | 相手最大HP減(魔王の威圧) | — |
| `ashenSuddenDeath`/`myokakuSuddenDeath`/`gedatsuPending` | 6層/妙覚サドンデス/解脱 | — |

---

## 3. プレイヤーパッシブ一覧（`AllPassiveSkillEffects.cs`）

### 3-A. 汎用パッシブ（Tier制・ショップ/ドロップで段階的に強化）

| SkillId(表示) | Tier/レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| Pursuit I/II/III/IV(追撃) | B/S/G/L | OnPreDealDamage | finalDamage += 2/4/6/8 |
| Counter I/II/III/IV(反撃) | B/S/G/L | OnRollLose | fixedDamageToEnemy += 2/4/6/8（2026-06-03 リバフ 1/2/3/4→2/4/6/8） |
| Might I/II/III/IV(剛力) | B/S/G/L | OnPostRoll | playerDiceTotal += 2/3/4/5（固定・ダイス数非依存） |
| Fortitude I/II/III/IV(頑強) | B/S/G/L | OnPreReceiveDamage | finalDamage −= 1/2/3/4 (min0) |
| Insight I/II/III/IV(慧眼) | B/S/G/L | OnCriticalCheck | criticalBonus += 1/2/3/4 |
| Vitality I/II/III/IV(活力) | B/S/G/L | OnTurnEnd | 自HP +1/2/3/4 |
| Indomitable I/II/III/IV(不屈) | B/S/G/L | OnBattleStart | enemyThreat −= 2/4/6/8 (min0) |
| ShieldBash I/II/III/IV(シールドバッシュ) | B/S/G/L | OnTurnStart | shieldOnWinPct += 0.05/0.10/0.15/0.20（勝利時 totalDmg×pct を consShield 化・天衣無縫減衰適用） |
| LentTime I/II/III/IV(貸与された時間) | B/S/G/L | OnPreReceiveDamage/OnRollWin | 敗北時 被ダメの15/30/45/60%を肩代わり(finalDamage減)し lentTimeStacks 蓄積。max(maxHP×同%)到達で同値を fixedDamageToPlayer で一括清算＋0。勝利で0クリア |
| Lifesteal I/II/III/IV(吸血) | B/S/G/L | OnTurnStart | lifestealPct += 0.02/0.04/0.06/0.08（勝利時 totalDmg×pct を HealPlayer 回復・負傷/封印尊重） |
| BladeEdge I/II/III/IV(利刃) | B/S/G/L | OnPostRoll | armorPenPct=0.15/0.20/0.25/0.30、winMinDamage=max(,1/2/3/4)。防御を上回る貫通分は与ダメ×(1+余剰pen)へ転化(CombatManager) |
| BountyHunter I/II/III/IV(賞金首狩り) | B/S/G/L | OnTurnEnd | 敵HP≤8/14/20/28% で enemyCurrentHP=0(処刑)＋最大HP×(lv×3%)回復＋coins +0/0/1/2 |
| Conqueror I/II/III/IV(重畳) | B/S/G/L | OnRollWin/OnPreDealDamage | 勝利毎に outgoing += 2/4/5/10% (上限 20/40/60/100%) ※2026-05-31 v5 %ベース |
| Grievous I/II(治癒阻害/治癒遮断) | S/G | OnBattleStart | enemyHealReductionPct = 0.5 / 1.0（敵回復を ReduceEnemyHeal で減衰） |
| Lightweight/Mastery/Skill(軽量/熟練/技量・武器Tier由来) | — | OnPostRoll | playerDiceTotal += 3/2/1 |

### 3-B. 固有パッシブ（ユニーク・名前付き／ダイス固有効果含む）

| SkillId(表示) | Trigger | 効果(ctx操作) |
|---|---|---|
| Parry(パリィ) | OnPreScratchDamage | nullifyScratchDamage=true |
| HolyShield(聖なる守り) | OnPreReceiveDamage | 敗北時 finalDamage ×0.5 |
| Riposte(切り返し) | OnPostReceiveDamage | 敗北時 fixedDamageToEnemy += ceil(finalDamage×0.5) |
| VoidStance(虚空) | OnPostRoll | ｜playerDiceTotal−enemyDiceTotal｜≤3 → nullifyAllDamage + fixedDamageToEnemy +=3（自前で差を算出） |
| Frenzy(復讐) | OnRollLose/Win/PostRoll | 敗北で蓄積+1/勝利でreset、PostRollで playerDiceTotal += 蓄積 |
| BloodDecree(血令) | OnPreDealDamage | ゾロ目勝利 → fixedDamageToEnemy += ceil(playerDiceTotal×2.5), finalDamage=0 |
| Execute(処刑) | OnRollWin | 次ターン敵の最小ダイスを1に固定 |
| Nightfall(蝕夜) | OnPostDealDamage/OnBattleStart | overDamage×2を永続蓄積→戦闘開始時 fixedDamageToEnemy |
| Sting(出血) | OnPostDealDamage | enemyBleedStacks++ |
| Ignite(業火) | OnBattleStart | 敵に burn を3スタック付与（#3 統一フレームへ移行）。毎ターンのDOT/減衰は `TickStatuses` が処理（burn=固定3ダメ×3T） |
| 脆弱(契約・狩猟旅団) | OnBattleStart (Apply) + 防御後最終ダメ | armed状態で会心ダメ時 ×(1+0.15/0.30/0.45) 倍率→consumed / 非会心ロール勝利で再armed。 戦闘終了で剥がれる。 仕様: docs/specs/contracts.md |
| CurseBind(呪縛) | OnTurnStart/OnPostRoll | 毎T自HP−1+debuff累積 / enemyDiceTotal −= debuff |
| Abyss(深淵) | 多数 | 致命被ダメで finalDamage=HP−1→狂化: playerDiceTotal+10/fixedDamageToEnemy+=被ダメ蓄積×3/criticalBonus+99 |
| Shimmer(煌玉) | OnPostRoll | 最大面の出目があれば criticalBonus +1 |
| ReversalFlame(盟約) | OnRollLose | 次ターン diceBonus +3 |
| Steadfast(堅忍) | OnPreReceiveDamage | finalDamage −= 3 (min0) ※被ダメ軽減。ダイス合計ではない |
| IronWall(鉄壁) | OnPreReceiveDamage | finalDamage −= 1 (min0) ※被ダメ軽減 |
| Skyladder(天梯) | OnPreDealDamage | 出目が3個以上の連続昇順(階段)なら outgoing +=1.0 (旧 finalDamage×2、 2026-05-31 outgoing移行) |
| ApexCrit(天極) | OnCriticalCheck | ゾロ目(全同値)なら criticalBonus+99(会心確定)＋criticalMultiplier+1.0 |
| Lifeline(命脈) | OnBattleStart + OnPostReceiveDamage | 戦闘開始時 maxHP×10% 回復／1戦闘1回 HP≤50% で consShield += ceil(maxHP×0.5) |
| PalePikeKnight(蒼白の槍騎士) | OnBattleStart | fixedDamageMultiplier=1.5（軽減無視ダメ×1.5） |
| Resonance(共鳴・LEG) | OnPreDealDamage | 発動中パッシブ数(over5) × 0.05 を outgoing 加算 (旧 finalDamage×倍率、 2026-05-31 outgoing移行) |
| Moroha(諸刃・ダイス) | OnRollWin | healShieldReduction++ (max20) ※負傷 |
| Greed(貪欲・ダイス) | OnTurnStart | lifestealPct = 0.1 |
| Perfection(完全・ダイス) | OnPreDealDamage | 重複あり → outgoing +=1.0 / 重複なし → finalDamage÷2 (min1)。 2026-05-31 outgoing移行 (旧 finalDamage×2) |
| Eternal(永劫・ダイス) | OnPostRoll/OnBattleEnd | playerDiceTotal += min(5, eternalStacks/10)、勝利でstack++保存(ラン跨ぎ) |
| StarFate(星命・ダイス) | OnRollWin | ゾロ目 → pursuitDamage += 出目値 |
| Destiny(運命・ダイス) | OnPreDealDamage/OnPreReceiveDamage | 全ダイス最大出目→outgoing+=1.0、全最低出目→被ダメ0 (2026-05-31 outgoing移行) |
| Starguide(星導・ダイス) | OnPostRoll | 全ダイス相異→ playerDiceTotal +3 |
| Truce(停戦協定) | OnRollDraw | truceThisTurn=true、fixedDamageToEnemy=ceil(enemyMaxHP×0.1)上書き、他効果抑止 |
| TenkouKaibutsu(天工開物) | (なし) | no-op(効果は GameManager.TryUpgradeWeapon の素材返還) |
| Bloodlust(背水の狂刃) | OnPreDealDamage | HP≤50% → outgoing+=0.3、≤25% → +=0.8 (2026-05-31 outgoing移行) |
| Hermes(ヘルメスの靴) | OnPostRoll | 初回ロール playerDiceTotal +5 |
| HungerPill(飢餓丸) | OnTurnStart/OnPreDealDamage/OnPreReceiveDamage | 毎T fixedDamageToPlayer+1、T7覚醒後 与ダメ+18永続/次被ダメ−18(1回)（2026-06-03 リバフ T10/+10/-10→T7/+18/-18） |
| GoldKingBlade(黄金卿の剣) | OnPreDealDamage | outgoing += 0.01×coinsSpent (100Gで+1.0=×2倍相当、 上限なし、 2026-05-31 v3 消費Gold基準) |
| Judgement(天命) | OnPreReceiveDamage | 敵合計≥自分×2 かつ HP≥最大30% → finalDamage上限=現HP−2 |
| MugaMushin(無我無心) | OnBattleStart | rollPurity=true(diceBonus等の補正拒否) |
| GaryoTensei(画竜点睛) | OnPostRoll | 出目が最大面 → garyoProc=true,garyoDieValue。即勝利+(出目+10)×criticalMultiplier会心確定 |

### 3-C. 固有パッシブ（`PassiveItemEffects.cs`・ITimedEffect系。TimedEffectManager が CombatStart/OnRoll/OnTurnEnd/CombatEnd/OnMapMove で発火）

| 名称 | レア | Trigger | 効果(ctx/run操作) |
|---|---|---|---|
| 灯心の鈴 | B | OnMapMove | 10%で空腹度+1 |
| 安らぎ/癒し/神聖の靴 | B/S/G | OnMapMove | 歩行毎 playerHP +1/2/3 |
| 死神の数珠 | S | CombatEnd | 勝利時50%で coins +1 |
| 巡礼者の杖 | S | CombatEnd | 50%で空腹度+1 |
| 希望の灯片 | S | CombatEnd | HealPlayer(3) |
| 静寂のローブ | S | CombatStart | enemyPassivesDisabledTurns=1（1T目敵パッシブ封印） |
| 食通の懐刀 | S | (イベント) | イベント由来の空腹回復+1（EventEffectExecutor） |
| 記憶の砂時計 | G | OnRoll(T1) | 1T目 最低出目ダイス→最大値 |
| 嵐の徽章 | G | OnRoll(T1) | 1T目 playerDiceTotal += max(1, diceMax/2) |
| 黄金の天秤 | G | CombatEnd | 勝利時 coins +5 |
| 黒煙の符 | G | CombatStart | enemyBleedStacks +2 |
| 蒼穹の眼 | G | OnRoll(T1) | criticalBonus=max(,9)（1T目 会心確定） |
| 狂乱のメダリオン | L | OnRoll | HP≤25% → outgoingDamageMultiplier +0.5（与ダメ+50%） |
| 末那識(旧 死神の予感) | L | OnRoll | HP≤20% → forceCritical=true（会心確定・乱数/会心率cap無視） |
| 沈黙の剣帯 | L | OnRoll | fixedDamageToEnemy +1／1T目のみ enemyDiceTotalPenalty +=99（床なし・先制必殺）。消費封印は ItemUseHandler 側（2026-06-03 -99先制を追加） |
| 倍音のクロック | L | OnRoll | currentTurn%3==0 → outgoing +=1.0 (×2.0)。 2026-06-03 リバフ (+0.5→+1.0) |
| 鋼の心臓 | L | CombatEnd | HealPlayer(5)（取得時に最大HP+20ボーナス別途） |
| 守護天使の鈴 | L | OnTurnEnd | 戦闘中1回 HP≤25 → HealPlayer(15) |
| 災厄の指輪 | L | OnRoll | 被弾毎に次の与ダメ+2累積(上限+10・戦闘終了リセット) finalDamage加算 |
| 永遠の燈 | L | CombatEnd | HP≤10 → HealPlayer(20) |
| 商人の符牒 | L | (ショップ連携) | ショップ系フック（PassiveItemRegistry 非登録） |
| 巡礼の杖飾り【新2026-06-03】 | B | OnMapMove | 25%で希望+1（HopeSystem.ApplyFood） |
| 狂宴の仮面【新2026-06-03】 | S | OnRoll | 希望[悲観]以下 outgoing+0.10 ／[絶望]以下 +0.25 |

### 3-D. 2026-06-03 追加の汎用パッシブ（`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| EvenEyes(賽振りの目隠し) | B | OnPreDealDamage | 全出目が偶数 → outgoing +=0.15 |
| TwinDice(双子の賽) | S | OnCriticalCheck | 出目に重複ペアあり → criticalBonus +1 |
| BloodPathBanner(血路の旗) | G | OnPreDealDamage | outgoing += 0.03 × enemyBleedStacks |
| MasterworkNotes(匠の手控え) | G | OnPreDealDamage | run.weaponPlus ≥ 3 → outgoing +=0.12 |
| KaleidoDice(万華の賽) | L | OnPreDealDamage | 全同値/全相異/階段(3+)のいずれか → outgoing +=1.0 |
| JudgmentScale(断罪の天秤) | L | OnPreDealDamage | 勝利時 outgoing += min(1.0, 0.04×diceDifference) |

### 3-E. [剣の舞] セット（2026-06-04・`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

Passive カテゴリのシナジー武器群。4枚がインベントリに揃うと全消滅し〈ブレイドダンス〉に変化（`GameLoop.SwordDanceSet`）。
ダイス合計加算は OnPostRoll（RecomputeDiceTotals 後で確実に効く）。run 参照は `GameManager.Instance.Run`。

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| SaberWaltz(サーベル・ワルツ) | B | OnBattleStart / OnPostRoll | playerDiceTotal +1。他の[剣の舞]がインベントリにも昇華にも無い時、開戦時に playerCurrentHP を半減。ショップ出現率上昇は ShopManager フック |
| EspadaPasodoble(エスパーダ・パソドブレ) | S | OnPostRoll / OnPreReceiveDamage | playerDiceTotal +5・enemyDiceTotal +5・outgoing +=0.2／被弾時 finalDamage ×1.2（被ダメ+20%） |
| FleuretBallet(フルーレ・バレエ) | B | OnPostRoll | playerDiceTotal +3。敗北時の自壊+最大HP1生還は `LastStand.TryConsumeRevival`（灯火→ラストスタンド→フルーレ の順） |
| FalconTango(ファコン・タンゴ) | L | OnBattleEnd | ランダムな[剣の舞]以外の所持パッシブを1廃棄し、全カテゴリから無条件ランダム1獲得（武器/ダイスは装備置換・weaponPlus=0） |
| BladeDance(ブレイドダンス) | L(特殊) | OnBattleStart / OnPostRoll / OnPostDealDamage / OnPostReceiveDamage | 開戦毎に剣先スタック+1(最大99・ラン中持続/IRunResettable)。playerDiceTotal +剣先／与ダメ時 剣先分HP回復／被ダメ時 fixedDamageToEnemy +剣先 |

> 条件の差（仕様厳守）: サーベルの孤剣判定＝`OwnsPassive`（昇華込み）/ 4枚変化判定＝`ownedPassiveItems`のみ（昇華除外）。
> ブレイドダンスは `EventOnlyItemFilter` 除外でショップ・ランダム配布に出ない（変化でのみ入手）。

### 3-F. 会心バリエーション（2026-06-05・`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

会心を「ただ ×criticalMultiplier」から質の違う一撃へ。**`OnCriticalDamage` は ProcessDamage 内
`isCritical && totalDamage>0` 成立後・`totalDamage ×= criticalMultiplier` 適用前**に発火（§1 ②）。
会心後ダメが要る効果は `(finalDamage + pursuitDamage) × criticalMultiplier` を自前算出する。

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| LacerationCore(裂傷の刃心) | B | OnCriticalDamage | `enemyBleedStacks += 2 + floor(max(0, criticalMultiplier−1))`（×2.0で+3, ×3.0で+4。血路の旗と相乗） |
| GuardFlash(防殻の一閃) | B | OnCriticalDamage | `consShield += ceil(会心後ダメ×0.05)`（`shieldGainedTotal` も加算） |
| VitalPierce(急所穿ち) | S | OnCriticalDamage | `fixedDamageToEnemy += 5`（軽減無視の追い打ち） |
| LifeFang(吸命の牙) | S | OnCriticalDamage | `lifestealPct += 0.15`（その勝利の最終ダメ15%回復・負傷/封印尊重） |
| SinglePoint(一点集中) | G | OnCriticalCheck | `criticalMultiplier += 0.5` ／ `criticalBonus −= 2`（毎T適用。稀だが特大） |
| ChainApex(連環の極み) | L | OnCriticalCheck / OnCriticalDamage | Check: `criticalMultiplier += 0.2 × accumulated["chainApexStacks"]` ／ Damage: スタック+1（戦闘中持続） |

> 数値は暫定（BOT オートランで要チューニング）。`criticalMultiplier`/`criticalBonus` は毎T再取得・0リセット
> のため、倍率/分子への加算は **OnCriticalCheck で毎ターン再適用**する。`accumulatedValues` は戦闘開始でのみ
> リセット＝連環のスタックは戦闘中持続。

### 3-G. ステータス統一フレーム（2026-06-05・`StatusEffectSystem.cs` ＋ `CombatContext`）

汎用ステータス層（#3）。**加算的導入**：既存の出血(`enemyBleedStacks`)/威圧(`enemyThreat`)/負傷
(`healShieldReduction`)/敵ダイス減 等は**バランス済みのため現状フィールドのまま据え置き**、本フレームは
新ステータス用＋パイロットとして **炎上(burn) のみ移行**した。

- 保持: `CombatContext.playerStatusStacks` / `enemyStatusStacks`（id→stacks・**絶対視点**＝視点スワップしない）。
- 定義: `StatusRegistry.Defs[id] = StatusDef{ target, tickTiming, dotPerStack, dotScalesWithStacks, decayPerTurn, maxStacks }`。
- tick: `CombatContext.TickStatuses(StatusTick.TurnStart)` を **`PassiveSkillManager.BeginTurn` の BeginNewTurn 直後**に1回呼ぶ
  （fixedDamage 0化後）。DOT＝`dotScalesWithStacks ? stacks×dotPerStack : dotPerStack` を fixedDamageToEnemy/Player へ加算 → decay。
- burn 定義: target=Enemy, TurnStart, dotPerStack=3, scales=false, decay=1, 初期stacks=3（旧 Ignite と等価）。Ignite は開幕に `AddStatus(Enemy,"burn",3)` するのみ。
- 制限（パイロット）: DOT値は def 単位（発生源別の可変ダメ未対応）／DOT は軽減無視（fixedDamage 経由）。

---

## 4. 敵/汎用パッシブ一覧（`EnemyPassiveSkillEffects.cs`・視点は§0参照）

| SkillId(表示) | Trigger | 効果 |
|---|---|---|
| Trapper(罠師) | OnRollWin | 次T 相手 enemyDiceDebuff +1 |
| Undying(不死者) | OnTurnStart | 自HP +1 |
| Sprint(疾駆) | OnPostRoll | 初回 自ダイス +2 |
| BruteForce(剛力) | OnPreDealDamage | 勝利時 finalDamage +2 |
| Flight(飛翔)/Immovable(不動) | OnPrePursuitDamage | nullifyPursuitDamage=true(追撃無効) |
| HardScales(硬鱗) | OnPreReceiveDamage | 敗北時 finalDamage −2 (min0) |
| TailStrike(尾撃) | OnRollLose | fixedDamageToEnemy +1(→実プレイヤー) |
| Rampage(暴走) | OnRollLose | 次T 自分 diceBonus +3 |
| Ethereal(虚体) | OnPreReceiveDamage | 敗北時 finalDamage /2 |
| Curse(呪縛/敵) | OnRollWin | 次T 相手 enemyDiceDebuff +2 |
| CounterStance(反撃態勢) | OnRollLose | 次T 自分 damageBonus +3 |
| HoningDuel(研ぎ澄まし) | OnRollWin | currentBuffs damageBonus += currentTurn/3 |
| EliteVigor(精鋭・汎用) | OnBattleStart/OnPostRoll | enemyDamageReductionPct +0.10 / 自ダイス += currentTurn/3 |
| EliteSlime/EliteGoblin | OnPostRoll | 自ダイス +3 |
| EliteKobold(早業) | OnPostRoll/OnPreReceiveDamage | GOLD奪取 / 50%で被ダメ0 |
| EliteSkeleton(不死の軍勢) | OnTurnEnd/OnPostRoll | 致命時2回までHP全回復 / 自ダイス += 3×発動回数 |
| EliteWolf(血盟の疾走) | OnPostRoll/OnPreDealDamage | 自ダイス +2 / 勝利時 finalDamage +2 |
| EliteHarpy(死翔) | OnBattleStart/OnPostRoll | 消費ロック / 自ダイス += 3+currentTurn |
| EliteDecree13(死の重圧) | OnPostRoll | 宣告ターン中 自ダイス +13 |
| EliteOrc(痛恨の一撃) | OnPreDealDamage他 | 勝利+8 / 敗北でダイス数+1(max3,extraDice)・勝利reset |
| EliteLizard(重甲) | OnPreReceiveDamage/OnRollLose | 敗北時 −2軽減 / fixedDamageToEnemy +1反射 |
| EliteWraith(霊体) | OnTurnStart/OnPreReceiveDamage | 奇数Tダイス+1 / 偶数T(霊体)被ダメ=1 |
| EliteGolem(巌の意志) | OnTurnEnd | 意志+1、撃破時 意志分を実プレイヤーへ確定ダメ |
| EliteMinotaur | OnPostRoll | 自ダイス +1 |
| EliteDarkKnight(闇技) | OnPreDealDamage | 勝利時 finalDamage +2 |
| MultiHead(多頭攻撃) | OnRollWin | extraPursuitDice +1 |
| Regeneration(再生) | OnTurnStart | 自HP +1 |
| DemonAura(魔王の威圧) | OnBattleStart | enemyMaxHPReduction +3(相手最大HP−3) |
| Hellfire(地獄の業火) | OnRollWin | fixedDamageToEnemy +2 |
| Lifesteal(吸血) | OnPostDealDamage | 勝利時 自HP += finalDamage/2 |
| NightLord(夜の王) | OnTurnStart | T5以降 extraDice=1 |
| DeathSentence(死の宣告) | OnTurnStart | T>10 で fixedDamageToEnemy +999(即死) |
| ScratchAura | OnRollLose | no-op(脅威はCombatManager共通処理に昇格) |
| IntimidatePlus/PlusPlus(威圧+/++) | OnBattleStart | enemyThreat += 3/5 |
| GreedyMerchant(貪欲商人) | OnBattleStart | no-op(スケーリングは GameManager) |
| Berserk(狂暴化・全ボス付与) | OnTurnStart | T>50: healBlocked, enemyDamageTakenMultiplier=3, enemyDiceTotalBonus +=10(1回) |
| FlawlessRobe(天衣無縫) | OnRollWin | healShieldReduction++ (max10) |

---

## 5. ボス専用パッシブ一覧（`EnemyPassiveSkillEffects.cs`）

| SkillId(表示) | Trigger | 効果 |
|---|---|---|
| GoblinKingsCall(号令) | OnPostRoll | 自ダイス +3 |
| FrozenBardSong(凍えの旋律) | OnTurnEnd/OnPostRoll | 未使用streak++ / 自ダイス += min(8, streak−1) |
| MiasmaCorrosion(毒の侵蝕) | OnTurnEnd | 毒stack+1(max5)、stack分を実プレイヤーへ軽減無視ダメ |
| MirrorTwinsResponse(鏡映の応答) | OnPostReceiveDamage/OnTurnStart | 与ダメ<12で reflect=min(9,12−ダメ)蓄積、次T開始で反射 |
| JudgmentFlames(審判の炎) | OnTurnEnd | dmg=min(10, 1+currentTurn+罪)、実プレイヤーへ軽減無視（罪=totalBattles/8,max2） |
| RoyalEmber(王の業炎) | OnTurnStart/OnTurnEnd | stack分ダメ / stack++ |
| SinChain(業の連鎖) | OnRollLose/Win/PostRoll | ボス敗北でcount+1(max5)・勝利reset / 自ダイス += count |
| EternalBurning(永劫の燃焼) | OnPostRoll | 実プレイヤーHP割合 ≤50/25/10% で 自ダイス +2/3/5 |
| ReturnToAshes(灰燼への回帰) | OnTurnStart | 自HP≤50% で 最大HP5%回復 |
| JudgmentBlaze(業火の断罪) | OnTurnStart他 | 断罪周期=HP割合で3/2/1T。断罪ターン:ボス勝利→finalDamage×10+15 / プレイヤー見切り→反撃18(鎧貫通) / 断罪敗北→追加4軽減無視 |
| AshArmor(灰塵の鎧) | OnPreReceiveDamage | 非断罪ターン・敗北時 finalDamage = min(max(0,ダメ−9), 10) |
| ImmortalEmber(不滅の残り火) | OnTurnStart | HP≤60%/30%で 失HPの5%/8%回復(ラチェット上限)、断罪ターンは回復なし |
| StarfireProliferation(星火燎原) | OnRollLose | starfire stack++、enemyDiceTotalBonus = stack(累積・リセット無) |
| ScorchedEarth(焦土) | OnTurnEnd | プレイヤー敗北時 実プレイヤー最大HP −= 被ダメ10%、consShield=0 |
| Decree13th(13番目の宣告) | OnTurnStart/OnTurnEnd | 13%でフラグ、成就で (playerDiceTotal+enemyDiceTotal)×criticalMultiplier を軽減無視で実プレイヤーへ |
| StrongOne/Throne/Setsuna(強者/玉座/刹那) | OnBattleStart | bossDiceBonus = 4/8/12 ※**現在enemies.jsonから撤廃・未参照（クラスは残置）** |
| SaintGeorgesPhases(シュヴァリエ) | 多数 | 形態1:シールド140+ロール勝利で(プレイヤー合計+プリオリテ×3)反撃+シールド−25 / 形態2:4d6+連勝報酬(ボスダイス+4,+15ダメ)・3勝で形態1帰還 / プリオリテ累積でシールド−28・反撃+10 |

### 覚者7形態連戦（boss_layer7 → p2 → … → p7、各OnTurnEndでHP0検知し次形態SwapEnemy予約）

| SkillId(形態) | Trigger | 効果 |
|---|---|---|
| AwakenedP1Inverse(初眼・逆観) | OnPostRoll | fixedDamageToEnemy += (プレイヤー最大出目 + currentTurn/3) 軽減無視 |
| AwakenedP2BurstFire(業火残響・爆ぜ火) | OnRollWin | プレイヤー敗北時 fixedDamageToEnemy +5 軽減無視 |
| AwakenedP3Mirror(無相・鏡映) | OnPreReceiveDamage | 覚者への与ダメの25%を fixedDamageToEnemy(実プレイヤーへ)反射 |
| AwakenedP4Riposte(残影・一閃返し) | OnPreReceiveDamage | 1回限り finalDamage=0（反射なし・初撃完全無効のみ） |
| AwakenedP5Silent(寂照) | OnPostRoll/OnTurnEnd | 毎T 所持パッシブ数/4(min1)軽減無視 / 消費品使用でランダムパッシブ永久喪失 |
| AwakenedP6EmberWill(薄火・業火の遺志) | OnTurnStart | enemyDiceTotalBonus = 形態内経過T(ランプ) |
| AwakenedP7Myokaku(妙覚) | OnTurnStart/OnTurnEnd | 妙覚T1〜6: enemyDiceTotalBonus=99 ＋ fixedDamageToEnemy += mT×4(軽減無視・HP直引きで死亡回避/シールド貫通)。T6生存後 myokakuSuddenDeath(両者1d2)。サドンデスでボス敗北→gedatsuPending(解脱・特殊勝利) |
| FlawlessRobe(天衣無縫・覚者各形態) | OnRollWin | healShieldReduction++(max10) |
| AwakenedTrial(旧・悟達の試練) | — | 連戦化で**未登録**(残置のみ) |

### SinAltar(6層)由来・動的注入（`CombatManager.ApplySinDebuffsToBossIfApplicable`）
| SkillId | Trigger | 効果 |
|---|---|---|
| boss6_golgotha(ゴルゴダの心) | OnBattleStart | 実プレイヤー最大HPを半減(戦闘中) |
| boss6_severed_time(断絶した時間) | OnTurnStart | enemyDiceTotalBonus = currentTurn−1 |
| boss6_ashen(灰燼の烙印) | OnTurnEnd | ボス致命時HP1踏みとどまり(1回)+ashenSuddenDeath(両者1d6で決着) |

---

## 6. Λ層（時間の狭間）由来の恒久デバフ（`GameLoop.Lambda.LambdaDebuffEffects`）

5層ボス撃破後〈決意〉以上で強制突入。環状線を周回するたび「次元の乱れ」+1、3毎に下表からランダムに1つ付与（同種再付与で段階+1、最大3）。`run.lambdaDebuffs`(id→段階)に格納し、戦闘開始時に `CombatManager.StartCombatInternal` で `ctx.lambda*` フィールドへ反映（戦闘スコープで保持・覚者連戦でも維持）。lv1/2/3 = 効果の3段階。

| デバフ | lv1/2/3 | 実装フック | 効果 |
|---|---|---|---|
| 重い足取り | -2/-4/-6 | `RecomputeDiceTotals`(currentTurn==1) | 1ターン目のみ playerDiceTotal をデルタ分減算(下限0・無我無心中は無効) |
| 微妙な手応え | 0.95/0.90/0.85 | `ApplyWinDamageModifiers`(基礎防御の後・最低保証の前) | 勝利時の最終与ダメに lambdaDamageDealtMult を乗算(CeilToInt) |
| 苛立つ強敵 | 5/4/3 T間隔 | `RecomputeDiceTotals`(末尾) | enemyDiceTotal += floor(currentTurn / interval)（累積・勝敗判定前） |
| 注意散漫 | cap 8/6/4 | `ProcessDamage`(会心分子clamp) | effectiveNumerator の上限(既定9)を 8/6/4 に。会心率= numerator/9 を制限。判定 Random(0,9) は不変 |
| 慈悲の処刑 | 0.05/0.10/0.15 | `CombatManager`(被弾後・combatLethalThisTurn確定前) | 被弾したターンに playerHP ≤ playerMaxHP×閾値 なら playerHP=0（回復蘇生を防ぐ） |
| 神経錯乱 | 3/5/7 | `ItemUseHandler.UseItem` | CurrentCombatTurn < lockUntil の間 消費アイテム使用不可 |
| 迫りくる死 | -/-/HP=1 | `StartCombatInternal` | lv3 のみ戦闘開始時に playerHP=1（lv1/2は無効＝実質3スタック猶予） |

- ctx フィールド: `lambdaFirstTurnDiceDelta`(0=無効) / `lambdaIrritatingInterval`(0=無効) / `lambdaDamageDealtMult`(1.0=無効) / `lambdaCritNumeratorCap`(9=無効) / `lambdaMercifulExecThreshold`(0=無効) / `lambdaConsumableLockUntilTurn`(0=無効)。BeginNewTurn ではリセットしない。
- 即死コンボ（意図通り）: 慈悲の処刑 ＋ 迫りくる死(lv3) = 戦闘開始HP1 → 初回被弾で確定死。
