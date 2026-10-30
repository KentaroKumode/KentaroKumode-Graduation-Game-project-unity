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
        /// 画竜点睛 → 与ダメ倍率 (パッシブ・メタ・遺物・研磨剤・鬼火の油を 1 つのプールで加算) → 攻撃バースト → メタ会心 → 向かい風
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
            // 画竜点睛: ダメージ＝(出目+10)×会心倍率、会心確定
            if (ctx.garyoProc)
            {
                totalDmg = Mathf.CeilToInt((ctx.garyoDieValue + 10) * ctx.criticalMultiplier);
                isCrit = true;
                Debug.Log($"[画竜点睛] 確定会心 {totalDmg} ダメ (出目{ctx.garyoDieValue}+10 ×{ctx.criticalMultiplier})");
            }

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
            // 無我無心 (rollPurity) 中は消費アイテム由来の補正を受けない規約に従い不発。
            if (ctx.polishArmed && !ctx.rollPurity)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += 1.5f;
                DmgSourceDiag.Note("消費〈瞬間研磨剤〉", DmgSourceDiag.Out, 1.5f);
                ctx.polishArmed = false;   // 1 撃限り
                if (CombatStats.DiagWinAttack) CombatStats.DamageStages[19]++;
                Debug.Log("[瞬間研磨剤] 与ダメージ+150%");
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
            if (ctx.outgoingDamageMultiplier > 0f
                && Mathf.Abs(ctx.outgoingDamageMultiplier - 1f) > 0.001f)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.outgoingDamageMultiplier);
                Debug.Log($"[CombatManager] 与ダメ補正 ×{ctx.outgoingDamageMultiplier:F2}: {orig}→{totalDmg}");
            }
            if (CombatStats.DiagWinAttack)
            {
                CombatStats.DamageStages[5] += totalDmg;   // 段 E
                CombatStats.DamageStages[22] += ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier;
            }

            // 消費: 攻撃力バースト（この勝利ターンのみ・単発消費）
            if (ctx.consAtkBurst > 0)
            {
                totalDmg += ctx.consAtkBurst;
                Debug.Log($"[CombatManager] 攻撃バースト +{ctx.consAtkBurst}");
                ctx.consAtkBurst = 0;
            }
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[6] += totalDmg;   // 段 F

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
            float metaDmgMul = MetaProgression.MetaDebuffApplicator.GetPlayerDamageMultiplier();
            if (metaDmgMul < 0.999f && totalDmg > 0)
                totalDmg = Mathf.Max(1, Mathf.RoundToInt(totalDmg * metaDmgMul));
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[7] += totalDmg;   // 段 G

            // 敵側のダメージ軽減パッシブを発火（前後で ctx.finalDamage と totalDmg を同期し、
            // 敵パッシブ（AshArmor 等やシュヴァリエのシールド経由処理）が
            // 実際の与ダメに反映されるようにする）
            ctx.finalDamage = totalDmg;
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreReceiveDamage);
            totalDmg = System.Math.Max(0, ctx.finalDamage);
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[8] += totalDmg;   // 段 H

            // 敵の防御値 (ADR-0013)。 敵の軽減パッシブ (灰塵の鎧 等) の後、 倍率が全部乗った最終値に掛ける。
            //   貫通は防御値を固定値で引く。 余った貫通は何にもならない
            //   (旧〈利刃〉の余剰貫通→与ダメ%転用は 2026-10-28 に撤去・GAME.md §24)。
            // 短剣〈急所〉: 会心の一撃は敵の防御を無視する (2026-10-28)。
            if (ctx.enemyDefense > 0 && totalDmg > 0 && isCrit
                && ItemSystem.PassiveSkills.Effects.VitalStrike.IgnoresDefense(ctx))
            {
                Debug.Log($"[急所] 会心 → 敵の防御{ctx.enemyDefense} を無視");
            }
            else if (ctx.enemyDefense > 0 && totalDmg > 0 && ctx.suppressedCritProc)
            {
                Debug.Log($"[研ぎ知らずの銑鉄棍] 会心の代わりに敵の防御{ctx.enemyDefense} を無視");
            }
            else if (ctx.enemyDefense > 0 && totalDmg > 0)
            {
                int effDef = DefenseFormula.Effective(ctx.enemyDefense, ctx.playerArmorPen);
                if (effDef > 0)
                {
                    int beforeDef = totalDmg;
                    totalDmg = DefenseFormula.Apply(totalDmg, effDef);
                    Debug.Log($"[敵の防御] 防御{ctx.enemyDefense}−貫通{ctx.playerArmorPen}→{effDef}"
                            + $"（軽減{DefenseFormula.Reduction(effDef):P0}）{beforeDef}→{totalDmg}");
                }
            }
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[9] += totalDmg;   // 段 I

            // 段 J (旧: 余剰貫通の与ダメ転用) は撤去。 段別実測の添字を保つため計測点だけ残す。
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[10] += totalDmg;   // 段 J

            // 段 K は撤去。 段別実測の添字を保つため計測点だけ残す。
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[11] += totalDmg;   // 段 K

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
                    Debug.Log($"[狩猟旅団] 脆弱発動 ×{vulMult:F2} {beforeVul}→{totalDmg}");
                    if (CombatStats.DiagWinAttack) CombatStats.DamageStages[20]++;
                }
            }
            else if (!isCrit && totalDmg > 0)
            {
                // ロール勝利時の非会心ダメで脆弱を再点火 (契約継続中のみ)
                VulnerabilityStatus.RearmOnNonCritWin(ctx);
            }
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[12] += totalDmg;   // 段 L

            // 勝利時の与ダメ最低保証（基本1、利刃Lvで1/2/3/4）。「勝ったのに0」を防止。
            if (totalDmg < ctx.winMinDamage)
                totalDmg = ctx.winMinDamage;
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[13] += totalDmg;   // 段 M

            // 狂暴化(ボス50T後): エネミーが受けるダメージを倍化（軽減処理の後・適用直前）
            if (ctx.enemyDamageTakenMultiplier > 1f && totalDmg > 0)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.enemyDamageTakenMultiplier);
                Debug.Log($"[CombatManager] 狂暴化: 敵被ダメ ×{ctx.enemyDamageTakenMultiplier:F1} ({orig}→{totalDmg})");
                if (CombatStats.DiagWinAttack) CombatStats.DamageStages[21]++;
            }
            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[14] += totalDmg;   // 段 N

            // 挑戦デバフ 軸7〈俊敏〉
            //   T1 以上: 各戦闘の最初の 1 回の被弾を必ず回避
            //   T2/T3  : さらに 2 回目以降も 20% / 40% で回避
            if ((totalDmg + fixedDmg) > 0 && MetaProgression.MetaDebuffApplicator.EnemyDodgesFirstHit())
            {
                bool dodged = false;
                if (!metaAgilityDodgeUsed)
                {
                    metaAgilityDodgeUsed = true;
                    dodged = true;
                    Debug.Log($"[俊敏] 初撃を回避 (与ダメ {totalDmg}+{fixedDmg} を無効化)");
                }
                else
                {
                    float p = MetaProgression.MetaDebuffApplicator.GetEnemyDodgeChance();
                    // 決定論のため GameRng を通す。 連番は戦闘通し番号 + ターンで一意 (RngIdx)。
                    if (p > 0f && GameLoop.GameRng.Value("meta.agilityDodge", RngIdx(7)) < p)
                    {
                        dodged = true;
                        Debug.Log($"[俊敏] 2回目以降の回避 ({p:P0}) (与ダメ {totalDmg}+{fixedDmg} を無効化)");
                    }
                }
                if (dodged) { totalDmg = 0; fixedDmg = 0; }
            }

            if (CombatStats.DiagWinAttack) CombatStats.DamageStages[15] += totalDmg;   // 段 O (最終)
            return totalDmg;
        }

        /// <summary>
        /// プレイヤー敗北時の被ダメージ修飾（ProcessDamage後〜プレイヤーHP適用前）。適用順:
        /// 天変地異(×2) → メタ被ダメ軽減 → 不屈の鎧/苦難の刻印 → 地獄門 → 亡者の招待(+30%)
        /// → 共助(T1半減) → 獣の絆 → コントラタック(50%軽減+反射) → 消費シールド → 鏡写し反射。
        /// enemyHP はメンバフィールドのため反射系はここで直接削る。
        /// </summary>
        private int ApplyLossDamageModifiers(int totalDmg, MapSystem.FloorModifier floorMod, CombatContext ctx)
        {
            // 挑戦デバフ〈狂暴化〉: 敵ダメージ倍率。
            //   〈天変地異〉のボス初撃強化は 2026-08-08 に廃止し、 エスカレーション閾値の
            //   前倒し (Escalation.StageOf) へ移した。 ボス戦のみ・最初の 3 撃という形は
            //   40 ターン級の戦闘では誤差にしかならず、 単独 T3 が実質無料だったため。
            float enemyMul = MetaProgression.MetaDebuffApplicator.GetEnemyDamageMultiplier();
            if (Mathf.Abs(enemyMul - 1f) > 0.001f)
                totalDmg = Mathf.CeilToInt(totalDmg * enemyMul);

            // 敵スタンス（ADR-0005）: 被ダメ倍率（高ダメ>1/低ダメ<1）。以降の軽減はスタンス後の値に効く。
            if (ctx != null && Mathf.Abs(ctx.enemyStanceDamageMult - 1f) > 0.001f && totalDmg > 0)
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.enemyStanceDamageMult);

            // (2026-06-01) ボス被ダメ倍率(dmgMul)の直接操作は廃止。 硬化/易化は Dice/HP/機構軸で行う。

            // [削除 2026-09-13] メタバフの定額被ダメ軽減 ── 2026-08-15 に割合軽減へ移して以降
            //   常に 0 を返す残骸だった。 現行は下の GetGuardDamageReductionPct の乗算段。

            // 名前付きパッシブ由来の固定被ダメ削減（不屈の鎧・苦難の刻印 等の合算）
            if (ctx.playerFlatDamageReduction > 0 && totalDmg > 0)
                totalDmg = Mathf.Max(1, totalDmg - ctx.playerFlatDamageReduction);

            // 遺物 (§15-5) 被ダメージ−N%。 **割合軽減**であって定額ではない。
            //   定額だと参照被ダメ 7.7/T に対し段6 が 78% 軽減になり、 序盤の敵 (baseAttack 2〜10) を
            //   丸ごと無力化していた。 割合なら敵が弱いほど軽減の絶対量も小さくなる (§24)。
            float relicRed = MetaProgression.Relics.RelicApplicator
                             .GetDamageReductionPct(GameLoop.GameManager.Instance?.Run, ctx);
            if (relicRed > 0f && totalDmg > 0)
                totalDmg = Mathf.Max(1, Mathf.CeilToInt(totalDmg * (1f - relicRed)));

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
                    Debug.Log($"[不抜の聖紋] HP 80% 割れにより破棄 (HP {playerHP}/{playerMaxHP})");
                }
                else if (totalDmg > 0)
                {
                    int reduced = Mathf.CeilToInt(totalDmg * 0.4f);
                    totalDmg = Mathf.Max(0, totalDmg - reduced);
                    Debug.Log($"[不抜の聖紋] 被ダメ -{reduced} → {totalDmg} (HP {playerHP}/{playerMaxHP})");
                    // この被ダメ適用後に 80% を割るなら次回以降は無効化
                    if ((playerHP - totalDmg) * 5 < playerMaxHP * 4)
                    {
                        ctx.oathBroken = true;
                        Debug.Log($"[不抜の聖紋] この被ダメで HP 80% 割れ予定 → 破棄");
                    }
                }
            }

            // フロアデバフ: 敗北時の被ダメージ軽減（5層 地獄門: -2）
            if (floorMod != null && floorMod.defeatDamageReduction > 0 && totalDmg > 0)
            {
                int reduced = Mathf.Min(totalDmg, floorMod.defeatDamageReduction);
                totalDmg -= reduced;
                Debug.Log($"[CombatManager] フロアデバフ: 敗北時被ダメ-{reduced} → {totalDmg}");
            }

            // 亡者の招待: 被ダメ +30%
            if (ctx.receivedDamageBonus > 0f && totalDmg > 0)
            {
                int bonusDmg = Mathf.CeilToInt(totalDmg * ctx.receivedDamageBonus);
                totalDmg += bonusDmg;
                Debug.Log($"[CombatManager] 亡者の招待: 被ダメ+{bonusDmg} (合計{totalDmg})");
            }

            // 共助: 1ターン目のメインダメージ半減
            if (ctx.halveFirstEnemyAttack && ctx.currentTurn == 1)
            {
                totalDmg = totalDmg / 2;
                ctx.halveFirstEnemyAttack = false;
                Debug.Log("[CombatManager] 共助発動: 敵の最初の攻撃を半減");
            }

            // 獣の絆: 被弾無効化チャージ消費
            if (ctx.playerDamageNegateCharges > 0 && totalDmg > 0)
            {
                ctx.playerDamageNegateCharges--;
                Debug.Log($"[CombatManager] 獣の絆発動: 被弾{totalDmg}を無効化（残チャージ{ctx.playerDamageNegateCharges}）");
                totalDmg = 0;
            }

            // シュヴァリエのレイピア コントラタック: 被ダメ50%軽減 + 軽減量×2 を敵へ反射
            if (ctx.GetAccumulated("player_contre") > 0 && totalDmg > 0)
            {
                int mitigated = totalDmg / 2;
                totalDmg -= mitigated;
                int reflect = mitigated * 2;
                if (reflect > 0)
                {
                    enemyHP = Math.Max(0, enemyHP - reflect);
                    Debug.Log($"[コントラタック] 軽減{mitigated} → 被ダメ{totalDmg + mitigated}→{totalDmg}、反射{reflect} → 敵HP{enemyHP}");
                }
            }

            // 消費: シールド吸収（残量から差し引き）
            if (ctx.consShield > 0 && totalDmg > 0)
            {
                // 吸収・割れの出来事を配る (2026-10-31)。 吸収に応じて品が敵へ返す分 (〈鏡返しの小盾〉) はここで使う。
                int absorbed = ItemSystem.PassiveSkills.Keywords.Kw.AbsorbShield(ctx, totalDmg, isAttack: true);
                totalDmg -= absorbed;
                ctx.dbgShieldToNormal += absorbed;   // [計装] チップ側との配分を見る
                Debug.Log($"[CombatManager] シールド吸収 {absorbed} (残{ctx.consShield})");
                int reflect = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.ReflectKey);
                if (reflect > 0)
                {
                    enemyHP = Math.Max(0, enemyHP - reflect);
                    ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.ReflectKey] = 0;
                    Debug.Log($"[シールドの返し] {reflect} → 敵HP {enemyHP}");
                }
            }

            // 消費: 鏡写しの水晶（吸収後の実被ダメと同量を敵へ反射）
            if (ctx.consReflect && totalDmg > 0)
            {
                enemyHP = Math.Max(0, enemyHP - totalDmg);
                Debug.Log($"[CombatManager] 鏡写し反射 {totalDmg} → 敵HP {enemyHP}");
            }

            // 〈虚空〉の殻: 攻撃端子 0 本のターンだけ立つ。 積むほど薄くなり n=6 で消える。
            // **下限を 0 にしないので完全無敵にはならない** (旧 nullifyAllDamage の抜け穴対策)。
            if (ctx != null && ctx.voidStanceDamageMul < 1f && totalDmg > 0)
            {
                int before = totalDmg;
                totalDmg = Mathf.Max(1, Mathf.CeilToInt(totalDmg * ctx.voidStanceDamageMul));
                Debug.Log($"[虚空] 殻が受け止めた {before}→{totalDmg} (×{ctx.voidStanceDamageMul:F2})");
            }

            // 2026-06-28: 痛覚遮断剤 (狂戦士スターター): 使用ターン中の致命ダメは HP=1 で踏みとどまる
            // (シールド/軽減すべての後の最終値で判定。 fixedDamageToPlayer 等の別経路は別途処理されるが
            //  メインダメで死なせない最低保証として機能)
            if (ctx != null && ctx.painkillerArmedThisTurn && totalDmg >= playerHP && playerHP > 0)
            {
                int clamp = playerHP - 1;
                Debug.Log($"[痛覚遮断剤] 致命ダメ {totalDmg} → {clamp} (HP=1 踏みとどまり)");
                totalDmg = clamp;
            }

            return totalDmg;
        }
    }
}
