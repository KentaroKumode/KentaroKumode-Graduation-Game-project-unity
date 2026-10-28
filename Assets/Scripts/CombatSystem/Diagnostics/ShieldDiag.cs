using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// [計装 2026-09-19] プレイヤーのシールドを<b>出どころ別・戦闘の種類別</b>に数える。
    ///
    /// <para>5 層ボス戦でターン開始時のシールドが平均 17〜21 残っており、 攻撃 24 のターンが実質無傷、
    /// 攻撃 111 の大技でも HP が 11 しか減っていなかった。 シールドは失効しない (expireTurn = −1) ので
    /// 毎ターンの補充は丸ごと積み上がる。 どの出どころが効いているかを推測でなく数で決めるための計数。</para>
    /// </summary>
    public static class ShieldDiag
    {
        /// <summary>いまの戦闘の種類 (SkillDiag の 0 雑魚 / 1 エリート / 2 ボス)。 CombatManager が開始時に立てる。</summary>
        public static int CurrentKind;

        private static readonly Dictionary<string, long[]> _amt = new Dictionary<string, long[]>();
        private static readonly Dictionary<string, long[]> _cnt = new Dictionary<string, long[]>();

        public static void Reset() { _amt.Clear(); _cnt.Clear(); }

        public static void Note(string source, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(source)) return;
            int k = CurrentKind < 0 || CurrentKind > 2 ? 0 : CurrentKind;
            if (!_amt.TryGetValue(source, out var a)) { a = new long[3]; _amt[source] = a; _cnt[source] = new long[3]; }
            a[k] += amount;
            _cnt[source][k]++;
        }

        /// <summary>worker の response 用。 名前と、 名前ごとに [雑魚, エリート, ボス] の 3 要素を平らに並べた配列。</summary>
        public static void Export(out string[] names, out long[] amounts, out long[] counts)
        {
            names = new string[_amt.Count];
            amounts = new long[_amt.Count * 3];
            counts = new long[_amt.Count * 3];
            int i = 0;
            foreach (var kv in _amt)
            {
                names[i] = kv.Key;
                for (int k = 0; k < 3; k++) { amounts[i * 3 + k] = kv.Value[k]; counts[i * 3 + k] = _cnt[kv.Key][k]; }
                i++;
            }
        }
    }
}
