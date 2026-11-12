using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>[計装 2026-11-12・#35] 追跡者に捕まった後の結果。 GAME.md §23-21 の目標
    /// (捕まった後の死亡 15〜25%・生還時の残り HP 30〜40%) を測るため。
    ///
    /// <para>AutoRunner がバッチ中にログを切るので、 静的カウンタで持ち、 ワーカーの応答に載せる (CombatSystem.GuardDiag と同じ)。</para></summary>
    public static class NavDiag
    {
        /// <summary>列: [0] 捕まった / [1] 勝った / [2] 逃げた / [3] 救済で生き延びた / [4] 死んだ /
        /// [5] 捕まった時の HP% の合計 / [6] 生き延びた時 (勝った・逃げた・囮罠) の HP% の合計 / [7] 捕まった時の深さの合計 /
        /// [8] 捕まった時に囮罠を持っていた / [9] 捕まった時に囮罠が地図に置いてあった。</summary>
        public const int Width = 10;
        public const int Caught = 0, Won = 1, Fled = 2, Rescued = 3, Died = 4, HpBeforeSum = 5, HpAfterSum = 6, DepthSum = 7, HadLure = 8, LureOnMap = 9;
        /// <summary>囮罠を置いた / 追跡者が踏んだ (キー "__lures" の行の [0] / [1]・型を問わない)。</summary>
        public static void NoteLure(bool triggered) => Row("__lures")[triggered ? 1 : 0]++;

        /// <summary>追跡者の型 (FoeProfile の id) ごと。 キー "all" は全部の合計。</summary>
        public static readonly Dictionary<string, double[]> Rows = new Dictionary<string, double[]>();
        private static string _cur;

        private static double[] Row(string id)
        {
            if (!Rows.TryGetValue(id, out var r)) Rows[id] = r = new double[Width];
            return r;
        }

        private static double Pct(RunState run) => run != null && run.playerMaxHP > 0 ? 100.0 * run.playerHP / run.playerMaxHP : 0.0;

        public static void Begin(string profileId, RunState run, bool lureOnMap = false)
        {
            int lures = 0;
            if (run?.ownedConsumables != null) foreach (var c in run.ownedConsumables) if (c == ItemIds.Lure) lures++;
            _cur = string.IsNullOrEmpty(profileId) ? "?" : profileId;
            foreach (var r in new[] { Row(_cur), Row("all") })
            {
                r[Caught]++; r[HpBeforeSum] += Pct(run); r[DepthSum] += run?.Depth ?? 0;
                if (lures > 0) r[HadLure]++;
                if (lureOnMap) r[LureOnMap]++;
            }
        }

        /// <summary>col = Won / Fled / Rescued / Died。</summary>
        public static void End(int col, RunState run)
        {
            if (_cur == null) return;
            foreach (var r in new[] { Row(_cur), Row("all") })
            {
                r[col]++;
                if (col != Died) r[HpAfterSum] += Pct(run);
            }
            _cur = null;
        }

        public static void Reset() { Rows.Clear(); _cur = null; }
    }
}
