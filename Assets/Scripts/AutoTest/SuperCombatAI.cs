using System;

namespace AutoTest
{
    /// <summary>
    /// 旧い先読み方策 (ADR-0010 技量帯の天井・戦闘決着までのロールアウト) の<b>残り</b>。
    ///
    /// <para><b>2026-11-12 に配線とリロールの判断 (ChooseWiringPlan / ChooseReroll・新方式 V2・
    /// ロールアウト・浅い評価・並列ワーカー・実測からの自己較正) を削除した。</b> 端子の山札 (2026-11-11) の後は、
    /// どの技量でも配線は <c>DeckWiringPlanPolicy</c>、 リロールは <c>MutualRerollPolicy</c> に上書きされ
    /// (AutoRunner.Combat)、 ここの判断は 1 度も呼ばれていなかった (super_prof.txt は「配線 0 回 / リロール 0 回」)。
    /// 設計と測定の経緯は git の履歴と docs/GAME.md §13-7 を読むこと。</para>
    ///
    /// <para>残しているもの:
    /// <list type="bullet">
    ///   <item>レバーの公開フィールド ── <b>どれも今は何にも効かない。</b> Editor のメニュー (AutoRunMenu・
    ///         ItemLearningParallelRunner) とリフレクションの上書き (<c>RankMarginWorkerConfig</c> の
    ///         「superAI.winHpWeight」のような名前) が名前で指しているので、 消すかどうかは別に判断する</item>
    ///   <item>計装の口 (<see cref="DescribeSearch"/> / <see cref="DescribeProf"/> とラン単位の数) ──
    ///         レポートと worker の出力の書式を保つため。 数は常に 0</item>
    ///   <item><see cref="DmgMul"/> ── ラン全体の戦力見積もり (RunPowerBudget) が読む</item>
    /// </list></para>
    /// </summary>
    [System.Serializable]
    public sealed class SuperCombatAI
    {
        // ============================================================
        //  レバー (旧い先読みの経路を削除したので、 どれも何にも効かない)
        // ============================================================

        /// <summary>候補 1 つあたりのロールアウト本数 (効かない)。</summary>
        public int rolloutCount = 12;
        /// <summary>深く読む配線候補の数 (効かない)。</summary>
        public int candidateWirings = 6;
        /// <summary>浅い評価が同点だらけのときに候補枠を広げる数 (効かない)。</summary>
        public int tieExpandCandidates = 0;
        /// <summary>同点の中身を数える診断 (効かない)。</summary>
        public bool saturationCensus;
        /// <summary>ロールアウトの打ち切りターン数 (効かない)。</summary>
        public int rolloutDepth = 20;
        /// <summary>生存見込み (ttd) の頭打ちターン数 (効かない)。</summary>
        public float ttdCapTurns = 12f;
        /// <summary>ボス戦のロールアウト深さ (効かない)。</summary>
        public int bossRolloutDepth = 45;
        /// <summary>リロール判断で 1 案あたり振る回数 (効かない)。</summary>
        public int rerollSamples = 8;
        /// <summary>振り直す個数がこれ以下なら全列挙する (効かない)。</summary>
        public int exactRerollMaxDice = 2;
        /// <summary>1 ターンあたりのリロール試行上限 (効かない)。</summary>
        public int rerollMaxAttempts = 3;
        /// <summary>勝利時の価値に HP 残存率を足す重み (効かない)。</summary>
        public float winHpWeight = 0.5f;
        /// <summary>リロール 1 個あたりの出目底上げ (効かない)。</summary>
        public float chargeRerollGain = DefaultChargeRerollGain;
        /// <summary><see cref="chargeRerollGain"/> の既定値。 0.20 を採用して撤回した経緯は git の履歴にある。</summary>
        public const float DefaultChargeRerollGain = 0.30f;
        /// <summary>ロールアウト内の効用の重み (効かない)。</summary>
        public float rolloutTtkWeight = 4f;
        /// <summary>ロールアウト内の効用の重み (効かない)。</summary>
        public float rolloutTakenWeight = 1f;
        /// <summary>ロールアウト内の効用の重み (効かない)。</summary>
        public float rolloutChargeWeight = 0.3f;
        /// <summary>並列度 (効かない)。</summary>
        public int maxParallelism = 0;
        /// <summary>新方式 V2 (2026-11-05) で判断するか (効かない)。</summary>
        public bool useV2;
        /// <summary>V2 のロールアウト本数 (効かない)。</summary>
        public int v2Rollouts = 16;
        /// <summary>V2 のリロール判断の本数と標本数 (効かない)。</summary>
        public int v2RerollRollouts = 6, v2RerollSamples = 6;
        /// <summary>V2 の配線判断を worker.log に書く件数 (効かない)。</summary>
        public int v2TraceDecisions;

        /// <summary>現在ランの外部観測用カウンタ (常に 0)。</summary>
        [NonSerialized] public long wiringDecisions, rerollDecisions, rolloutEvaluations;

        // ============================================================
        //  [計装] 先読みの実測 (バッチ横断・static・常に 0)
        // ============================================================

        public static long SearchDecisions;
        public static long SearchOverturned;
        public static long SearchNoSeparation;
        public static long SearchShallowTied;
        public static long SearchTiedSum;
        public static long SearchExactTied;
        public static long SearchExactTiedSum;
        public static long SearchExpanded;

        public static long RrCalls;
        public static long RrDeclineAttempt;
        public static long RrDeclineAfford;
        public static long RrDeclineLethal;
        public static long RrDeclineNoGain;
        public static long RrAccept;
        public static long RrAcceptDiceSum, RrAcceptCostSum;
        public static long RrChargeSum, RrAtCap;
        public static long RrNoGainDeficitMilli;
        public static long WireChargeDice, WireChargeDecisions;
        public static int KForReport;
        public static float GainForReport = DefaultChargeRerollGain;

        public static long SatDecisions;
        public static long SatTiedTotal;
        public static long SatDistinctLanding;
        public static long SatSingleLanding;
        public static long SatBlockSaturated;
        public static long SatChargeCapped;
        public static long SatSafeProjected;
        public static long SatDistinctPHp;
        public static readonly long[] SearchRolloutDecided = new long[2];
        public static readonly long[] SearchRolloutCapped = new long[2];

        /// <summary>バッチ頭で落とす。 <b>プロセス横断の static なので、 落とさないと
        /// 前バッチの値が混ざる</b> (2026-08-10 の _combatSeq と同じ罠)。</summary>
        public static void ResetSearchStats()
        {
            SearchDecisions = SearchOverturned = SearchNoSeparation = SearchShallowTied = 0;
            SearchTiedSum = SearchExpanded = 0;
            SearchExactTied = SearchExactTiedSum = 0;
            SatDecisions = SatTiedTotal = SatDistinctLanding = SatSingleLanding = 0;
            SatBlockSaturated = SatChargeCapped = 0;
            SatSafeProjected = SatDistinctPHp = 0;
            RrCalls = RrDeclineAttempt = RrDeclineAfford = RrDeclineLethal = 0;
            RrDeclineNoGain = RrAccept = RrAcceptDiceSum = RrAcceptCostSum = 0;
            RrChargeSum = RrAtCap = RrNoGainDeficitMilli = 0;
            WireChargeDice = WireChargeDecisions = 0;
            Array.Clear(SearchRolloutDecided, 0, SearchRolloutDecided.Length);
            Array.Clear(SearchRolloutCapped, 0, SearchRolloutCapped.Length);
        }

        /// <summary>[計装] 先読みの実測。 <see cref="SearchDecisions"/> が 0 なら空文字を返す (今は常に空)。</summary>
        public static string DescribeSearch()
        {
            if (SearchDecisions <= 0) return "";
            var sb = new System.Text.StringBuilder();
            double d = SearchDecisions;
            sb.AppendLine("【先読みの実測 (SuperCombatAI)】");
            sb.AppendLine($"  ロールアウトまで進んだ配線判断 {SearchDecisions:N0}");
            sb.AppendLine($"    浅い評価の1位を覆した     {SearchOverturned,10:N0} ({100.0 * SearchOverturned / d,5:F1}%)"
                        + "  ※0%に近いと先読みは順位を変えていない");
            sb.AppendLine($"    候補間に差が付かなかった   {SearchNoSeparation,10:N0} ({100.0 * SearchNoSeparation / d,5:F1}%)"
                        + "  ※列挙順まかせで決まっている");
            sb.AppendLine($"    **厳密同値が候補枠を超えた** {SearchExactTied,10:N0} ({100.0 * SearchExactTied / d,5:F1}%)"
                        + $"  ※厳密同値 平均 {SearchExactTiedSum / d,5:F1} 本"
                        + " — ここだけが本当に列挙順で決まる");
            sb.AppendLine($"    浅い評価が最良同点だらけ  {SearchShallowTied,10:N0} ({100.0 * SearchShallowTied / d,5:F1}%)"
                        + $"  ※同点 平均 {(SearchShallowTied > 0 ? SearchTiedSum / (double)SearchShallowTied : 0):F1} 本"
                        + " — どの候補を残すかが選別されていない");
            if (SearchExpanded > 0)
                sb.AppendLine($"    同点で候補枠を広げた      {SearchExpanded,10:N0} ({100.0 * SearchExpanded / d,5:F1}%)"
                            + "  ※tieExpandCandidates 有効時のみ");
            for (int b = 0; b < 2; b++)
            {
                long dec = SearchRolloutDecided[b], cap = SearchRolloutCapped[b];
                long tot = dec + cap;
                if (tot <= 0) continue;
                sb.AppendLine($"  ロールアウト {(b == 1 ? "ボス戦" : "通常戦")}: {tot,12:N0} 本 / 決着 {dec,12:N0}"
                            + $" / 打ち切り {cap,12:N0} ({100.0 * cap / tot,5:F1}%)"
                            + (b == 1 ? "  ※打ち切りは最大0.45点。高いと順位が潰れる" : ""));
            }
            if (RrCalls > 0)
            {
                double rc = RrCalls;
                sb.AppendLine($"  【リロール判断の内訳】chargeRerollGain={GainForReport:F2}"
                            + (Math.Abs(GainForReport - DefaultChargeRerollGain) > 1e-6f
                               ? " **既定 0.30 から変更あり**" : "")
                            + $" / 呼び出し {RrCalls:N0}"
                            + $" / 入口の平均充電 {RrChargeSum / rc,5:F1}"
                            + $" (上限張り付き {100.0 * RrAtCap / rc,4:F1}%)");
                sb.AppendLine($"    採用          {RrAccept,10:N0} ({100.0 * RrAccept / rc,5:F1}%)"
                            + (RrAccept > 0 ? $"  平均 {RrAcceptDiceSum / (double)RrAccept:F2} 個"
                                            + $" / 充電 {RrAcceptCostSum / (double)RrAccept:F2}" : ""));
                sb.AppendLine($"    辞退: 撃破確定 {RrDeclineLethal,9:N0} ({100.0 * RrDeclineLethal / rc,5:F1}%)"
                            + "  ※正しい辞退");
                sb.AppendLine($"    辞退: 利得なし {RrDeclineNoGain,9:N0} ({100.0 * RrDeclineNoGain / rc,5:F1}%)"
                            + (RrDeclineNoGain > 0
                               ? $"  基準との差 平均 {RrNoGainDeficitMilli / (double)RrDeclineNoGain / 1000.0:F4}" : "")
                            + "  ※ここが多く差が小さいなら評価が保守的");
                sb.AppendLine($"    辞退: 充電不足 {RrDeclineAfford,9:N0} ({100.0 * RrDeclineAfford / rc,5:F1}%)"
                            + $" / 試行上限 {RrDeclineAttempt,9:N0} ({100.0 * RrDeclineAttempt / rc,5:F1}%)");
                if (WireChargeDecisions > 0)
                    sb.AppendLine($"    供給側: 1判断あたり充電端子へ {WireChargeDice / (double)WireChargeDecisions:F2} 個"
                                + $" (全 {KForReport} 個中)");
            }
            if (SatDecisions > 0)
            {
                double sd = SatDecisions, tied = Math.Max(1, SatTiedTotal);
                sb.AppendLine("  【飽和センサス】同点の中身 (判断 " + SatDecisions.ToString("N0") + ")");
                sb.AppendLine($"    同点 平均 {SatTiedTotal / sd,6:F1} 本 / **相異なる着地点 平均 {SatDistinctLanding / sd,5:F1} 種**");
                sb.AppendLine($"    着地点が1種類だけ  {SatSingleLanding,10:N0} ({100.0 * SatSingleLanding / sd,5:F1}%)"
                            + "  ※そのターンは本当に選択肢が無い");
                sb.AppendLine($"    うち相異なる自HP 平均 {SatDistinctPHp / sd,5:F1} 種"
                            + "  ※着地点より大幅に少なければ「自HPは同じで敵HPだけ違う」");
                sb.AppendLine($"    同点候補のうち ブロック≧敵攻撃 {100.0 * SatBlockSaturated / tied,5:F1}%"
                            + $" / 充電が上限 {100.0 * SatChargeCapped / tied,5:F1}%");
                sb.AppendLine($"    **「自分は安全」と見積もった候補 {100.0 * SatSafeProjected / tied,5:F1}%**"
                            + "  ※この経路では p が 1 に飽和し、戻り値から敵HPが消える");
                sb.AppendLine("    ※着地点が同点本数に近い → 評価の解像度不足。 1 に近い → 盤面の飽和");
            }
            return sb.ToString();
        }

        // ---- [計装 2026-09-19] 判断ごとの所要時間 (常に 0 回) ----
        public static long ProfWiringTicks, ProfWiringCalls, ProfRerollTicks, ProfRerollCalls, ProfApplyTurn;
        public static void ResetProf() { ProfWiringTicks = ProfWiringCalls = ProfRerollTicks = ProfRerollCalls = ProfApplyTurn = 0; }
        public static string DescribeProf()
        {
            double f = System.Diagnostics.Stopwatch.Frequency;
            return $"[Super計装] 配線 {ProfWiringCalls} 回 計 {ProfWiringTicks / f:F1}s (平均 {1000 * ProfWiringTicks / f / Math.Max(1, ProfWiringCalls):F2}ms)"
                 + $" / リロール {ProfRerollCalls} 回 計 {ProfRerollTicks / f:F1}s (平均 {1000 * ProfRerollTicks / f / Math.Max(1, ProfRerollCalls):F2}ms)"
                 + $" / ApplyTurn {ProfApplyTurn:N0} 回";
        }

        // ============================================================
        //  ラン単位の状態
        // ============================================================

        /// <summary>「攻撃端子合計 + 素火力」1 に対して実際に敵 HP がいくら減るか (旧い自己較正の値)。
        /// 書き換えていた較正 (Observe) は配線の判断の中にあったので、 判断と一緒に消えた。</summary>
        private float _dmgMul = 1f;

        /// <summary>較正済みの与ダメ倍率。 ラン全体の戦力見積もり (<see cref="RunPowerBudget"/>) が使う。</summary>
        public float DmgMul => _dmgMul;

        /// <summary>ラン開始時に呼ぶ。 ラン単位の数と較正を戻す。</summary>
        public void ResetForRun(int runSeed)
        {
            _dmgMul = 1f;
            wiringDecisions = rerollDecisions = rolloutEvaluations = 0;
        }
    }
}
