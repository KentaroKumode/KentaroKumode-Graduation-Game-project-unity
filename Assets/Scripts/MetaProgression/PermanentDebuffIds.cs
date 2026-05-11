using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// メタ層から付与される固有恒久デバフの ID 定数。
    /// run.permanentDebuffs に格納される文字列キーとして使う。
    /// </summary>
    public static class PermanentDebuffIds
    {
        public const string Pride    = "カイロスの傲慢";       // 通常戦闘の勝利報酬が0、エリート/ボス戦では2倍
        public const string Envy     = "ヤルノクの嫉妬";       // 1ショップにつき1個しか購入できない
        public const string Greed    = "ムシュファの強欲";     // 5層突入時にゴールドが0になる
        public const string Wrath    = "コルヴェンの憤怒";     // ボス戦の1T目: 自ダイス全て最大値 + 現在HP半減
        public const string Sloth    = "クェシナの怠惰";       // 戦闘開始から3T間、消費アイテム使用不可
        public const string Gluttony = "トゥルハドの暴食";     // 飢餓ダメージ ×2
        public const string Lust     = "クァディルの色欲";     // ショップ入店時、買える中で最も高価な品を強制購入
    }

    /// <summary>恒久デバフ判定の薄いヘルパー。</summary>
    public static class PermanentDebuffEffects
    {
        public static bool Has(RunState run, string id)
            => run != null && run.permanentDebuffs != null && run.permanentDebuffs.Contains(id);

        public static bool HasPride(RunState run)    => Has(run, PermanentDebuffIds.Pride);
        public static bool HasEnvy(RunState run)     => Has(run, PermanentDebuffIds.Envy);
        public static bool HasGreed(RunState run)    => Has(run, PermanentDebuffIds.Greed);
        public static bool HasWrath(RunState run)    => Has(run, PermanentDebuffIds.Wrath);
        public static bool HasSloth(RunState run)    => Has(run, PermanentDebuffIds.Sloth);
        public static bool HasGluttony(RunState run) => Has(run, PermanentDebuffIds.Gluttony);
        public static bool HasLust(RunState run)     => Has(run, PermanentDebuffIds.Lust);
    }
}
