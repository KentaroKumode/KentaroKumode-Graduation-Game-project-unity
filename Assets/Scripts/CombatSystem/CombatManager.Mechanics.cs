using System;
using System.Collections.Generic;
using UnityEngine;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>CombatManager: ターン内の各機構 (敵の攻撃ロール・ダイス・防御・リロール・ラストスタンド・役)。</summary>
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

        /// <summary>このランで始めた戦闘の数 (= 次の戦闘の乱数の通し番号)。 中断セーブが写す (issue #84)。</summary>
        public int RunCombatSequence => _combatSeq;

        /// <summary>中断セーブから再開する時に通し番号を戻す。 戻さないと再開後の戦闘の乱数列が、 中断しなかった時とずれる。</summary>
        public void RestoreRunCombatSequence(int seq) => _combatSeq = Math.Max(0, seq);

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
            string next = GameLoop.BossIds.VescaNextStage(id), label = null;
            switch (GameLoop.BossIds.VescaStageOf(id))
            {
                case 1: label = "第二段：遺物学者"; break;
                case 2: label = "第三段：神話";     break;
                case 3: label = "第四段：天与";     break;
                default: return false;   // p4 または非ヴェスカ
            }
            Debug.Log($"[ヴェスカ連戦] {id} 撃破 → {label} へ移行 (戦闘終了前に確定)");
            var ctx = PassiveSkillManager.Instance?.Context;
            // 計測は SwapEnemy 側で一元化 (ここで重ねると二重計上になる)
            SwapEnemy(next, label);
            if (ctx != null) SyncHPFromContext(ctx);
            return true;
        }

        private int[] RollEnemyAttack(EnemyData e, int extraDraws)
        {
            if (e == null) return new int[0];
            if (e.UsesAttackRoll)
                return e.RollAttack(extraDraws, e.attackRollMax);
            // ---- 旧ダイス方式 (互換パス) ----
            int[] faces = e.diceFaces;
            if (faces != null && faces.Length > 0)
                return RollDice(Math.Max(0, e.diceCount + extraDraws), 0, faces);
            return RollDice(Math.Max(0, e.diceCount + extraDraws), e.diceMaxValue);
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
        /// (<c>enemyArmorPenNegate</c>) を先に引く。 (旧: 斧〈血令〉持ちは防御を貫通へ換えていたので掛けなかった・2026-11-11 に〈血令〉を作り直して外した)</summary>
        public static int ApplyPlayerDefense(CombatContext ctx, int enemyAtk, int pen)
        {
            if (ctx == null || enemyAtk <= 0) return enemyAtk;
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
        /// <summary>振り直した出目へ、 振った直後と同じデバフを掛け直す (2026-11-10)。
        /// 旧: 振り直しは面から値を引き直すだけで、 大罪:怠惰 (全部の出目 −N) が振り直したダイスから消えていた。
        /// 振り直したダイスにだけ引く (振り直していないダイスは引き済み)。</summary>
        private void ReapplyDicePenaltiesAfterReroll(int[] playerDice, int[] playerFaceIdx, List<int> rerolled)
        {
            int sloth = MetaProgression.MetaDebuffApplicator.GetSlothDicePenalty();
            if (sloth > 0) foreach (int i in rerolled) playerDice[i] = Math.Max(1, playerDice[i] - sloth);
            ForceVescaDice(ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context, playerDice, rerolled);
        }

        /// <summary>[測定 2026-11-11] ヴェスカ連戦の間、 出目を <see cref="GameLoop.GameHooks.VescaDiceForce"/> に固定する (0 なら何もしない)。
        /// rerolled が null なら全部、 そうでなければ振り直したダイスだけ。</summary>
        private static void ForceVescaDice(ItemSystem.PassiveSkills.CombatContext ctx, int[] playerDice, List<int> rerolled)
        {
            int v = GameLoop.GameHooks.VescaDiceForce;
            if (v <= 0 || playerDice == null || ctx?.bossId == null || !ctx.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix)) return;
            if (rerolled == null) for (int i = 0; i < playerDice.Length; i++) playerDice[i] = v;
            else foreach (int i in rerolled) playerDice[i] = v;
        }

        /// <summary>振り直し 1 回の費用 (充電) の 1 か所 (2026-11-16)。 = 振る個数 × この戦闘で何回目か + 時限デバフ〈無力感〉の上乗せ (+1)。
        /// 無料の振り直し (〈特別・対〉) は 0 で、 上乗せも足さない。 本体の支払い・画面の費用の表示 (<see cref="TurnPause.rerollCost"/>)・
        /// BOT の振り直しの方策 (AutoRunner.MutualRerollPolicy) がここを読む。 式の正本は <see cref="EventSystem.TimedEffects.TimedDebuffRule.RerollCost"/>。</summary>
        public static int RerollCostNow(int diceCount, int nth, bool free)
        {
            if (DiePassives.FreeRerollByDie(GameLoop.GameManager.Instance?.Run)) return 0;      // 賭場の賽 (ADR-0015): 充電は払わない (代わりに HP を払う・RerollPhase)
            int cost = EventSystem.TimedEffects.TimedDebuffRule.RerollCost(diceCount, nth, free,
                EventSystem.TimedEffects.TimedDebuffRule.RerollSurcharge(GameLoop.GameManager.Instance?.Run?.timedDebuffs));
            var ctx = PassiveSkillManager.Instance?.Context;
            return ctx != null ? ctx.ChargeCostNow(cost) : cost;   // 〈過負荷チューナー〉: 過充電の間 −1 (最低 1・2026-11-20)
        }

        /// <summary>この戦闘で振り直しを 1 回でもした (無料も・ctx.accumulatedValues・〈擦り切れた賽袋〉の「戦闘の最初の振り直し」)。</summary>
        private const string RerolledKey = "role.rerolled";

        /// <summary>このターン、 次の振り直しに使える無料の回数 (2026-11-21): 前のターンから持ち越した無料 (〈特別・対〉〈鏡合わせの盤〉〈揃いの拍子木〉・
        /// currentBuffs の <see cref="YachtRoleEffects.FreeRerollKey"/>) と、 〈擦り切れた賽袋〉(この戦闘でまだ振り直していなければ 1)。
        /// 本体の支払い (<see cref="RerollPhase"/>)・画面の費用・BOT の振り直しの方策が同じ関数を読む。</summary>
        public static int FreeRerollsLeft(CombatContext ctx)
        {
            if (ctx == null) return 0;
            int n = ctx.currentBuffs.TryGetValue(YachtRoleEffects.FreeRerollKey, out float f) ? Mathf.Max(0, (int)f) : 0;
            if (ctx.GetAccumulated(RerolledKey) <= 0f && AxisRules.Owns(GameLoop.GameManager.Instance?.Run, GameLoop.ItemIds.RoleBag)) n++;
            return n + SparkRoleFreeLeft(ctx);   // 種火・役 (2026-11-21): 各戦闘の最初の振り直しが無料 (この戦闘の残り)
        }

        /// <summary>今の ctx での、 次の振り直し 1 回の費用 (無料の回数を読む・2026-11-21)。 本体・画面・BOT の 1 か所。</summary>
        public static int RerollCostNow(CombatContext ctx, int diceCount, int nth)
            => RerollCostNow(diceCount, nth, FreeRerollsLeft(ctx) > 0);

        /// <summary>振り直しを 1 回払った後: 無料を使ったなら 1 つ減らす (〈擦り切れた賽袋〉の分が先 ── 戦闘の最初の振り直し)。 振り直した印を立てる。</summary>
        private static void NoteRerollPaid(CombatContext ctx, bool usedFree)
        {
            bool bag = ctx.GetAccumulated(RerolledKey) <= 0f && AxisRules.Owns(GameLoop.GameManager.Instance?.Run, GameLoop.ItemIds.RoleBag);
            // 使う順: 〈擦り切れた賽袋〉(戦闘の最初の 1 回) → 〈特別・対〉など (ターンの終わりに消える) → 種火・役 (この戦闘の残り)
            if (usedFree && !bag)
            {
                if (ctx.currentBuffs.TryGetValue(YachtRoleEffects.FreeRerollKey, out float f) && f >= 1f)
                    ctx.currentBuffs[YachtRoleEffects.FreeRerollKey] = Mathf.Max(0f, f - 1f);
                else if (SparkRoleFreeLeft(ctx) > 0)
                    ctx.accumulatedValues[SparkRoleFreeUsedKey] = (ctx.accumulatedValues.TryGetValue(SparkRoleFreeUsedKey, out float u) ? u : 0f) + 1f;
            }
            if (usedFree && bag) Debug.Log("[擦り切れた賽袋] この戦闘の最初の振り直しは無料");
            ctx.accumulatedValues[RerolledKey] = 1;
        }

        /// <summary>種火・役 (2026-11-21) の無料の振り直しのうち、 この戦闘で使った回数 (CombatContext.accumulatedValues・戦闘ごとに作り直される)。</summary>
        public const string SparkRoleFreeUsedKey = "sparkRoleFreeUsed";

        /// <summary>種火・役の無料の振り直しの、 この戦闘の残り。</summary>
        private static int SparkRoleFreeLeft(CombatContext ctx)
        {
            int used = ctx.accumulatedValues.TryGetValue(SparkRoleFreeUsedKey, out float u) ? (int)u : 0;
            return Mathf.Max(0, MetaProgression.MetaBuffApplicator.GetSparkRoleFreeRerolls() - used);
        }

        private IEnumerator<TurnPause> RerollPhase(CombatContext ctx, int[] playerDice, int[] enemyDice, int playerDiceMax,
                                                   int[] playerFaceIdx = null)
        {
            if (ctx == null || playerDice == null || playerDice.Length == 0) yield break;
            if (RerollPolicy == null && !_interactiveTurn) yield break;

            // 予告を仮組みする。 敵ダイスはこの後変わらないので素の合計で作れる。
            int rawEnemyTotal = 0;
            if (enemyDice != null) for (int i = 0; i < enemyDice.Length; i++) rawEnemyTotal += enemyDice[i];
            var tele = new MutualTurnTelegraph
            {
                turn = ctx.currentTurn,
                enemyAttackValue = ApplyPlayerDefense(ctx,
                    Escalation.EnemyAttackValue(currentEnemy, EscalationTurn(ctx), rawEnemyTotal, ctx.currentTurn),
                    ctx.enemyArmorPen),
                escalationStage = Escalation.EffectiveStage(EscalationTurn(ctx)),
                nextThresholdTurn = Escalation.NextThresholdTurn(EscalationTurn(ctx)),
                turnsToHeavy = Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn),
                heavyMul = (currentEnemy?.heavyMul ?? 0f) > 0f ? currentEnemy.heavyMul + MetaProgression.MetaDebuffApplicator.GetHeavyMulBonus() : 0f,   // 挑戦カード: 大技の倍率 +N も予告に出す
                turnsToGuard = Escalation.TurnsToGuard(currentEnemy, ctx.currentTurn),
                enemyDefense = ctx.enemyDefense,
                enemyDice = enemyDice,
                enemyDiceTotal = rawEnemyTotal,
            };
            // ボスのダウン (2026-11-13): 配線の予告と同じ関数で入れる (ダウンのターンは攻撃 0・目盛りの刻み・次の目盛りの HP)。
            //   BOT の振り直しの値付け (MutualRerollPolicy → 山札の効用) もこの予告を読むので、 ダウンのターンの攻撃 0・与ダメ ×1.5 を見る。
            FillDownTelegraph(ref tele);

            // 〈特別・対〉(前ターン成立): 次のリロール N 回 (既定 2) を無料にする。 数えるのは FreeRerollsLeft (〈擦り切れた賽袋〉ほか・2026-11-21)

            // **全部振り直し・費用は戦闘の中で上がる (2026-10-17)。**
            //   旧規則は「選んだダイスだけ・費用 = 個数 × そのターンで何回目か」で、 回目がターンごとに戻るうえ
            //   1〜2 個を刻んで追えたので、 充電さえあれば役がほぼ任意に作れた (〈極〉が設計 0.077%/T → 充電 12 で約 10%)。
            //   いまは (1) 方策の pick は「振るかどうか」だけを表し、 中身は読まない ── 振り直せない面以外を全部振る
            //   (2) 費用 = 振り直す個数 × (この戦闘で払った回数 + 1)。 ターンでは戻らない。 docs/GAME.md §6-3。
            //   (3) 時限デバフ〈無力感〉(2026-11-16) を持っていれば毎回 +1 (無料の振り直しには足さない)。 式は RerollCostNow の 1 か所。
            bool appraiser = GameLoop.EquippedDie.IdOf(GameLoop.GameManager.Instance?.Run) == GameLoop.DieIds.Appraiser;   // 目利きの賽
            int attempt = 1;   // このターンの何回目か (乱数の添字にだけ使う)
            int guard = 16;    // 方策が空でない配列を返し続けても止まるようにする
            var rerollIdx = new List<int>(playerDice.Length);
            while (guard-- > 0)
            {
                int nth = ctx.rerollsThisCombat + 1;
                int[] pick;
                if (_interactiveTurn)
                {
                    _pause.Set(TurnPauseKind.Reroll, playerDice, playerFaceIdx, tele);
                    _pause.rerollCost = RerollCostNow(ctx, playerDice.Length, nth);
                    // 振り直すかを決める時も手札と場を見せる (手札は振り直しで変わらない)。 敵の攻撃は予告の近似で組む
                    if (_deck != null) { _pause.board = _deck.board; _pause.terminalEnv = TerminalEnvNow(ctx, tele.enemyAttackValue); _pause.terminalState = _deck.state; }
                    yield return _pause;
                    pick = !_pause.reroll ? null
                         : appraiser ? AllDiceIndexExcept(playerDice.Length, _pause.rerollKeep) : AllDiceIndex(playerDice.Length);
                }
                else pick = RerollPolicy(playerDice, tele, nth);
                if (pick == null || pick.Length == 0) break;

                // ADR-0011: 振り直せない面は無い。 常に全ダイスを振り直す。
                //   例外は目利きの賽 (ADR-0015): 方策 / 画面が 1 つ除いた添字を返したら、 その目を残して残りを振る。
                rerollIdx.Clear();
                if (appraiser && pick.Length == playerDice.Length - 1)
                    foreach (int i in pick) { if (i >= 0 && i < playerDice.Length) rerollIdx.Add(i); }
                else for (int i = 0; i < playerDice.Length; i++) rerollIdx.Add(i);

                bool usedFree = FreeRerollsLeft(ctx) > 0;
                int cost = RerollCostNow(ctx, rerollIdx.Count, nth);   // 〈無力感〉の +1 もここ (無料の時は足さない)
                // 賭場の賽 (ADR-0015): 充電の代わりに HP を払う。 HP が払えない (払うと 0 になる) なら振り直せない
                bool gamble = DiePassives.FreeRerollByDie(GameLoop.GameManager.Instance?.Run);
                if (gamble && playerHP <= DiePassives.GambleHpCost) break;
                if (!ctx.ConsumeCharge(cost)) break;   // 払えなければそこで打ち切り
                NoteRerollPaid(ctx, usedFree);
                if (gamble)
                {
                    playerHP -= DiePassives.GambleHpCost;
                    ctx.playerCurrentHP = playerHP;
                    Debug.Log($"[賽] 賭場の賽: 振り直しの代わりに HP −{DiePassives.GambleHpCost} → {playerHP}");
                }

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
                ReapplyDicePenaltiesAfterReroll(playerDice, playerFaceIdx, rerollIdx);
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
            // 墓標の賽 (ADR-0015): 1 度だけ砕けて HP 1 で踏みとどまる。 砕けた後は最初の賽に戻る (この戦闘の残りの面も替える)。
            //   ラストスタンド (メタ) より先 ── 賽の約束を先に果たし、 ラストスタンドは次の死に残す。
            if (GameLoop.EquippedDie.IdOf(run) == GameLoop.DieIds.Gravestone)
            {
                GameLoop.EquippedDie.Equip(run, GameLoop.DieIds.Starting);
                if (ctx != null)
                {
                    GameLoop.EquippedDie.Build(run, out var faces, out var tiers);
                    ctx.equippedDiceFaces = faces;
                    ctx.equippedFaceTiers = tiers;
                }
                playerHP = 1;
                if (ctx != null) ctx.playerCurrentHP = playerHP;
                Debug.Log("[賽] 墓標の賽が砕けた: HP 1 で踏みとどまり、 角の取れた賽に戻った");
                return;
            }
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

        /// <summary>〈最後の審判〉のラン累計戦闘ターンを進める。 刻限後の罰は敵の攻撃の上乗せ (MetaDebuffApplicator.GetEnemyAttackPct)。</summary>
        private void ApplyFinalJudgment()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return;
            // 〈最後の審判〉はファーム層のターンを数えない
            if (run.Depth == MapSystem.FreeMove.FloorPlan.FarmFloor) return;
            run.totalCombatTurns++;
            int th = MetaProgression.MetaDebuffApplicator.GetJudgmentTurnThreshold();
            if (th > 0 && run.totalCombatTurns == th)
                Debug.Log($"[最後の審判] 刻限到来 (累計{run.totalCombatTurns}T >= {th}) → 以後の敵の攻撃 +%");
        }

        // 役の判定で使う一時バッファ。 毎ターン new すると GC を踏むので使い回す。
        private readonly List<RoleKind> _formedRoles = new List<RoleKind>(4);

        /// <summary>ADR-0011: 役を判定し、 成立したものを<b>自動で</b>適用する。
        ///
        /// 役は振った時点の 3 個で判定し、 端子への置き方は条件にしない。 1 戦闘 1 回の制限も、
        /// 発動の任意選択も、 使用済みの管理も持たない (ADR-0011 決定 2)。
        /// 賽がその目の面を持っていれば、 2 個そろいもゾロ目も効く (ADR-0015・Tier は 2026-11-18 にやめた)。
        /// 連なりの賽を持つ時だけ、 3 つの連番 (連なり) も役になる。
        /// 3 個では 2 つの役を同時に作れないので、 効くのは 1 ターンに高々 1 つ。</summary>
        private RoleOutcome ResolveRoles(CombatContext ctx, int[] playerDice, int[] playerFaceIdx)
        {
            var outcome = new RoleOutcome();
            if (ctx == null || playerDice == null || playerDice.Length == 0) return outcome;

            YachtRoleEffects.TurnsEvaluated++;
            _formedRoles.Clear();
            YachtRoles.Evaluate(playerDice, playerFaceIdx, ctx.equippedFaceTiers, _formedRoles);

            // 賽目の締めと【規則】の品 (2026-11-21・段 1-f・item-overhaul-spec §5 / §12-1)。 所持は戦闘の始めから変わらない
            var roleRun = GameLoop.GameManager.Instance?.Run;
            bool omni = AxisRules.Owns(roleRun, GameLoop.ItemIds.RoleOmni), mirror = AxisRules.Owns(roleRun, GameLoop.ItemIds.RoleMirror),
                 fate = AxisRules.Owns(roleRun, GameLoop.ItemIds.RoleFate);
            var tier = GameLoop.DiceFaceParts.Tier.T3;   // Tier は使わない (ADR-0015)。 実績の呼び手のために渡すだけ
            // 〈宿命の刻み目〉: 前のターンに立った役の効果が、 このターンにももう 1 回働く (役が立ったのではないので数えない・出来事も鳴らさない)。
            //   連なりの効果 (端子がすべて 2 回働く) は端子の解決の中なので、 前のターンに TerminalBattleState.fateDoubleNext で渡してある
            if (fate)
            {
                int prev = (int)ctx.GetAccumulated(FateRoleKey) - 1;
                ctx.accumulatedValues[FateRoleKey] = 0;
                if (prev >= 0 && (RoleKind)prev != RoleKind.Straight)
                {
                    YachtRoleEffects.Apply((RoleKind)prev, tier, ctx, ref outcome);
                    Debug.Log($"[宿命の刻み目] 前のターンの役〈{YachtRoles.NameOf((RoleKind)prev)}〉の効果がもう 1 回働く");
                }
            }

            for (int i = 0; i < _formedRoles.Count; i++)
            {
                var k = _formedRoles[i];
                YachtRoleEffects.FormedCount[(int)k]++;
                // T4-C〈凶運〉: このランで封印されている役は成立しても効かない。
                if ((ctx.sealedRoleMask & (1 << (int)k)) != 0) continue;
                // 〈万象のゾロ目〉: このターン 2 個そろいをゾロ目として扱う (振り直しの後に DecideOmniPair が決めた・端子の解決も同じ TerminalCards.UpgradePair)
                k = TerminalCards.UpgradePair(k, OmniPairNow(ctx), ctx.sealedRoleMask);

                // 〈万象のゾロ目〉: ゾロ目の効果は 2 回働く
                int times = omni && YachtRoles.IsTriple(k) ? 2 : 1;
                for (int t = 0; t < times; t++) YachtRoleEffects.Apply(k, tier, ctx, ref outcome);
                // 〈鏡合わせの盤〉: 2 個そろいなら、 1 つ小さい目の 2 個そろいの効果も働く (1 の 2 個そろいなら、 次のターンの無料の振り直し +1)
                if (mirror && !YachtRoles.IsBig(k)) MirrorPair(k, ctx, tier, ref outcome);
                if (fate)
                {
                    ctx.accumulatedValues[FateRoleKey] = (int)k + 1;
                    if (k == RoleKind.Straight && _deck != null) _deck.state.fateDoubleNext = true;
                }
                ctx.firedRolesThisTurn.Add(k);
                RaiseCue(CombatCue.Role, YachtRoles.NameOf(k));      // 合図 (#319)
                YachtRoleEffects.FiredCount[(int)k]++;
                // [計装 2026-08-15] ダイス別の発動は戦闘単位で FinishCombat が積む
                //   (役の発動地点から GameManager.Instance を引かないため)。
                YachtRoleEffects.FiredThisCombat[(int)k]++;
                MetaProgression.Achievements.AchievementService.NoteRoleFired(k, tier);
                // 役が立った (2026-11-21・段 1-f): 量 = 強さ (2 個そろい 1 / ゾロ目・連なり 2)。 OnWired の前なので、 品の攻撃 +N は撃ごとに乗る
                ItemSystem.PassiveSkills.Keywords.Kw.Raise(ctx, PassiveSkillTrigger.OnRoleFired, YachtRoles.Strength(k));
            }

            if (ctx.firedRolesThisTurn.Count > 0)
            {
                var names = new string[ctx.firedRolesThisTurn.Count];
                for (int i = 0; i < names.Length; i++) names[i] = YachtRoles.NameOf(ctx.firedRolesThisTurn[i]);
                Debug.Log($"[役] T{ctx.currentTurn} 発動: {string.Join("/", names)}");
            }
            return outcome;
        }

        /// <summary>〈宿命の刻み目〉(2026-11-21): 前のターンに立った役 ((int)RoleKind + 1・0 = なし・ctx.accumulatedValues)。</summary>
        private const string FateRoleKey = "role.fate";
        /// <summary>〈万象のゾロ目〉(2026-11-21): 2 個そろいをゾロ目として扱うと決めたターンの番号 (ctx.accumulatedValues) / 乱数のキー (専用) / 賽目 1 つあたりの確率。</summary>
        private const string OmniTurnKey = "role.omniTurn";
        public const string OmniRngKey = "role.omni";
        public const float OmniPctPerDepth = 0.08f;

        /// <summary>〈万象のゾロ目〉: このターン 2 個そろいをゾロ目として扱う (<see cref="DecideOmniPair"/> が決めた)。 端子の盤面 (TerminalEnv.pairAsTriple) と ResolveRoles が読む。</summary>
        public static bool OmniPairNow(CombatContext ctx)
            => ctx != null && ctx.currentTurn > 0 && (int)ctx.GetAccumulated(OmniTurnKey) == ctx.currentTurn;

        /// <summary>〈万象のゾロ目〉(2026-11-21): 出目が決まった所 (振り直しと出目を書き換える効果の後・配線の前) で、 立つ役が 2 個そろいなら、
        /// 確率 min(100%, 賽目 × 8%) でこのターンそれをゾロ目として扱う。 配線の前に決めるので、 配線の判断 (人・BOT) はその結果を見て挿せる。</summary>
        private static void DecideOmniPair(CombatContext ctx, int[] dice)
        {
            if (ctx == null || !AxisRules.Owns(GameLoop.GameManager.Instance?.Run, GameLoop.ItemIds.RoleOmni)) return;
            var k = TerminalCards.RoleOf(dice, new TerminalEnv { sealedRoleMask = ctx.sealedRoleMask });
            if (k == null || YachtRoles.IsBig(k.Value)) return;
            float p = Mathf.Min(1f, OmniPctPerDepth * ctx.AxisDepth(AxisRules.Role));
            if (p <= 0f || !GameLoop.GameRng.Chance(p, OmniRngKey)) return;
            ctx.accumulatedValues[OmniTurnKey] = ctx.currentTurn;
            Debug.Log($"[万象のゾロ目] 〈{YachtRoles.NameOf(k.Value)}〉をゾロ目として扱う ({p * 100f:0}%)");
        }

        /// <summary>〈鏡合わせの盤〉(2026-11-21): 2 個そろい <paramref name="k"/> の、 1 つ小さい目の 2 個そろいの効果を働かせる (役が立ったのではない ── 数えない)。
        /// 1 の 2 個そろいなら、 次のターンの無料の振り直し +1。 1 つ小さい目の役が〈凶運〉で封じられていれば働かない。</summary>
        private static void MirrorPair(RoleKind k, CombatContext ctx, GameLoop.DiceFaceParts.Tier tier, ref RoleOutcome outcome)
        {
            int face = YachtRoles.FaceOf(k);
            if (face <= 1) { YachtRoleEffects.AddNextTurn(ctx, YachtRoleEffects.FreeRerollKey, 1); Debug.Log("[鏡合わせの盤] 特別・対 → 次のターンの無料の振り直し +1"); return; }
            var m = YachtRoles.Of(face - 1, false);
            if ((ctx.sealedRoleMask & (1 << (int)m)) != 0) return;
            YachtRoleEffects.Apply(m, tier, ctx, ref outcome);
            Debug.Log($"[鏡合わせの盤] 〈{YachtRoles.NameOf(k)}〉に重ねて〈{YachtRoles.NameOf(m)}〉の効果");
        }

        // 端子の状態のうち、 戦闘の情報 (accumulatedValues) を通して渡すキー。 **フィールドを増やさない**
        //   (CombatContext の 3 区分規約: set/read が 1〜2 箇所なら辞書へ)。
        /// <summary>エスカレーションの遅延ターン数のキー。 端子〈鎮静香〉〈氷結瓶〉(ADR-0014) と役〈時間〉(目 6) が共有する。</summary>
        public const string CoolantDelayKey = "termCoolantDelay";
        /// <summary>端子〈打ち直し〉(ADR-0014) で増えた、 この戦闘中の出目 +N (DrawTerminalHand が置き、 自ロールの後に足す)。</summary>
        private const string FoundryBonusKey = "termFoundryBonus";

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

        /// <summary>実効会心率 (0〜1)。 武器 + このターンの加算 + 整備パネル − 〈俊敏〉 を <c>ResolveCritRate</c> に通す。
        /// 役の追撃の判定と HUD の表示が同じ 1 箇所を通る (2026-11-05)。 本判定 (<c>PassiveSkillManager.ProcessDamage</c>) は
        /// 同じ式を <c>OnCriticalCheck</c> の加算とバフの後に組むので、 配線を決める前の値には
        /// <paramref name="pendingBuff"/> = true でまだ乗っていない戦闘中バフ (月蝕/レイピア) を足す。
        /// 本判定の後 (バフは <c>critRatePctAdd</c> に取り込み済み) では false にすること ── 二重に数えない。</summary>
        private float CritRateOf(CombatContext ctx, bool pendingBuff)
        {
            float add = playerCritBaseRate + ctx.critRatePctAdd
                      + MetaProgression.MetaBuffApplicator.GetCritRatePctBonus();
            if (pendingBuff) add += ctx.GetBuff(ItemSystem.PassiveSkills.CombatContext.CritRateBuffKey);
            add = Mathf.Max(0f, add);
            return ItemSystem.PassiveSkills.CombatContext.ResolveCritRate(add);
        }

        /// <summary>HUD に出すプレイヤーのステータス (2026-11-05)。 <b>配線を決める前に呼ぶ</b>こと
        /// (与ダメ補正の整備パネル分と戦闘中バフの会心率は、 まだ本判定に取り込まれていない前提で足している)。
        /// <c>OnCriticalCheck</c> で発動する条件付きの会心率 (3 種) は本判定まで分からないので含まない。
        /// <para>consOutgoingPct (2026-11-12) = そのうち消耗品 (〈油・膏薬〉と、 立てている〈瞬間研磨剤〉) が足す与ダメ%。
        /// どちらも本判定 (<c>ApplyWinDamageModifiers</c>) で同じ加算プールへ入るので、 outgoingPct にも含める。 戦闘中に使った品を画面に出すため。</para>
        /// <para>toilCutPct (2026-11-14) = 〈徒労〉の戦闘のデバフ (与えるダメージ −N%・<see cref="MetaProgression.MetaDebuffApplicator.GetToilDamageCutPct"/>)。
        /// 与ダメ% の加算プールとは別の掛け算 (すべての倍率の後) なので outgoingPct には含めない。 0 = 持っていない / 口が 0。</para></summary>
        public struct HudStats { public float critRate, critMul; public int attack, outgoingPct, defense, pen, consOutgoingPct, toilCutPct; }

        public HudStats PreviewHudStats()
        {
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var s = new HudStats { attack = playerAttackPower, toilCutPct = MetaProgression.MetaDebuffApplicator.GetToilDamageCutPct() };
            if (ctx == null) return s;
            s.critRate = CritRateOf(ctx, pendingBuff: true);
            s.critMul = ctx.criticalMultiplier;
            float mul = ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier;
            int metaPct = MetaProgression.MetaBuffApplicator.GetOutgoingDamagePct()
                        + MetaProgression.MetaBuffApplicator.GetBattleSpiritPct(GameLoop.GameManager.Instance?.Run);
            // 消耗品の分 (ApplyWinDamageModifiers の〈瞬間研磨剤〉+150%・〈油・膏薬〉consDmgMultPct と同じ条件)
            s.consOutgoingPct = Mathf.Max(0, ctx.consDmgMultPct) + (ctx.polishArmed && !ctx.rollPurity ? 150 : 0);
            s.outgoingPct = Mathf.RoundToInt((mul - 1f) * 100f) + metaPct + s.consOutgoingPct;
            s.defense = ctx.playerDefense;
            s.pen = ctx.playerArmorPen;
            return s;
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
                float rate = CritRateOf(ctx, pendingBuff: false);
                // 本判定と同じ優先順: 封印 (無心の刃) で外れ、 forceCritical が最後に確定で上書きする。
                crit = ((!ctx.critSuppressed && GameLoop.GameRng.Chance(rate, "combat.roleExtraCrit", RngIdx(3)))
                    || ctx.forceCritical) && !ctx.critToDamage;   // 〈銑鉄棍〉の【規則】: 会心が出なくなる
                if (crit) extra *= ctx.criticalMultiplier;
            }
            int add = Mathf.Max(1, Mathf.RoundToInt(extra));
            if (outcome.extraDamageTrue) dealtFixed += add; else dealtDmg += add;
            Debug.Log($"[役] 追撃: 与ダメ {basis} の {outcome.extraDamageRatio:P0} = +{add}"
                    + (outcome.extraDamageTrue ? " (確定)" : "") + (crit ? " 会心" : ""));
        }

        /// <summary>この戦闘で予告に出た一番高いエスカレーションの段 (不安定化の数え口・戦闘が始まる時に 0)。 〈鎮静香〉で段が下がって
        /// また上がっても、 一度数えた段は数え直さない (一番高い段を超えた分だけを数える)。</summary>
        private int _instabilityEscStage;
        /// <summary>合図 (#319) のための、 前に予告に出た激化の段 (下がった時も追う)。 戦闘の始まりで int.MinValue (= まだ見ていない)。</summary>
        private int _cueEscStage;

        /// <summary>予告の段 <paramref name="stage"/> が、 この戦闘で見た一番高い段を超えたら、 超えた段の数だけ本体へ知らせる
        /// (不安定化 +1 / 段・2026-11-13・GAME.md §5-12)。 ボス戦は本体が数えない (<see cref="Escalation.BossFight"/>)。 戦闘の規則には何も効かない。</summary>
        private void NoteEscalationForInstability(int stage)
        {
            if (_cueEscStage != int.MinValue && stage > _cueEscStage) RaiseCue(CombatCue.EscalationUp, stage.ToString());      // 合図 (#319): 下がってまた上がった時も知らせる (戦闘の最初の予告では鳴らさない)
            _cueEscStage = stage;
            if (stage <= _instabilityEscStage) return;
            int rises = stage - _instabilityEscStage;
            _instabilityEscStage = stage;
            GameLoop.GameManager.Instance?.NoteEscalationRise(rises, Escalation.BossFight);
        }

        /// <summary>エスカレーション判定に使うターン。 〈鎮静香〉の遅延を差し引く。
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
            EnemyHpLedger.CtxSync(enemyHP, ctx.enemyCurrentHP);   // [計装] ctx を通した削り・敵の自傷・回復
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
