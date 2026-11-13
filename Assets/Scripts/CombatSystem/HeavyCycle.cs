namespace CombatSystem
{
    /// <summary>
    /// 大技の周期の並び (小技・チャージ・大技) の<b>純粋な式</b> (2026-11-14)。 <see cref="Escalation"/> がこれを呼ぶ (式を 2 か所に書かない)。
    /// UnityEngine に依存しない ── Unity なしの検算 <c>Tools/rules_check</c> がそのままコンパイルする。
    ///
    /// <para>周期 N の敵は、 起点 (<see cref="Escalation.HeavyCycleOffset"/>) から数えて N, 2N, … のターンが<b>大技</b>、 その 1 ターン前が<b>チャージ</b>
    /// (大技の予告が出ているターン・敵は攻撃しない)、 残りが<b>小技</b>。 倍率は敵データの実数 (<see cref="EnemyData.heavyMul"/> / <see cref="EnemyData.heavyLightMul"/>)。
    /// チャージは倍率の欄を持たず「攻撃しない」の規則 (倍率 0)。 測定の口でチャージの攻撃を戻した旧い作りは、 チャージも小技の倍率。</para>
    /// </summary>
    public static class HeavyCycle
    {
        /// <summary>周期の中のターン <paramref name="cycleTurn"/> (起点から数えて 1 始まり) から、 次の大技まで何ターンか。
        /// 0 = このターンが大技 / −1 = 大技を持たない (周期 0 以下) かダウンで起点を置いたそのターン (0 以下)。</summary>
        public static int TurnsToHeavy(int period, int cycleTurn)
        {
            if (period <= 0 || cycleTurn <= 0) return -1;
            int r = cycleTurn % period;
            return r == 0 ? 0 : period - r;
        }

        /// <summary>チャージのターンか (大技の 1 ターン前)。 周期 1 (毎ターン大技) にはチャージが無い。</summary>
        public static bool IsCharge(int period, int cycleTurn) => period >= 2 && TurnsToHeavy(period, cycleTurn) == 1;

        /// <summary>攻撃値に掛ける周期の倍率。 <paramref name="heavyMulWithBonus"/> = 大技の倍率 (挑戦カードの大技の倍率 +N を足した後)・0 以下なら 1 /
        /// <paramref name="lightMul"/> = 小技の倍率・0 以下なら 1 / <paramref name="chargeNoAttack"/> = チャージは攻撃しない (既定 true)。
        /// 大技を持たない敵 (周期 0 以下) は 1。</summary>
        public static float Factor(int period, int cycleTurn, float heavyMul, float heavyMulWithBonus, float lightMul, bool chargeNoAttack)
        {
            if (period <= 0) return 1f;
            if (TurnsToHeavy(period, cycleTurn) == 0) return heavyMul > 0f ? heavyMulWithBonus : 1f;
            if (chargeNoAttack && IsCharge(period, cycleTurn)) return 0f;
            return lightMul > 0f ? lightMul : 1f;
        }
    }
}
