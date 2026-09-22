using System.Collections.Generic;

namespace AutoTest
{
    /// <summary>Lv 制家系（14 家系 × 最大 4 段）の**梯子が実際に登られているか**を測る計装。
    ///
    /// <para><b>なぜ要るか（2026-09-03）。</b> 学習データ (20,000 ラン) から段別の取得シェアを出すと
    /// Lv1 39.1% / Lv2 41.0% / Lv3 15.4% / Lv4 4.4% で、<b>ショップ提示重み
    /// (BRONZE 45 / SILVER 32 / GOLD 18 / LEGENDARY 5) とほぼ一致していた</b> ──
    /// つまり取得の分布は<b>需要ではなく供給</b>で決まっており、
    /// 「段が登られている」のか「別々に引かれているだけ」なのかが区別できない。</para>
    ///
    /// <para>段の最大のメリットは<b>分割払い</b>（安く入って後から 1G で伸ばす・§16-3）だが、
    /// それが発火しているかを示す数字が一つも取れていなかった。 <c>item_stats</c> には
    /// 家系内の共起データが無い（<c>synergies</c> は名前付きセット 2 件のみ）。</para>
    ///
    /// <para><b>これはバランスに一切触らない。</b> 数えるだけ。
    /// 「4 段が要るか」の判断は、この 3 つが出てから行う。</para></summary>
    public static class FamilyTierStats
    {
        /// <summary>① ショップが家系品を提示した回数。 添字 = Lv-1。</summary>
        public static readonly long[] OfferedByLevel = new long[4];

        /// <summary>① ラン内で「同じ家系が N 回提示された」の分布。 添字 = 回数 (0..7, 7 は 7 回以上)。
        /// <b>分割払いの機会が何回あるか</b>を示す。 1 回しか出会わない家系ばかりなら、
        /// 段は原理的に登れない。</summary>
        public static readonly long[] SameFamilyOfferHist = new long[8];

        /// <summary>② アップグレード割引が**提示された**回数（1G 化したスロットが並んだ）。</summary>
        public static long UpgradeOffered;

        /// <summary>② アップグレード割引で**実際に買われた**回数。
        /// <b>これが梯子を登った回数そのもの。</b> Offered との差が「見送られた」量。</summary>
        public static long UpgradeBought;

        /// <summary>② 買われた割引の段差合計（2 段飛ばしを 2 と数える）。</summary>
        public static long UpgradeStepSum;

        /// <summary>② 見送られた 1G アップグレードの内訳。 <b>店を出る時点で 1 スロット 1 回だけ</b>数える。
        ///
        /// <para><b>なぜ内訳が要るか。</b> BOT の購入は
        /// <c>if (pw &lt; effMinPower) continue;</c> の<b>足切りが価格を一切見ない</b>うえ、
        /// コスパ (<c>ΔPower/G</c>) は準パワー同点時のタイブレークにしか使われない。
        /// つまり「1G なのに買わなかった」は<b>価値判断ではなく学習ゲートの結果である可能性が高い</b>。
        /// 内訳を採らずに成約率だけを読むと、 学習の問題を段の設計の問題と取り違える。</para>
        ///
        /// <para>[0] 足切りで落ちた / [1] 閾値は超えたが他スロットに負け続けた / [2] 金が足りない</para></summary>
        public static readonly long[] UpgradeDeclineReason = new long[3];

        public const int DeclineCut = 0, DeclineOutbid = 1, DeclineNoGold = 2;

        public static void NoteUpgradeDeclined(int reason)
        {
            if (reason >= 0 && reason < UpgradeDeclineReason.Length) UpgradeDeclineReason[reason]++;
        }

        /// <summary>③ ラン終了時の家系別最高段の分布。 添字 = Lv (0 = その家系を 1 つも持たない)。
        /// <b>どこで止まっているか</b>を示す。 1 と 2 に偏るなら 4 段は過剰。</summary>
        public static readonly long[] FinalMaxLevelHist = new long[5];

        /// <summary>③ ラン数（③ の分母に使う。 1 ラン = 14 家系ぶん FinalMaxLevelHist に積むので、
        /// 家系数で割る前の生の本数を持っておく）。</summary>
        public static long RunsCounted;

        /// <summary>このランで各家系が提示された回数。 ラン終了時に <see cref="SameFamilyOfferHist"/> へ畳む。</summary>
        private static readonly Dictionary<string, int> _offersThisRun = new Dictionary<string, int>();

        public static void Reset()
        {
            System.Array.Clear(OfferedByLevel, 0, OfferedByLevel.Length);
            System.Array.Clear(SameFamilyOfferHist, 0, SameFamilyOfferHist.Length);
            System.Array.Clear(FinalMaxLevelHist, 0, FinalMaxLevelHist.Length);
            System.Array.Clear(UpgradeDeclineReason, 0, UpgradeDeclineReason.Length);
            UpgradeOffered = UpgradeBought = UpgradeStepSum = 0;
            RunsCounted = 0;
            _offersThisRun.Clear();
        }

        /// <summary>ラン開始時に呼ぶ。 ラン内カウンタだけ落とす（累計は保つ）。</summary>
        public static void BeginRun() => _offersThisRun.Clear();

        /// <summary>① ショップが家系品を 1 枠提示した。</summary>
        public static void NoteOffer(string family, int level)
        {
            if (level >= 1 && level <= 4) OfferedByLevel[level - 1]++;
            if (string.IsNullOrEmpty(family)) return;
            _offersThisRun.TryGetValue(family, out int n);
            _offersThisRun[family] = n + 1;
        }

        /// <summary>② 1G 割引スロットが並んだ。</summary>
        public static void NoteUpgradeOffered() => UpgradeOffered++;

        /// <summary>② 1G 割引スロットが買われた。</summary>
        public static void NoteUpgradeBought(int step)
        {
            UpgradeBought++;
            UpgradeStepSum += System.Math.Max(1, step);
        }

        /// <summary>ラン終了時に呼ぶ。 ① のラン内分布と ③ の最終到達段を確定させる。
        /// <paramref name="finalMaxByFamily"/> は家系名 → その時点の最高 Lv。
        /// **提示された家系だけでなく 14 家系すべて**を渡すこと ──
        /// 渡さないと「0 段で終わった家系」が数えられず、到達率が過大に出る。</summary>
        public static void EndRun(IReadOnlyDictionary<string, int> finalMaxByFamily)
        {
            foreach (var kv in _offersThisRun)
            {
                int n = kv.Value;
                if (n < 0) n = 0;
                if (n > 7) n = 7;
                SameFamilyOfferHist[n]++;
            }
            _offersThisRun.Clear();

            if (finalMaxByFamily != null)
            {
                foreach (var kv in finalMaxByFamily)
                {
                    int lv = kv.Value;
                    if (lv < 0) lv = 0;
                    if (lv > 4) lv = 4;
                    FinalMaxLevelHist[lv]++;
                }
                RunsCounted++;
            }
        }

        /// <summary>レポート 1 ブロック。</summary>
        public static string Dump()
        {
            var sb = new System.Text.StringBuilder();
            long off = 0; for (int i = 0; i < 4; i++) off += OfferedByLevel[i];
            sb.AppendLine("【家系 Tier の梯子】 ※段を減らすかの判断材料。 バランスには非干渉");
            if (off == 0) { sb.AppendLine("  提示 0 件"); return sb.ToString(); }

            sb.Append("  ① 提示 段別 : ");
            for (int i = 0; i < 4; i++) sb.Append($"Lv{i + 1} {100.0 * OfferedByLevel[i] / off:F1}%  ");
            sb.AppendLine($"(計 {off:N0})");

            long fam = 0; for (int i = 1; i < SameFamilyOfferHist.Length; i++) fam += SameFamilyOfferHist[i];
            if (fam > 0)
            {
                sb.Append("  ① 同一家系の提示回数/ラン : ");
                for (int i = 1; i < SameFamilyOfferHist.Length; i++)
                    if (SameFamilyOfferHist[i] > 0)
                        sb.Append($"{i}{(i == 7 ? "+" : "")}回 {100.0 * SameFamilyOfferHist[i] / fam:F1}%  ");
                sb.AppendLine();
                sb.AppendLine($"     ※1 回しか出会わない家系は**原理的に段を上げられない**");
            }

            sb.AppendLine($"  ② アップグレード割引 : 提示 {UpgradeOffered:N0} / 購入 {UpgradeBought:N0}"
                        + $" (成約 {(UpgradeOffered > 0 ? 100.0 * UpgradeBought / UpgradeOffered : 0):F1}%)"
                        + $" 平均段差 {(UpgradeBought > 0 ? (double)UpgradeStepSum / UpgradeBought : 0):F2}");
            sb.AppendLine("     ※購入回数が**梯子を登った回数そのもの**");
            long dc = UpgradeDeclineReason[DeclineCut], dob = UpgradeDeclineReason[DeclineOutbid],
                 dng = UpgradeDeclineReason[DeclineNoGold];
            long dsum = dc + dob + dng;
            if (dsum > 0)
            {
                sb.AppendLine($"     見送りの内訳 : 足切り {100.0 * dc / dsum:F1}%"
                            + $" / 他に負けた {100.0 * dob / dsum:F1}%"
                            + $" / 金不足 {100.0 * dng / dsum:F1}%   (計 {dsum:N0})");
                sb.AppendLine("     ※足切りが大半なら、 これは**段の設計ではなく学習ゲートの問題**");
            }

            long fin = 0; for (int i = 0; i < 5; i++) fin += FinalMaxLevelHist[i];
            if (fin > 0)
            {
                sb.Append("  ③ ラン終了時の家系別最高段 : ");
                for (int i = 0; i < 5; i++)
                    sb.Append($"{(i == 0 ? "無" : "Lv" + i)} {100.0 * FinalMaxLevelHist[i] / fin:F1}%  ");
                sb.AppendLine();
                if (RunsCounted > 0)
                {
                    double per = 0; for (int i = 1; i < 5; i++) per += i * FinalMaxLevelHist[i];
                    sb.AppendLine($"     1 ランあたり 到達段の合計 {per / RunsCounted:F2}"
                                + $" / Lv3 以上の家系 {(FinalMaxLevelHist[3] + FinalMaxLevelHist[4]) / (double)RunsCounted:F2} 個");
                }
            }
            return sb.ToString();
        }
    }
}
