using System;
using System.Collections.Generic;
using UnityEngine;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>CombatManager: 与ダメ／被ダメの修飾チェーン (ApplyWin/ApplyLoss)。 順序が結果を左右するので崩さない。</summary>
    public partial class CombatManager
    {
        /// <summary>
        /// プレイヤー勝利時の与ダメージ修飾（ProcessDamage後〜敵HP適用前）。適用順:
        /// 与ダメ倍率 (パッシブ・メタ・遺物・研磨剤・鬼火の油を 1 つのプールで加算) → 攻撃バースト → メタ会心 → 向かい風
        /// → 敵被ダメ軽減(灰塵等の OnPreReceiveDamage) → 基礎防御%(利刃で相殺) → 勝利時最低保証
        /// → 狂暴化 → メタ俊敏回避。
        /// </summary>
        /// <summary>[計装] 層別集計の添字 (= 深さ)。 0 は旧 Λ層 (2026-10-28 廃止) の枠でいまは使わない。</summary>
        private static int FloorStatIndex()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            return run == null ? 0 : run.Depth;
        }

        /// <summary>[計装] 武器 ID が所持パッシブ (ownedPassiveItems) にも入っているか。
        /// 入っていると RunPassiveSync が武器の静的 skills を登録し、 段階式の動的付与と二重になる疑い。</summary>
        private static string DsWeaponAlsoOwnedFlag()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run?.ownedPassiveItems == null) return null;
            if (!string.IsNullOrEmpty(run.equippedWeaponId) && run.ownedPassiveItems.Contains(run.equippedWeaponId))
                return "__flag__:装備中の武器が所持パッシブにもある";
            var db = ItemSystem.ItemDatabase.Instance;
            if (db != null)
                foreach (var id in run.ownedPassiveItems)
                    if (db.GetItem(id)?.category == ItemSystem.ItemCategory.Weapon)
                        return "__flag__:別の武器が所持パッシブにある";
            return null;
        }

        private int ApplyWinDamageModifiers(int totalDmg, ref int fixedDmg, ref bool isCrit,
                                            int lbStage, PassiveSkillManager psm, CombatContext ctx)
        {
            // 与ダメ倍率の内訳計測 (診断用)。 **ここへ来る時点で outgoing にはパッシブ由来が
            // 既に積まれている** (OnPreDealDamage は ProcessDamage の中で発火するため)。
            // 以降の 業物/メタ/遺物 の増分と分けて記録する。
            double _ovPassive = ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier;
            double _ovPrev = _ovPassive;
            CombatStats.DamageBreakdown[11] += _ovPassive;
            // 黄金卿の剣 (+0.01 × 消費GOLD) は **上限が無い**ので、 消費GOLDも記録して
            // パッシブ由来 outgoing のうちどれだけがこれ由来かを切り分けられるようにする。
            CombatStats.DamageBreakdown[17] += GameLoop.GameManager.Instance?.Run?.coinsSpent ?? 0;
            if (_ovPassive > CombatStats.DamageBreakdown[18]) CombatStats.DamageBreakdown[18] = _ovPassive;

            // 業物: 与ダメ倍率 +20%/lv（outgoingDamageMultiplier に加算）
            // **2026-08-10 廃止。** 旧: 与ダメ倍率 +20%/lv。
            //   素の倍率で天井を押し上げるだけの段で、 かつ素材の唯一の後半シンクだった。
            //   素材価格が 2^N の倍々カーブなので、 ショップ価格倍率 (搾取経済) の効きが
            //   この経路を通って **log で潰れ**、 挑戦デバフに段差を作れなくなっていた
            //   (実測: 価格 ×1.15 と ×1.35 で業物が 3.62 / 3.63 と動かない)。
            //   代償は後半層の敵HP −10% (SpawnEnemy 側の LateFloorEnemyHpMultiplier)。
            //   lbStage は常に 0 (RunState.limitBreakStage は互換のため残置)。
            CombatStats.DamageBreakdown[12] += ctx.outgoingDamageMultiplier - _ovPrev; _ovPrev = ctx.outgoingDamageMultiplier;

            // メタバフ: 与ダメ +5%×N (cap 50%)。 他の outgoing% と同じ pool に加算合成
            // ＝最終に純倍率を掛けるとインフレするので、 ここでは additive。
            int metaPct = MetaProgression.MetaBuffApplicator.GetOutgoingDamagePct();
            if (metaPct > 0) DmgSourceDiag.Note("整備パネル〈出力〉", DmgSourceDiag.Out, metaPct * 0.01f);
            // 出力 r10 (極点) 〈戦意〉: 勝利数 × 2%。 同じ pool に加算合成 (2026-09-13)。
            int dsSpirit = MetaProgression.MetaBuffApplicator.GetBattleSpiritPct(
                GameLoop.GameManager.Instance?.Run);
            metaPct += dsSpirit;
            if (MetaProgression.MetaBuffApplicator.IsBattleSpiritUnlocked())
                DmgSourceDiag.Note("整備パネル〈戦意〉", DmgSourceDiag.Out, dsSpirit * 0.01f);
            // 剛胆 r10 (極点): **精鋭にだけ** +10%。 通常戦には乗らない ──
            //   乗せると出力トラックの薄い複製になり、 軸の性格が消える。
            //
            //   **2026-09-14: ボスを対象から外した。** 含めていたため最終ボス勝率が 90.2%
            //   (他トラックは 67〜78%) まで跳ね、 r10−r9 +11.17pt の主因になっていた。
            //   ボスは<b>自分が呼んだ強敵ではない</b>ので、 「リスクを取って強敵を呼び、
            //   その見返りを取る」というトラックの筋から外れる。
            {
                int slayer = MetaProgression.MetaBuffApplicator.GetEliteSlayerPct();
                if (slayer > 0)
                {
                    var kindNode = MapSystem.MapManager.Instance?.CurrentNode;
                    var kind = kindNode != null
                        ? kindNode.EffectiveType.ToEnemyKind() : EnemyKind.Normal;
                    DmgSourceDiag.Held("整備パネル〈剛胆r10〉");
                    if (kind == EnemyKind.Elite)
                    {
                        metaPct += slayer;
                        DmgSourceDiag.Note("整備パネル〈剛胆r10〉", DmgSourceDiag.Out, slayer * 0.01f);
                    }
                }
            }
            if (metaPct > 0)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += metaPct * 0.01f;
            }
            CombatStats.DamageBreakdown[13] += ctx.outgoingDamageMultiplier - _ovPrev; _ovPrev = ctx.outgoingDamageMultiplier;

            // [14] は旧「アイテム副次ステータスの与ダメ%」。 2026-09-15 に副次を全廃したので
            //      書き手が居なくなった。 添字は他段の意味を保つため詰めない (常に 0)。

            // 遺物 (§15-5) 与ダメージ+N%。 同じ pool に additive (純倍率にするとインフレするため)。
            float relicOut = MetaProgression.Relics.RelicApplicator
                             .GetOutgoingPct(GameLoop.GameManager.Instance?.Run, ctx);
            if (relicOut > 0f)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += relicOut;
                DmgSourceDiag.Note("遺物〈与ダメ%〉", DmgSourceDiag.Out, relicOut);
            }
            CombatStats.DamageBreakdown[15] += ctx.outgoingDamageMultiplier - _ovPrev;
            CombatStats.DamageBreakdown[16] += ctx.outgoingDamageMultiplier;

            // 瞬間研磨剤 (剣士スターター・2026-07-27 リワーク): 次の 1 撃だけ 与ダメージ+150%。
            // 旧「ダイス合計+[本数×2]」は相互攻撃モデルで無効だったため倍率へ移設。
            if (ctx.polishArmed)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += 1.5f;
                DmgSourceDiag.Note("消費〈瞬間研磨剤〉", DmgSourceDiag.Out, 1.5f);
                ctx.polishArmed = false;   // 1 撃限り
                if (CombatStats.DiagWinAttack) CombatStats.Stage(19, 1);
                if (Debug.unityLogger.logEnabled) Debug.Log("[瞬間研磨剤] 与ダメージ+150%");
            }

            // 消費: 鬼火の油 与ダメ+X%（全戦闘）。 **他の与ダメ% と同じプールへ加算** (2026-09-19)。
            //   旧: 倍率を掛けた後の totalDmg に さらに ×(1+X%) を掛けており、 与ダメ% を重ねると掛け算で膨らんだ。
            if (ctx.consDmgMultPct > 0)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += ctx.consDmgMultPct / 100f;
                DmgSourceDiag.Note("消費〈油・膏薬〉", DmgSourceDiag.Out, ctx.consDmgMultPct / 100f);
            }

            // 与ダメ倍率（激情の刃 等のパッシブ由来 + メタ% + 遺物 + 研磨剤 + 鬼火の油）
            //   + この 1 撃だけの池への上乗せ (〈加重の拳套〉・ctx.hitOutgoingPct)
            float outMul = (ctx.outgoingDamageMultiplier > 0f ? ctx.outgoingDamageMultiplier : 1f) + ctx.hitOutgoingPct / 100f;
            if (Mathf.Abs(outMul - 1f) > 0.001f)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * outMul);
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] 与ダメ補正 ×{outMul:F2}: {orig}→{totalDmg}");
            }
            if (CombatStats.DiagWinAttack)
            {
                CombatStats.Stage(5, totalDmg);   // 段 E
                CombatStats.Stage(22, ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier);
            }

            // 消費: 攻撃力バースト（この勝利ターンのみ・単発消費）
            if (ctx.consAtkBurst > 0)
            {
                totalDmg += ctx.consAtkBurst;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] 攻撃バースト +{ctx.consAtkBurst}");
                ctx.consAtkBurst = 0;
            }
            if (CombatStats.DiagWinAttack) CombatStats.Stage(6, totalDmg);   // 段 F

            // [2026-07-28 撤去] メタバフ「会心ダイス」による会心時の固定ダメ加算。
            //   GetCritBonus() は精密トラックの分子補正だったが、 分子/分母モデル廃止に伴い常に 0。
            //   同じ値を PassiveSkillManager では会心率の分子、 ここでは固定ダメージ加算として
            //   二重に解釈していたため、 精密の効果は会心率(%)へ一本化した (§15-1)。

            // 挑戦デバフ 軸2〈練度不足〉: プレイヤー与ダメ ×0.94 / ×0.91 / ×0.89 (2026-08-09)。
            //   旧「向かい風 −1」の定数減算から % 乗算へ (docs/GAME.md §15-2)。 定数減算は
            //   高火力ビルドではほぼ無視でき、 低火力ビルドだけを一方的に潰していた。
            //   **最低 1 は残す** ── 0 にすると削りが完全に止まり、 耐久レースが成立しなくなる。
            //
            //   (同種のデバフを足すときは重ねがけせず厳しい方 1 つを採ること ── 乗算は 0.756 まで落ちた。)
            if (CombatStats.DiagWinAttack) CombatStats.Stage(7, totalDmg);   // 段 G

            // [測定用の物差し 2026-11-10] 主攻撃の倍率 (既定 1 = 素通し・製品では使わない)。 この関数の呼び手はプレイヤーの主攻撃だけ (Turn.cs)。
            // 端子の山札 (ADR-0014): 別枠の与ダメージの掛け算 (〈激昂〉〈共鳴〉)。 他の与ダメ% と足さずに掛ける。
            if (_termDamageMul != 1f && totalDmg > 0) totalDmg = Mathf.Max(1, Mathf.RoundToInt(totalDmg * _termDamageMul));
            totalDmg = EnemyHpLedger.ProbeMain(totalDmg);
            // 最終与ダメージ倍率 (2026-11-20・GAME.md §23-32・〈防人の軍標〉): すべての倍率の後・敵の軽減パッシブと防御の前 (怠惰の直前)。
            //   主攻撃以外は敵の HP を減らす口 (HpLossDiag.Lose) で掛ける
            totalDmg = ItemSystem.PassiveSkills.Keywords.Kw.ApplyFinalDamage(ctx, totalDmg, ctx.hitFinalDamagePct);   // この 1 撃だけの上乗せ (〈血桜〉〈血の楔〉・オーバークロック) も足す
            // 〈徒労〉の戦闘のデバフ (旧 大罪:怠惰の枠・怠惰の与ダメ −30% は 2026-11-22 に撤去) (2026-11-14・GameHooks.ToilDamageDownPct・既定 50): 主攻撃の最終ダメージ −N%
            //   (すべての倍率の後・敵の防御の前)。 2 つが重なったら厳しい方 1 つ (BattleBoonRule.OutgoingCutPct)
            totalDmg = MetaProgression.MetaDebuffApplicator.ApplyOutgoingCut(totalDmg);   // 主攻撃以外は敵の HP を減らす口 (HpLossDiag.Lose) で掛ける
            // ボスのダウン (2026-11-13): 受けるダメージ ×1.5 (別枠の掛け算・与ダメ% の池に足さない)。 怠惰の直後 = すべての倍率の後・敵の軽減パッシブと防御の前。
            //   主攻撃以外 (毒・爆発・品の預けた分 = 固定ダメの枠・出血・反射・役〈極〉・仕込み刃・反撃固定・品の直接) は HpLossDiag.Lose で同じく掛ける
            totalDmg = BossDown.ApplyTakenNow(totalDmg);

            // 敵側のダメージ軽減パッシブを発火（前後で ctx.finalDamage と totalDmg を同期し、
            // 敵パッシブ（AshArmor 等やシュヴァリエのシールド経由処理）が
            // 実際の与ダメに反映されるようにする）
            ctx.finalDamage = totalDmg;
            int ledgerBeforeRed = totalDmg;   // [計装] 敵の軽減パッシブで削られた量
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreReceiveDamage);
            totalDmg = System.Math.Max(0, ctx.finalDamage);
            // 〈血の楔〉(2026-11-20・§23-32): 敵の軽減パッシブで減った量のうち 出血 ÷ (出血 + 30) を戻す (効き目を弱める)
            if (ctx.bleedWedge && ledgerBeforeRed > totalDmg)
            {
                int cut = ledgerBeforeRed - totalDmg;
                int back = cut - ctx.WedgeEnemyGain(cut);
                if (back > 0) { totalDmg += back; Debug.Log($"[血の楔] 敵の軽減 −{cut} のうち {back} を戻す (出血 {ctx.enemyBleedStacks}) → {totalDmg}"); }
            }
            EnemyHpLedger.NoteMainEnemyReduction(ledgerBeforeRed, totalDmg);
            if (CombatStats.DiagWinAttack) CombatStats.Stage(8, totalDmg);   // 段 H

            // 敵の防御値 (ADR-0013)。 敵の軽減パッシブ (灰塵の鎧 等) の後、 倍率が全部乗った最終値に掛ける。
            //   貫通は防御値を固定値で引く。 余った貫通は何にもならない
            //   (旧〈利刃〉の余剰貫通→与ダメ%転用は 2026-10-28 に撤去・GAME.md §24)。
            // 短剣〈急所〉: 会心の一撃は敵の防御を無視する (2026-10-28)。
            if (ctx.enemyDefense > 0 && totalDmg > 0 && isCrit
                && ItemSystem.PassiveSkills.Effects.VitalStrike.IgnoresDefense(ctx))
            {
                if (Debug.unityLogger.logEnabled) Debug.Log($"[急所] 会心 → 敵の防御{ctx.enemyDefense} を無視");
            }
            else if (isCrit && ctx.enemyDefense > 0 && totalDmg > 0
                     && ((ctx.hitIsFocused && ItemSystem.PassiveSkills.Effects.HeavenEyeGuard.Held(ctx))
                         || (ctx.critExcessDouble && ItemSystem.PassiveSkills.Effects.Crit.HitRenki(ctx) >= ItemSystem.PassiveSkills.Effects.Suntetsu.IgnoreRenki)))
            {
                // 〈天眼の鍔〉の集中会心 / 〈寸鉄〉の【練気】5 以上の会心は敵の防御を完全に無視する (2026-11-23)
                if (Debug.unityLogger.logEnabled) Debug.Log($"[天眼の鍔/寸鉄] 会心 → 敵の防御{ctx.enemyDefense} を無視");
            }
            else if (ctx.hitIgnoreDefense && ctx.enemyDefense > 0 && totalDmg > 0)
            {
                if (Debug.unityLogger.logEnabled) Debug.Log($"[防御の無視] この撃は敵の防御{ctx.enemyDefense} を無視 (破城斧・急所の 2 撃目以降・連刃の追撃)");
            }
            else if (ctx.enemyDefense > 0 && totalDmg > 0)
            {
                int effDef = DefenseFormula.Effective(ctx.enemyDefense, ctx.playerArmorPen + ctx.hitArmorPen + (isCrit ? ctx.critArmorPen : 0));
                if (effDef > 0)
                {
                    int beforeDef = totalDmg;
                    totalDmg = DefenseFormula.Apply(totalDmg, effDef);
                    EnemyHpLedger.NoteMainDefense(beforeDef, totalDmg);   // [計装] 防御値で削られた量
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[敵の防御] 防御{ctx.enemyDefense}−貫通{ctx.playerArmorPen}→{effDef}"
                            + $"（軽減{DefenseFormula.Reduction(effDef):P0}）{beforeDef}→{totalDmg}");
                }
            }
            if (CombatStats.DiagWinAttack) CombatStats.Stage(9, totalDmg);   // 段 I

            // 段 J (旧: 余剰貫通の与ダメ転用) は撤去。 段別実測の添字を保つため計測点だけ残す。
            if (CombatStats.DiagWinAttack) CombatStats.Stage(10, totalDmg);   // 段 J

            // 段 K は撤去。 段別実測の添字を保つため計測点だけ残す。
            if (CombatStats.DiagWinAttack) CombatStats.Stage(11, totalDmg);   // 段 K

            // 脆弱 (狩猟旅団契約): armed状態で会心ダメージを与えると最終ダメージに ×(1+0.15/0.30/0.45) 倍率。
            // 適用後 consumed 状態へ遷移。 ロール勝利時の非会心ダメで armed に再点火する処理は応答側で別途。
            if (isCrit && totalDmg > 0)
            {
                float vulMult = VulnerabilityStatus.ConsumeOnCrit(ctx);
                if (vulMult > 1f)
                {
                    int beforeVul = totalDmg;
                    totalDmg = Mathf.CeilToInt(totalDmg * vulMult);
                    DmgSourceDiag.Note("契約〈脆弱〉", DmgSourceDiag.Vul, vulMult - 1f);
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[狩猟旅団] 脆弱発動 ×{vulMult:F2} {beforeVul}→{totalDmg}");
                    if (CombatStats.DiagWinAttack) CombatStats.Stage(20, 1);
                }
            }
            else if (!isCrit && totalDmg > 0)
            {
                // ロール勝利時の非会心ダメで脆弱を再点火 (契約継続中のみ)
                VulnerabilityStatus.RearmOnNonCritWin(ctx);
            }
            if (CombatStats.DiagWinAttack) CombatStats.Stage(12, totalDmg);   // 段 L

            // 勝利時の与ダメ最低保証（基本1、利刃Lvで1/2/3/4）。「勝ったのに0」を防止。
            if (totalDmg < ctx.winMinDamage)
                totalDmg = ctx.winMinDamage;
            if (CombatStats.DiagWinAttack) CombatStats.Stage(13, totalDmg);   // 段 M

            // 狂暴化(ボス50T後): エネミーが受けるダメージを倍化（軽減処理の後・適用直前）
            if (ctx.enemyDamageTakenMultiplier > 1f && totalDmg > 0)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.enemyDamageTakenMultiplier);
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] 狂暴化: 敵被ダメ ×{ctx.enemyDamageTakenMultiplier:F1} ({orig}→{totalDmg})");
                if (CombatStats.DiagWinAttack) CombatStats.Stage(21, 1);
            }
            if (CombatStats.DiagWinAttack) CombatStats.Stage(14, totalDmg);   // 段 N


            if (CombatStats.DiagWinAttack) CombatStats.Stage(15, totalDmg);   // 段 O (最終)
            return totalDmg;
        }

        /// <summary>
        /// プレイヤー敗北時の被ダメージ修飾（ProcessDamage後〜プレイヤーHP適用前）。適用順:
        /// 天変地異(×2) → メタ被ダメ軽減 → 不屈の鎧/苦難の刻印 → 地獄門 → 亡者の招待(+30%)
        /// → 共助(T1半減) → 獣の絆 → 消費シールド → コントラタック (ブロックとシールドで防いだ分 ×2 を返す) → 鏡写し反射。
        /// <paramref name="blocked"/> = この攻撃のうちブロックで止めた量 (〈シュヴァリエのレイピア〉のコントラタックが読む)。
        /// enemyHP はメンバフィールドのため反射系はここで直接削る。
        /// </summary>
        private int ApplyLossDamageModifiers(int totalDmg, MapSystem.FloorModifier floorMod, CombatContext ctx, int blocked = 0)
        {
            // 挑戦デバフ〈狂暴化〉: 敵ダメージ倍率。
            //   〈天変地異〉のボス初撃強化は 2026-08-08 に廃止し、 エスカレーション閾値の
            //   前倒し (Escalation.StageOf) へ移した。 ボス戦のみ・最初の 3 撃という形は
            //   40 ターン級の戦闘では誤差にしかならず、 単独 T3 が実質無料だったため。

            // (2026-06-01) ボス被ダメ倍率(dmgMul)の直接操作は廃止。 硬化/易化は Dice/HP/機構軸で行う。

            // [削除 2026-09-13] メタバフの定額被ダメ軽減 ── 2026-08-15 に割合軽減へ移して以降
            //   常に 0 を返す残骸だった。 現行は下の GetGuardDamageReductionPct の乗算段。

            // 名前付きパッシブ由来の固定被ダメ削減（不屈の鎧・苦難の刻印 等の合算）
            if (ctx.playerFlatDamageReduction > 0 && totalDmg > 0)
            {
                int bf = totalDmg;
                totalDmg = Mathf.Max(1, totalDmg - ctx.playerFlatDamageReduction);
                if (GuardDiag.InVesca()) GuardDiag.Cut7F[5] += bf - totalDmg;   // [計装 2026-11-11]
            }

            // 遺物 (§15-5) 被ダメージ−N%。 **割合軽減**であって定額ではない。
            //   定額だと参照被ダメ 7.7/T に対し段6 が 78% 軽減になり、 序盤の敵 (baseAttack 2〜10) を
            //   丸ごと無力化していた。 割合なら敵が弱いほど軽減の絶対量も小さくなる (§24)。
            float relicRed = MetaProgression.Relics.RelicApplicator
                             .GetDamageReductionPct(GameLoop.GameManager.Instance?.Run, ctx);
            if (relicRed > 0f && totalDmg > 0)
            {
                int bf = totalDmg;
                totalDmg = Mathf.Max(1, Mathf.CeilToInt(totalDmg * (1f - relicRed)));
                if (GuardDiag.InVesca()) GuardDiag.Cut7F[6] += bf - totalDmg;   // [計装 2026-11-11]
            }

            // [撤去 2026-09-13] 整備パネル〈防御〉の割合軽減。 このトラックは
            //   リスク軸〈剛胆〉へ完全リワークし、 被ダメには一切触れなくなった。

            // 2026-06-28: 不抜の聖紋 (騎士スターター): HP 80% 以上維持中 被ダメ -40%、
            // 一度でも 80% を割ったら以後無効 (回復しても復活しない)
            if (ctx.oathArmed && !ctx.oathBroken && playerMaxHP > 0)
            {
                // 開始時点で既に 80% 未満なら即 break
                if (playerHP * 5 < playerMaxHP * 4)
                {
                    ctx.oathBroken = true;
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[不抜の聖紋] HP 80% 割れにより破棄 (HP {playerHP}/{playerMaxHP})");
                }
                else if (totalDmg > 0)
                {
                    int reduced = Mathf.CeilToInt(totalDmg * 0.4f);
                    totalDmg = Mathf.Max(0, totalDmg - reduced);
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[不抜の聖紋] 被ダメ -{reduced} → {totalDmg} (HP {playerHP}/{playerMaxHP})");
                    // この被ダメ適用後に 80% を割るなら次回以降は無効化
                    if ((playerHP - totalDmg) * 5 < playerMaxHP * 4)
                    {
                        ctx.oathBroken = true;
                        if (Debug.unityLogger.logEnabled) Debug.Log($"[不抜の聖紋] この被ダメで HP 80% 割れ予定 → 破棄");
                    }
                }
            }

            // フロアデバフ: 敗北時の被ダメージ軽減（5層 地獄門: -2）
            if (floorMod != null && floorMod.defeatDamageReduction > 0 && totalDmg > 0)
            {
                int reduced = Mathf.Min(totalDmg, floorMod.defeatDamageReduction);
                totalDmg -= reduced;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] フロアデバフ: 敗北時被ダメ-{reduced} → {totalDmg}");
            }

            // 亡者の招待: 被ダメ +30%
            if (ctx.receivedDamageBonus > 0f && totalDmg > 0)
            {
                int bonusDmg = Mathf.CeilToInt(totalDmg * ctx.receivedDamageBonus);
                totalDmg += bonusDmg;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] 亡者の招待: 被ダメ+{bonusDmg} (合計{totalDmg})");
            }

            // 共助: 1ターン目のメインダメージ半減
            if (ctx.halveFirstEnemyAttack && ctx.currentTurn == 1)
            {
                totalDmg = totalDmg / 2;
                ctx.halveFirstEnemyAttack = false;
                Debug.Log("[CombatManager] 共助発動: 敵の最初の攻撃を半減");
            }

            // 〈死相の面頬〉(2026-11-23): 会心したターン、 そのターンの敵の攻撃を 練気 ÷ (練気 + 12) 減らす (大技のターンは練気を 2 倍で数える・切り捨てで減らす・上限なし)
            {
                bool heavyNow = currentEnemy != null && Escalation.IsHeavyTurn(currentEnemy, ctx.currentTurn) && !BossDown.ActiveNow;
                float maskCut = ItemSystem.PassiveSkills.Effects.DeathMaskVisor.CutNow(ctx, heavyNow);
                if (maskCut > 0f && totalDmg > 0)
                {
                    int reduced = (int)(totalDmg * maskCut);
                    totalDmg -= reduced;
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[死相の面頬] 会心したターン (練気 {ItemSystem.PassiveSkills.Effects.Crit.CritRenkiThisTurn(ctx)}{(heavyNow ? "・大技" : "")}): 敵の攻撃 −{maskCut:P0} (−{reduced}) → {totalDmg}");
                }
            }

            // 獣の絆: 被弾無効化チャージ消費
            if (ctx.playerDamageNegateCharges > 0 && totalDmg > 0)
            {
                ctx.playerDamageNegateCharges--;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] 獣の絆発動: 被弾{totalDmg}を無効化（残チャージ{ctx.playerDamageNegateCharges}）");
                totalDmg = 0;
            }

            // 消費: シールド吸収（残量から差し引き）
            int shieldAbsorbed = 0;
            if (ctx.consShield > 0 && totalDmg > 0)
            {
                // 吸収・割れの出来事を配る (2026-10-31)
                int absorbed = ItemSystem.PassiveSkills.Keywords.Kw.AbsorbShield(ctx, totalDmg, isAttack: true);
                totalDmg -= absorbed;
                shieldAbsorbed = absorbed;
                if (GuardDiag.InVesca()) GuardDiag.Cut7F[7] += absorbed;   // [計装 2026-11-11]
                ctx.dbgShieldToNormal += absorbed;   // [計装] チップ側との配分を見る
                ctx.shieldAbsorbedAttackThisTurn += absorbed;   // 灰燼の王の〈烈炎〉が読む (主攻撃の肩代わりだけ・2026-11-14)
                if (Debug.unityLogger.logEnabled) Debug.Log($"[CombatManager] シールド吸収 {absorbed} (残{ctx.consShield})");
            }

            // 〈シュヴァリエのレイピア〉のコントラタック (2026-11-20 作り直し): ブロックとシールドで防いだダメージの 2 倍を敵へ軽減不可で返す。
            //   旧: 被ダメ 50% 軽減 + 軽減した量 ×2 を返す
            if (GameLoop.Consumables.RapierContreOn(ctx))
            {
                int reflect = 2 * (Math.Max(0, blocked) + shieldAbsorbed);
                if (reflect > 0)
                {
                    HpLossDiag.Lose(ref enemyHP, reflect, "反射(コントラタック)");
                    if (Debug.unityLogger.logEnabled) Debug.Log($"[コントラタック] ブロック {blocked}・シールド {shieldAbsorbed} で防いだ → ×2 = {reflect} を返す → 敵HP{enemyHP}");
                }
            }

            // 2026-06-28: 痛覚遮断剤 (狂戦士スターター): 使用ターン中の致命ダメは HP=1 で踏みとどまる
            // (シールド/軽減すべての後の最終値で判定。 fixedDamageToPlayer 等の別経路は別途処理されるが
            //  メインダメで死なせない最低保証として機能)
            if (ctx != null && ctx.painkillerArmedThisTurn && totalDmg >= playerHP && playerHP > 0)
            {
                int clamp = playerHP - 1;
                if (Debug.unityLogger.logEnabled) Debug.Log($"[痛覚遮断剤] 致命ダメ {totalDmg} → {clamp} (HP=1 踏みとどまり)");
                totalDmg = clamp;
            }

            return totalDmg;
        }
    }
}
