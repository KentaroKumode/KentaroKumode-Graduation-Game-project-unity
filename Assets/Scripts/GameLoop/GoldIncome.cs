namespace GameLoop
{
    /// <summary>
    /// GOLD 獲得の**単一の入り口**。 `run.coins += x` を直接書かず、 必ずここを通す。
    ///
    /// 2026-07-29 に新設。 それまで獲得口が 7 ファイル 14 箇所に散在しており、
    /// 挑戦デバフ 軸3〈偽の硬貨〉(GOLD 収入 −20/−35/−50%) を実装する足場が無かった。
    ///
    /// **消費 (run.coins -= x) はここを通さない。** 収入だけを絞るのが目的で、
    /// 支払いに係数を掛けると価格デバフ (軸4 不足する物資) と二重計上になる。
    ///
    /// <para><b>ラストスタンド濾過の不整合について</b> ──
    /// 導入前から <see cref="LastStand.FilterGoldGain"/> を通す口 (9) と通さない口 (5) が
    /// 混在していた。 集約時点ではその差をそのまま写している
    /// (<paramref name="applyLastStandFilter"/>)。 統一するかどうかは別途決めること。</para>
    /// </summary>
    public static class GoldIncome
    {
        /// <summary>GOLD を獲得する。 実際に加算された額を返す。
        /// <paramref name="source"/> はログ用のラベル (「ボス報酬」「宝箱」など)。</summary>
        /// <summary>[計装 2026-09-14] 実際に入ったゴールドの累計。
        /// <b>「エリートは何を余分に配っているのか」を切り分けるため。</b>
        /// 報酬倍率を振っても傾きが動かなかったので、 ゴールド以外が主因の疑いがある。</summary>
        public static long GainedTotal;

        /// <summary>[計装 2026-09-17] <b>経路別</b>の内訳。 合計だけでは
        /// 「なぜ 1 ラン 545G も入るのか」に答えられない ── どの蛇口が太いかを出す。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, long> BySource
            = new System.Collections.Generic.Dictionary<string, long>();
        public static void ResetStats() { GainedTotal = 0; BySource.Clear(); }
        private static void Note(string source, int granted)
        {
            if (string.IsNullOrEmpty(source)) source = "(不明)";
            BySource.TryGetValue(source, out long v);
            BySource[source] = v + granted;
        }

        public static int Gain(RunState run, int amount, string source,
                               bool applyLastStandFilter = true)
        {
            if (run == null || amount <= 0) return 0;

            int granted = applyLastStandFilter ? LastStand.FilterGoldGain(run, amount) : amount;

            // 挑戦デバフ 軸3〈偽の硬貨〉: GOLD 収入 −20% / −35% / −50%。
            //   **掛ける場所はここ 1 箇所だけ。** 呼び出し側では絶対に掛けないこと。
            //   1 以上あった収入を 0 にはしない (完全な無収入は経済が停止して別ゲームになる)。
            float goldMul = MetaProgression.MetaDebuffApplicator.GetGoldGainMultiplier();
            if (goldMul < 0.999f && granted > 0)
                granted = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(granted * goldMul));

            if (granted <= 0) return 0;
            run.coins += granted;
            GainedTotal += granted;
            Note(source, granted);
            return granted;
        }

        /// <summary>挑戦デバフの収入減を**受けない** GOLD 加算。
        /// 売却の代金 (収入ではなく資産の変換) と、 開幕所持金 (T4-D 破産 の管轄) に使う。</summary>
        public static int GainExempt(RunState run, int amount, string source,
                                     bool applyLastStandFilter = true)
        {
            if (run == null || amount <= 0) return 0;
            int granted = applyLastStandFilter ? LastStand.FilterGoldGain(run, amount) : amount;
            if (granted <= 0) return 0;
            run.coins += granted;
            Note(source, granted);          // **免除経路も数える** (売却・開幕所持金)
            return granted;
        }
    }
}
