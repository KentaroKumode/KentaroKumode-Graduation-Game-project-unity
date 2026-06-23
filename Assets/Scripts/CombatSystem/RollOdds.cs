namespace CombatSystem
{
    /// <summary>
    /// ロール優勢度。 プレイヤーが opposed ロールに勝つ推定確率を 5 段階で表す。
    /// 期待値を毎回暗算しなくても、 ロール前に「自分が優位か不利か」を一目で読めるようにするための表示用区分。
    /// 推定勝率は <see cref="CombatManager"/> の正規近似 (EstimateWinProbability) を使う。 BOT バランスには非関与。
    /// </summary>
    public enum RollOdds
    {
        Hopeless = 0,      // 絶望的
        Hard = 1,          // 苦戦
        Even = 2,          // 互角
        Favored = 3,       // 優勢
        Overwhelming = 4,  // 圧倒的
    }

    /// <summary>ロール優勢度の分類・表示・テレグラフ。</summary>
    public static class RollOddsRating
    {
        // しきい値（プレイヤーのロール勝率）。 [0,HopelessMax)=絶望的 … [FavoredMax,1]=圧倒的。
        public const float HopelessMax = 0.15f;
        public const float HardMax     = 0.40f;
        public const float EvenMax     = 0.60f;
        public const float FavoredMax  = 0.85f;

        public static RollOdds Classify(float winProb)
        {
            if (winProb < HopelessMax) return RollOdds.Hopeless;
            if (winProb < HardMax)     return RollOdds.Hard;
            if (winProb < EvenMax)     return RollOdds.Even;
            if (winProb < FavoredMax)  return RollOdds.Favored;
            return RollOdds.Overwhelming;
        }

        public static string Label(RollOdds o)
        {
            switch (o)
            {
                case RollOdds.Hopeless:     return "絶望的";
                case RollOdds.Hard:         return "苦戦";
                case RollOdds.Even:         return "互角";
                case RollOdds.Favored:      return "優勢";
                case RollOdds.Overwhelming: return "圧倒的";
                default:                    return "—";
            }
        }

        /// <summary>UI テレグラフ配線用（引数=優勢度, 推定勝率）。ビジュアルは後付け。</summary>
        public static event System.Action<RollOdds, float> OnTelegraph;

        /// <summary>推定勝率を分類し、 テレグラフを発火して優勢度を返す。</summary>
        public static RollOdds Telegraph(float winProb)
        {
            var o = Classify(winProb);
            OnTelegraph?.Invoke(o, winProb);
            return o;
        }
    }
}
