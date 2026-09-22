# ダメージ計算リファレンス（最重要・実コード準拠／憶測禁止）

> このファイルは実コードから直接抽出した事実のみを記載する。
> 仕様変更時は**必ず**コードと突き合わせて更新すること。推測で書かない。
> 出典: `CombatManager.cs` / `PassiveSkillManager.cs` / `CombatContext.cs` /
> `AllPassiveSkillEffects.cs`（プレイヤー） / `EnemyPassiveSkillEffects.cs`（敵）
> 最終確認: 2026-07-26（会心システム リバランス — §2-B 追加）
>
> **2026-07-15 追記 — ADR-0009 相互攻撃パイプラインの併存について**
> [ADR-0009](../../../docs/adr/0009-mutual-attack-combat.md) が Accepted になり、
> `CombatManager.UseMutualAttackPipeline` で `ExecuteTurnMutual()` に切り替わる。
> **2026-07-27 に既定 true 化（ADR-0009 が現行実装）**。false は新旧比較スイープ用の退避経路。
> 本 §1 は**旧パイプラインの記録**として残す ── 現行の正本は **§9**。
> 新パイプラインの計算経路は **§9** に別建てで記載（v1・W7 進行中の暫定）。
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
[会心] FireTrigger(OnCriticalCheck); critRatePctAdd += buff "critRatePct"
       ── 2026-09-19: 会心率を % に統一し 5% 刻みに揃えた（旧「分子 1 = 5.5%」は撤去・§2-B）──
       critAdd  = critBaseRate(武器+層補正) + ctx.critRatePctAdd + メタ
       critRate = ResolveCritRate(critAdd)   ← 加算した分がそのまま (0〜1 クランプのみ・§2-B)
       critRate = min(critRate, ctx.lambdaCritRateCap)   ← Λ注意散漫(1f=無効)。変換の"後"に効く
       会心成立 = !critSuppressed && Random.value < critRate
       critSuppressed(無心の刃) → 常に false / forceCritical(末那識・天極・短絡・画竜) → 常に true（最終上書き）
       成立かつ total>0 → FireTrigger(OnCriticalDamage); total ×= criticalMultiplier(既定2.0)
       【狩猟旅団 (契約)】armed状態の脆弱を持つ敵への会心時、 全段に ×(1 + 0.15/0.30/0.45) 倍率を適用 → 脆弱は consumed へ
                          (詳細: docs/GAME.md §12)
FireTrigger(OnPostDealDamage / OnPostReceiveDamage)
【蒼白の槍騎士】fixedDamageMultiplier>1 なら fixedDamageToEnemy ×= 倍率（return前）
return (total, fixedDamageToEnemy, isCritical)
```

> **会心倍率の既定値 (2026-08-03)**
> `MetaBuffApplicator.GetCriticalMultiplier()` が正本で、現在 **2.0**。
> 2026-07-26 に 2.0→3.0 へ上げたが、与ダメ倍率の段別実測で会心段が単独 ×2.18
> （会心率 36% × 実測倍率 3.33）と全修飾チェーン中で最大の増幅源になっていたため差し戻した。
> 実測平均が 3.33 と既定を超えていたのはメタ精密 r10（逓減の捨て分を倍率へ還元）の寄与。
> **精密トラックは 2026-09-10 に、逓減は 2026-09-15 に撤去済み**なので、この経路はもう無い。
> 遺物の会心倍率軸は「既定値の N%」定義なので、この変更で段6 が +2.7 → +1.8 に追随する。

### ③-A 与ダメ確定（プレイヤー勝利・`CombatManager.ApplyWinDamageModifiers`）

```
total = ProcessDamage結果
(画竜点睛) total = ceil((garyoDieValue+10) × criticalMultiplier)   ※発動時上書き・会心確定
outgoingDamageMultiplier += メタ% + 遺物% + 研磨剤1.5 + consDmgMultPct/100   （全部同じプールへ加算）
× outgoingDamageMultiplier        ← パッシブ(OnPreDealDamage で加算済み)/メタ/遺物/研磨剤/鬼火の油 を 1 回だけ掛ける
                                     ※鬼火の油は 2026-09-19 まで倍率適用後に ×(1+X%) を別掛けしていた
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
不抜の聖紋【職業/騎士 2026-06-28】: oathArmed && !oathBroken && HP≥80% → total −= ceil(total×0.4)。
  HP80%未満 (適用前 or この被ダメで割込予定) を検知した時点で oathBroken=true＝以後永久無効
− floorMod.defeatDamageReduction(5層 地獄門 −2)
+ ceil(total × receivedDamageBonus)(亡者の招待 +30%)
÷2 halveFirstEnemyAttack(共助・T1のみ)
playerDamageNegateCharges>0 → total=0(獣の絆)
コントラタック(player_contre): total半減 + 軽減量×2を敵へ反射
− consShield(消費シールド吸収)
consReflect(鏡写し): 吸収後の被ダメ同量を敵へ反射
プレイヤー防御スタンス(ADR-0006): 防御時 total ×=0.5（受け最終-50%・全軽減/シールドの後。反撃等の固定反射は対象外）
痛覚遮断剤【職業/狂戦士 2026-06-28】: painkillerArmedThisTurn && total≥playerHP → total=playerHP−1（HP=1踏みとどまり・チェーン**最後**）
→ (ダメージ適用直前) 仕込み刃【職業/暗殺者 2026-06-28】: daggerArmed && total>0 → total=0 ＋ 同値を enemyHP へ軽減不能反射、daggerArmed=false（1ロール限り）
→ playerHP −= total (+ fixedDamageToPlayer)
  fixedDamageToEnemy>0 は敗北時も敵へ(反撃/Counter/Riposte) / 出血
  痛覚遮断剤 post-clamp: メイン+固定ダメ適用後も painkillerArmedThisTurn 中は playerHP≤0 → 1 に復帰
fixedDamageToPlayer ≥ 999 → playerHP = 0（死の宣告）
```

#### 職業スターター消耗品のダメージ関連効果まとめ（2026-06-28 追加）

| 品 | 職業 | 効果と適用点 |
|---|---|---|
| 瞬間研磨剤 | 剣士 | 次の 1 撃だけ `outgoingDamageMultiplier += 1.5`（与ダメージ+150%）。`ApplyWinDamageModifiers` 内・rollPurity 中は無効・適用後 disarm。**2026-07-27 リワーク**（旧 playerDiceTotal 加算は相互攻撃モデルで無効だった） |
| 不抜の聖紋 | 騎士 | 上記 ③-B チェーン参照。HP80% 維持中のみ被ダメ −40%、一度割れたら戦闘中永久無効 |
| 痛覚遮断剤 | 狂戦士 | 使用ターン致命ダメを HP=1 に clamp（2 経路）。次ターン開始時 (`CombatContext.BeginNewTurn` Phase A) HP=1 なら consDiceRoll+10 / consAtkBurst+10、そのターン終了時に死亡確定 (`ExecuteTurn` 終端) |
| 仕込み刃 | 暗殺者 | 次のロール敗北時、被ダメ 0 化＋同値を enemyHP へ直接減算（軽減不能）。発火後 disarm |

### ③-C 「軽減無視」ダメージとシールドの関係【2026-08-15 確定】

**軽減無視はシールドを貫かない。** 軽減 (%カット・定数カット・基礎防御・スタンス) を無視するだけで、
**被ダメを肩代わりするシールドは機能する**。

適用の実体は `CombatContext.EnemyDealUnmitigable(dmg, cause)` ただ 1 箇所。
敵パッシブはここを通し、`enemyCurrentHP` を直接引く書き方をしないこと。

- **敵視点でのみ呼ぶ。** `PassiveSkillManager.SwapPerspective` は HP/ダイス/勝敗を入れ替えるが
  `consShield` は入れ替えない。敵スキル内でだけ `enemyCurrentHP`=プレイヤーHP /
  `consShield`=プレイヤーの盾 が揃う。プレイヤー側から呼ぶと自分の盾を削る。
- `cause` は被ダメ内訳と死因タグへの計上分類。`null` で計上しない。
- 対照群スイッチ: `CombatContext.ShieldAbsorbsUnmitigable`（既定 true。false は計測専用）。

通す対象は 9 件 ─ 審判の炎 / 王の業炎 / 烈炎 / 大出血 / 毒の侵蝕 / 鏡映の応答 /
13番目の宣告 / 巌の意志の撃破時反撃 / コントラタック。

**この変更が持つ副作用**（同一シード 1000 ラン・対照群比較で実測）:
盾は `ShieldBash` が **残存量の 20〜80% を攻撃力に加算**する常時効果の弾でもある。
チップが盾を毎ターン削るため、5層ボス戦で盾の使い残しが 15.19 → 1.21、
ShieldBash の空振り（盾 0）が 34.2% → 66.1%、1 機会あたりの攻撃ボーナスが 10.18 → 4.59 に落ちる。
結果として同戦闘は 14.7 → 15.6 ターンに伸び、勝率 66.6% → 60.8%。
7層クリア率は 72 → 71 で変化なし (p=1.000)。
**盾の「使い残し」は余剰ではなく火力**という構造を踏まえずに盾の消費先を増やすと、
必ず火力側に跳ね返る。〈盾解放〉が謳う「防御か火力かの二相性」はこの変更で初めて成立した。

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
| `criticalMultiplier`/`isCritical` | 会心倍率(既定**2.0**)/結果 | isCriticalは毎T false、倍率はメタ値で毎T再取得 |
| `critRatePctAdd` | 会心率への加算(小数・0.05=+5%)。**パッシブ・消耗品・バフの会心率はすべてここ** | BeginNewTurnで0 |
| `critSuppressed`【新2026-07-26】 | 会心封印(無心の刃)。forceCritical より優先度が低い | BeginNewTurnでfalse |
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
| `consShield`/`consReflect`/`consDmgMultPct`/`consAtkBurst`/`consCritPct`/`consDiceRoll`/`consEnemyDiceDebuff` | 消費アイテム由来 | 各種 |
| buff `diceBonus`/`damageBonus`/`critRatePct`(小数)/`enemyDiceDebuff` | currentBuffs/nextTurnBuffs経由 | nextTurn→current移行 |
| `accumulatedValues["extraDice"]` | そのターンのダイス数増減(NightLord/精鋭/0d0等) | — |
| `accumulatedValues["extraPursuitDice"]` | 追撃ダイス追加(多頭) | — |
| `accumulatedValues["enemyMaxHPReduction"]` | 相手最大HP減(魔王の威圧) | — |
| `ashenSuddenDeath`/`myokakuSuddenDeath`/`gedatsuPending` | 6層/妙覚サドンデス/解脱 | — |

---

## 2-B. 会心率の変換（正本 `CombatContext.ResolveCritRate`）

**2026-09-15: 逓減を撤去した。** 会心率(%) を float で加算し、**0〜1 へクランプするだけ**。
倍率は **×2.0**（2026-07-26 に 2.0→3.0、2026-08-03 に 2.0 へ差し戻し）。

```csharp
ResolveCritRate(addTotal):
    addTotal <= 0    →  0
    addTotal <  1.0  →  addTotal        // 加算した分がそのまま乗る
    それ以外         →  1.0             // 常時会心
```

**加算が 100% に届けば常時会心が成立する。** 旧カーブ（knee 20% / falloff 1.25・100% へ漸近）は
`forceCritical`（末那識 / 天極 / 短絡 / 画竜点睛）を「確定会心の特権」にしていたが、
その特権は**到達コストの差**に置き換わった ── 特権側は 1 ターンで確定する。

| 加算 | 実 critRate | 期待DPS(×3) | | 加算 | 実 critRate | 期待DPS(×3) |
|---|---|---|---|---|---|---|
| 10% | 10.0% | 1.20 | | 50% | 50.0% | 2.00 |
| 20% | 20.0% | 1.40 | | 80% | 80.0% | 2.60 |
| 30% | 30.0% | 1.60 | | 100% | 100.0% | 3.00 |

> **旧カーブ下の測定値は会心に関して無効。** 同じ加算 50% が 41.8% ではなく 50.0% になり、
> 高会心帯ほど差が開く（旧 100% → 60.0% / 旧 200% → 75.4%）。
> 遺物軸〈会心率〉の引退根拠のうち「逓減が会心ビルドで最も価値を下げる」（`RelicAxis.Retired`）は
> **もう成り立たない**。オーバーキル由来の理由は残るので引退自体は維持している。

**入力の 3 経路**（加算してから 1 度だけ `ResolveCritRate` に通す。**単位はすべて %／小数**・2026-09-19 統一）:
1. `critBaseRate` — 武器の `critRatePct`（items.json・例 11 = 11%）＋ 層補正 `critRatePctBonus`。
   **この 2 つだけを 0〜50% にクランプ**してから渡す（層のマイナスは武器の会心率しか削れない）
2. `ctx.critRatePctAdd` — パッシブ（心眼 +5/10/15/20% など）・消耗品・戦闘中バフ（月蝕 +15% / レイピア解除 +50%）
3. メタ（`GetCritRatePctBonus` − 〈俊敏〉ペナルティ、合計は 0 下限）

> メタ精密トラックは 2026-09-10 に撤去済み（`PrecisionCritRatePct` / `PrecisionOverflowMultiplier`
> はどちらも常に 0）。**「逓減の捨て分を会心倍率へ還元する」極点はもう存在しない**ので、
> 逓減の撤去で失われた機構も無い。
> アイテム側の副次ステータス `subCritRatePct` も 2026-09-15 に全廃（GAME.md §24）。

**天井**: Λ注意散漫 `lambdaCritRateCap` は `ResolveCritRate` の**後**に `min` で効く。
`forceCritical` はこの天井も無視して確定する。

**UI/ログ表示**: `ItemDataV2.CriticalRateLabel()` が判定と同じ `ResolveCritRate` を通すので、表示と判定は常に一致する。
---

## 2-C. ステータス化されたスキル（`StatModifierEffect`・2026-09-19）

**効果が「ステータスへの加算」だけのスキルは、専用クラスを持たない。** items.json の
`passiveSkills[].stats` から汎用の `StatModifierEffect` を作り、**旧クラスと同じ ID・同じトリガー・同じ式**で
1 件ずつ当てる（表示は合算、計算は 1 件ずつ ── 被ダメ軽減は効果ごとに端数処理するので合算すると結果が変わる）。

| stat | トリガー | 式 |
|---|---|---|
| `atk` | OnPostRoll | `AddPlayerAttackOrDiceBonus(v)` |
| `dmgPct` | OnPreDealDamage | `outgoingDamageMultiplier += v/100`（未初期化の 0 は 1.0 に直してから） |
| `nonCritPct` | OnPreDealDamage | `finalDamage > 0` なら `nonCritOutgoingMultiplier += v/100` |
| `critPct` | OnCriticalCheck | `critRatePctAdd += v/100` |
| `critMul` | OnCriticalCheck | `criticalMultiplier += v` |
| `critSure` | OnCriticalCheck | `forceCritical = true`（会心確定。乱数は通常どおり消費し ProcessDamage の最後に上書き） |
| `dmgTaken` | OnPreReceiveDamage | `finalDamage > 0` なら `finalDamage = max(0, finalDamage + v)`（v は負） |
| `dmgTakenPct` | OnPreReceiveDamage | `finalDamage > 0` なら `finalDamage = max(0, ceil(finalDamage × (1 + v/100)))`（v は負・端数切り上げ） |
| `shieldStart` | OnBattleStart | `consShield += v; shieldGainedTotal += v` |
| `regen` | OnTurnEnd | `playerCurrentHP = min(max, cur + v)` |
| `lifesteal` | OnTurnStart | `lifestealPct += v/100` |

**% の値は生成時に 1 度だけ float へ確定させる。** Mono は float 演算を double 精度のまま進めることがあり、
実行時に `v/100f` を足すと float の `0.2f` ではなく double の `0.2` が足されて、最下位ビットの差が
端数処理の境目で結果を割る（2026-09-19 に 10,000 ラン中 2 ランのダイジェストがずれた原因）。

ステータス化済み（段 1・常時 17 件）: MightI / FortitudeI / InsightI・II / VitalityI / CoarseWhetstone /
DoubleStruck / BackingPlate / GatewardStance / LifestealII / HeavyStrike / BatteringRam / BluntWeapon /
SwordReachII・III / BulwarkII・III。 置き換え前と同一シード 10,000 ランで決定性ダイジェスト 10,000/10,000 一致。

**条件 (`stats[].when`・段 2)。** 書いた項目はすべて AND。判定は**そのステータスのトリガーの時点**（旧クラスと同じ）。

| when | 成立条件（式は旧クラスのまま） |
|---|---|
| `hit` | `finalDamage > 0`（旧クラスの「与ダメ 0 なら何もしない」ガード） |
| `solo` | `!isPairEncounter` |
| `turnMax: N` | `currentTurn ≤ N` |
| `firstRoll` | `isFirstRoll` |
| `hpPctMax: N` / `hpPctAbove: N` | `playerMaxHP > 0` かつ `cur×100 ≤ max×N` / `cur×100 > max×N`（段階を排他にする下限） |
| `enemyHpPctMax: N` | `enemyMaxHP > 0` かつ `enemyCur×100 ≤ enemyMax×N` |
| `behind` | 両 maxHP > 0 かつ `cur/max < enemyCur/enemyMax`（float） |
| `hitLastTurn` | 前ターンの OnPreReceiveDamage で `finalDamage > 0` だった（`nextTurnBuffs["hitArmed:<ID>"]`、自分の軽減より前の値で判定） |
| `enemyPoisoned` / `enemyBleedMin: N` | 敵の毒 > 0 / `enemyBleedStacks ≥ N` |
| `overcharged` | `UseMutualAttackPipeline && IsOvercharged()` |
| `allEven` / `kaleido` | 出目が全て偶数（1 個以上）/ 全同値・全て異なる・連続 3 個以上 のいずれか（2 個以上） |
| `weaponPlusMin: N` | `run.weaponPlus ≥ N` |
| `strongFoe` | `MapManager.CurrentNode.type` が EliteBattle か Boss |
| `hopeTierMin: 名` / `hopeTierBelow: 名` | 希望段階（HopeTier・数字が大きいほど希望が低い）が 名 以上 / 名 未満 |
| `nonCritStreakMin: N` | 連続非会心数 ≥ N。OnPostDealDamage で 会心→0 / 非会心→+1（`accumulatedValues["nonCritStreak:<ID>"]`） |

**固有部分を持つスキル**（〈処刑〉のダイス潰し）は専用クラスに固有部分だけを残し、`StatModifierEffect` が
それを包んで同じ ID で登録する（同じトリガーでは クラス → ステータス の順）。

ステータス化済み（段 2・条件付き 19 件）: SwiftHandII・III / DuelistI・II / Riposte / Destiny / BloodDecree /
Execute（与ダメ%部分）/ IndomitableIII / Overload / SingleMinded / Stubbornness / VenomBlade / BloodScent /
Hermes / Bloodlust / EvenEyes / MasterworkNotes / KaleidoDice。

**アイテム側から移したもの（2026-09-19）**: 末那識 / 狂宴の仮面 / 英雄の意志 / 激情の刃。旧 ITimedEffect（OnRoll・CombatStart）
から、パッシブと**同じタイミング**で加算するステータスへ。与ダメ% はもともと同じ加算プールだったが、
加算の時点と条件の判定時点をパッシブに揃えた。パッシブスキルとして登録されるので〈共鳴〉の数・〈迷妄〉の対象・
同名 1 回の規則にも入る（挙動の変更＝同一シードのペア比較で確認）。

**与ダメ% は全経路が 1 つの加算プール**（`outgoingDamageMultiplier`、BeginNewTurn で 1.0）。パッシブ・メタ・遺物・
研磨剤はここへ足し、`ApplyWinDamageModifiers` で 1 回だけ掛ける。別に掛かるのは 非会心ダメ%（独立ステータス）だけ。
消耗品〈鬼火の油〉（`consDmgMultPct`）も 2026-09-19 にプールへの加算へ直した（旧: 倍率適用後に別掛け）。

**パッシブ id（2026-09-19〜）**: パッシブ品のパッシブ id は**品の id**（`LifestealII` → `Lifesteal_2`、`BountyHunterIII` → `BountyHunter_3`、`Overload` → `過負荷` 等）。専用クラスの `SkillId` も品の id に揃えた（クラス名は旧名のまま）。武器ラダー（`SwordReachIII` 等）は items.json 最上位 `skills` 表の id で、変わっていない。下の §3 の表は旧 id の家系表記のまま ── 品の id は items.json の `skill`（パッシブ名）で引ける。

下の §3 の表の該当行は、効果量の記録として残している（実装は本節）。

## 3. プレイヤーパッシブ一覧（`AllPassiveSkillEffects.cs`）

### 3-A. 汎用パッシブ（Tier制・ショップ/ドロップで段階的に強化）

| SkillId(表示) | Tier/レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| Pursuit I/II/III/IV(追撃) | B/S/G/L | OnPreDealDamage | finalDamage += 2/4/6/8 |
| Counter I/II/III/IV(反撃) | B/S/G/L | OnRollLose | fixedDamageToEnemy += 2/4/6/8（2026-06-03 リバフ 1/2/3/4→2/4/6/8） |
| Might I/II/III/IV(剛力) | B/S/G/L | OnPostRoll | playerDiceTotal += 2/3/4/5（固定・ダイス数非依存） |
| Fortitude I/II/III/IV(頑強) | B/S/G/L | OnPreReceiveDamage | finalDamage −= 1/2/3/4 (min0) |
| Insight I/II/III/IV(慧眼) | B/S/G/L | OnCriticalCheck | critRatePctAdd += 0.05/0.10/0.15/0.20 |
| Vitality I/II/III/IV(活力) | B/S/G/L | OnTurnEnd | 自HP +1/2/3/4 |
| Indomitable I/II/III/IV(不屈) | B/S/G/L | OnPreReceiveDamage | 自HP ≤ 20/30/40/60% で `finalDamage = ceil(finalDamage × 0.7)`（2026-07-15 リワーク。III はステータス化 §2-C） |
| ShieldBash I/II/III/IV(シールドバッシュ) | B/S/G/L | OnPreDealDamage | **攻撃時 `finalDamage += ceil(consShield × 0.20/0.40/0.60/0.80)`**。IV のみ加算後に `consShield += 5`。※旧仕様 (勝利時に与ダメ%をシールド化＝`shieldOnWinPct`) は 2026-07-15 リワークで廃止。**盾は消費されず「残存量」を読むだけ**なので、盾を持ち続けること自体が火力になる ─ ③-C 参照 |
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
| Parry(パリィ) | OnPreReceiveDamage | **3ターンに1度 finalDamage=0**（`accumulatedValues["parry_ready_turn"]` でクールダウン管理）。※旧記述「scratch 無効化＝no-op」は**誤り**だった（2026-09-05 実コードで確認・訂正） |
| HolyShield(衛士の慣い) | OnBattleStart/OnTurnEnd | **2026-09-05 リワーク**: 開幕 consShield +10 / ターン終了時 consShield<3 なら +4。※旧「HP半分を境に 被ダメ−20% / 与ダメ+20%」は廃止 |
| Riposte(切り返し) | OnPreReceiveDamage/OnPostRoll/OnPreDealDamage | **2026-09-05 リワーク**（ステータス化 §2-C `hitLastTurn`）: 被弾で `nextTurnBuffs["hitArmed:Riposte"]`、次ターン 攻撃+7 / outgoing+20%。※旧「被ダメ50%を反射」は盾家系〈反攻〉へ移管 |
| VoidStance(虚空) | OnPostRoll | ｜playerDiceTotal−enemyDiceTotal｜≤3 → nullifyAllDamage + fixedDamageToEnemy +=3（自前で差を算出） |
| Frenzy(復讐) | OnPreReceiveDamage/OnPostRoll | **2026-09-05 リワーク**: 被弾で蓄積+1(上限10)、PostRoll で `AddPlayerAttackOrDiceBonus(蓄積×2)`。※旧 `finalDamage += 蓄積`(最大+10) は桁が合わず実質無効だった |
| BloodDecree(血令) | OnPreDealDamage/OnPreReceiveDamage | **2026-09-05 リワーク**: 自HP割合 < 敵HP割合 のとき outgoing+50% / 被ダメ×0.85。※旧「ゾロ目で×2.5を軽減不能へ置換」は**発火率 1/1296 で事実上死んでいた** |
| Destiny(果たし合い) | OnCriticalCheck/OnPreDealDamage/OnPreReceiveDamage | **2026-09-05 リワーク**: `isPairEncounter==false` のとき 会心率+15% / outgoing+25% / 被ダメ×0.85。※旧「全ダイス最大で×2・全ダイス最低で被ダメ0」は**発火率 1/1296 で事実上死んでいた** |
| Execute(処刑) | OnRollWin/OnPreDealDamage | 勝利時 次ターン敵の**最大**ダイスを1に固定（クラス）。加えて 敵残HP ≤25% で outgoing+80%（2026-09-05 増補・ステータス §2-C） |
| Nightfall(蝕夜) | OnPostDealDamage/OnBattleStart | overDamage×2を永続蓄積→戦闘開始時 fixedDamageToEnemy |
| Sting(出血) | OnPostDealDamage | enemyBleedStacks++ |
| 脆弱(契約・狩猟旅団) | OnBattleStart (Apply) + 防御後最終ダメ | armed状態で会心ダメ時 ×(1+0.15/0.30/0.45) 倍率→consumed / 非会心ロール勝利で再armed。 戦闘終了で剥がれる。 仕様: docs/GAME.md §12 |
| ApexCrit(天極) | OnCriticalCheck | ゾロ目(全同値)なら **forceCritical=true**(会心確定)＋criticalMultiplier+1.0 |
| Lifeline(命脈) | OnBattleStart + OnPostReceiveDamage | 戦闘開始時 maxHP×10% 回復／1戦闘1回 HP≤50% で consShield += ceil(maxHP×0.5) |
| Resonance(共鳴・LEG) | OnPreDealDamage | 発動中パッシブ数 × 0.01 を outgoing 加算 (2026-09-20: 旧 over5 × 0.05) |
| Greed(貪欲・ダイス) | OnTurnStart | lifestealPct = 0.1 |
| Eternal(永劫・ダイス) | OnPostRoll/OnBattleEnd | playerDiceTotal += min(5, eternalStacks/10)、勝利でstack++保存(ラン跨ぎ) |
| TenkouKaibutsu(天工開物) | (なし) | no-op(効果は GameManager.TryUpgradeWeapon の素材返還) |
| Bloodlust(背水の狂刃) | OnPreDealDamage | HP≤50% → outgoing+=0.3、≤25% → +=0.8 (2026-05-31 outgoing移行) |
| Hermes(ヘルメスの靴) | OnPostRoll | 初回ロール `AddPlayerAttackOrDiceBonus(5)`（攻撃+5・ステータス化 §2-C） |
| GoldKingBlade(黄金卿の剣) | OnPreDealDamage | outgoing += 0.01×coinsSpent (100Gで+1.0=×2倍相当、 上限なし、 2026-05-31 v3 消費Gold基準) |
| MugaMushin(無我無心) | OnBattleStart | rollPurity=true(diceBonus等の補正拒否) |
| GaryoTensei(画竜点睛) | OnPostRoll | 出目が最大面 → garyoProc=true,garyoDieValue。即勝利+(出目+10)×criticalMultiplier会心確定 |

### 3-B2. 武器家系 専用ラダー（`WeaponFamilyEffects.cs`・2026-09-05）

**武器の passiveSkills（items.json）は実行時に読まれない。** 進行武器（`sword/axe/dagger/shield_tN`）は
`RunPassiveSync` が `WeaponProgression.Compute(weaponId, plus)` で算出したものを動的付与する。
items.json 側は**ツールチップ表示専用**なので、両者を必ず同期させること。

旧構成は共通ラダー（筋力/追撃/心眼/頑強/活力）の借用で、剣=筋力+追撃 / 斧=筋力+心眼 と
**半分が同じ**だった（アイテム側家系システムの名残）。家系ごとに専用ラダー2本へ置換。

| 家系 | 性格 | ラダーA (T1〜) | ラダーB (T2〜) | 固有1 (T3) | 固有2 (T4) |
|---|---|---|---|---|---|
| 短剣 | 短期決戦・一撃必殺＋DOT | 疾手 | 毒手 | 処刑 | 蝕夜 |
| 剣 | タイマン力＋バランス | 間合 | 一対一 | 切り返し | 果たし合い |
| 盾 | 生存＋カウンター | 城壁 | 反攻 | パリィ | 衛士の慣い |
| 斧 | 削り合いレース＋自己バフ | 猛り | 大鉈 | 復讐 | 血令 |

| SkillId(表示) | Trigger | 効果(ctx操作) |
|---|---|---|
| SwiftHand I/II/III(疾手) | OnPostRoll | `currentTurn ≤ 3` のみ `AddPlayerAttackOrDiceBonus(3/5/8)`。4T目以降は0 |
| VenomHand I/II/III(毒手) | OnBattleStart/OnPostDealDamage | 開幕 `AddStatus(Enemy,"poison",2/3/4)`。以降 `isCritical` のターンに poison +1 |
| SwordReach I/II/III(間合) | OnPostRoll/OnPreReceiveDamage | 攻撃 +2/+3/+5 かつ 被ダメ −1/−2/−3（常時） |
| Duelist I/II/III(一対一) | OnPostRoll/OnPreReceiveDamage | **`isPairEncounter==false` のみ** 攻撃 +3/+5/+8 / 被ダメ −1/−2/−4 |
| Bulwark I/II/III(城壁) | OnPreReceiveDamage/OnTurnEnd | 被ダメ −2/−4/−7 かつ ターン終了時 自HP +1/+1/+2 |
| Retaliation I/II/III(反攻) | OnPreDealDamage | `fixedDamageToEnemy += ceil(blockSumThisTurn × 0.4/0.7/1.0)` |
| Fervor I/II/III(猛り) | OnPostRoll | `AddPlayerAttackOrDiceBonus(min(currentTurn−1, 5/9/14))` |
| Cleaver I/II/III(大鉈) | OnPreDealDamage | `outgoing += 敵が失ったHP割合 × 0.30/0.50/0.80` |

**発火順序の約束（間違えると黙って無効になる）**: 攻撃値への加算は **OnPostRoll** で
`AddPlayerAttackOrDiceBonus` を使う。`atkBase` は §9.2 step5（配線）の直後に確定するので、
OnPreDealDamage で `mutualAttackBonus` を足しても**一切乗らない**。
与ダメ% は OnPreDealDamage、被ダメ軽減は OnPreReceiveDamage、会心率は OnCriticalCheck。

**新規フィールド**:
- `ctx.blockSumThisTurn` [perTurn] — そのターン Block 端子へ配線した**出目合計**（本数ではない）。
  CombatManager の配線集計が貫通適用後に set、BeginNewTurn Phase B で 0。
- `ctx.isPairEncounter` [persistent] — 2体戦か。CombatManager が戦闘開始時に1度だけ set。

**設計上の禁止事項**: 「全ダイス同値／全ダイス最大」を発火条件にしないこと。
素の6面×5個で `6/6^5 = 1/1296`、1ラン260ターン前後を回して期待 0.2 回 ＝ **5ランに1回**。
旧・血令（ゾロ目で×2.5を軽減不能）と旧・運命（全ダイス最大で×2）が両方これで死んでいた。

### 3-C. 固有パッシブ（`PassiveItemEffects.cs`・ITimedEffect系。TimedEffectManager が CombatStart/OnRoll/OnTurnEnd/CombatEnd/OnMapMove で発火）

| 名称 | レア | Trigger | 効果(ctx/run操作) |
|---|---|---|---|
| 灯心の鈴 | B | OnMapMove | 10%で空腹度+1 |
| 安らぎ/癒し/神聖の靴 | B/S/G | OnMapMove | 歩行毎 playerHP +1/2/3 |
| 巡礼者の杖 | S | CombatEnd | 50%で空腹度+1 |
| 希望の灯片 | S | CombatEnd | HealPlayer(3) |
| 食通の懐刀 | S | (イベント) | イベント由来の空腹回復+1（EventEffectExecutor） |
| 黄金の天秤 | G | CombatEnd | 勝利時 coins +5 |
| 末那識(旧 死神の予感) | L | OnCriticalCheck/OnPreDealDamage | HP≤35% → forceCritical=true ＋ outgoing +60%（会心確定・会心率と天井を無視）。**2026-09-19 ステータス化 §2-C**（旧 OnRoll の ITimedEffect） |
| 鋼の心臓 | L | CombatEnd | HealPlayer(5)（取得時に最大HP+20ボーナス別途） |
| 災厄の指輪 | L | OnRoll | 被弾毎に次の与ダメ+2累積(上限+10・戦闘終了リセット) finalDamage加算 |
| 永遠の燈 | L | CombatEnd | HP≤10 → HealPlayer(20) |
| 商人の符牒 | L | (ショップ連携) | ショップ系フック（PassiveItemRegistry 非登録） |
| 巡礼の杖飾り【新2026-06-03】 | B | OnMapMove | 25%で希望+1（HopeSystem.ApplyFood） |
| 狂宴の仮面【新2026-06-03】 | S | OnPreDealDamage | 希望[悲観]以下 outgoing+0.10 ／[絶望]以下 +0.25。**2026-09-19 ステータス化 §2-C**（旧 OnRoll の ITimedEffect） |

> **2026-07-18 削除済み**（`PassiveItemEffects.cs` から class ごと削除・items.json からも撤去）:
> 記憶の砂時計 / 死神の数珠 / 嵐の徽章 / 沈黙の剣帯 / 狂乱のメダリオン / 静寂のローブ / 黒煙の符 /
> **蒼穹の眼** / 守護天使の鈴 —— 本表からも 2026-07-26 に削除（ドリフト解消）。

### 3-D. 2026-06-03 追加の汎用パッシブ（`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| EvenEyes(賽振りの目隠し) | B | OnPreDealDamage | 全出目が偶数 → outgoing +=0.15 |
| MasterworkNotes(匠の手控え) | G | OnPreDealDamage | run.weaponPlus ≥ 3 → outgoing +=0.12 |
| KaleidoDice(万華の賽) | L | OnPreDealDamage | 全同値/全相異/階段(3+)のいずれか → outgoing +=1.0 |

### 3-E. [剣の舞] セット（2026-06-04・`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

Passive カテゴリのシナジー武器群。4枚がインベントリに揃うと全消滅し〈ブレイドダンス〉に変化（`GameLoop.SwordDanceSet`）。
ダイス合計加算は OnPostRoll（RecomputeDiceTotals 後で確実に効く）。run 参照は `GameManager.Instance.Run`。

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| SaberWaltz(サーベル・ワルツ) | B | OnBattleStart / OnPostRoll | playerDiceTotal +1。他の[剣の舞]がインベントリにも昇華にも無い時、開戦時に playerCurrentHP を半減。ショップ出現率上昇は ShopManager フック |
| EspadaPasodoble(エスパーダ・パソドブレ) | S | OnPostRoll / OnPreReceiveDamage | playerDiceTotal +5・enemyDiceTotal +5・outgoing +=0.2／被弾時 finalDamage ×1.2（被ダメ+20%） |
| FleuretBallet(フルーレ・バレエ) | B | OnPostRoll | playerDiceTotal +3。敗北時の自壊+最大HP1生還は `LastStand.TryConsumeRevival`（灯火→ラストスタンド→フルーレ の順） |
| FalconTango(ファコン・タンゴ) | L | OnPostRoll | 攻撃 +2（[剣の舞]。4 枚揃うと〈無銘の剣舞譜〉へ変化） |
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
| ChainApex(連環の極み) | L | OnCriticalCheck / OnCriticalDamage | Check: `criticalMultiplier += 0.2 × accumulated["chainApexStacks"]` ／ Damage: スタック+1（戦闘中持続） |

> 数値は暫定（BOT オートランで要チューニング）。`criticalMultiplier`/`critRatePctAdd` は毎T再取得・0リセット
> のため、倍率/率への加算は **OnCriticalCheck で毎ターン再適用**する。`accumulatedValues` は戦闘開始でのみ
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
| JudgmentFlames(審判の炎) | OnTurnEnd | `dmg=min(ChipCap, 1+ceil(currentTurn/3)+罪)`、実プレイヤーへ軽減無視（罪=totalBattles/8,max2 / ChipCap 基準 10・BossTuning で可変）。ターン係数は 2026-07-28 に 1→1/2、2026-08-15 に 1/2→1/3 へ緩和（15T 累積 108→90・上限は T≥19 まで届かない）。**シールドは肩代わりする**（③-C） |
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

### 〈門〉の不完全起動（`GameLoop.GateFlaws` / GAME.md §4-3）

**2026-09-14: 旧 SinAltar 由来の敵パッシブ 3 種（boss6_golgotha / boss6_severed_time /
boss6_ashen）は撤去した。** 罰は敵強化ではなく**プレイヤー側の規則**になったので、
パッシブ表には載らない。効くのは門をくぐった後の戦闘＝ 8 層ヴェスカ戦のみ。

| 欠陥 | 掛かる位置 | 効果 |
|---|---|---|
| 〈不完全な修復〉 | `ExecuteTurnMutual` ターン終了（充電の獲得より後・次Tの消費より前） | `ConsumeHalfCharge()`（切り捨て）。**支出には計上しない** ── `ConsumeCharge` を使うと〈短絡〉と充電経済の計装が汚れる |
| 〈不完全な起動〉 | `SanitizeWiring`（配線集計の直前） | 1 端子あたりの接続本数が **2 本まで**。武器は全て 5 ダイスなので 2+2+1 を強制される。超過分は最も空いている端子へ移す |
| 〈不完全な転移〉 | `blockSum` 確定後・〈貫きの錐〉の**直後** | `blockSum -= floor(blockSum × 0.50)`。錐（敵攻撃値に比例）と違い**ブロック量に比例**するので、厚く配線した分は必ず一定割合残る。両方掛かるときは錐で削られた残りへ乗る |

> 〈不完全な転移〉の 0.50 は**未計測の仮置き**。8 層ボス戦のブロック／被ダメ分布を採ってから決めること。

---

## 6. Λ層（時間の狭間）由来の恒久デバフ（`GameLoop.Lambda.LambdaDebuffEffects`）

5層ボス撃破後〈決意〉以上で強制突入。環状線を周回するたび「次元の乱れ」+1、3毎に下表からランダムに1つ付与（同種再付与で段階+1、最大3）。`run.lambdaDebuffs`(id→段階)に格納し、戦闘開始時に `CombatManager.StartCombatInternal` で `ctx.lambda*` フィールドへ反映（戦闘スコープで保持・覚者連戦でも維持）。lv1/2/3 = 効果の3段階。

| デバフ | lv1/2/3 | 実装フック | 効果 |
|---|---|---|---|
| 重い足取り | -2/-4/-6 | `RecomputeDiceTotals`(currentTurn==1) | 1ターン目のみ playerDiceTotal をデルタ分減算(下限0・無我無心中は無効) |
| 微妙な手応え | 0.95/0.90/0.85 | `ApplyWinDamageModifiers` **段 G**(挑戦〈練度不足〉と同じ場所) | **2026-08-09: 同種デバフの重ねがけを禁止**。 挑戦〈練度不足〉と `Mathf.Min` を取り、 **厳しい方 1 つだけ**を乗算する。 旧実装は段 G で練度不足・段 K で微妙な手応えを別々に掛けており、 T3×lv3 で 0.89×0.85 = 0.756 まで落ちていた。 Λ は 6 層への強制通過点なので高難易度では必ずこの二重取りを踏む。 なお**合計 (−26% → 0.74) は乗算 (0.756) より厳しい**ので、加算化は緩和にならない |
| 苛立つ強敵 | 5/4/3 T間隔 | `RecomputeDiceTotals`(末尾) | enemyDiceTotal += floor(currentTurn / interval)（累積・勝敗判定前） |
| 注意散漫【2026-07-27 改】 | cap 0.30/0.20/0.10 | `ProcessDamage`(`ResolveCritRate` の後) | **実効会心率そのもの**の上限。`ResolveCritRate` の後に効くため レガシー分子/critRatePctAdd のどちら経由でも貫通できない。`forceCritical` は無視して確定。旧「会心分子 8/6/4 clamp」から置換 |
| 慈悲の処刑 | 0.05/0.10/0.15 | `CombatManager`(被弾後・combatLethalThisTurn確定前) | 被弾したターンに playerHP ≤ playerMaxHP×閾値 なら playerHP=0（回復蘇生を防ぐ） |
| 神経錯乱 | 3/5/7 | `ItemUseHandler.UseItem` | CurrentCombatTurn < lockUntil の間 消費アイテム使用不可 |
| 迫りくる死 | -/-/HP=1 | `StartCombatInternal` | lv3 のみ戦闘開始時に playerHP=1（lv1/2は無効＝実質3スタック猶予） |

- ctx フィールド: `lambdaFirstTurnDiceDelta`(0=無効) / `lambdaIrritatingInterval`(0=無効) / `lambdaDamageDealtMult`(1.0=無効) / `lambdaCritRateCap`(1f=無効) / `lambdaMercifulExecThreshold`(0=無効) / `lambdaConsumableLockUntilTurn`(0=無効)。BeginNewTurn ではリセットしない。
- 即死コンボ（意図通り）: 慈悲の処刑 ＋ 迫りくる死(lv3) = 戦闘開始HP1 → 初回被弾で確定死。

---

## 9. ADR-0009 相互攻撃パイプライン（v1・W7 進行中）

**切替**: `CombatManager.UseMutualAttackPipeline` で `ExecuteTurnMutual()` に分岐する。
**既定 true（2026-07-27〜）** ── 本節が現行の正本。false にすると §1 の旧パイプラインに戻る
（AutoRunner の `useMutualAttackPipeline` トグルで新旧比較スイープに使う）。
`ExecuteTurn()` と `ExecuteTurnMutual()` は終端処理を `FinishTurnCommon()` で共有する。

### 9.1 ターン構造（ADR-0009 柱2）

```
0. ターン開始       : BeginTurn / DoT / OnTurnStart / SyncHP / フロア敵回復
1. 敵ロール         : スタンスなし → 敵ダイスを振る
3. 自ロール         : 自ダイスを振る（旧法と同一の RollDice）
4. リロール（任意） : MaybeRerollPlayerDice（配線前のみ、目的関数は暫定=旧勝率のまま）
   時限効果 OnRoll / PassiveItemManager.OnRoll / メタバフ +1
   ProcessPostRoll（パターン/ゾロ目・合計確定） + OnPostRoll
2. 予告             : Escalation.EnemyAttackValue(currentEnemy, turn, ctx.enemyDiceTotal)
5. 配線             : WiringPolicy(playerDice, tele) → ダイス index → 端子（Attack/Block/Charge）
   未配置は Attack へ自動合流（`mutualWiring[i] = Attack` 初期化）
   SanitizeWiring: ①特殊端子の接続制限 ②〈綻び〉の封印端子 ③〈不完全な起動〉の 1 端子 2 本上限
   （**方策/UI を信用しない**。制約を破った配線が来たら本体側で移す）
6. 解決             : 下記 9.2（自先制・撃破時は敵攻撃なし）
   収支トリガー     : balance = 与ダメ − 被ダメ → OnRollWin/Lose/Draw を再発火
7. ターン終了       : FinishTurnCommon（旧法と共用・順序不変）
```

### 9.2 ダメージ計算（`ExecuteTurnMutual`）

```
リロール (ADR-0010・配線より**前**)
         RerollPhase: 充電を払ってダイスを振り直す。 コスト = 振り直す個数 × そのターンで何回目か。
         対象は RerollPolicy が決める (null なら振り直さない)。 予告は素の敵ダイス合計から
         仮組みして渡すので、 **完全情報のまま**判断できる (ADR-0009 柱3)。
         〈中階〉が前ターンに成立していれば 1 回目が無料 (currentBuffs["roleFreeReroll"])。

役 (ADR-0010・配線集計の直後 / 攻防の解決より**前**)
         ResolveRoles: 手札役 (5個全体) / 端子役 (攻撃・ブロックの各組) / 配線役 (盤面) を判定し、
         **既に使った役を除いて** RolePolicy に切るものを選ばせる。 **1 戦闘 1 役 1 回・発動は任意**。
         同じ役が 2 端子で成立したら **1 回消費で両方発動**する。
         効果は既存フィールドへ加算する形に寄せてある (正本は YachtRoleEffects.Apply)。
           attackSum / blockSum への加算 … 散・対・偶(攻)・満・均
           ctx への加算                  … 奇(会心率+25%) / 小階(貫通+30%, 敵次T攻撃-3) /
                                            束(攻=forceCritical) / 飛階(攻=fixedDamageToEnemy) /
                                            偶(防=consShield) / 飛階・奇(防=vescaShieldReflectRate) /
                                            二対・中階・相殺(AddCharge) / 拮抗(outgoingDamageMultiplier +0.5)
           盤面フラグ (RoleOutcome)      … 極(最大HP の 出目合計×N% ・通常 N=4 / ボス N=2) /
                                            大階・相殺(被ダメ0) / 束(防)(被ダメ半減) /
                                            大束(敵が次T行動不能) / 中階(次Tリロール1回無料)

自攻撃   atkBase   = playerAttackPower + Σ(attack 端子の出目) + ctx.mutualAttackBonus
         → 臨界爆発 (予約消費): rinkaiBurstActive なら fixedDamageToEnemy += rinkaiBurstDamage
           (rinkaiCritOnBurst なら forceCritical=true。消費後 false に戻す)
         → ProcessDamage(atkBase, pursuitDamage, playerCriticalNumerator)
         → ApplyWinDamageModifiers（旧勝利分岐と同一チェーン）
         → 希望疲労（15% で**最終ダメージ半減**。 isCrit は落とさない）
         → 敵HP -= dealtDmg + dealtFixed
         → 鋼の皮膚
         → **役の削り**: 極(instantWin フラグ + yachtChunkPct)。 **鋼の皮膚より後**に置く
           ── 「軽減・シールドを無視して削る」を謳う役なので 1HP 踏みとどまりに吸われると文面が嘘になる
           ※〈過不足なし〉(exactLethal) は 2026-08-22 に〈拮抗〉(与ダメ+50%) へリワークされ廃止。
             成立条件が「攻撃合計 = 敵残HP」でスケールが合わず、実測 1000ラン で 発動 0 だったため。
         → 出血 / 吸血 / シールドバッシュ（旧勝利分岐と同一）

敵攻撃   〈大束〉が前ターンに成立していれば **このターンの敵攻撃は丸ごと不発**
         (currentBuffs["roleEnemyStun"])。 予告は既に出ているので「見えている一撃を踏み倒す」形。

         enemyAtk  = Escalation.EnemyAttackValue(e, escTurn, enemyDiceTotal, currentTurn)
                    = round(段階倍率(escTurn) × (baseAttack + enemyDiceTotal) × 大技倍率(currentTurn) × ボス倍率)
                     大技倍率 (2026-09-19・EnemyData.heavyPeriod > 0 の敵だけ):
                       currentTurn % heavyPeriod == 0 → heavyMul (大技) / それ以外 → heavyWindupMul (溜め)
                       周期は冷却材の遅延を受けない実ターンで数える。 予告に turnsToHeavy / heavyMul を出す。
                       対象: 5 層以降のボス (5 層・5 層裏・ヴェスカ p1〜p4)。 6 層は〈業火の予兆〉が同じ役なので無し。
                     ボス倍率 = 挑戦〈天変地異〉(ボスのみ)
         blockSum  = Σ(block 端子の出目) + 端子調律メタ(本数×N) + 特殊端子
                     → 〈貫きの錐〉: blockSum −= enemyAtk × PierceAwlPerAttack
                     → 〈不完全な転移〉(門・§4-3): blockSum −= floor(blockSum × 0.50)
         lossBase  = max(0, enemyAtk − blockSum)
         → ProcessDamage(lossBase, 0, enemyCriticalNumerator, attackerIsEnemy: true)
           ※ attackerIsEnemy: true = **会心判定を飛ばす**。 これが無いと敵の攻撃が
             プレイヤーの会心率で会心し、 プレイヤーの criticalMultiplier で増幅される
         → ApplyLossDamageModifiers（旧敗北分岐と同一）
         → nullifyAllDamage (虚空)
         → **役の被ダメ側**: 大階・相殺(0) / 束(防)(半減)。 虚空と**同じ位置・同じ対象**
           (敵の直接攻撃パケットのみ)。 fixedDamageToPlayer は別チャネルなので触らない
         → 仕込み刃（旧法と同一）
         → プレイヤーHP -= takenDmg (+ fixedDamageToPlayer)
         → 痛覚遮断剤 post-clamp / Counter 固定ダメ

充電     chargeSum = Σ(charge 端子の**出目**)      ← 個数ではない
         → ctx.AddCharge(chargeSum)  (上限 ChargeMax=**30** + メタ種火r2 で +5)
         ※ ChargeMax は ADR-0010 で 10→30。 充電が**リロールの原資**になったため
         ※ chargeDiceCount(個数) も併算されるが AddCharge には渡らない
           (2026-08-03 確認: CombatManager.cs の `if (chargeSum > 0) ctx.AddCharge(chargeSum);`)
         消費: Spark(1消費→+5) / LightningStrike(3消費→与ダメ+30%) がパッシブ側で ConsumeCharge
         過充電: charge >= CombatContext.OverchargeThreshold (=7) で IsOvercharged()=true
                 (Overload: 過充電中 outgoing+30% / ShortCircuit: 過充電3T継続で会心確定+全消費)

臨界     (Rinkai メーター・2026-07-16 追加。rinkaiThreshold>0 = いずれかの臨界パッシブ所持時のみ)
         解決後: meter += attackSum (+ 攻撃配線Tなら rinkaiRadiationBonus)
                 (rinkaiConductionEnabled なら + takenApplied)
         meter >= rinkaiThreshold (基準50 / LowerThreshold で35) 到達で
           → 次T爆発予約 (rinkaiBurstActive=true)、meter = rinkaiAfterglow (基準0 / Afterglow=20 / NoCooldown=threshold/2)
         爆発 flat = rinkaiBurstDamage (基準50 / CriticalPressure=80)
         **2026-08-03 修正: 爆発は atkBase ではなく fixedDamageToEnemy へ入る。**
           旧実装は atkBase へ加算していたため、 その後の会心 ×3.0 と outgoingDamageMultiplier
           が丸ごと乗り、 「flat 50」が実効 150〜300 になっていた。 rinkaiCritOnBurst 持ちは
           会心が確定するので ×3.0 が保証され、 さらに悪化していた。
           固定ダメ枠へ移したことで倍率が乗らなくなった代わりに、 敵の基礎防御%も通らなくなる。
         遺物 (§15-5) の臨界軸 (爆発時に敵の現在HP の N%) も同じ枠へ入る。
```

### 9.3 収支トリガー（柱2 の再定義・§0 の敵視点は変わらない）

`balance = (dealtDmg + dealtFixed) − takenApplied`（takenApplied = 主被ダメ + fixedDamageToPlayer）
を解決後に算出し、`ctx.playerWonRoll = balance > 0` / `playerLostRoll = balance < 0` を上書きしてから
`OnRollWin/Lose/Draw` を再発火する。既存 79 箇所のトリガー効果は無改修で「収支トリガー」として動く。
副作用: 出目参照系は `ProcessPostRoll` の時点で発火するため、収支確定前の `playerWonRoll` を
参照する効果があると値が古い（W7 後段で個別判定）。

### 9.4 Escalation プロファイル（`Escalation.cs`・stage は T5/10/15/20 で 0→1→2→3→4）

| プロファイル | 段階倍率（`stage 0..4`） | 用途 |
|---|---|---|
| `std` (既定) | 1.00 / 1.30 / 1.60 / 1.95 / 2.30 | シミュレートで採択した中庸曲線 |
| `rush`       | 1.00 / 1.40 / 1.80 / 2.20 / 2.60 | 早期決着教師（ガードを壊す） |
| `gentle`     | 1.00 / 1.15 / 1.30 / 1.45 / 1.60 | グラインド許容 |
| `spike`      | std + T12 のみ `max(std, 2.5)` | 断罪周期の後継（暫定・要スイープ） |

enemies.json が未指定なら既定: `baseAttack = max(1, threat)` /
`escalationProfile = "std"`（`EnemyData.Effective*` プロパティ経由）。

### 9.5 v1 未実装リスト（W7 後段で解消・要スイープ）

- 特殊端子 11 種（重攻撃/完全防御/出血/治癒/決死/急所/照準/蓄電池/冷却材/鋳造口/反攻）
- ~~電力経済の消費/停電~~ → 充電の消費先は **ADR-0010 のリロール**（§9.2）。
  オーバーロードは 2026-08-09 に**廃止**（GAME.md §24）。停電（充電切れのペナルティ）は未実装のまま
- ダイス改変系（鐘/正午/蝕夜/影の代償/コルヴェンの憤怒）
- ボス固有機構（サドンデス/コントラタック/妙覚）— boss6/7 は当面旧法で計測
- 収支確定前の `playerWonRoll` を参照する効果の棚卸し（AllPassiveSkillEffects 側）

### 9.6 パッシブリワーク一覧 (2026-07-15)

ADR-0009 パイプラインでのパッシブ挙動を、UseMutualAttackPipeline トグルで自動分岐する
ブリッジ方式でリワーク済み。`ctx.AddPlayerAttackOrDiceBonus(n)` helper が新旧の書き先を切替。

**追加フィールド (CombatContext, [perTurn])**:
- `mutualAttackBonus` — ExecuteTurnMutual が atkBase に加算 (ダイス合計+N の転生先)
- `mutualEnemyAttackReduction` — Escalation.EnemyAttackValue 後に差し引く (敵ダイス-N の転生先)

**リワーク内訳 (プレイヤーパッシブ・全 24 件)**:

| 群 | パッシブ | 変更内容 |
|---|---|---|
| A. ダイス合計+N → 攻撃+N (helper 経由) | Might I-IV / Frenzy(累積分) / Abyss(Berserk+5) / Eternal / Starguide / Hermes / Lightweight / Mastery / Skill / SaberWaltz(+1) / FleuretBallet(+3) / BladeDance(剣先) | 新: `mutualAttackBonus += N`。数値は保存 |
| A2. 追撃 (Pursuit) の役割分離 | PursuitI-IV | 新: 与ダメ+15%/+30%/+50%/+40% (outgoing 加算・IV は 2026-09-20 に +100% から)。筋力=加算・追撃=乗算で意味論分離 |
| A3. 反撃 (Counter) のトリガー変更 | CounterI-IV | 新: OnPreReceiveDamage で発火 (被ダメ発生時)。完全防御/シールド吸収で不発 |
| A 特殊 | EspadaPasodoble | 新: 自攻撃+5 のみ (敵ダイス+5 は「ダイス合計」概念消失で除去、+20%outgoing は維持) |
| C. 差分依存の解消 | VoidStance | 新: 「配線ダイスの過半が同値ペアなら双方0+固定3」に置換 |
|  | Judgement (天命) | 新: 「被ダメ≥現HPの50%かつHP≥30%で HP-2 上限」に置換 |
| D. 敵ダイス操作 | CurseBind (呪縛) | 新: `mutualEnemyAttackReduction += debuff` (敵攻撃値-N) |
| E. 特殊機構の再設計 | GaryoTensei (画竜点睛) | 新: 「即勝利」廃止 → 出目最大なら `mutualAttackBonus += 出目+10` + `forceCritical=true` |
|  | BloodDecree (血令) | 新: ゾロ目時 `max(finalDamage, playerDiceTotal) × 2.5` を軽減不能に置換 |
| DEAD | Truce (停戦協定) | 新: 収支0が頻発するため no-op 化 (旧法のみ機能。再設計待ち) |

**リワーク不要 (自然に動く・敵側含む)**:
- 被ダメ軽減/回復/シールド/会心/出目パターン/HP参照系 — 全 46 種
- 敵側ダイス合計+N の 22 種 — 既存の enemyDiceTotal → β=0.3 圧縮経由で自動的に攻撃値へ寄与
- 収支トリガー再定義で無改修動作の 15 種 (Counter/ShieldBash/LentTime/BladeEdge/Conqueror 他)

**W7 後段の宿題 (§9.5 に既記載)**:
- ボス固有機構 (SaintGeorges/Awakened P1-P7/EmberKing/JudgmentBlaze) の個別移植
- パッシブ〈Parry〉の scratch 廃止に伴う効果差し替え (現状は KEEP 動作だが無意味に)。
  **タイミング判定のパリィとは別物**（そちらは 2026-08-09 に全廃・GAME.md §24）
- IntimidatePlus/PlusPlus の threat 加算を「基礎攻撃値」加算に統合
- ScratchAura の正式削除
