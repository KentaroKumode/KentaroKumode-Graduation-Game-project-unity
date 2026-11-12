// Unity なしの検算用の代用品 (本体の型のうち、 TerminalCards.cs / BossDown.cs が読む所だけ)。
using System.Collections.Generic;
namespace GameLoop
{
    public class RunState { }
    public static class GameHooks
    {
        public static int TerminalHealMulOverride, TerminalArresterNeedOverride;
        public static bool TerminalFirstAidBattleOnly;
        public static CombatSystem.HandCarryMode HandCarry = CombatSystem.HandCarryMode.Pick;
    }
    public static class GameRng
    {
        static ulong Mix(ulong z) { z += 0x9E3779B97F4A7C15UL; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
        static readonly Dictionary<string, int> _n = new Dictionary<string, int>();
        public static ulong Salt = 1;
        static ulong Next(string key)
        {
            _n.TryGetValue(key, out int i); _n[key] = i + 1;
            ulong h = 1469598103934665603UL; foreach (char c in key) { h ^= c; h *= 1099511628211UL; }
            return Mix(h ^ Mix((ulong)i) ^ Salt);
        }
        public static int RangeAuto(string key, int minInclusive, int maxExclusive)
            => maxExclusive <= minInclusive ? minInclusive : minInclusive + (int)(Next(key) % (ulong)(maxExclusive - minInclusive));
        public static bool Chance(float p, string key, int index = -1) => (Next(key) >> 40) / 16777216f < p;
        public static int Count(string key) { _n.TryGetValue(key, out int i); return i; }
        public static void Reset() => _n.Clear();
    }
}
namespace ItemSystem.PassiveSkills
{
    public static class AxisRules { public const string Crit = "会心", Bleed = "出血", Poison = "毒", Heat = "臨界", Charge = "充電", Shield = "シールド"; }
}
namespace ItemSystem.PassiveSkills.Effects
{
    public static class OathSeals { public static HashSet<string> SealedAxes(GameLoop.RunState run) => new HashSet<string>(); }
}
