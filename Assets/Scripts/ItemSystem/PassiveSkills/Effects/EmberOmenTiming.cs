// UnityEngine に依存しない (Tools/rules_check がそのままコンパイルする)。
namespace ItemSystem.PassiveSkills.Effects
{
    /// <summary>予兆ターン判定 (2026-07-16 二相化): Phase1 (HP>50%) は周期4T (T1/5/9/13...)、
    /// Phase2 (HP≤50%) は周期3T (T1/4/7...) に加速。</summary>
    /// <para><b>2026-11-14〜</b>: このターンの並びが灰燼の王の大技〈業火〉の周期 (Escalation.TurnsToHeavy が EnemyData.heavyCycle = ember の時に読む)。 予兆のターン = 業火のターン、 その 1 ターン前 = チャージ。</para>
    /// <para><b>ボスのダウン (2026-11-13)</b>: ダウンが明けたら予兆の周期も頭から (<see cref="CombatSystem.Escalation.HeavyCycleOffset"/> を大技と同じく読む)。
    /// 起点を戻した後は、 大技と同じく「1 周期ぶん溜めてから予兆」(明けたターン = 周期の 1 ターン目・予兆は p ターン目)。
    /// 起点が 0 (ダウンが無い・旧い作り) の時は今までどおり T1/1+p/… 。 BOT の BossPlaybook も同じ関数を呼ぶ。</para>
    public static class EmberOmenTiming
    {
        public static int PeriodFor(float hpRatio) => hpRatio <= 0.5f ? 3 : 4;
        /// <summary>周期の 1 ターン目からの位置 (0 始まり)。 起点 0 なら T1 が 0、 ダウンで起点を戻した後は明けたターンが 1 (予兆は p の倍数)。</summary>
        private static int Rel(int turn, int p)
        {
            int off = CombatSystem.Escalation.HeavyCycleOffset;
            return off <= 0 ? (turn - 1) % p : (turn - off) % p;
        }
        public static bool IsOmen(int turn, float hpRatio)
        {
            if (turn < 1 || turn - CombatSystem.Escalation.HeavyCycleOffset < 1) return false;
            int p = PeriodFor(hpRatio);
            return Rel(turn, p) == 0;
        }
        public static int NextOmen(int turn, float hpRatio)
        {
            int p = PeriodFor(hpRatio);
            if (turn - CombatSystem.Escalation.HeavyCycleOffset < 1) return CombatSystem.Escalation.HeavyCycleOffset + p;   // ダウンのターン: 明けてから p ターン目
            int rel = Rel(turn, p);
            return turn + (rel == 0 ? p : (p - rel));
        }
        /// <summary>次の予兆まで何ターンか (0 = このターンが予兆・1 = 次のターンが予兆)。 ボスのダウンが予兆を取り消すかの判定
        /// (<see cref="CombatSystem.BossDown.CancelsCycle"/>・2026-11-14) が読む。 大技と同じく「予兆のターンか、 その 1 ターン前」に重なったダウンだけが周期を頭に戻す。</summary>
        public static int TurnsToOmen(int turn, float hpRatio) => IsOmen(turn, hpRatio) ? 0 : NextOmen(turn, hpRatio) - turn;
    }

}
