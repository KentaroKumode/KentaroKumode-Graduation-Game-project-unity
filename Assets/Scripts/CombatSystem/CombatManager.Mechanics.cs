using System;
using System.Collections.Generic;
using UnityEngine;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>CombatManager: ターン内の各機構 (敵の攻撃ロール・ダイス・防御・リロール・ラストスタンド・役・特殊端子)。</summary>
    public partial class CombatManager
    {
        /// <summary>敵の攻撃ロール。 2026-07-28 以降の敵は **ダイスを振らず**、
        /// enemies.json の `attackRollMin`〜`attackRollMax` から一様乱数を 1 個引く (§6 の「波」)。
        /// 旧データ (attackRollMax 未設定) はダイス方式へ自動フォールバックする。
        ///
        /// - `extraDraws`: 旧「敵ダイス数+N」効果の受け皿。 同じ範囲から追加で引いて足す。
        /// - 弱ロール (ADR-0005): **上限だけ**を比率で縮める。 下限は据え置きなので
        ///   「弱いターンは上振れが消える」という意味になり、 面を一律縮小していた旧挙動と等価。
        /// 返り値が配列なのは LED 表示と enemyDice を読む既存パッシブの互換のため。</summary>
        /// <summary>〈貫きの錐〉: 敵の攻撃値 1 あたりに削るブロック量。</summary>
        public const int PierceAwlPerAttack = 2;

        /// <summary>**ラン内の**戦闘通し番号。 決定論的乱数のキーを戦闘ごとに分けるために使う。
        ///
        /// **ラン開始時に必ず 0 へ戻すこと** (<see cref="ResetRunCombatSequence"/>)。
        /// 2026-08-10 まではリセットが無く、 シングルトンなのでバッチ全体で増え続けていた。
        /// その結果 <see cref="RngIdx"/> が「そのランがバッチの何番目か」に依存し、
        /// **同じシード・同じ runIdx でも戦闘の乱数列が変わっていた**。
        /// 実測: 設定が同一の 2 アームで 94% のランが別結果、 7層クリアが 4.0pt 振れた。
        /// マップ/エンカウント/報酬は別キーなので一致し、 **戦闘に入った瞬間だけ分岐**する
        /// ── 症状が戦闘に限局するので、 集計値だけを見ていると気づけない。</summary>
        private int _combatSeq;

        /// <summary>ラン開始時に呼ぶ。 戦闘通し番号を 0 に戻す。
        /// これを飛ばすと同一シードのペア比較が成立しない (上記)。</summary>
        public void ResetRunCombatSequence() => _combatSeq = 0;

        /// <summary>決定論的乱数の連番。 (戦闘番号, ターン, スロット) で一意になる。
        /// slot は同一ターン内で複数回引く場合の識別子 (ダイスの本数など)。</summary>
        private int RngIdx(int slot)
            => (_combatSeq * 1000 + Math.Max(0, CurrentCombatTurn)) * 100 + slot;

        /// <summary>7層ヴェスカ 4 段連戦の段移行。 現在の敵 id から次段を引き、 あれば SwapEnemy する。
        /// 〈連続実験〉パッシブ (OnTurnEnd) の代替ではなく **最終防衛線**。
        /// 最終段 (p4) には次が無いので何もしない ＝ そこで初めて 7 層クリアになる。</summary>
        private bool TryAdvanceVescaChain()
        {
            string id = currentEnemy?.id;
            if (string.IsNullOrEmpty(id)) return false;
            string next = null, label = null;
            switch (id)
            {
                case "boss_layer7":    next = "boss_layer7_p2"; label = "第二段：遺物学者"; break;
                case "boss_layer7_p2": next = "boss_layer7_p3"; label = "第三段：神話";     break;
                case "boss_layer7_p3": next = "boss_layer7_p4"; label = "第四段：天与";     break;
                default: return false;   // p4 または非ヴェスカ
            }
            Debug.Log($"[ヴェスカ連戦] {id} 撃破 → {label} へ移行 (戦闘終了前に確定)");
            var ctx = PassiveSkillManager.Instance?.Context;
            // 計測は SwapEnemy 側で一元化 (ここで重ねると二重計上になる)
            SwapEnemy(next, label);
            if (ctx != null) SyncHPFromContext(ctx);
            return true;
        }

        private int[] RollEnemyAttack(EnemyData e, int extraDraws, bool weakRoll, float weakRatio)
        {
            if (e == null) return new int[0];
            if (e.UsesAttackRoll)
            {
                int hi = weakRoll ? EnemyStance.WeakRollMax(e.attackRollMax, weakRatio) : e.attackRollMax;
                return e.RollAttack(extraDraws, hi);
            }
            // ---- 旧ダイス方式 (互換パス) ----
            int[] faces = e.diceFaces;
            if (faces != null && faces.Length > 0)
                return RollDice(Math.Max(0, e.diceCount + extraDraws), 0,
                                weakRoll ? EnemyStance.WeakRollFaces(faces, weakRatio) : faces);
            int mx = weakRoll ? EnemyStance.WeakRollMax(e.diceMaxValue, weakRatio) : e.diceMaxValue;
            return RollDice(Math.Max(0, e.diceCount + extraDraws), mx);
        }

        private int[] RollDice(int count, int maxValue, int[] diceFaces = null)
            => RollDice(count, maxValue, diceFaces, out _);

        /// <summary>ロールして、 **どの面に止まったか (添字)** も返す。
        ///
        /// <para>出目パーツ (2026-08-17) は「面の個体」に付くので、 値だけでは効果を引けない
        /// ── 素の <c>1</c> とパーツの <c>1</c> は別物。 面配列を使わない素のロールでは
        /// <paramref name="faceIdx"/> は <c>-1</c>。</para>
        ///
        /// <para><b>乱数の呼び方は変えていない。</b> 既に <c>Range</c> の戻り値で添字を引いていたので、
        /// それを捨てずに控えるだけ。 決定論シードの再現性は保たれる。</para></summary>
        private int[] RollDice(int count, int maxValue, int[] diceFaces, out int[] faceIdx)
        {
            var results = new int[count];
            faceIdx = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (diceFaces != null && diceFaces.Length > 0)
                {
                    int idx = GameLoop.GameRng.Range(0, diceFaces.Length, "combat.dieFace", RngIdx(30 + i));
                    faceIdx[i] = idx;
                    results[i] = diceFaces[idx];
                }
                else
                {
                    faceIdx[i] = -1;
                    results[i] = GameLoop.GameRng.Range(1, maxValue + 1, "combat.die", RngIdx(30 + i));
                }
            }
            return results;
        }

        /// <summary>プレイヤーの防御で敵の攻撃値を軽減する (ADR-0013)。 <b>ブロックの前</b>に掛け、 予告にもこの値を出す。
        /// <paramref name="pen"/> はその攻撃の主がプレイヤーの防御を削る固定値。 剣〈間合〉の打ち消し
        /// (<c>enemyArmorPenNegate</c>) を先に引く。 斧〈血令〉持ちは防御を貫通へ換えているので掛けない
        /// (リロール判断用の仮予告は換算より前に組まれるため、 ここでも見る)。</summary>
        private static int ApplyPlayerDefense(CombatContext ctx, int enemyAtk, int pen)
        {
            if (ctx == null || enemyAtk <= 0) return enemyAtk;
            if (ItemSystem.PassiveSkills.Effects.BloodEdict.IsActive(ctx)) return enemyAtk;
            int effPen = Math.Max(0, pen - ctx.enemyArmorPenNegate);
            return DefenseFormula.Apply(enemyAtk, DefenseFormula.Effective(ctx.playerDefense, effPen));
        }

        /// <summary>ADR-0010: リロール段階。 **充電を払ってダイスを振り直す**。
        ///
        /// コストは `振り直す個数 × そのターンで何回目か`。 回数で逓増するので
        /// 「全部振り直しを 2 回」は上限 30 では届かず、 **狙った少数を刻む方が効率的**になる。
        /// これがヨットの「何を残すか」をコスト面からも支える。
        ///
        /// 振り直す対象は <see cref="RerollPolicy"/> が決める (BOT / UI が差す)。
        /// null なら振り直さない ── 旧実装は物資消費の自動ポリシーだったが、
        /// リロールのたびに発狂へ近づく形だとヨットの中核が経済ペナルティに潰される (§design-yacht)。
        ///
        /// <para>人が操作するターン (<c>_interactiveTurn</c>) では、 方策を呼ぶ位置で止まって「振り直すか」を待つ。</para>
        /// </summary>
        private IEnumerator<TurnPause> RerollPhase(CombatContext ctx, int[] playerDice, int[] enemyDice, int playerDiceMax,
                                                   int[] playerFaceIdx = null)
        {
            if (ctx == null || playerDice == null || playerDice.Length == 0) yield break;
            if (RerollPolicy == null && !_interactiveTurn) yield break;
            // 強制ロール中は対象外 (出た目が仕様で固定されているため)
            if (ctx.GetAccumulated("player_contre") > 0) yield break;

            // 予告を仮組みする。 敵ダイスはこの後変わらないので素の合計で作れる。
            int rawEnemyTotal = 0;
            if (enemyDice != null) for (int i = 0; i < enemyDice.Length; i++) rawEnemyTotal += enemyDice[i];
            var tele = new MutualTurnTelegraph
            {
                turn = ctx.currentTurn,
                enemyAttackValue = ApplyPlayerDefense(ctx,
                    Escalation.EnemyAttackValue(currentEnemy, EscalationTurn(ctx), rawEnemyTotal, ctx.currentTurn),
                    ctx.enemyArmorPen),
                escalationStage = Escalation.StageOf(EscalationTurn(ctx)),
                nextThresholdTurn = Escalation.NextThresholdTurn(EscalationTurn(ctx)),
                turnsToHeavy = Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn),
                heavyMul = currentEnemy?.heavyMul ?? 0f,
                enemyDice = enemyDice,
                enemyDiceTotal = rawEnemyTotal,
            };

            // 〈特別・対〉(前ターン成立): 次のリロール N 回 (既定 2) を無料にする。
            int freeRerolls = ctx.currentBuffs.TryGetValue(YachtRoleEffects.FreeRerollKey, out float freeFlag)
                ? Mathf.Max(0, (int)freeFlag) : 0;

            // **全部振り直し・費用は戦闘の中で上がる (2026-10-17)。**
            //   旧規則は「選んだダイスだけ・費用 = 個数 × そのターンで何回目か」で、 回目がターンごとに戻るうえ
            //   1〜2 個を刻んで追えたので、 充電さえあれば役がほぼ任意に作れた (〈極〉が設計 0.077%/T → 充電 12 で約 10%)。
            //   いまは (1) 方策の pick は「振るかどうか」だけを表し、 中身は読まない ── 振り直せない面以外を全部振る
            //   (2) 費用 = 振り直す個数 × (この戦闘で払った回数 + 1)。 ターンでは戻らない。 docs/GAME.md §6-3。
            int attempt = 1;   // このターンの何回目か (乱数の添字にだけ使う)
            int guard = 16;    // 方策が空でない配列を返し続けても止まるようにする
            var rerollIdx = new List<int>(playerDice.Length);
            while (guard-- > 0)
            {
                int nth = ctx.rerollsThisCombat + 1;
                int[] pick;
                if (_interactiveTurn)
                {
                    _pause.Set(TurnPauseKind.Reroll, playerDice, playerFaceIdx, tele, mutualWiring);
                    _pause.rerollCost = freeRerolls > 0 ? 0 : playerDice.Length * nth;
                    yield return _pause;
                    pick = _pause.reroll ? AllDiceIndex(playerDice.Length) : null;
                }
                else pick = RerollPolicy(playerDice, tele, nth);
                if (pick == null || pick.Length == 0) break;

                // ADR-0011: 振り直せない面は無い。 常に全ダイスを振り直す。
                rerollIdx.Clear();
                for (int i = 0; i < playerDice.Length; i++) rerollIdx.Add(i);

                int cost = rerollIdx.Count * nth;
                bool usedFree = false;
                if (freeRerolls > 0) { cost = 0; freeRerolls--; usedFree = true; }
                if (!ctx.ConsumeCharge(cost)) break;   // 払えなければそこで打ち切り

                for (int k = 0; k < rerollIdx.Count; k++)
                {
                    int i = rerollIdx[k];
                    if (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
                    {
                        int idx = GameLoop.GameRng.Range(
                            0, ctx.equippedDiceFaces.Length, "combat.rerollFace", RngIdx(20 + i + attempt * 8));
                        playerDice[i] = ctx.equippedDiceFaces[idx];
                        if (playerFaceIdx != null && i < playerFaceIdx.Length) playerFaceIdx[i] = idx;
                    }
                    else
                    {
                        playerDice[i] = GameLoop.GameRng.Range(1, playerDiceMax + 1, "combat.reroll", RngIdx(20 + i + attempt * 8));
                        if (playerFaceIdx != null && i < playerFaceIdx.Length) playerFaceIdx[i] = -1;
                    }
                }
                if (!usedFree) ctx.rerollsThisCombat++;
                ctx.rerollsThisTurn++;
                CombatStats.RerollStats[0]++; CombatStats.RerollStats[1] += rerollIdx.Count; CombatStats.RerollStats[2] += cost;
                MetaProgression.Achievements.AchievementService.NoteReroll();
                Debug.Log($"[リロール] 戦闘{ctx.rerollsThisCombat}回目 {rerollIdx.Count}個 (全部) / 充電-{cost} → 残{ctx.GetCharge()}");
                attempt++;
            }

            // [計装] このターンで何回刻んだか。 attempt は「次に何回目か」なので −1 が実回数。
            int did = Math.Max(0, attempt - 1);
            CombatStats.RerollPerTurn[Math.Min(8, did)]++;
            // 〈極〉が成立している手なら、 極側の分布にも積む
            // （振り直しが極の追跡に使われているかを、 全体分布との形の差で見る）。
            if (YachtRoles.MaxSameCount(playerDice) >= 3)
                CombatStats.RerollPerTurnYacht[Math.Min(8, did)]++;
        }

        /// <summary>ラストスタンド (メタ〈外殻〉r10): HP 0 なら<b>その場で蘇生して戦闘を続ける</b>。
        ///
        /// <para>2026-09-13 に戦闘終了後の救済チェーン
        /// (<see cref="GameLoop.LastStand.TryConsumeRevival"/>) からここへ移した。
        /// あちらは「戦闘を畳んでマップへ戻る」経路で、 <b>ボスノードは出力接続が無い</b>ため
        /// ボス戦で発動したランが移動先なしで詰んでいた (300 ラン中 61 件 = 20.3%)。
        /// 灯火とフルーレがボス戦で発動しないのは元々この理由で、
        /// ラストスタンドだけ解禁したのに退却経路は共通のままだった。</para>
        ///
        /// <para><b>呼ぶ位置。</b> 致死が確定するより前。 戦闘を閉じる判定の手前であれば、
        /// ターン終了の被ダメ適用後でもターン開始の刻限清算後でもよい ──
        /// <b>死因によって蘇生したりしなかったりする形にしないこと。</b></para></summary>
        private void TryLastStandRevive(CombatContext ctx)
        {
            if (playerHP > 0) return;
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return;
            bool isBoss = MapSystem.MapManager.Instance?.CurrentNode != null
                && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Boss;
            if (!GameLoop.LastStand.TryConsumeLastStandInCombat(run, isBoss)) return;

            playerMaxHP = run.playerMaxHP;
            playerHP = run.playerHP;
            if (ctx != null)
            {
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
            }
        }

        /// <summary>T4-A〈破綻〉: HP が閾値を下回っていたら**その時点の最大HP を半分にする**。
        ///
        /// 発動後は HP が新しい最大HP の 38% 相当になるので、 **その場で連鎖しない**。
        /// 次の発動は新しい最大HP の 20% を割ったときで、 踏むたびに難しくなる。
        /// 現在HP は下げない ── 半減で最大HP を下回ったぶんだけ切り詰める。</summary>
        private void ApplyBreakdownIfNeeded()
        {
            float th = MetaProgression.MetaDebuffApplicator.GetBreakdownThreshold();
            if (th <= 0f || playerHP <= 0) return;
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null || run.playerMaxHP <= 1) return;
            // **回数制限なし** (2026-08-11 差し戻し)。 1 回限りにしたところ 1000 ラン で
            //   590 回発動してなお T4 コスト −2.2pt しかなかった ── HP 20% を割る局面は
            //   既に負けが決まっており、 そこで 1 度だけ最大HP を半分にしても結果が動かない。
            //   複数回に戻すと「踏むたびに次の閾値が下がる」自己減速はそのままに、
            //   立て直しの余地だけが削られる。
            if (playerHP >= Mathf.CeilToInt(run.playerMaxHP * th)) return;

            int before = run.playerMaxHP;
            run.playerMaxHP = Math.Max(1, run.playerMaxHP / 2);
            run.breakdownCount++;
            MetaProgression.MetaDebuffApplicator.BreakdownTriggers++;
            run.playerHP = Math.Min(playerHP, run.playerMaxHP);
            playerMaxHP = run.playerMaxHP;
            playerHP = run.playerHP;
            Debug.Log($"[破綻] HP {playerHP} が閾値 {th:P0} を割った → 最大HP {before} → {run.playerMaxHP}"
                    + $" (通算 {run.breakdownCount} 回目)");
        }

        /// <summary>T4-E〈最後の審判〉のラン累計戦闘ターンを進める。
        /// 刻限後の罰は各被ダメージの最終適用時に乗せる。</summary>
        private void ApplyFinalJudgment()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return;
            run.totalCombatTurns++;
            MetaProgression.MetaDebuffApplicator.JudgmentTurnsTotal++;
            int th = MetaProgression.MetaDebuffApplicator.GetJudgmentTurnThreshold();
            // 刻限の分布は**デバフの有無に関わらず**採る (閾値を決める材料なので)。
            if (th <= 0)
            {
                const int ProbeThreshold = 120;
                if (run.totalCombatTurns == ProbeThreshold)
                    MetaProgression.MetaDebuffApplicator.JudgmentRunsOverThreshold++;
                return;
            }
            if (run.totalCombatTurns == th)
                MetaProgression.MetaDebuffApplicator.JudgmentRunsOverThreshold++;
            if (run.totalCombatTurns == th)
                Debug.Log($"[最後の審判] 刻限到来 (累計{run.totalCombatTurns}T >= {th}) → 以後の被ダメージ×1.30");
        }

        // 役の判定で使う一時バッファ。 毎ターン new すると GC を踏むので使い回す。
        private readonly List<RoleKind> _formedRoles = new List<RoleKind>(4);

        /// <summary>ADR-0011: 役を判定し、 成立したものを<b>自動で</b>適用する。
        ///
        /// 役は振った時点の 3 個で判定し、 端子への置き方は条件にしない。 1 戦闘 1 回の制限も、
        /// 発動の任意選択も、 使用済みの管理も持たない (ADR-0011 決定 2)。
        /// 成立するかどうかは「その目の面を持っているか」(出目パーツの Tier) で決まる ──
        /// T2 以上で 2 個そろい、 T3 以上でゾロ目。 T1 (素の面) は何も起きない。
        /// 3 個では 2 つの役を同時に作れないので、 効くのは 1 ターンに高々 1 つ。</summary>
        private RoleOutcome ResolveRoles(CombatContext ctx, int[] playerDice, int[] playerFaceIdx)
        {
            var outcome = new RoleOutcome();
            if (ctx == null || playerDice == null || playerDice.Length == 0) return outcome;

            YachtRoleEffects.TurnsEvaluated++;
            _formedRoles.Clear();
            YachtRoles.Evaluate(playerDice, playerFaceIdx, ctx.equippedFaceTiers, _formedRoles);

            for (int i = 0; i < _formedRoles.Count; i++)
            {
                var k = _formedRoles[i];
                YachtRoleEffects.FormedCount[(int)k]++;
                // T4-C〈凶運〉: このランで封印されている役は成立しても効かない。
                if ((ctx.sealedRoleMask & (1 << (int)k)) != 0) continue;

                // その目の面の Tier (同じ目の面は 1 枚までなので、 同じ出目なら同じ Tier)。
                int face = YachtRoles.FaceOf(k);
                var tier = GameLoop.DiceFaceParts.Tier.None;
                for (int d = 0; d < playerDice.Length; d++)
                    if (playerDice[d] == face) { tier = YachtRoles.TierAt(ctx.equippedFaceTiers, playerFaceIdx, d); break; }

                YachtRoleEffects.Apply(k, tier, ctx, ref outcome);
                ctx.firedRolesThisTurn.Add(k);
                YachtRoleEffects.FiredCount[(int)k]++;
                // [計装 2026-08-15] ダイス別の発動は戦闘単位で FinishCombat が積む
                //   (役の発動地点から GameManager.Instance を引かないため)。
                YachtRoleEffects.FiredThisCombat[(int)k]++;
                MetaProgression.Achievements.AchievementService.NoteRoleFired(k);
            }

            if (ctx.firedRolesThisTurn.Count > 0)
            {
                var names = new string[ctx.firedRolesThisTurn.Count];
                for (int i = 0; i < names.Length; i++) names[i] = YachtRoles.NameOf(ctx.firedRolesThisTurn[i]);
                Debug.Log($"[役] T{ctx.currentTurn} 発動: {string.Join("/", names)}");
            }
            return outcome;
        }

        /// <summary>配線の後始末。 **方策や UI が返した配線を信用しない**。
        ///
        ///   ① 特殊端子 (§6-5) の接続制限 N を超えた分を攻撃へ落とす。 未装着なら N=0 ＝ 全部落とす
        ///   ② 挑戦デバフ〈不器用〉の「同時に配線できる端子の上限」を強制する
        ///
        /// ② は **使う端子を減らす**ので、 どれを捨てるかで結果が変わる。
        /// 上限に収まらなかった端子のうち **本数の少ない方から**畳んで攻撃へ寄せる ──
        /// 厚く挿した端子ほどプレイヤーの意図が強いとみなす。</summary>
        private void SanitizeWiring(DiceTerminal[] wiring, int diceCount)
        {
            if (wiring == null) return;
            int n = Math.Min(diceCount, wiring.Length);

            // ① 特殊端子の接続制限
            int limit = SpecialTerminals.EquippedLimit(GameLoop.GameManager.Instance?.Run);
            int used = 0;
            for (int i = 0; i < n; i++)
            {
                if (wiring[i] != DiceTerminal.Special) continue;
                if (used < limit) { used++; continue; }
                wiring[i] = DiceTerminal.Attack;
            }

            // ② 〈綻び〉: 封印された端子へ挿さっているダイスを追い出す。
            //   **方策/UI を信用しない** ── 封印を破った配線が来たら別の端子へ落とす。
            {
                int kinds = limit > 0 ? 4 : 3;
                int sealed_ = MetaProgression.MetaDebuffApplicator.GetSealedTerminal(
                    PassiveSkillManager.Instance?.Context?.currentTurn ?? 0, kinds);
                if (sealed_ >= 0)
                {
                    // 逃がし先は攻撃。 攻撃が封印されているなら充電へ (最も無害な受け皿)。
                    var fallback = (sealed_ == (int)DiceTerminal.Attack)
                                 ? DiceTerminal.Charge : DiceTerminal.Attack;
                    for (int i = 0; i < n; i++)
                        if ((int)wiring[i] == sealed_) wiring[i] = fallback;
                }
            }
            // (旧〈不器用〉の端子上限は撤去済み。)
            //   2026-08-10 リワーク前は「同時に使える端子の種類数の上限」で、 ここで畳んでいた。
            //   新効果は**厚みへの後払い**なので、 配線を検閲せず ResolveClumsyOverload が
            //   次ターンの合計へ罰を掛ける。 配線の自由は残す。

            // (〈不完全な起動〉の端子ごとの本数上限は 2026-09-14 に撤去。
            //   5 ダイスなら 2+2+1 で置けてしまい全振りとの差が小さく、
            //   1 本上限にすると 3 端子へ 5 本置けず**配線不能**になる。
            //   罰は賽の本数 (GateFlaws.DiceCountAfterIgnition) へ移した。)
        }

        /// <summary>特殊端子 (§6-5) の効果を適用する。 効果表の正本は
        /// <see cref="SpecialTerminals"/> と docs/GAME.md §6-5。
        ///
        /// **特殊端子は端子役 (ADR-0010) を成立させない。** 固有効果と接続制限を既に持っており、
        /// そこへ役まで乗せると同じダイスで二重に報われる。 接続制限が最大 3 本なので
        /// 分布系の役 (3 本以上) がちょうど成立してしまう点も避けたい。</summary>
        private void ApplySpecialTerminal(CombatContext ctx, int[] dice, int sum, int count,
                                          ref int attackSum, ref int blockSum, ref int chargeSum)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            var def = SpecialTerminals.Equipped(run);
            if (def == null || ctx == null || sum <= 0) return;

            switch (def.kind)
            {
                case SpecialTerminalKind.HeavyStrike:
                {
                    int add = Mathf.CeilToInt(sum * SpecialTerminals.HeavyStrikeMultiplier);
                    attackSum += add;
                    Debug.Log($"[特殊端子/重攻撃] 出目{sum} ×{SpecialTerminals.HeavyStrikeMultiplier} → 攻撃 +{add}");
                    break;
                }
                case SpecialTerminalKind.FullGuard:
                {
                    int add = sum * SpecialTerminals.FullGuardMultiplier;
                    blockSum += add;
                    Debug.Log($"[特殊端子/完全防御] 出目{sum} ×{SpecialTerminals.FullGuardMultiplier} → ブロック +{add}");
                    break;
                }
                case SpecialTerminalKind.Bleed:
                {
                    ctx.enemyBleedStacks += sum;
                    // 接続**本数**ぶんだけ即時に発動させる (合計値ではない ── 制限Ⅱなので最大 2 回)
                    for (int k = 0; k < count && ctx.enemyBleedStacks > 0; k++)
                    {
                        int dmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                        enemyHP = Math.Max(0, enemyHP - dmg);
                        ctx.enemyCurrentHP = enemyHP;
                    }
                    Debug.Log($"[特殊端子/出血] +{sum} スタック (計 {ctx.enemyBleedStacks}) / 即時 {count} 回");
                    break;
                }
                case SpecialTerminalKind.Heal:
                    HealPlayer(sum);
                    Debug.Log($"[特殊端子/治癒] HP +{sum}");
                    break;

                case SpecialTerminalKind.Deathwish:
                {
                    int missing = Math.Max(0, playerMaxHP - playerHP);
                    int bonus = Mathf.FloorToInt(missing * SpecialTerminals.DeathwishMissingHpPct);
                    // 自傷は **HP を 1 未満にしない** ── 端子を挿しただけで死ぬのは選択にならない
                    int selfDmg = Math.Min(sum, Math.Max(0, playerHP - 1));
                    playerHP -= selfDmg;
                    ctx.playerCurrentHP = playerHP;
                    attackSum += sum + bonus;
                    Debug.Log($"[特殊端子/決死] 自傷{selfDmg} → 攻撃 +{sum}+{bonus}(不足HP{missing}の5%)");
                    break;
                }
                case SpecialTerminalKind.VitalPoint:
                {
                    int min = int.MaxValue;
                    for (int i = 0; i < dice.Length; i++) if (dice[i] < min) min = dice[i];
                    // 接続したダイスが **すべてそのロールの最小値** のときだけ発動
                    bool allMin = true;
                    for (int i = 0; i < dice.Length && allMin; i++)
                        if (i < mutualWiring.Length && mutualWiring[i] == DiceTerminal.Special
                            && dice[i] != min) allMin = false;
                    if (allMin)
                    {
                        int dmg = sum * SpecialTerminals.VitalPointMultiplier;
                        ctx.fixedDamageToEnemy += dmg;
                        Debug.Log($"[特殊端子/急所] 最小値{min} を接続 → 軽減不可 {dmg}");
                    }
                    break;
                }
                case SpecialTerminalKind.Aim:
                {
                    int pts = Mathf.CeilToInt(sum / 2f);
                    ctx.critRatePctAdd += pts / 100f;
                    DmgSourceDiag.Note(DmgSourceDiag.AimKey, DmgSourceDiag.CritRate, pts / 100f);
                    Debug.Log($"[特殊端子/照準] 会心率 +{pts}pt");
                    break;
                }
                case SpecialTerminalKind.Battery:
                    chargeSum += sum + count * SpecialTerminals.BatteryPerDice;
                    Debug.Log($"[特殊端子/蓄電池] 充電 +{sum} +{count * SpecialTerminals.BatteryPerDice}");
                    break;

                case SpecialTerminalKind.Coolant:
                {
                    float acc = ctx.GetAccumulated(CoolantAccKey) + sum;
                    int gained = 0;
                    while (acc >= SpecialTerminals.CoolantThreshold)
                    { acc -= SpecialTerminals.CoolantThreshold; gained++; }
                    ctx.accumulatedValues[CoolantAccKey] = acc;
                    if (gained > 0)
                    {
                        ctx.accumulatedValues[CoolantDelayKey] =
                            ctx.GetAccumulated(CoolantDelayKey) + gained;
                        Debug.Log($"[特殊端子/冷却材] エスカレーションを {gained}T 遅延 "
                                + $"(累計 {(int)ctx.GetAccumulated(CoolantDelayKey)}T)");
                    }
                    break;
                }
                case SpecialTerminalKind.Foundry:
                {
                    float acc = ctx.GetAccumulated(FoundryAccKey) + sum;
                    int gained = 0;
                    while (acc >= SpecialTerminals.FoundryThreshold)
                    { acc -= SpecialTerminals.FoundryThreshold; gained++; }
                    ctx.accumulatedValues[FoundryAccKey] = acc;
                    if (gained > 0)
                    {
                        ctx.accumulatedValues[FoundryBonusKey] =
                            ctx.GetAccumulated(FoundryBonusKey) + gained;
                        Debug.Log($"[特殊端子/鋳造口] この戦闘中の出目 +{gained} "
                                + $"(累計 +{(int)ctx.GetAccumulated(FoundryBonusKey)})");
                    }
                    break;
                }
                case SpecialTerminalKind.Riposte:
                    // 反射量の上限を積むだけ。 実際の反射は被ダメ確定後に行う
                    ctx.accumulatedValues[RiposteKey] = sum;
                    break;
            }
        }

        /// <summary>特殊端子が accumulatedValues に置くキー。 **フィールドを増やさない**
        /// (CombatContext の 3 区分規約: set/read が 1〜2 箇所なら辞書へ)。</summary>
        private const string CoolantAccKey   = "termCoolantAcc";
        /// <summary>エスカレーションの遅延ターン数のキー。 〈冷却材〉と役〈時間〉(目 6) が共有する。</summary>
        public const string CoolantDelayKey = "termCoolantDelay";
        private const string FoundryAccKey   = "termFoundryAcc";
        private const string FoundryBonusKey = "termFoundryBonus";
        private const string RiposteKey      = "termRiposteCap";

        /// <summary>端子上限の畳み込みで使う本数バッファ。 毎ターン new しないため使い回す。</summary>
        private readonly int[] _termCountBuf = new int[4];

        /// <summary>ADR-0011 〈守り〉(目 5): このターンの被ダメを N% 減らす。 T4 は減らした分の半分をシールドにする。
        /// 〈虚空〉と同じ位置・同じ対象 (敵の直接攻撃パケット) に置く ── fixedDamageToPlayer は別チャネルなので触らない。</summary>
        private int ApplyRoleGuard(CombatContext ctx, int takenDmg, RoleOutcome outcome)
        {
            if (outcome.damageTakenCutPct <= 0 || takenDmg <= 0) return takenDmg;
            int before = takenDmg;
            takenDmg = Mathf.Max(0, Mathf.RoundToInt(takenDmg * (1f - outcome.damageTakenCutPct / 100f)));
            int saved = before - takenDmg;
            Debug.Log($"[役] 守り: 被ダメ {before} → {takenDmg} (-{outcome.damageTakenCutPct}%)");
            if (outcome.guardToShield && saved / 2 > 0 && ctx != null)
            {
                int sh = saved / 2;
                ctx.consShield += sh;
                ctx.shieldGainedTotal += sh;
                ShieldDiag.Note("役〈守り〉", sh);
            }
            return takenDmg;
        }

        /// <summary>ADR-0011 〈追撃〉(目 3): 与えたダメージの 30% (2 個そろい) / 100% (ゾロ目) を追加で与える。
        /// ゾロ目は<b>確定</b>ダメージ (固定ダメ枠・軽減無視)、 2 個そろいは通常ダメージへの上乗せ。
        /// T4 は追加分が会心判定を持つ。 会心率は本判定 (<c>PassiveSkillManager</c> の会心) と同じ組み立て:
        /// 武器 + このターンの加算 + 整備パネル − 〈俊敏〉。 <c>critRatePctAdd</c> は割合 (0.15 = 15%) なので 100 で割らない。</summary>
        private void ApplyRoleExtraDamage(CombatContext ctx, ref int dealtDmg, ref int dealtFixed, RoleOutcome outcome)
        {
            if (outcome.extraDamageRatio <= 0f) return;
            int basis = dealtDmg + dealtFixed;
            if (basis <= 0) return;
            float extra = basis * outcome.extraDamageRatio;
            bool crit = false;
            if (outcome.extraDamageCanCrit)
            {
                float critAdd = playerCritBaseRate + ctx.critRatePctAdd
                              + MetaProgression.MetaBuffApplicator.GetCritRatePctBonus();
                critAdd = Mathf.Max(0f, critAdd - MetaProgression.MetaDebuffApplicator.GetPlayerCritRatePenalty());
                float rate = ItemSystem.PassiveSkills.CombatContext.ResolveCritRate(critAdd);
                // 本判定と同じ優先順: 封印 (無心の刃) で外れ、 forceCritical が最後に確定で上書きする。
                crit = (!ctx.critSuppressed && GameLoop.GameRng.Chance(rate, "combat.roleExtraCrit", RngIdx(3)))
                    || ctx.forceCritical;
                if (crit) extra *= ctx.criticalMultiplier;
            }
            int add = Mathf.Max(1, Mathf.RoundToInt(extra));
            if (outcome.extraDamageTrue) dealtFixed += add; else dealtDmg += add;
            Debug.Log($"[役] 追撃: 与ダメ {basis} の {outcome.extraDamageRatio:P0} = +{add}"
                    + (outcome.extraDamageTrue ? " (確定)" : "") + (crit ? " 会心" : ""));
        }

        /// <summary>エスカレーション判定に使うターン。 〈冷却材〉の遅延を差し引く。
        /// **1 を下回らせない** ── 段階 0 より前は存在しない。</summary>
        private int EscalationTurn(CombatContext ctx)
        {
            if (ctx == null) return 1;
            int delay = (int)ctx.GetAccumulated(CoolantDelayKey);
            return Math.Max(1, ctx.currentTurn - delay);
        }

        private void SyncHPFromContext(CombatContext ctx)
        {
            if (ctx == null) return;
            playerHP = ctx.playerCurrentHP;
            playerMaxHP = ctx.playerMaxHP;
            enemyHP = ctx.enemyCurrentHP;
        }

        private void LogTurnResult(TurnResult r)
        {
            string playerDiceStr = r.playerDice != null ? string.Join("+", r.playerDice) : "?";
            string enemyDiceStr = r.enemyDice != null ? string.Join("+", r.enemyDice) : "?";
            string critStr = r.isCritical ? " ★CRITICAL★" : "";
            string winStr = r.isDraw ? "DRAW" : (r.playerWon ? "Player WIN" : "Enemy WIN");

            Debug.Log($"[Turn {r.turnNumber}] {winStr}{critStr}");
            Debug.Log($"  Player Dice: [{playerDiceStr}] = {r.playerDiceTotal}");
            Debug.Log($"  Enemy  Dice: [{enemyDiceStr}] = {r.enemyDiceTotal}");
            if (!r.isDraw)
            {
                Debug.Log($"  Main: {r.mainDamage}, Pursuit: {r.pursuitDamage}, " +
                          $"Total: {r.totalDamage}, Fixed: {r.fixedDamage}, Scratch: {r.scratchDamage}");
            }
            Debug.Log($"  Player HP: {r.playerHPAfter}, Enemy HP: {r.enemyHPAfter}");
        }
    }
}
