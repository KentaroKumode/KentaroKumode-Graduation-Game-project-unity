namespace MetaProgression
{
    /// <summary>Display-only challenge classification. It never changes gameplay effects.</summary>
    public static class ChallengeDifficultyClass
    {
        public static string NameOf(int score)
        {
            if (score < 10) return "STABLE";
            if (score < 20) return "CAUTION";
            if (score < 30) return "DANGER";
            if (score < 40) return "CRITICAL-SITUATION";
            if (score < ChallengeCatalog.TotalMaxScore) return "FATAL-ERROR";
            return "CATASTROPHIC-ERROR";
        }
    }
}
