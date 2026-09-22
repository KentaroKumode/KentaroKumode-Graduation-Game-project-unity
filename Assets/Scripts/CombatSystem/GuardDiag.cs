namespace CombatSystem
{
    /// <summary>[計装] 〈防御〉の軽減が<b>どれだけの量に触れているか</b>。
    ///
    /// <para><b>なぜ要るか。</b> 防御は 3%/段 → 4%/段 に上げても 10,000 ラン 中 14 ランしか
    /// 結果が変わらなかった。 原因として 2 つの説があり、 コードを読むだけでは決まらない。
    ///   ① 切り上げに飲まれている (オペランドが小さい)
    ///   ② そもそもこの経路を通る被ダメが一部でしかない
    ///      (固定ダメージ / かすり / DOT / 停滞 は別経路)
    /// 数字が無いまま「たぶん①」で直すと、 今日すでに 2 回やった
    /// 「読みだけで判断して外す」を繰り返す。 両方を分けて数える。</para>
    ///
    /// <para>AutoRunner がバッチ中 <c>logEnabled=false</c> にするのでログでは数えられない。
    /// 静的カウンタで持ち、 ワーカーの応答に載せる。</para></summary>
    public static class GuardDiag
    {
        /// <summary>敵の攻撃値の総和 (軽減前)。</summary>
        public static long AtkBefore;
        /// <summary>同 (軽減後)。 差が〈防御〉が実際に削った量。</summary>
        public static long AtkAfter;
        /// <summary>ブロック出目の総和。</summary>
        public static long BlockSum;
        /// <summary>ブロックを抜けて被ダメ計算へ入った量の総和。</summary>
        public static long LossBase;
        /// <summary>攻撃回数。</summary>
        public static long Attacks;
        /// <summary>軽減後にブロックで<b>全吸収</b>された回数 (lossBase == 0)。</summary>
        public static long FullyBlocked;

        public static void Reset()
        {
            AtkBefore = AtkAfter = BlockSum = LossBase = Attacks = FullyBlocked = 0;
        }

        public static void Note(int atkBefore, int atkAfter, int blockSum, int lossBase)
        {
            Attacks++;
            AtkBefore += atkBefore;
            AtkAfter += atkAfter;
            BlockSum += blockSum;
            LossBase += lossBase;
            if (lossBase <= 0) FullyBlocked++;
        }
    }
}
