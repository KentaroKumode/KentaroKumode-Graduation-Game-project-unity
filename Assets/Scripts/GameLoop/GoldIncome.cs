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
    /// <para>(旧: ラストスタンドの濾過 LastStand.FilterGoldGain を通す口と通さない口の区別
    /// applyLastStandFilter があったが、 濾過は常に素通しだったので 2026-11-12 に区別ごと削除)</para>
    /// </summary>
    /// <summary>金の出どころの種類 (2026-11-19)。 溶金の領主剣が数えるのは <see cref="Item"/>・<see cref="Die"/>・<see cref="Event"/> だけ
    /// (作者「秘宝・賽・イベントからゴールドを得るたび」・戦闘の報酬と売却は数えない)。 出どころの名前 (source) の綴りでは判定しない。</summary>
    public enum GoldOrigin { Other, Battle, Sale, Item, Die, Event }

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
        /// <summary>[計装 2026-11-06] <b>今のラン</b>の「深さ|経路」別の内訳。 BOT がランの頭で空にし、 ランの終わりに写す
        /// (<see cref="BySource"/> は全ランの累計なので、 クリアしたランだけを取り出せない)。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> RunByDepthSource
            = new System.Collections.Generic.Dictionary<string, int>();
        public static void ResetStats() { GainedTotal = 0; BySource.Clear(); }
        private static void Note(RunState run, string source, int granted)
        {
            if (string.IsNullOrEmpty(source)) source = "(不明)";
            BySource.TryGetValue(source, out long v);
            BySource[source] = v + granted;
            string key = run.Depth + "|" + source;
            RunByDepthSource.TryGetValue(key, out int rv);
            RunByDepthSource[key] = rv + granted;
        }

        public static int Gain(RunState run, int amount, string source, GoldOrigin origin = GoldOrigin.Other)
        {
            if (run == null || amount <= 0) return 0;

            int granted = amount;

            // 挑戦デバフ 軸3〈偽の硬貨〉: GOLD 収入 −20% / −35% / −50%。
            //   **掛ける場所はここ 1 箇所だけ。** 呼び出し側では絶対に掛けないこと。
            //   1 以上あった収入を 0 にはしない (完全な無収入は経済が停止して別ゲームになる)。

            if (granted <= 0) return 0;
            run.coins += granted;
            GainedTotal += granted;
            Note(run, source, granted);
            CountWindfall(run, origin, granted);
            MetaProgression.Achievements.AchievementService.NoteCoins(run.coins);   // 実績〈懐が重い〉
            return granted;
        }

        /// <summary>溶金の領主剣 (2026-11-19・作者): 持っている間に、 秘宝・賽・イベントから得た金を積む (<see cref="RunState.swordWindfallGold"/>)。
        /// 金を入れる口とは別に呼べる ── イベントの金は入口を通らずに足しているので、 そこからも呼ぶ (額は変えず数えるだけ)。</summary>
        public static void CountWindfall(RunState run, GoldOrigin origin, int granted)
        {
            if (run == null || granted <= 0) return;
            if (origin != GoldOrigin.Item && origin != GoldOrigin.Die && origin != GoldOrigin.Event) return;
            if (!run.OwnsPassive(ItemIds.GoldenKingSword)) return;
            run.swordWindfallGold += granted;
        }

        /// <summary>挑戦デバフの収入減を**受けない** GOLD 加算。
        /// 売却の代金 (収入ではなく資産の変換) と、 開幕所持金 (T4-D 破産 の管轄) に使う。</summary>
        public static int GainExempt(RunState run, int amount, string source)
        {
            if (run == null || amount <= 0) return 0;
            int granted = amount;
            if (granted <= 0) return 0;
            run.coins += granted;
            Note(run, source, granted);     // **免除経路も数える** (売却・開幕所持金)
            MetaProgression.Achievements.AchievementService.NoteCoins(run.coins);   // 実績〈懐が重い〉
            return granted;
        }
    }
}
