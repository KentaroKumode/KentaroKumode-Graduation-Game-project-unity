using System;
using System.Collections.Generic;
using UnityEngine;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>CombatManager: 1 ターンの解決 (ADR-0009 相互攻撃パイプライン・ターン終端の共通処理・ヴェスカの防御)。</summary>
    public partial class CombatManager
    {
        /// <summary>ADR-0009 柱5: 充電ゲージ最大値。
        /// 過充電判定 = charge >= ChargeMax。
        ///
        /// **2026-08-09 に 10 → 30** (ADR-0010)。 充電がリロールの原資も兼ねるようになり、
        /// 供給 (充電端子 2 本で +7/ターン程度) に対して 10 では窮屈すぎるため。</summary>
        public const int ChargeMax = 30;

        /// <summary>配線ポリシー注入点 (完全情報: 自出目+テレグラフ → 配線)。
        /// null なら前ターン配線を維持 (初期値は全ダイス攻撃 = 未配置の自動合流)。
        /// AutoRunner / 配線 UI (ADR-0008 W5) がここへ差す。</summary>
        public Func<int[], MutualTurnTelegraph, WiringPlan> WiringPolicy;

        /// <summary>ADR-0010: リロール方策の注入点。
        /// 引数 = (現在の出目, テレグラフ, そのターンで何回目の振り直しか 1..)。
        /// 戻り値 = 振り直すダイスの index 配列。 null / 空なら振り直しを打ち切る。
        ///
        /// **コストは `個数 × 回数`** で逓増する。 払えなければそこで打ち切られるので、
        /// 「全部振り直しを 2 回」は上限 30 では不可能 ── 狙った少数を刻む方が効率的になる。</summary>
        public Func<int[], MutualTurnTelegraph, int, int[]> RerollPolicy;

        // (ADR-0011: 役は成立すれば自動で効く。 発動方策の注入点 RolePolicy は廃止した。)

        /// <summary>現在の配線 (ダイス index → 端子)。「配線は残る」= 戦闘中は自動適用。
        /// TODO(W7): ラン跨ぎの既定値保存は RunState へ (ADR-0008 残タスク #5)。</summary>
        private DiceTerminal[] mutualWiring;

        /// <summary>1 ターンを実行して結果を返す (ADR-0009 相互攻撃パイプライン)。</summary>
        public TurnResult ExecuteTurn()
        {
            if (!isCombatActive)
            {
                Debug.LogError("[CombatManager] No active combat!");
                return default;
            }
            if (_activeTurn != null)
            {
                Debug.LogError("[CombatManager] 人が操作するターンの判断待ち ── AnswerReroll / AnswerWiring で進めること");
                return default;
            }

            return ExecuteTurnMutual();
        }

        /// <summary>
        /// ターン終端の共通処理（ExecuteTurnMutual の後段）。
        /// 死の宣告 → HP同期 → 致死確定/影武者蘇生 → OnTurnEnd 系 →
        /// 敵スワップ → 時限効果 → フロアデバフ → 継続回復/シールド失効 →
        /// サドンデス決着 → 致死回復無効化 → 戦闘終了チェック。順序厳守。
        /// </summary>
        private TurnResult FinishTurnCommon(TurnResult result, CombatContext ctx)
        {
            var psm = PassiveSkillManager.Instance;
            var floorMod = GameLoop.GameManager.Instance?.ActiveModifier;

            // 死の宣告チェック（敵スキル由来の即死ダメージ）
            if (ctx.fixedDamageToPlayer >= 999)
                playerHP = 0;

            // コンテキストにHP同期
            ctx.playerCurrentHP = playerHP;
            ctx.playerMaxHP = playerMaxHP;
            ctx.enemyCurrentHP = enemyHP;
            ctx.enemyMaxHP = currentEnemy.maxHP;

            // ラストスタンド: **combatLethalThisTurn の確定より前**。 ここを後ろへ動かすと
            //   「致死が確定した後で HP を戻す」形になり、 蘇生禁止の判定と噛み合わない。
            TryLastStandRevive(ctx);

            // 戦闘ダメージ（メイン/固定/scratch/死の宣告）でこのターンに致死へ至ったか。
            // これ以降のターン終了回復(活力/継続回復/剣鎧等)で蘇生させないための確定フラグ。
            // 天命/深淵は被ダメを上限化してHPを1〜2残すため（HP=0にならず）ここでは false。
            bool combatLethalThisTurn = playerHP <= 0;

            // ターン終了トリガー
            psm.FireTrigger(PassiveSkillTrigger.OnTurnEnd);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnEnd);

            // ヴェスカ連戦: 敵パッシブが予約した SwapEnemy を perspective 復帰後に実行
            if (!string.IsNullOrEmpty(ctx.pendingEnemySwapId))
            {
                string swapId = ctx.pendingEnemySwapId;
                string swapLabel = ctx.pendingEnemySwapLabel;
                ctx.pendingEnemySwapId = null;
                ctx.pendingEnemySwapLabel = null;
                SwapEnemy(swapId, swapLabel);   // 計測は SwapEnemy 内で一元化
                // 敵HP は SwapEnemy で新形態の MaxHP に再設定済み。
                // 戦闘継続のため enemyHP <= 0 判定を回避する目的で SyncHP しなおす
                SyncHPFromContext(ctx);
            }

            // (〈不完全な修復〉の充電半減は 2026-09-14 に撤去。 Optimal 方策は充電を
            //   リロールにしか使っておらず、 半減しても毎ターン 1 回は回せるので効かなかった。
            //   罰は回復量へ移した ── GateFlaws.ApplyRepairPenalty / HealPlayer。)

            // ターン終了系時限効果（中毒等。適用のみ、消費は戦闘終了時）
            EventSystem.TimedEffects.TimedEffectManager.OnTurnEnd(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（ターン終了時系。現状は該当なし、フック確保のため呼び出し）
            ItemSystem.PassiveItems.PassiveItemManager.OnTurnEnd(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 〈不完全な修復〉(門・2026-09-14): 縁の直っていない身体は長く保たない。
            //   **軽減不可・毎ターン終了時**。 回復半減だけでは 1.3pt しか効かず
            //   (最終戦は回復薬で支える構造ではない)、 時間を軸にした罰へ差し替えた。
            {
                var drainRun = GameLoop.GameManager.Instance?.Run;
                int drain = GameLoop.GateFlaws.RepairDrainAmount(drainRun, playerMaxHP);
                if (drain > 0 && playerHP > 0)
                {
                    playerHP = Math.Max(0, playerHP - drain);
                    ctx.playerCurrentHP = playerHP;
                    Debug.Log($"[不完全な修復] 身体が保たない -{drain} (HP: {playerHP}/{playerMaxHP})");
                }
            }

            // フロアデバフ: 毎ターン自傷（6層 深淵の洗礼: -1）
            if (floorMod != null && floorMod.perTurnSelfDamage > 0 && playerHP > 0)
            {
                int dmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    floorMod.perTurnSelfDamage, GameLoop.GameManager.Instance?.Run, playerHP);
                playerHP = Math.Max(0, playerHP - dmg);
                ctx.playerCurrentHP = playerHP;
                Debug.Log($"[CombatManager] フロアデバフ: 自傷-{dmg} (HP: {playerHP}/{playerMaxHP})");
            }

            // ターン終了スキルによるHP変動をCombatManagerに反映（剣鎧等）
            SyncHPFromContext(ctx);

            // 消費: 継続回復（毎ターン終了時 +consRegen → consRegen--）。狂暴化中は封印。
            if (ctx.consRegen > 0 && playerHP > 0 && !ctx.healBlocked)
            {
                int regen = Math.Max(0, ctx.consRegen - ctx.healShieldReduction); // 天衣無縫減衰
                int heal = Math.Min(playerMaxHP - playerHP, regen);
                if (heal > 0) { playerHP += heal; ctx.playerCurrentHP = playerHP; ctx.healAppliedTotal += heal; }
                Debug.Log($"[CombatManager] 継続回復 +{heal} (次T {ctx.consRegen - 1})");
                ctx.consRegen--;
            }
            // 消費: シールド残ターン減算（-1=無制限 / 0で失効）
            if (ctx.consShieldExpireTurn > 0)
            {
                ctx.consShieldExpireTurn--;
                if (ctx.consShieldExpireTurn == 0)
                {
                    ctx.consShield = 0;
                    Debug.Log("[CombatManager] シールド失効");
                }
            }
            // 2026-07-21 消費: 攻撃バフ (cons_atk_*) の残ターン減算
            if (ctx.consDiceRollTurnsLeft > 0)
            {
                ctx.consDiceRollTurnsLeft--;
                if (ctx.consDiceRollTurnsLeft == 0)
                {
                    ctx.consDiceRoll = 0;
                    Debug.Log("[CombatManager] 攻撃バフ失効");
                }
            }
            // 2026-07-21 消費: 与ダメ倍率 (cons_dmg_*) の残ターン減算
            if (ctx.consDmgMultTurnsLeft > 0)
            {
                ctx.consDmgMultTurnsLeft--;
                if (ctx.consDmgMultTurnsLeft == 0)
                {
                    ctx.consDmgMultPct = 0;
                    Debug.Log("[CombatManager] 与ダメ倍率失効");
                }
            }

            result.playerHPAfter = playerHP;
            result.enemyHPAfter = enemyHP;
            turnLog.Add(result);

            OnTurnEnd?.Invoke(result);
            MetaProgression.Achievements.AchievementService.NoteTurnEnded(result);

            // ログ出力
            LogTurnResult(result);

            // 戦闘ダメージで致死に至っていたら、ターン終了回復で蘇生していても死亡を確定させる
            // （オーバーキル消失バグの修正：致死ダメージは回復で帳消しにできない）。
            if (combatLethalThisTurn && playerHP > 0)
            {
                Debug.Log($"[CombatManager] 戦闘致死確定: ターン終了回復による蘇生を無効化 (HP {playerHP}→0)");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                result.playerHPAfter = 0;
            }

            // 2026-06-28: 痛覚遮断剤 (狂戦士スターター): proc 後の攻撃終了時に死亡確定
            if (ctx.painkillerProcced && playerHP > 0)
            {
                Debug.Log($"[痛覚遮断剤] 強化攻撃終了 → 死亡 (HP {playerHP}→0)");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                ctx.painkillerProcced = false;
                result.playerHPAfter = 0;
            }

            // 7層ヴェスカ連戦: 敵 HP が 0 なら **戦闘終了より先に**次形態へ移す。
            //   〈連続実験〉は OnTurnEnd でしか発火しないため、 削り切りが OnTurnEnd より前に
            //   起きたターンは段移行が予約されず、 p1〜p3 を倒した時点で「7層クリア」に
            //   なっていた (実測: 完全クリア 197 件のうち 93 件 = 47% が p1〜p3 撃破)。
            //   固定ダメージ・虚空の刻み・反射など、 敵パッシブを経由しない撃破経路が複数あるため、
            //   パッシブの発火順に頼らずここで確定させる。
            if (enemyHP <= 0 && playerHP > 0) TryAdvanceVescaChain();

            // 2 体戦の締め。 SetSecondaryEnemy の不変条件 (2 体目の HP ≤ 1 体目) が成り立つ限り
            //   2 体目は必ず先に落ちているので、 ここは到達しない。 到達したら不変条件が破れている。
            if (enemyHP <= 0 && secondaryHP > 0)
            {
                Debug.LogWarning($"[2体戦] 不変条件違反: 1体目撃破時に2体目が残 {secondaryHP} ── 強制決着");
                secondaryHP = 0;
            }

            // 戦闘終了チェック
            if (playerHP <= 0 || enemyHP <= 0)
            {
                FinishCombat();
            }

            return result;
        }

        /// <summary>
        /// <summary>7 層ヴェスカの防御層。 正本: docs/GAME.md §13-4。
        ///
        /// 順序は **回避 → シールド吸収**。
        ///   - 〈解析演算〉の回避は **通常・固定・軽減不可を問わず全ダメージを消す**
        ///     （既存のメタ俊敏回避 EnemyDodgesFirstHit と同じ挙動に揃えてある）。
        ///   - シールドは通常ダメージから先に吸い、 余れば固定ダメージも吸う。
        ///     吸収量は反射（返しの盾/不落の城盾）の原資として記録する。
        /// </summary>
        private void ApplyVescaDefense(ItemSystem.PassiveSkills.CombatContext ctx,
                                       ref int dealtDmg, ref int dealtFixed)
        {
            if (ctx == null) return;

            // --- 解析演算: 回避 ---
            if (ctx.enemyDodgeChance > 0f && ctx.enemyDodgeCooldown <= 0
                && (dealtDmg > 0 || dealtFixed > 0)
                && GameLoop.GameRng.Chance(ctx.enemyDodgeChance, "combat.dodge", RngIdx(3)))
            {
                Debug.Log($"[解析演算] 回避 {ctx.enemyDodgeChance:P0} 成功 — 与ダメ {dealtDmg}+{dealtFixed} を無効化"
                        + " (次ターンは回避不能)");
                dealtDmg = 0; dealtFixed = 0;
                ctx.enemyDodgeCooldown = 2;   // 今ターン分 + 次ターン分
                return; // 回避したのでシールドは減らない
            }

            // --- 天与の盾: このターンの与ダメは半分 ---
            //   完全無効だと攻撃端子が死に札になり、 手番ごと消えるのと変わらなかった。
            //   半減なら「それでも押し切る」判断が残る。 端数は切り上げ (0 にはしない)。
            if (ctx.vescaHalvesDamageThisTurn && (dealtDmg > 0 || dealtFixed > 0))
            {
                int beforeDmg = dealtDmg, beforeFixed = dealtFixed;
                dealtDmg   = (dealtDmg   + 1) / 2;
                dealtFixed = (dealtFixed + 1) / 2;
                Debug.Log($"[天与の盾] 与ダメ {beforeDmg}+{beforeFixed} → {dealtDmg}+{dealtFixed} (半減)");
            }

            // --- シールド吸収 ---
            if (ctx.vescaShield <= 0) return;
            int absorbed = 0;
            int take = Math.Min(ctx.vescaShield, dealtDmg);
            dealtDmg -= take; ctx.vescaShield -= take; absorbed += take;
            if (ctx.vescaShield > 0 && dealtFixed > 0)
            {
                int takeF = Math.Min(ctx.vescaShield, dealtFixed);
                dealtFixed -= takeF; ctx.vescaShield -= takeF; absorbed += takeF;
            }
            if (absorbed > 0)
            {
                ctx.vescaShieldAbsorbedThisTurn += absorbed;
                Debug.Log($"[ヴェスカ] シールドが {absorbed} 吸収 (残 {ctx.vescaShield})");
            }
        }

        /// <summary>7 層ヴェスカの攻撃時効果。 敵攻撃の解決直後に呼ぶ。 正本: docs/GAME.md §13-4。
        ///   - 〈裂傷の刃心/殺戮〉大出血の付与。 **解除不可・減衰しない・4 連戦を跨ぐ**ため、
        ///     実際の毎ターンダメージは RelicScholar の OnTurnStart 側で処理する。
        ///   - 〈天与の剣〉最大 HP の削り。
        ///   - 〈処刑人の烙印〉攻撃終了時に HP が最大の 20% 以下なら 9999 の軽減不可ダメージ。
        /// </summary>
        private void ApplyVescaOnHitEffects(ItemSystem.PassiveSkills.CombatContext ctx, int takenApplied)
        {
            if (ctx == null) return;

            // **付随する新規状態異常は「実際に通ったか」で判定する**。
            //   パリィ廃止 (ADR-0010) 前は PERFECT の受け流しを別枠で見ていたが、
            //   役の〈大階〉〈相殺〉は被ダメを 0 にするので takenApplied == 0 で自然に弾かれる。
            //   既に溜まったスタックの tick と〈裂け目〉の自壊は通常どおり進む。
            if (ctx.vescaBleedOnHit > 0 && takenApplied > 0)
            {
                ctx.massiveBleedStacks += ctx.vescaBleedOnHit;
                Debug.Log($"[大出血] +{ctx.vescaBleedOnHit} (計 {ctx.massiveBleedStacks}・解除不可/減衰なし)");
            }

            if (ctx.vescaMaxHpBiteOnHit > 0 && takenApplied > 0)
            {
                int bite = ctx.vescaMaxHpBiteOnHit;
                playerMaxHP = Math.Max(1, playerMaxHP - bite);
                playerHP = Math.Min(playerHP, playerMaxHP);
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
                var run = GameLoop.GameManager.Instance?.Run;
                if (run != null) { run.playerMaxHP = playerMaxHP; run.playerHP = playerHP; }
                Debug.Log($"[天与の剣] 最大HP -{bite} → {playerMaxHP}");
            }

            // 処刑は「攻撃終了時」判定なので、 このターンに被弾していなくても閾値を割っていれば発動する。
            if (ctx.vescaExecuteArmed && playerHP > 0
                && playerHP <= Mathf.CeilToInt(playerMaxHP * 0.20f))
            {
                Debug.Log($"[処刑人の烙印] HP {playerHP}/{playerMaxHP} が 20% 以下 — 9999 の軽減不可ダメージ");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                // 通常被ダメと区別する (§13-3 の死因診断は「総被ダメに占める支配率」で本命レバーを選ぶため、
                // 処刑を Normal に混ぜると敵攻撃値のレバーへ誤誘導される)
                ctx.lastDamageCause = ItemSystem.PassiveSkills.DeathCause.Other;
            }
        }

        /// ADR-0009 相互攻撃パイプライン（8段ターン構造）。ExecuteTurn の本体。
        /// ロール勝負は存在せず (旧ロール勝負の経路は 2026-10-29 に削除)、毎ターン
        /// 自攻撃(武器+攻撃端子出目) → 敵攻撃(基礎×段階倍率+β×ロール − ブロック) を解決する。
        /// 勝敗トリガーは「収支」(与ダメ−被ダメ) に再定義され**解決後**に発火する。
        ///
        /// v1 の未実装/暫定 (W7 後段で解消。ADR-0009 / HANDOFF-OPUS47 W7 実装メモ参照):
        ///   - 柱5 電力経済は充電獲得の記録のみ (accumulatedValues["mutualCharge"])。消費/停電なし。
        ///   - 特殊端子なし (Attack/Block/Charge の 3 種)。
        ///   - 影の代償・コルヴェンの憤怒は未接続。
        ///   - ボス固有機構 (サドンデス/コントラタック) 未移植 → boss6 は旧法で計測する。
        ///   - リロール自動ポリシーは旧目的関数 (ロール勝率) のまま。
        ///   - ProcessPostRoll 中の playerWonRoll はロール比較値 (収支確定前) — 出目参照系は
        ///     従来どおり、勝敗参照系のみ解決後の再発火で拾う。
        /// </summary>
        private TurnResult ExecuteTurnMutual()
        {
            // 方策 (BOT) で決める経路: 判断の所で止まらずに最後まで回す (_interactiveTurn = false なら一度も yield しない)。
            var steps = ExecuteTurnMutualSteps();
            while (steps.MoveNext()) { }
            return _stepResult;
        }

        /// <summary>1 ターンの本体。 人が操作するとき (<see cref="BeginInteractiveTurn"/>) だけ、 リロールと配線の判断の所で止まる。
        /// 止まる位置は方策を呼んでいた位置と同じなので、 乱数を引く順番は方策の経路と変わらない。 結果は <see cref="_stepResult"/>。</summary>
        private IEnumerator<TurnPause> ExecuteTurnMutualSteps()
        {
            var psm = PassiveSkillManager.Instance;
            var ctx = psm.Context;

            // 偽の商人: 規定ターン数を終えても未決着なら逃走 (旧法と同一)
            if (fleeAfterTurns > 0 && ctx != null && ctx.currentTurn >= fleeAfterTurns
                && playerHP > 0 && enemyHP > 0)
            {
                MetaProgression.Achievements.AchievementService.NoteEnemyEscaped(currentEnemy?.id);
                Debug.Log($"[CombatManager] 偽の商人: {fleeAfterTurns}ターン経過 → 逃走（戦闘強制終了）");
                FinishCombat();
                _stepResult = new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = playerHP,
                    enemyHPAfter = enemyHP,
                };
                yield break;
            }

            // --- 0. ターン開始処理 ---
            // ここからターンの最後の HP 同期までは、 品が入れる敵へのダメージを預かる (Kw.DamageEnemy)。
            if (ctx != null) ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.ResolvingKey] = 1;
            psm.BeginTurn();

            // 装備維持費 (2026-08-15): **所持パッシブ 10 個につき充電 1** を毎ターン消費する。
            //   §7-3 の仕様として決まっていたが実装されていなかった。
            //   狙い: 充電の収支が 1ターン 収入 4.04 / 支出 1.69 と**2.4 倍余っており**、
            //   余剰 2.35 は戦闘終了時に捨てられていた。 リロールが安いのではなく
            //   **使い道が無い**のが実態で、 結果として〈極〉を狙う刻みリロールが実質無料だった。
            //   ビルドが太るほど維持費が増えるので、 極を連発する厚いビルドほど原資が減る。
            //   **払えなければあるだけ払う**（罰は与えない）。 上限を割ることによる
            //   過充電型パッシブの失効が、 それ自体の重みになる。
            {
                // [凍結 2026-08-15] 10 個につき 1 は重すぎた ── 踏み倒し 35.8%、
                //   7層クリア 25.1% → 16.2% (−8.9pt) に対し 極は 12.7 → 8.06 回/ラン (−37%) のみ。
                //   極はリロール量に線形にしか反応しないので、 経済で殴っても届かない。
                //   PassiveUpkeepPer を 0 以外に戻せば復活する。
                const int PassiveUpkeepPer = 0;
                var upRun = GameLoop.GameManager.Instance?.Run;
                int owned = upRun?.ownedPassiveItems?.Count ?? 0;
                int upkeep = PassiveUpkeepPer > 0 ? owned / PassiveUpkeepPer : 0;
                if (upkeep > 0 && ctx != null)
                {
                    int pay = Math.Min(upkeep, ctx.GetCharge());
                    if (pay > 0) ctx.ConsumeCharge(pay);
                    CombatStats.PassiveUpkeepPaid += pay;
                    CombatStats.PassiveUpkeepDue  += upkeep;
                }
            }
            // T4-E〈最後の審判〉: ラン累計ターンの刻限。 **ターン開始時に清算する**。
            ApplyFinalJudgment();
            // 刻限の死にもラストスタンドは効く。 ここを飛ばすと「ターン開始で死んだ時だけ
            //   蘇生しない」という、 死因でしか区別できない不整合になる。
            TryLastStandRevive(ctx);
            if (playerHP <= 0)
            {
                // 刻限で力尽きた。 強制決着と同じ経路で閉じる (放置すると戦闘が続く)。
                if (ctx != null) ctx.playerCurrentHP = 0;
                FinishCombat();
                _stepResult = new TurnResult
                {
                    turnNumber = ctx != null ? ctx.currentTurn : 0,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = 0,
                };
                yield break;
            }

            // 安全弁: 300T 超で強制決着 (旧法と同一)
            const int kHardTurnCap = 300;
            if (ctx != null && ctx.currentTurn > kHardTurnCap)
            {
                Debug.LogWarning($"[CombatManager] 戦闘ターン上限超過 ({ctx.currentTurn} > {kHardTurnCap}) — 強制決着（プレイヤー敗北）");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                FinishCombat();
                _stepResult = new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = 0,
                    enemyHPAfter = enemyHP,
                };
                yield break;
            }

            // 消費アイテム持続効果の毎ターン再適用 + 死因タグリセット (旧法と同一)
            if (ctx != null)
            {
                if (ctx.consCritPct > 0f)
                {
                    ctx.critRatePctAdd += ctx.consCritPct;
                    DmgSourceDiag.Note("消費〈会心率〉", DmgSourceDiag.CritRate, ctx.consCritPct);
                }
                if (ctx.consFlatReduce > 0) ctx.playerFlatDamageReduction += ctx.consFlatReduce;
                ctx.lastDamageCause = ItemSystem.PassiveSkills.DeathCause.Normal;
            }

            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnStart);
            SyncHPFromContext(ctx);

            // フロアデバフ: 敵の毎ターンHP回復 (旧法と同一)
            var floorMod = GameLoop.GameManager.Instance?.ActiveModifier;
            if (floorMod != null && floorMod.enemyPerTurnHeal > 0 && enemyHP > 0)
            {
                int heal = Mathf.Min(currentEnemy.maxHP - enemyHP, floorMod.enemyPerTurnHeal);
                if (heal > 0)
                {
                    enemyHP += heal;
                    ctx.enemyCurrentHP = enemyHP;
                    Debug.Log($"[CombatManager] フロアデバフ: 敵HP+{heal} ({enemyHP}/{currentEnemy.maxHP})");
                }
            }

            // 2 体戦: このターンに 1 体目が受けた削りを 2 体目へ丸ごと写すための基準点。
            //   **回復の後・ダメージの前**に取る。 差分で採るのは、 通常ダメージ・固定ダメ・出血・
            //   反射・反撃固定ダメと**削る経路が 5 本ある**ため ── 個別に足すと必ずどれかを取り落とす。
            int primaryHpBeforeDamage = enemyHP;

            int enemyExtraDice = (int)ctx.GetAccumulated("extraDice");
            ctx.accumulatedValues["extraDice"] = 0;
            // [計装] BossCombatTrace 用。 TurnResult には与ダメ/被ダメが無いので自前で持つ。
            int traceAtk = 0, traceBlock = 0, traceThrough = 0;
            int traceEBase = 0, traceERoll = 0; float traceEsc = 1f;
            int traceRaw = 0, traceBonus = 0, traceBossBonus = 0;

            // --- 1. 敵ロール (スタンスなし = ADR-0005 は superseded) ---
            int actualPlayerDiceCount = playerDiceCount;
            int[] enemyDice = RollEnemyAttack(currentEnemy, enemyExtraDice, false, 0f);
            int actualEnemyDiceCount = enemyDice.Length;

            // --- 3. 自ロール ---
            //   出目パーツ (2026-08-17): 値だけでなく **どの面に止まったか** を持ち回る。
            //   以降のロール書き換え (見放された運 / 避雷針 / リロール / レイピア) でも
            //   playerFaceIdx を必ず同期すること ── 忘れると別の面の効果が発火する。
            int[] playerFaceIdx;
            int[] playerDice = RollDice(actualPlayerDiceCount, playerDiceMax, ctx.equippedDiceFaces, out playerFaceIdx);
            CombatStats.NoteFirstRoll(actualPlayerDiceCount, ctx, playerDice);

            // 挑戦デバフ 軸9〈見放された運〉: ダイスを N 本 1 に潰す。
            //   T1 = 1T目に 1 本 / T2 = 1T目に 2 本 / T3 = 全ターン 1 本。
            //   潰す対象は **出目が大きいものから** ── ランダムだと元から低い目を潰すだけの
            //   無害なターンが混ざり、 Tier ごとの重みが安定しない。
            int forsaken = MetaProgression.MetaDebuffApplicator.GetForsakenLuckDiceCount(CurrentCombatTurn);
            for (int k = 0; k < forsaken && k < playerDice.Length; k++)
            {
                int best = -1, bestVal = 0;
                for (int i = 0; i < playerDice.Length; i++)
                    if (playerDice[i] > bestVal) { bestVal = playerDice[i]; best = i; }
                if (best < 0 || bestVal <= 1) break;
                playerDice[best] = 1;
                // 出目パーツ: **値を上書きする効果は、そのダイスを面から降ろす**。
                //   潰された 1 はもうその面ではないので、 パーツ効果も発火しない。
                //   (加算する〈鋳造口〉は面が変わらないので添字を保つ ── 上書きと加算で扱いが違う)
                if (playerFaceIdx != null && best < playerFaceIdx.Length) playerFaceIdx[best] = -1;
                Debug.Log($"[見放された運] 出目 {bestVal} → 1 (T{CurrentCombatTurn})");
            }

            // 特殊端子〈鋳造口〉(§6-5): 前ターンまでに蓄積した恒常の出目 +N。
            //   **1 固定系デバフの後に置く** ── 先に足すと〈見放された運〉が潰す対象が
            //   底上げ後の値になり、 デバフと端子が互いを打ち消し合って両方無意味になる。
            {
                int foundry = (int)ctx.GetAccumulated(FoundryBonusKey);
                if (foundry > 0)
                    for (int i = 0; i < playerDice.Length; i++) playerDice[i] += foundry;
            }

            // 7層ヴェスカ〈天与の指輪〉: 3 以上の出目をすべて 1〜2 へ再抽選する。
            //   全出目 1 は本数ぶんの手が完全に死に、 その前の「行動不可」と重さが変わらなかった。
            //   再抽選なら低いなりに幅が残り、 どこへ挿すかの判断が生きる。
            if (ctx.playerHighDiceCrushed)
            {
                int crushed = 0;
                for (int i = 0; i < playerDice.Length; i++)
                    if (playerDice[i] >= 3)
                    {
                        playerDice[i] = GameLoop.GameRng.Range(1, 3, "vesca.divineRing", RngIdx(i));
                        crushed++;
                    }
                Debug.Log($"[天与の指輪] 3 以上の出目 {crushed} 本を 1〜2 へ再抽選 ({playerDice.Length} 本中)");
            }
            // 7層ヴェスカ〈落雷の避雷針〉: ダイス 1 本 (ランダム) を **そのダイスの最小面**に固定。
            // どの目が潰れるか読めない圧にするためランダム選択 (§13-4)。
            // 2026-08-16: 固定値 1 → 最小面へ。 面パーツ等で面配列が 1 始まりでないダイスに
            //   1 を書き込むと、 **そのダイスが出せない目**を作ってしまう。
            else if (ctx.lightningRodArmed && playerDice.Length > 0)
            {
                int minFace = 1;
                var faces = ctx.equippedDiceFaces;
                if (faces != null && faces.Length > 0)
                {
                    minFace = faces[0];
                    for (int i = 1; i < faces.Length; i++) if (faces[i] < minFace) minFace = faces[i];
                }
                int idx = GameLoop.GameRng.Range(0, playerDice.Length, "combat.lightning", RngIdx(4));
                Debug.Log($"[落雷の避雷針] ダイス{idx + 1} を {playerDice[idx]} → {minFace} (最小面) に固定");
                playerDice[idx] = minFace;
                // 上書き系なので面から降ろす (見放された運と同じ扱い)。
                if (playerFaceIdx != null && idx < playerFaceIdx.Length) playerFaceIdx[idx] = -1;
            }

            // --- 4. リロール (ADR-0010: 充電消費・方策注入・何度でも払える限り) ---
            //   **予告を先に組んでから振り直す。** 敵の攻撃値が見えていないと
            //   「何を残すか」を決められず、 完全情報テレグラフ (柱3) の意味が消える。
            //   ここでは敵ダイスの素の合計で予告を作る ── 敵ダイスはこの後変化しないので、
            //   パッシブによる enemyDiceTotal 補正のぶんだけ近似になる。
            // **リロールより前に ctx へ載せる。** リロール方策は
            //   「T1 の面に止まったダイスは振れない」を見て予算を組むので、
            //   後で代入すると方策が前ターンの添字を読む。
            //   RerollPhase は配列を **その場で** 書き換えるので、 参照を先に渡せば同期は保たれる。
            ctx.playerDiceFaceIdx = playerFaceIdx;

            var reroll = RerollPhase(ctx, playerDice, enemyDice, playerDiceMax, playerFaceIdx);
            while (reroll.MoveNext()) yield return reroll.Current;

            // ロール時系効果 (出目書き換え系はここまで = LED に見える値が配線に使える値)
            ctx.playerDice = playerDice;
            ctx.enemyDice = enemyDice;
            ctx.playerDiceMax = playerDiceMax;
            EventSystem.TimedEffects.TimedEffectManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);
            ItemSystem.PassiveItems.PassiveItemManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // メタバフ: ダイス合計値補正 (旧法と同一)
            int metaDiceBonus = ctx.rollPurity ? 0 : MetaProgression.MetaBuffApplicator.GetDiceTotalBonus();
            int safety = metaDiceBonus * playerDice.Length;
            while (metaDiceBonus > 0 && safety-- > 0)
            {
                int minIdx = -1;
                for (int j = 0; j < playerDice.Length; j++)
                {
                    if (playerDice[j] >= playerDiceMax) continue;
                    if (minIdx < 0 || playerDice[j] < playerDice[minIdx]) minIdx = j;
                }
                if (minIdx < 0) break;
                playerDice[minIdx]++;
                metaDiceBonus--;
            }

            // パターン系パッシブ (ゾロ目/階段/全相異) + 合計確定
            psm.ProcessPostRoll(playerDice, enemyDice);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostRoll);
            // 斧〈血令〉: 防御を貫通へ換える。 防御の加算 (OnPostRoll) が全部済んだ後・予告より前でなければならない。
            ItemSystem.PassiveSkills.Effects.BloodEdict.ConvertDefenseToPen(ctx);

            _fightPlayerRollSum += ctx.playerDiceTotal;
            _fightPlayerRollCount++;

            // --- 2. 予告 (柱3: 完全情報テレグラフ) ---
            // 〈冷却材〉(§6-5) の遅延を差し引いたターンで段階を引く。 予告にもそのまま出るので、
            // 「何ターン稼いだか」が盤面から読める (完全情報テレグラフ・柱3)。
            int escTurn = EscalationTurn(ctx);
            int enemyAtkValue = Escalation.EnemyAttackValue(currentEnemy, escTurn, ctx.enemyDiceTotal, ctx.currentTurn);
            // ADR-0009 D群: 敵攻撃値-N (呪縛 等の敵ダイス弱化系の転生先)
            enemyAtkValue = Math.Max(0, enemyAtkValue - ctx.mutualEnemyAttackReduction);
            // ADR-0011 〈封じ・対〉(前ターン成立): 敵の攻撃を N% 減らす。 予告にもそのまま出る (完全情報)。
            if (ctx.currentBuffs.TryGetValue(YachtRoleEffects.EnemyAtkCutKey, out float roleAtkCut) && roleAtkCut > 0f)
                enemyAtkValue = Math.Max(0, Mathf.RoundToInt(enemyAtkValue * (1f - roleAtkCut / 100f)));
            // 2026-07-25 v6: 出血 r2 失血衰弱 (敵の出血 3 以上で攻撃 -2)
            enemyAtkValue = Math.Max(0, enemyAtkValue - MetaProgression.MetaBuffApplicator.GetBleedWeakenAttack(ctx.enemyBleedStacks));
            // ADR-0013: プレイヤーの防御は**ブロックの前**に敵の攻撃値へ掛ける。 予告にもこの値が出る (柱3)。
            //   〈貫きの錐〉は敵の攻撃値に比例してブロックを削るので、 防御で減った後の値を使う。
            int enemyAtkBeforeDefense = enemyAtkValue;
            enemyAtkValue = ApplyPlayerDefense(ctx, enemyAtkValue, ctx.enemyArmorPen);

            // 2 体戦: 2 体目の攻撃も**配線前に**確定させて予告へ載せる (柱3 完全情報テレグラフ)。
            //   解決時に振ると「見えていない一撃」ができ、 ADR-0009 の思想に正面から反する。
            //   ここで振った値を解決でもそのまま使う (二度振らない)。
            _secondaryAtkThisTurn = 0;
            int secondaryAtkBeforeDefense = 0;
            if (secondaryEnemy != null && secondaryHP > 0)
            {
                var secDiceTele = RollEnemyAttack(secondaryEnemy, 0, false, 0f);
                int secTotalTele = 0;
                for (int i = 0; i < secDiceTele.Length; i++) secTotalTele += secDiceTele[i];
                secondaryAtkBeforeDefense =
                    Math.Max(0, Escalation.EnemyAttackValue(secondaryEnemy, escTurn, secTotalTele, ctx.currentTurn));
                _secondaryAtkThisTurn = ApplyPlayerDefense(ctx, secondaryAtkBeforeDefense,
                    Math.Max(0, secondaryEnemy.armorPen));
            }

            var tele = new MutualTurnTelegraph
            {
                turn = ctx.currentTurn,
                enemyAttackValue = enemyAtkValue,
                secondaryAttackValue = _secondaryAtkThisTurn,
                escalationStage = Escalation.StageOf(escTurn),
                nextThresholdTurn = Escalation.NextThresholdTurn(escTurn),
                turnsToHeavy = Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn),
                heavyMul = currentEnemy?.heavyMul ?? 0f,
                enemyDice = enemyDice,
                enemyDiceTotal = ctx.enemyDiceTotal,

                // 7層ヴェスカ: 抽選結果を開示する (§6-2 完全情報 / §13-4)。
                // 遺物の攻撃強化は enemyAttackValue に既に含まれる (enemyDiceTotalBonus 経由・β圧縮後)。
                // ここで渡すのは攻撃値に乗らない性質だけ。
                drawnRelics = ctx.vescaDrawnRelics != null ? ctx.vescaDrawnRelics.ToArray() : null,
                enemyShield = ctx.vescaShield,
                enemyShieldReflectRate = ctx.vescaShieldReflectRate,
                enemyDodgeChance = ctx.enemyDodgeChance,
                enemyDamageTakenMul = ctx.enemyDamageTakenMultiplier,
                enemyHalvesDamageThisTurn = ctx.vescaHalvesDamageThisTurn,
                playerAttackPenalty = ctx.playerAttackPowerPenalty,
                playerHighDiceCrushed = ctx.playerHighDiceCrushed,
                playerBlockIgnored = ctx.playerBlockIgnored,
                blazeStacks = ctx.accumulatedValues.TryGetValue(
                        ItemSystem.PassiveSkills.Effects.BlazeBrand.StackKey, out var _bz)
                    ? (int)_bz : 0,
                blazePenalizesBlock = ctx.accumulatedValues.TryGetValue(
                        ItemSystem.PassiveSkills.Effects.BlazeBrand.ActiveKey, out var _bza)
                    && _bza > 0f,
                executeArmed = ctx.vescaExecuteArmed,
                // 〈綻び〉: このターン封印される端子。 **配線前に開示する** (柱3)。
                sealedTerminal = MetaProgression.MetaDebuffApplicator.GetSealedTerminal(
                    ctx.currentTurn, SpecialTerminals.EquippedLimit(GameLoop.GameManager.Instance?.Run) > 0 ? 4 : 3),
            };
            if (ctx.bossId == "boss_layer7_p4")
                BossDmgDiag.P4Turn(ctx.playerHighDiceCrushed, ctx.vescaExecuteArmed,
                                   ctx.vescaMaxHpBiteOnHit, ctx.vescaDrawnRelics);
            if (!string.IsNullOrEmpty(ctx.bossId) && ctx.bossId.StartsWith("boss_layer7"))
                BossDmgDiag.V7Turn(ctx.vescaDrawnRelics);
            Debug.Log($"[相互攻撃] T{tele.turn} 予告: 敵攻撃 {enemyAtkValue} (段階{tele.escalationStage}" +
                      $"{(tele.nextThresholdTurn > 0 ? $"・次段階T{tele.nextThresholdTurn}" : "・最終段階")})" +
                      $" 防御前{enemyAtkBeforeDefense}・自防御{ctx.playerDefense}−削り{ctx.enemyArmorPen}");
            if (tele.drawnRelics != null && tele.drawnRelics.Length > 0)
                Debug.Log($"[予告] 遺物学者: {string.Join(" / ", tele.drawnRelics)}"
                        + (tele.enemyShield > 0 ? $"  シールド{tele.enemyShield}" : "")
                        + (tele.enemyShieldReflectRate > 0f ? $"  反射×{tele.enemyShieldReflectRate:F1}" : "")
                        + (tele.enemyDodgeChance > 0f ? $"  回避{tele.enemyDodgeChance:P0}" : "")
                        + (tele.enemyHalvesDamageThisTurn ? "  【ダメージ半減】" : "")
                        + (tele.executeArmed ? "  【処刑】" : "")
                        + (tele.playerHighDiceCrushed ? "  【出目3以上を再抽選】" : "")
                        + (tele.playerBlockIgnored ? "  【ブロック無効】" : ""));

            // --- 5. 配線 (前ターン配線の自動適用 → ポリシー/UI による変更) ---
            if (mutualWiring == null || mutualWiring.Length != playerDice.Length)
            {
                var next = new DiceTerminal[playerDice.Length];
                for (int i = 0; i < next.Length; i++)
                    next[i] = (mutualWiring != null && i < mutualWiring.Length)
                        ? mutualWiring[i] : DiceTerminal.Attack; // 未配置は攻撃へ自動合流
                mutualWiring = next;
            }
            if (_interactiveTurn)
            {
                // 人が操作する: 出目と予告を見せて、 配線が来るまで止まる (渡された配線は下の SanitizeWiring が直す)。
                _pause.Set(TurnPauseKind.Wiring, playerDice, playerFaceIdx, tele, mutualWiring);
                yield return _pause;
                var main = _pause.wiring;
                if (main != null && main.Length == playerDice.Length)
                    mutualWiring = (DiceTerminal[])main.Clone();
            }
            else if (WiringPolicy != null)
            {
                var plan = WiringPolicy(playerDice, tele);
                if (plan.main != null && plan.main.Length == playerDice.Length)
                    mutualWiring = plan.main;
            }

            // 特殊端子 (§6-5) の接続制限と、 挑戦デバフ〈不器用〉の端子上限を強制する。
            // **方策/UI を信用しない** ── 制限を破った配線が来たら攻撃へ落とす。
            SanitizeWiring(mutualWiring, playerDice.Length);

            int attackSum = 0, blockSum = 0, chargeDiceCount = 0;
            int attackDiceCount = 0, blockDiceCount = 0, chargeSum = 0;
            int specialSum = 0, specialCount = 0;
            for (int i = 0; i < playerDice.Length; i++)
            {
                switch (i < mutualWiring.Length ? mutualWiring[i] : DiceTerminal.Attack)
                {
                    case DiceTerminal.Block:   blockSum   += playerDice[i]; blockDiceCount++;  break;
                    case DiceTerminal.Charge:  chargeSum  += playerDice[i]; chargeDiceCount++; break;
                    case DiceTerminal.Special: specialSum += playerDice[i]; specialCount++;    break;
                    default:                   attackSum  += playerDice[i]; attackDiceCount++; break;
                }
            }
            int _diagAtkRaw = attackSum;

            // 端子調律メタバフ: **接続したダイス 1 本あたり** +N (2026-07-28 に「合計 +N」から変更)。
            // r3 で 3 本挿せば +9 ── 端子へ厚く配線するほど伸びるので、 §6-4 の配線判断と噛む。
            // 本数ベースなので「0 配線の端子に架空の加算が乗る」問題は構造的に起きない。
            int _diagAtkGhost = 0;   // (ゴースト接続は ADR-0011 で廃止)
            attackSum += attackDiceCount * MetaProgression.MetaBuffApplicator.GetAttackTerminalPerDice();
            blockSum  += blockDiceCount  * MetaProgression.MetaBuffApplicator.GetBlockTerminalPerDice();

            // --- 特殊端子 (§6-5) ---
            //   端子調律メタの後・〈貫きの錐〉と充電付与の前に置く。
            //   〈完全防御〉のぶんも錐の貫通対象にしたいので、 錐より前でなければならない。
            if (specialCount > 0)
                ApplySpecialTerminal(ctx, playerDice, specialSum, specialCount,
                                     ref attackSum, ref blockSum, ref chargeSum);
            // 〈貫きの錐〉(7層ヴェスカ GOLD 遺物・2026-07-29 変更)。
            //   旧: ブロック配線を **丸ごと 0 にする**。 防御という選択肢が消えるため、
            //       予告があっても対処のしようがなく、 致死率が基準の 2.80 倍に達していた。
            //   新: **敵の攻撃値 1 につきブロックを 2 削る**。 貫通量が攻撃値に比例するので、
            //       厚く配線すれば残る ＝ 「守り切れるか」の計算問題になる。
            //   端子調律メタの本数ボーナスも貫通対象に含めるため、 加算の **後** に処理する。
            if (ctx != null && ctx.playerBlockIgnored && blockSum > 0)
            {
                int pierce = Math.Max(0, enemyAtkValue) * PierceAwlPerAttack;
                int before = blockSum;
                blockSum = Math.Max(0, blockSum - pierce);
                Debug.Log($"[貫きの錐] 敵攻撃{enemyAtkValue} × {PierceAwlPerAttack} = ブロック -{pierce} "
                        + $"({before} → {blockSum})");
            }
            // 〈不完全な転移〉(門を不完全に起動した罰・2026-09-14)。
            //   錐と同じ「ブロックを削る」枠だが、 <b>敵攻撃値ではなくブロック量に比例</b>する。
            //   厚く配線した分は必ず一定割合残るので、 守りという選択肢は消えない。
            //   錐の後に置く ── 両方掛かるときは「錐で削られた残り」へさらに貫通が乗る。
            if (blockSum > 0)
            {
                var gateRun = GameLoop.GameManager.Instance?.Run;
                int beforeGate = blockSum;
                blockSum = GameLoop.GateFlaws.ApplyTransferPierce(gateRun, blockSum);
                if (blockSum != beforeGate)
                    Debug.Log($"[不完全な転移] ブロック貫通 {beforeGate} → {blockSum}");
            }
            // 充電: **接続ダイスの出目合計**を獲得 (2026-07-28 に「個数」から変更)。
            //   旧: 1 本 = 1 充電。 だが消費側は 雷撃 3 / 火花 1 / 過充電閾値 7 と桁が合わず、
            //       雷撃を毎ターン撃つだけで 5 本中 3 本を充電に固定する必要があった。
            //   「屑目の定席」は端子固定の性質ではなくなり、 特殊端子〈蓄電池〉(1本あたり+2 を
            //   出目に上乗せ = 出目1→3 / 出目6→8) を買った者だけの性質へ移る。 §6-5。
            if (chargeSum > 0) ctx.AddCharge(chargeSum);

            // 遺物 (§15-5): 刻印〈ブロック端子に未配線〉の判定材料。 **配線集計の直後に確定させる**。
            ctx.blockWiredThisTurn = blockDiceCount > 0;
            // 剣〈切り返し〉・盾〈城壁〉が読む「守った量」。 **貫通 (playerBlockIgnored) を差し引いた後の値**を渡す
            //   ── 実際に受け止めた分だけを数える。 ここは解決 (step6) より前なので
            //   OnWired / OnPreDealDamage から読める (OnPostRoll は配線より前なので読めない)。
            ctx.blockSumThisTurn = blockSum;

            // 遺物 (§15-5) 充電の毎ターン成分 (段4 以上)。 開幕成分は通常戦・毎ターン成分はボス戦で効く。
            // 充電端子へ 1 本も挿していないターンにも入る (「毎ターン」なので配線に依存しない)。
            {
                var relicRun = GameLoop.GameManager.Instance?.Run;
                int rct = MetaProgression.Relics.RelicApplicator.GetChargePerTurn(relicRun, ctx);
                if (rct > 0) ctx.AddCharge(rct);
            }

            // --- 5b. 役 (ADR-0010) ---
            //   手札役 / 端子役 / 配線役 を判定し、 方策が「切る」と決めた役だけを適用する。
            //   **1 戦闘 1 役 1 回・発動は任意**。 オーバーロード (タイミング判定) の後継。
            var roleOutcome = ResolveRoles(ctx, playerDice, playerFaceIdx);
            // 次ターンへ持ち越す分 (無料リロール・敵の攻撃減・行動不能) は Apply が nextTurnBuffs へ積み済み。
            // 〈癒し〉: 回復する。 T4 は HP 上限を超えた分をシールドに変える。
            if (roleOutcome.healAmount > 0)
            {
                int missing = Math.Max(0, playerMaxHP - playerHP);
                HealPlayer(roleOutcome.healAmount);
                int over = Math.Max(0, roleOutcome.healAmount - missing);
                if (roleOutcome.healOverflowToShield && over > 0)
                {
                    ctx.consShield += over;
                    ctx.shieldGainedTotal += over;
                    ShieldDiag.Note("役〈癒し〉", over);
                }
            }

            // 配線結果をパッシブから参照可能に (虚空 等が「攻撃端子接続0」を判定する)
            ctx.accumulatedValues["mutualAttackDiceCount"] = attackDiceCount;
            // 2026-07-29: ブロック本数も記録する。 〈停滞する時間〉が「攻撃0本」ではなく
            // 「守りに寄せたか (攻撃 ≦ ブロック)」で判定できるようにするため。
            // 攻撃に 1 本だけ挿して残りを全部ブロックへ回す ガン守りが、 旧条件では素通りしていた。
            ctx.accumulatedValues["mutualBlockDiceCount"] = blockDiceCount;
            // 2026-10-28: 充電・特殊端子の本数と予告の敵攻撃も載せ、 配線を条件にするパッシブ
            //   (剣〈切り返し〉〈不滅の刃〉・盾〈シールドバッシュ〉〈堅守の構え〉〈城壁〉) を OnWired で焚く。
            //   **atkBase の確定より前** ── ここで足した攻撃はこのターンの攻撃に乗る。
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.ChargeDiceCount]  = chargeDiceCount;
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.SpecialDiceCount] = specialCount;
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.EnemyAtk] =
                Math.Max(enemyAtkValue, _secondaryAtkThisTurn);
            // 盾〈黎明〉が読む。 敵の攻撃の解決後に上書きする (行動しなかったら 0 のまま)。
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.EnemyAtkPreDefense] = 0f;
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.HpLossFromEnemy] = 0f;
            psm.FireTrigger(PassiveSkillTrigger.OnWired);

            var result = new TurnResult
            {
                turnNumber = ctx.currentTurn,
                playerDice = playerDice,
                enemyDice = enemyDice,
                playerDiceTotal = ctx.playerDiceTotal,
                enemyDiceTotal = ctx.enemyDiceTotal,
            };

            // --- 6. 解決: プレイヤー攻撃 → 敵攻撃 (自先制・撃破時は敵攻撃なし) ---
            //   atkBase = 武器素火力 + 配線攻撃合計 + パッシブボーナス (ダイス合計加算系の転生先)
            //   ProcessDamage 内の OnPreDealDamage/OnPreReceiveDamage 分岐は playerWonRoll に依存するため、
            //   自攻撃前に true、敵攻撃前に false へ toggle する (収支トリガーは §収支計算 で最終確定)
            ctx.playerWonRoll = true; ctx.playerLostRoll = false;
            // 2026-07-25: 消費 (cons_atk_*) の攻撃バフ。旧モデルでは consDiceRoll=勝敗判定のみだったが、
            // 相互攻撃モデルでは攻撃値に直接加算する (竜閃 rollPurity 中は無効)。
            int consAtk = ctx.rollPurity ? 0 : ctx.consDiceRoll;
            // 遺物 (§15-5) 攻撃+N。 パッシブ由来の mutualAttackBonus と同じ枠へ足す。
            int relicAtk = MetaProgression.Relics.RelicApplicator
                           .GetAttackBonus(GameLoop.GameManager.Instance?.Run, ctx);
            int atkBase = playerAttackPower + attackSum + ctx.mutualAttackBonus + consAtk + relicAtk;

            // 攻撃端子への配線が 0 本なら、**このターンは攻撃を行わない** (2026-07-28)。
            //   意図: 端子への配線を「威力を足す作業」ではなく **攻撃するかどうかの宣言** にする。
            //   「このターンは殴らない」が明確な選択になり、 §6-2 のジレンマがターン単位で立つ。
            //
            //   止めるもの: 武器の素火力・パッシブの攻撃ボーナス・消費アイテムの加算・**追撃**。
            //   止めないもの: 固定ダメージ枠 (fixedDamageToEnemy)。
            //     反撃・出血・毒は「自分が攻撃したこと」と独立に成立する別チャネルであり、
            //     さらに〈虚空〉(sword_t3/t4) は **攻撃端子 0 の時に** そこへ最大HP5% を積む札なので、
            //     ここを潰すと「攻撃しない」を選ぶための札そのものが死ぬ。
            bool skipAttack = attackDiceCount <= 0 || _fleeRequested;
            if (_fleeRequested)
            {
                // 逃げるターン: こちらの攻撃は発生しない (素火力・パッシブ・追撃・固定ダメの枠も捨てる)。
                ctx.fixedDamageToEnemy = 0;
                Debug.Log("[逃げる] このターンは攻撃しない ── 敵の予告攻撃をブロックで受けて離脱する");
            }
            if (skipAttack)
            {
                if (atkBase > 0 || ctx.pursuitDamage > 0)
                    Debug.Log($"[配線] 攻撃端子が空 → **攻撃を行わない** "
                            + $"(素火力{playerAttackPower} 込み {atkBase} / 追撃 {ctx.pursuitDamage} を破棄)");
                atkBase = 0;
                ctx.pursuitDamage = 0;
            }
            // 7層ヴェスカ〈麻痺毒の小瓶〉: このターンのプレイヤー攻撃力を減算 (0 で床)。
            // 武器 attackPower は LEG でも 4〜8 なので実効は「武器素火力を消す」。 配線出目の分は残る。
            if (ctx.playerAttackPowerPenalty > 0)
            {
                int before = atkBase;
                atkBase = Math.Max(0, atkBase - ctx.playerAttackPowerPenalty);
                Debug.Log($"[麻痺毒の小瓶] 攻撃力 {before} → {atkBase} (-{ctx.playerAttackPowerPenalty}, 0で床)");
            }
            // 〈不完全な起動〉(門を不完全に起動した罰・2026-09-14)。
            //   回路が繋がりきらず出力が出ない。 **atkBase への割合**で掛ける ──
            //   攻撃端子の合計だけに掛けると梃子が弱すぎた (atkBase 60 のうち端子は 17 で、
            //   端子 25% カットでも atkBase は 7% しか減らない)。
            //   NPC 帯同と同じ位置・同じ形 (フラット減算の後・臨界爆発の前)。
            //   ブロックには掛けない (掛けると〈不完全な転移〉と軸が重なる)。
            if (atkBase > 0)
            {
                var igRun = GameLoop.GameManager.Instance?.Run;
                int beforeIg = atkBase;
                atkBase = GameLoop.GateFlaws.ApplyIgnitionCut(igRun, atkBase);
                if (atkBase != beforeIg)
                    Debug.Log($"[不完全な起動] 出力不足 atkBase {beforeIg} → {atkBase}");
            }

            // 臨界爆発の予約消費 (臨界が閾値に達した時点で予約される・Kw.AddHeat)
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BurstThisAttackKey] = 0;
            if (ctx.rinkaiBurstActive)
            {
                // **atkBase に足さない (2026-08-03 修正)。**
                //   旧実装は atkBase へ加算していたため、 その後の会心 ×3.0 と
                //   outgoingDamageMultiplier が丸ごと乗り、 「flat 50」のつもりの数字が
                //   実効 150〜300 になっていた。
                //   §9.2 の「爆発 flat」という設計意図どおり、 倍率の乗らない
                //   fixedDamageToEnemy (軽減無視の固定ダメ枠) へ回す。
                // 〈帳簿外の連爆〉で同じターンに何度も爆発したら、 その回数ぶん乗る。 品の上乗せ (〈溢れ取りの鋳型〉) は Kw.BurstBonusKey。
                int burstN = Math.Max(1, (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BurstCountKey));
                int burstBonus = (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Keywords.Kw.BurstBonusKey);
                int burstDmg = ctx.rinkaiBurstDamage * burstN + burstBonus;
                ctx.fixedDamageToEnemy += burstDmg;
                ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BurstCountKey] = 0;
                ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BurstBonusKey] = 0;
                ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BurstThisAttackKey] = 1;
                ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BurstAppliedTurnKey] = ctx.currentTurn;
                ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.BurstAppliedDmgKey] = burstDmg;
                // 遺物 (§15-5) 臨界: 爆発時に **敵の現在HP** の N% を上乗せする。
                //   最大HP 比だと削っても威力が落ちず、 1 戦闘 8 回の爆発で合計が最大HP の
                //   180% に達して遺物 1 個でボスを 2 回殺せる量になった (§24)。
                //   現在HP 比なら自己減衰し、 撃破もできない (漸近するだけ)。
                //   **これも固定ダメ枠へ。** 旧実装は atkBase 経由だったので同じ二重取りをしていた。
                float rinPct = MetaProgression.Relics.RelicApplicator
                               .GetRinkaiCurrentHpPct(GameLoop.GameManager.Instance?.Run, ctx);
                int relicBurst = rinPct > 0f && ctx.enemyCurrentHP > 0
                               ? Mathf.FloorToInt(ctx.enemyCurrentHP * rinPct) : 0;
                if (relicBurst > 0) ctx.fixedDamageToEnemy += relicBurst;
                ctx.rinkaiBurstActive = false;
                Debug.Log($"[臨界爆発] ×{burstN} 固定ダメ +{burstDmg}"
                    + (relicBurst > 0 ? $" ＋遺物 {relicBurst} (現在HP {ctx.enemyCurrentHP} の {rinPct:P0})" : ""));
            }
            // 品がここまでに預けた敵へのダメージは、 この攻撃の固定ダメ枠で通す (Kw.DamageEnemy)。
            ctx.fixedDamageToEnemy += ItemSystem.PassiveSkills.Keywords.Kw.TakePendingEnemyDamage(ctx);
            // [診断] この呼び出しだけが「プレイヤー勝利の主攻撃」。 段別計測はここでのみ積む。
            CombatStats.DiagWinAttack = true;
            var (dealtDmg, dealtFixed, isCrit) = psm.ProcessDamage(
                atkBase, ctx.pursuitDamage, playerCritBaseRate);
            int lbStage = GameLoop.GameManager.Instance?.Run?.limitBreakStage ?? 0;
            dealtDmg = ApplyWinDamageModifiers(dealtDmg, ref dealtFixed, ref isCrit, lbStage, psm, ctx);
            ApplyRoleExtraDamage(ctx, ref dealtDmg, ref dealtFixed, roleOutcome);
            ctx.lastPlayerDamage = dealtDmg + dealtFixed;
            CombatStats.DiagWinAttack = false;
            // [計装] 与ダメ出どころ別の発動率。 攻撃した回だけ数える。
            if (atkBase > 0)
                DmgSourceDiag.Commit(ctx, isCrit, _diagKind == SkillDiag.Boss, playerCritBaseRate,
                                     psm.ActivePlayerSkillIds,
                                     GameLoop.GameManager.Instance?.Run?.ownedPassiveItems,
                                     DsWeaponAlsoOwnedFlag());

            // 与ダメージの内訳を記録する (診断用・§15-5 の単位較正を実機基準へ直すため)
            CombatStats.DamageBreakdown[0]++;
            CombatStats.DamageBreakdown[1] += playerAttackPower;
            CombatStats.DamageBreakdown[2] += attackSum;
            CombatStats.DamageBreakdown[3] += ctx.mutualAttackBonus;
            CombatStats.DamageBreakdown[4] += consAtk;
            CombatStats.DamageBreakdown[5] += relicAtk;
            CombatStats.DamageBreakdown[6] += atkBase;
            CombatStats.DamageBreakdown[7] += dealtDmg;
            if (isCrit) CombatStats.DamageBreakdown[8]++;
            CombatStats.DamageBreakdown[9] += dealtFixed;
            CombatStats.DamageBreakdown[10] += ctx.pursuitDamage;
            CombatStats.RecordFloorDamage(ctx, dealtDmg, dealtFixed, currentEnemy != null ? currentEnemy.maxHP : 0);

            // 物資(ADR-0002) 疲労: 攻撃が15%で**最終ダメージ半減** (2026-08-09 に 0 ダメから緩和)。
            //   **isCrit は落とさない** ── 会心の成否は判定済みの事実で、 疲労は結果の目減り。
            float fatigueChance = GameLoop.ProvisionSystem.GetFatigueChance(GameLoop.GameManager.Instance?.Run);
            if (fatigueChance > 0f && GameLoop.GameRng.Chance(fatigueChance, "combat.fatigue", RngIdx(2)))
            {
                float fm = GameLoop.ProvisionSystem.FatigueDamageMultiplier;
                dealtDmg   = dealtDmg   > 0 ? Math.Max(1, Mathf.RoundToInt(dealtDmg   * fm)) : dealtDmg;
                dealtFixed = dealtFixed > 0 ? Math.Max(1, Mathf.RoundToInt(dealtFixed * fm)) : dealtFixed;
                Debug.Log($"[物資] 疲労: 最終ダメージ半減 → 主{dealtDmg} 固{dealtFixed}");
            }

            result.mainDamage = atkBase;
            result.totalDamage = dealtDmg;
            result.fixedDamage = dealtFixed;
            result.isCritical = isCrit;
            result.pursuitDamage = ctx.pursuitDamage;
            result.scratchDamage = 0; // scratch 廃止 (threat は基礎攻撃値へ転生)

            // 7層ヴェスカ: 〈解析演算〉の回避 → 〈真守/革の手甲/不落の城盾〉のシールド吸収 の順に通す。
            ApplyVescaDefense(ctx, ref dealtDmg, ref dealtFixed);
            result.totalDamage = dealtDmg;
            result.fixedDamage = dealtFixed;
            // [計測] ボス戦のみ、 プレイヤーの主攻撃と固定ダメを分けて実測する
            if (ctx != null && !string.IsNullOrEmpty(ctx.bossId))
            {
                BossDmgDiag.Record(ctx.bossId, currentEnemy != null ? currentEnemy.displayName : ctx.bossId,
                                   dealtDmg, dealtFixed);
                // [計測] 上振れヒットの内訳を実データで残す (平均の 10 倍が出る理由の特定用)
                if (BossDmgDiag.WantSpike(dealtDmg))
                {
                    BossDmgDiag.AddSpike(
                        $"{(currentEnemy != null ? currentEnemy.displayName : ctx.bossId)} T{ctx.currentTurn} "
                      + $"主{dealtDmg} 固{dealtFixed} | crit={isCrit} critMul={ctx.criticalMultiplier:F2} "
                      + $"outMul={ctx.outgoingDamageMultiplier:F2} 臨界burst={ctx.rinkaiBurstActive} "
                      + $"burstDmg={ctx.rinkaiBurstDamage} 脆弱={ctx.enemyVulnerabilityMultiplier:F2} "
                      + $"armorPen={ctx.playerArmorPen} 敵防御={ctx.enemyDefense} 画竜={ctx.garyoProc} "
                      + $"atkDice={(int)ctx.GetAccumulated("mutualAttackDiceCount")} "
                      + $"pRoll={ctx.playerDiceTotal} 武器={GameLoop.GameManager.Instance?.Run?.equippedWeaponId}");
                }
            }

            // [計装] 7層 4 段の端子別ダイス配分。 p4 だけ実火力が 1/4.5 になる理由を
            //   「ブロックに本数を取られているのか」で切る。 与ダメも同じ行に載せて、
            //   本数比とダメージ比が一致するか (＝本数で説明しきれるか) を見る。
            if (ctx != null && !string.IsNullOrEmpty(ctx.bossId)
                && ctx.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix))
            {
                // **段の判定は ctx.bossId から取る。** VescaRelicPool.ResolveStage() は
                //   CombatManager.Instance.CurrentEnemy を読む実装で、 取れないと既定 1 を返すため
                //   p1 が過大・p4 が過小に偏る (実測: p4 の記録ターンが実際の 1/5 しか無かった)。
                //   ctx.bossId は SwapEnemy が形態ごとに更新している。
                GameLoop.RunChronicle.NotePhaseWiring(
                    ctx.bossId.EndsWith("_p4") ? 4 : ctx.bossId.EndsWith("_p3") ? 3
                        : ctx.bossId.EndsWith("_p2") ? 2 : 1,
                    (int)ctx.GetAccumulated("mutualAttackDiceCount"),
                    (int)ctx.GetAccumulated("mutualBlockDiceCount"),
                    attackSum, blockSum, dealtDmg + dealtFixed);
            }

            enemyHP = Math.Max(0, enemyHP - dealtDmg - dealtFixed);
            if (_diagKind == SkillDiag.Boss && currentEnemy != null)
            {
                var bd = SkillDiag.BossDmgOf(currentEnemy.id);
                bd[2] += dealtDmg; bd[3] += dealtFixed;
                if (dealtDmg > bd[7]) bd[7] = dealtDmg;
                if (atkBase > 0)
                {
                    bd[9] += 1; bd[10] += atkBase; if (isCrit) bd[11] += 1;
                    int metaPer = attackDiceCount * MetaProgression.MetaBuffApplicator.GetAttackTerminalPerDice();
                    bd[12] += playerAttackPower; bd[13] += attackSum - metaPer; bd[14] += metaPer;
                    bd[15] += ctx.mutualAttackBonus; bd[16] += consAtk; bd[17] += relicAtk;
                    bd[18] += attackDiceCount; bd[19] += playerDice.Length;
                    bd[20] += _diagAtkRaw; bd[21] += _diagAtkGhost;
                    bd[22] += attackSum - metaPer - _diagAtkRaw - _diagAtkGhost;
                    int mx = 0; for (int q = 0; q < playerDice.Length; q++) if (playerDice[q] > mx) mx = playerDice[q];
                    bd[23] += mx;
                }
            }

            // **敵の「被弾した」トリガーはここで焚く (2026-09-09 修正)。**
            //   相互攻撃モデルでは、 これより下の分岐が
            //     `else if (enemyHP > 0) { 敵の攻撃 … OnPostDealDamage }`
            //     `else               {                OnPostReceiveDamage }`
            //   という形で、 **敵が死んだターンにしか OnPostReceiveDamage が鳴らなかった**。
            //   このトリガーを聴いている敵パッシブは〈鏡映の応答〉(4層ボス) だけで、
            //   反射は「次ターン開始時」に着弾する設計なので、 死亡ターンに予約しても
            //   永久に不発。 実測: 2,460 戦で被弾イベント 1,809 回 (＝ほぼ死亡ターンのみ)、
            //   反射 11,632 を積んだのに被ダメは 1 も動かなかった。
            //   ADR-0009 移行時の配線漏れで、 鈍器の attackerIsEnemy 漏れと同型。
            if (dealtDmg + dealtFixed > 0)
            {
                ctx.finalDamage = dealtDmg + dealtFixed;   // 敵視点で「受けた量」
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostReceiveDamage);
            }

            // シールドが吸収した分の反射 (返しの盾 100% / 不落の城盾 200%)。
            // 吸収量が上限なので、 シールド値の 2 倍を超える反射は原理的に発生しない。
            if (ctx.vescaShieldAbsorbedThisTurn > 0 && ctx.vescaShieldReflectRate > 0f && playerHP > 0)
            {
                int reflect = Mathf.RoundToInt(ctx.vescaShieldAbsorbedThisTurn * ctx.vescaShieldReflectRate);
                if (reflect > 0)
                {
                    reflect = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        reflect, GameLoop.GameManager.Instance?.Run, playerHP);
                    playerHP = Math.Max(0, playerHP - reflect);
                    ctx.playerCurrentHP = playerHP;
                    Debug.Log($"[ヴェスカ] シールド反射 {ctx.vescaShieldAbsorbedThisTurn}×{ctx.vescaShieldReflectRate:F1} = {reflect} ダメージ");
                }
            }

            // メタデバフ Lv9 鋼の皮膚 (旧法と同一)
            if (enemyHP == 0 && !metaLethalSurviveUsed
                && MetaProgression.MetaDebuffApplicator.EnemySurvivesFirstLethal())
            {
                enemyHP = 1;
                metaLethalSurviveUsed = true;
                Debug.Log("[CombatManager] メタデバフ Lv9 鋼の皮膚: 敵が初回致命傷で1HPに踏みとどまった");
            }

            // --- 役 (ADR-0010) の撃破系。 **メタデバフ〈鋼の皮膚〉より後に置く** ---
            //   どちらも「即勝利」「確定キル」を謳う役なので、 1HP 踏みとどまりに吸われると
            //   役の文面が嘘になる。 1 戦闘 1 回・極は 1/1296 級の頻度なので上書きを許す。
            if (roleOutcome.yachtFired && enemyHP > 0)
            {
                // ADR-0011: 〈極〉(1・1・1) は敵の最大HP の (面数 × 30%) を軽減無視で削る。
                //   ボス・ヴェスカの各形態は面数 × 15%。 即勝利は持たない ── 面が多いほど出にくく、 出れば強い。
                int chunk = Mathf.CeilToInt(ctx.enemyMaxHP * (roleOutcome.yachtChunkPct / 100f));
                if (_diagKind == SkillDiag.Boss && currentEnemy != null)
                    SkillDiag.BossDmgOf(currentEnemy.id)[4] += Math.Min(chunk, enemyHP);
                enemyHP = Math.Max(0, enemyHP - chunk);
                Debug.Log($"[役] 極: 最大HP {ctx.enemyMaxHP} の {roleOutcome.yachtChunkPct:F1}% "
                        + $"= {chunk} を軽減無視で削る → 残 {enemyHP}");
            }

            // 出血
            if (ctx.enemyBleedStacks > 0)
            {
                int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                if (_diagKind == SkillDiag.Boss && currentEnemy != null)
                    SkillDiag.BossDmgOf(currentEnemy.id)[5] += Math.Min(bleedDmg, enemyHP);
                enemyHP = Math.Max(0, enemyHP - bleedDmg);
            }

            // --- 〈天与の才〉踏みとどまり (2026-08-16) ---
            //   まだ通過していない閾値 (最大HP の 66% / 33%) を跨ぐダメージは、
            //   その閾値ちょうどで止める。 通過の記録は同ターン末の DivineTalent.Execute が行う。
            //   **位置**: 通常ダメージ・固定ダメ・出血をすべて解決した後、 かつ役の確定キルより後。
            //   役〈極〉が決めた削りは踏みとどまりで巻き戻さない ──
            //   「軽減・シールドを無視して削る」という文面を守るため (鋼の皮膚と同じ扱い)。
            //   〈過不足なし〉の確定キルは 2026-08-22 に〈拮抗〉(与ダメ+50%) へリワークされ、
            //   通常のダメージ経路を通るようになったので、 ここでの特別扱いは無くなった。
            //   **致死打も止める。** `enemyHP > 0` で条件を切ってはいけない ──
            //   「閾値を割る一撃」を素通しすると、 火力が高いほど踏みとどまりが起きなくなり、
            //   この仕組みそのものが無意味になる。
            if (!roleOutcome.yachtFired)
            {
                enemyHP = ItemSystem.PassiveSkills.Effects.DivineTalent.ClampToThreshold(
                    ctx, enemyHP, currentEnemy != null ? currentEnemy.maxHP : ctx.enemyMaxHP);
                ctx.enemyCurrentHP = enemyHP;
            }

            if (enemyHP == 0 && dealtDmg > 0)
                ctx.overDamageAccumulated = dealtDmg + dealtFixed;

            // 吸血 / シールドバッシュ (旧法の勝利分岐と同一)
            if (ctx.lifestealPct > 0f && dealtDmg > 0)
            {
                int ls = Mathf.CeilToInt(dealtDmg * ctx.lifestealPct);
                if (ls > 0) HealPlayer(ls);
            }
            if (ctx.shieldOnWinPct > 0f && dealtDmg > 0)
            {
                int sh = Mathf.CeilToInt(dealtDmg * ctx.shieldOnWinPct) - ctx.healShieldReduction;
                if (sh > 0)
                {
                    ShieldDiag.Note("与ダメのシールド化", sh);
                    ctx.consShield += sh;
                    ctx.shieldGainedTotal += sh;
                }
            }

            int takenApplied = 0;
            bool primaryAttacked = false, secondaryAttacked = false;   // 盾〈黎明〉の「敵が放った攻撃」

            // 〈封じ・揃〉(前ターン成立) — このターン敵は攻撃しない。 予告は既に出ているので
            // 「見えている一撃を丸ごと踏み倒す」形になり、 予告の価値を壊さない。
            bool enemyStunnedThisTurn =
                ctx.currentBuffs.TryGetValue(YachtRoleEffects.EnemyStunKey, out float stunFlag) && stunFlag > 0f;

            if (enemyHP > 0 && enemyStunnedThisTurn)
            {
                Debug.Log($"[役] 封じ・揃: 敵は行動不能 (予告されていた攻撃 {enemyAtkValue} は不発)");
                ctx.daggerArmed = false;   // 仕込み刃はこのターン限り ── 攻撃が来なくても失効
                ctx.accumulatedValues.Remove(GameLoop.Consumables.OldLetterGuardKey);   // 古い手紙も同じ
            }
            else if (enemyHP > 0)
            {
                primaryAttacked = true;
                // 〈廃庭の霧吹き〉: 敵が攻撃するたびにも毒のダメージが入る (2026-10-31)
                if (ctx.poisonTickOnEnemyAttack)
                {
                    int pd = ctx.TickPoisonOnce();
                    if (pd > 0)
                    {
                        enemyHP = Math.Max(0, enemyHP - pd);
                        Debug.Log($"[廃庭の霧吹き] 敵の攻撃に合わせて毒 {pd} → 敵HP {enemyHP}");
                    }
                }
                // --- 敵攻撃: 基礎×段階倍率 + β×ロール − ブロック (下限0) → 被ダメ修飾チェーン ---
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreDealDamage);
                if (ctx != null && !string.IsNullOrEmpty(ctx.bossId))
                    BossDmgDiag.RecordTurn(ctx.bossId, currentEnemy != null ? currentEnemy.displayName : ctx.bossId);
                int lossBase = Math.Max(0, enemyAtkValue - blockSum);
                GuardDiag.Note(enemyAtkValue, enemyAtkValue, blockSum, lossBase);
                // ラン内のブロック実績。 **GuardDiag (バッチ累計) とは別に持つ** ──
                //   航行の危険度見積りが読むので、 ラン跨ぎの値を混ぜると決定性が壊れる。
                {
                    var brun = GameLoop.GameManager.Instance?.Run;
                    if (brun != null) { brun.blockSeenSum += blockSum; brun.blockSeenCount++; }
                }
                traceAtk = enemyAtkValue; traceBlock = blockSum; traceThrough = lossBase;
                traceEBase = currentEnemy != null ? currentEnemy.EffectiveBaseAttack : 0;
                traceERoll = ctx.enemyDiceTotal;
                traceEsc = currentEnemy != null
                    ? Escalation.GetMultiplier(currentEnemy.EffectiveEscalationProfile, escTurn) : 1f;
                traceRaw = ctx.rawEnemyDiceTotal;
                traceBonus = ctx.enemyDiceTotalBonus;
                traceBossBonus = ctx.bossDiceBonus;
                // [計装] 7層p4 だけ、 攻撃値の内訳をターン帯ごとに積む。
                //   p1〜p3 が 9T で 7 しか削らないのに p4 が 14T で 90 削る理由を、
                //   素火力 / ダイス合計 / 停滞 / 遺物 / ブロック貫通 に割って見る。
                if (ctx.bossId == GameLoop.BossIds.VescaFinal)
                    GameLoop.RunChronicle.NoteP4Turn(
                        ctx.currentTurn,
                        currentEnemy != null ? currentEnemy.EffectiveBaseAttack : 0,
                        ctx.enemyDiceTotal,
                        ctx.stagnationStacks * ItemSystem.PassiveSkills.Effects.StagnantTime.DicePerStack(ctx),
                        (int)ctx.GetAccumulated(ItemSystem.PassiveSkills.Effects.VescaRelicPool.KeyDiceBonusThisTurn),
                        enemyAtkValue, blockSum, lossBase, ctx.massiveBleedStacks);
                ctx.playerWonRoll = false; ctx.playerLostRoll = true; // ProcessDamage 内で OnPreReceiveDamage を発火させる
                // attackerIsEnemy: true ＝ 会心判定を飛ばす。 これが無いと敵の攻撃が
                //   **プレイヤーの**会心率で会心し、 **プレイヤーの** criticalMultiplier で
                //   増幅される (§6-10 敵会心の全廃は enemies.json の分子 0 だけでは担保できない)。
                var (takenDmg, counterFixed, _) =
                    psm.ProcessDamage(lossBase, 0, 0f, attackerIsEnemy: true);
                takenDmg = ApplyLossDamageModifiers(takenDmg, floorMod, ctx);
                if (ctx.nullifyAllDamage) takenDmg = 0; // 虚空 等: 双方ダメ0化

                // 役 (ADR-0010) の被ダメ側。 〈虚空〉と**同じ位置・同じ対象**（敵の直接攻撃パケット）に置く。
                //   fixedDamageToPlayer (毒・出血・反射) は別チャネルなので触らない ──
                //   ここを含めると「攻撃していない敵の継続ダメージまで役で消える」ことになる。
                takenDmg = ApplyRoleGuard(ctx, takenDmg, roleOutcome);

                // 仕込み刃 (暗殺者): **使ったターンの被ダメを 0 にし、 その 5 倍を軽減不可で返す** (2026-09-22)。
                //   旧仕様は「被弾するまで持ち越し・同値で返す」だった。 **効くのはこのターンだけ** ──
                //   被弾しなくても失効させる (持ち越すと「いつか必ず 1 回は無傷」という保険になり、
                //   使いどころの判断が消える)。 敵が行動不能のターンは上の分岐で失効させている。
                if (ctx.daggerArmed)
                {
                    if (takenDmg > 0)
                    {
                        int reflect = takenDmg * DaggerReflectMultiplier;
                        Debug.Log($"[仕込み刃] 被ダメ {takenDmg} を無効化 + 軽減不可 {reflect} (×{DaggerReflectMultiplier}) で反撃");
                        takenDmg = 0;
                        enemyHP = Math.Max(0, enemyHP - reflect);
                    }
                    ctx.daggerArmed = false;
                }

                // 古い手紙 (2026-10-31): 使ったターンの被ダメを 0 にする (反撃は無い)。 仕込み刃と同じく
                //   このターン限りで、 被弾しなくても失効する。 仕込み刃が先に 0 にしていれば何もしない。
                if (ctx.GetAccumulated(GameLoop.Consumables.OldLetterGuardKey) > 0f)
                {
                    if (takenDmg > 0) Debug.Log($"[古い手紙] 被ダメ {takenDmg} を無効化");
                    takenDmg = 0;
                    ctx.accumulatedValues.Remove(GameLoop.Consumables.OldLetterGuardKey);
                }

                takenDmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    takenDmg, GameLoop.GameManager.Instance?.Run, playerHP);
                playerHP = Math.Max(0, playerHP - takenDmg);
                takenApplied = takenDmg;
                MetaProgression.MetaDebuffApplicator.NoteDamageTaken(
                    takenDmg, GameLoop.GameManager.Instance?.Run);
                ApplyBreakdownIfNeeded();
                // 〈長引く負傷〉: ラン中の累計被ダメを積む。 **最大HP の減少はここからは行わない** ──
                //   戦闘中に最大HP を削ると、 予告に無い形でその場の生死が変わる。 精算は戦闘終了時。
                if (takenDmg > 0)
                {
                    var lwAccRun = GameLoop.GameManager.Instance?.Run;
                    if (lwAccRun != null) lwAccRun.lingeringWoundDamageAccum += takenDmg;
                }

                // 特殊端子〈反攻〉(§6-5): このターン受けた最終ダメージのうち、
                //   接続合計値までを敵へ返す。 **受けた量が上限**なので、
                //   守り切ったターンは何も返らない ── 殴られる覚悟とセットの端子。
                {
                    int cap = (int)ctx.GetAccumulated(RiposteKey);
                    if (cap > 0 && takenDmg > 0)
                    {
                        int reflect = Math.Min(cap, takenDmg);
                        enemyHP = Math.Max(0, enemyHP - reflect);
                        ctx.enemyCurrentHP = enemyHP;
                        Debug.Log($"[特殊端子/反攻] 被ダメ{takenDmg} のうち {reflect} を反射");
                    }
                }
                ctx.playerDamageThisTurn += takenDmg;
                ctx.AddPlayerDamageSource(ctx.lastDamageCause, takenDmg);
                if (ctx.fixedDamageToPlayer > 0)
                {
                    int fixedTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        ctx.fixedDamageToPlayer, GameLoop.GameManager.Instance?.Run, playerHP);
                    playerHP = Math.Max(0, playerHP - fixedTaken);
                    takenApplied += fixedTaken;
                    ctx.playerDamageThisTurn += fixedTaken;
                    ctx.AddPlayerDamageSource(ctx.lastDamageCause, fixedTaken);
                    MetaProgression.MetaDebuffApplicator.NoteDamageTaken(
                        fixedTaken, GameLoop.GameManager.Instance?.Run);
                }

                // 痛覚遮断剤 (狂戦士) post-clamp (旧法と同一)
                if (ctx.painkillerArmedThisTurn && playerHP <= 0)
                {
                    Debug.Log("[痛覚遮断剤] post-clamp: HP=1 復帰");
                    playerHP = 1;
                    ctx.playerCurrentHP = 1;
                }

                // [計測] 7層でこのターンにプレイヤーが落ちたなら、 出ていた遺物を致死側へ計上する。
                if (playerHP <= 0 && !string.IsNullOrEmpty(ctx.bossId)
                    && ctx.bossId.StartsWith("boss_layer7"))
                    BossDmgDiag.V7Death(ctx.vescaDrawnRelics);

                ctx.playerCurrentHP = playerHP;
                ApplyVescaOnHitEffects(ctx, takenApplied);

                // 反撃固定ダメ (Counter/Riposte 等)
                if (counterFixed > 0)
                    enemyHP = Math.Max(0, enemyHP - counterFixed);

                psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostDealDamage);
            }
            // **旧: else で OnPostReceiveDamage を焚いていた (2026-09-09 撤去)。**
            //   この else は「敵が死んでいて攻撃しなかった」枝で、 被弾の通知には使えない
            //   (死亡ターンにしか鳴らない)。 正しい発火点は上の enemyHP 減算直後へ移した。

            // ============================================================
            //  2 体戦 (2026-08-28)
            // ============================================================
            if (secondaryEnemy != null && secondaryHP > 0)
            {
                // --- (a) 与ダメージは 2 体目にも全額入る ---
                //   標的選択は無い。 撃破ターンは和ではなく max になるので**戦闘長は伸びず**、
                //   エスカレーションも 1 体戦と同じ速さでしか進まない。 増えるのは被ダメだけ。
                int mirrored = Math.Max(0, primaryHpBeforeDamage - enemyHP);
                if (mirrored > 0)
                {
                    secondaryHP = Math.Max(0, secondaryHP - mirrored);
                    if (secondaryHP == 0)
                        Debug.Log($"[2体戦] {secondaryEnemy.displayName} 撃破 (相殺ダメージ {mirrored})");
                }

                // --- (b) 2 体目の攻撃 = 独立した 2 つ目のパケット ---
                //   **ブロックは各パケットから全額引く (倍率なし)。** 1 本のプールを分配すると
                //   防御配線が成立しなくなるため、 2 回引く形を採る (2026-08-28 決定)。
                if (secondaryHP > 0 && playerHP > 0 && !enemyStunnedThisTurn)
                {
                    secondaryAttacked = true;
                    // 予告で開示済みの値をそのまま使う (ここで振り直さない)。
                    int secAtkValue = _secondaryAtkThisTurn;
                    int secLossBase = Math.Max(0, secAtkValue - blockSum);

                    ctx.playerWonRoll = false; ctx.playerLostRoll = true;
                    var (secTaken, _, _) =
                        psm.ProcessDamage(secLossBase, 0, 0, attackerIsEnemy: true);
                    secTaken = ApplyLossDamageModifiers(secTaken, floorMod, ctx);
                    if (ctx.nullifyAllDamage) secTaken = 0;

                    // 役の被ダメ側は 1 体目と**同じ扱い**にする。 片方にしか効かないと
                    //   〈守り〉の価値が 2 体戦でだけ半減してしまう。
                    secTaken = ApplyRoleGuard(ctx, secTaken, roleOutcome);

                    secTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        secTaken, GameLoop.GameManager.Instance?.Run, playerHP);

                    if (secTaken > 0)
                    {
                        playerHP = Math.Max(0, playerHP - secTaken);
                        takenApplied += secTaken;
                        ctx.playerCurrentHP = playerHP;
                        ctx.playerDamageThisTurn += secTaken;
                        ctx.AddPlayerDamageSource(ctx.lastDamageCause, secTaken);
                    }
                    Debug.Log($"[2体戦] {secondaryEnemy.displayName} 攻撃 {secAtkValue} − ブロック {blockSum} "
                            + $"→ 被ダメ {secTaken} (HP {playerHP})");
                }
            }

            // 盾〈黎明〉(OnTurnEnd) が読む: 敵が放った直接攻撃の防御前の合計と、 敵の攻撃で減った HP。
            //   パッシブ由来のダメージ (審判の炎等) は軽減前の側には数えないが、 HP が減っていれば不成立になる。
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.EnemyAtkPreDefense] =
                (primaryAttacked ? enemyAtkBeforeDefense : 0) + (secondaryAttacked ? secondaryAtkBeforeDefense : 0);
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Effects.MutualKeys.HpLossFromEnemy] = takenApplied;

            // 踏みとどまり 2 回目 ── 反射 (返しの盾/コントラタック/鏡写し) と反撃固定ダメは
            //   敵の攻撃解決の**中**でヴェスカの HP を削るため、 1 回目の clamp より後に走る。
            //   1 回目を敵攻撃より前に置くのは、 致死打で `enemyHP > 0` が落ちて
            //   **ヴェスカがその手番を丸ごと飛ばす**のを防ぐため (踏みとどまった以上は殴り返す)。
            //   よってこの 2 点セットで 1 ターン分を覆う。 ClampToThreshold は冪等。
            // 攻撃の後に品が預けた敵へのダメージ (Kw.DamageEnemy) をここで通す。 踏みとどまりの clamp より前。
            {
                int pendingKw = ItemSystem.PassiveSkills.Keywords.Kw.TakePendingEnemyDamage(ctx);
                if (pendingKw > 0) enemyHP = Math.Max(0, enemyHP - pendingKw);
            }
            if (!roleOutcome.yachtFired)
            {
                enemyHP = ItemSystem.PassiveSkills.Effects.DivineTalent.ClampToThreshold(
                    ctx, enemyHP, currentEnemy != null ? currentEnemy.maxHP : ctx.enemyMaxHP);
            }
            ctx.enemyCurrentHP = enemyHP;
            ctx.accumulatedValues[ItemSystem.PassiveSkills.Keywords.Kw.ResolvingKey] = 0;   // 以降は品が HP を直接減らす

            AccumulateRunCombatTotals(ctx);
            CombatStats.RecordTurn(ctx, attackDiceCount, blockDiceCount, chargeDiceCount, blockSum, enemyAtkValue);

            // --- 臨界メーター (Rinkai) 更新 ---
            //   攻撃端子の出目の合計を溜める。 閾値をまたいだ時点で爆発が決まり、 次の攻撃に乗る (Kw.AddHeat)。
            //   品が溜める分 (被弾・充電・会心など) は、 それぞれの出来事の時点で Kw.AddHeat を通る。
            if (ctx.rinkaiThreshold > 0 && attackSum > 0)
                ItemSystem.PassiveSkills.Keywords.Kw.AddHeat(ctx, attackSum);

            // --- 収支トリガー (柱2: OnRollWin/Lose の再定義 = 収支±のターン。解決後に発火) ---
            int balance = (dealtDmg + dealtFixed) - takenApplied;
            // [計装] 対象層のボス戦だけターン単位で残す (BossCombatTrace)。 既定は無効。
            //   **収支が確定した後**に置く ── 与ダメ/被ダメの最終値がここで揃う。
            BossCombatTrace.NoteTurn(ctx.currentTurn, traceAtk, traceBlock, traceThrough,
                takenApplied, dealtDmg + dealtFixed, playerHP, enemyHP,
                traceEBase, traceERoll, traceEsc, attackSum, actualPlayerDiceCount,
                traceRaw, traceBonus, traceBossBonus,
                currentEnemy != null ? currentEnemy.maxHP : 0);
            ctx.playerWonRoll = balance > 0;
            ctx.playerLostRoll = balance < 0;
            result.playerWon = balance > 0;
            result.isDraw = balance == 0;
            Debug.Log($"[相互攻撃] T{ctx.currentTurn} 収支 {(balance >= 0 ? "+" : "")}{balance} " +
                      $"(与 {dealtDmg + dealtFixed} / 被 {takenApplied})");

            if (ctx.playerWonRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollLose);
            else if (ctx.playerLostRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollWin);
            else
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollDraw);

            // --- 7. ターン終了処理 (共通終端) ---
            var turnResult = FinishTurnCommon(result, ctx);

            // 逃げる: 生き残っていればここで戦闘を抜ける (敵を倒してしまった・倒れた場合は通常の決着)。
            if (_fleeRequested)
            {
                _fleeRequested = false;
                if (isCombatActive && playerHP > 0 && enemyHP > 0)
                {
                    LastCombatFled = true;
                    Debug.Log($"[逃げる] 戦闘から離脱 (HP {playerHP})");
                    FinishCombat();
                }
            }
            _stepResult = turnResult;
        }

        // ===========================================================
        //  ダメージ修飾チェーン（ExecuteTurn から分離。順序が結果を左右するため厳守）
        // ===========================================================

        /// <summary>[11]パッシブ由来の outgoing [12]業物 [13]メタ [14]<b>欠番</b>(旧 副次・2026-09-15 全廃)
        /// [15]遺物 [16]最終 outgoing</summary>

        private readonly double[] _bdAtCombatStart = new double[19];
        private bool _bd7F;

        /// <summary>ターンの被弾をランの累計へ足す (ゲーム処理)。
        /// ラン内の実被弾は航行の危険度見積りが読む (RunState.AverageFightDamage)。
        /// **ターンごとに足す** ── 戦闘開始/終了の HP 差だと回復で相殺されて過小になる。</summary>
        private static void AccumulateRunCombatTotals(CombatContext ctx)
        {
            var drun = GameLoop.GameManager.Instance?.Run;
            if (ctx == null || drun == null) return;
            drun.fightDamageSum += ctx.playerDamageThisTurn;
            if (ctx.currentEnemyKind != EnemyKind.Boss)
            {
                drun.navTurns++;
                drun.navDealt += Math.Max(0, ctx.lastPlayerDamage);
                drun.navTaken += Math.Max(0, ctx.playerDamageThisTurn);
            }
        }
    }
}
