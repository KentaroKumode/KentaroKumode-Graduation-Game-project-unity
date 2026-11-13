using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// [計装 2026-11-13] ボスのダウンと手札の持ち越しの数え (バッチ累計・結果を変えない・乱数も引かない)。
    /// ボス戦ごとに: ダウンの回数・うち大技を取り消した回数・空振り (大技が溜まっていない時のダウン)・大技を受けた回数。
    /// 手札の持ち越し: ターンあたりの持ち越した枚数。 AutoRunner のサマリーが <see cref="Line"/> を出す。
    /// </summary>
    public static class BossDownDiag
    {
        /// <summary>ボス id → [戦闘数, 目盛りのある戦闘, ダウン, 大技を取り消した, 空振り (大技を持つ敵で溜まっていない), 大技を持たない敵のダウン, 大技を受けた]。</summary>
        public static readonly Dictionary<string, long[]> ByBoss = new Dictionary<string, long[]>();
        /// <summary>手札の持ち越し: ターン数 / 持ち越した枚数の合計。</summary>
        public static long CarryTurns, CarryCards;

        private static long[] _cur;

        public static void Reset() { ByBoss.Clear(); CarryTurns = 0; CarryCards = 0; KeptCards = 0; KeptUsed = 0; _cur = null; }

        /// <summary>戦闘の始め (ヴェスカは段ごと)。 ボスでなければ数えない。</summary>
        public static void BeginFight(string id, bool hasNotches)
        {
            _cur = null;
            if (!GameLoop.BossIds.IsBoss(id)) return;
            if (!ByBoss.TryGetValue(id, out _cur)) { _cur = new long[7]; ByBoss[id] = _cur; }
            _cur[0]++;
            if (hasNotches) _cur[1]++;
        }

        public static void EndFight() { _cur = null; }

        /// <summary>[計装 2026-11-14] ダウンしたターンの通算 (プロセスを通して増えるだけ・Reset で戻さない)。 戦闘ごとの数は、 読む側が戦闘の始めとの差で取る。</summary>
        public static long TotalDowns;

        public static void NoteDown(bool hasHeavy, bool canceled)
        {
            TotalDowns++;
            if (_cur == null) return;
            _cur[2]++;
            if (!hasHeavy) _cur[5]++;
            else if (canceled) _cur[3]++;
            else _cur[4]++;
        }

        public static void NoteHeavyTaken() { if (_cur != null) _cur[6]++; }

        public static void NoteCarry(int carried) { CarryTurns++; CarryCards += carried; }

        /// <summary>手札の規則 Pick: 残す印で残した端子の数 / そのうち、 残した次のターンに挿した (使った) 数。</summary>
        public static long KeptCards, KeptUsed;
        public static void NoteKeptUse(int kept, int used) { KeptCards += kept; KeptUsed += used; }

        /// <summary>サマリーの行 (何も数えていなければ空)。</summary>
        public static string Line()
        {
            if (ByBoss.Count == 0 && CarryTurns == 0) return "";
            var sb = new System.Text.StringBuilder("[ダウンと手札] ");
            sb.Append(CarryTurns > 0 ? $"手札の持ち越し ({GameLoop.GameHooks.HandCarry}) 1 ターン平均 {(double)CarryCards / CarryTurns:0.00} 枚 ({CarryTurns} ターン)" : "手札の持ち越し なし");
            if (KeptCards > 0) sb.Append($" / 印で残した端子 {KeptCards} 枚のうち、 次のターンに使った {KeptUsed} 枚 ({100.0 * KeptUsed / KeptCards:0.0}%)");
            var ids = new List<string>(ByBoss.Keys); ids.Sort(System.StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var a = ByBoss[id];
                if (a[0] == 0) continue;
                sb.Append(System.Environment.NewLine).Append("  ").Append(id)
                  .Append($": 戦闘 {a[0]} (目盛りあり {a[1]}) / ダウン {a[2]} (1 戦 {(double)a[2] / a[0]:0.00})")
                  .Append($" うち大技を取り消した {a[3]}・空振り {a[4]}・大技なしの敵 {a[5]} / 大技を受けた {a[6]} (1 戦 {(double)a[6] / a[0]:0.00})");
            }
            return sb.ToString();
        }
    }
}
