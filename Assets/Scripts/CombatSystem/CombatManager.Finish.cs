using System;
using System.Collections.Generic;
using UnityEngine;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>CombatManager: 戦闘の一括実行と終了処理 (FinishCombat・結果の組み立て)。</summary>
    public partial class CombatManager
    {
        /// <summary>
        /// 決着がつくまで自動でターンを回す
        /// </summary>
        public CombatResult ExecuteFullCombat()
        {
            int maxTurns = 100; // 無限ループ防止
            int turn = 0;
            while (isCombatActive && turn < maxTurns)
            {
                ExecuteTurn();
                turn++;
            }

            if (isCombatActive)
            {
                Debug.LogWarning("[CombatManager] Combat exceeded max turns, forcing end.");
                FinishCombat();
            }

            return GetLastCombatResult();
        }

        // ===========================================================
        //  戦闘終了
        // ===========================================================

        /// <summary>仕込み刃の反撃倍率 (2026-09-22: 1 → 5)。 無効化した被ダメの何倍を軽減不可で返すか。</summary>
        public const int DaggerReflectMultiplier = 5;

        private void FinishCombat()
        {
            // **冪等にする** (2026-09-20)。 呼び出し口が 7 箇所あり、 どれも
            //   「終わったから閉じる」を独立に判断している。 二度閉じると
            //   <b>計装だけが二重に積まれる</b> ── 戦闘側の死亡数がラン側より 10.3% 多い、
            //   という形で現れた (実測 10,000 ラン: 戦闘側 9,478 / ラン側 8,591)。
            //   `isCombatActive = false` は元からこの関数の末尾にあるので、 それを入口の番人にする。
            if (!isCombatActive) { SkillDiag.FinishCombatReentry++; return; }

            // [計装] 対象層のボス戦の締め。 既定は無効。
            CombatSystem.BossCombatTrace.EndFight(enemyHP <= 0 && playerHP > 0,
                PassiveSkillManager.Instance?.Context?.currentTurn ?? 0);
            SkillDiag.Note(_diagKind, playerHP <= 0, PassiveSkillManager.Instance?.Context?.currentTurn ?? 0,
                           _diagStartHp, playerHP, _diagStartMaxHp,
                           FloorStatIndex());
            if (_diagKind == SkillDiag.Boss)
            {
                SkillDiag.NoteBoss(currentEnemy?.id, PassiveSkillManager.Instance?.Context?.currentTurn ?? 0, playerHP <= 0);
                if (currentEnemy != null)
                {
                    var bd = SkillDiag.BossDmgOf(currentEnemy.id);
                    bd[0] += 1; bd[1] += currentEnemy.maxHP; bd[6] += Math.Max(0, enemyHP);
                    if (enemyHP <= 0) bd[8] += 1;
                }
            }

            // 戦闘終了系時限効果（泉の祝福、芽吹きの祈り等）+ ロール/ターン系の消費
            var psmCtx = PassiveSkillManager.Instance?.Context;
            EventSystem.TimedEffects.TimedEffectManager.OnCombatEnd(
                psmCtx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（戦闘終了時系: 巡礼者の杖、希望の灯片）
            ItemSystem.PassiveItems.PassiveItemManager.OnCombatEnd(
                psmCtx, GameLoop.GameManager.Instance?.Run, this);

            // 2026-07-25 v6: 臨界 r2 保温炉 - メーターの N% を次戦闘へ持ち越し
            {
                var runEnd = GameLoop.GameManager.Instance?.Run;
                float ratio = MetaProgression.MetaBuffApplicator.GetRinkaiCarryoverRatio();
                if (runEnd != null && psmCtx != null && ratio > 0f && psmCtx.rinkaiMeter > 0)
                {
                    int carry = Mathf.FloorToInt(psmCtx.rinkaiMeter * ratio);
                    if (carry > 0)
                    {
                        // 〈朝にも熱い竈〉も同じ枠へ持ち越す (OnBattleEnd)。 大きい方を残す。
                        runEnd.pendingRinkaiCarryover = Math.Max(runEnd.pendingRinkaiCarryover, carry);
                        Debug.Log($"[MetaBuff] 臨界保温炉: メーター {psmCtx.rinkaiMeter} × {ratio:P0} → 次戦闘へ {carry}");
                    }
                }
            }

            // 移動で貯まる品の歩数は戦闘ごとに数え直す (2026-10-31)
            {
                var runSteps = GameLoop.GameManager.Instance?.Run;
                if (runSteps != null) runSteps.stepsSinceBattle = 0;
            }

            isCombatActive = false;

            // 挑戦デバフ〈長引く負傷〉(2026-08-10 リワーク): **ラン中の累計被ダメの N% だけ最大HP を失う**。
            //   蓄積量に割合を掛けた整数部が増えるたび、 その差分を最大HP から引く
            //   (例: 5% なら累計 20 ダメごとに −1)。 閾値も回数も持たないので、
            //   「削られながら勝つ」を繰り返すほど確実に効く ── 旧実装が死んでいた理由の裏返し。
            //
            //   **上限を設けない。** 旧実装の累計上限 20 は、 閾値が現在最大HP 比だったころの
            //   自己加速 (削れる→閾値が下がる→さらに削れる) を止めるためのもの。 新方式は
            //   絶対量の累計に比例するだけで自己加速しないので、 上限は意味を持たない。
            {
                float lwRatio = MetaProgression.MetaDebuffApplicator.GetLingeringWoundRatio();
                var lwRun = GameLoop.GameManager.Instance?.Run;
                if (lwRatio > 0f && playerHP > 0 && lwRun != null)
                {
                    int target = Mathf.FloorToInt(lwRun.lingeringWoundDamageAccum * lwRatio);
                    int delta = target - lwRun.lingeringWoundLost;
                    if (delta > 0)
                    {
                        lwRun.lingeringWoundLost = target;
                        lwRun.lingeringWoundTriggers++;
                        lwRun.playerMaxHP = Math.Max(1, lwRun.playerMaxHP - delta);
                        lwRun.playerHP = Math.Min(lwRun.playerHP, lwRun.playerMaxHP);
                        Debug.Log($"[長引く負傷] 累計被ダメ {lwRun.lingeringWoundDamageAccum} × {lwRatio:P0}"
                                + $" → 最大HP -{delta} (累計 -{lwRun.lingeringWoundLost}) → {lwRun.playerMaxHP}");
                    }
                }
            }


            var result = GetLastCombatResult();
            // **EndCombat() は context を null にする。** 計装の flush はその後なので、
            //   ここで参照を掴んでおく (掴まないと黙って 0 件が積まれる)。
            var ctxAtEnd = PassiveSkillManager.Instance?.Context;
            // [計装] 最終段を締める。 SwapEnemy は「次段へ移るとき」しか呼ばれないので、
            //   戦闘が終わった段 (勝っても負けても) はここでしか記録できない。
            if (ctxAtEnd != null && !string.IsNullOrEmpty(ctxAtEnd.bossId)
                && ctxAtEnd.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix))
            {
                GameLoop.RunChronicle.NotePhaseDuration(
                    ctxAtEnd.bossId.EndsWith("_p4") ? 4 : ctxAtEnd.bossId.EndsWith("_p3") ? 3
                        : ctxAtEnd.bossId.EndsWith("_p2") ? 2 : 1,
                    ctxAtEnd.phaseStartTurn, ctxAtEnd.currentTurn);
            }
            MetaProgression.Achievements.AchievementService.NoteCombatEnded(
                GameLoop.GameManager.Instance?.Run, result, ctxAtEnd);
            PassiveSkillManager.Instance.EndCombat();

            // [廃止] 旅団契約の戦闘フック一式 (2026-08-11 にシステムごと削除)。
            //   戦闘開始/ターン終了/戦闘終了 hook・影武者の致死復活・HP20% 解除判定。
            {
                var runRef = GameLoop.GameManager.Instance?.Run;
                if (runRef != null) playerHP = runRef.playerHP;
            }

            // 行動台帳 (RunChronicle): 戦闘 1 件を 1 行積む。
            //   **ここが唯一の記録点にしてある** ── 実プレイも BOT も FinishCombat を通るので、
            //   同じ定義のコード列が両方から出る (＝ BOT のバッチがそのまま計装になる)。
            //   分類は RunChronicle.ClassifyCombat 側に置く。 閾値を 2 箇所に分けると
            //   サマリの緊張感曲線 (knife ≤20% / blowout >80%) と定義が食い違う。
            {
                var chRun = GameLoop.GameManager.Instance?.Run;
                if (chRun != null && currentEnemy != null)
                {
                    // **`playerHP` を読んではいけない。** 直前のブロックで `run.playerHP` から
                    //   上書きされており、 戦闘終了時点の値ではなくなっている
                    //   （敗北なのに残 HP 56% / 被ダメ 107% という矛盾した記録が出た）。
                    //   `result` は上書き前の GetLastCombatResult() で確定しているのでそちらを使う。
                    int endHp = Math.Max(0, result.playerHPRemaining);
                    int endPct = this.playerMaxHP > 0
                               ? Mathf.RoundToInt(100f * endHp / this.playerMaxHP)
                               : 0;
                    // **この戦闘で受けた量**: 開始HP − 終了HP + 戦闘中に入れた回復。
                    //   回復を足さないと、 削られてから回復した戦闘が「無傷」に化ける。
                    int taken = Math.Max(0, _hpAtCombatStart - endHp + result.healApplied);
                    int takenPct = this.playerMaxHP > 0
                                 ? Mathf.RoundToInt(100f * taken / this.playerMaxHP)
                                 : 0;
                    // 7層の内訳を締める。 **[18] は最大値なので差分ではなく max を採る。**
                    if (_bd7F)
                    {
                        for (int i = 0; i < CombatStats.DamageBreakdown.Length; i++)
                            CombatStats.DamageBreakdown7F[i] += CombatStats.DamageBreakdown[i] - _bdAtCombatStart[i];
                        CombatStats.DamageBreakdown7F[18] = Math.Max(CombatStats.DamageBreakdown7F[18], CombatStats.DamageBreakdown[18]);
                        _bd7F = false;
                    }
                    YachtRoleEffects.FlushByDice(chRun.equippedDiceId);
                    GameLoop.RunChronicle.HealInCombat += Math.Max(0, result.healApplied);
                    GameLoop.RunChronicle.ShieldGained += Math.Max(0, result.shieldGained);
                    // [計装] 盾がチップと通常攻撃のどちらに使われ、 何点余ったか。
                    //   ctx は戦闘毎に作り直されるので、 ここが唯一の flush 点。
                    // 防御 r10 (極点): 残ったシールドの半分を次戦へ持ち越す。 **敗北時は持ち越さない**
                    //   (ランが終わるので意味が無い)。 ctx は戦闘毎に作り直されるのでここが唯一の保存点。
                    if (ctxAtEnd != null && result.playerWon
                        && MetaProgression.MetaBuffApplicator.IsShieldCarryUnlocked())
                    {
                        // **2026-09-13: 持ち越すのは残量の 30% (切り上げ)。** 全量 → 50% → 30%。
                        //   50% でも 防御 r10 が Balanced +6.31pt で基準帯を超えていた。
                        chRun.carriedShield = Mathf.CeilToInt(Math.Max(0, ctxAtEnd.consShield) * 0.30f);
                        if (chRun.carriedShield > 0)
                            Debug.Log($"[MetaBuff] シールド持ち越し保存 {chRun.carriedShield} (防御r10)");
                    }
                    if (ctxAtEnd != null)
                        GameLoop.RunChronicle.NoteShieldSplit(
                            currentEnemy.id == GameLoop.BossIds.Layer5Judgment,
                            ctxAtEnd.dbgShieldToChip, ctxAtEnd.dbgShieldToNormal,
                            Math.Max(0, ctxAtEnd.consShield),
                            ctxAtEnd.dbgChipRaw, ctxAtEnd.dbgChipToHp,
                            !result.playerWon, ctxAtEnd.playerDamageThisTurn,
                            Math.Max(0, ctxAtEnd.consShield));
                    GameLoop.RunChronicle.Combat(chRun, currentEnemy.id,
                        GameLoop.BossIds.IsBoss(currentEnemy.id),
                        result.playerWon, result.totalTurns, _hpPctAtCombatStart, endPct, takenPct);
                }
            }

            // **終了通知が誰にも届かない状態を黙って通さない。**
            //   購読者が居ないと戦闘だけ終わってフェーズが Combat のまま残り、
            //   呼び出し側は「戦闘中なのに戦闘が動かない」無限ループに落ちる。
            //   ストール検出は「phase=Combat」としか言わないので、 ここで名指ししないと追えない。
            if (OnCombatEnd == null)
                Debug.LogError("[CombatManager] 戦闘終了イベントの購読者が居ない "
                    + "── フェーズが進まず停止する (GameManager.Start の購読が外れている)");
            OnCombatEnd?.Invoke(result);

            // 通知した結果フェーズが動いたか。 動いていなければ購読者側で止まっている。
            {
                var gmPhase = GameLoop.GameManager.Instance?.CurrentPhase;
                if (gmPhase == GameLoop.GameManager.GamePhase.Combat)
                    Debug.LogError("[CombatManager] 戦闘終了を通知したのにフェーズが Combat のまま "
                        + $"(敵={currentEnemy?.id} 勝敗={(result.playerWon ? "勝" : "敗")} "
                        + $"{result.totalTurns}T)");
            }

            Debug.Log($"[CombatManager] ===== COMBAT END =====");
            Debug.Log($"  Result: {(result.playerWon ? "PLAYER WIN" : "PLAYER LOSE")}");
            Debug.Log($"  Player HP: {playerHP}/{playerMaxHP}");
            Debug.Log($"  Enemy  HP: {enemyHP}/{currentEnemy.maxHP}");
            Debug.Log($"  Total Turns: {result.totalTurns}");
        }

        private CombatResult GetLastCombatResult()
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            return new CombatResult
            {
                enemyId = currentEnemy.id,
                enemyDisplayName = currentEnemy.displayName,
                playerWon = playerHP > 0 && enemyHP <= 0,
                playerFled = LastCombatFled,
                totalTurns = turnLog.Count,
                playerHPRemaining = playerHP,
                enemyHPRemaining = enemyHP,
                turnLog = new List<TurnResult>(turnLog),
                healApplied = ctx?.healAppliedTotal ?? 0,
                shieldGained = ctx?.shieldGainedTotal ?? 0,
                damageDealt = Math.Max(0, (currentEnemy != null ? currentEnemy.maxHP : 0) - Math.Max(0, enemyHP)),
                damageTaken = Math.Max(0, playerMaxHP - Math.Max(0, playerHP)) + (ctx?.healAppliedTotal ?? 0),
                enemyMaxHP = currentEnemy != null ? currentEnemy.maxHP : 0,
                // プレイヤー敗北時のみ死因を記録 (勝利時は Normal)
                deathCause = (playerHP <= 0 && ctx != null) ? ctx.lastDamageCause
                           : ItemSystem.PassiveSkills.DeathCause.Normal,
                playerRollSum = _fightPlayerRollSum,
                playerRollCount = _fightPlayerRollCount,
                playerDamageBySource = BuildDamageBreakdown(ctx),
                strongRollTurns = _fightStrongTurns,
                strongRollBossWins = _fightStrongBossWins,
                weakRollTurns = _fightWeakTurns,
                weakRollBossWins = _fightWeakBossWins,
            };
        }

        /// <summary>被ダメ ソース別内訳のスナップショット (明示帰属のみ)。 メイン被ダメ(ロール敗北/断罪増幅)と
        /// 特殊スキル(審判の炎/毒/反射/王の業炎)が各自計上済み。 heal 込みのグロスで残差を取ると heal 分が
        /// Normal を過大計上するため、 残差寄せはしない。 ※診断対象の1〜6層は実質これで全被ダメを網羅。</summary>
        private Dictionary<ItemSystem.PassiveSkills.DeathCause, int> BuildDamageBreakdown(CombatContext ctx)
        {
            var map = new Dictionary<ItemSystem.PassiveSkills.DeathCause, int>();
            if (ctx == null) return map;
            foreach (var kv in ctx.playerDamageBySource) map[kv.Key] = kv.Value;
            return map;
        }
    }
}
