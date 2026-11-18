using GameLoop;

namespace CombatSystem
{
    /// <summary>
    /// 装備中の賽のパッシブ (ADR-0015・2026-11-18)。 <b>賽は 1 個で毎ターン 3 回振る</b>ので、 目 1 つごとに効くパッシブは 1 ターンに最大 3 回効く。
    ///
    /// <para><b>挿した目に効く物</b> (錆釘・鈍色・逆目・秤・渡し守・夜番・大技読み・残り火) は <see cref="ApplyToTerminals"/>。
    /// <see cref="TerminalCards.Resolve"/> の最後の段で呼ぶので、 本体・BOT の見積もり・画面の見込みが同じ値になる (盤面は <see cref="TerminalEnv"/>)。</para>
    ///
    /// <para><b>戦闘の他の所で効く物</b>: 拾い物・古銭 = <see cref="OnTurnEnd"/> (ターンの終わり)・賭場 = 振り直しの費用 (CombatManager.RerollCostNow) と HP の支払い・
    /// 墓標 = HP 0 からの踏みとどまり (CombatManager.TryLastStandRevive)。</para>
    ///
    /// <para><b>ほかの所にある賽</b>: 目利き = 振り直し (CombatManager.RerollPhase・残す目)・刻み = 次のターンの 1 つ目 (CombatManager.DecideCarve)・
    /// 繋ぎ = 残す印 (TerminalDeckState.KeepFromMarks)・時計 = 常設の〈時計〉の端子 (TerminalIds.Clock)・分け身 = 主攻撃を 2 回 (CombatManager.Turn)・
    /// 連なり = 役〈連なり〉(YachtRoles / YachtRoleEffects)。 効果の数値はすべて仮の値 (items.json の説明文と同じ)。</para>
    ///
    /// <para><b>出目は書き換えない</b> (ADR-0015 の 2)。 ここは出目が決まって端子に挿された後の上乗せだけ。</para>
    /// </summary>
    public static class DiePassives
    {
        /// <summary>錆釘: 攻撃に挿した目がこれ以上なら攻撃 +1。</summary>
        public const int RustyNailMin = 5;
        /// <summary>渡し守: 振り直した後の目を攻撃に挿すと攻撃 +N。</summary>
        public const int FerryAttack = 2;
        /// <summary>夜番: 防御に挿した目が敵の出目の最大より大きければシールド +N。</summary>
        public const int NightWatchShield = 3;
        /// <summary>残り火: 挿さずに捨てた目の何倍を次のターンの攻撃に足すか。</summary>
        public const int EmberMul = 2;
        /// <summary>拾い物: ①が出たターンの終わりにゴールド +N。</summary>
        public const int ScavengerGold = 1;
        /// <summary>古銭: ⑧が出たターンに敵を倒すとゴールド +N。</summary>
        public const int OldCoinGold = 3;
        public const int OldCoinFace = 8;

        /// <summary>端子の結果へ賽のパッシブを足す。 <paramref name="v"/> は端子に入る目 (〈号令の剣〉の +1 の後)。
        /// 誓約の封じ (シールド) より前に呼ぶ ── 夜番のシールドも封じで落ちる。</summary>
        public static void ApplyToTerminals(TerminalEnv env, TerminalBattleState state, TerminalOutcome o, TerminalDef[] defs, int[] v)
        {
            string die = env.dieId;
            if (string.IsNullOrEmpty(die) || o == null || v == null) return;
            int n = v.Length;

            // 残り火: 前のターンに捨てた分を、 このターン攻撃に挿した目があれば足す。 持ち越すのは 1 ターン分だけ
            if (die == DieIds.Ember && state != null)
            {
                if (o.attackDice > 0) o.attackAdd += state.emberCarry;
                state.emberCarry = 0;
                int next = 0;
                for (int i = 0; i < n; i++) if (o.slotOfDie[i] < 0) next += EmberMul * System.Math.Max(0, v[i]);
                state.emberNext = next;
                return;
            }

            for (int i = 0; i < n; i++)
            {
                int s = o.slotOfDie[i];
                if (s < 0 || s >= defs.Length || defs[s] == null) continue;
                var k = defs[s].kind;
                int val = v[i];
                switch (die)
                {
                    case DieIds.RustyNail:
                        if (k == TerminalKind.Attack && val >= RustyNailMin) o.attackAdd += 1;
                        break;
                    case DieIds.Dull:
                        if (val % 2 == 0 && k == TerminalKind.Block) o.blockAdd += 1;
                        else if (val % 2 != 0 && k == TerminalKind.Attack) o.attackAdd += 1;
                        break;
                    case DieIds.Reverse:
                        if (k == TerminalKind.Attack) o.attackAdd += System.Math.Max(0, (7 - val) / 2);
                        break;
                    case DieIds.Scale:
                        if (n == 3 && Between(v, i)) AddByKind(o, k, 1);
                        break;
                    case DieIds.Ferry:
                        if (env.diceRerolled && k == TerminalKind.Attack) o.attackAdd += FerryAttack;
                        break;
                    case DieIds.NightWatch:
                        if (k == TerminalKind.Block && val > env.enemyMaxDie) o.shield += NightWatchShield;
                        break;
                    case DieIds.HeavyRead:
                        if (env.heavyTurn) AddByKind(o, k, val / 2);
                        break;
                }
            }
        }

        /// <summary>目 i が、 ほかの 2 つの目の間 (両端を含まない) にあるか。</summary>
        private static bool Between(int[] v, int i)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int j = 0; j < v.Length; j++)
            {
                if (j == i) continue;
                if (v[j] < lo) lo = v[j];
                if (v[j] > hi) hi = v[j];
            }
            return lo < v[i] && v[i] < hi;
        }

        /// <summary>挿した端子の種類の量へ足す (攻撃・防御・充電。 補助の端子には足さない)。</summary>
        private static void AddByKind(TerminalOutcome o, TerminalKind k, int add)
        {
            if (add <= 0) return;
            switch (k)
            {
                case TerminalKind.Attack: o.attackAdd += add; break;
                case TerminalKind.Block: o.blockAdd += add; break;
                case TerminalKind.Charge: o.chargeAdd += add; break;
            }
        }

        /// <summary>ターンの終わり: 拾い物 (①が出た) と古銭 (⑧が出て敵を倒した) のゴールド。 <paramref name="dice"/> = このターンの出目。</summary>
        public static void OnTurnEnd(RunState run, int[] dice, bool enemyKilled)
        {
            if (run == null || dice == null) return;
            string die = EquippedDie.IdOf(run);
            if (die == DieIds.Scavenger && Has(dice, 1))
            {
                run.coins += ScavengerGold;
                UnityEngine.Debug.Log($"[賽] 拾い物の賽: ①が出た → +{ScavengerGold}G");
            }
            else if (die == DieIds.OldCoin && enemyKilled && Has(dice, OldCoinFace))
            {
                run.coins += OldCoinGold;
                UnityEngine.Debug.Log($"[賽] 古銭の賽: ⑧のターンに撃破 → +{OldCoinGold}G");
            }
        }

        private static bool Has(int[] dice, int face)
        {
            for (int i = 0; i < dice.Length; i++) if (dice[i] == face) return true;
            return false;
        }

        /// <summary>賭場の賽: 振り直しが無料 (費用 0)・代わりに振り直すたびに HP −N。</summary>
        public const int GambleHpCost = 1;
        public static bool FreeRerollByDie(RunState run) => EquippedDie.IdOf(run) == DieIds.Gamble;
    }
}
