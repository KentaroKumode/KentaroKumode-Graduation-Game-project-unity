using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// 「死神の影」「天変地異」用の固有恒久デバフの抽選。
    /// 内容は後決め。プールが空の場合は null を返す。
    /// </summary>
    public static class MetaPermanentDebuffPicker
    {
        private static readonly List<string> pool = new List<string>
        {
            PermanentDebuffIds.Pride,
            PermanentDebuffIds.Envy,
            PermanentDebuffIds.Greed,
            PermanentDebuffIds.Wrath,
            PermanentDebuffIds.Sloth,
            PermanentDebuffIds.Gluttony,
            PermanentDebuffIds.Lust,
        };

        public static string Pick(GameLoop.RunState run, System.Random rng = null)
        {
            if (run == null || pool.Count == 0) return null;

            var available = new List<string>();
            foreach (var id in pool)
                if (!run.permanentDebuffs.Contains(id)) available.Add(id);

            if (available.Count == 0) return null;
            int idx = (rng != null) ? rng.Next(available.Count) : UnityEngine.Random.Range(0, available.Count);
            return available[idx];
        }

        public static int PoolSize => pool.Count;
    }
}
