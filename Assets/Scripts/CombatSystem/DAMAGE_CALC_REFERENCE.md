# ダメージ計算リファレンス（最重要・実コード準拠／憶測禁止）

> このファイルは実コードから直接抽出した事実のみを記載する。
> 仕様変更時は**必ず**コードと突き合わせて更新すること。推測で書かない。
> 出典: `CombatManager.cs` / `PassiveSkillManager.cs` / `CombatContext.cs` /
> `AllPassiveSkillEffects.cs`（プレイヤー） / `EnemyPassiveSkillEffects.cs`（敵）
> 最終確認: 2026-07-26（会心システム リバランス — §2-B 追加）
> 2026-10-28: 防御値と貫通（ADR-0013）── ③-A の敵の防御・§9 の予告・フィールド表・武器ラダー・敵パッシブ表を更新
> 2026-11-11: #104 の敵パッシブ 12 件を説明文どおりに直した ── §0・②・③-C・§4・§5・§9.2・§9.3・§9.6 を更新（〈多頭攻撃〉は効果を置き換えた: 攻撃よりブロックに多く挿したターン、2 回攻撃）
>
> **戦闘の経路は ADR-0009 相互攻撃パイプライン（§9）だけ**。旧ロール勝負の経路
> (`UseMutualAttackPipeline=false`) は **2026-10-29 に削除**した。
> 本 §1 にはロール勝負時代の記述（⓪ の物資リロール・ロール比較・勝敗分岐）が残っているが、
> 与ダメ／被ダメの補正チェーン（`ProcessDamage` / `ApplyWinDamageModifiers` / `ApplyLossDamageModifiers`）は
> §9 から呼ばれる現役の段。**順序の正本は §9**、各段の中身は §1 を参照する。
>
> 【最適化リファクタ済み（2026-05-29）】
> - `diceDifference` は `playerDiceTotal − enemyDiceTotal` の**読み取り専用プロパティ**（手動代入は全廃。視点スワップ時も両合計入替で符号が自動反転）。
> - ダイス合計の組み立ては `PassiveSkillManager.RecomputeDiceTotals()` に集約（通常ロール／引分リロール共通）。
> - 勝利時の与ダメ修飾チェーンは `CombatManager.ApplyWinDamageModifiers()`、敗北時は `ApplyLossDamageModifiers()` に抽出（適用順は不変・厳守）。

---

## 0. 敵パッシブの視点（最重要の前提）

敵スキルは `FireEnemyTrigger` で**視点を入れ替えて**実行される。敵パッシブのコード内では：

| コード上の表記 | 実際の意味（敵視点） |
|---|---|
| `ctx.playerDiceTotal` / `playerDice` | **敵自身**のダイス合計／出目 |
| `ctx.playerCurrentHP` / `playerMaxHP` | **敵自身**のHP |
| `ctx.playerWonRoll` / `OnRollWin` | **敵が**収支で勝った＝プレイヤーの収支マイナス（ロール勝負は無い。収支トリガーはターン解決の**後**に鳴る・§9.3） |
| `ctx.enemyCurrentHP` / `enemyMaxHP` | **実プレイヤー**のHP |
| `ctx.enemyDiceTotal` | **実プレイヤー**のダイス合計 |
| `ctx.fixedDamageToEnemy` | **実プレイヤーへの**軽減無視ダメージ |
| `ctx.finalDamage` | そのターンの与/被ダメ（文脈による） |

**スワップされない共有フィールド**（敵パッシブが書くと実値に直接効く）:
`enemyDiceTotalBonus` / `bossDiceBonus` / `enemyDefense` / `enemyArmorPen` / `enemyThreat` /
`enemyDamageTakenMultiplier` / `healBlocked` / `accumulatedValues` /
`currentBuffs` / `nextTurnBuffs` / `pendingEnemySwapId`
（`ashenSuddenDeath` / `myokakuSuddenDeath` はコードから消えている。）

**共有バフの罠（2026-11-11 #30 の監査）**: `nextTurnBuffs["enemyDiceDebuff"]` / `["diceBonus"]` / `["damageBonus"]` は
視点スワップされず、次ターンの `RecomputeDiceTotals` / `ProcessDamage` が**プレイヤー視点で**読む。敵パッシブが書くと
「相手の出目を下げる」つもりが**敵自身の攻撃**を下げ（罠師・呪縛）、「自分の出目 +」が**プレイヤーの**出目に入り（暴走）、
`damageBonus` はプレイヤーの主攻撃と敵の攻撃の両方に乗る（反撃態勢）。
**2026-11-11 #104 で、敵パッシブはこの 3 キーに書かなくなった。** 次ターンへ持ち越す値は各クラスの専用キー
（`enemy.trapper.atkDown` / `enemy.curse.atkDown` / `enemy.rampage.atkUp` / `enemy.counterStance.dmgUp`）で `nextTurnBuffs` に積み、
同じクラスが次ターンに敵の側の計算で読む（§4）。新しく敵パッシブを書くときも共有キーへは書かない。
`diceBonus` は 2026-11-12 に読む側（`RecomputeDiceTotals`）ごと削除した（最後の書き手だった加速の粉 `RunState.pendingFirstRollTotal` は書き込み元が無かった）。

**敵パッシブが ctx の HP を動かすとき（2026-11-11 #104）**: CombatManager は `playerHP` / `enemyHP` を自分のフィールドで持ち、
ctx の HP はフィールドから書き戻される。敵のトリガーを鳴らす前に ctx の HP をフィールドへ揃え、鳴らした後に
`SyncHPFromContext` で拾うのは、ターン頭（OnTurnStart）・敵の OnPostDealDamage（〈吸血〉）・収支トリガー（〈尾撃〉〈地獄の業火〉〈重甲〉・シュヴァリエ）・
OnTurnEnd の 4 か所。これ以外の位置で敵パッシブが ctx の HP を書いても残らない。

---

## 1. 計算パイプライン（適用順・実コード準拠）

### ⓪ ダイス振り直しフェーズ（#1・`CombatManager.RerollPhase`・ロール時系効果と ProcessPostRoll の前）

出目書き換え系（落雷の避雷針 等）の後、予告を仮組みしてから、**充電を払って**ダイスを**全部**振り直す
（`playerDice` を in-place 更新）。払える限り同じターンに何度でも振れる（ルール上の回数制限は無い。
「1 ターン N 回まで」は BOT の方策 `AutoRunner.RerollMaxPerTurn` 側の打ち切り）。

- コスト: 充電 `振り直す個数 × (この戦闘で払った回数 + 1)`（`ctx.rerollsThisCombat`・ターンでは戻らない）を `ctx.ConsumeCharge` で支払い。**払えなければそこで打ち切り**。
  時限デバフ〈無力感〉（2026-11-16）を持っている間は毎回 **+1**（3 個なら 4・7・10）。式は `CombatManager.RerollCostNow` → `TimedDebuffRule.RerollCost` の 1 か所
  （本体の支払い・画面の費用の表示 `TurnPause.rerollCost`・BOT の `AutoRunner.MutualRerollPolicy` が同じ値を読む）。
- 〈特別・対〉の無料分（`YachtRoleEffects.FreeRerollKey`）は費用 0 で、回目に数えない。〈無力感〉の +1 も足さない。
- 整備パネルの種火・役（2026-11-21）: 各戦闘の最初の振り直し 1 回（`MetaPanel.RoleFreeRerolls`）も同じ無料分に足す（`CombatManager.FreeRerollsLeft`）。〈特別・対〉の分と重なった時は〈特別・対〉の分（ターンの終わりで消える）から先に使い、種火の分はその戦闘の間残る（`ctx.accumulatedValues[SparkRoleFreeUsedKey]`）。
- 無料の回数は `CombatManager.FreeRerollsLeft` の 1 か所（2026-11-21・段 1-f）= currentBuffs `roleFreeReroll`（〈特別・対〉+2・〈鏡合わせの盤〉の 1 の 2 個そろい +1・〈揃いの拍子木〉+1 ── 次のターンへ足し合わせる `YachtRoleEffects.AddNextTurn`）
  + 〈擦り切れた賽袋〉（この戦闘でまだ 1 回も振り直していなければ 1・`role.rerolled`）+ 種火・役の残り。 使う順は 賽袋 → currentBuffs → 種火（`NoteRerollPaid`・currentBuffs から減らす）。
- 〈賽振り台〉（端子・2026-11-21）: 挿した次のターンの最初の振り直し（`rerollsThisTurn = 0`）の費用 −min(6, この戦闘で役が成立した回数〔このターンの役を含む・`roleCount`〕)（強化 8・下限 0・複数挿せば足す）。
  `TerminalOutcome.rerollDiscountNext` → nextTurnBuffs `roleRerollDiscount` → `RerollCostNow(ctx, 個数, 回目)` が引く（本体・画面・BOT の同じ関数・〈過負荷チューナー〉の −1 の後）。
- 振るかどうかは `RerollPolicy`（BOT / UI が差す）。人が操作するターンは `TurnPauseKind.Reroll` で止まって答えを待つ。
- 〈過負荷チューナー〉（2026-11-21〜）: 過充電（充電 10 以上・`ChargeCore.OverchargeAt`）の間、費用 −2（2 以上の物だけ・最低 1・`ChargeCore.CostNow` を `CombatContext.ChargeCostNow` 経由で `RerollCostNow` が最後に通す）。
  同じ口を充電を払う端子（`TerminalEnv.chargeCostCut`・「全部払う」端子は除く）と【再起動】・【オーバークロック】・〈逆さ避雷針〉（Resolve の 5c）も通す。
- （旧: 〈シュヴァリエのレイピア〉のコントラタック中〔`player_contre`〕は振り直さない ── 2026-11-20 に「1d1 強制」が消えたので外した）
- 旧規則（物資 30 を払う・毎ターン最大 1 回・期待値割れだけ振り直す自動ポリシー）は廃止。`ProvisionSystem.RerollCost` の定数は参照されていない。

### ① ダイス合計フェーズ（`PassiveSkillManager.ProcessPostRoll` L434-463）

```
playerDiceTotal = Σ(プレイヤー出目)
enemyDiceTotal  = Σ(敵出目)
                − buff "enemyDiceDebuff"
                − enemyDiceTotalPenalty    ← 床なし減算(負値許容)。沈黙の剣帯=1T目-99
                + enemyDiceTotalBonus      ← 星火燎原/業火の遺志/狂暴化 等(累積)
                + bossDiceBonus            ← 強者/玉座/刹那(※現在enemies.jsonから撤廃・常時0)

→ FireTrigger(OnPostRoll)  ← ここで OnPostRoll パッシブが両合計を加算改変
   (軽量/熟練/技量/筋力/星導/永劫/ヘルメス/黄金卿/復讐/Abyss など、
    敵側: 号令/精鋭各種/号令/業の連鎖/永劫の燃焼 など)

diceDifference = playerDiceTotal − enemyDiceTotal
勝敗 = sign(diceDifference + consDiceRoll)   ← consDiceRoll(消費ダイス補正)は勝敗のみ・ダメ非加算
引き分けは勝敗が付くまでダイス再ロール（OnPostRollは再発火しない）
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
[敵の攻撃・1 体目だけ (fireEnemyDealTriggers)・final > 0] FireEnemyTrigger(OnPreDealDamage)
                                         ← 敵の与ダメ改変（剛力/闇技/研ぎ澄まし/反撃態勢・2026-11-11 #104）。
                                           敵視点 playerWonRoll = true。 final は視点スワップされないので敵の攻撃に乗る。
                                           2 体目・〈連撃〉の 2 回目では鳴らさない
[勝利] FireTrigger(OnPreDealDamage)      ← 与ダメ改変パッシブ
[敗北] FireTrigger(OnPreReceiveDamage)   ← 被ダメ軽減パッシブ（堅忍/鉄壁/頑強/天命 等）
FireTrigger(OnPrePursuitDamage)
[プレイヤーの攻撃だけ] FireEnemyTrigger(OnPrePursuitDamage)
                                         ← 追撃無効化(飛翔/不動) が nullifyPursuitDamage を立てる (2026-11-11 #104)。
                                           CombatManager がこれを見て役〈追撃〉の追加ダメージを入れない (§9.2)
nullifyPursuitDamage → pursuit=0（pursuitDamage はいま書き手が無く常に 0）
total = final + pursuitDamage
[会心] FireTrigger(OnCriticalCheck); critRatePctAdd += buff "critRatePct"
       ── 2026-09-19: 会心率を % に統一し 5% 刻みに揃えた（旧「分子 1 = 5.5%」は撤去・§2-B）──
       critAdd  = critBaseRate(武器+層補正) + ctx.critRatePctAdd + メタ
       critRate = ResolveCritRate(critAdd)   ← 加算した分がそのまま (0〜1 クランプのみ・§2-B)
       会心成立 = !critSuppressed && !critSkipRoll && GameRng.Value("PassiveSkillManager.2") < critRate
         critSkipRoll【2026-11-21】= 〈必殺の照準器〉がもう 1 回発火した撃。 乱数を引かない (キーの並びを崩さない)・forceCritThisHit で確定
       critSuppressed(無心の刃) → 常に false / forceCritical(末那識・天極・短絡・画竜) → 常に true（最終上書き）
         forceCritThisHit【2026-11-20】→ この 1 撃だけ確定 (〈抜き打ちの作法〉・撃ごとに CombatManager が戻す)
       〈銑鉄棍〉【規則】(critToDamage・2026-11-21): 会心しなかった撃だけ outgoingDamageMultiplier += min(1, critAdd) (与ダメ% の池・撃ごとに戻る)。 会心は普通に出る
       成立かつ total>0 → Crit.Note (このターンの会心の数) → FireTrigger(OnCriticalDamage)
                         → total ×= CritCore.EffectiveMul (§3-N: 敵の会心耐性 (下限 1.0) → + 超過会心率 → 〈天眼の鍔〉の確定会心 ×1.25)
       不成立かつ total>0 → 鈍器 ×(1 + nonCritOutgoingMultiplier) → 〈一点読みの片眼〉の代価 ×0.5 (2026-11-22・旧 ×0.6)
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
outgoingDamageMultiplier += メタ% + 遺物% + 研磨剤1.5 + consDmgMultPct/100   （全部同じプールへ加算）
                                     ※メタ% = 整備パネル (2026-11-20〜): 基礎整備 5 段目 +10% / 〈戦意〉勝利ごと +1.5% / 〈剛胆〉エリートに +10% + 挑戦カード
× (outgoingDamageMultiplier + hitOutgoingPct/100)   ← パッシブ(OnPreDealDamage で加算済み)/メタ/遺物/研磨剤/鬼火の油 を 1 回だけ掛ける
                                     hitOutgoingPct = この 1 撃だけ池に足す % (〈加重の拳套〉: 2 撃目以降 +20・撃ごとに 0 に戻す・池そのものは書き換えない・2026-11-21 §3-Q)
                                     ※鬼火の油は 2026-09-19 まで倍率適用後に ×(1+X%) を別掛けしていた
+ consAtkBurst                     ← 消費:攻撃バースト(単発)
+ メタ会心ボーナス(会心時)
− メタデバフ向かい風(最低1)
× (100 + finalDamagePct + hitFinalDamagePct)/100      ← 最終与ダメージ倍率【2026-11-20・GAME.md §23-32】(Kw.ApplyFinalDamage・切り捨て・既定 0 = 素通し)。 〈防人の軍標〉が【ジャスト防御】のたび +35 (この戦闘・累積・2026-11-21・旧: シールドを得るたび +5)。
                                     hitFinalDamagePct = 主攻撃のこの 1 撃だけの上乗せ (〈血桜〉〈血の楔〉・撃ごとに 0 に戻す・主攻撃以外の口には無い)。 【オーバークロック】(TerminalOutcome.overclock) は撃 0 に +60・その撃だけ playerArmorPen += IgnoreAll (撃の後に戻す)。 〈雷神槍〉〈四つ打ちの鉄芯〉は OnPreDealDamage で本物の撃に足す。 〈破城斧〉は撃ごとに +80 と `hitIgnoreDefense` (その撃だけ敵の防御を飛ばす・撃ごとに false へ戻す・2026-11-21)。
                                     怠惰の直前 = すべての倍率 (会心・与ダメ% の池・端子の別枠・物差し) の後・敵の軽減パッシブと防御の前。 与ダメ% の池には足さない。
                                     掛ける口は怠惰と同じ 4 つ (主攻撃 = ApplyWinDamageModifiers / HpLossDiag.Lose (channel ≠ "通常攻撃") / 〈逆綴じの止血帯〉/ Kw.DamageEnemy の直接)。 BOT の見積もりも同じ位置 (AutoRunner.ApplyFinalPct)
× (100 − 30)/100                  ← 大罪:怠惰 (挑戦カード IV) を受けている時だけ・与えるダメージ −30%【2026-11-11】(主攻撃はすべての倍率の後・敵の防御の前・切り捨て。 毒・出血・爆発・固定ダメ・反射・品の直接は敵の HP を減らす口 HpLossDiag.Lose で同じく −30%)
                                     〈徒労〉の戦闘のデバフ【2026-11-14・GameHooks.ToilDamageDownPct・既定 50 (BattleBoonRule.DefaultToilDamageDownPct・作者の決定)・測定の口 toilDamageDownPct で 0 にすれば切れる】も同じ枠: 〈徒労〉を持っている間 −N% (既定 −50%)。
                                     実体は MetaDebuffApplicator.ApplyOutgoingCut の 1 つで、 怠惰と重なったら厳しい方 1 つ (× (100 − max(30, N))/100・足さない・掛けない)。
                                     この枠を通らない経路: 盾〈黎明〉の削り (WeaponFamilyEffects.Daybreak が ctx.enemyCurrentHP を直接減らす・ボスのダウンも乗らない)・
                                     処刑 (〈百人切りの鉈〉〈賞金首狩り〉・短剣〈処刑〉= HP を 0 にする)。
                                     武器の撃ごとの +% (§3-B2: 一対一・捨て身・処刑・鎧砕き・不滅の刃・画竜点睛) は hitFinalDamagePct へ足す (足し合わせて 1 回掛ける)。 2 体目は 1 体目の減った量を写すので削られた後の値が入る
× 1.5 (切り捨て)                    ← ボスのダウン【2026-11-13】: ボスが満額のダウンをしているターンだけ (BossDown.ApplyTakenNow・定数 BossDown.TakenMul)。
                                     ひびで割れた弱いダウン (2026-11-14) では掛けない (BossDown.TakenMulNow = false)。
                                     怠惰の直後 = すべての倍率の後・敵の軽減パッシブと敵の防御の前。 与ダメ% の池には足さない (別枠の掛け算)。
                                     主攻撃以外も全部掛かる (下の「ボスのダウン」)
─ FireEnemyTrigger(OnPreReceiveDamage): 灰塵の鎧/Ethereal/各敵被ダメ軽減
                                     〈血の楔〉【2026-11-20】: 持っていれば、 この軽減で減った量のうち 出血 ÷ (出血 + 30) を戻す (2026-11-21: 旧 + 40) (切り捨て・CombatManager.Damage.cs)。
                                     同じ品は敵が得るシールド (vescaShield の増加) と回復 (ReduceEnemyHeal・フロアデバフ・挑戦カード〈再生〉) を 30 ÷ (出血 + 30) 倍にする (CombatContext.WedgeEnemyGain)
  → total = max(0, ctx.finalDamage)
× 敵の防御値【2026-10-28・ADR-0013】: effDef = max(0, enemyDefense − (playerArmorPen + hitArmorPen (+ critArmorPen 会心時)))
                       hitArmorPen = この 1 撃だけの貫通 (剣〈切り返し〉の最初の 1 撃 +10・撃ごとに 0 に戻す・2026-11-21)
                       total = DefenseFormula.Apply(total, effDef)   ← ceil(total × (1 − R(effDef)))・整数演算
                       ※ 短剣〈急所〉持ちの会心ならこの段を丸ごと飛ばす (VitalStrike.IgnoresDefense)。 〈急所〉の 2 撃目以降と〈連刃〉の追撃は hitIgnoreDefense で飛ばす
                       ※ 戦術消耗品〈急所突きの針〉【2026-11-20】: 攻撃のある最初の撃だけ playerArmorPen += IgnoreAll (撃の後に戻す)・その撃は forceCritThisHit
                       ※ 戦術消耗品〈鎧割りの酸瓶〉【2026-11-20】: 使った時に enemyDefense −12 (下限 0)。 形態の交代で防御を入れ直した後も、 使った数 × 12 を引き直す
                       R(D) = Rmax·D/(D+K)  (Rmax = 3/5, K = 10・仮置き)
  ※ 旧: effRate = max(0, enemyDamageReductionPct − armorPenPct) の割合軽減と、
    余剰貫通 (armorPen − 防御率) を与ダメ ×(1+余剰) へ転用する段 J。 どちらも撤去
↑ 勝利時最低保証【新】: total = max(total, winMinDamage[既定1, 利刃で1〜4])
× enemyDamageTakenMultiplier       ← 狂暴化(50T後 ×2・2026-11-21〜 全部の戦闘)
メタデバフ俊敏: 各戦闘の初撃を total=0,fixedDmg=0
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
− メタバフ被ダメ軽減(最大−2,最低1)
− playerFlatDamageReduction(不屈の鎧/苦難の刻印/整備パネル〈不動〉−2 合算, 最低1)
不抜の聖紋【職業/騎士 2026-06-28】: oathArmed && !oathBroken && HP≥80% → total −= ceil(total×0.4)。
  HP80%未満 (適用前 or この被ダメで割込予定) を検知した時点で oathBroken=true＝以後永久無効
− floorMod.defeatDamageReduction(5層 地獄門 −2)
+ ceil(total × receivedDamageBonus)(亡者の招待 +30%)
÷2 halveFirstEnemyAttack(共助・T1のみ)
playerDamageNegateCharges>0 → total=0(獣の絆)
− consShield(消費シールド吸収。 盾は T2 以降のターン頭に半減 ── ③-D。 〈重装兵の隠し刃〉はボスのダウン中のターンだけ半減しない・2026-11-20)
  吸収した量は ctx.shieldAbsorbedAttackThisTurn ([perTurn]・2026-11-14) にも積む ── 灰燼の王の〈烈炎〉がターン終了時に読む (敵パッシブ表の BlazeBrand)。
  ここ (主攻撃: 1 回目・〈連撃〉/2 体戦の 2 回目) の吸収だけで、 ③-C の軽減無視の肩代わりは積まない
コントラタック(player_contre・〈シュヴァリエのレイピア〉・2026-11-20 作り直し): 2 × (ブロックで止めた量 min(敵の攻撃, ブロック) + この攻撃のシールドの吸収) を
  enemyHP へ軽減不可で返す (HpLossDiag.Lose "反射(コントラタック)")。 発動中は攻撃の端子に挿せない (TerminalEnv.noAttack・Resolve の 0.)。 旧: total 半減 + 軽減量 ×2 を返す
痛覚遮断剤【職業/狂戦士 2026-06-28】: painkillerArmedThisTurn && total≥playerHP → total=playerHP−1（HP=1踏みとどまり・チェーン**最後**）
→ (ダメージ適用直前) 仕込み刃【職業/暗殺者 2026-06-28・2026-09-22 に倍率 1→5】: daggerArmed && total>0 → total=0 ＋ **その 5 倍**を enemyHP へ軽減不能で返す（`CombatManager` の仕込み刃の反撃倍率）。使ったターン限りで、被弾しなくても失効
→ 古い手紙【消耗品 2026-10-31】: accumulatedValues[Consumables.OldLetterGuardKey] > 0 → total=0（反撃なし）。仕込み刃の直後・同じく使ったターン限り。1 枚で 3 回使える
→ (整備パネル〈不動〉の一撃の上限 = 最大 HP の 35% の後) 〈無害灯〉【規則】(2026-11-21・§3-M): 臨界 ≥ 15 なら 1 戦闘 2 回 (1 ターン 1 回) total = 0・臨界 −15・爆発の 30% を敵へ (HarmlessLamp.Apply・2026-11-21〜22 Phase 4 較正: 旧 1 ターン 1 回・−10・50%)
→ playerHP −= total (+ fixedDamageToPlayer)
  fixedDamageToEnemy>0 は敗北時も敵へ(反撃/Counter/Riposte) / 出血
  痛覚遮断剤 post-clamp: メイン+固定ダメ適用後も painkillerArmedThisTurn 中は playerHP≤0 → 1 に復帰
fixedDamageToPlayer ≥ 999 → playerHP = 0（死の宣告）
```

#### 職業スターター消耗品のダメージ関連効果まとめ（2026-06-28 追加）

| 品 | 職業 | 効果と適用点 |
|---|---|---|
| 瞬間研磨剤 | 剣士 | 次の 1 撃だけ `outgoingDamageMultiplier += 1.5`（与ダメージ+150%）。`ApplyWinDamageModifiers` 内・適用後 disarm。**2026-07-27 リワーク**（旧 playerDiceTotal 加算は相互攻撃モデルで無効だった） |
| 不抜の聖紋 | 騎士 | 上記 ③-B チェーン参照。HP80% 維持中のみ被ダメ −40%、一度割れたら戦闘中永久無効 |
| 痛覚遮断剤 | 狂戦士 | 使用ターン致命ダメを HP=1 に clamp（2 経路）。次ターン開始時 (`CombatContext.BeginNewTurn` Phase A) HP=1 なら consDiceRoll+10 / consAtkBurst+10、そのターン終了時に死亡確定 (`ExecuteTurn` 終端) |
| 仕込み刃 | 暗殺者 | 使ったターンの被ダメを 0 化＋**その 5 倍**を enemyHP へ直接減算（軽減不能・2026-09-22 に 1→5 倍）。このターン限りで、被弾しなくても失効 |

#### 戦術消耗品・消耗品の連動品のダメージ関連効果（2026-11-20・item-overhaul-spec §7）

正本は `GameLoop.Consumables`（ApplyTactic・定数）と `CombatManager.Tactics.cs`。 どれも戦闘中・判断の間（振り直し・配線）だけ使える。

| 品 | 効果と適用点 |
|---|---|
| 急所突きの針 | `accumulatedValues["tactic.needle"]` を立てる。 ADR-0016 の撃のループで、 攻撃のある最初の撃に `forceCritThisHit = true`・`playerArmorPen += DefenseFormula.IgnoreAll`（その撃の ApplyWinDamageModifiers の後に戻す）。 攻撃しないターンは持ち越す |
| 鎧割りの酸瓶 | `enemyDefense = max(0, enemyDefense − 12)`（`Consumables.AcidDefenseCut`）。 ヴェスカの形態の交代では `ReapplyTacticAcid` が使った数 × 12 を引き直す |
| 目盛り削りの鑿 | 敵に 15（`ChiselDamage`）を `HpLossDiag.Lose(…, "目盛り削りの鑿")` で入れる（主攻撃以外と同じに 最終与ダメージ倍率・怠惰・ダウンの ×1.5 が掛かる・敵の防御は掛からない）。 その後 `NoteBossDownMainHit` を通す ＝ 目盛りは通常攻撃として割れる（ひびの入った目盛りも満額のダウン） |
| 倍化の共鳴玉 | `TerminalBattleState.doubleAll = true`（このターン・BeginTurn で戻す）。 `TerminalCards.Resolve` の 5. で〈残響〉と同じ倍化 |
| 無銘の潤滑油 | `TerminalBattleState.ignoreCond = true`（このターン・〈無形の構え〉と同じ旗） |
| 使い切りの矜持（秘宝） | OnConsumableUse: そのターン 1 回 `AddOutgoingPct(0.25)`（与ダメ % の池・最終与ダメージ倍率ではない） |

#### 金・軸なし汎用の与ダメに入る分は §3-Q

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

通す対象は 12 件 ─ 審判の炎 / 王の業炎 / 烈炎 / 大出血 / 毒の侵蝕 / 鏡映の応答 /
13番目の宣告 / 巌の意志の撃破時反撃 / コントラタック /
尾撃 / 地獄の業火 / 重甲の反射（この 3 件は 2026-11-11 #104 に fixedDamageToEnemy からここへ移した・収支トリガーで入る）。

**この変更が持つ副作用**（同一シード 1000 ラン・対照群比較で実測）:
盾は `ShieldBash` が **残存量の 20〜80% を攻撃力に加算**する常時効果の弾でもある。
チップが盾を毎ターン削るため、5層ボス戦で盾の使い残しが 15.19 → 1.21、
ShieldBash の空振り（盾 0）が 34.2% → 66.1%、1 機会あたりの攻撃ボーナスが 10.18 → 4.59 に落ちる。
結果として同戦闘は 14.7 → 15.6 ターンに伸び、勝率 66.6% → 60.8%。
7層クリア率は 72 → 71 で変化なし (p=1.000)。
**盾の「使い残し」は余剰ではなく火力**という構造を踏まえずに盾の消費先を増やすと、
必ず火力側に跳ね返る。〈盾解放〉が謳う「防御か火力かの二相性」はこの変更で初めて成立した。

### ③-D プレイヤーのシールドは毎ターン半減【2026-10-16 確定】

`consShield` は **T2 以降のターン頭に半分（切り捨て）** になる。1 → 0 で確実に消える。
実体は `CombatContext.BeginNewTurn` の Phase C（`if (currentTurn > 1 && consShield > 0) consShield /= 2;`）。

- **順番は「半減 → 付与」**。`PassiveSkillManager.BeginTurn` は `BeginNewTurn` のあとに `OnTurnStart` を撃つので、
  そのターンに入る盾（ターン頭の付与）は半減されない。ヴェスカのシールド規約（GAME.md §13-4）と同じ。
- **減衰しない例外**: 〈重装兵の隠し刃〉はボスのダウンのターン、 〈脈なしの鋼心臓〉はターン頭のシールドが 50 以上 (2026-11-22・旧 35)。 残る割合は〈油染みの盾革〉で 70% (前のターンにジャスト防御か完封した時だけ・§3-I)。
- **T1 は半減しない**。戦闘開始時の付与（開幕シールド・持ち越し `carriedShield`・消費アイテムの持ち込み）は、最初のターンにそのまま使える。
- 期限付きの盾（`consShieldExpireTurn`）の失効は従来どおり（ターン終了時に残ターンを減らし、0 で `consShield = 0`）。
- 整備パネル〈剛胆〉（主方針の枠・2026-11-20〜。旧 Guard r10）の持ち越しは従来どおり「**勝った**戦闘の終了時の残量 × 30%（切り上げ）」（`CombatManager` の戦闘終了処理・2026-09-13 に 50% → 30%）。
- 波及: `ShieldBash` の弾（残存量の 20〜80%）が毎ターン目減りする / `HolyShield` の「ターン終了時に 3 未満なら +4」が発動しやすくなる /
  `Lifeline` の大きな盾は翌ターンから半減する。**バランスは未測定**（2026-10-16 時点。測定は回さない判断）。
- 2026-10-28: `ShieldBash` I〜IV・`HolyShield`（衛士の慣い）・盾解放は削除／リワーク済み。半減は盾家系の
  〈城壁〉→〈シールドバッシュ〉（溜めて翌ターン以降に放つ）にそのまま効く ── 溜めた分は放つ前に 1 回半分になる。

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
| `enemyBleedStacks` | 出血。毎ターン比例減衰 (1 + 出血/10・2026-11-07〜 §3-I)、毎T末に敵へ | 戦闘保持 |
| `enemyBurnTurns`/`enemyBurnDamage` | 炎上（業火） | 戦闘保持 |
| `enemyDiceTotalBonus` | 敵合計への加算（判定前・累積） | **BeginNewTurnでリセットしない** |
| `enemyDiceTotalPenalty`【新】 | 敵合計への床なし減算（負値許容＝大差勝ち）。沈黙の剣帯=1T目99 | BeginNewTurnで0 |
| `bossDiceBonus` | 強者/玉座/刹那(現在未使用) | swapで0 |
| `enemyDefense`【2026-10-28】 | 敵の防御値。EnemyData.defense + 精鋭+2 / 硬鱗+4 / 重甲+4 (OnBattleStart)。 戦闘中に動くもの: 割れる装甲 −4 / 積み上がる装甲 +3 (OnTurnEnd)・構え (ターンの頭・`ApplyGuardCycle`)・鎧砕き | **戦闘保持**(start/swapで設定) |
| `enemyArmorPen`【2026-10-28】 | 敵がプレイヤーの防御を固定で削る量。EnemyData.armorPen | **戦闘保持**(start/swapで設定) |
| `playerDefenseBase` / `playerArmorPenBase`【2026-10-28】 | プレイヤーの防御・貫通の土台 = 初期防御 **5**（2026-11-20 に 2 から）+ 武器の `defense` / `armorPen`（[無印,+]）+ 整備パネル 基礎整備 2 段目 +2 + 遺物 | 戦闘保持(start で設定) |
| `playerDefense`【2026-10-28】 | プレイヤーの防御値。ステータス `def`・一対一 が OnPostRoll で加算。（旧〈血令〉の 0 へ換算は 2026-11-11 に外した） | BeginNewTurnで土台へ |
| `enemyArmorPenNegate`【2026-10-28】 | 敵の固定削りを打ち消す量。剣〈間合〉（ステータス `penNeg`）が OnPostRoll で加算 | BeginNewTurnで0 |
| `playerArmorPen`【2026-10-28】 | プレイヤーの貫通。遺物 +N (BeginNewTurn)・ステータス `pen` (OnPostRoll) (役〈攻め〉T4 の +999 は 2026-11-18 に削除) | BeginNewTurnで土台+遺物へ |
| `winMinDamage`【新】 | 勝利時与ダメ最低保証(既定1,利刃1〜4) | BeginNewTurnで1 |
| `enemyHealReductionPct`【新】 | 敵回復の減衰率(治癒阻害0.5/遮断1.0)。`ReduceEnemyHeal()`経由で敵自己回復に適用 | 戦闘保持 |
| `fixedDamageMultiplier`【新】 | 軽減無視ダメ倍率(蒼白の槍騎士1.5)。ProcessDamage末＆引分分岐で fixedDamageToEnemy に乗算 | 戦闘保持(既定1.0) |
| `outgoingDamageMultiplier` | プレイヤー与ダメ倍率(激情の刃) | 毎T1.0 |
| `enemyDamageTakenMultiplier` | 敵被ダメ倍率(狂暴化×2) | 毎T1.0 |
| `healBlocked`/`lifestealPct` | 回復封印/与ダメ回復% | 毎T |
| `playerFlatDamageReduction` | 被ダメ定額減(不屈の鎧/苦難の刻印) | BeginNewTurn |
| `receivedDamageBonus` | 被ダメ増(亡者の招待+30%) | — |
| `halveFirstEnemyAttack`/`playerDamageNegateCharges` | 共助/獣の絆 | フラグ消費 (〈獣の恩義〉`nullifyFirstEnemyRoll` は 2026-11-22 に削除・GAME.md §24) |
| `truceThisTurn` | 停戦協定ターン(他効果抑止) | 毎T |
| `consShield`/`consDmgMultPct`/`consAtkBurst`/`consDiceRoll` | 消費アイテム由来 (`consAtkBurst`/`consDiceRoll` を書くのは〈痛覚遮断剤〉だけ) | 各種 |
| buff `damageBonus`/`critRatePct`(小数)/`enemyDiceDebuff` | currentBuffs/nextTurnBuffs経由 | nextTurn→current移行 |
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

**加算が 100% に届けば常時会心が成立する。** 100% を超えた分は捨てずに、 会心した撃の会心倍率へ 1% ごとに +0.01 で変わる (2026-11-21・`CritCore.ExcessMul`・§3-N)。 旧カーブ（knee 20% / falloff 1.25・100% へ漸近）は
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
2. `ctx.critRatePctAdd` — パッシブ（心眼 +5/10/15/20% など）・消耗品・戦闘中バフ（月蝕 +15%）。 〈シュヴァリエのレイピア〉の解除は 2026-11-20 から会心率 +50% ではなく次のターンの会心確定（`Consumables.ApplyRapierRelease`）
3. メタ（`GetCritRatePctBonus` − 〈俊敏〉ペナルティ、合計は 0 下限）

> メタ精密トラックは 2026-09-10 に撤去済み（`PrecisionCritRatePct` / `PrecisionOverflowMultiplier`
> はどちらも常に 0）。**「逓減の捨て分を会心倍率へ還元する」極点はもう存在しない**ので、
> 逓減の撤去で失われた機構も無い。
> アイテム側の副次ステータス `subCritRatePct` も 2026-09-15 に全廃（GAME.md §24）。

> **単位は小数 (`0.05f` = +5%)。** 2026-11-07 に、 整数の % (`8f` = +800%) で足していた 13 品
> (軸の品 9・10/31 再編の 5: 血日に澄む佩玉・測量師の片眼鏡・烈刃・遅効の毒刃・瑠璃砥石) と、
> 〈寸鉄〉〈重ね斬りの型〉の超過判定 (`lastCritAddPct > 100f`) を直した。 1 品で常時会心になり、 全体の会心率が 72% だった。
> **10/31〜11/07 の測定値は会心に関して無効。**

**天井**: 100%（`ResolveCritRate` のクランプ）だけ。`forceCritical` は率に関係なく確定する。

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

**可変量 (`stats[].per`・段 3・2026-11-11)。** 量が実行時の状態で変わる効果は `per`（単位数の出どころ: `coins` (今の所持ゴールド) / `swordShopSpent` (〈溶金の領主剣〉を持って店で使った額・ShopManager.Close) / `axis:軸名` / `enemyBleed` / `turnsElapsed`）・`perEvery`（N につき・整数の割り算）・`perMax`（単位数の頭打ち）・`on`（乗せるトリガーの上書き）で書き、`StatModifierEffect.ApplyScaled` が「生成時に確定した float の量 × 単位数」を旧クラスと同じ形で足す。単位数が 0 なら何もしない。
段 3 でステータス化済み（9 件）: 溶金の領主剣 / 研ぎ師の見立て書 / 千里眼の水晶 / 血日に澄む佩玉 / 鎧縫いの針 / 麻痺の吹き矢 / 雷神槍 / 猛りII・III。 同一シードのダイジェストは未測定。
2026-11-20: 〈千里眼の水晶〉〈英雄の意志〉は作り直してクラスへ戻した（`AxisItemEffects2.cs`）。 2026-11-21: 充電の軸の 26 品は `ChargeEffects.cs` のクラス (〈雷神槍〉の stats と `per: chargeSpent` は削除)。 同日: 毒の軸の 25 品は `PoisonEffects.cs` のクラスと `PoisonCore` の旗 (〈麻痺の吹き矢〉の stats と `per: enemyPoison` は削除)。 同日: シールドの軸の 22 品は `ShieldEffects.cs` のクラスと `ShieldCore` の旗 (§3-K)。 同日: 出血の軸の 26 品は `BleedEffects.cs` のクラスと `BleedCore` の旗 (§3-L・〈血日に澄む佩玉〉の stats は削除・〈鎧縫いの針〉は stats の pen を残しクラスを内側に)。

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
| `overcharged` | `IsOvercharged()` |
| `allEven` / `kaleido` | 出目が全て偶数（1 個以上）/ 全同値・全て異なる・連続 3 個以上 のいずれか（2 個以上） |
| `weaponPlusMin: N` | `run.weaponPlus ≥ N` |
| `strongFoe` | `MapManager.CurrentNode.type` が EliteBattle か Boss |
| `provisionTierMin: 名` / `provisionTierBelow: 名` | 物資の段階（ProvisionTier・数字が大きいほど物資が少ない）が 名 以上 / 名 未満。旧名 `hopeTierMin` / `hopeTierBelow`・旧段階名 Calm〜Madness も読む |
| `nonCritStreakMin: N` | 連続非会心数 ≥ N。OnPostDealDamage で 会心→0 / 非会心→+1（`accumulatedValues["nonCritStreak:<ID>"]`） |

**固有部分を持つスキル**（〈処刑〉のダイス潰し）は専用クラスに固有部分だけを残し、`StatModifierEffect` が
それを包んで同じ ID で登録する（同じトリガーでは クラス → ステータス の順）。

ステータス化済み（段 2・条件付き 19 件）: SwiftHandII・III / DuelistI・II / Riposte / Destiny / BloodDecree /
Execute（与ダメ%部分）/ IndomitableIII / Overload / SingleMinded / Stubbornness / VenomBlade / BloodScent /
Hermes / Bloodlust / EvenEyes / MasterworkNotes / KaleidoDice。

**アイテム側から移したもの（2026-09-19）**: 末那識 / 狂宴の仮面 / 英雄の意志（2026-11-20 に作り直してクラスへ・大技の予告のターンの頭にシールド +18） / 激情の刃。旧 ITimedEffect（OnRoll・CombatStart）
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
| ~~ShieldBash I/II/III/IV~~ | — | — | **2026-10-28 削除**（I〜III はどの品にも付いていなかった・IV＝〈内から落ちた城盾〉は品ごと削除）。シールドを攻撃に変える仕組みは盾家系〈シールドバッシュ〉（§3-B2）へ一本化 |
| LentTime I/II/III/IV(貸与された時間) | B/S/G/L | OnPreReceiveDamage/OnRollWin | 敗北時 被ダメの15/30/45/60%を肩代わり(finalDamage減)し lentTimeStacks 蓄積。max(maxHP×同%)到達で同値を fixedDamageToPlayer で一括清算＋0。勝利で0クリア |
| Lifesteal I/II/III/IV(吸血) | B/S/G/L | OnTurnStart | lifestealPct += 0.02/0.04/0.06/0.08（勝利時 totalDmg×pct を HealPlayer 回復・負傷/封印尊重） |
| BladeEdge I/II/III/IV(利刃) | B/S/G/L | OnPreDealDamage | 敵シールド(sg_shield)を −5/−10/−15/−20、IV は会心倍率 +0.2（2026-07-15 リワーク・実コード準拠）。貫通は持たない。items.json には現在なし |
| BountyHunter I/II/III/IV(賞金首狩り) | B/S/G/L | OnTurnEnd | 敵HP≤8/14/20/28% で enemyCurrentHP=0(処刑)＋最大HP×(lv×3%)回復＋coins +0/0/1/2 |
| Conqueror I/II/III/IV(重畳) | B/S/G/L | OnRollWin/OnPreDealDamage | 勝利毎に outgoing += 2/4/5/10% (上限 20/40/60/100%) ※2026-05-31 v5 %ベース |
| Grievous I/II(治癒阻害/治癒遮断) | S/G | OnBattleStart | enemyHealReductionPct = 0.5 / 1.0（敵回復を ReduceEnemyHeal で減衰） |
| Lightweight/Mastery/Skill(軽量/熟練/技量・武器Tier由来) | — | OnPostRoll | playerDiceTotal += 3/2/1 |

### 3-B. 固有パッシブ（ユニーク・名前付き／ダイス固有効果含む）

| SkillId(表示) | Trigger | 効果(ctx操作) |
|---|---|---|
| ~~パリィ / 衛士の慣い / 復讐 / 果たし合い / 蝕夜~~ | — | **2026-10-28 削除**（武器家系の整理・GAME.md §24）。後継は §3-B2 の〈堅守の構え〉〈黎明〉〈不滅の刃〉。〈切り返し〉〈血令〉〈処刑〉は同名のまま §3-B2 で中身が変わった |
| 重装兵の隠し刃 | OnBattleStart / OnJustGuard | **2026-11-21**: 【ジャスト防御】のたび予告 E の軽減不可ダメージ (§3-K)。 **2026-11-20 作り直し**: `shieldKeepOnBossDown` を立てる。 そのターンがボスのダウンなら BeginNewTurn のシールドの半減を飛ばす（ダウンの待ちが 1 以上 = このターンはダウン）。 旧（2026-11-04〜）: 敵の HP 30% 以下のターン、シールドを最大 20 使い使った量 × 2 を攻撃に（1 戦闘 1 回） |
| Sting(出血) | OnPostDealDamage | enemyBleedStacks++ |
| 脆弱(契約・狩猟旅団) | OnBattleStart (Apply) + 防御後最終ダメ | armed状態で会心ダメ時 ×(1+0.15/0.30/0.45) 倍率→consumed / 非会心ロール勝利で再armed。 戦闘終了で剥がれる。 仕様: docs/GAME.md §12 |
| ApexCrit(天極) | OnCriticalCheck | ゾロ目(全同値)なら **forceCritical=true**(会心確定)＋criticalMultiplier+1.0 |
| Lifeline(命脈) | OnBattleStart + OnPostReceiveDamage | 戦闘開始時 maxHP×10% 回復／1戦闘1回 HP≤50% で consShield += ceil(maxHP×0.5) |
| Resonance(共鳴・LEG) | OnPreDealDamage | 発動中パッシブ数 × 0.01 を outgoing 加算 (2026-09-20: 旧 over5 × 0.05) |
| Greed(貪欲・ダイス) | OnTurnStart | lifestealPct = 0.1 |
| Eternal(永劫・ダイス) | OnPostRoll/OnBattleEnd | playerDiceTotal += min(5, eternalStacks/10)、勝利でstack++保存(ラン跨ぎ) |
| TenkouKaibutsu(天工開物) | OnBattleStart / OnWired | 2026-11-21 (§3-M): `burstFlatBonus += 12 × RunState.heavenlyCraftUpgrades`・【過熱】のターン `playerArmorPen += 12` |
| Bloodlust(背水の狂刃) | OnPreDealDamage | HP≤50% → outgoing+=0.3、≤25% → +=0.8 (2026-05-31 outgoing移行) |
| Hermes(ヘルメスの靴) | OnPostRoll | 初回ロール `AddPlayerAttackOrDiceBonus(5)`（攻撃+5・ステータス化 §2-C） |
| 溶金の領主剣 | stats | §3-Q |

### 3-B2. 武器 13 品（`WeaponFamilyEffects.cs`・2026-11-21 フルリワーク・full-item-rework-ledger §⑫）

**武器の passiveSkills（items.json）は進行武器では実行時に読まれない。** 進行武器（items.json の `family` / `tier` を持つ 12 品）は
`RunPassiveSync` が `WeaponProgression.Compute(weaponId, plus)` で算出したものを動的付与する（items.json 側は表示用・同期させること）。
竜閃（family なし）は items.json の skills をそのまま付ける。

段: T2 = A-I / T2+ = A-II / T3 = A-II + B-I / T3+ = A-II + B-II + 固有1 / T4 = A-III + B-II + 固有1 + 固有2 / T4+ = 同じ（**B は II まで**・+ は武器の防御/貫通だけ上がる）。

| 家系 | A (T2〜) | B (T3〜) | 固有1 (T3+〜) | 固有2 (T4〜) |
|---|---|---|---|---|
| 斧 | 猛り | 鎧砕き | 捨て身 | 血令 |
| 剣 | 間合 | 一対一 | 切り返し | 不滅の刃 |
| 短剣 | 疾手 | 連刃 | 急所 | 処刑 |
| 盾 | 城壁 | シールドバッシュ | 堅守の構え | 黎明 |

口: 「全攻撃ヒットの攻撃 +N」= `AddPlayerAttackOrDiceBonus`（OnPostRoll / OnWired・端子の外の攻撃なので全部の撃に乗る）。
「最終与ダメージ +%」= 撃ごとの `hitFinalDamagePct`（OnPreDealDamage・`IsRealHit`）。 「目盛りへの削り +N%」= 当たった撃のダメージ × N% だけ `RaiseBossNextNotch`（〈鎧縫いの針〉と同じ口）。
式の数字は `CombatRules`（整数の % で切り上げ/切り捨て）。 BOT は `AutoTest.WeaponRungModel` / `TurnRules` で同じ関数を読む。

| スキル | Trigger | 効果 |
|---|---|---|
| 猛り I/II/III | OnPostRoll / OnPostDealDamage | 攻撃 +min(currentTurn−1, 5/9/14)・当たった撃のダメージ × 15/25/40% だけボスの次の目盛りを上げる |
| 鎧砕き I/II | OnBattleStart / OnPreDealDamage + 撃の輪 | 撃が当たるたび（`ArmorBreak.OnHit`・その撃の防御の計算の後）敵の防御 −2/−3（1 ターン 3 回・下限 0）。 自分で 0 にした後、 防御 0 の間 +20/20% (2026-11-22・II 旧 35) |
| 捨て身 | OnPreDealDamage | 攻撃に挿し防御の端子に 0 個のターン +35% |
| 血令 | OnBattleStart / OnPostRoll / OnWired / OnTurnEnd / OnBossDown | 敵の攻撃で失った HP × 60% を血へ（ボスのダウンのたび +8）。 攻撃 +血・攻撃に挿したら使い切り、 通ったら（`lastPlayerDamage > 0`）HP +floor(使った血 / 3) |
| 間合 I/II/III | OnPostRoll（III は OnWired も） | `AddAttackDicePct(0.25/0.40/0.60)`・`enemyArmorPenNegate += 3/5/8`。 III: 攻撃と防御の両方に挿したターン 次のターンの手札 +1 |
| 一対一 I/II | OnPostRoll / OnPreDealDamage | 単体戦のみ 防御 +3/+5・+10/18% (2026-11-22・旧 15/25) |
| 切り返し | OnWired / OnPreDealDamage | 両方に挿したターン 攻撃 +ceil(blockSumThisTurn × 0.6)・最初の 1 撃 `hitArmorPen += 10` |
| 不滅の刃 | OnWired / OnPreDealDamage | 両方に挿したターンが続くたび連続数 +1（途切れたら 0）・+min(72, 12 × 連続数)% |
| 疾手 I/II/III | OnBattleStart / OnPostRoll | currentTurn ≤ 5 で攻撃 +4/+6/+9・戦闘開始時に `extraDrawNext += 1/1/2`（1 ターン目の手札） |
| 連刃 I/II | OnWired + 撃の輪 | 攻撃の端子 2 枚以上のターン、 撃の列の最後に追撃 1 撃: 攻撃 = ceil(武器の攻撃力 × 1.2/1.6)（端子の外の攻撃は乗らない）・`hitIgnoreDefense`・会心と撃ごとの品はふつうの撃と同じ。 追撃するターン `itemBlockBonus += 4` (OnWired・2026-11-22) |
| 急所 | OnBattleStart / OnPreDealDamage | 会心の撃は防御の段を飛ばす（`VitalStrike.IgnoresDefense`）・2 撃目以降は `hitIgnoreDefense` |
| 処刑 | OnBattleStart / OnPreDealDamage + 撃の輪 | 敵の HP（配線の時点）≤ 40% で +50%。 撃を HP に入れた後 HP ≤ 15%（ボス 10%）なら倒す（`Execution.Kills`・`CanExecuteEnemy` で止まる） |
| 城壁 I/II/III | OnTurnEnd | 【ジャスト防御】のターン シールド +8/12/16。 それ以外で block ≥ 予告の敵攻撃 なら floor((block − 敵攻撃) × 0.6/0.9/1.2) |
| シールドバッシュ I/II | OnWired / OnPostDealDamage | 攻撃に挿し防御の端子 0 個のターン、 シールドの半分（切り上げ）/ 全部を `Kw.SpendShield` で使い、 攻撃 +ceil(使った量 × 0.9/1.5)・そのターンの撃は目盛りへの削り +30% |
| 堅守の構え | OnWired | 防御の端子に 2 個以上のターン シールド +18・次の 2 ターンは不発（`steadfastStanceReadyTurn`） |
| 黎明 | OnTurnEnd | `mutualHpLossFromEnemy == 0` なら floor(軽減前 / 10) × 5%（ジャスト防御なら 8% とボスの次の目盛りにひび `CrackBossNotch`）× 敵最大 HP を `enemyCurrentHP` から引く |
| 画竜点睛（竜閃） | OnPreDealDamage / OnPostDealDamage | `hitCount == 1` の撃で `CombatRules.GaryoPct(hitFaceMax, このターン振った目の最大, 装備ダイスの最大の目)`: 振った目の最大以上（タイ含む）→ `forceCritThisHit`・+60%（×1.6）／ それが装備ダイスの最大の目以上 → +150%（×2.5）・当たったら `BreakBossNotch`（1 ターン 1 回・2026-11-22 旧: ダイスの最大の目だけ） |
| 無我無心（竜閃） | OnWired | 攻撃の端子 1 枚: 攻撃 +ceil(攻撃以外の端子に挿した目の合計 × 1.5)（他の端子の働きはそのまま）・`itemBlockBonus += 3 × 防御の端子に挿した出目の数`（`CombatRules.MugaBlock`・2026-11-22）。 2 枚以上: 攻撃 −武器の攻撃力（=武器の攻撃力 0）・画竜点睛は撃が 2 つ以上なので不発 |

**武器そのものの防御・貫通（2026-10-28・ADR-0013）**: items.json の武器の `defense` / `armorPen`（`[無印, +]`）。
戦闘開始時に `CombatManager` が `weaponPlus` で要素を選び、`playerDefenseBase` = 2 + 防御、`playerArmorPenBase` = 貫通 へ写す。
短剣 = 貫通のみ / 斧 = 貫通高・防御低 / 剣 = 中・中 / 盾 = 防御のみ / 竜閃 = 貫通 99。値の表は GAME.md §9-4b。

**発火順序の約束（間違えると黙って無効になる）**: 攻撃値への加算は **OnPostRoll か OnWired** で
`AddPlayerAttackOrDiceBonus` を使う。`atkBase` は §9.2 step5（配線）の直後に確定するので、
OnPreDealDamage で `mutualAttackBonus` を足しても**一切乗らない**。
**配線を条件にする効果は OnWired でしか判定できない**（OnPostRoll は予告より前＝配線より前に走る）。
与ダメ% は OnPreDealDamage、被ダメ軽減は OnPreReceiveDamage、会心率は OnCriticalCheck。

**フィールドとキー**:
- `ctx.blockSumThisTurn` [perTurn] — そのターン Block の端子を挿した**出目合計**（本数ではない）。
  CombatManager の配線集計が貫通適用後に set、BeginNewTurn Phase B で 0。
- `ctx.isPairEncounter` [persistent] — 2体戦か。CombatManager が戦闘開始時に1度だけ set。
- `ctx.enemyArmorPenNegate` [perTurn] — 敵の固定削りの打ち消し。BeginNewTurn で 0。
- `accumulatedValues`（キーは `Effects.MutualKeys`）— 配線確定時に `mutualAttack/Block/Charge/SpecialDiceCount`・
  `mutualEnemyAtk`（予告・防御後）を書いてから **OnWired** を焚く。敵の攻撃の解決後に
  `mutualEnemyAtkPreDefense`（実際に放たれた直接攻撃の防御前合計）と `mutualHpLossFromEnemy` を書く。

**設計上の禁止事項**: 「全ダイス同値／全ダイス最大」を発火条件にしないこと。
素の6面×5個で `6/6^5 = 1/1296`、1ラン260ターン前後を回して期待 0.2 回 ＝ **5ランに1回**。
旧・血令（ゾロ目で×2.5を軽減不能）と旧・運命（全ダイス最大で×2）が両方これで死んでいた。

### 3-C. 固有パッシブ（`PassiveItemEffects.cs`・ITimedEffect系。TimedEffectManager が CombatStart/OnRoll/OnTurnEnd/CombatEnd/OnMapMove で発火）

| 名称 | レア | Trigger | 効果(ctx/run操作) |
|---|---|---|---|
| 黄金色の封蝋 | B | OnFloorEnter | 層に入るたび ゴールド +6 |
| 佯狂者の杖・衣・冠 | — | OnRoll | 発狂中のセット効果（物資・ADR-0002） |

> **2026-10-31 アイテム再編で移した品**: 巡礼の杖・希望の灯片・旅靴/白靴・天秤・鋼の心臓・災厄の指輪・無害灯・幸運の硬貨・相棒の魂は
> パッシブスキル側 (軸別の効果ファイル) へ移り、 ここからは class ごと削除した。
> 2026-07-18 削除済み: 記憶の砂時計 / 死神の数珠 / 嵐の徽章 / 沈黙の剣帯 / 狂乱のメダリオン / 静寂のローブ / 黒煙の符 / 蒼穹の眼 / 守護天使の鈴。

### 3-D. 2026-06-03 追加の汎用パッシブ（`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| EvenEyes(賽振りの目隠し) | B | OnPreDealDamage | 全出目が偶数 → outgoing +=0.15 |
| MasterworkNotes(匠の手控え) | G | OnPreDealDamage | run.weaponPlus ≥ 3 → outgoing +=0.12 |
| KaleidoDice(万華の賽) | L | OnPreDealDamage | 全同値/全相異/階段(3+)のいずれか → outgoing +=1.0 |

### 3-E. [剣の舞] セット（2026-06-04・`AllPassiveSkillEffects.cs`・PassiveSkillRegistry 登録）

Passive カテゴリのシナジー武器群。4枚がインベントリに揃うと全消滅し〈無銘の剣舞譜〉に変化（`GameLoop.SwordDanceSet`）。
ダイス合計加算は OnPostRoll（RecomputeDiceTotals 後で確実に効く）。run 参照は `GameManager.Instance.Run`。

| SkillId(表示) | レア | Trigger | 効果(ctx操作) |
|---|---|---|---|
| SaberWaltz(サーベル・ワルツ) | L | OnPostRoll | outgoing +=0.1 × s。 [剣の舞]を 1 枚以上持っていると LEGENDARY の枠がまれに[剣の舞]に差し替わる（店 = ShopManager の LEGENDARY 帯・交換マス・宝箱 = `SwordDanceSet.MaybeSwapToDance`・確率はどれも `SaberWaltzShopBias`・2026-11-20 に交換マス・宝箱を足した） |
| EspadaPasodoble(エスパーダ・パソドブレ) | L | OnPostRoll / OnPreReceiveDamage | outgoing +=0.4 × s（2026-11-10・旧 出目 +50% と outgoing +0.2）／被弾時 finalDamage ×1.2（被ダメ+20%・s は掛けない） |
| FleuretBallet(フルーレ・バレエ) | L | OnPostRoll | outgoing +=0.25 × s（2026-11-10・旧 出目 +30%）。敗北時の自壊+最大HP1生還は `LastStand.TryConsumeRevival`（灯火→ラストスタンド→フルーレ の順） |
| FalconTango(ファコン・タンゴ) | L | OnPostRoll | outgoing +=0.2 × s（2026-11-10・旧 出目 +20%・[剣の舞]。4 枚揃うと〈無銘の剣舞譜〉へ変化） |
| BladeDance(無銘の剣舞譜) | L(特殊) | OnBattleStart / OnPostRoll / OnPostDealDamage / OnPostReceiveDamage | 開戦毎に剣先スタック+1(最大99・ラン中持続/IRunResettable)。playerDiceTotal +剣先／与ダメ時 剣先分HP回復／被ダメ時 fixedDamageToEnemy +剣先 |

s = `SwordDanceSet.SoloScale`（2026-11-20）: 持っている[剣の舞]（昇華を含む `OwnedCount`）が 1 枚で 1・2 枚で 1.5・3 枚で 2。 伸ばすのは与ダメージの分だけ。

> 条件の差（仕様厳守）: サーベルの孤剣判定＝`OwnsPassive`（昇華込み）/ 4枚変化判定＝`ownedPassiveItems`のみ（昇華除外）。
> 無銘の剣舞譜は `EventOnlyItemFilter` 除外でショップ・ランダム配布に出ない（変化でのみ入手）。

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

汎用ステータス層（#3）。**加算的導入**：既存の出血(`enemyBleedStacks`)/威圧(`enemyThreat`)/敵ダイス減 等は**バランス済みのため現状フィールドのまま据え置き**、本フレームは
新ステータス用＋パイロットとして **炎上(burn) のみ移行**した。

- 保持: `CombatContext.playerStatusStacks` / `enemyStatusStacks`（id→stacks・**絶対視点**＝視点スワップしない）。
- 定義: `StatusRegistry.Defs[id] = StatusDef{ target, tickTiming, dotPerStack, dotScalesWithStacks, decayPerTurn, maxStacks }`。
- tick: `CombatContext.TickStatuses(StatusTick.TurnStart)` を **`PassiveSkillManager.BeginTurn` の BeginNewTurn 直後**に1回呼ぶ
  （fixedDamage 0化後）。DOT＝`dotScalesWithStacks ? stacks×dotPerStack : dotPerStack` を fixedDamageToEnemy/Player へ加算 → decay。
- burn 定義: target=Enemy, TurnStart, dotPerStack=3, scales=false, decay=1, 初期stacks=3（旧 Ignite と等価）。Ignite は開幕に `AddStatus(Enemy,"burn",3)` するのみ。
- 制限（パイロット）: DOT値は def 単位（発生源別の可変ダメ未対応）／DOT は軽減無視（fixedDamage 経由）。

---

### 3-H. キーワードの出来事 (2026-10-31〜・`Keywords.cs`)

**キーワードの出来事。** 量の変化は `Keywords.Kw` と `CombatContext` の 1 か所を通り、 品へ出来事として配られる
(`PassiveSkillManager.RaiseKeyword`)。 量は `Kw.EventAmount(ctx)`。
同じ品が 1 つの連鎖で何度働いてもよい (2026-11-19〜・旧い「1 連鎖 1 度」は削除)。 輪は品の文の制限で止める。 深さ 200 は不具合の網 (`ChainGuardDepth`・エラーを出して止める)。

| 出来事 (トリガー) | 出る場所 | 量 |
|---|---|---|
| OnRinkaiBurst | Kw.AddHeat が閾値をまたいだとき | その爆発で使った臨界 |
| OnSuperBurst | 同じ爆発が超臨界のとき (OnRinkaiBurst の直後・閾値の 1.5 倍以上・2026-11-21) | その爆発のダメージ (敵の防御の後) |
| OnTerminalDispose | 使い捨ての端子を使ったとき (2026-11-20・`CombatManager.RaiseTerminalEvents`・配線の解決 `ApplyTerminalOutcome` の直後・OnWired の前・1 枚につき 1 回) | 1 |
| OnDeckShuffle | 端子の捨て札を切り直したとき (ターンの頭に引く所 = `DrawTerminalHand` の直後 / 判断の間に消耗品・品で引いた分は `RaiseTerminalEvents` の頭) | 切り直した枚数 |
| OnKeptTerminalUse | 残す印で前のターンから残した端子を発動したとき (`RaiseTerminalEvents`・名前で数える) | 挿した出目の合計 (最低 1) |
| OnSupportTerminalUse | 補助の端子 (基本の〈時計〉を除く) を発動したとき (`RaiseTerminalEvents`・〈残響〉で 2 回働いても 1 回) | 挿した出目の合計 (最低 1) |
| OnRoleFired | 役が立ったとき (2026-11-21・`CombatManager.ResolveRoles`・配線の解決の後・OnWired の前・〈鏡合わせの盤〉の重ねた効果・〈宿命の刻み目〉のもう 1 回では鳴らない) | 強さ (2 個そろい 1 / ゾロ目・連なり 2) |
| OnBossDown | ボスの目盛りを割ってダウンが決まったとき (2026-11-21)。 通常攻撃 = `NoteBossDownMainHit` (撃の輪の中・〈目盛り削りの鑿〉も)・旧い判定 AnyDamage のターンの終わり / ひび = `NoteBossDownTurnEnd`。 `Kw.BossDownCrackKey` (1 = ひび) で分ける | 割った目盛りの数 |
| OnHeavyCanceled | 大技を取り消したとき (2026-11-21): ダウンがチャージか大技のターンに重なった (`BeginBossDownTurnCore`・ターンの頭) / 大技のターンに〈封じ・揃〉か端子の行動不能で敵が行動しなかった (敵の攻撃の解決) | 1 |
| OnChargeGain / OnOvercharge | CombatContext.AddCharge | 増えた分 / 上限で捨てた分 (〈雷壷〉は上限が無く、 ターン終了時に上限超過の 1/4 を失う・2026-11-21 Phase 4 較正: 旧 1/2) |
| OnChargeSpend | CombatContext.ConsumeCharge (リロールも通る) | 使った分 |
| OnShieldGain | consShield の setter (増えたときだけ) | 増えた分 |
| OnShieldAbsorb / OnShieldBreak | Kw.AbsorbShield (敵の直接攻撃と軽減無視の肩代わり) | 吸収した分 / 割られる前の量 |
| OnShieldDecay | BeginNewTurn Phase C の半減 | 失った分 |
| OnShieldSpend | Kw.SpendShield (品・端子の効果でシールドを使う: 〈破城斧〉・端子〈重装突撃〉の shieldSpend・2026-11-21) | 使った分 (割れの出来事は配らない) |
| OnJustGuard | 【ジャスト防御】の成立 (`Shd.NoteJustGuard`・敵の攻撃の解決の後・ターン終了の前・2026-11-21) | 予告 E |
| OnPoisonGain | CombatContext.AddStatus (敵の毒) | 増えた分 (上限到達の出来事 OnPoisonCap は 2026-11-21 に削除・読む品が無くなった) |
| OnPoisonDetonate | 【起爆】(`Psn.Detonate`・2026-11-21) | 起爆のダメージ |
| OnPoisonTick | TickStatuses と〈廃庭の霧吹き〉(敵の攻撃のとき・1 ターン 1 回・`TickPoisonOnce(50)` = 威力 50%・毒を減らさない・2026-11-21) | 毒のダメージ = `floor(毒 × enemyMaxHP × 10 / 10000)` (**2026-11-20: 1 スタック = 最大 HP の 0.1%**・旧 0.05%〔2026-11-10 に 0.1% から〕)・下限 1・`fixedDamageToEnemy` 経由 (両経路とも `CombatContext.DotDamage`)。 **2026-11-07: 1 スタック = 敵の最大 HP の 0.1% (旧 0.5%)・上限 99 (旧 10)**。 同日に一度「現在 HP」基準にしたが、 毒を積んでも与ダメの 13% しか出ず最大 HP に戻した。 ダメージを出すたびに、 残っているスタックを `poisonKeepPct`% に減らす (切り捨て・基準 50 = 半減・〈腐れ沼の苔玉〉で 75・2026-11-21)。 ターン開始の分だけ減らす (霧吹き・〈毒溜まりの主〉の 2 回目は減らさない)。 〈骨溶かしの黒油〉: 待っているダウンがある (このターンがダウン) なら ×2 |
| OnBleedDecay / OnBleedConsume / OnBleedZero | Phase C の自然減 / Kw.ConsumeBleed / 0 到達 | 減った分 / 使った分 / 1。 **自然減は 2026-11-07〜 比例減衰** `1 + 出血 / (10 + bleedDecayDivisorBonus)` (`AxisRules.BleedDecay`・〈止血禁じの軟膏〉で割る数 +15〔2026-11-21・旧 +10〕・〈医書裏の開き針〉で減らない) |

端子の 4 つの出来事は OnWired の前に配るので、 ここで品が足す「攻撃 +N」は撃ごとの口 (ADR-0016) に乗るが、 BOT の配線の判断からは見えない。
端子の出力を変える品 (山札の軸の多く) は出来事でなく `TerminalCards.Resolve` の中で効かせる (`TerminalBattleState.deckItems`・BOT と自動で一致・§3-O)。

役の軸の品は §3-P。
軸なし汎用の戦術の品 (〈隙衝きの短槍〉〈崩しの一手〉〈大技殺しの楔〉〈逆境の砂時計〉) は §3-Q。

**敵への軽減不可ダメージ (Kw.DamageEnemy)。** ターンの解決中 (`ExecuteTurnMutual` の頭〜最後の HP 同期) は
CombatManager に預け、 プレイヤーの攻撃の直前に固定ダメ枠へ、 攻撃の後の分はターン最後の HP 同期 (踏みとどまりの clamp より前) で通す。

**会心の規則。** 2026-11-21 に会心の軸へ移した (§3-N)。 撃ごとの会心 (ADR-0016): `ctx.hitIndex` (0 = そのターンの最初の 1 撃)・`ctx.hitCount` (攻撃しないターンは 0)・
`ctx.forceCritThisHit` (この撃だけ確定)。 会心率・会心倍率・与ダメの池は撃ごとに 1 撃目の前の値へ戻すので、 OnCriticalCheck で足した分はその撃だけに乗る。

### 3-I. 軸の品 (2026-11-07・`AxisItemEffects.cs`・`AxisRules.cs`)

**軸の品の数。** 品の軸は items.json の `axis`。 `CombatContext.AxisDepth(axis)` が戦闘の頭に 1 度だけ所持パッシブを数える
(呼び名: 会心 = 心眼 / 出血 = 血晶 / 毒 = 瘴気 / 臨界 = 炉温 / 充電 = 電流 / シールド = 堅牢)。 出目は軸にしない。

| 計算の位置 | 式 |
|---|---|
| 臨界 | 2026-11-21 に式を `CombatSystem.HeatCore` へ移した (§3-M)。 旧: 基礎 `15 + 炉温 / 2` (〈火花読みの眼鏡〉)・爆発の会心・〈焚き足しの炭〉+1・閾値 50 は削除 |
| 出血のダメージ | `ApplyBleedModifiers`: 出血の量そのまま (戦場の変調〈血潮〉で ×2)。 2026-11-21: `bleedDamagePct` と〈血止め知らずの針箱〉の出血の会心 (`bleedCritRule`) は削除 (§3-L) |
| シールドの減衰 | 残る割合 `min(80, 50 + shieldKeepBonusPct)`% (〈油染みの盾革〉が +20・2026-11-21 に 堅牢 × 3 から。 盾押しは §3-K) |
| 入り口 (`EntryEffect`) | 2026-11-21 に削除 (毒の行は Phase 2b・臨界の 12 行は Phase 3b・§3-M) |
| 種火・毒 r2 | 2026-11-07: 毒を与えるたび +1 → 1 ターンに最初の 1 回だけ +2 |

### 3-J. 毒の軸 (2026-11-21・`PoisonEffects.cs`・`PoisonCore.cs`・full-item-rework-ledger §②)

| 計算の位置 | 式 |
|---|---|
| 【起爆】(`Psn.Detonate`) | 消費 = `ceil(毒 / 3)` (`PoisonCore.Spend`・2026-11-22 neon 収束: 旧 `ceil(毒 / 2)`)・毒は 毒 − 消費。 ダメージ = `max(消費 × 倍率, floor(消費 × ctx.enemyMaxHP × bp / 10000))` (倍率 既定 4・〈劇毒の火薬庫〉5・2026-11-22 Phase 4 較正: 旧 3 / 4。 bp 既定 25・〈劇毒の火薬庫〉32・同日 neon 収束で追加。 ヴェスカは段ごとの最大 HP) → 〈遅効の毒刃〉`× (100 + 30 × この戦闘でここまでに起爆した回数) / 100` → 〈骨溶かしの黒油〉ダウン中 (`BossDown.ActiveNow`) × 2 (`PoisonCore.Damage`・long で切り捨て)。 `Kw.DamageEnemy` で入れる (軽減不可・防御も無視・預けた分は HpLossDiag.Lose で最終与ダメ倍率・怠惰・ダウンの ×1.5 が掛かる) |
| 起爆の起こり | 〈石抜きの毒指輪〉OnPostDealDamage の本物の撃が当たり 毒 ≥ 24 (1 ターン 1 回・Phase 4 較正で 24 → 16・neon 収束で 16 → 24) / 〈劇毒の火薬庫〉このターンに与えた毒 (OnPoisonGain の量の和) ≥ 8 のターンの OnTurnEnd (1 ターン 1 回)。 人も BOT も選ばない (自動) |
| ひびを割る | 起爆したターン (〈骨溶かしの黒油〉は OnPoisonTick が入ったターンも) の `NoteBossDownTurnEnd`: ひびを入れ (猶予の割れはしない)、 `BreakTopCrack` を上から続く限り = 正規のダウン (×1.5 あり)・OnBossDown (ひびの印 0) |
| 〈瘴気の外套〉 | 5d の takenMul 0.65 (毒 ≥ 15)。 OnTurnEnd に毒 ≥ 25 なら `Damage(Spend(毒), 旗, 回数, ダウン, 50)` を毒を消費せずに入れる (起爆の回数・ひび・OnPoisonDetonate には数えない) |
| 〈毒溜まりの主〉 | TickStatuses: 毒 ≥ 20 なら毒のダメージを 2 回 (同じ量)・減らすのは 1 回 |
| 〈腐蝕の王冠〉 | AddStatus: 付与 (種火 r2 の上乗せの後) を `ceil(× 1.5)`・上限なし。 SwapEnemy (段の交代): 毒 = 王冠 ? floor(毒 / 2) : 0 (王冠が無ければ段の交代で毒は消える・2026-11-21〜。 旧: そのまま持ち越した) |
| 蝕みの品 | 〈岸上げの麻痺瓶〉mutualEnemyAttackReduction += min(10, 毒 / 4) (OnPostRoll・2026-11-22 旧 / 5) / 〈口縫いの毒糸〉毒 ≥ 20 で mutualEnemyAttackReduction += 2・毒 ≥ 10 かつ大技のターン (Escalation.IsHeavyTurn・ダウン中は除く) で enemyAtkCutPct += 0.20 / 〈腐し刃〉OnWired で AddOutgoingPct(min(0.45, 毒 × 0.015)) / 〈麻痺の吹き矢〉OnWired で playerArmorPen += 毒 / 3 / 〈酸の小瓶〉毒 ≥ 8 で playerArmorPen += 4 |
| BOT | 配線の見積もり (AutoRunner.DeckSums の ItemSight): 〈石抜きの毒指輪〉を持ち、 このターンまだ起爆していなければ、 攻撃の撃があり 配線の後の毒 (env.enemyPoison + o.poison) ≥ 24 の挿し方に `PoisonCore.Damage(Spend(毒), 旗, 回数, ダウン, ctx.enemyMaxHP)` を軽減不可の分 (extraDmg) として足す。 ボスのひびの割れは見込みに入れない |

個々の品の数値は `PoisonEffects.cs` / `PoisonCore.cs` の定数と XML コメントが正本。

### 3-K. シールドの軸 (2026-11-21・`ShieldEffects.cs`・`ShieldCore.cs`・full-item-rework-ledger §③)

| 計算の位置 | 式 |
|---|---|
| 【ジャスト防御】(`ShieldCore.IsJustGuard`) | E = 1 体目の敵の直接攻撃 (防御・〈大結界〉の後の予告の値・`enemyAtkValue`)。 D = 敵の攻撃を受ける時の `blockSum + consShield` (シールドの吸収の前)。 成立 = `E > 0 && E ≤ D ≤ E + 許容幅` かつ敵の攻撃で失った HP (`takenApplied`・2 体目・〈連撃〉・固定ダメも含む) が 0。 許容幅 2 (〈擦り減った小盾〉で 4)。 攻撃しないターン (ダウン・チャージ・行動不能)・〈狂暴化〉の攻撃は E = 0。 成立で `Shd.NoteJustGuard` → OnJustGuard |
| ブロックの最終値 (`CombatManager.FinalBlockSum`) | 端子の blockAdd → 遺物〈堅牢の環〉`+ceil(× %)` → 大罪:傲慢 `floor(× (1 − %))` → 端子調律 `+ 個数 × N` → `+ itemBlockBonus` → 〈貫きの錐〉`− 敵の攻撃 × 2` → 〈狂暴化〉0 → 〈不完全な転移〉。 本体の配線の集計と見込み (`JustGuardForecast`) が同じ関数 |
| 見込み (`CombatManager.JustGuardForecast`) | E = `ShieldCore.EnemyAttackAfter(o, 予告)`・D = `FinalBlockSum(o.blockAdd, o.blockDice, E) + ShieldCore.ShieldAfterWiring(o, env)` (= `max(0, 今のシールド + 〈応急箱＋〉の溢れ − o.shieldSpend) + o.shield`)。 敵の攻撃までに品が足す・使うシールド (OnWired の品・〈破城斧〉の消費) は見ない |
| 5e (`ShieldCore.ApplyToTerminals`) | `o.shield +=` 〈肩当て〉防御の端子に出目全部 12 / 〈当て布の籠手〉2 個以上 6 (旧 16 / 8) / 〈擦り減った小盾〉個数 × 2 / 〈鋲の胸当て〉予告 ≥ 12 で 3・未満 0 (Phase 4 較正: 旧 × 3・5 / 2) |
| 反撃 | 〈鏡返しの小盾〉OnTurnEnd: `floor((min(EnemyAtk, blockSumThisTurn) + shieldAbsorbedAttackThisTurn) × (ジャスト防御 ? 100 : 60) / 100)` (旧 40) / 〈重装兵の隠し刃〉OnJustGuard: E / 〈割れ盾の仕返し〉OnShieldBreak: 割られる前の量 / 〈落城の礎石〉OnShieldAbsorb・OnShieldDecay: 減った量 × 2 と `CrackBossNotch` (次の目盛りにひび・既にあれば何もしない)。 どれも `Kw.DamageEnemy` (軽減不可・防御無視) |
| 攻撃へ | 〈盾押しの心得〉OnWired・攻撃の端子あり: `AddPlayerAttackOrDiceBonus(floor(シールド × 35 / 100))` (撃ごとに乗る) / 〈力みの小手〉OnShieldGain の累計 12 ごとに力み +1・OnWired で 4 × 力み を攻撃 +N にして 0 / 〈相棒の魂〉張り直した次のターンの OnWired で +10 / 〈受け太刀の籠手〉ジャスト防御の次のターンの OnTurnStart で `firstHitAttackBonus += 8` |
| 倍率 | 〈破城斧〉OnPreDealDamage: 最初の撃でシールド ≥ 20 ならそのターン、 シールド ≥ 6 の撃ごとに `Kw.SpendShield(6)`・`hitFinalDamagePct += 110`・`hitIgnoreDefense` (2026-11-22・旧 15 / 8 / 80) / 〈静寂の構え〉攻撃の端子なしで受け切った (`Psn.Shutout`) 次のターンの本物の撃に `hitFinalDamagePct += 60` (ジャスト防御なら 100) / 〈防人の軍標〉OnJustGuard: `finalDamagePct += 45` (旧 35)・次のターンの手札 +1・この戦闘のジャスト防御が 2 回以上 (neon 収束: 旧 3) なら次のターン、 撃の輪 (CombatManager.Turn) が全部の撃をもう 1 回ずつ発火 (`BorderGuardStandard.EchoNow`・会心は振り直す・〈必殺の照準器〉で発火した撃には重ねない・2026-11-22) / 〈落城の礎石〉OnShieldAbsorb・Decay で 1 ターンの失った量を数え、 ターンの終わりに ≥ 12 (neon 収束: 旧 20) なら次のターン OnWired で `AddPlayerAttackOrDiceBonus(失った量 / 2)`・本物の撃に `hitFinalDamagePct += 60` (減るたび CrackBossNotch・2026-11-22 旧: 減った量 × 2 の軽減不可) / 〈敗残兵の部隊章〉OnShieldAbsorb・Decay・Spend の累計 8 ごとに反骨 +1 (最大 10)・OnPostRoll で `AddOutgoingPct(0.08 × 反骨)` (Phase 4 較正: 旧 0.12) / 〈割れ盾の仕返し〉割られた次のターン `AddOutgoingPct(0.35)` |
| 守り | 〈重ね札の盾〉シールド ≥ 16 (2026-11-22・旧 10) で OnPreReceiveDamage −2 (旧 −3) / 〈油染みの盾革〉OnTurnEnd: そのターンにジャスト防御か完封なら `shieldKeepBonusPct = 20`・でなければ 0 (2026-11-22・旧: 常に 20) / 〈脈なしの鋼心臓〉戦闘開始時 X (4・勝つたび +1) と `shieldNoDecayAt = 50` (ターン頭のシールド ≥ 50 なら減衰しない・2026-11-22・旧 6 / 35) / 〈二拍目の心臓〉HP ≤ 50% で `ceil(最大 HP × 30%)` (旧 60%)・`shieldGainPct += 50` (consShield の setter で増えた分に `× (100 + これ) / 100`・遺物の上乗せより前) / 〈相棒の魂〉割られたら `floor(前の量 × 70%)` (1 戦闘 1 回) / 〈聖路の白靴〉戦闘開始時 `min(20, 歩数 / 2)` (2026-11-22・旧 歩数) / 〈英雄の意志〉強敵の大技かチャージのターンの頭 +18・そのターンのジャスト防御で `BreakBossNotch` (HP に関係なく次の目盛りを満額のダウンで割る) |
| BOT | 配線の見積もり (AutoRunner.DeckSums の ItemSight): ジャスト防御で働く 6 品のどれかを持てば、 挿し方ごとに `JustGuardForecast` を見て、 成立するなら 鏡返し +E × 60%・隠し刃 +E を軽減不可の分 (extraDmg) に、 軍標・受け太刀・静寂・英雄の先の得を控えめな定数で足す。 〈破城斧〉は 1 撃あたりの見込み × 80% × 払える撃の数 |

個々の品の数値は `ShieldEffects.cs` / `ShieldCore.cs` の定数と XML コメントが正本。

### 3-L. 出血の軸 (2026-11-21・`BleedEffects.cs`・`BleedCore.cs`・full-item-rework-ledger §④)

| 計算の位置 | 式 |
|---|---|
| 撃ごとの出血 (5f・`BleedCore.ApplyToTerminals`) | 撃 h (攻撃の端子 1 枚・【再起動】の撃は元の端子の目) ごとに `attackHitBleed[h]` = 〈欠け刃のナイフ〉1 + 〈百人切りの鉈〉1 + 〈鉛入りの握斧〉挿した目の最大 ≥ 4 で 2 + 〈裂け目の楔〉挿した目の最小 1〜3 で 3 + 〈返し刃の留め金〉攻撃の端子の発動 ≥ 2 枚で 1 (旧 2) + 〈鋸刃の山刀〉敵の HP ≥ 50% 1・< 50% 2・< 25% 4 + 〈医家の反り刃〉HP 満タンか ≤ 50% で 2 (1 ターン 3 撃まで) + 〈先血の腕輪〉h = 0 で 1 (旧 2・開始時の出血も 4 → 3) + 〈血日に澄む佩玉〉h = 0・出血 ≥ 6 で 3 → 〈血止め知らずの針箱〉h ≥ 1 かつここまでが > 0 なら +1。 敵の HP・出血は配線の時点 (`TerminalEnv`) |
| 撃ごとの攻撃 +N (5f) | `attackHits[h] +=` 〈血日に澄む佩玉〉h = 0・出血 ≥ 6 で 3 / 〈医書裏の開き針〉`2 × floor(出血 / 10)` / 〈激情の刃〉h ≥ 2 で `min(15, floor(出血 / 2))` (出血は配線の時点)。 `attackAdd` にも足す |
| 出血を入れる所 | 本体の撃の輪 (CombatManager.Turn): 撃 h の ProcessDamage の後、 与ダメ > 0 なら `AddEnemyBleed(attackHitBleed[h])` (次の撃の OnPreDealDamage は増えた出血を見る)。 〈涸れぬ血晶〉`bleedDouble`: 今の出血 ≥ 10 (2026-11-22 neon 収束: 旧 5) なら与える出血 ×2 (`AddEnemyBleed` の中・`BleedCore.CrystalGain`・全部の経路) |
| 処刑 | 〈百人切りの鉈〉撃を HP に入れた直後 (〈天眼の鍔〉と同じ輪): 出血 ≥ 12 で線 = `最大 HP × min(250, 出血 × 12 (ボスは ÷ 2)) / 1000` (`BleedCore.CleaverKillLine`)・HP ≤ 線なら 0 (`CanExecuteEnemy` で止まる) |
| 倍率 | 〈血桜〉OnPreDealDamage・本物の撃・h ≥ 1・出血 ≥ 12: `Kw.ConsumeBleed(2)`・`hitFinalDamagePct += min(120, 消費前の出血 × 2)` / 〈血の楔〉出血 ≥ 20 の撃ごとに `hitFinalDamagePct += 40`。 敵の得る量の弱めは `30 ÷ (出血 + 30)` 倍 |
| 出血を使う | 〈紅の搾り具〉OnPostDealDamage・出血 ≥ 15 (1 ターン 1 回): `ConsumeBleed(ceil(出血 / 3))`・使った量 × 8 を `Kw.DamageEnemy`・`CrackBossNotch` / 出来事 OnBleedConsume: 〈血車〉使った量 × 2 の `Kw.DamageEnemy`・次のターンの手札 +1 (1 ターン 1 回) / 〈戻り血の古鎌〉`floor(使った量 × 60 / 100)` を次のターンの頭に `AddEnemyBleed` / 〈血脈の災環〉(減衰も) 減った量 × 5 の `Kw.DamageEnemy`・`floor(その 20%)` を回復 (1 ターン 10 まで) / 〈魂喰らいの血石〉(減衰も) 減った量を `itemBlockBonus` (1 ターン 12 まで・攻撃の中で使った分は次のターンの頭) |
| ターンの終わり | 〈逆綴じの止血帯〉`Kw.DamageEnemy(出血 × (hitCount ≥ 3 ? 2 : 1))` (Phase 4 較正: 旧 3 : 2) / 〈血の滴る刃先〉出血 ≥ 8 で撃ごと HP +1 (ターン 3 まで・旧 +2 / 6) / 出血のダメージ (§3-I) |
| 守り | 〈凝血の小瓶〉ターン開始時の出血 ≥ 16 で `enemyAtkCutPct += 0.25`・≥ 30 のボスの大技のターン (`Escalation.IsHeavyTurn`・ダウン中を除く) は `+= 0.5 / (heavyMul + 挑戦カードの上乗せ)` / 〈棘付きの籠手〉受け切り (`Psn.Shutout`) かつ `blockSumThisTurn ≥ EnemyAtk` で出血 +3・次のターンの `firstHitAttackBonus += 4` / 〈血の滴る刃先〉出血 ≥ 8 で撃ごとに HP +2 (1 ターン 6 まで) |
| その他 | 〈鎧縫いの針〉stats `pen` × 出血 (OnWired) と、 出血 ≥ 15 の撃ごとに `RaiseBossNextNotch(floor(その撃の与ダメ × 20 / 100))` / 〈止血禁じの軟膏〉割る数 +15 / 〈医書裏の開き針〉`bleedDecayDisabled` / 〈先血の腕輪〉戦闘開始時 出血 +4 |
| BOT | 配線の見積もり (AutoRunner.DeckSums): 5f の撃ごとの出血 (血晶の倍込み) を 〈鉤爪〉と同じ `DeckBleedDmg` で、 撃を順に追って 〈血の楔〉+40%・〈血桜〉の % を 1 撃の見込み (1 ターンの見込み ÷ 撃の数) に、 〈紅の搾り具〉の × 8 を軽減不可の分に足す。 5f の攻撃 +N は Resolve の結果にそのまま入る |
| 計装 | `CombatManager.BleedSpendFired` (Kw.ConsumeBleed の回数) / `BleedExecuteFired` (鉈の処刑) → ランの記録 `bleedSpendFired` / `bleedExecuteFired` |

個々の品の数値は `BleedEffects.cs` / `BleedCore.cs` の定数と XML コメントが正本。

### 3-M. 臨界の軸 (2026-11-21・`HeatEffects.cs`・`HeatCore.cs`・full-item-rework-ledger §⑤)

| 計算の位置 | 式 |
|---|---|
| 有効化 | 臨界の品を 1 つ以上持つ戦闘は戦闘開始時 (品の OnBattleStart の前) に `Kw.EnableHeat`: 閾値 = `max(12, 32 − (〈早沸かし釜〉? 6 : 0))` (`HeatCore.Threshold`・2026-11-22 段 3 で 30 → 36・neon 収束で 36 → 32)・〈恒温の竈〉は臨界 = 閾値の半分から。 端子が臨界を出した戦闘も同じ |
| 臨界を得る (`HeatCore.Gain`) | `n` → 〈焚き足しの炭〉+1 (1 ターン 6 回まで・2026-11-22 段 3: 旧 +2) → 〈燧石の指輪〉の倍のターンは ×2 → 遺物〈炉温〉2 個 +20% (切り上げ)。 1 回の `Kw.AddHeat` が 1 回の「得る」 |
| 爆発 (`HeatCore.TryBurst`) | 臨界 ≥ 閾値で 1 回。 超臨界 = 臨界 ≥ 閾値 × 1.5。 臨界 → 0 (〈帳簿外の爆薬〉は 閾値ぶんだけ減らし、 届けば続けて・そのターン 5 回まで)。 〈恒温の竈〉は爆発の後 `max(残り, 8)`。 〈二度詰めの雷管〉爆発の後 `stack = min(32, stack + 4)` (減らない・2026-11-22 neon 収束: 旧 48 / 6)。 〈暴れ窯の火床〉超臨界の後 閾値 −4 (この戦闘で −16 まで・下限 12) |
| 爆発のダメージ (`HeatCore.Damage`) | `30 + stack + burstFlatBonus (〈天工開物〉8 × 強化) + 〈溢れ取りの鋳型〉2 × 溢れ (閾値を超えた臨界・帳簿外は 0)` → 超臨界 ×1.5 (〈暴れ窯の火床〉×2.0) → 〈火花読みの眼鏡〉×(1 + 0.05 × この戦闘の爆発の回数 (この爆発を含む)) (2026-11-22 neon 収束: 旧 天工 12・鋳型 3・眼鏡 0.08) → 〈燧石の指輪〉の倍のターン ×(1 + 0.25 × このターンのそれより前の爆発) → 〈八日目の灰燼〉臨界 ≥ 40 で起きた爆発 ×1.6 → 〈柱折りの発破筒〉ダウン中の敵 ×2.0 (各段 切り捨て) |
| 敵の防御 (`HeatCore.VsDefense`) | `DefenseFormula.Apply(ダメージ, 敵の防御 − floor(敵の防御 × 50 / 100))` (防御の 50% を無視)。 〈火花読みの眼鏡〉は防御を全部無視。 会心・与ダメの池は乗らない。 その後 `CombatManager.LandBurst` → HpLossDiag.Lose が最終与ダメ倍率・怠惰・ダウンの ×1.5・遺物〈炉温〉の現在 HP の % を掛ける |
| ボスのひび | 爆発のたび `CrackBossNotch` (次の目盛りにひび・既にあれば何もしない)。 〈柱折りの発破筒〉はその後 `BreakBossCrack` (満額のダウン) |
| 端子の解決の 5g (`HeatCore.ApplyToTerminals`) | `heatGains` (1 つ = 1 回の得る) に: 〈過熱石〉攻撃に挿した目 ≥ 2 で 6・3 個とも攻撃なら 12 / 〈燻りの革胸当て〉防御の端子の発動 (再起動も) 1 回ごとに 4 (と `blockAdd += 2`) / 〈火守りの火搔き棒〉`6 × (防御 + 充電 + 補助に挿した目)` / 〈点火スパナ〉撃ごとに 2 / 〈荒砥〉撃ごとに 1 (臨界の品の値は 2026-11-22 Phase 4 較正: 旧 10 / 18・4・2)。 `heatDouble` = 〈燧石の指輪〉かつ攻撃の撃 (`attackHits.Count`・【再起動】の撃を含む) ≥ 3 (2026-11-22 neon 収束: 旧 攻撃の端子の発動 ≥ 2 枚) |
| 本体の順 | 配線を反映する所 (`ApplyTerminalOutcome`・OnWired と撃より前): 倍のターンを立てる → 端子の `heat` → `heatGains` を 1 つずつ `Kw.AddHeat`。 ここでの爆発はそのターンの撃より前 (【過熱】がそのターンの撃に間に合う)。 ターンの終わりに `HeatCore.EndGain` = `floor(TerminalOutcome.attackPips / 2)` (攻撃の種類の端子に挿した出目の合計・〈号令の剣〉〈歯車の軸〉の出目 +N は入る・〈剛腕〉の倍・端子の倍率・基礎攻撃・品の攻撃 +N・端子調律・【再起動】〈残響〉は入らない) を `Kw.AddHeat` (2026-11-22 neon 収束: 旧 攻撃の端子の合計 attackSum) |
| 【過熱】 | このターンに爆発した (`Kw.Overheated`・`BurstTurnKey == currentTurn`) |
| 品 (出来事) | 〈工廠の材料箱〉爆発で `playerDefense +2` (この戦闘 +10 まで・毎ターン積み直す) / 〈乾き火口箱〉開始時 +14 / 〈炉番の外套〉ターンの終わり `min(12, floor(EnemyAtkPreDefense / 2))` / 〈継ぎ革の旅靴〉開始時 `min(20, 歩数 × 2)` / 〈荒砥〉爆発で与ダメの池 +5% (この戦闘 30% まで・OnPostRoll で足す = 次のターンから) / 〈点火スパナ〉爆発したターン 次のターンの手札 +1 (1 ターン 1 回) / 〈恒熱の炉壁〉爆発で `itemBlockBonus += min(14, floor(防御の後のダメージ × 25 / 100))` / 〈二度目の打金〉【過熱】と次のターン OnWired で `AddPlayerAttackOrDiceBonus(3)` (撃ごと) / 〈溢れ取りの鋳型〉爆発で `2 × 溢れ` % を溜め、 次の本物の撃の OnPreDealDamage で与ダメの池へ (その撃だけ) / 〈早沸かし釜〉敵が攻撃したターンの終わり +2 (閾値 −6・2026-11-22 段 3: 旧 +3・−8) / 〈天工開物〉上の表 / 〈希望の灯片〉爆発 ≥ 2 回で勝つと最大 HP +3・【過熱】のターンの終わり HP +3 / 〈八日目の灰燼〉OnPreReceiveDamage で致死・臨界 ≥ 25 なら HP 1 で止め、 臨界を全部使って `使った量 × 3` を `LandBurst` (防御を通さない・1 戦闘 1 回) / 〈烈刃〉【過熱】の本物の撃の `hitFinalDamagePct += min(150, 15 × この戦闘の爆発の回数)` / 〈無害灯〉臨界 ≥ 15 なら敵の直接攻撃で失う HP を 0 にし臨界 −15・`VsDefense(floor((30 + stack + flat) × 30 / 100))` を `LandBurst` (1 戦闘 2 回・1 ターン 1 回・CombatManager の敵の攻撃・`HarmlessLamp.Apply`) |
| BOT・画面 | 配線の見積もり (AutoRunner.DeckSums) と戦闘の右の列 (`Game2DBattleSim.HeatState`) は `Kw.HeatState(ctx)` の写しで `HeatCore.Forecast(写し, 端子の結果)` (本体と同じ順で Gain / TryBurst・ターンの終わりは `EndGain`)。 BOT: 爆発のダメージを軽減不可の分へ・溜まりの増減を `臨界 × 30 / 閾値 × DeckGaugeShare`・配線の時の爆発か【過熱】なら〈二度目の打金〉+6 × 撃・〈烈刃〉の % × 1 ターンの見込み・〈無害灯〉が使えるターンは被ダメ 0 |
| 計装 | `CombatManager.BurstFired` (爆発の回数) / `BurstBreakFired` (〈柱折りの発破筒〉の正規のダウン) → ランの記録 `burstFired` / `burstBreakFired` |

個々の品の数値は `HeatEffects.cs` / `HeatCore.cs` の定数と XML コメントが正本。

### 3-N. 会心の軸 (2026-11-21・`CritEffects.cs`・`CritCore.cs`・full-item-rework-ledger §⑥)

| 計算の位置 | 式 |
|---|---|
| 超過会心率 (全員の規則・`CritCore.ExcessMul`) | 会心した撃の会心倍率 `+= max(0, critAdd − 1)` (超過 1% ごとに +0.01・〈寸鉄〉は ×2)。 critAdd = 武器 + `critRatePctAdd` + 整備パネル (100% 超えもそのまま・`lastCritAddPct`) |
| 会心倍率 (`CritCore.EffectiveMul`) | `m = max(1, criticalMultiplier − 敵の会心耐性)` → `m += 超過` → 〈天眼の鍔〉を持ち critAdd ≥ 1 なら `m ×= 1.25`。 本体 (ProcessDamage)・BOT (FillReadOut / ReadOutOneHit)・画面 (PreviewHudStats の critMul) が同じ関数 |
| このターンの会心 (`Crit.Note`) | 会心のたび (OnCriticalDamage の前) にこのターンの数 (`Crit.ThisTurn`) と会心を出したターン (`Crit.LastTurn` = 前のターンに会心) を書く |
| 撃ごとの目 | 撃の輪が `ctx.hitFaceMax = CritCore.HitFaceMax(端子の結果, 目, h)` (その撃の端子に挿した目の最大) を書く (〈狙い目の刻み〉) |
| 〈必殺の照準器〉 | 攻撃の端子 1 枚のターン、 その撃が会心なら同じ撃をもう 1 回 (撃の輪で `h--`・池・会心倍率・会心率を戻し、 `forceCritThisHit` と `critSkipRoll`。 critAdd ≥ 1 なら `criticalMultiplier += 1.0`)。 もう 1 回の撃も目盛り・天眼の処刑・5f の出血の対象 |
| 配線の後のブロック | 配線の集計 (`FinalBlockSum`) の後に品が足した `itemBlockBonus` (OnWired・会心のたび・爆発のたび) は、 敵の攻撃の解決の前に `blockSum` へ足す (〈狂暴化〉なら足さない・遺物/大罪/錐/不完全な転移は掛けない) |
| 〈死相の面頬〉 | 敵の攻撃 `× (1 − min(0.6, (会心倍率 + 超過 − 1) × 0.25))` (会心したターン・`ApplyLossDamageModifiers`)。 会心した撃の与ダメはヴェスカのシールドに吸われない (`ApplyVescaDefense` の unabsorbable) |
| 〈天眼の鍔〉の処刑 | 会心した撃を HP に入れた後、 敵の HP ≤ `最大 HP × min(0.30, 掛かった会心倍率 × 0.06)` (ボスは半分) なら倒す (`CanExecuteEnemy` で止まる) |
| 品 (出来事) | 〈鷹匠の革手袋〉OnWired 攻撃の目の最大 ≥ 4 で 会心率 +16%・会心倍率 +0.15 (2026-11-22 neon 収束) / 〈狙い目の刻み〉撃の目 ≥ 5 で その撃 +20%・6 で確定 / 〈一の矢〉hitCount = 1 で撃の目 ≥ 4 の撃 +35%・+0.30 (2026-11-22 neon 収束) / 〈余韻の鈴〉前のターンに会心 1 回以上 (`Crit.LastTurnCount`) かつこのターンの攻撃の撃 ≥ 2 (`Fam.AttackTerminals`・【再起動】を含む) で OnWired +14%・ブロック +4 (2026-11-22 neon 収束) / 〈受け流しの扇〉防御の端子 2 枚以上 (`Fam.BlockTerminals` = `TerminalOutcome.blockTerminals`・【再起動】を含む) での完封 (`Psn.Shutout`) の次のターン +28%・ブロック +4・最初の会心の撃で `playerArmorPen += 8` (2026-11-22 neon 収束) / 〈銑鉄棍〉上の ProcessDamage / 〈測量師の片眼鏡〉1〜2 ターン目の撃に `min(1, 歩数 × 0.05)` (neon 収束: 旧 1 ターン目) / 〈研ぎ師の見立て書〉会心のたび ブロック +3 (ターン 9 まで)・ターン 2 回目の会心で HP +2 (ターン 1 回) / 〈刃毀れの見立て〉`critArmorPen += 12`・会心した撃のダメージ × 25% だけ `RaiseBossNextNotch` / 〈渾身の柄頭〉hitCount = 1 の撃が会心なら `hitFinalDamagePct += 35`・ブロック +6 / 〈瑠璃砥石〉+10%・ターン 2 回目の会心で戦闘の会心倍率 +0.10 (最大 1.00・OnCriticalCheck で撃ごと) / 〈斬り重ねの鞘〉会心のたび +0.12 (2026-11-22・旧 0.20) が積もり、 ターン開始で `n − ceil(n/2)` (前のターンの会心 ≥ 3 なら減らない) / 〈重ね斬りの型〉同じターンに直前 2 撃が会心なら その撃の会心倍率 +1.0・会心したらブロック +5 / 〈連なる閃き〉前のターンに会心 1 回以上・このターンの最初の会心の撃とその後の撃 `hitFinalDamagePct += 25` (2026-11-22 Phase 4: 1 回・45 → 2 回・35 → neon 収束: 1 回・25) / 〈抜き打ちの作法〉1 ターン目かダウン中の最初の撃 確定・+1.0 / 〈寸鉄〉`critExcessDouble`・`critArmorPen += IgnoreAll` / 〈一点読みの片眼〉OnWired +20%・+0.50・非会心 ×0.5 (会心の品の値は 2026-11-21 Phase 4 較正・旧値は docs/GAME.md §13-8) / 〈千里眼の水晶〉ターン 2 回目の会心で 次のターン 手札 +1・会心率 +40%・全部の撃 `hitFinalDamagePct += 25` |
| BOT | `FillReadOut` が配線の前に分かる会心率と倍率 (`Crit.ForecastTurn`: 受け流し・瑠璃・斬り重ね・一点読み・千里眼・測量師) を足し、 `ReadOutOneHit` が撃ごとに 余韻 (攻撃の撃 ≥ 2)・鷹匠 (攻撃の目の最大)・狙い目 (撃の目)・一の矢/渾身/照準器 (撃の数 1)・抜き打ち (最初の撃) を足して `CritCore.EffectiveMul` で倍率を出す。 〈銑鉄棍〉は非会心の撃の池へ。 照準器は `p × 会心の撃` を足す。 乱数は引かない |
| 計装 | `CombatManager.CritOverflowFired` (超過会心率が倍率になった会心) / `CritExecuteFired` (天眼の処刑) / `CritRefireFired` (照準器のもう 1 回) → ランの記録 `critOverflowFired` / `critExecuteFired` / `critRefireFired` |

個々の品の数値は `CritEffects.cs` / `CritCore.cs` の定数と XML コメントが正本。

### 3-O. 山札の軸 (2026-11-21・`DeckEffects.cs`・`TerminalCards.cs` の `DeckItems`・full-item-rework-ledger §⑦)

分岐 A〈廃棄・回転ループ〉/ 分岐 B〈永続・Pick 盤面構築〉。 軸の数 (采配) を読む品は無い。 n = このターンの使い捨て (`disposedNow`)・T = この戦闘の使い捨ての累計 (`disposedTotal`・このターンの分を含む)・
sup = 発動した補助の端子 (基本を除く)・perm = 場の常設 (基本を除く・止まっていない)。

| 計算の位置 | 式 |
|---|---|
| 6c 撃ごとの攻撃 +N (`ApplyDeckItems`) | 〈焼き入れの挟み〉+1 × n / 〈留め金の栞〉残した端子を発動したターン +5 / 〈使い切りの矜持〉+4 × min(1, n) / 〈早回しのゼンマイ〉+min(10, 切り直した枚数) / 〈再利用のるつぼ〉n > 0 なら +min(16, 3 × T) / 〈継ぎ目のない導線〉+6 × sup / 〈常設の作業台〉+5 × perm (6c の値は 2026-11-22 neon 収束: 矜持 +2・るつぼ 2・作業台 +3 から) |
| 6c ブロック | 〈補助歯車〉+6 × min(3, sup) / 〈使い切りの矜持〉+5 × min(1, n) / 〈灰払いの箒〉+3 × n (neon 収束: 矜持 +3・箒 +1) (n ≥ 2 のターンだけ次のターン 1 枚多く引く) / 〈早回しのゼンマイ〉切り直したターン +6 / 〈常設の作業台〉+3 × perm |
| 6c 別枠の掛け算 (`damageMul` → `_termDamageMul`・主攻撃) | 〈常設の作業台〉×(1 + 0.13 × perm) / 〈灰から組む機関〉T ≥ 2 なら ×(1 + min(0.60, 0.09 × T)) (neon 収束: 0.10 / 0.06) (山札・役の品の値は 2026-11-21 Phase 4 較正) |
| 6c その他 | 〈継ぎ目のない導線〉`armorPen += 5 × sup` (本体は `ctx.playerArmorPen` へ・BOT は `ws.armorPen`) / 〈折り目のついた札〉残した端子を発動したターンが 2 ターン以上続けば `outgoingPctAdd += 0.45` / 〈灰払いの箒〉n ≥ 2 で `extraDrawNext += 1` / 〈仕込みの配電盤〉基本以外 3 枚以上で `extraDrawNext += 2` / 〈空回りの錘〉挿さなかった出目 min(2, 数) |
| 切り直し (`BeforeShuffle`) | 〈無銘の設計図〉山札 > 3 枚なら捨て札の基本の端子を 1 枚 exiled (最後の攻撃の端子は外さない = `KeepsLastAttack`) / 〈永久機関の主軸〉1 枚多く引く (1 ターン 2 枚まで・`engineDrawTurn`)・shuffles が 2 の倍数なら `doubleAll` |
| 使い捨ての外し方 (`DisposeFired`) | 〈解体屋の袋〉T が 4 の倍数をまたぐたび (旧 3) exiled の最後の使い捨てを returnNext へ (1 戦闘 5 回まで・`bagReturns`) |
| 戦闘の始め (`BeginTerminalDeck`) | 〈予備の差込口〉`extraDrawNext += 2` / 〈整理された工具箱〉ランの山札 ≤ 12 枚なら手札 +1 / 〈常設の作業台〉山の常設を全部場へ |
| 出来事 (`DeckEffects.cs`) | 〈焼き入れの挟み〉OnTerminalDispose で `Kw.DamageEnemy(3)` (防御無視) / 〈仕込みの配電盤〉基本以外 4 枚以上のターン (旧 3・多く引くのは 2 → 1 枚) (`LastTerminalOutcome.firedNonBasic`)、 当たった撃のダメージ × 20% だけ `RaiseBossNextNotch` |
| BOT | 6c と出し入れは Resolve と同じ関数なので自動で見積もりに入る。 〈焼き入れの挟み〉の 6 × n は `ws.extraDmg` (軽減不可の分)。 目盛りの削りは見ない。 乱数は引かない |
| 計装 | `CombatManager.DeckDisposeFired` (使い捨ての回数) / `DeckShuffleFired` (切り直し) → ランの記録 `deckDisposeFired` / `deckShuffleFired` |

個々の品の数値は `TerminalCards.cs` の山札の定数と `DeckEffects.cs` の XML コメントが正本。

### 3-P. 役の軸 (2026-11-21・`RoleEffects.cs`・`TerminalCards.cs` の `RoleItems`・full-item-rework-ledger §⑧)

分岐 A〈低目ロール連打〉/ 分岐 B〈リロール＆格上げ爆発〉。 軸の数 (賽目) を読む品は無い。 C = この戦闘で役が成立した回数 (`TerminalBattleState.roleCount`・Resolve の中ではこのターンの役を足した C + 1)・
F = 役の目 (`TerminalCards.FaceOfRole`・2 個そろいとゾロ目の目・連なりは 0)・L = 直前のターンに立った役の目 (`roleLastFace`・0 = なしか連なり)・
S = 直前のターンまで同じ目の役が続いたターン数 (`roleStreak`・表示だけ)。 どれも `CombatManager.ResolveRoles` がターンの役の後に書く (〈宿命の刻み目〉のもう 1 回・〈鏡合わせの盤〉の重ねた効果は数えない)。
big = ゾロ目か連なり。 品が働く目 (2026-11-22〜・前はどの役でも / 1・2・3 の目を含む役) は `TerminalCards.*Face` の定数。

| 計算の位置 | 式 |
|---|---|
| 6d 撃ごとの攻撃 +N (`ApplyRoleItems`) | 〈見切りの木札〉F ∈ {4, 5} なら +1 / 〈血染めの歯車〉振り直したターン (`env.diceRerolled`) かつ F = 3 なら +6 / 〈符丁の刻み板〉F ∈ {3, 4} なら +min(6, 1 × (C + 1)) × (ゾロ目 ? 2 : 1) (C はどの役も数える) |
| 6d ブロック | 〈見切りの木札〉F ∈ {4, 5} なら +2 / 〈雷鳴のゾロ目〉big なら +4 |
| 6d 掛け算 | 〈雷鳴のゾロ目〉big なら `damageMul ×= 1.2` / 〈血濡れの符丁〉役が立てば (連なりも) `damageMul ×= 1 + min(0.5, 0.08 × (C + 1))` / 〈揃いの拍子木〉F ≠ 0 かつ F = L なら `outgoingPctAdd += 0.12` |
| 6d その他 | 〈欠け目の見立て〉F ∈ {1, 2} なら `armorPen += 12` (6d の目の集合は `TerminalCards.*Faces`・2026-11-22 neon 収束で広げた) |
| 出来事 (`RoleEffects.cs`) | 〈欠け目の見立て〉1・2 の役のターン、 当たった撃のダメージ × 25% だけ `RaiseBossNextNotch` / 〈揃いの拍子木〉OnRoleFired で F ≠ 0 かつ F = L なら次のターン 手札 +1・無料の振り直し +1 / 〈雷毒の導管〉OnRoleFired で F ∈ {1, 2, 3} なら次のターン 手札 +1・ブロック +4 / 〈負け越しの賽〉OnTurnEnd で役なしなら次のターン 無料の振り直し +1・ブロック +6 |
| 次のターンのブロック | nextTurnBuffs `roleBlockNext` (`YachtRoleEffects.BlockNextKey`) → BeginNewTurn の Phase B で `itemBlockBonus` の初期値 (〈負け越しの賽〉〈雷毒の導管〉〈鏡合わせの盤〉) |
| 役の適用 (`ResolveRoles` / `DecideOmniPair`) | 〈擦り切れた賽袋〉戦闘の最初の振り直しが無料 / 〈鏡合わせの盤〉〈宿命の刻み目〉〈万象のゾロ目〉は §9.2 の役の段 |
| BOT | 6d は Resolve と同じ関数なので配線の見積もりに自動で入る。 振り直すかの線 (`RoleRerollPips`) は TerminalCards の定数で 6d を面の出方ごとに数え、 掛け算は振った目の和に掛けた量。 次のターンへ持ち越す分は見ない。 乱数は引かない |
| 計装 | `CombatManager.RoleFormedFired` (役の成立) / `RoleOmniFired` (万象の格上げ) → ランの記録 `roleFormedFired` / `roleOmniFired` |

個々の品の数値は `TerminalCards.cs` の役の定数と `RoleEffects.cs` の XML コメントが正本。 役そのものの効果の表 (`YachtRoleEffects.cs`) は変えていない。

### 3-Q. 金・軸なし汎用 (2026-11-21・`GoldEffects.cs` / `GeneralEffects.cs`・full-item-rework-ledger §⑨ §⑩)

枠外 (動的 4 枠ロックの対象外)。 金は戦闘の外の経済だけで、 与ダメに入る金の品は〈研ぎ代の領収書〉〈相場師の算盤〉〈溶金の領主剣〉の 3 品。

| 品 | 式・適用点 |
|---|---|
| 加重の拳套 | OnPreDealDamage: 本物の撃で `hitIndex ≥ 1` なら `hitOutgoingPct += 20` (その撃だけ与ダメ % の池へ・③-A) |
| 隙衝きの短槍 | OnWired: 敵がチャージ中かダウン中 (`CombatManager.EnemyChargingOrDownNow`) なら `AddOutgoingPct(0.40)` |
| 崩しの一手 | OnBossDown (ターン 1 回): 次のターンの手札 +2 (`extraDrawNext`)。 次のターンの OnPostRoll で `AddPlayerAttackOrDiceBonus(6)` (攻撃 +N = 全撃) |
| 大技殺しの楔 | OnHeavyCanceled: `Kw.DamageEnemy(最大 HP × 10%)`・次のターンのブロック +15 (`roleBlockNext`・取り消したターンは敵が攻撃しない) |
| 逆境の砂時計 | 段階 (`CombatManager.EscalationStageNow`) 2 以上: OnTurnStart で `itemBlockBonus += 4`・OnWired で `AddOutgoingPct(0.25)` |
| 帯革の薬嚢 | OnConsumableUse: `DrawIntoHand(1)`・`itemBlockBonus += 6` |
| 気圧計のペンダント | OnBattleStart: 嵐のフェーズ × 5 をシールドへ (開幕のバリア・台帳 §⑩ の「維持可」)。 OnPostRoll: `AddPlayerAttackOrDiceBonus(2 × フェーズ)` |
| 研ぎ代の領収書 | OnPostRoll: `AddPlayerAttackOrDiceBonus(2 × RunState.receiptRemovals)` (攻撃 +N は撃ごとに乗る = 全攻撃ヒット) |
| 相場師の算盤 | 1 ターン目の OnTurnStart: `itemBlockBonus += 3 × abacusStacks`・`firstHitAttackBonus += 2 × abacusStacks` (neon 収束: 2 / 1) (abacusStacks = 店のリロール 1 回ごとに +1・最大 10・ShopManager.TryReroll) |
| 溶金の領主剣 | stats: `dmgPct` 10 × ⌊所持ゴールド / 50⌋ (最大 8 = +80%・与ダメ % の池・攻撃が通る時) / `atk` 2 × ⌊swordShopSpent / 100⌋ |
| 経済 (与ダメに入らない) | 見送り換金 ×1.8 切り上げ (neon 収束: ×1.5・〈銭皿〉`CoinDish.Apply`・`AxisPool.SkipGoldFor`) / 勝利の報酬 ×1.10 切り上げ + 強敵 5 (〈戦果の天秤〉`SpoilsScale.Apply`・GameManager.Rewards の〈富〉の後) / 〈幸運の硬貨〉同じ目 2 個以上で +3 (1 戦闘 12 まで) / 〈爆破祭の小切手〉3 ターン以内の勝利 +6 / 〈黄金色の封蝋〉層 +18 / 〈合札〉2 品目以降 −10 (下限 1) / 〈黄金の通行手形〉店で 50G ごと (neon 収束: 80G) / 〈目利きの紹介状〉軸指定 −3 |
| BOT | 与ダメに入る分は ctx の口 (攻撃 +N・`itemBlockBonus`・池) なので配線の見積もりに自動で入る。 〈加重の拳套〉は `AutoRunner.ReadOutOneHit` で 2 撃目以降の池に +0.20。 〈銭皿〉を持つと一番の軸でない BRONZE を見送る (`BotHooks.WantsPassive`)。 乱数は引かない |
| 計装 | 見送り換金の回数 / 額 → ランの記録 `skipFired` / `skipGold` (`AxisPool.RunSkips` / `RunSkipGold`) |

個々の品の数値は `GoldEffects.cs` / `GeneralEffects.cs` の定数と XML コメントが正本。

## 4. 敵/汎用パッシブ一覧（`EnemyPassiveSkillEffects.cs`・視点は§0参照）

**2026-11-11 に enemies.json と 1 件ずつ突き合わせた（#30）。** Trigger の OnRollWin / OnRollLose は収支トリガー
（敵視点 OnRollWin = プレイヤーの収支マイナス・ターン解決の**後**に鳴る・§9.3）。「自ダイス +N」は敵の攻撃値へそのまま入る
（`Escalation.EnemyAttackValue` = 基礎攻撃 + 出目の合計）。印は 2026-11-11 #104 で消費側まで追い切った区分: **[不発・確定]** 効いていない / **[向きが逆・確定]** **[漏れ・確定]** 効き先が逆・プレイヤーにも乗る / **[積み上がり・確定]** 説明より積み上がる。
**同日 #104 で直した**（数値は変えていない。〈多頭攻撃〉だけは効果を置き換えた）。直した行は **[#104 修正]** と、相互攻撃モデルでの読み替えを書いてある。
読み替えの決まり: OnRollWin/Lose は収支トリガー（解決の後）のまま、「次ターン …」は次のターンに、固定ダメージはその場で入れる。
ProcessDamage の中の OnPreDealDamage は「その陣営が攻撃している計算」（敵は 1 体目の攻撃が通るときだけ・§1 ②）。
根拠は各クラスの XML コメント。**[未装備]** はどの敵も持っていない（登録だけ残っている）。

| SkillId(表示) | Trigger | 効果 |
|---|---|---|
| Trapper(罠師) | OnRollWin/OnTurnStart | **[#104 修正]** 収支マイナスの次のターン、**プレイヤーの攻撃 −1**（`nextTurnBuffs["enemy.trapper.atkDown"]` → 次ターンの OnTurnStart で `playerAttackPowerPenalty` += → atkBase から減算・下限 0・予告の playerAttackPenalty にも出る）。「相手のダイス合計値」をプレイヤーの攻撃と読む（プレイヤーの〈呪縛〉= 敵攻撃値 −N と同じ向き）。旧: 共有バフ `enemyDiceDebuff` で**この敵自身**の出目の合計を引いていた |
| Undying(不死者) | OnTurnStart | 自HP +1 |
| Sprint(疾駆) | OnPostRoll | 1T目 自ダイス +2（＝攻撃 +2） |
| BruteForce(剛力) | OnPreDealDamage | **[#104 修正]** この敵の攻撃が通る（ブロックの後に 1 以上残る）たび、ダメージ +2。敵の攻撃の ProcessDamage の中で鳴る（§1 ②）ので、+2 はプレイヤーの被ダメ軽減・シールドを通る。プレイヤーの攻撃には乗らない。旧: 敵の攻撃の前に鳴り条件が立たず、立っても finalDamage が上書きされていた |
| Flight(飛翔)/Immovable(不動) | OnPrePursuitDamage | **[#104 修正]** 役〈追撃〉（目 3）の追加ダメージを受けない。プレイヤーの攻撃の ProcessDamage の中で鳴り nullifyPursuitDamage=true、CombatManager が ApplyRoleExtraDamage を飛ばす。いまの「追撃」のダメージは役〈追撃〉だけ（`ctx.pursuitDamage` の書き手は無い）。旧: OnPrePursuitDamage が FireEnemyTrigger で一度も焚かれていなかった |
| HardScales(硬鱗) | OnBattleStart | enemyDefense +4（2026-10-28。旧: 被弾時 −2） |
| CrackingArmor(割れる装甲) | OnTurnEnd | `lastPlayerDamage > 0` なら `enemyDefense = max(0, enemyDefense − 4)`（2026-11-04。初期の防御は EnemyData.defense） |
| StackingArmor(積み上がる装甲) | OnTurnEnd | `enemyDefense = min(24, enemyDefense + 3)`（2026-11-04・既に 24 以上なら触らない） |
| TailStrike(尾撃) | OnRollLose | **[#104 修正]** プレイヤーの収支プラスのターン、その場で `EnemyDealUnmitigable(1)`（軽減無視・シールドは肩代わり・③-C）。収支トリガーの後に CombatManager が ctx の HP を拾う（§9.3）。旧: fixedDamageToEnemy に積み、読まれないまま次の BeginNewTurn で 0 に戻っていた |
| Rampage(暴走) | OnRollLose/OnPostRoll | **[#104 修正]** 収支プラス（プレイヤー）の次のターン、**この敵の攻撃 +3**（`nextTurnBuffs["enemy.rampage.atkUp"]` → 次ターンの敵の OnPostRoll で自ダイス +3 → 予告値に入る）。旧: 共有バフ `diceBonus` で**プレイヤーの** playerDiceTotal へ入っていた |
| Ethereal(虚体) | OnPreReceiveDamage | 主攻撃の finalDamage /2（`CombatManager.Damage.cs` の敵の軽減パッシブ。固定ダメ・出血・毒には効かない） |
| Curse(呪縛/敵) | OnRollWin/OnTurnStart | **[#104 修正]** 罠師と同じ形で、次のターンのプレイヤーの攻撃 −2（`enemy.curse.atkDown`）。旧: この敵自身の攻撃を下げていた |
| CounterStance(反撃態勢) | OnRollLose/OnPreDealDamage | **[#104 修正]** 収支プラス（プレイヤー）の次のターン、**この敵の攻撃のダメージ +3**（`nextTurnBuffs["enemy.counterStance.dmgUp"]` → 次ターンの敵の OnPreDealDamage で finalDamage += ・剛力と同じ位置）。旧: 共有バフ `damageBonus` で次ターンのプレイヤーの主攻撃と敵の攻撃 2 経路の全部に +3 |
| HoningDuel(研ぎ澄まし) | OnPreDealDamage | **[#104 修正]** この敵の攻撃が通るたび、ダメージ +currentTurn/3（剛力・闇技と同じ「勝利時の与ダメージ」の読み替え。トリガーを OnRollWin から OnPreDealDamage に替えた）。旧: 収支トリガーで currentBuffs["damageBonus"] に書き、次の BeginNewTurn の `currentBuffs.Clear()` で消えていた |
| EliteVigor(精鋭・汎用) | OnBattleStart/OnPostRoll | enemyDefense +2（2026-10-28。旧: 被ダメ%軽減 +0.10） / 自ダイス += currentTurn/3 |
| EliteDecree13(死の重圧) | OnPostRoll | 宣告ターン中 自ダイス +13 |
| EliteLizard(重甲) | OnBattleStart/OnRollLose | enemyDefense +4（硬鱗と合わせて 8・2026-10-28。旧: 被弾時 −2） / 収支プラス（プレイヤー）のターンに `EnemyDealUnmitigable(1)` の反射（**[#104 修正]** 尾撃と同じ） |
| EliteWraith(霊体) | OnTurnStart/OnPreReceiveDamage | 奇数T 攻撃ロールを 1 回多く引く(extraDice=1) / 偶数T(霊体) 主攻撃の被ダメ=1 |
| EliteGolem(巌の意志) | OnTurnEnd | 意志+1、撃破時 意志分を実プレイヤーへ確定ダメ |
| EliteMinotaur | OnPostRoll | 自ダイス +1 |
| EliteDarkKnight(闇技) | OnPreDealDamage | **[#104 修正]** 剛力と同じ（この敵の攻撃が通るたびダメージ +2） |
| MultiHead(多頭攻撃) | OnBattleStart | **[#104 置き換え]** 印 `MutualKeys.EnemyDoubleOnBlockHeavy` を立てる。プレイヤーのブロックの個数 > 攻撃の個数 のターン、CombatManager.Turn.cs が 2 回目の攻撃 (1 回目と同じ攻撃値・ブロックは 2 回とも全額効く) を〈連撃〉と同じパケットで入れる。2 体戦・〈連撃〉で既に 2 回目がある時は増やさない。挿し方で決まるので配線前の予告には載らない。旧: 「ロール勝利時、追撃ダイスを 1 個追加」(読み手が無く不発) |
| Regeneration(再生) | OnTurnStart | 自HP +1 |
| DemonAura(魔王の威圧) | OnBattleStart | enemyMaxHPReduction +3(相手最大HP−3) |
| Hellfire(地獄の業火) | OnRollWin | **[#104 修正]** プレイヤーの収支マイナスのターン、その場で `EnemyDealUnmitigable(2)`（尾撃と同じ） |
| Lifesteal(吸血) | OnPostDealDamage | **[#104 修正]** 敵の攻撃の末尾で、自HP += この攻撃で実際に通った主攻撃のダメージ（ブロック・シールド・軽減の後）/2（ReduceEnemyHeal を通す）。CombatManager が鳴らす前に ctx の HP と `ctx.finalDamage = takenDmg` を揃え、鳴らした後に HP を拾う。旧: ctx にだけ書いた回復が同じターンの `ctx.enemyCurrentHP = enemyHP` で上書きされていた |
| NightLord(夜の王) | OnTurnStart | T5以降 extraDice=1（攻撃ロールを 1 回多く引いて足す） |
| DeathSentence(死の宣告) | OnTurnStart | T>10（＝11ターン目）で fixedDamageToEnemy +999(即死) |
| IntimidatePlus(威圧+) | OnBattleStart | **[#104 修正]** この敵の攻撃 +3（戦闘を通して・ボスの威圧+は BossParam.IntimidateThreat）。足し先は `enemyDiceTotalBonus`（Escalation.EnemyAttackValue で基礎攻撃と同じく倍率が掛かる＝基礎攻撃 +N と同値・予告に出る）。「脅威」はいま基礎攻撃値の代わり（baseAttack 未指定時）としてしか使われないので、§9.6 の宿題どおり基礎攻撃値への加算と読んだ。旧: `ctx.enemyThreat` に足して読む処理が無かった（いまは enemyThreat へ書かない） |
| GreedyMerchant(貪欲商人) | OnBattleStart | no-op(スケーリングは GameManager.StartFalseMerchantCombat: HP +2/個・攻撃 +1/3個) |
| Storm(嵐・2026-11-13) | OnBattleStart | 航行の地図で**嵐の中の点に居る時に始まった戦闘**だけ、 1 体目の敵に付く（`CombatManager.StartCombatInternal` が付ける・ボスには付かない）。この敵の攻撃 +`FreeMapParams.StormEnemyAttack`（3・戦闘を通して）。足し先は威圧+ と同じ `enemyDiceTotalBonus`（基礎攻撃 +N と同値・予告に出る）。仕様は GAME.md §5-12 |
| Berserk(狂暴化・全部の戦闘の敵に付与・2026-11-21〜) | OnTurnStart | T>50: healBlocked, enemyDamageTakenMultiplier=3, enemyAttackIgnoresGuard=true, enemyDiceTotalBonus を `Berserk.AttackBonusAt(T)` = 10 + 10×⌊(T−51)/5⌋ まで積む (差分だけ足す)。 enemyAttackIgnoresGuard の間、 敵の主攻撃は ApplyPlayerDefense を通さず (予告もその値)・blockSum=0・ProcessDamage 以降の鎖 (シールド・被ダメの軽減・役の守り・仕込み刃・古い手紙・〈不動〉・〈無害灯〉) に 0 を流し、 HP を減らす直前に攻撃値をそのまま入れる。 2 体目・〈連撃〉の 2 回目は今までどおり |

（FlawlessRobe 天衣無縫は 2026-10-17 にクラスごと削除。）

---

## 5. ボス専用パッシブ一覧（`EnemyPassiveSkillEffects.cs` / ヴェスカは `VescaEffects.cs`）

機構の数値（`BossParam.*`）の基準値の正本は `BossParams.Spec`。

| SkillId(表示) | Trigger | 効果 |
|---|---|---|
| GoblinKingsCall(号令) | OnPostRoll | 自ダイス + GoblinCall（基準 3） |
| MiasmaCorrosion(毒の侵蝕) | OnTurnEnd | 毒stack+1(max PoisonStackCap=5)、stack分を実プレイヤーへ軽減無視ダメ。ダウン中は鳴らず、ダウンに入った時に stack −2（2026-11-20・ボスのダウンの節） |
| MirrorTwinsResponse(鏡映の応答) | OnPostReceiveDamage/OnTurnStart | 閾値 `Thr = max(12, ボス最大HP×4%)`。与ダメ<Thr で reflect=min(MirrorReflectCap=9, Thr−ダメ)を蓄積、次T開始で軽減無視の反射 |
| JudgmentFlames(審判の炎) | OnTurnEnd | `dmg=min(ChipCap, 1+ceil(currentTurn/3)+罪)`、実プレイヤーへ軽減無視（罪=totalBattles/8,max2 / ChipCap 基準 10・`BossParams` の基準値）。ターン係数は 2026-07-28 に 1→1/2、2026-08-15 に 1/2→1/3 へ緩和（15T 累積 108→90・上限は T≥19 まで届かない）。**シールドは肩代わりする**（③-C） |
| JudgmentBlaze / AshArmor / AshCarapace | — | 旧 6 層ボスの機構。**[未装備]**（6 層は下の 5 つで組み直した・数値はクラスの XML コメント）。同じ旧機構の RoyalEmber / SinChain / EternalBurning / ReturnToAshes / ImmortalEmber / EmberAura / StarfireProliferation / ScorchedEarth は 2026-11-12 に削除（GAME.md §24） |
| EmberOmen(業火・旧 業火の予兆) | OnTurnStart | **2026-11-14〜 王の大技〈業火〉の周期**（`EnemyData.heavyCycle = ember`・`Escalation.TurnsToHeavy` が `EmberOmenTiming` を読む）: 業火のターン = 旧予兆ターン（HP>50% は T1/5/9…・HP≤50% は T1/4/7…・HP の割合はターンの頭の `Escalation.HeavyHpRatio`）。1 ターン前はチャージ（攻撃しない）。業火 = 攻撃 × heavyMul（2.0）+ 烈炎 × `BlazeBurstPerStack`（基準 5・軽減無視・シールドは肩代わり・撃ったら烈炎 0・`CombatManager.ApplyBlazeBurst`）。小技 × heavyLightMul（1.0）。このパッシブ自身は攻撃 +N を積まない。旧い形（口 `ashKingHeavy=False`）: 予兆ターン = HP>50% は T1/5/9…（周期4）、HP≤50% は T1/4/7…（周期3）。予兆Tだけ攻撃 +JudgmentDice（基準 12・2026-11-14 に 8 から）。**[#104 修正]** 焦土契約と同じ差分更新（自分の寄与 `ember_omen_applied` を毎T頭に「予兆なら JudgmentDice、それ以外 0」へ合わせて `enemyDiceTotalBonus` を足し引き）。旧: `+=` を戻す処理が無く、予兆のたびに積み上がっていた |
| AshRegrowth(灰の再生) | OnTurnStart | HP>50% かつ業火でもチャージでもないT（2026-11-14〜・作者の決定: 業火のターンとその 1 ターン前のチャージのターンは回復なし。旧い形 = 口 `ashKingHeavy=False` は予兆Tだけ回復なし）: 最大HP × RegrowthRate（基準 0.02 = 2%・`BossParams`）回復（切り捨て・最低1）。2026-11-14 に直書きの 0.02f を `BossParam.RegrowthRate` へ移し（式は同じ）、同日 0.03 へ・2026-11-18 に 0.02 へ戻した（案 B・GAME.md §13-1） |
| EmberFury(憤怒の残滓) | OnTurnStart | T頭の HP≤50% のT: 攻撃 +FuryAttack（基準 3・2026-11-14 に定数 `EmberFury.Bonus` = 3 から `BossParam.FuryAttack` へ移し、同日 5 へ・2026-11-18 に 3 へ戻した（案 B・GAME.md §13-1））（それ以外 0）。**[#104 修正]** 業火の予兆と同じ差分更新（`ember_fury_applied`）。旧: 毎ターン `+=` で積み上がっていた |
| ScorchedPact(焦土契約) | OnRollWin/OnTurnStart | 収支負けでスタック+1（上限5）。毎T 差分更新で `enemyDiceTotalBonus` に現在スタック×1 を反映（累積しない） |
| BlazeBrand(烈炎) | OnTurnEnd/OnTurnStart | **2026-11-14〜 (作者の決定)**: T終了時、 そのTに主攻撃をシールドが肩代わりした量 (`ctx.shieldAbsorbedAttackThisTurn`・③-B) ÷ `BlazePerShield` (基準 10・`BossParams`) の切り捨てだけ烈炎+ (端数は持ち越さない・式は `HeavyCycle.BlazeGain`)。軽減無視の肩代わり (③-C・業火の烈炎の分を含む) は数えない。旧い条件 (口 `blazeStackRule=Guard2`): ブロック端子へ 2 本以上のT終了時に烈炎+1。2026-11-14〜: T開始時の削りは無し（積もった烈炎は〈業火〉で × BlazeBurstPerStack にまとめて返り、撃ったら 0。業火がダウン・〈封じ・揃〉で撃たれなければ持ち越す）。口 `blazeTickEachTurn=True` で旧い削り（T開始時に烈炎の数だけ軽減無視ダメ）も残す |
| Decree13th(13番目の宣告) | OnTurnStart/OnTurnEnd | 1/13でフラグ、本体が生存していれば成就で ceil((playerDiceTotal+enemyDiceTotal)×2.0) を軽減無視で実プレイヤーへ（2.0 は定数。プレイヤーの criticalMultiplier は 2026-08-08 から読まない・シールドは肩代わり） |
| SaintGeorgesPhases(シュヴァリエ) | 多数 | 開幕プリオリテ1。形態1: シールド = 140−28×プリオリテ（最低28）が主攻撃を吸う・収支プラスのTに (プレイヤーの出目の合計+プリオリテ×7) の不可避反撃＋シールド−25 / 形態2: 連勝報酬（次T ボスの攻撃+4、そのTも収支プラスならボスへ15）・ボスの収支 3 勝で形態1帰還 / 帰還ごとにプリオリテ+1。**[#104 修正]** 形態1の 0d0（`extraDice=−4`）: `EnemyData.RollAttack` が引く回数を max(0, 1 + extra) にしたので、形態1は攻撃ロールを引かない（**基礎攻撃 × 大技・段階の倍率は残る**・「攻撃しない」とは読んでいない）。反撃のHP減と形態2の「ボスへ15」: 収支トリガーの後に CombatManager が ctx の HP を拾う（§9.3）ので残る。旧: 引く回数が max(1, …) で負の値が効かず、HP の増減は FinishTurnCommon の書き戻しで消えていた |
| RelicScholar(遺物学者) | OnTurnStart/OnTurnEnd | 毎T 遺物を 1 枚抽選（段の切り替え直後は新等級から・天与の才の次Tは 2 枚）。`enemyDiceTotalBonus` = 遺物 + 溜め打ち を `=` で一括代入（停滞の加算は 2026-11-12 #103 で無くなった）。OnTurnEnd で大出血の tick。抽選結果は `MutualTurnTelegraph.drawnRelics` に載るが画面は未配線 |
| StagnantTime(停滞する時間) | —（トリガーなし） | **2026-11-12 #103 作り直し。** 端子を引くたび 1 枚ごとに確率 p で止める（`TerminalDeckState.DrawTurn`・乱数は専用キー `vesca.stagnant`）。p = `BossParam.Stagnation` %（基準 p1 10 / p2 15 / p3 20 / p4 25・範囲 0〜50）。止まった端子はそのターン挿せず（`Resolve` の 0. で外す）、捨て札に行かず次のターンも手札に残って解ける（もう一度は止まらない・その分 新しく引く枚数が減る）。止まった常設はそのターンだけ挿せない。**敵の攻撃・ダメージの式には何も足さない**。旧: 攻撃 < ブロックのTに停滞+1（上限10）・1 スタックあたり敵攻撃 +1 |
| AnalyticComputation(解析演算) | OnRollLose | 収支プラスごとに回避率 +2%（上限40%）。回避はその攻撃の通常・固定ダメを丸ごと消し、次Tは回避しない（`ApplyVescaDefense`） |
| RiftHeldOpen(裂け目) | OnTurnStart | ヴェスカ 最大HPの2% / プレイヤー 最大HPの1%（軽減無視・シールドは肩代わり） |
| RecurrentTruth(回帰性真理) | OnBattleStart/OnTurnStart | 出血を毎T半減・毒を毎T 3/4 (切り捨て・`CombatContext.RecurrentTruthPoisonKeepPct`・2026-11-22 neon 収束: 旧 毒も半減) / 受ける会心倍率 −0.5（下限1.0） / 臨界メーター −10/T (2026-11-22 段 3: 旧 −5) / 最大HP変更と処刑を拒否 |
| ContinuousExperiment(連続実験) | OnTurnEnd | HP0 で次段へ差し替えを予約（p4 は持たない） |
| DivineTalent(天与の才) | OnTurnEnd | 最大HPの 66%/33% を跨ぐ一撃を閾値で止め（`ClampToThreshold`）、初めて割った次Tに遺物 2 枚 |

### （欠番）覚者7形態連戦

2026-07-27 に廃止、`AwakenedP1..P7` / `FlawlessRobe` のクラスも 2026-10-17 までに削除（GAME.md §24）。
連戦の差し替えと死因（大出血・裂け目）はヴェスカが流用している（上の表）。

### 〈門〉の不完全起動（`GameLoop.GateFlaws` / GAME.md §4-3）

**2026-09-14: 旧 SinAltar 由来の敵パッシブ 3 種（boss6_golgotha / boss6_severed_time /
boss6_ashen）は撤去した。** 罰は敵強化ではなく**プレイヤー側の規則**になったので、
パッシブ表には載らない。効くのは門をくぐった後の戦闘＝ 8 層ヴェスカ戦のみ。

| 欠陥 | 掛かる位置 | 効果 |
|---|---|---|
| 〈不完全な修復〉 | `ExecuteTurnMutual` ターン終了（充電の獲得より後・次Tの消費より前） | `ConsumeHalfCharge()`（切り捨て）。**支出には計上しない** ── `ConsumeCharge` を使うと〈短絡〉と充電経済の計装が汚れる |
| 〈不完全な起動〉 | `atkBase` 確定の直後（`GateFlaws.ApplyIgnitionCut`） | 1 撃ごとに `hitBases[h] -= floor(hitBases[h] × IgnitionAttackCutPct)`（既定 0.12・ADR-0016）。旧「1 端子 2 本まで」（`SanitizeWiring`）は 2026-09-14 に撤去・`SanitizeWiring` 自体も 2026-11-11 に削除 |
| 〈不完全な転移〉 | `blockSum` 確定後・〈貫きの錐〉の**直後** | `blockSum -= floor(blockSum × 0.50)`。錐（敵攻撃値に比例）と違い**ブロック量に比例**するので、厚く配線した分は必ず一定割合残る。両方掛かるときは錐で削られた残りへ乗る |

> 〈不完全な転移〉の 0.50 は**未計測の仮置き**。8 層ボス戦のブロック／被ダメ分布を採ってから決めること。

---

## 6. （欠番）旧 Λ層の恒久デバフ

2026-10-28 に Λ層ごと削除（`LambdaDebuffEffects`・`run.lambdaDebuffs`・`ctx.lambda*` はコードに無い）。
与ダメの段 K は計測点だけが残り、値は段 J と同じ。7 種の内容と作り直すときの注意は
[GAME.md §23-23](../../../docs/GAME.md) に保留している。

---

## 9. ADR-0009 相互攻撃パイプライン（v1・W7 進行中）

**本節が現行の正本**。`ExecuteTurn()` は `ExecuteTurnMutual()` を呼ぶだけ（旧ロール勝負の経路は 2026-10-29 に削除）。
終端処理は `FinishTurnCommon()`。

### 9.1 ターン構造（ADR-0009 柱2）

```
0. ターン開始       : BeginTurn / DoT / OnTurnStart / SyncHP / フロア敵回復
   端子の手札         : DrawTerminalHand (ADR-0014) ── 前の手札を捨てて HandSize(5) + 〈先読み〉の分を引く (振り直しの判断より前)
                        ヴェスカ〈停滞する時間〉: 前のターンに止まった端子は捨てずに手札に残し (解ける)、 新しく引いた 1 枚ごとに確率 p で止める
1. 敵ロール         : スタンスなし → 敵ダイスを振る
3. 自ロール         : 自ダイスを振る（旧法と同一の RollDice）
4. リロール（任意） : MaybeRerollPlayerDice（配線前のみ、目的関数は暫定=旧勝率のまま）
   時限効果 OnRoll / PassiveItemManager.OnRoll / メタバフ +1
   ProcessPostRoll（パターン/ゾロ目・合計確定） + OnPostRoll
2. 予告             : Escalation.EnemyAttackValue(currentEnemy, turn, ctx.enemyDiceTotal)
                      → プレイヤーの防御 (ADR-0013): DefenseFormula.Apply(enemyAtk, max(0, playerDefense − max(0, enemyArmorPen − enemyArmorPenNegate)))
                      予告に出る値はここまで (ブロックの前)。 2 体目は secondaryEnemy.armorPen で同じく掛ける
5. 配線             : 出目ごとに挿す場の端子 (常設 + 手札) を決める (場の添字・−1 = 挿さない)
                      人 = 画面 (_pause.slots) / BOT = WiringPolicy(...).slots / どちらも無ければ TerminalWiring.Auto (素朴な点の最大)
                      → TerminalCards.Resolve(board, slots, dice, TerminalEnvNow, deck.state, targets) → TerminalOutcome
                      → ApplyTerminalOutcome (下記 9.2「配線」)
   **方策/UI を信用しない**: 規則に合わない挿し方 (挿せる数の超過・条件に合わない出目・払えない充電) は Resolve が「挿さなかった」に直す。
   端子を挿さない出目は捨てる (旧: 攻撃へ自動合流)。 旧い配線 (前ターンの配線の自動適用) と SanitizeWiring は 2026-11-11 に削除
6. 解決             : 下記 9.2（自先制・撃破時は敵攻撃なし）
   収支トリガー     : balance = 与ダメ − 被ダメ → OnRollWin/Lose/Draw を再発火
7. ターン終了       : FinishTurnCommon（旧法と共用・順序不変）
```

### 9.2 ダメージ計算（`ExecuteTurnMutual`）

```
リロール (ADR-0010 / ADR-0011・配線より**前**)
         RerollPhase: 充電を払って**ダイスを全部**振り直す。 コスト = 個数 × この戦闘で何回目か (+1 = 時限デバフ〈無力感〉・無料の時は 0)
         (回目はターンでは戻らず戦闘の中で上がる)。 振るかどうかは RerollPolicy が決める (null なら振り直さない)。
         予告は素の敵ダイス合計から仮組みして渡すので、 **完全情報のまま**判断できる (ADR-0009 柱3)。
         〈特別・対〉が前ターンに成立していれば次の N 回 (既定 2) が無料 (currentBuffs["roleFreeReroll"] = 回数)。
         無料の回数と費用は CombatManager.FreeRerollsLeft / RerollCostNow(ctx, 個数, 回目) の 1 か所 (〈擦り切れた賽袋〉〈賽振り台〉・上の ⓪)。
         〈万象のゾロ目〉(2026-11-21): 出目が決まった所 (OnPostRoll の後・配線の前・CombatManager.DecideOmniPair) で、 立つ役が 2 個そろいなら
           確率 OmniPairChance(この戦闘の役の回数) = 3 回以上 50% (GameRng "role.omni")・5 回以上 100% (乱数を引かない) でこのターンそれをゾロ目として扱う
           (TerminalEnv.pairAsTriple・ResolveRoles の TerminalCards.UpgradePair)。

配線 (端子の山札・ADR-0014・2026-11-11〜 これだけ)
         式の正本は TerminalCards.Resolve (盤面を書き換えない純粋な計算・BOT も同じ関数を写しの状態で呼ぶ)。
         強化した端子 (名前の後ろに「＋」・2026-11-20) は基の端子と同じ枝を通り、 量だけ *Up の定数に替わる (TerminalCards.Effect の up)。
           式が素の端子と違う所: 〈号令の剣＋〉は 1. で挿した出目も +1 / 〈剛腕＋〉は挿した出目も攻撃に足す / 〈処刑の刃＋〉は敵の HP が半分以下 (解く時の HP) なら攻撃 +4 /
           〈諸刃の剣＋〉は撃破した時に払った HP が戻る (回復ではない・TerminalOutcome.killRefundHp) / 〈応急箱＋〉は回復する前の空きを超えた分をシールドへ (overhealToShield) /
           〈大導電＋〉は満たす前にこの戦闘の充電の上限 +5 (ctx.chargeCapBonus・chargeCapAdd)
         数値は TerminalCards の定数、 端子の一覧は TerminalCards.Build()。 ここには「どの値がどこへ入るか」だけを書く。
         Resolve の順:
           0. 止まった端子 (ヴェスカ〈停滞する時間〉・state.stopped) を挿した出目と、 挿せる数 (capacity・既定 1) を超えた出目を外す
           1. 〈号令の剣〉を挿した出目があれば、 他の出目すべて +1 (以降の条件と量はこの値で見る)
           2. 条件 (3 以下 / 5 以上 / 6 以上 / 偶数 / 奇数 / 同じ目が他の端子に / 同じ目 2 個)。 合わない出目は外す。
              〈無形の構え〉の次のターンは条件を無視 (同じ目 2 個の端子も 2 個そろっていれば通る)
           3. 充電の支払い (場の順)。 足りなければその端子は不発。 〈裁きの雷〉は充電 1 以上で通り、 効果の所で全部払う (使い捨て・2026-11-19〜)
           4. 〈火葬の剣〉(2026-11-14)・〈影写し〉(2026-11-20〜): 対象の候補 (下の「対象」) が無ければ不発 (挿した出目を外す・使い捨ても消費しない)
              〈一刀両断〉(2026-11-20): 挿した攻撃の種類の端子が 2 枚以上なら不発 (同じ扱い)
              発動の条件 (2026-11-22・TerminalCards.SituationOk・出目の条件とは別・〈無形の構え〉でも無視しない): 合わなければ不発 (同じ扱い)。
                〈応急箱〉TerminalEnv.hpLostBattle (この戦闘で HP を 1 以上失った = 開始時 HP − 今の HP + 回復の累計 > 0) /
                〈身代わり札〉TerminalEnv.enemyAttack > 0 (予告のあるターン) / 〈焼却斬り〉state.disposedTotal ≥ 1 (前のターンまで)
              使い捨ての出目の条件 (2026-11-22): 〈応急箱〉〈身代わり札〉3 以下・〈火薬樽〉〈緊急再装填〉5 以上・〈貫通弾〉奇数
           5. 効果 (場の順)。 〈残響〉が発動していれば他の端子の効果を 2 回走らせる (撃破のボーナス・段階を戻す数も 2 回分)。
              払う物は 1 回だけ (2026-11-20〜): 充電は 3. で 1 回・〈諸刃の剣〉の HP = ceil(sum/2) は 5. の頭で 1 回 (払うと HP が 0 になるなら不発・旧: HP−1 までに切って攻撃は全部入った)・
              〈裁きの雷〉は今の充電をすべて 1 回払い、 2 回目も払った量で攻撃に足す (旧: 2 回目は充電 0)
              〈打ち直し〉(2026-11-20〜): 溜まりが届いたら、 場の順で後ろの (まだ効果を出していない) 端子の出目 1 個ずつ +1 (sum に入る量だけ・条件は変えない)。
              届くのは 1 ターンに FoundryMaxPerTurn (=1) 回まで (2026-11-21〜・何枚・〈残響〉でも合わせて・溜まりの余りは持ち越す・foundryFiredTurn は BeginTurn で 0)
              〈剛腕〉(2026-11-20〜): **攻撃の種類の端子**に挿した出目の合計を、 その端子の効果に入れる前に ×2 (〈剛腕〉の効果 1 回につき ×2 ── 2 枚・〈残響〉で ×4)。
              条件 (2.) は 2 倍にする前の目で見る。 〈反復突き〉の「同じ目」も 2 倍にする前の目。 〈剛腕〉自身に挿した出目は使わない
              〈歯車の軸〉(2026-11-20・挿せない常設 capacity 0): 場にあれば (止まっていない)、 場の順で最初に発動した基本以外の端子の sum += (2 または強化 3) × 挿した出目の数 (何枚でも足す・
              〈諸刃の剣〉の HP 不足で不発なら次の端子へ)。 条件 (2.) は足す前の目で見る
              端子へ入る量 (sum) = 挿した出目の合計。 ただし**防御の種類の端子**に挿した出目は 1 つずつ max(0, 出目 − TerminalEnv.blockDieCut)
              (時限デバフ〈罪悪感〉で 2・2026-11-16・〈号令の剣〉の +1 の後)。 条件 (2.) は引く前の目で見る。 攻撃・充電・補助の端子の出目は変えない
           5b. 〈緊急再装填〉(2026-11-20): 発動した〈緊急再装填〉1 枚ごとに、 このターンに使った他の使い捨てを 1 枚 (場の右から・自分は除く) reloadTargets へ
              (FinishTurn の DisposeFired が次のターンの手札 returnNext へ。 〈残響〉で 2 回働いても 1 枚)
           使い捨ての効果 (5.・2026-11-22): 〈火薬樽〉攻撃 += sum × 3 (強化 4) / 〈応急箱〉heal += sum (強化 sum + 2・溢れはシールド)
           軸の端子の効果 (5.・2026-11-22〜 偏重 TerminalEnv.ax* は削除・戦闘の状態を読む):
              〈一閃〉critMultAdd += 0.08 (強化 0.10) × critsBattle (この戦闘の会心の回数・Crit.TotalKey) / 〈弱点看破〉forceCrit・critMultAdd += 0.08 (強化 0.10) × critsBattle /
              〈鉤爪〉bleedOnHit += max(2 (強化 3), 敵の出血 ÷ 4) / 〈毒霧〉poison += sum + (敵に毒があれば 5・無ければ 3) (+強化 2) /
              〈火種〉heat += sum + (前のターンかこのターンに爆発 = Kw.BurstTurnKey なら 10・でなければ 5) (+強化 3) /
              〈招雷〉chargeAdd += sum + (env.charge ≥ ChargeCore.OverchargeAt なら 6・でなければ 3) (+強化 2) /
              〈裁きの雷〉攻撃 += floor(払った充電 × (1 + 0.25 × このターンにこれより前に充電を使った回数 (env.chargeSpendsTurn + 先に払った端子))) (強化は + sum) /
              〈装甲板〉ブロック += sum・shield += (前のターンに【ジャスト防御】なら 8・でなければ 4) (+強化 2) /
              〈重装突撃〉push = floor(今のシールド × 0.50 (強化 0.75))・攻撃 += sum + push・shieldSpend += push
           采配と共通の端子の効果 (5.): 〈焼却斬り〉攻撃 += sum × 2 (強化 3) + disposedTotal (このターンより前) × 2 (強化 3) (2026-11-22〜・旧 max(2, 采配)) /
              〈一刀両断〉攻撃 += sum × 3 (強化 4)・ignoreDefense (攻撃の端子は 1 枚だけなので、 この撃だけ) / 〈巡りの鐘〉bells +1 (切り直すたび extraDrawNext += bells) /
              〈見切りの構え〉ブロック += sum × (大技の予告のターン TerminalEnv.heavyTurn なら 2) / 〈崩し斬り〉攻撃 += sum (強化 +1)
           6. 他の端子を見る効果: 〈連係斬り〉攻撃 + RelayPerFired × 基本以外の発動数 (自分も数える・2026-11-20〜 旧: −1)。
              (旧 〈剛腕〉: 攻撃 + 攻撃の種類の端子を挿した出目の合計 ── 2026-11-20 に 5. の ×2 へ)
              溜めた攻撃 (〈力溜め〉〈後の先〉) は、 攻撃の種類の端子を 1 枚以上挿したターンに attackAdd へ足して 0 に戻す
              〈後の先〉の溜める量 (2026-11-20〜) = ブロックで防いだ量。 Resolve は見込み min(後の先のブロック, TerminalEnv.enemyAttack) を storedAttackNext に入れ (parryStore)、
              本体が敵の攻撃の後 (CombatManager.SettleParry) に Σ_攻撃 1 回ごと min(後の先のブロック, min(敵の攻撃, blockSum)) へ直す (攻撃しなかったターンは 0)。 BOT は見込みで値付けする
           山札の軸の品 (2026-11-21・full-item-rework-ledger §⑦・旗は TerminalBattleState.deckItems = 戦闘の始めの所持品・BOT の写しも同じ旗・式は §3-O):
              5. の端子 1 枚ごと (〈残響〉で効果が 2 回走っても 1 回): 〈型抜きの金型〉条件のある端子の攻撃・ブロック +4 (攻撃はその端子の撃に・補助はブロック +6) / 使い捨てなら disposedNow +1
              6. の後: disposedTotal += disposedNow (この戦闘で使い捨てを使った回数・6c はこのターンの分を足した後の回数を読む)
              6c. ApplyDeckItems (§3-O の表)。 撃ごとの攻撃 +N は attackHits の 1 つずつに足し、 attackAdd にも 撃の数 × N を足す (攻撃しないターンは入らない)
           山札の出し入れ: §3-O (BeforeShuffle / DisposeFired・本体と BOT の SampleNextBoard が同じ関数)
           立つ役 (2026-11-21・段 1-f): 5. の頭で o.roleNow = TerminalCards.RoleOf(振った目, env) (0 = 立たない / 1 = 2 個そろい / 2 = ゾロ目か連なり)。
              判定は ResolveRoles と同じ (YachtRoles.Evaluate・〈凶運〉で封じた役は立たない env.sealedRoleMask・〈万象のゾロ目〉の格上げ env.pairAsTriple)。
              BOT の先読みの次のターンは引いた出目で決まる (格上げは見込まない)。
              〈役落とし〉: 出目 + (役が立てば この戦闘の役の回数 (このターンを含む) × 3 (強化 × 4)・ゾロ目か連なりなら 2 倍) を攻撃に / 〈賽振り台〉: 充電 +出目・rerollDiscountNext += min(6 (強化 8), 役の回数)
           5c. 充電のコアアクション (2026-11-21・ChargeCore・旗は TerminalBattleState.chargeItems・Resolve の引数 reboot / overclock = 人の画面 / BOT の方策):
              控え = 5. で各端子に入れた sum / raw / num と、 その端子が足した攻撃 atkD・ブロック blkD。 支払いは Pay (o.chargePays に 1 回ずつ・合計 chargeSpend)
              【再起動】〈二股の導線〉: reboot ≠ RebootNone・このターン発動した攻撃か防御の端子 (〈裁きの雷〉〈残響〉は除く)・充電 ≥ CostNow(5) → 払って、 その端子の効果を控えの量で 1 回走らせる
                (〈残響〉の倍化なし・ダイスは足さない・攻撃の端子なら attackHits に撃が 1 つ増える (attackHitDice は 0))。 おまかせ (RebootAuto) の対象 = max(ChargeCore.AutoValue = atkD + min(blkD, max(0, 敵攻撃 − blockAdd)))
              〈逆さ避雷針〉: 敵攻撃 > 0 かつ 充電 ≥ CostNow(5) → 払って takenFlatCut += 6・blkD 最大の防御の端子を再起動 (1 ターン 1 回)
              【オーバークロック】〈雷鳴の太鼓〉: overclock かつ 攻撃の撃がある かつ (ここまで払った後の) 充電 ≥ 15 → CostNow(12) を払い overclock = true (掛けるのは本体の撃の輪・下の自攻撃)
              〈灼線鞭〉: env.chargeSpendsTurn (配線より前に充電を使った回数・振り直し) + この解決の支払いの回数 ≥ 2 → atkD 最大の攻撃の端子を無料で再起動 (1 ターン 1 回)
           5d. 毒の品 (2026-11-21・PoisonCore.ApplyToTerminals・旗は TerminalBattleState.poisonItems・5c の後 = 再起動の撃も数える):
              〈緑染みの小刀〉poison += 3 × min(3, 攻撃の撃の数) / 〈蛇血瓶〉4 以上の目を挿した攻撃の端子 1 枚につき poison += 4 (最大 3 枚・振った目そのもので見る) /
              〈毒見役の小匙〉攻撃と防御の両方に挿したら poison += 3・heal += 1 / 〈毒棘の小盾〉防御に挿したら blockAdd += 6 (2026-11-22 旧 4) /
              〈瘴気の外套〉env.enemyPoison ≥ 15 なら takenMul ×= 0.65 / 〈麻痺の吹き矢〉env.enemyPoison ≥ 12 なら ignoreDefense (毒 2 ごとに防御 1 無視・Phase 4 較正: 旧 3 ごと・18)
           6d. 役の軸の品 (旗は TerminalBattleState.roleItems・役が立ったターンだけ・攻撃 +N は 6c と同じく撃 1 つずつ・式は §3-P)
         〈影写し〉〈火葬の剣〉〈保持器〉の対象: Resolve の wantTarget (人 = 画面で選んだ端子 / BOT = WiringPlan.targets)。
           候補 = 自分でない・常設でない・止まっていない・挿し方を直した後に出目が 1 個も無い手札の端子 (TerminalCards.IsCandidate)。 選ばない・候補でない時は左端の候補
         結果 (TerminalOutcome) の行き先:
           attackAdd / blockAdd / chargeAdd → attackSum / blockSum / chargeSum (出目をそのまま足さない ── 端子ごとに入る量が違う)
           attackDice / blockDice / chargeDice → 攻撃・ブロック・充電の「個数」(既存の品・敵〈多頭攻撃〉・攻撃しない判定が読む)
           supportDice → MutualKeys.SpecialDiceCount (補助の種類の端子を挿した個数)
           kindOfDie   → mutualWiring (出目ごとの種類・DiceTerminal。 補助は Special・挿さないは None)
         ApplyTerminalOutcome (配線の時点・堅牢の環/傲慢/端子調律より前) が戦闘へ配る:
           damageMul      → _termDamageMul (主攻撃の別枠の掛け算・下の自攻撃を参照)。 毎ターン 1 に戻す
           chargePays     → 1 回ずつ ctx.ConsumeCharge(min(払う量, 今の充電)) (「充電を使うたび」の品 = OnChargeSpend が回数と量を数える)     chargeFill → ctx.AddCharge(ChargeCap)
           takenFlatCut   → ctx.playerFlatDamageReduction += 量 (〈逆さ避雷針〉)
           heal           → HealPlayer        selfDamage → playerHP = max(1, playerHP − 量)  (〈諸刃の剣〉は払えなければ Resolve が不発にする)
           shieldSpend    → ctx.consShield = max(0, consShield − 量) (〈重装突撃〉が攻撃に足した分・shield より先に引く)
           shield         → ctx.consShield += 量 (shieldGainedTotal にも足す)
           poison         → Rw.Poison(ctx, 量)       heat → Kw.EnableHeat + Kw.AddHeat(ctx, 量)
           retreatStage   → AddEscalationDelay(TurnsToRetreatOneStage) を回数分 (段階を戻す数・〈氷結瓶〉1 回ごと・〈鎮静香〉は溜まりが CoolantNeed に届いた回数・2026-11-20〜 旧: 真偽で 1 段。 遅延のキーは CoolantDelayKey・役〈時間〉と共有)
           enemyStunNext  → nextTurnBuffs[EnemyStunKey] (次のターン敵は行動しない)
           ignoreDefense  → ctx.playerArmorPen += DefenseFormula.IgnoreAll
           forceCrit      → ctx.forceCritical = true       critMultAdd → ctx.criticalMultiplier += 量 (毎ターン組み直されるのでこのターンだけ)
           outgoingPctAdd → ctx.AddOutgoingPct(量) (他の与ダメ% と同じ池に足す)
           enemyAttackMul → enemyAtkValue = max(0, round(enemyAtkValue × 率)) ── 予告値 (プレイヤーの防御の後) に掛ける。
                            2 体目の攻撃・〈連撃〉の 2 回目 (_secondaryAtkThisTurn) にも同じ率
           takenMul       → 役〈守り〉の口へ合成: damageTakenCutPct = round(100 − (100 − cut) × takenMul)
                            → ApplyRoleGuard で takenDmg = round(takenDmg × (1 − cut/100)) (敵の直接攻撃パケットだけ・固定ダメの枠は触らない)
           bleedOnHit     → 主攻撃の dealtDmg > 0 のとき ctx.AddEnemyBleed(量) (〈鉤爪〉)
         ターンの頭 (DrawTerminalHand): 〈打ち直し〉の出目 +N を accumulatedValues[termFoundryBonus] へ置き、 自ロールの後 (大罪:怠惰の後) に全部の出目へ足す。
         N は 1 ターンに最大 +1 しか増えない (2026-11-21〜・FoundryMaxPerTurn) ので、 戦闘の T ターン目で N ≤ T (int のあふれは起きない)。
           〈柔軟な戦略〉をこのターンに引いて場にあれば ctx.AddOutgoingPct(CounterPct × floor(基本以外の発動の累計 ÷ CounterEvery))
           (2026-11-20〜。 旧: ランの山札に入っていれば引かなくても。 累計は引いていない間も数える)
         ターンの終わり (FinishTerminalTurn): 発動した端子の撃破のボーナス (量は TerminalOutcome.killMaxHp / killWhet / killGold / killChoices・〈残響〉の 2 回目も足す)
           (〈魂狩り〉最大 HP + / 〈処刑の刃〉RunState.terminalWhetBonus + / 〈賞金狩り〉ゴールド + / 〈剥ぎ取り〉次の報酬の選択肢 +1)。
           撃破の経路は問わない (#117・2026-11-13)。 ここで敵 HP ≤ 0 なら配り、 まだ倒れていなければ控えておき、
           FinishTurnCommon の 2 か所 (OnTurnEnd の後・SwapEnemy の前 / SyncHPFromContext の後・TryAdvanceVescaChain の前) で敵 HP ≤ 0 なら配る
           (〈絞り出し〉〈百断〉などターン終了時の撃破)。 2 体戦の 2 体目だけが倒れたターンは数えない (戦闘の撃破 = 1 体目)。
           旧: 敵 HP ≤ 0 かつ攻撃の種類の端子を 1 枚以上挿したターンの、 ここまでの撃破だけ (測定の口 GameHooks.TerminalKillBonusNeedsAttack)。
           その後、 使い捨て・〈影写し〉(写しは次のターンの手札へ・引く枚数とは別・2026-11-14)・〈火葬の剣〉・〈保持器〉を山札へ反映

役 (ADR-0011 → ADR-0015・配線集計の直後 / 攻防の解決より**前**)
         ResolveRoles: 振った 3 つの目から **目 × {2 個そろい, ゾロ目}** の 18 種と、 連なりの賽を持つ時だけ **連なり (3 つの連番)** を判定する。
         端子の置き方は条件にしない。 **成立すれば自動で効く** (1 戦闘 1 回・任意発動・使用済み管理は無い)。
         **Tier は 2026-11-18 にやめた** (ADR-0015): 賽がその目の面を持っていれば 2 個そろいもゾロ目も効く。 小さい目ほど役が強い (作者 p10)。
         効果は既存フィールドへ加算する形に寄せてある (正本は YachtRoleEffects.ApplyCore・数値は仮)。
           ctx への加算   … 攻め: outgoingDamageMultiplier +0.40 / +1.0 (ゾロ目は forceCritical) /
                            充電: AddCharge(+3 / 上限まで) /
                            時間: エスカレーションの遅延 (term Coolant と同じキー) +2 / 1 段戻す /
                            封じ: enemyPassivesDisabledTurns = 2 (2 個そろいもゾロ目も・旧 T4 を本体へ)
           次ターンへ     … 封じ: currentBuffs["roleEnemyAtkCut"] (敵攻撃 −40%) / ["roleEnemyStun"] (敵が行動しない) /
                            特別: ["roleFreeReroll"] (無料リロール 2 回) / 連なり: そのターン、 挿した端子がすべて倍化 (TerminalCards.Resolve・〈残響〉と同じ・2026-11-20〜)
           特別 2 個そろい … 小さな極も撃つ (面数×5%・ボス・ヴェスカは面数×2.5%・旧 T4 を本体へ)
           盤面フラグ (RoleOutcome)
                          … 極 (yachtFired + yachtChunkPct: 敵最大HP の 面数×30% (ボス・ヴェスカは ×15%) を軽減無視) /
                            追撃 (extraDamageRatio 0.5 / 1.5・ゾロ目は確定ダメージ) /
                            守り (damageTakenCutPct 40 / 100) /
                            癒し (healAmount 2 / 6・〈古銭の賽〉を装備中は 1 回ごとに +1 = DiePassives.RoleHealBonus・Phase 4 較正: 旧 +3)
           富             … RunState.roleGoldBonusPct (+5% / +30%) を勝利報酬の割合先に掛ける (9 の面を持つ賽は今は無い)
         旧 T4 の固有効果のうち、 攻め=防御無視・守り=軽減分をシールド・時間=段階を攻撃・充電=超過を攻撃・癒し=超過をシールド・富=所持金を攻撃 は 2026-11-18 に無くなった。
         同じターンに役の効果が 2 回働く時 (2026-11-21): 追撃は割合を足す (確定はどちらかがゾロ目なら)・守りは掛け合わせる (−40% を 2 回で −64%)・
           特別・対の無料の振り直しは足す。 ほかはもともと足し算 (攻め・極・癒し・富・時間) か、 2 回でも同じ (封じ・充電のゾロ目)。
         賽目の【規則】と締め (2026-11-21・段 1-f・所持は AxisRules.Owns):
           〈万象のゾロ目〉: ゾロ目 (格上げも) の効果を 2 回 Apply
           〈鏡合わせの盤〉: 2 個そろい (目 f) なら Pair(f − 1) の効果も Apply (役は立っていない ── firedRolesThisTurn・OnRoleFired・計装に数えない)。 f = 1 なら次のターンの無料の振り直し +1・次のターンのブロック +6
           〈宿命の刻み目〉: 立った役がゾロ目・連なり (格上げも)、 または前のターンにも役が立った (role.fateLast・2026-11-22 neon 収束: 旧 ゾロ目だけ) なら ctx.accumulatedValues["role.fate"] に控え、 次のターンの ResolveRoles の頭でその効果を 1 回 Apply (数えない・万象の 2 回・鏡は掛けない)
         役を適用した直後に出来事 OnRoleFired (量 = YachtRoles.Strength・2 個そろい 1 / ゾロ目・連なり 2)。 OnWired の前 = 品の攻撃 +N (mutualAttackBonus) と貫通は撃ごとに乗る。

攻撃しないターン (攻撃端子 0 本 / 逃げる・2026-11-04 修正)
         atkBase = 0・追撃 0 に加えて、 ApplyWinDamageModifiers の後で **主攻撃 dealtDmg = 0・会心なし**。
         (旧: ProcessDamage の damageBonus バフと OnPreDealDamage のパッシブが主攻撃へ加算し、 攻撃しないターンに 1 以上入っていた)
         役の追撃も乗せない。 攻撃端子 0 本のターンは固定ダメ枠 (反撃・出血・毒など) を通す。
         **逃げるターンは固定ダメ枠も 0** (品が預けた分を含む)。 出血の DOT は別の段なので入る。 臨界の爆発はその場で入る (下の「臨界」) ので逃げるターンも入る。

自攻撃   **攻撃の端子 1 枚につき 1 撃 (ADR-0016・2026-11-20)**。 撃 h の攻撃値 hitBases[h]:
           hitBases[h] = 端子 h の出力 + 端子の外の攻撃 (毎回に乗せる)
             端子 h の出力 = TerminalOutcome.attackHits[h] (その端子の効果で attackAdd が増えた分・〈残響〉で 2 回発動ならその合計)
                             + attackHitDice[h] × 端子調律メタ (挿した出目の数 × N)
                             + ceil(その撃の端子の出力 × ctx.attackDicePct) (2026-11-06 の出目 +%・1 撃ごとに掛ける)
             端子の外の攻撃 = playerAttackPower + ctx.mutualAttackBonus (品の攻撃 +N) + consAtk (消耗品) + relicAtk (遺物)
           端子に紐づかない攻撃の加算は **最初の 1 撃 (h = 0) だけ** に入る:
             連係・変圧・溜めた攻撃 (〈力溜め〉〈後の先〉)・賽の加算 (attackAdd − Σ attackHits)・ctx.firstHitAttackBonus (〈引き直しの撃鉄〉)・
             挑戦カード〈金で殴る〉(#6) の GoldPunchAttack(G) (攻撃するターンだけ・払った金 G・所持金まで)
           (品は OnPostRoll までに AddAttackDicePct で率を積む。 攻撃端子 0 本なら 0。 ステータス `atkDicePct` / 〈間合〉〈一対一〉〈猛り〉など武器の段。
            [剣の舞] 4 種・刻みを使い切る 3 品・充電/出血/シールドの 5 品は 2026-11-10 に与ダメージ +% (`AddOutgoingPct` → outgoingDamageMultiplier) へ移した)
           麻痺毒の減算 (playerAttackPowerPenalty) と〈不完全な起動〉も 1 撃ごと (各撃の攻撃値から引く・各撃に掛ける)。
           atkBase = Σ hitBases (画面の倍率・計装が読む合計)。 ターンの終わりに臨界へ足すのは攻撃に挿した出目の合計の半分 (`HeatCore.EndGain`・2026-11-22 neon 収束: 旧 attackSum)。
         各撃 h について (2 撃目以降は、 1 撃目の前の与ダメの池・会心倍率・会心率へ戻してから通す ── 同じ % を重ねて足さない。 固定ダメの枠も空にする):
         → ProcessDamage(hitBases[h], h == 0 ? pursuitDamage : 0, playerCriticalNumerator)   ← 会心の判定・OnPreDealDamage / OnPostDealDamage (「攻撃が通るたび」)・敵の防御
         → ApplyWinDamageModifiers（旧勝利分岐と同一チェーン）
           その末尾 (練度不足の後・大罪:怠惰と敵の軽減パッシブの前) で **端子の別枠**:
           totalDmg = max(1, round(totalDmg × _termDamageMul))  (〈激昂〉1 + AmplifierPerPip × 出目・〈共鳴〉ResonatorMul。 _termDamageMul ≠ 1 かつ totalDmg > 0 のときだけ)
         → 物資の疲労（15% で**最終ダメージ半減**。 isCrit は落とさない）
         → 敵HP -= 撃ごとの dealtDmg (1 撃ずつ HP へ入れ、 〈天眼の鍔〉の首落とし・ボスの目盛りを割る判定も 1 撃ごと。
              合計が役〈追撃〉・ヴェスカの守りで変わった分は配り直す: 減った分は後ろの撃から、 増えた分は最後の撃へ) → 敵HP -= dealtFixed
              端子〈崩し斬り〉(2026-11-20): 撃 h (= 攻撃の端子の h 枚目・TerminalOutcome.attackHitIds[h]) が NoteBossDownMainHit で目盛りを 1 つ以上割ったら、
              Kw.DamageEnemy(floor(敵の最大 HP × 5% (強化 6%))) (預かり・ターンの最後の HP 同期で通る・通常攻撃ではないので目盛りは割らない)
         → 鋼の皮膚
         → **役の追撃**: ApplyWinDamageModifiers の直後に ApplyRoleExtraDamage (与ダメ内訳の算出後)。
           敵の〈飛翔〉〈不動〉が ProcessDamage の中で nullifyPursuitDamage を立てていれば入れない (2026-11-11 #104)
         → **役の削り**: 極(yachtFired フラグ + yachtChunkPct)。 **鋼の皮膚より後**に置く
           ── 「軽減・シールドを無視して削る」を謳う役なので 1HP 踏みとどまりに吸われると文面が嘘になる
           ※〈過不足なし〉(exactLethal) は 2026-08-22 に〈拮抗〉(与ダメ+50%) へリワークされ廃止。
             成立条件が「攻撃合計 = 敵残HP」でスケールが合わず、実測 1000ラン で 発動 0 だったため。
         → 出血 / 吸血 / シールドバッシュ（旧勝利分岐と同一）
         → **固定ダメ枠の取り出し (2026-11-05)**: 主攻撃の ProcessDamage が返した固定ダメ (dealtFixed) を取り出した直後に
           ctx.fixedDamageToEnemy = 0 へ戻す。 戻さないと敵攻撃側の ProcessDamage (attackerIsEnemy: true) が同じ枠を返し、
           「反撃固定」として同じ分がもう一度敵 HP に入っていた (毒・〈二度目の打金〉・〈急所〉・蒼白の槍騎士の倍率まで二重)。
           敵攻撃側で返るのは、 これ以降に足された分 (被弾で発動する反撃・反射) だけ。

ボスのダウン (2026-11-13・規則の正本 BossDown.cs・目盛りの値は BossParams.DownNotches)
         割れ方 (2026-11-14・作者の決定・口 downBreakRule・既定 MainAttackOnly):
           ・通常攻撃 (主攻撃) が 1 以上のダメージで当たった直後 (HpLossDiag.Lose(…, "通常攻撃") の直後・固定ダメの枠より前・CombatManager.NoteBossDownMainHit) の
             敵の HP ≤ floor(最大 HP × 目盛り) の、 まだ割っていない目盛りを全部割る (満額のダウン)。 回避・吸収で 0・撃破 (HP 0) では割らない。
           ・ターンの終わり (FinishTurnCommon のターン終了時の削りを拾った後・NoteBossDownTurnEnd): HP が未割りの目盛り以下ならひびを入れる (そのターン)。 一度入ったひびは回復で目盛りの上へ戻っても消えない (2026-11-14・作者の決定)。
             ひびが入ったターン c から、 c + 猶予 (BossDown.CrackGraceTurns = 1・口 downCrackGrace・負 = ひびでは割れない) のターンの終わりに、 上から順にひとりでに割れる
             (弱いダウン = 攻撃しないが ×1.5 なし・口 downCrackWeak=False で満額)。 1 ターンに両方割れたら、 通常攻撃の分が先・ひびの分が後 (BossDownState.pendingWeak)。
           ・BOT の見込みは同じ規則の BossDown.ForecastTurn (本体の進め方との一致は Tools/rules_check の 14)。
           ・口 downBreakRule=AnyDamage = 旧い判定: ターンの終わりの敵の HP ≤ floor(最大 HP × 目盛り) になった目盛りの数だけ、 次のターンからダウンを待たせる (全部 満額)。
         割った目盛りの数だけ、 次のターンからダウンが続く (1 ターンで 2 つ割れば 2 ターン・ダウン中に次を割ればもう 1 ターン。 上限なし)。 ヴェスカは段ごとに目盛りを入れ直す (待ちは持ち越さない)。
         博士の「止まらない時計」を秘宝として持っているラン (GameLoop.VescaClock.Held = ownedPassiveItems・2026-11-13) は、 ヴェスカの 4 つの段それぞれの目盛りに
         BossParams.VescaClockNotch (0.80) を 1 つ足す (BossParams.DownNotches(id, true)・大きい順に入れる)。 bossDown=False では足さない。
         ダウンのターン (ターンの頭・敵が振る前に立つ):
           ・敵の攻撃 (予告値) = 0 ・〈連撃〉の 2 回目も 0。 敵の出目は振る (乱数の消費は変えない)。 攻撃の枝は〈封じ・揃〉と同じ (下の「敵攻撃」)
           ・受けるダメージ ×1.5 (切り捨て・BossDown.ApplyTaken・満額のダウンだけ = BossDown.TakenMulNow)。 掛ける口は 4 つで、 大罪:怠惰と同じ所:
               主攻撃 = ApplyWinDamageModifiers の怠惰の直後 (CombatManager.Damage.cs)
               主攻撃以外 = HpLossDiag.Lose (channel ≠ "通常攻撃") ── 固定ダメの枠 (毒の tick・臨界の爆発・品が預けた分など)・出血・役〈極〉・反射・仕込み刃・反撃固定・品の直接 (攻撃後)
               〈逆綴じの止血帯〉(ターン終了・ctx の HP を直接減らす) = AllPassiveSkillEffects.Wringing
               品の直接 (ターンの解決の外) = Kw.DamageEnemy
           ・敵のパッシブ・エスカレーション・敵の回復は普段どおり動く (止まるのは攻撃だけ)。 例外は「ダウン中は発動しない」を名乗ったパッシブ
             (BossDownPassives の一覧・PassiveSkillManager.FireEnemyTrigger の入口で鳴らさない・2026-11-20〜 毒の侵蝕 / 鏡映の応答 / 審判の炎 / 灰の再生 / 遺物学者。
             毒の侵蝕はダウンに入った時に毒stack −2 (0 未満にしない)・遺物学者はダウンのターンの頭に前のターンの vescaShieldReflectRate / vescaShieldAbsorbedThisTurn / vescaDrawnRelics を消す)。
           ・大技 (2026-11-14・作者の決定): ダウンのターンが大技のチャージのターン (大技まで 1) か大技のターン (大技まで 0) に重なった時だけ、 その大技は取り消し、
             周期は次のターンから頭に戻す (Escalation.HeavyCycleOffset = このターン)。 それ以外 (小技のターン) のダウンは、 そのターンの攻撃を潰すだけで周期は進み続ける。
             判定は起点を動かす前の周期で BossDown.CancelsCycle (CombatManager.DownCancelsCycleAt)。 構え・〈業火の予兆〉も同じ (山場か、 その 1 ターン前)。
             起点は 1 つなので、 どれかで取り消すと全部の周期が頭に戻る。 測定の口 downCancelRule=Always で旧い規則 (どのダウンでも頭に戻す)。
             周期は「戦闘のターン − HeavyCycleOffset」で数える (既定 0 = 旧い数え方)。 構え (guardPeriod)・灰燼の王〈業火の予兆〉(EmberOmenTiming) も同じ起点を読む
             (予兆は起点 0 なら T1/1+p/…・頭に戻した後は明けてから p ターン目)。

敵攻撃   〈封じ・揃〉が前ターンに成立していれば **このターンの敵攻撃は丸ごと不発**。 ボスのダウンのターン・大技のチャージのターン (2026-11-14) も同じ枝
         (敵の OnPreDealDamage / OnPostDealDamage・〈廃庭の霧吹き〉の毒・〈多頭攻撃〉の 2 回目・プレイヤーの状態の固定ダメ fixedDamageToPlayer の適用は、 攻撃の枝の中にあるので鳴らない)
         (currentBuffs["roleEnemyStun"])。 予告は既に出ているので「見えている一撃を踏み倒す」形。
         〈封じ・対〉は予告値そのものを −40% する (currentBuffs["roleEnemyAtkCut"]・予告にも反映される)。
         被ダメ側の役は〈守り〉(ApplyRoleGuard)。 敵の直接攻撃パケットに置く。

         enemyAtk  = Escalation.EnemyAttackValue(e, escTurn, enemyDiceTotal, currentTurn)
                    = round(段階倍率(escTurn) × (baseAttack + enemyDiceTotal) × 大技倍率(currentTurn) × ボス倍率)
                     大技倍率 (2026-09-19・EnemyData.heavyPeriod > 0 の敵だけ):
                       c = currentTurn − HeavyCycleOffset。 c % heavyPeriod == 0 → heavyMul (大技・挑戦カードの大技の倍率 +N を足す)
                         / 大技の 1 ターン前 (チャージ・予告 turnsToHeavy = 1) → 0 (攻撃しない・2026-11-14・口 heavyChargeNoAttack=False で小技の倍率に戻る)
                         / それ以外 → heavyLightMul (小技・2026-11-14 に heavyWindupMul から改名。 それまでは大技以外の全ターン = チャージも含めた倍率だった)
                       式の正本は HeavyCycle.Factor。 チャージのターンは攻撃値 0 で、 解決はダウンと同じ「攻撃しない」の枝 (下の「敵攻撃」)。
                       周期は鎮静香の遅延を受けない実ターンで数える (ボスのダウンで起点が戻る・上)。 予告に turnsToHeavy / heavyMul / enemyCharging を出す。
                       値 (2026-11-14・倍率は仮置き): 5 層・5 層裏・ヴェスカ p3・p4 = 周期 5 (11-14 に 3 → 5・作者の決定)・小技 ×0.8・チャージ 攻撃なし・大技 ×3.0
                         (2026-11-15: ヴェスカ p1・p2 は大技なし = heavyPeriod 0・小技の倍率も掛からない (×1.0)。 p3・p4 の大技は天与 (TenyoHeavy・下))
                         ヴェスカの天与 (2026-11-15・CombatSystem.TenyoHeavy): チャージのターンの頭に剣 / 盾 / 指輪を抽選 (直前と同じ物は続けない・GameRng "vesca.tenyo")。
                           剣 = 大技の倍率のまま攻撃 + 当たったら最大 HP −TenyoSwordBite (6・vescaMaxHpBiteOnHit) / 盾・指輪 = 大技の倍率 0 (攻撃しない・チャージと同じ「攻撃しない」の枝)。
                           盾 = 大技のターンの頭にヴェスカのシールド + 最大 HP × TenyoShieldPct (20) / 100 (次のターンの頭の半減は普段どおり) /
                           指輪 = 次のターン、 引いた端子を全部止める (〈停滞する時間〉の止める確率を 1 に)。 チャージと大技のターンは〈遺物学者〉の抽選を止める (〈天与の才〉の 2 枚引きは次のふつうのターンへ)。
                           段が入れ替わったら (〈連続実験〉) 起点を入れ替わったターンにして周期を数え直す (TenyoHeavy.OnStageSwap)
                         (それまで 小技 = チャージ ×0.7・大技 ×2.0 / ヴェスカ ×1.75)。 深さ 3 のボス = 周期 3・小技 ×0.6・大技 ×2.0 (BossParams.Depth3Heavy*)。
                       測定の口 enemyHeavySpec="id:小技:大技[:周期],…" (id に * で大技を持つ全員・周期は省略可 = データのまま・2026-11-14) で上書き。 旧い周期 3 へ戻すには周期を明示する ("*:0.8:3.0:3")。
                         周期 N の並びは「小技 × (N−2)・チャージ・大技」(N = 2 はチャージ・大技 / N = 1 は毎ターン大技でチャージなし)。
                         1 周期の平均 (小技 0.8・大技 3.0・チャージ 0): 周期 3 = ×1.27 / 4 = ×1.15 / 5 = ×1.08 (HeavyCycle.CycleMean)。 灰燼の王の周期 (ember) は変えない。
                       対象: 5 層以降のボス (5 層・5 層裏・ヴェスカ p1〜p4)・深さ 3 のボス (BossParams.Depth3Heavy)・6 層 灰燼の王 (2026-11-14〜 大技〈業火〉:
                         周期は旧〈業火の予兆〉の数え方 (heavyCycle = ember・HP で 4 / 3)・小技 ×1.0・大技 ×2.0・業火のターンは別に烈炎 × BlazeBurstPerStack の軽減無視。
                         測定の口 ashKingHeavy=False で旧い形 = 大技なし・予兆ターンに攻撃 +JudgmentDice)。
                     × 構え倍率 (2026-11-04・EnemyData.guardPeriod > 0 の敵だけ): currentTurn % guardPeriod == 0 → guardAttackMul
                     × 先手倍率 (2026-11-04・EnemyData.openingTurns > 0 の敵だけ): currentTurn ≤ openingTurns → openingAttackMul
                     ボス倍率 = 挑戦〈天変地異〉(ボスのみ)
                     → 呪縛等 (mutualEnemyAttackReduction) / 〈封じ・対〉 / 失血衰弱 の減算
                     → **プレイヤーの防御** (ADR-0013・2026-10-28): DefenseFormula.Apply(enemyAtk, max(0, playerDefense − max(0, enemyArmorPen − enemyArmorPenNegate)))
                       ここまでが予告値。
                       (剣〈間合〉が enemyArmorPenNegate を足す)
         配線確定  → accumulatedValues に端子ごとの本数・予告の敵攻撃を書いて **OnWired** (2026-10-28)
                       ── 剣〈切り返し〉〈不滅の刃〉・盾〈シールドバッシュ〉〈堅守の構え〉が読む。 atkBase の確定より前
         blockSum  = 端子の結果の blockAdd → 遺物〈堅牢の環〉+ceil(×率) → 大罪:傲慢 floor(×(1 − 率)) → + 端子調律メタ(ブロックの個数×N)
                     → + ctx.itemBlockBonus (品のブロック +N・〈帯電の胸甲〉〈絶縁の外套〉〈避雷の杭〉・perTurn)
                     → 〈貫きの錐〉: blockSum −= enemyAtk × PierceAwlPerAttack   (enemyAtk は防御を引いた後の値)
                     → 〈不完全な転移〉(門・§4-3): blockSum −= floor(blockSum × 0.50)
         lossBase  = max(0, enemyAtk − blockSum)
         → ProcessDamage(lossBase, 0, enemyCriticalNumerator, attackerIsEnemy: true, fireEnemyDealTriggers: true)
           ※ attackerIsEnemy: true = **会心判定を飛ばす**。 これが無いと敵の攻撃が
             プレイヤーの会心率で会心し、 プレイヤーの criticalMultiplier で増幅される
           ※ fireEnemyDealTriggers: true = lossBase (+damageBonus) > 0 なら中で敵の OnPreDealDamage を鳴らす
             (剛力/闇技/研ぎ澄まし/反撃態勢・§1 ②)。 2 体目・〈連撃〉の 2 回目は false。
             (旧: この直前で敵の OnPreDealDamage を鳴らしていた ── 2026-11-11 #104 で撤去)
         → ApplyLossDamageModifiers（旧敗北分岐と同一）
         → **役の被ダメ側**: 大階・相殺(0) / 束(防)(半減)。 対象は
           敵の直接攻撃パケットのみ。 fixedDamageToPlayer は別チャネルなので触らない
         → 仕込み刃（旧法と同一）
         → プレイヤーHP -= takenDmg (+ fixedDamageToPlayer)
         → 痛覚遮断剤 post-clamp / Counter 固定ダメ
         → ctx の HP を playerHP / enemyHP へ揃え、 ctx.finalDamage = takenDmg にして敵の OnPostDealDamage (〈吸血〉)
           → SyncHPFromContext (2026-11-11 #104)

充電     chargeSum = 端子の結果の chargeAdd      ← 個数ではない (基本の〈充電〉は出目そのまま・〈雷溜め〉出目 +2 など)
         → ctx.AddCharge(chargeSum)  (上限 ChargeMax=**30** + 整備パネル 種火・充電で +10)
         ※ ChargeMax は ADR-0010 で 10→30。 充電が**リロールの原資**になったため
         ※ chargeDiceCount(個数) も併算されるが AddCharge には渡らない
           (2026-08-03 確認: CombatManager.cs の `if (chargeSum > 0) ctx.AddCharge(chargeSum);`)
         消費 (2026-11-21〜・充電 = 戦闘のマナ): 振り直し (⓪)・充電を払う端子・【再起動】・【オーバークロック】・〈逆さ避雷針〉 (Resolve の 5c)。
               どれも ctx.ConsumeCharge を 1 回ずつ通り、 OnChargeSpend (量 = 払った量) と perTurn の ChargeSpentThisTurnKey (量) / ChargeSpendsThisTurnKey (回数) を積む
         過充電: charge >= ChargeCore.OverchargeAt (=10) で IsOvercharged()=true (2026-11-21〜・旧 7)
         品の式は ChargeEffects.cs (26 品・full-item-rework-ledger §①) の各クラスの XML コメント

臨界     (Rinkai メーター・2026-07-16 追加。 2026-11-21 フルリワークで式を CombatSystem.HeatCore へ・詳細は §3-M)
         溜める処理は Kw.AddHeat の 1 か所 (中で HeatCore.Gain / TryBurst)。 ターン終わりに meter += floor(攻撃に挿した出目の合計 / 2) (`HeatCore.EndGain`・2026-11-22 neon 収束: 旧 attackSum)、 品・端子の分はその出来事の時点。
         meter >= 閾値 (既定 32・〈早沸かし釜〉−6・〈暴れ窯の火床〉で下がる・下限 12) でその場で爆発 → CombatManager.LandBurst、 OnRinkaiBurst を配る。
         爆発 1 回 = HeatCore.Damage (基礎 30 + 上乗せ → 超臨界 ×1.5 ほか) を HeatCore.VsDefense (敵の防御の 50% を無視) に通した量。
           会心・与ダメの池は乗らない。 最終与ダメージ倍率・大罪:怠惰・ダウンの ×1.5 は HpLossDiag.Lose (channel "臨界の爆発") が掛ける。 遺物 (§15-5) の臨界軸: 爆発 1 回ごとに + floor(その時の敵の現在HP × N%)。
         爆発のたびボスの次の目盛りにひび (CrackBossNotch)。
         超臨界爆発: 閾値を引く前の meter が閾値の 1.5 倍以上 (2026-11-21・旧 2 倍)。 OnRinkaiBurst の直後に OnSuperBurst。 出来事の間は Kw.BurstDamageNowKey (防御の後の量) / BurstExcessKey が読める。
```

### 9.3 収支トリガー（柱2 の再定義・§0 の敵視点は変わらない）

`balance = (dealtDmg + dealtFixed) − takenApplied`（takenApplied = 主被ダメ + fixedDamageToPlayer）
を解決後に算出し、`ctx.playerWonRoll = balance > 0` / `playerLostRoll = balance < 0` を上書きしてから
`OnRollWin/Lose/Draw` を再発火する。既存 79 箇所のトリガー効果は無改修で「収支トリガー」として動く。
副作用: 出目参照系は `ProcessPostRoll` の時点で発火するため、収支確定前の `playerWonRoll` を
参照する効果があると値が古い（W7 後段で個別判定）。

**HP の受け渡し（2026-11-11 #104）**: 収支トリガーを鳴らす直前に `ctx.playerCurrentHP/playerMaxHP/enemyCurrentHP` を
CombatManager のフィールドへ揃え、鳴らした直後に `SyncHPFromContext` で拾う（CombatManager.Turn.cs の収支トリガー）。
揃える値は直後の FinishTurnCommon の書き戻しと同じなので、敵パッシブが ctx の HP を動かさない限り挙動は変わらない。
これで収支トリガーの中の HP の増減（〈尾撃〉〈地獄の業火〉〈重甲〉の `EnemyDealUnmitigable`・シュヴァリエの反撃と「ボスへ 15」）が残る。
ここで入ったダメージは収支（balance）には数え直さない。致死なら FinishTurnCommon の combatLethalThisTurn に入る。

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

- ~~特殊端子 11 種~~ → 2026-11-11 に**削除**し、端子の山札（ADR-0014・§9.2「配線」）へ置き換えた（GAME.md §24）
- ~~電力経済の消費/停電~~ → 充電の消費先は **ADR-0010 のリロール**（§9.2）。
  オーバーロードは 2026-08-09 に**廃止**（GAME.md §24）。停電（充電切れのペナルティ）は未実装のまま
- ダイス改変系（影の代償/コルヴェンの憤怒。 大穴の異常現象の鐘/正午/蝕夜は 2026-10-29 に仕組みごと削除）
- ボス固有機構（サドンデス/コントラタック/妙覚）— boss6/7 は当面旧法で計測
- 収支確定前の `playerWonRoll` を参照する効果の棚卸し（AllPassiveSkillEffects 側）

### 9.6 パッシブリワーク一覧 (2026-07-15)

ADR-0009 パイプラインでのパッシブ挙動のリワーク記録 (2026-07-15)。当時は新旧の経路をトグルで切り替える
ブリッジ方式だった (旧経路は 2026-10-29 に削除)。「攻撃+N」は `ctx.AddPlayerAttackOrDiceBonus(n)` で `mutualAttackBonus` へ積む。

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
| C. 差分依存の解消 | Judgement (天命) | 新: 「被ダメ≥現HPの50%かつHP≥30%で HP-2 上限」に置換 |
| D. 敵ダイス操作 | CurseBind (呪縛) | 新: `mutualEnemyAttackReduction += debuff` (敵攻撃値-N) |
| E. 特殊機構の再設計 | GaryoTensei (画竜点睛) | 新: 「即勝利」廃止 → 出目最大なら `mutualAttackBonus += 出目+10` + `forceCritical=true` |
|  | BloodDecree (血令) | 新: ゾロ目時 `max(finalDamage, playerDiceTotal) × 2.5` を軽減不能に置換 |
| DEAD | Truce (停戦協定) | 新: 収支0が頻発するため no-op 化 (旧法のみ機能。再設計待ち) |

**リワーク不要 (自然に動く・敵側含む)**:
- 被ダメ軽減/回復/シールド/会心/出目パターン/HP参照系 — 全 46 種
- 敵側ダイス合計+N の 22 種 — 既存の enemyDiceTotal 経由で自動的に攻撃値へ寄与（当時は β=0.3 圧縮。β は 2026-07-28 に撤去し、いまは等倍で入る）。
  ただし 2026-11-11 の監査（#30）で、共有バフ（`nextTurnBuffs`）や OnPreDealDamage・OnPrePursuitDamage・解決後の収支トリガーに頼る敵パッシブは
  効いていないことが確定した（§0 の「共有バフの罠」と §4 の [不発・確定] ほか・#104）。同日 #104 で直した（§4・§5 の [#104 修正]。〈多頭攻撃〉は効果を置き換えた）
- 収支トリガー再定義で無改修動作の 15 種 (Counter/ShieldBash/LentTime/BladeEdge/Conqueror 他)

**W7 後段の宿題 (§9.5 に既記載)**:
- ボス固有機構 (SaintGeorges/Awakened P1-P7/EmberKing/JudgmentBlaze) の個別移植
- パッシブ〈Parry〉の scratch 廃止に伴う効果差し替え (現状は KEEP 動作だが無意味に)。
  **タイミング判定のパリィとは別物**（そちらは 2026-08-09 に全廃・GAME.md §24）
- ~~IntimidatePlus/PlusPlus の threat 加算を「基礎攻撃値」加算に統合~~ → 2026-11-11 #104 で敵の攻撃 +N（enemyDiceTotalBonus）にした（§4）
