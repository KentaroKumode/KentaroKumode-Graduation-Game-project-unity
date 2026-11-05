using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CombatSystem
{
    /// <summary>
    /// 特定の層の戦闘を、 先頭の数ランぶんだけターン単位で残す診断 (2026-11-05)。
    ///
    /// <para><b>なぜ要るか。</b> 層 1 の通常戦で BOT は 1 戦に最大 HP の 17〜20% を失うが、 手動プレイでは 10% 未満。
    /// 集計 (損耗・ターン数) では「配線が悪いのか、 出目が悪いのか、 ブロックが効いていないのか」を分けられない。
    /// 出目・端子の割り当て・ブロック量・敵の攻撃・受けたダメージを 1 ターンずつ並べて、 実物を読む。</para>
    ///
    /// <para>計測専用。 既定は無効 (<see cref="MaxRuns"/> = 0)。 ゲームの結果は変えない。</para>
    /// </summary>
    public static class FightTrace
    {
        /// <summary>記録するラン数 (プロセスごとに先頭からこの本数)。 0 = 無効。</summary>
        public static int MaxRuns;
        /// <summary>記録する層 (深さ)。</summary>
        public static int Depth = 1;
        public static string OutputPath = "";

        private static readonly List<string> _lines = new List<string>();
        private static readonly HashSet<int> _runs = new HashSet<int>();

        public static bool Enabled => MaxRuns > 0 && !string.IsNullOrEmpty(OutputPath);

        public static void Reset() { _lines.Clear(); _runs.Clear(); }

        /// <summary>1 ターンぶんを残す。 対象の層でなく、 記録するランの本数を超えていれば何もしない。</summary>
        public static void Turn(int depth, int runIdx, string enemyId, int turn,
                                int[] dice, DiceTerminal[] wiring,
                                int atkSum, int blockSum, int enemyAtk, int through, int taken, int dealt,
                                int hp, int maxHp, int enemyHp, int charge)
        {
            if (!Enabled || depth != Depth) return;
            if (!_runs.Contains(runIdx))
            {
                if (_runs.Count >= MaxRuns) return;
                _runs.Add(runIdx);
            }
            var sb = new StringBuilder();
            sb.Append("T\t").Append(runIdx).Append('\t').Append(enemyId).Append("\tT").Append(turn).Append("\tdice=");
            for (int i = 0; dice != null && i < dice.Length; i++) { if (i > 0) sb.Append(','); sb.Append(dice[i]); }
            sb.Append("\twire=");
            for (int i = 0; dice != null && i < dice.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var t = wiring != null && i < wiring.Length ? wiring[i] : DiceTerminal.Attack;
                sb.Append(t == DiceTerminal.Block ? 'B' : t == DiceTerminal.Charge ? 'C' : t == DiceTerminal.Special ? 'S' : 'A');
            }
            sb.Append("\tatk=").Append(atkSum).Append("\tblock=").Append(blockSum)
              .Append("\tenemyAtk=").Append(enemyAtk).Append("\tthrough=").Append(through)
              .Append("\ttaken=").Append(taken).Append("\tdealt=").Append(dealt)
              .Append("\thp=").Append(hp).Append('/').Append(maxHp).Append("\tehp=").Append(enemyHp)
              .Append("\tcharge=").Append(charge);
            _lines.Add(sb.ToString());
            if (_lines.Count > 3000) Flush();
        }

        public static void Flush()
        {
            if (_lines.Count == 0 || string.IsNullOrEmpty(OutputPath)) return;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(OutputPath, string.Join("\n", _lines) + "\n", new UTF8Encoding(false));
            }
            catch { }
            _lines.Clear();
        }
    }
}
