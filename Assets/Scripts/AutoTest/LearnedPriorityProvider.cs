using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// L1学習データ (item_stats.json) を読み込んで、 動的に S/A 級を決める。
    ///
    /// 判定方針 (ユーザー指定 2026-05-30):
    ///   ・「学習で置き換え」モード: 十分なサンプル(acq6F >= minAcq6F) かつ lift6F 上位を採用
    ///   ・データ不足時は PriorityItemList の手書きに自動フォールバック
    ///   ・Tier 4 weapons (acq6F が常に小さい) は手書きを優先的に補う
    ///
    /// BOT は PriorityItemList の代わりに本クラスを参照する。
    /// 1バッチ起動時に EnsureLoaded() を呼んで item_stats.json を読み込む。
    /// </summary>
    public static class LearnedPriorityProvider
    {
        // ===== 設定 =====
        /// <summary>採用判定の最低 acq6F (これ未満はノイズ扱い)。</summary>
        public const int MinAcq6F = 30;

        // ============================================================
        //  2026-08-17b: **S/A/B/C/D/E のパーセンタイル枠を廃止**。
        //
        //  旧方式は `S 5% / A 10% / B 30% / C 25% / D 15% / E 残り` の **相対配分**だった。
        //  枠の数が固定なので、 強い品がプールに増えると **定義上ほかの品が押し出される**。
        //  出目パーツ 36 種を学習に載せた時点でこれが致命的になる ── パーツが上位を占めれば
        //  同じだけパッシブが降格し、 BOT はパッシブを買わなくなる。 実測 (2026-08-17) では
        //  パッシブ購入 7.30 → 9.37 の増加がそのまま 7層到達 57.8% → 68.3% を作っており、
        //  **パッシブが買えること自体が勝率の源泉**。 そこを枠の取り合いで削るのは危険。
        //
        //  置換: **準パワー (連続値) の絶対しきい値**。 枠を配らないので、
        //  強いパーツと強いパッシブが同時に上位へ並べる。 買う順は準パワーの降順、
        //  予算が許す限り上から。
        //
        //  しきい値は **旧パーセンタイル境界を 1 度だけ実測して固定した値**。
        //  2026-08-17 の学習データ (buffOn_debuffOff / 10 バッチ 10,000 ラン / n=159) で
        //  分位を測り、 旧 5% / 15% / 45% / 70% の位置にある準パワーを読んで丸めた:
        //      上位 5%=1.65  15%=0.45  45%=−0.06  70%=−0.34  (min −3.51 / max 7.04)
        //  → 採用 [1.60 / 0.45 / −0.05 / −0.35] で 該当 9 / 24 / 71 / 111 品。
        //  旧配分の 8 / 24 / 72 / 111 とほぼ一致するので、 **Power の絶対値は据え置き**
        //  ── InventoryPower の帯境界 (10/25/50/80) が意味を変えずに済む。
        //  以後は絶対値なので、 パーツ 36 種が学習へ載っても既存品は押し出されない。
        //  分布は Reload ごとにログと MD へ印字する。 ずれていたらここを動かす。
        // ============================================================

        /// <summary>準パワー → Power スケール (0〜4) の絶対しきい値。 **降順**。
        /// <c>[0]</c> 以上 = 絶対取得 / <c>[1]</c> 以上 = 強推 /
        /// <c>[2]</c> 以上 = 余裕時取得 / <c>[3]</c> 以上 = 中立 / それ未満 = 罠。
        /// <b>定員は無い</b> ── 何品が入るかは分布次第。
        ///
        /// <para><b>2026-09-05: 単位が変わった。</b> 準パワーは z-score ではなく
        /// <b>band スコアの生単位</b>になった (regβ = 「この品を持つと band が +N」)。
        /// したがってここの値も band 単位で読む ── <c>0.35</c> は「band を 0.35 押し上げる」。
        /// <b>「罠」= 寄与が実質ゼロ以下</b> という絶対的な意味を持つようになったので、
        /// 1 品を強化しても、 弱い品を削除しても、 他の品の帯は動かない。</para>
        ///
        /// <para>初期値は 250,000 ラン 実測の regβ 分布で置いた
        /// (生存 108 品・平均 +0.137 / 中央 +0.107 / SD 0.133 / 最大 +0.506)。
        /// 該当数は 12 / 16 / 29 / 34 / 17 品。 <b>要再校正</b> ── 効果を大量に変えた直後なので。</para>
        ///
        /// <para><b>掃引のため const にしない</b> (AutoRunner が起動時に流し込めるようにする)。</para></summary>
        public static float[] PowerCuts = { 0.35f, 0.20f, 0.10f, 0.03f };

        /// <summary><b>クリア率を目的関数にしたときのしきい値 (2026-09-09)。</b>
        /// 単位は <b>クリア率のパーセントポイント</b> ── <c>1.00</c> は
        /// 「この品を渡されるとクリア率が 1pt 上がる」。 band 単位ではないので
        /// <see cref="PowerCuts"/> とは数値の意味が違う。 <see cref="AutoRunner"/> が
        /// ITT モードのときに <c>PowerCuts</c> へ流し込む。
        ///
        /// <para><b>金の交換レートから決めた (2026-09-09 / ITT 60,000ラン)。</b> 分位ではない ──
        /// ランダム付与では<b>金も 1 つの処置</b>なので、 同じ回帰から「1G の限界価値」が出る。
        /// 実測 <c>10G → 0.270 pt/G</c> / <c>25G → 0.286 pt/G</c> (2 つの投与量でほぼ一致＝用量反応が線形)。
        /// パッシブ価格は 7〜10G なので<b>損益分岐は 1.9〜2.7 pt</b> ── これが
        /// 「買う価値があるか」の絶対的な意味を持つ。 分位に合わせないので、
        /// 品を強化しても削除しても他の品の帯は動かない (2026-09-05 の相対性の教訓)。</para>
        ///
        /// <para>該当数 (89 品中): <b>絶対取得 8 / 強推 17 / 余裕時 49 / 最低限 62</b>。
        /// <b>27 品 (30%) が価格に見合わない</b> ── 帯が痩せても分位で埋め直さないこと。
        /// 実測分位は 上位5%=19.1 / 15%=6.9 / 45%=3.6 / 70%=1.7 / 中央=3.3 (min −1.9 / max 33.2)。</para>
        ///
        /// <para><b>単位は「基準クリア率 38.9% からの pt」。</b> ロジスティックなので
        /// 基準率が変わると pt も変わる (順序は不変)。 別の条件で測り直したら
        /// 交換レートごと引き直すこと。</para></summary>
        public static float[] PowerCutsClear = { 12.0f, 6.0f, 3.0f, 1.9f };

        /// <summary>Tier 1 段ぶんの準パワー幅。 整数段で表現されていた補正
        /// (特売 +1/+2、 冗長 −1/−2、 ペルソナ ±) を連続値へ写すときの倍率。
        /// 2026-09-05: band 単位化に伴い 0.45 → 0.10 (<see cref="PowerCuts"/> の帯間隔に合わせる)。</summary>
        /// <para>2026-09-09: 目的関数がクリア率になったので <b>const をやめた</b> ──
        /// ITT モードでは単位が pt なので <see cref="StepPerTierClear"/> へ差し替わる。</para>
        public static float StepPerTier = 0.10f;
        /// <summary>クリア率 (pt) 単位での 1 段ぶん。 <see cref="PowerCutsClear"/> の帯間隔
        /// (12→6→3→1.9) のおよそ半分。 特売/冗長の補正がちょうど帯を 1 つ跨ぐ量。</summary>
        public const float StepPerTierClear = 1.5f;

        /// <summary>データ不足判定: 分類できた品がこの数未満なら手書きフォールバック。
        /// 0 = 判定実質撤廃 (item_stats.json 自体が無い/空の場合のみフォールバック)。</summary>
        public const int MinDynamicItemsToTrust = 0;

        // ===== 状態 =====
        private static bool _loaded;
        /// <summary>アイテム別の**準パワー** (連続値)。 これが唯一の序列軸。
        /// ItemLearningStats から算出した z-score 加重和 × confidence (sqrt)。
        /// ここに載っていない id は「サンプル不足 or 未知」= 学習値なし。</summary>
        private static Dictionary<string, double> _itemContinuousScore = new Dictionary<string, double>();

        // ============================================================
        //  不偏 lift の混入 (2026-08-22)
        // ============================================================
        //  **ランダム化ホールドアウトの成果物が配線されていなかった。**
        //  `ItemAggregate.ExploreLift` は選択バイアスの無い唯一の因果推定量として実装済みだったが、
        //  参照が定義の 1 行だけで、 購入方策は一度も読んでいなかった。
        //
        //  監査 (learned_score_audit.tsv): BuyScore と不偏 lift の相関は **r≈0.25〜0.33**
        //  ── 序列は「ランダムよりまし」程度。 一方その序列は 11.5pt の価値がある
        //  (ショップの順位付けだけを潰した実測)。 精度を上げる余地がここにある。
        //
        //  **単純置換はしない。** 両群が揃うのは 112/219 品で、 低 N ではノイズが支配的。
        //  標本数による縮小 w(N)=N/(N+K) を掛け、 揃わない品は w=0 で従来どおりにする。
        //  スケールが違う (準パワー vs band 差) ので **z 化して合わせる**。

        /// <summary>不偏 lift の縮小係数 K。 **0 で無効 (従来と完全に同一)**。
        /// 小さいほどホールドアウトを信じ、 大きいほど従来スコアへ寄る。</summary>
        public static float ExploreBlendK;
        // ===== ITT (ランダム付与) を序列の主成分にする (2026-09-09) =====
        // regβ は「取得したか」＝内生。 ランダム付与の割り当てで回帰し直すと外生になり、
        // 単位は同じ band なので `PowerCuts` の絶対しきい値がそのまま通る。
        // 詳細と推定量の定義は <see cref="GrantItt"/>。

        /// <summary>ITT 効果量 (<see cref="GrantItt"/>) を regβ より優先するか。
        /// <b>既定 false</b> ── 有効化は測定メニュー側で明示する。</summary>
        public static bool UseIttBeta;

        /// <summary>ITT に推定値が無い品を「未学習」として落とすか (RawPt モードでのみ意味を持つ)。</summary>
        public static bool IttStrict = true;

        /// <summary>band → pt 等化に要する最小の対応品数。 これを下回ると換算しない。
        /// <b>両方の推定量が信頼できる品だけが対になる</b>ので、 実測では 89 ITT 品のうち
        /// さらに ItemRegression.IsTrusted を通った分に限られる。</summary>
        public static int IttEquateMinPairs = 20;

        public enum IttScoreMode
        {
            /// <summary><b>順位マッピング (既定)。</b> ITT 品の得点<b>分布</b>は観測のまま固定し、
            /// <b>どの品がどのスロットに入るか</b>だけを Δクリア率の降順に付け替える。
            ///
            /// <para><b>なぜこれが既定か。</b> ITT が意見を持つのは 93 パッシブ中 89 品だけで、
            /// 武器T4・消耗品・出目パーツは付与プールに入らない。 生 pt をそのまま入れると
            /// <b>同じ序列に band 単位と pt 単位が同居する</b> ── 実測で
            /// 「上位5%=13.91(pt) … 45%=0.17(band)」という表ができ、 非ITT品が全部
            /// 足切りされて<b>パーツと消耗品を一切買わなくなる</b>。 そうなると A/B が
            /// 「序列のせいか、 パーツを買わなくなったせいか」で交絡する。
            /// 順位マッピングなら分布も非ITT品との相対位置も不変なので、
            /// <b>並び順の効果だけ</b>が分離できる。</para></summary>
            RankMap,
            /// <summary>Δクリア率 (pt) をそのまま準パワーにする。 <see cref="PowerCutsClear"/> と
            /// 併用し、 金の交換レートによる絶対判定 (「8G 払う価値があるか」) ができる。
            /// <b>付与プールが全品を覆うまでは本番に使わないこと</b> ── 覆えていない品の
            /// 単位が合わない。</summary>
            RawPt,
        }

        /// <summary>ITT の載せ方。 既定は交絡しない <see cref="IttScoreMode.RankMap"/>。</summary>
        public static IttScoreMode IttMode = IttScoreMode.RankMap;

        /// <summary><b>ITT の順位でスロットを付け替える (2026-09-09)。</b>
        /// 得点の集合は変えず、 割り当てだけを Δクリア率の降順にする ＝ 純粋な並び順の介入。
        /// 対象は「ITT に列があり、 標本が足りている」品のみ。</summary>
        private static int ApplyIttRankMapping(List<ItemLearningStats.ItemAggregate> pool)
        {
            var targets = new List<string>();
            foreach (var a in pool)
                if (GrantItt.IsTrusted(a.id)
                    && GrantItt.TryGetClearEffect(a.id, out double _, out double _2)
                    && _itemContinuousScore.ContainsKey(a.id))
                    targets.Add(a.id);
            if (targets.Count < 2) return 0;

            // 空いているスロット (= 対象品が今持っている得点) を降順に並べる
            var slots = new List<double>(targets.Count);
            foreach (var id in targets) slots.Add(_itemContinuousScore[id]);
            slots.Sort(); slots.Reverse();

            // Δクリア率の降順。 同値は id 順で決定的に割る (乱数を使わない)。
            targets.Sort((x, y) =>
            {
                GrantItt.TryGetClearEffect(x, out double dx, out double _a);
                GrantItt.TryGetClearEffect(y, out double dy, out double _b);
                int c = dy.CompareTo(dx);
                return c != 0 ? c : string.CompareOrdinal(x, y);
            });
            for (int i = 0; i < targets.Count; i++) _itemContinuousScore[targets[i]] = slots[i];
            return targets.Count;
        }

        /// <summary>準パワー降順の id 列 (MD 出力と診断用)。</summary>
        private static List<string> _ranked = new List<string>();
        /// <summary>id → acq6F。 探索のサンプル加点が「データの薄さ」を見るために使う。</summary>
        private static Dictionary<string, int> _sampleCount = new Dictionary<string, int>();
        private static bool _useFallback = true;  // true なら手書きのみ
        private static string _lastLoadedSummary = "(未読み込み)";

        // ===== 公開 API =====
        public static string LastLoadedSummary => _lastLoadedSummary;
        public static bool UsingFallback => _useFallback;

        /// <summary>その id に**学習値があるか**。 false なら呼び出し側で
        /// 代替評価へ落とすこと (パーツは学習ゼロだと永久に買われない詰みが起きる)。</summary>
        public static bool HasLearned(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLoaded();
            return !_useFallback && _itemContinuousScore.ContainsKey(id);
        }

        /// <summary>バッチ起動時に1回呼ぶ。 item_stats.json をロードして準パワーを構築。</summary>
        public static void EnsureLoaded(string learningRoot = null)
        {
            if (_loaded) return;
            _loaded = true;
            Reload(learningRoot);
        }

        // ===== 挑戦スコア帯ごとの学習 (2026-08-17) =====
        // 0pt で無双できる品と高難易度で要る品は違うので、 帯ごとに別の item_stats.json を読む。
        // **帯にデータが無い品は 0pt 帯の値で代替する**（下の _baseScore）── 帯を作った直後は
        // 空なので、 そのままだと Unlearned=-9 で全部足切りされ、
        // 「買わない → 統計が溜まらない → 永遠に買わない」で詰む（出目パーツで踏んだ罠）。
        // 帯にサンプルが貯まった品から順に上書きされていく。

        /// <summary>いま読み込んでいる挑戦スコア帯。 −1 = 未設定 (帯 0 と同じ扱い)。</summary>
        private static int _loadedBandScore = -1;
        /// <summary>0pt 帯の準パワー (帯にデータが無い品のフォールバック)。</summary>
        private static Dictionary<string, double> _baseScore = new Dictionary<string, double>();

        /// <summary>挑戦スコアに応じた学習へ切り替える。 帯が変わったときだけ読み直す。
        /// **スイープはアームごとに呼ぶこと** ── 呼ばないと前のアームの序列で買い続ける。</summary>
        public static void SwitchToChallengeScore(int challengeScore)
        {
            string band = MetaProfileHelper.ChallengeBand(challengeScore);
            string prevBand = _loadedBandScore < 0 ? null : MetaProfileHelper.ChallengeBand(_loadedBandScore);
            if (_loaded && band == prevBand) return;
            _loadedBandScore = challengeScore;

            // 帯 0 以外なら、 フォールバック用に 0pt 帯を先に読んでおく
            _baseScore.Clear();
            if (band != "band_0")
            {
                try
                {
                    string basePath = Path.GetFullPath(Path.Combine(
                        MetaProfileHelper.BotLearningRoot(0), "item_stats.json"));
                    if (File.Exists(basePath))
                    {
                        var bf = ItemLearningStats.LoadOrNew(basePath);
                        if (bf?.items != null)
                        {
                            // 0pt 帯の準パワーを得るには同じ Score 計算が要るので、
                            //   いったんその帯としてロードして退避する。
                            Reload(MetaProfileHelper.BotLearningRoot(0), writeMarkdown: false);
                            foreach (var kv in _itemContinuousScore) _baseScore[kv.Key] = kv.Value;
                        }
                    }
                }
                catch (Exception e)
                { Debug.LogWarning($"[LearnedPriorityProvider] 0pt帯の先読み失敗: {e.Message}"); }
            }

            _loaded = true;
            Reload(MetaProfileHelper.BotLearningRoot(challengeScore), writeMarkdown: false);
            Debug.Log($"[LearnedPriorityProvider] 挑戦{challengeScore}pt → 帯 {band} / "
                    + $"帯の品 {_itemContinuousScore.Count} / 0pt帯フォールバック {_baseScore.Count}");
        }

        /// <summary>強制再読み込み。 リスト変化を検出して BotJudgmentLog に追記。
        /// <paramref name="writeMarkdown"/>=false なら BALANCE_TIER_LIST.md を書かない
        /// (AIルーチン学習モードで Tier表を凍結したまま BOT用の S/A/B だけ更新する用途)。</summary>
        public static void Reload(string learningRoot = null, bool writeMarkdown = true)
        {
            // 計装のみ (既定 OFF)。 通常バッチはこの固定費を 1000 ラン に償却するが、
            // rollout worker は 1 ラン にしか償却できないので、 同じコードが相対的に
            // 桁違いに重い。 削る前に実測するための計時。
            long phase = AutoTest.Ultra.UltraPhaseClock.Begin();
            try { ReloadCore(learningRoot, writeMarkdown); }
            finally { AutoTest.Ultra.UltraPhaseClock.End("└ 学習Reload", phase); }
        }

        private static void ReloadCore(string learningRoot, bool writeMarkdown)
        {
            // 旧スナップショット (差分検出用)
            var prevTop  = new HashSet<string>(TopSet());
            var prevHigh = new HashSet<string>(HighSet());
            bool prevFallback = _useFallback;

            _itemContinuousScore.Clear();
            _ranked.Clear();
            _useFallback = true;
            _lastLoadedSummary = "(未読み込み)";

            try
            {
                if (string.IsNullOrEmpty(learningRoot))
                    learningRoot = MetaProfileHelper.LearningRoot();
                string path = Path.GetFullPath(Path.Combine(learningRoot, "item_stats.json"));
                if (!File.Exists(path))
                {
                    _lastLoadedSummary = "item_stats.json なし → 手書きフォールバック";
                    return;
                }
                long phaseStats = AutoTest.Ultra.UltraPhaseClock.Begin();   // 計装のみ
                var sf = ItemLearningStats.LoadOrNew(path);
                AutoTest.Ultra.UltraPhaseClock.End("└ └ item_stats読込", phaseStats);
                long fileSize = new FileInfo(path).Length;
                Debug.Log($"[LearnedPriorityProvider] item_stats.json size={fileSize / 1024}KB, items.Count={sf?.items?.Count ?? -1}, batches.Count={sf?.batches?.Count ?? -1}, totalRuns={sf?.totalRuns ?? -1}");
                if (sf == null || sf.items == null || sf.items.Count == 0)
                {
                    _lastLoadedSummary = $"学習データ空 (file={fileSize / 1024}KB) → 手書きフォールバック";
                    return;
                }

                // 線形回帰の係数を先に計算 (Score 評価で参照する)
                try { ItemRegression.Recompute(learningRoot); }
                catch (Exception ee) { Debug.LogWarning($"[LearnedPriorityProvider] regression 失敗: {ee.Message}"); }
                // ITT を使うなら itt_effects.json を読む。 **無ければ黙って観測へ落ちない** ──
                //   「ITT で回したつもりが regβ だった」は条件の取り違えなので、 警告を出す。
                if (UseIttBeta)
                {
                    GrantItt.TryLoad(learningRoot);
                    if (!GrantItt.TryLoadClear(learningRoot))
                        Debug.LogWarning($"[LearnedPriorityProvider] UseIttBeta=true だが itt_clear.json が無い"
                                       + $" ({GrantItt.ClearPath(learningRoot)})"
                                       + (IttStrict ? " → 付与対象品が全て未学習になる" : " → 観測 regβ へフォールバック"));
                    else
                        Debug.Log($"[LearnedPriorityProvider] 序列 = Δクリア率 / {GrantItt.SurvivalSummary}");
                }

                // 除外リスト適用 + minAcq6F フィルタ → スコア用プール確定。
                // (Score の z-score 正規化に使う μ/σ はこの確定プールで 1 回だけ算出する)
                var pool = new List<ItemLearningStats.ItemAggregate>();
                // **武器 T4 は別枠** (2026-09-05)。 z-score の μ/σ を汚さないようプールから外すが、
                //   スコアは同じ μ/σ で算出して BOT の挙動は変えない (理由は RankedSeparately の解説)。
                var weaponPool = new List<ItemLearningStats.ItemAggregate>();
                foreach (var a in sf.items)
                {
                    if (a == null || string.IsNullOrEmpty(a.id)) continue;
                    if (ItemLearningStats.ExcludedFromLift.Contains(a.id)) continue;
                    if (ItemLearningStats.DeletedItems.Contains(a.id)) continue; // 削除済は表示・分類対象外
                    if (a.acq6FRuns < MinAcq6F) continue;
                    if (ItemLearningStats.RankedSeparately.Contains(a.id)) { weaponPool.Add(a); continue; }
                    pool.Add(a);
                }

                // ============================================================
                //  総合 Score (2026-06-03 Option B: z-score 正規化)
                //  従来は生値に重みを掛けていたため、 σ が桁違いに大きい lift5F が
                //  名目重み 0.10 でもランキングを支配し、 regβ(0.30) の実効influenceが埋没していた。
                //  各指標をプールσで正規化し、 「重み = ランキング分散の取り分」を成立させる。
                //
                //  【2026-09-04 重み再配分】 各成分を**無作為化ホールドアウト由来の
                //  ExploreLift (唯一の因果推定量) との順位相関**で採点し、 配分し直した:
                //     regβ(全ラン) +0.596 / regβ(6F限定) +0.494 / lift6F +0.382
                //     formΔ +0.365 / offL6F +0.354 / lift7F +0.273 / lift5F **+0.119**
                //  旧配分は最良成分に 0.30、 ほぼ無価値な lift5F に 0.10 を与えていた。
                //  合成スコア自体を同じ基準で採点した結果 (対象 111 品):
                //     旧 0.543 → 新 0.669  (純 regβ のみは 0.585 で、 混ぜた方が良い)
                //  ⇒ regβ 55% / lift6F 15% / formΔ 15% / offL6F 10% / lift7F 5%、 **lift5F 廃止**。
                //  6F限定 regβ は混ぜると悪化した (0.669→0.658) ので表示専用。
                //
                //   regβ 未収束/低信頼時は β 重みを lift6F に転送 (従来踏襲)。
                //   信頼度: sqrt(min(1, acq6F/500)) — 中サンプル品 (300-500) を救う。
                //   一致ボーナス: 主要指標が全部「0基準の同符号」(=助ける/害する一致) で ±25% (符号は生値で判定)。
                // ============================================================

                // regβ: 2σ 有意ゲート (|b|≥2se で b、 それ以外 0)。
                //   さらに **ItemRegression.IsTrusted** ── 取得率 1% 未満の品は
                //   split-half r が 0 近傍 (実測 −0.045) で係数がノイズしかないため、
                //   収束扱いにせず lift6F へ重みを逃がす。
                // ============================================================
                //  band → pt の**等化** (2026-09-17)
                //
                //  RawPt モードは「目的関数と同じ単位 (Δクリア率 pt)」で絶対判定するために
                //  ある。 ところが ITT が意見を持つのは付与プールに入る品だけで、
                //  武器T4・消耗品・出目パーツには**列そのものが無い** (GrantItt.HasColumn)。
                //  従来はそこを regβ (band 単位) のまま返していたので、
                //  **1 つの序列に 2 つの単位が同居していた** ── 実測で
                //  「上位5%=13.91(pt) … 45%=0.17(band)」という表ができ、 非ITT品が全部
                //  足切りされて**パーツと消耗品を一切買わなくなる**。 これが RawPt を
                //  既定にできなかった理由 (IttScoreMode の解説)。
                //
                //  両方の推定値を持つ品で最小二乗 pt ≈ A + B·band を引き、 regβ しか無い品を
                //  その写像で pt へ載せる。 **外挿ではなく等化** ── 2 つの推定量は同じ
                //  ランから出ているので、 重なりのある帯で傾きを決めれば単位が揃う。
                //
                //  <b>B ≤ 0 か 標本不足なら換算しない。</b> そのときは従来どおり band を
                //  そのまま返す ＝ 単位の混在は残るが、 でたらめな写像で全品を動かすよりよい。
                //  採否は Debug ログの n / B / r を見て判断する。
                // ============================================================
                double eqA = 0, eqB = 0; bool eqOk = false;
                if (UseIttBeta && IttMode == IttScoreMode.RawPt)
                {
                    var bx = new List<double>(); var py = new List<double>();
                    foreach (var a in pool)
                    {
                        if (!GrantItt.IsTrusted(a.id)) continue;
                        if (!GrantItt.TryGetClearEffect(a.id, out double dc0, out double _e)) continue;
                        if (!ItemRegression.TryGetCoef(a.id, out double b0, out double _s)) continue;
                        if (!ItemRegression.IsTrusted(a.id)) continue;
                        bx.Add(b0); py.Add(dc0);
                    }
                    double r = 0;
                    if (bx.Count >= IttEquateMinPairs)
                    {
                        double mx = 0, my = 0;
                        for (int i = 0; i < bx.Count; i++) { mx += bx[i]; my += py[i]; }
                        mx /= bx.Count; my /= py.Count;
                        double sxy = 0, sxx = 0, syy = 0;
                        for (int i = 0; i < bx.Count; i++)
                        {
                            double dx = bx[i] - mx, dy = py[i] - my;
                            sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
                        }
                        if (sxx > 1e-12 && syy > 1e-12)
                        {
                            eqB = sxy / sxx;
                            eqA = my - eqB * mx;
                            r = sxy / Math.Sqrt(sxx * syy);
                            eqOk = eqB > 0;
                        }
                    }
                    Debug.Log($"[LearnedPriorityProvider] band→pt 等化: 対{bx.Count}品"
                            + $" / pt = {eqA:F3} + {eqB:F3}·band / r={r:F3}"
                            + $" → {(eqOk ? "採用" : "**不採用 (単位混在のまま)**")}");
                }

                // regβ: 2σ 有意ゲート (|b|≥2se で b、 それ以外 0)。
                //   さらに **ItemRegression.IsTrusted** ── 取得率 1% 未満の品は
                //   split-half r が 0 近傍 (実測 −0.045) で係数がノイズしかないため、
                //   収束扱いにせず lift6F へ重みを逃がす。
                double RegGated(ItemLearningStats.ItemAggregate a, out bool converged)
                {
                    // **ITT を優先する (2026-09-09)。** 説明変数が「取得したか (内生)」から
                    //   「ランダムに割り当てられたか (外生)」へ替わる。 さらに目的関数が
                    //   band 平均 → **クリア率**へ替わったので、 単位は Δクリア率 (pt)。
                    //   PowerCuts も同時に差し替えること (AutoRunner が流し込む)。
                    if (UseIttBeta && IttMode == IttScoreMode.RawPt
                        && GrantItt.TryGetClearEffect(a.id, out double dc, out double dse)
                        && GrantItt.IsTrusted(a.id))
                    { converged = true; return Math.Abs(dc) >= 2 * dse ? dc : 0; }
                    if (ItemRegression.TryGetCoef(a.id, out double b, out double se)
                        && ItemRegression.IsTrusted(a.id))
                    {
                        converged = true;
                        double v = Math.Abs(b) >= 2 * se ? b : 0;
                        // ITT に列が無い品はここへ来る。 RawPt では **pt へ載せ替える**。
                        return eqOk ? eqA + eqB * v : v;
                    }
                    converged = false; return 0;
                }
                // offL6F: |off|>1 は外れ値として 0 に潰す (従来踏襲)。
                double CapOff(ItemLearningStats.ItemAggregate a)
                { double off = a.OfferedLift6F; return Math.Abs(off) > 1.0 ? 0 : off; }

                void MeanStd(List<double> xs, out double mu, out double sd)
                {
                    if (xs.Count == 0) { mu = 0; sd = 0; return; }
                    double sum = 0; foreach (var x in xs) sum += x;
                    mu = sum / xs.Count;
                    double sq = 0; foreach (var x in xs) { double d = x - mu; sq += d * d; }
                    sd = Math.Sqrt(sq / xs.Count);
                }
                double Z(double x, double mu, double sd) => sd > 1e-9 ? (x - mu) / sd : 0.0;

                // プール全体の μ/σ (regβ は収束品のみ)
                var regList = new List<double>();
                foreach (var a in pool) { double v = RegGated(a, out bool cv); if (cv) regList.Add(v); }
                MeanStd(regList, out double muReg, out double sdReg);
                MeanStd(pool.ConvertAll(a => a.Lift6F), out double mu6, out double sd6);
                MeanStd(pool.ConvertAll(a => a.Lift5F), out double mu5, out double sd5);
                MeanStd(pool.ConvertAll(a => a.Lift7F), out double mu7, out double sd7);
                MeanStd(pool.ConvertAll(CapOff),        out double muOff, out double sdOff);
                MeanStd(pool.ConvertAll(a => (double)a.FormsLift6F), out double muForm, out double sdForm);

                // ============================================================
                //  **z-score を廃止し、band スコアの生単位へ移行** (2026-09-05)
                //
                //  z 化は「絶対しきい値」を名乗りながら、実際には**相対**だった ──
                //  z の 0 はプール平均なので、`PowerCuts` は「現在のプール平均から何σ下か」
                //  という意味しか持たない。 帰結:
                //    ・**1 品を強化すると平均が上がり、別の品が定義上 降格する**
                //    ・**弱い品を削除すると平均が上がり、罠帯の割合が増える**
                //      (実測シミュレーション: 182品 罠 2% → 130品 罠 10%)
                //  これは 2026-08-17 に S/A/B のパーセンタイル枠を廃止した理由
                //  「枠が固定だと強い品が増えたぶん既存品が定義上降格する」と**同じ病**で、
                //  相対性が枠から指標の中へ移動しただけだった。
                //
                //  そこで **regβ をそのままの単位で使う**。 regβ は
                //  「この品を持つとランの band スコアが +N 上がる」という絶対量なので、
                //  カタログに何が入っていようが値の意味が変わらない。
                //    ・「罠」= 寄与が実質ゼロ以下 という**絶対的な意味**を持つ
                //    ・1 品を直しても他は 1 mm も動かない
                //    ・帯の定員は保証されない ── 全品が優秀なら全員が上位帯でよい。
                //      人数を絞るのは購入判断 (金と枠) の仕事であって、評価軸の仕事ではない。
                //
                //  補助 2 成分も **band 単位のまま**足す (lift6F / offL6F はどちらも
                //  band スコアの差分なので単位が揃う)。 formΔ は「撃破段数」で単位が違うため
                //  スコアから外し、 表示のみに回した。 lift5F は 2026-09-04 に廃止済み。
                //  補助成分は交絡を含むので重みは小さく、 実質タイブレークに留める
                //  (regβ の SD 0.133 に対し 0.15×lift6F の寄与は ±0.02 程度)。
                // ============================================================
                double Score(ItemLearningStats.ItemAggregate a)
                {
                    double reg = RegGated(a, out bool converged);
                    // 未収束/低サンプルは**絶対値を推定できない**ので、 既定値を返す。
                    //   0 を返してはいけない ── 買われない → 統計が溜まらない → 永久に 0、で詰む。
                    if (!converged) return Unlearned;

                    // **ITT の生 pt を使うときは補助 2 成分を足さない (2026-09-09)。**
                    //   lift6F / offL6F はどちらも観測ベース ── 交絡した量をタイブレークに
                    //   混ぜると、 せっかく不偏にした主成分に交絡を再注入することになる。
                    //   RankMap のときは足してよい ── 値はスロットにしか使わないので、
                    //   誰がどのスロットに入るかは ITT が決める。
                    if (UseIttBeta && IttMode == IttScoreMode.RawPt) return reg;

                    return reg
                         + 0.15 * a.Lift6F
                         + 0.10 * CapOff(a);
                }

                /// <summary>不偏 lift を <see cref="_itemContinuousScore"/> へ混ぜる。
                /// **K=0 なら 1 品も触らない** ── 既存の基準値をそのまま生かすため。</summary>
                void BlendExploreLift(List<ItemLearningStats.ItemAggregate> items)
                {
                    if (ExploreBlendK <= 0f || items == null || items.Count == 0) return;

                    // 両群が揃った品だけで平均・SD を採る (z 化の土台)。
                    var el = new List<double>(items.Count);
                    var ok = new List<ItemLearningStats.ItemAggregate>(items.Count);
                    foreach (var a in items)
                    {
                        if (a == null || a.expAcqRuns <= 0 || a.expNoAcqRuns <= 0) continue;
                        ok.Add(a); el.Add(a.ExploreLift);
                    }
                    if (ok.Count < 8) return;   // 標本が薄いうちは触らない

                    double mEl = 0; foreach (var v in el) mEl += v; mEl /= el.Count;
                    double sEl = 0; foreach (var v in el) sEl += (v - mEl) * (v - mEl);
                    sEl = Math.Sqrt(sEl / Math.Max(1, el.Count - 1));
                    if (sEl <= 1e-9) return;

                    // 既存スコア側の SD (混ぜる量を同じ尺度に載せる)
                    double mSc = 0; foreach (var a in ok) mSc += _itemContinuousScore[a.id];
                    mSc /= ok.Count;
                    double sSc = 0; foreach (var a in ok)
                    { double d = _itemContinuousScore[a.id] - mSc; sSc += d * d; }
                    sSc = Math.Sqrt(sSc / Math.Max(1, ok.Count - 1));
                    if (sSc <= 1e-9) return;

                    int touched = 0;
                    foreach (var a in ok)
                    {
                        // **両群の小さい方**を有効標本とする ── 片群だけ厚くても差は締まらない。
                        double n = Math.Min(a.expAcqRuns, a.expNoAcqRuns);
                        double w = n / (n + ExploreBlendK);
                        double z = (a.ExploreLift - mEl) / sEl;
                        _itemContinuousScore[a.id] += w * z * sSc;
                        touched++;
                    }
                    Debug.Log($"[LearnedPriorityProvider] 不偏lift混入 K={ExploreBlendK:F0}"
                            + $" / 対象 {touched} 品 (全 {items.Count})"
                            + $" / lift平均 {mEl:F3} SD {sEl:F3} / スコアSD {sSc:F3}");
                }

                // 準パワー (連続値) をキャッシュし、 **降順に並べる**。
                //   枠は配らない ── 順番だけが意味を持つ (2026-08-17b にパーセンタイル配分を廃止)。
                _itemContinuousScore.Clear();
                _sampleCount.Clear();
                foreach (var a in pool) { _itemContinuousScore[a.id] = Score(a); _sampleCount[a.id] = a.acq6FRuns; }
                // 武器 T4: **同じ μ/σ でスコアだけ出す** ── BOT の買い判断は据え置き。
                //   `_ranked` (＝通し表・分位・帯の集計) には入れない。
                foreach (var a in weaponPool) { _itemContinuousScore[a.id] = Score(a); _sampleCount[a.id] = a.acq6FRuns; }
                BlendExploreLift(pool);
                if (UseIttBeta && IttMode == IttScoreMode.RankMap)
                {
                    int n = ApplyIttRankMapping(pool);
                    Debug.Log($"[LearnedPriorityProvider] ITT 順位マッピング: {n} 品のスロットを付け替え"
                            + " (得点分布は不変 ＝ 並び順だけの介入)");
                }
                pool.Sort((x, y) => _itemContinuousScore[y.id].CompareTo(_itemContinuousScore[x.id]));
                weaponPool.Sort((x, y) => _itemContinuousScore[y.id].CompareTo(_itemContinuousScore[x.id]));
                _ranked.Clear();
                foreach (var a in pool) _ranked.Add(a.id);

                // データ不足判定
                if (_ranked.Count < MinDynamicItemsToTrust)
                {
                    _lastLoadedSummary = $"分類できた品 {_ranked.Count}個 < {MinDynamicItemsToTrust} → 手書きフォールバック";
                    _itemContinuousScore.Clear(); _ranked.Clear();
                    if (writeMarkdown)
                        try { WriteTierListMarkdown(sf, pool, weaponPool, learningRoot); } catch (Exception ee)
                        { Debug.LogWarning($"[LearnedPriorityProvider] fallback MD write fail: {ee.Message}"); }
                    return;
                }

                _useFallback = false;
                // 現プロファイルの準パワーを保存 (高難度/低難度 タグで他プロファイルと突合する)
                SaveScoreAssignment(learningRoot);
                _lastLoadedSummary = $"動的学習 採用: {_ranked.Count}品 (絶対取得 {CountAtLeast(PowerCuts[0])} / 強推 {CountAtLeast(PowerCuts[1])} / 余裕時 {CountAtLeast(PowerCuts[2])}) "
                                   + $"(累積バッチ {sf.totalBatches}, ラン {sf.totalRuns})";
                Debug.Log($"[LearnedPriorityProvider] {_lastLoadedSummary}");
                // **しきい値が分布に合っているかの確認材料**。 枠を配らなくなった以上、
                //   絶対値がずれていると「全部買う」か「何も買わない」に振り切れる。
                Debug.Log($"[LearnedPriorityProvider] 準パワー分位: {DescribePercentiles()}");
                {
                    var topList = TopSet();
                    if (topList.Count > 0)
                        Debug.Log($"[LearnedPriorityProvider] 絶対取得帯: {string.Join(", ", topList)}");
                }

                // 差分検出 → BotJudgmentLog に追記
                LogIfChanged(prevTop, prevHigh, prevFallback, sf);
                // 人間向け Tier リスト Markdown を出力 (変更有無に関わらず最新で上書き)。 writeMarkdown=false なら凍結。
                if (writeMarkdown)
                    WriteTierListMarkdown(sf, pool, weaponPool, learningRoot);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LearnedPriorityProvider] 読み込み失敗: {e.Message} → 手書きフォールバック");
                _useFallback = true;
                _lastLoadedSummary = $"例外 → 手書きフォールバック: {e.Message}";
            }
        }

        /// <summary>準パワーが <paramref name="cut"/> 以上の品数。</summary>
        private static int CountAtLeast(double cut)
        {
            int n = 0;
            foreach (var kv in _itemContinuousScore) if (kv.Value >= cut) n++;
            return n;
        }

        /// <summary>準パワーが <paramref name="cut"/> 以上の id 集合 (降順)。</summary>
        private static List<string> SetAtLeast(double cut)
        {
            var list = new List<string>();
            foreach (var id in _ranked)
                if (_itemContinuousScore.TryGetValue(id, out double v) && v >= cut) list.Add(id);
            return list;
        }
        private static List<string> TopSet()  => SetAtLeast(PowerCuts[0]);
        private static List<string> HighSet() => SetAtLeast(PowerCuts[1]);

        /// <summary>しきい値が分布に合っているかを見るための分位表示。</summary>
        private static string DescribePercentiles()
        {
            if (_ranked.Count == 0) return "(空)";
            var vals = new List<double>(_ranked.Count);
            foreach (var id in _ranked) vals.Add(_itemContinuousScore[id]);
            vals.Sort(); // 昇順
            double P(double q)
            {
                int i = (int)System.Math.Round((vals.Count - 1) * q);
                return vals[System.Math.Max(0, System.Math.Min(vals.Count - 1, i))];
            }
            return $"n={vals.Count} 上位5%={P(0.95):F2} 15%={P(0.85):F2} 45%={P(0.55):F2} 70%={P(0.30):F2} "
                 + $"min={vals[0]:F2} max={vals[vals.Count - 1]:F2} / 現しきい値 "
                 + $"[{PowerCuts[0]:F2}/{PowerCuts[1]:F2}/{PowerCuts[2]:F2}/{PowerCuts[3]:F2}] "
                 + $"→ 該当 {CountAtLeast(PowerCuts[0])}/{CountAtLeast(PowerCuts[1])}/{CountAtLeast(PowerCuts[2])}/{CountAtLeast(PowerCuts[3])}";
        }

        /// <summary>「絶対取得」帯 (準パワー ≥ <c>PowerCuts[0]</c>)。 リロールしてでも取りに行く。</summary>
        public static bool IsSRank(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            // 剣の舞ピース文脈ブースト: 既に 2 枚以上所持していればピース全てを最上位扱い (BD 集約を優先誘導)
            if (IsDanceBoostedToS(id)) return true;
            return PowerScore(id) >= PowerCuts[0];   // フォールバックも Unlearned も PowerScore が吸収する
        }

        /// <summary>「余裕があれば取る」帯 (準パワー ≥ <c>PowerCuts[2]</c>)。
        /// 旧 A+B 相当。 名前は呼び出し側の互換のために残してある。</summary>
        public static bool IsARank(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return PowerScore(id) >= PowerCuts[2];
        }

        public static bool IsPriority(string id) => IsSRank(id) || IsARank(id);

        /// <summary>学習値が無い品に与える準パワー。 <b>楽観初期化</b>（2026-09-03〜）。
        ///
        /// <para><b>旧値 −9（全しきい値より下＝足切り）を廃止した。</b>
        /// あれは「どの Tier 集合にも属さない ＝ 足切り」という旧方式の挙動を連続値で保つための
        /// 番兵だったが、 <b>自己強化ループを作っていた</b> ──
        /// サンプルが無い → −9 で足切り → 買われない → サンプルが増えない → 永久に未学習。</para>
        ///
        /// <para>実害が測れている。 学習をゼロから積み直した 10,000 ラン で、
        /// <b>LEGENDARY 28 品のうち 25 品が acqRuns 200 件未満・中央 73 件</b>のまま動かなかった。
        /// パッシブ枠を 3 → 5 に増やして提示を +67% にしても改善しなかったので、
        /// <b>薄さの原因は提示不足ではなく購入されないこと</b>だと確定している。
        /// 1G アップグレードの見送り 98.4% が「足切り」だったのも同じ機構。</para>
        ///
        /// <para><b>値の置き方 (2026-09-05 に band 単位へ再設定)。</b>
        /// しきい値は <c>PowerCuts = [0.35 / 0.20 / 0.10 / 0.03]</c>。
        /// ここは <b>強推 と 余裕時 のあいだ</b>に置く ── 未学習品は
        /// 「予算が許せば買う」程度に扱えば統計は溜まる。
        /// 旧 2.00 は z-score 時代に「絶対取得の上」を狙った値で、 単位が変わった今そのままでは
        /// 全未学習品が最優先になり、 実測品を押しのけてしまう。</para>
        ///
        /// <para><b>過渡的な挙動であることが重要。</b> 一度取得されれば実測値へ置き換わるので、
        /// この優先は<b>各品につき事実上 1 回だけ</b>効き、学習が飽和すれば自然に消える。
        /// 未学習が大量にある状態（＝いま）では BOT は未知の品を掻き集めるが、それが狙い。</para>
        ///
        /// <para><b>副作用</b>: <see cref="IsSRank"/> が <c>PowerScore &gt;= PowerCuts[0]</c> なので、
        /// 未学習品は S ランク扱いになり <c>priorityItemsAcquired</c> に計上される。
        /// 学習期のこの指標は「優先品を取れた」ではなく「未知を掘った」を意味する ── <b>混同しないこと</b>。</para>
        ///
        /// <para>強く掘らせたいなら 0.40f（絶対取得の上）、 穏当にしたいなら 0.05f（中立帯）。
        /// 0 以下に戻せば旧 −9 と同じ自己強化ループ（買われない→溜まらない→永久に未学習）が戻る。</para></summary>
        public static float Unlearned = 0.15f;

        /// <summary>[廃止 2026-09-03] 家系内 Lv 補正 (LV1=0 / LV2=0.5 / LV3=1.0 / LV4=1.5)。
        ///
        /// <para><b>もう呼ばれていない。</b> 家系の Lv 制を廃し、
        /// 14 家系それぞれを <b>Lv1〜4 のどれか 1 段に固定</b>した（Tier を家系<b>間</b>の識別子へ付け替え）ため、
        /// 同一家系に複数段が同時に存在しなくなった。 この補正の役割は「同水準なら上位 Lv を選ぶ」ことで、
        /// 比較相手が消えた以上、 残すと<b>ただの家系間の下駄</b>になる ──
        /// 追撃(Lv4 固定) が剛力(Lv1 固定) より無条件に +1.5 されるのは、 学習が言っていないことである。</para>
        ///
        /// <para>メソッド自体は残置（将来 Lv 制を再導入するなら参照点になる）。 <b>PowerScore からは外した。</b></para></summary>
        private static float FamilyLvBonus(string id)
        {
            var data = InventorySystem.ItemDatabase.Instance?.GetItem(id);
            if (data?.passiveSkills == null) return 0f;
            int maxLv = 0;
            foreach (var ps in data.passiveSkills)
            {
                if (string.IsNullOrEmpty(ps.internalName)) continue;
                var (_, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                if (lv > maxLv) maxLv = lv;
            }
            return maxLv > 0 ? (maxLv - 1) * 0.5f : 0f;
        }

        /// <summary><b>準パワー</b> ── アイテムの価値を表す唯一の連続値。
        /// z-score 加重和 × 信頼度 (概ね -2.0 〜 +3.0) に家系内 Lv 補正を足したもの。
        /// <b>学習値が無ければ <see cref="Unlearned"/></b>。
        /// 手書きフォールバック中は PriorityItemList の S/A をしきい値の位置へ写す。</summary>
        /// <summary><b>regβ そのもの</b>（band 単位）。 信用できなければ NaN。
        ///
        /// <para>準パワーには lift6F / offL6F が混ざっている。 <b>武器では lift が
        /// 「選ばれやすさ」そのものになる</b> ── 1 ラン に武器は 1 本しかないので、
        /// 取得群 vs 非取得群の差が「その分岐を選んだラン vs 選ばなかったラン」に等しい。
        /// 結果、 選ばれない → サンプルが薄い → 準パワーが動かない、 の自己強化ループになる。
        /// 実測 (2026-09-06): 純 shield_t4 は <b>regβ +0.576 で全武器 1 位</b>なのに担ぎ手 1,571 ラン、
        /// 最終武器としては 0.1% ── 当時存在した複合 T4 の 1 つが 61,467 ラン 担がれて
        /// regβ +0.252 で、 <b>評価が低いほうへサンプルが集まっていた</b>。</para>
        ///
        /// <para><b>2026-09-21: 複合武器を廃止したので、 武器の分岐そのものが無くなった</b>
        /// (<c>GameManager.ChooseUpgradeTarget</c> は純ラダーを 1 段上がるだけ)。
        /// ここは武器以外の判断で引き続き使う ── 自己強化ループの理屈は
        /// 「1 ランに 1 本しか持てない」全ての枠に当てはまる。</para></summary>
        public static float RegBetaOnly(string id)
        {
            if (string.IsNullOrEmpty(id)) return float.NaN;
            EnsureLoaded();
            if (!ItemRegression.IsTrusted(id)) return float.NaN;
            return ItemRegression.TryGetCoef(id, out double b, out _) ? (float)b : float.NaN;
        }

        public static float PowerScore(string id)
        {
            if (string.IsNullOrEmpty(id)) return Unlearned;
            EnsureLoaded();
            if (_useFallback)
            {
                if (PriorityItemList.IsSRank(id)) return PowerCuts[0];
                if (PriorityItemList.IsARank(id)) return PowerCuts[1]; // フォールバックは A・B 区別なし
                return Unlearned;
            }
            if (!_itemContinuousScore.TryGetValue(id, out double v))
            {
                // 帯にサンプルが無い品は 0pt 帯の値で代替する。 これが無いと帯を作った直後に
                //   全品が足切りされ、 統計が永久に溜まらない。
                if (!_baseScore.TryGetValue(id, out v)) return Unlearned;
            }
            // [切り分け中 2026-09-03] 段固定化と同時に FamilyLvBonus を外したため、
            //   7層クリア 20.6% → 7.5% の寄与が分離できていない。 **一度に 1 つだけ変える**ため、
            //   この 1 行で戻せるようにしてある。 分離が済んだら片方を消すこと。
            return (float)v + (DisableFamilyLvBonus ? 0f : FamilyLvBonus(id));
        }

        /// <summary>FamilyLvBonus を無効化するか。 <b>既定 true = 加算しない</b>（2026-09-03〜）。
        ///
        /// <para>段固定化により同一家系に複数段が同時存在しなくなったので、
        /// この加算は「同水準なら上位 Lv」ではなく<b>家系間の恒久的な下駄</b>にしかならない
        /// （追撃(Lv4固定) が剛力(Lv1固定) より無条件に +1.5）。
        /// 実測でも寄与は <b>7層クリア +0.5pt（n=1000・SE 0.9pt ＝ 誤差）</b>で、
        /// 20.6%→7.5% の低下とは無関係だと確認済み。 <b>ゼロ点に既知の恣意を残さないため既定を切る。</b>
        /// false に戻せば旧挙動を再現できる。</para></summary>
        public static bool DisableFamilyLvBonus = true;

        // ===== 探索 (2026-08-17) =====
        // 学習は「低評価 → 買わない → サンプルが増えない → 低評価のまま」で序列が固定する。
        // 1 巡目の評価がそのまま焼き付くので、 帯別学習では特に害が大きい。
        //
        // **乱数を使わない。** ε-greedy はランダムに候補を選ぶので GameRng の消費列が変わり、
        // シード固定のペア比較 (測定の土台) が壊れる。 代わりに
        // **サンプル数が少ない品ほど準パワーへ上乗せ**する ── 決定性を保ったまま、
        // かつ「データが薄いところ」を狙って探索できる。 n が増えれば自動で 0 に減衰する。
        //
        // **学習バッチ中だけ有効にすること。** 測定中に効かせると BOT が意図的に
        // 劣る買い物をするので、 クリア率が下がって比較にならない。

        /// <summary>サンプル加点を有効にするか。 **方策側の措置**で、値付けの不偏性には効かない
        /// (UCB 型なので履歴が決まれば選択が一意 ＝ 交絡を減らさない)。
        /// 不偏な値付けは AutoRunner のランダム化ホールドアウトが担う。</summary>
        public static bool SampleBonusEnabled = false;
        /// <summary>サンプルゼロの品へ与える上乗せ。 Tier 1 段ぶん (<see cref="StepPerTier"/>) を基準に置く。</summary>
        public static float ExplorationWeight = 0.45f;
        /// <summary>この acq6F に達したら上乗せ 0。 これ未満は線形に減衰する。</summary>
        public static int ExplorationSatN = 200;

        /// <summary>その id の acq6F (現在の帯)。 学習に載っていなければ 0。</summary>
        public static int SampleCount(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            EnsureLoaded();
            return _sampleCount.TryGetValue(id, out int n) ? n : 0;
        }

        /// <summary>探索の上乗せ量。 無効なら 0。</summary>
        private static float ExplorationBonus(string id)
        {
            if (!SampleBonusEnabled || ExplorationWeight <= 0f) return 0f;
            int n = SampleCount(id);
            if (n >= ExplorationSatN) return 0f;
            return ExplorationWeight * (1f - n / (float)ExplorationSatN);
        }

        /// <summary>購入判断に使う準パワー = <see cref="PowerScore"/> − 冗長ペナルティ。
        /// 冗長 1 段につき <see cref="StepPerTier"/> を引く。 0 床は付けない
        /// ── 他がそれ以上にゴミなら「マイナスでも仕方なく買う」。</summary>
        public static float BuyScore(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            if (IsDanceForcedTop(id)) return 100f;
            return PowerScore(id) - ComputeRedundancyPenalty(id) * StepPerTier;
        }

        /// <summary>RawScore: 「所持価値」 を Power スケール (0〜4) に落とした整数。
        /// InventoryPower の帯判定が整数前提なので、 準パワーを <see cref="PowerCuts"/> で刻む。
        /// <b>枠の配分ではなく絶対しきい値</b>なので、 プールに強い品が増えても
        /// 既存品が押し出されることはない。 重複ペナルティは含めない。</summary>
        public static int RawScore(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            // 剣の舞 forced top (ラン中文脈ブースト) のみ反映
            if (IsDanceForcedTop(id)) return 100;
            EnsureLoaded();
            if (_useFallback)
            {
                if (PriorityItemList.IsSRank(id)) return 4;
                if (PriorityItemList.IsARank(id)) return 3; // フォールバックは A・B 区別なし
                return 0;
            }
            // **学習値が無ければ 0。** ここは所持価値の合算 (InventoryPower) に入るので、
            //   未知の品に点を与えると Power 帯 (10/25/50/80) が丸ごとずれる。
            //   Lv 補正も入れない ── 旧 RawScore と同じく素の Tier 相当だけを見る。
            if (!_itemContinuousScore.TryGetValue(id, out double v)
                && !_baseScore.TryGetValue(id, out v)) return 0;
            for (int i = 0; i < PowerCuts.Length; i++)
                if (v >= PowerCuts[i]) return PowerCuts.Length - i;   // [0]→4 … [3]→1
            return 0;
        }

        /// <summary>整数版の購入スコア (Power スケール)。 売却判定など整数比較が要る所で使う。
        /// 購入の序列そのものは <see cref="BuyScore"/> (連続値) を見ること。</summary>
        public static int Score(string id)
        {
            // 剣の舞ピース文脈ブースト: 3 枚以上所持なら全アイテム最優先 (集約一手前)
            if (IsDanceForcedTop(id)) return 100;
            int baseScore = RawScore(id);
            int penalty = ComputeRedundancyPenalty(id);
            return baseScore - penalty;
        }

        /// <summary>2026-06-22: 購入候補のパッシブが既に発動中 (同名 or 下位 Lv) ならペナルティ。
        /// 全 passive が冗長 → -2、 一部冗長 → -1、 新規あり → 0。
        /// 修正 2026-06-22b: 判定対象アイテム自身を firing 集合から除外する (自分の passive が
        ///                   firing に含まれているせいで自分が冗長判定されるバグ防止)。</summary>
        private static int ComputeRedundancyPenalty(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            var run = GameLoop.GameManager.Instance?.Run;
            var db = InventorySystem.ItemDatabase.Instance;
            if (run == null || db == null) return 0;
            var data = db.GetItem(id);
            if (data?.passiveSkills == null || data.passiveSkills.Count == 0) return 0;

            // 「この id を所持していない状態」 で発動するパッシブ集合を再構成
            var firing = InventoryPower.CollectFiringSkillIdsExcluding(run, db, id);
            int redundantCount = 0;
            int totalSkills = 0;
            foreach (var ps in data.passiveSkills)
            {
                if (string.IsNullOrEmpty(ps.internalName)) continue;
                totalSkills++;
                if (firing.Contains(ps.internalName)) { redundantCount++; continue; }
                if (InventorySystem.PassiveSkills.PassiveSkillRegistry.IsHigherTierPresent(ps.internalName, firing))
                    redundantCount++;
            }
            if (totalSkills == 0) return 0;
            if (redundantCount >= totalSkills) return 2;
            if (redundantCount > 0) return 1;
            return 0;
        }

        /// <summary>剣の舞ピース文脈ブースト判定 (S 扱い): 既に 2 枚以上所持なら集約路線を優先誘導。
        /// 4 枚集約成功で BD に変化するため、 残りピース取得を S Tier 同等優先度で取りに行く。</summary>
        private static bool IsDanceBoostedToS(string id)
        {
            if (!GameLoop.SwordDanceSet.IsDance(id)) return false;
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return false;
            return GameLoop.SwordDanceSet.OwnedCount(run) >= 2;
        }

        /// <summary>剣の舞ピース文脈ブースト判定 (最優先): 3 枚所持で残り 1 枚なら全アイテム最優先。</summary>
        private static bool IsDanceForcedTop(string id)
        {
            if (!GameLoop.SwordDanceSet.IsDance(id)) return false;
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return false;
            return GameLoop.SwordDanceSet.OwnedCount(run) >= 3;
        }

        /// <summary>
        /// プール (採用済アイテム) の各指標の平均と標準偏差。 z-score 判定用。
        /// debuffOff / debuffOn / buffOff 等で lift の絶対値スケールが大きく異なるため、
        /// 絶対閾値だと「片方の環境では OP 多発、もう片方では激減」になる。
        /// プール内 z-score で相対化することで環境スケールを自動キャンセル。
        /// </summary>
        private struct PhaseStats
        {
            public double mean5, mean6, mean7, meanF;
            public double std5,  std6,  std7,  stdF;
            public bool   ready;
        }
        private static PhaseStats _phaseStats;

        private static void ComputePhaseStats(List<ItemLearningStats.ItemAggregate> pool)
        {
            _phaseStats = default;
            if (pool == null || pool.Count == 0) return;
            double s5=0, s6=0, s7=0, sf=0; int n = pool.Count;
            foreach (var a in pool) { s5 += a.Lift5F; s6 += a.Lift6F; s7 += a.Lift7F; sf += a.FormsLift6F; }
            double m5=s5/n, m6=s6/n, m7=s7/n, mf=sf/n;
            double v5=0, v6=0, v7=0, vf=0;
            foreach (var a in pool)
            {
                double d;
                d = a.Lift5F - m5;     v5 += d * d;
                d = a.Lift6F - m6;     v6 += d * d;
                d = a.Lift7F - m7;     v7 += d * d;
                d = a.FormsLift6F - mf; vf += d * d;
            }
            _phaseStats = new PhaseStats
            {
                mean5 = m5, mean6 = m6, mean7 = m7, meanF = mf,
                std5  = Math.Sqrt(v5 / n),
                std6  = Math.Sqrt(v6 / n),
                std7  = Math.Sqrt(v7 / n),
                stdF  = Math.Sqrt(vf / n),
                ready = true,
            };
        }

        private static double Z(double v, double mean, double std)
            => std > 1e-9 ? (v - mean) / std : 0;

        // ===== 高難度/低難度 タグ (デバフ on/off の準パワー比較) =====
        // debuffOff(低難度) と debuffOn(高難度) で各アイテムの**準パワー**を直接比較する。
        // 各プロファイルの Reload 時に score_assignment.json を保存しておき、 それを突合:
        //   score(debuffOn) − score(debuffOff) ≥ +DiffScoreGap → [高難度] (高難度で明確に格上)
        //                                      ≤ −DiffScoreGap → [低難度]
        // 優先度: OP > 高難度/低難度 > その他 (アーリー/レイト/ミッド/バランス)。
        // 2026-08-17b: Tier(S..E) 比較から連続値比較へ。 段が消えたので差も連続値で測る。

        /// <summary>タグを付ける準パワー差。 旧 3 Tier 相当 (<see cref="StepPerTier"/> × 3)。
        /// 2026-09-09: StepPerTier が単位に応じて動くようになったのでプロパティ化。</summary>
        public static float DiffScoreGap => StepPerTier * 3f;

        private static Dictionary<string, double> _diffScore = new Dictionary<string, double>();

        [Serializable] private class ScoreEntry { public string id; public float score; }
        [Serializable] private class ScoreFile { public List<ScoreEntry> items = new List<ScoreEntry>(); }

        /// <summary>現プロファイルの準パワーを score_assignment.json に保存。
        /// 高難度/低難度 タグの突合のために他プロファイルから参照される。</summary>
        private static void SaveScoreAssignment(string learningRoot)
        {
            try
            {
                var f = new ScoreFile();
                foreach (var kv in _itemContinuousScore)
                    f.items.Add(new ScoreEntry { id = kv.Key, score = (float)kv.Value });
                Directory.CreateDirectory(learningRoot);
                File.WriteAllText(Path.Combine(learningRoot, "score_assignment.json"),
                    JsonUtility.ToJson(f, true), new System.Text.UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[LearnedPriorityProvider] score_assignment 保存失敗: {e.Message}"); }
        }

        /// <summary>指定プロファイルの score_assignment.json を {id → 準パワー} で読む。</summary>
        private static Dictionary<string, double> LoadScores(MetaProfile p)
        {
            var result = new Dictionary<string, double>();
            try
            {
                string path = Path.GetFullPath(Path.Combine(MetaProfileHelper.LearningRootFor(p), "score_assignment.json"));
                if (!File.Exists(path)) return result;
                var f = JsonUtility.FromJson<ScoreFile>(File.ReadAllText(path));
                if (f?.items == null) return result;
                foreach (var e in f.items)
                    if (e != null && !string.IsNullOrEmpty(e.id)) result[e.id] = e.score;
            }
            catch (Exception e) { Debug.LogWarning($"[LearnedPriorityProvider] score_assignment 読込失敗({p}): {e.Message}"); }
            return result;
        }

        /// <summary>デバフ on/off の準パワー差を全アイテムについて計算しキャッシュ。
        /// 両プロファイルの score_assignment.json が揃ったときのみ算出 (どちらか欠けると空 = タグ無し)。</summary>
        private static void ComputeDifficultySensitivity()
        {
            _diffScore = new Dictionary<string, double>();
            var off = LoadScores(MetaProfile.BuffOn_DebuffOff);
            var on  = LoadScores(MetaProfile.BuffOn_DebuffOn);
            if (off.Count == 0 || on.Count == 0) return;
            foreach (var kv in on)
                if (off.TryGetValue(kv.Key, out double soff))
                    _diffScore[kv.Key] = kv.Value - soff; // >0 = 高難度で格上
        }

        /// <summary>デバフ準パワー差タグ。 [高難度]/[低難度]/null。</summary>
        private static string DifficultyTag(string id)
        {
            if (string.IsNullOrEmpty(id) || !_diffScore.TryGetValue(id, out double d)) return null;
            if (d >= DiffScoreGap)  return "[高難度]";
            if (d <= -DiffScoreGap) return "[低難度]";
            return null;
        }

        /// <summary>
        /// S/A/B 級アイテムにゲームフェーズタグ付与。 環境スケール非依存 (z-score ベース)。
        ///
        ///   OP       : z5/z6/z7 全部 ≥ 1.2σ かつ acq6F ≥ 500 (プール上位 ~12% × 3指標)
        ///   高難度/低難度 : OP に次ぐ優先。 debuffOn/Off の **準パワー差** が ±DiffScoreGap 以上
        ///                  (高難度で格上=[高難度] / 低難度で格上=[低難度])。 該当すればアーリー/レイト等より優先して返す。
        ///   アーリー  : z5 ≥ 1.0 かつ (z5 − z7) ≥ 1.0 (序盤に偏重)
        ///   レイト   : 次のいずれか — 終盤偏重 (lift7F or formΔ ベース)
        ///     (a) zForm ≥ 1.5 (ヴェスカ段突破力が圧倒的 → アーリー判定より優先)
        ///     (b) z7 ≥ 0.5 かつ (z7 − z5) ≥ 0.5
        ///     (c) zForm ≥ 0.8 かつ (zForm − z5×0.5) ≥ 0.5
        ///   ミッド汎用  : どれにも該当しない場合は空 (lift6F のみ高い)
        ///
        /// z-score 化のメリット:
        ///   - debuffOff (lift がフルレンジで散らばる) / debuffOn (lift が圧縮) どちらでも
        ///     プール上位 ~12% が OP として安定的に拾える (絶対閾値ならどちらかが極端になる)
        ///   - レイト 閾値も相対化されるので、 終盤が苦しい環境でも相対的に強いレイト品を抽出可能
        ///
        /// レイト 非対称 (z7 >= 0.5 vs z5 >= 1.0) の理由 — 構造的バイアス補正:
        ///   7F到達ラン (acq7F) は acq5F より大幅に少なく lift7F の分散が大 → z7 自体が広がりやすい。
        ///   さらにサバイバル天井 (7F到達ラン自体が既に強ビルド) で lift7F は圧縮されがちなので、
        ///   z7 のシグマ閾値を z5 より低く設定。
        ///   ヴェスカ段突破数差 (formΔ) は純粋に終盤性能なので二次条件として有効。
        /// </summary>
        private static string PhaseTag(ItemLearningStats.ItemAggregate a)
        {
            if (!_phaseStats.ready) return "";  // プール統計未計算なら無印

            double z5 = Z(a.Lift5F,      _phaseStats.mean5, _phaseStats.std5);
            double z6 = Z(a.Lift6F,      _phaseStats.mean6, _phaseStats.std6);
            double z7 = Z(a.Lift7F,      _phaseStats.mean7, _phaseStats.std7);
            double zF = Z(a.FormsLift6F, _phaseStats.meanF, _phaseStats.stdF);

            const double opZ        = 1.2;
            const int    opMinAcq   = 500;
            const double earlyZ     = 0.8;   // 緩和 1.0→0.8
            const double earlyDelta = 0.7;   // 緩和 1.0→0.7
            const double lateZ      = 0.3;   // 緩和 0.5→0.3
            const double lateDelta  = 0.3;   // 緩和 0.5→0.3
            const double lateFormZ      = 0.6;  // 緩和 0.8→0.6
            const double lateFormDelta  = 0.3;  // 緩和 0.5→0.3
            const double lateFormStrong = 1.5;
            const double midZ6      = 0.3;   // [ミッド] 判定: z6 がこの値以上なら 6F バンド特化

            if (z5 >= opZ && z6 >= opZ && z7 >= opZ && a.acq6FRuns >= opMinAcq)
                return "**[OP]**";

            double d57 = z5 - z7;
            bool isEarly   = z5 >= earlyZ && d57 >= earlyDelta;
            bool lateByLift = z7 >= lateZ && -d57 >= lateDelta;
            bool lateByForm = zF >= lateFormZ && (zF - z5 * 0.5) >= lateFormDelta;
            bool isLate    = (zF >= lateFormStrong) || lateByLift || lateByForm;

            // 2026-05-31: レイト判定 かつ アーリー条件も満たす品 → OP扱い (序盤も終盤も強い実質OP)
            //   サンプル不問 (acq6F 制約はオリジナル OP のみ — こちらは「両極で強い」シグナル自体が貴重)
            if (isEarly && isLate) return "**[OP]**";

            // OP に次ぐ優先: デバフ on/off で評価が激変する品 (高難度/低難度特化)
            string diff = DifficultyTag(a.id);
            if (diff != null) return diff;

            if (isEarly) return "[アーリー]";
            if (isLate)  return "[レイト]";

            // 2026-05-31 v2: 2段フォールバック
            //   [ミッド]   = z6 ≥ 0.3σ (6Fバンド特化、 中盤に明確に効く)
            //   [バランス] = 上記いずれも非該当 (regβ/offL6F で底上げ、 フェーズ偏り無く広く貢献)
            if (z6 >= midZ6) return "[ミッド]";
            return "[バランス]";
        }

        // 共通カラムヘッダ (横軸統一で見比べやすく、 等幅フォントでも揃うようパディング)
        // 2026-08-17b: 先頭に **順位** と **準パワー** を追加、 Tier 列は廃止。
        private static readonly string[] UnifiedHeaders =
            { "#", "アイテム", "準パワー", "帯", "フェーズ", "acq6F", "lift5F", "lift6F", "lift7F", "offL6F", "regβ", "formΔ" };

        // ===== 表示名解決 =====
        // 仕様 (2026-05-31):
        //   ・末尾ローマ数字 (I/II/III/IV/V/VI/VII/VIII/IX/X) → "汎用パッシブ:<元名>"
        //   ・それ以外 → items.json から id→name 解決 (なければ id そのまま)
        // 目的: 表内で「raw ID (cons_regen_1)」と「ユニーク名 (倍音のクロック)」が混在して見づらいのを解消。
        private static Dictionary<string, string> _itemNameCache;
        private static readonly Regex _romanSuffix = new Regex(@"(?:^|[^A-Za-z])(I{1,3}|IV|VI{0,3}|IX|X)$");
        /// <summary>その id が進行武器か (items.json の family/tier)。
        /// <b>id を綴りで切らない</b> (2026-09-22) ── id は表示名。</summary>
        private static bool IsLadderWeapon(string id)
        {
            var d = string.IsNullOrEmpty(id) ? null : InventorySystem.ItemDatabase.Instance?.GetItem(id);
            return d != null && !string.IsNullOrEmpty(d.family) && d.tier >= 1 && d.tier <= 4;
        }
        private static readonly Dictionary<string, string> _weaponCatJp = new Dictionary<string, string>
        {
            { "sword",  "剣"   },
            { "shield", "盾"   },
            { "axe",    "斧"   },
            { "dagger", "短剣" },
            { "curse",  "呪"   },
            { "invest", "投資" },
        };
        private static readonly string[] _tierRomanNum = { "", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ" };

        /// <summary>
        /// シナジー探索（別枠）: 既知シナジーグループの「メンバー所持数 k 別 平均bandScore」を表示。
        ///
        /// 背景: アイテム単体の lift は「単独で取った時の効果」しか測れず、 セットで化ける品を取りこぼす。
        ///       例) サーベル・ワルツは単独だと戦闘開始HP半減で罠に見えるが、 [剣の舞]が揃うと化ける。
        ///       k(所持メンバー数) が増えるほど band が伸びるなら、 そのセットは「集める価値」がある。
        ///
        ///   soloΔ = avg(k=1) − avg(k=0)  … 単独で取った時の素の効果（負なら単独罠）
        ///   synΔ  = avg(k≥2) − avg(k=1)  … 2枚目以降の相乗（正なら集める価値）
        ///   fullΔ = avg(k=全) − avg(k=1) … フルセット到達価値
        ///
        /// データは ItemLearningStats が記録側（共起が観測できる唯一の地点）で k 別に積んだもの。
        /// グループ登録簿: SynergyGroups.cs。
        /// </summary>
        private static void WriteSynergySection(StringBuilder sb, ItemLearningStats.StatsFile sf, string learningRoot)
        {
            sb.AppendLine("## シナジー探索 (別枠) — セット相乗の検出");
            sb.AppendLine();
            sb.AppendLine("> 単体 lift では「セットで化ける品」を取りこぼす（例: サーベル・ワルツは単独だと戦闘開始HP半減で罠に見えるが、[剣の舞]が揃うと化ける）。");
            sb.AppendLine("> 既知シナジーグループごとに、 メンバー所持数 **k** 別の平均 bandScore を測り、 k が増えるほど伸びるか（=相乗）を可視化する。");
            sb.AppendLine("> **soloΔ** = avg(k=1) − avg(k=0)（単独効果。 負なら単独罠）/ **synΔ** = avg(k≥2) − avg(k=1)（2枚目以降の相乗）/ **fullΔ** = avg(k=全) − avg(k=1)（フルセット価値）。");
            sb.AppendLine("> 登録簿: [SynergyGroups.cs](Assets/Scripts/AutoTest/SynergyGroups.cs)。 数値は全ラン基準（6F到達基準も併記）。");
            sb.AppendLine();

            if (sf?.synergies == null || sf.synergies.Count == 0)
            {
                sb.AppendLine("*(シナジーデータなし — バッチ未蓄積、 または旧フォーマットの item_stats.json)*");
                sb.AppendLine();
                return;
            }

            foreach (var s in sf.synergies)
            {
                if (s == null || s.members <= 0) continue;
                int M = s.members;
                sb.AppendLine($"### {s.name}（{M}種）");
                sb.AppendLine();

                var g = System.Array.Find(SynergyGroups.All, x => x.id == s.id);
                if (g != null)
                {
                    var names = new List<string>();
                    foreach (var m in g.members) names.Add(DisplayName(m));
                    sb.AppendLine($"- メンバー: {string.Join(" / ", names)}");
                    if (!string.IsNullOrEmpty(g.note)) sb.AppendLine($"- メモ: {g.note}");
                }

                double a0 = s.KBandAvg(0), a1 = s.KBandAvg(1), aM = s.KBandAvg(M);
                double sum2 = 0; int n2 = 0;
                for (int k = 2; k <= M && k < s.kRuns.Count; k++) { sum2 += s.kBandSum[k]; n2 += s.kRuns[k]; }
                double a2 = n2 > 0 ? sum2 / n2 : 0;
                bool hasSolo = (1 < s.kRuns.Count && s.kRuns[1] > 0);
                bool hasFull = (M < s.kRuns.Count && s.kRuns[M] > 0);
                string soloD = (hasSolo && s.kRuns.Count > 0 && s.kRuns[0] > 0) ? (a1 - a0).ToString("+0.00;-0.00") : "—";
                string synD  = (n2 > 0 && hasSolo) ? (a2 - a1).ToString("+0.00;-0.00") : "—";
                string fullD = (hasFull && hasSolo) ? (aM - a1).ToString("+0.00;-0.00") : "—";
                sb.AppendLine($"- **soloΔ = {soloD}** / **synΔ = {synD}** / **fullΔ = {fullD}**");
                sb.AppendLine();

                var headers = new[] { "k(所持数)", "runs", "avgBand", "6Fruns", "6FavgBand" };
                var rows = new List<string[]>();
                for (int k = 0; k <= M && k < s.kRuns.Count; k++)
                {
                    int r6 = k < s.k6FRuns.Count ? s.k6FRuns[k] : 0;
                    rows.Add(new[]
                    {
                        k.ToString(),
                        s.kRuns[k].ToString(),
                        s.KBandAvg(k).ToString("F2"),
                        r6.ToString(),
                        s.K6FBandAvg(k).ToString("F2"),
                    });
                }
                WritePaddedTable(sb, headers, rows);
                sb.AppendLine();
            }

            // ---- 任意ペア探索（提示条件付き2×2 DiD・セット未登録の自動発掘） ----
            sb.AppendLine("### 任意ペア探索（セット未登録・自動発掘）");
            sb.AppendLine();
            sb.AppendLine("> 全アイテムの2つ組を、 **両方が提示されたラン**に限定した 2×2（A取得?×B取得?）で評価:");
            sb.AppendLine("> **interaction = avg(両取得) − avg(Aのみ) − avg(Bのみ) + avg(両未取得)**（差分の差分＝純粋な相互作用）。");
            sb.AppendLine("> 提示条件付けで出現バイアスを除去。 ただし「取得するか」はBOTポリシー依存のため選択交絡は残る（厳密因果ではなく候補抽出）。");
            sb.AppendLine($"> 全4セル各 ≥ {PairSynergyStats.MinCellForReport} サンプルのペアのみ表示。 データ: `synergy_pairs.json`（学習本体とは別管理）。");
            sb.AppendLine();

            var pf = PairSynergyStats.Load(learningRoot);
            var reportable = new List<PairSynergyStats.PairRec>();
            if (pf?.pairs != null)
                foreach (var p in pf.pairs)
                    if (p != null && p.AllCells(PairSynergyStats.MinCellForReport)) reportable.Add(p);

            if (reportable.Count == 0)
            {
                sb.AppendLine("*(報告可能なペアなし — サンプル蓄積待ち、 または synergy_pairs.json 未生成)*");
                sb.AppendLine();
                return;
            }

            reportable.Sort((x, y) => y.Interaction().CompareTo(x.Interaction()));

            void EmitPairs(string title, List<PairSynergyStats.PairRec> list)
            {
                sb.AppendLine(title);
                sb.AppendLine();
                var headers = new[] { "A", "B", "interaction", "n(両取得)", "avg両取得", "avg両未取得" };
                var rows = new List<string[]>();
                foreach (var p in list)
                    rows.Add(new[]
                    {
                        DisplayName(p.a),
                        DisplayName(p.b),
                        p.Interaction().ToString("+0.00;-0.00"),
                        p.n[3].ToString(),
                        p.Avg(3).ToString("F2"),
                        p.Avg(0).ToString("F2"),
                    });
                WritePaddedTable(sb, headers, rows);
                sb.AppendLine();
            }

            int topK = System.Math.Min(25, reportable.Count);
            EmitPairs($"**▲ 相乗トップ{topK}**（正の相互作用＝一緒に取ると単体の和を超える）",
                reportable.GetRange(0, topK));

            var neg = new List<PairSynergyStats.PairRec>(reportable);
            neg.Reverse();
            int botK = System.Math.Min(25, neg.Count);
            EmitPairs($"**▼ 反目・冗長トップ{botK}**（負の相互作用＝重ねても伸びない/食い合う）",
                neg.GetRange(0, botK));
        }

        /// <summary>
        /// 武器 T4 の専用テーブル (2026-09-05)。 **通し表とは別プール**。
        ///
        /// <para>1 ランに武器は 1 本しかないので、 武器ダミーの「持たない側」は
        /// <b>そのチェーンを完走できなかったラン</b>になる。 パッシブの「200 品のうち 1 品だけ買わなかった」
        /// とは反実仮想の大きさが違い、 regβ が系統的に 6 倍膨らむ
        /// (実測 武器 μ=+0.786 / パッシブ μ=+0.128)。 <b>同じ表に並べて順位を付けてはいけない。</b></para>
        ///
        /// <para>ただし<b>武器同士の比較は成立する</b> ── 4 種とも参照カテゴリが同じ
        /// (「このチェーンを完走しなかった」) なので、 相対順位は読んでよい。</para>
        /// </summary>
        private static void WriteWeaponTierSection(StringBuilder sb, List<ItemLearningStats.ItemAggregate> weaponPool)
        {
            sb.AppendLine("## 武器 T4 (別プール・通し表とは比較不能)");
            sb.AppendLine();
            sb.AppendLine("> **通し表に載せない理由。** 1 ランに武器は 1 本しかないので、 武器ダミーの「持たない側」は");
            sb.AppendLine("> **そのチェーンを完走できなかったラン**になる (実測: 斧T3止まり の 7層クリア 5.4% → T4 到達 66.0%)。");
            sb.AppendLine("> パッシブの「200 品のうちこの 1 品だけ買わなかった」とは反実仮想の大きさが桁違いで、");
            sb.AppendLine("> regβ の平均が **武器 +0.786 / パッシブ +0.128 (6 倍)** と系統的に膨らむ。");
            sb.AppendLine("> これは強さではなく参照カテゴリの違いなので、 **同じ表で順位を付けてはいけない**。");
            sb.AppendLine(">");
            sb.AppendLine("> **武器同士の比較は成立する** ── 4 種とも参照カテゴリが同じなので相対順位は読んでよい。");
            sb.AppendLine("> 準パワーの値は通し表と同じ μ/σ で算出している (BOT の買い判断を変えないため)。");
            sb.AppendLine("> 回帰の特徴量からは外していない ── 外すと「武器を育て切ったか」が残差へ落ち、");
            sb.AppendLine("> それと相関する品の β を歪める (欠落変数バイアス)。");
            sb.AppendLine();
            if (weaponPool == null || weaponPool.Count == 0)
            { sb.AppendLine("*(該当なし)*"); sb.AppendLine(); return; }
            var rows = new List<string[]>();
            int rank = 0;
            foreach (var a in weaponPool) rows.Add(BuildRow(a, ++rank, false));
            WritePaddedTable(sb, UnifiedHeaders, rows);
            sb.AppendLine();
        }

        /// <summary>
        /// 進化チェーン武器 (T1-T3) のミッド性能だけを抽出して別表で表示。
        ///
        /// 背景: T1-T3 武器は ExcludedFromLift で Tier 評価対象外。
        ///       理由は lift7F/formΔ にチェーン進化先 (T4) の効果が流れ込むため。
        ///       しかし lift5F/lift6F は「拾った時点のミッド性能」を測れる (T4 化前の戦闘が中心)。
        ///       「この T2 はミッド無双する」というデータを別枠で見るための参考表示。
        ///
        /// ミッドScore = 0.5 × lift5F + 0.5 × lift6F (lift7F/formΔ は使わない)
        /// </summary>
        private static void WriteChainWeaponReference(StringBuilder sb, ItemLearningStats.StatsFile sf)
        {
            sb.AppendLine("## 進化チェーン武器 参考データ (T1-T3、 Tier評価対象外)");
            sb.AppendLine();
            sb.AppendLine("> T1-T3 武器は進化下流の T4 効果が lift7F/formΔ に流れ込むため Tier 分類から除外。");
            sb.AppendLine("> ただし lift5F/lift6F は「拾った時点のミッド性能」を反映するので、 ミッド無双品の識別に有用。");
            sb.AppendLine("> **ミッドScore** = 0.5 × lift5F + 0.5 × lift6F (lift7F/formΔ は使わず、 チェーン汚染を回避)");
            sb.AppendLine();

            if (sf?.items == null) { sb.AppendLine("*(データなし)*"); sb.AppendLine(); return; }

            var chain = new List<ItemLearningStats.ItemAggregate>();
            foreach (var a in sf.items)
            {
                if (a == null || string.IsNullOrEmpty(a.id)) continue;
                if (!ItemLearningStats.ExcludedFromLift.Contains(a.id)) continue;
                if (!IsLadderWeapon(a.id)) continue; // 武器階梯のみ
                if (a.acq6FRuns < MinAcq6F) continue;
                chain.Add(a);
            }
            if (chain.Count == 0) { sb.AppendLine("*(該当なし)*"); sb.AppendLine(); return; }

            double MidScore(ItemLearningStats.ItemAggregate a) => 0.5 * a.Lift5F + 0.5 * a.Lift6F;
            chain.Sort((x, y) => MidScore(y).CompareTo(MidScore(x)));

            var headers = new[] { "武器", "acq6F", "ミッドScore", "lift5F", "lift6F", "offL6F", "regβ" };
            var rows = new List<string[]>();
            foreach (var a in chain)
            {
                string reg = ItemRegression.TryGetCoef(a.id, out double bv, out double se)
                             ? $"{bv:+0.00;-0.00}±{se:F2}" : "—";
                rows.Add(new[]
                {
                    "`" + DisplayName(a.id) + "`",
                    a.acq6FRuns.ToString(),
                    MidScore(a).ToString("+0.00;-0.00"),
                    a.Lift5F.ToString("F2"),
                    a.Lift6F.ToString("F2"),
                    a.OfferedLift6F.ToString("+0.00;-0.00"),
                    reg,
                });
            }
            WritePaddedTable(sb, headers, rows);
            sb.AppendLine();
        }

        /// <summary>武器 ID なら "[剣Ⅳ]" などのカテゴリ+階梯ラベルを返す。 該当しなければ ""。</summary>
        private static string WeaponLabel(string id)
        {
            if (!IsLadderWeapon(id)) return "";
            var d = InventorySystem.ItemDatabase.Instance.GetItem(id);
            if (!_weaponCatJp.TryGetValue(d.family, out var jp)) return "";
            return $"[{jp}{_tierRomanNum[d.tier]}]";
        }

        private static void EnsureNameCache()
        {
            if (_itemNameCache != null) return;
            _itemNameCache = new Dictionary<string, string>();
            try
            {
                string path = Path.Combine(Application.dataPath, "Data/InventorySystem/items.json");
                if (!File.Exists(path)) return;
                var text = File.ReadAllText(path);
                var rx = new Regex("\"id\"\\s*:\\s*\"([^\"]+)\".*?\"name\"\\s*:\\s*\"([^\"]+)\"",
                                   RegexOptions.Singleline);
                int pos = 0;
                while (pos < text.Length)
                {
                    var m = rx.Match(text, pos);
                    if (!m.Success) break;
                    string id = m.Groups[1].Value;
                    string name = m.Groups[2].Value;
                    if (!_itemNameCache.ContainsKey(id)) _itemNameCache[id] = name;
                    pos = m.Index + m.Length;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[LearnedPriorityProvider] item name cache 構築失敗: {e.Message}"); }
        }

        private static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            // 出目パーツは items.json に無い ── 生成元 (DiceFaceParts) から表示名を引く
            var partLabel = GameLoop.DiceFaceParts.LabelOfId(id);
            if (partLabel != null) return "出目パーツ:" + partLabel;
            // 末尾ローマ数字 → 汎用パッシブ
            if (_romanSuffix.IsMatch(id)) return "汎用パッシブ:" + id;
            // items.json 解決
            EnsureNameCache();
            string baseName;
            if (_itemNameCache != null && _itemNameCache.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n))
                baseName = n;
            else
                baseName = id;
            // 武器 ID なら "[剣Ⅳ]" 等のカテゴリ+階梯ラベルを末尾に付与
            string wlabel = WeaponLabel(id);
            return string.IsNullOrEmpty(wlabel) ? baseName : baseName + " " + wlabel;
        }

        /// <summary>CJK 文字を2幅として計算する表示幅 (等幅フォント想定)。</summary>
        private static int DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (var c in s)
            {
                // CJK / 全角 / かな / カナ / ハングル / 全角記号
                // East Asian Ambiguous (EAW=A) のうち、 日本語環境で 2幅 描画される代表を追加:
                //   - 0x2160-0x217F: ローマ数字 (Ⅰ Ⅱ Ⅲ Ⅳ Ⅴ Ⅵ Ⅶ Ⅷ Ⅸ Ⅹ Ⅺ Ⅻ ⅰ ⅱ ...)
                //   - 0x2460-0x24FF: 囲み英数字 (① ② ③ ...)
                //   - 0x25A0-0x26FF: 幾何学・その他記号 (■ ● ★ ☆ ♠ ...)
                //   - 0x2E80-0x303E: CJK 部首・記号 (既存範囲だが念のため明示)
                if ((c >= 0x1100 && c <= 0x115F) || c == 0x2329 || c == 0x232A ||
                    (c >= 0x2160 && c <= 0x217F) ||
                    (c >= 0x2460 && c <= 0x24FF) ||
                    (c >= 0x25A0 && c <= 0x26FF) ||
                    (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||
                    (c >= 0xAC00 && c <= 0xD7A3) ||
                    (c >= 0xF900 && c <= 0xFAFF) ||
                    (c >= 0xFE30 && c <= 0xFE4F) ||
                    (c >= 0xFF00 && c <= 0xFF60) ||
                    (c >= 0xFFE0 && c <= 0xFFE6))
                    w += 2;
                else
                    w += 1;
            }
            return w;
        }

        private static string PadDisplay(string s, int width)
        {
            int diff = width - DisplayWidth(s);
            return diff > 0 ? s + new string(' ', diff) : s;
        }

        /// <summary>準パワーが属する帯の表示ラベル。 <b>枠ではなくしきい値</b>なので、
        /// ここに何個入るかは分布次第 (定員は無い)。</summary>
        private static string BandLabel(double score)
        {
            if (score >= PowerCuts[0]) return "🟥絶対取得";
            if (score >= PowerCuts[1]) return "🟧強推";
            if (score >= PowerCuts[2]) return "🟨余裕時";
            if (score >= PowerCuts[3]) return "🟦中立";
            return "💀罠";
        }

        private static string[] BuildRow(ItemLearningStats.ItemAggregate a, int rank, bool showTag)
        {
            double sc = _itemContinuousScore.TryGetValue(a.id, out double v) ? v : double.NaN;
            string scStr = double.IsNaN(sc) ? "—" : sc.ToString("+0.00;-0.00");
            string band  = double.IsNaN(sc) ? "—" : BandLabel(sc);
            string regB = ItemRegression.TryGetCoef(a.id, out double bv, out double se)
                          ? $"{bv:+0.00;-0.00}±{se:F2}" : "—";
            return new[]
            {
                rank > 0 ? rank.ToString() : "—",
                "`" + DisplayName(a.id) + "`",
                scStr,
                band,
                showTag ? PhaseTag(a) : "",
                a.acq6FRuns.ToString(),
                a.Lift5F.ToString("F2"),
                a.Lift6F.ToString("F2"),
                a.Lift7F.ToString("F2"),
                a.OfferedLift6F.ToString("+0.00;-0.00"),
                regB,
                a.FormsLift6F.ToString("+0.00;-0.00"),
            };
        }

        /// <summary>等幅フォントで縦揃えする左揃え Markdown 表を出力。</summary>
        private static void WritePaddedTable(StringBuilder sb, string[] headers, List<string[]> rows)
        {
            int cols = headers.Length;
            int[] widths = new int[cols];
            for (int i = 0; i < cols; i++) widths[i] = DisplayWidth(headers[i]);
            foreach (var r in rows)
                for (int i = 0; i < cols; i++)
                    if (DisplayWidth(r[i]) > widths[i]) widths[i] = DisplayWidth(r[i]);

            sb.Append('|');
            for (int i = 0; i < cols; i++) sb.Append(' ').Append(PadDisplay(headers[i], widths[i])).Append(" |");
            sb.AppendLine();
            sb.Append('|');
            for (int i = 0; i < cols; i++) sb.Append(':').Append(new string('-', widths[i] + 1)).Append('|'); // 全列左揃え
            sb.AppendLine();
            foreach (var r in rows)
            {
                sb.Append('|');
                for (int i = 0; i < cols; i++) sb.Append(' ').Append(PadDisplay(r[i], widths[i])).Append(" |");
                sb.AppendLine();
            }
        }

        /// <summary>準パワー降順の**通し表**を sb に追記 (2026-08-17b)。
        ///
        /// <para>旧版は S/A/B/C/D/E の 6 セクションに分けて書いていた。 廃止した理由は
        /// クラス冒頭の設計コメントのとおり ── 枠が固定だと強い品が増えたぶんだけ
        /// 既存品が降格するので、 序列そのものを 1 本の表で見せる形へ変えた。
        /// 帯 (絶対取得/強推/…) は**しきい値の位置を目で追うための目印**で、定員は無い。</para></summary>
        private static void WriteRankedTable(StringBuilder sb, List<ItemLearningStats.ItemAggregate> pool)
        {
            sb.AppendLine($"## 準パワー順 (全 {pool.Count} 品)");
            sb.AppendLine();
            if (pool.Count == 0) { sb.AppendLine("*(該当なし)*"); sb.AppendLine(); return; }
            var rows = new List<string[]>();
            for (int i = 0; i < pool.Count; i++) rows.Add(BuildRow(pool[i], i + 1, true));
            WritePaddedTable(sb, UnifiedHeaders, rows);
            sb.AppendLine();
        }

        /// <summary>
        /// 人間向け Tier リスト Markdown を BALANCE_TIER_LIST.md に上書き出力。
        /// `pool` は除外/サンプル不足を弾いた lift6F 降順のリスト。
        /// </summary>
        private static void WriteTierListMarkdown(ItemLearningStats.StatsFile sf, List<ItemLearningStats.ItemAggregate> pool,
                                                  List<ItemLearningStats.ItemAggregate> weaponPool, string learningRoot)
        {
            try
            {
                string leaf = new DirectoryInfo(Path.GetFullPath(learningRoot)).Name;
                string suffix = leaf.StartsWith("tier_score_", StringComparison.Ordinal)
                    ? $"{MetaProfileHelper.CurrentSuffix}_score{leaf.Substring("tier_score_".Length)}"
                    : MetaProfileHelper.CurrentSuffix;
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                    $"BALANCE_TIER_LIST_{suffix}.md"));
                var sb = new StringBuilder();
                sb.AppendLine("# 準パワー順 アイテムリスト (L1学習による自動生成)");
                sb.AppendLine();
                sb.AppendLine("> 編集禁止: バッチ実行ごとに自動更新される。 手書き手動指定は [PriorityItemList.cs](Assets/Scripts/AutoTest/PriorityItemList.cs) を編集 (フォールバック時のみ使用)。");
                sb.AppendLine($"> 最終更新: **{sf.updatedAt}** / 累積バッチ: **{sf.totalBatches}** / 累積ラン: **{sf.totalRuns}**");
                sb.AppendLine($"> モード: **{(_useFallback ? "手書きフォールバック" : "動的学習採用")}** / 採用基準: `acq6F ≥ {MinAcq6F}`");
                sb.AppendLine();
                sb.AppendLine("> **S/A/B/C/D/E のパーセンタイル枠は 2026-08-17 に廃止**。 枠が固定だと、 強い品がプールに増えたぶんだけ");
                sb.AppendLine("> 既存品が**定義上**降格する。 出目パーツ 36 種を学習へ載せた時点でこれが致命的になる");
                sb.AppendLine("> (パーツが上位を占めれば同じだけパッシブが降格し、 BOT がパッシブを買わなくなる。");
                sb.AppendLine("> 実測: パッシブ購入 7.30→9.37 の増加がそのまま 7層到達 57.8%→68.3% を作っている)。");
                sb.AppendLine("> 代わりに**準パワーの降順に 1 本で並べ、 予算が許す限り上から買う**。 下の「帯」は");
                sb.AppendLine($"> しきい値の位置を目で追うための目印で **定員は無い**: 絶対取得 `≥{PowerCuts[0]:F2}` / 強推 `≥{PowerCuts[1]:F2}` / 余裕時 `≥{PowerCuts[2]:F2}` / 中立 `≥{PowerCuts[3]:F2}` / 罠 それ未満。");
                sb.AppendLine($"> 現プールの分位: `{DescribePercentiles()}`");
                sb.AppendLine();
                sb.AppendLine($"> **準パワー = band スコアの実測押し上げ量**（2026-09-05 に z-score から移行）。");
                sb.AppendLine($"> `score = regβ + 0.15×lift6F + 0.10×offL6F(|>1|=0)`。 <b>3 項とも band スコアの差分</b>なので単位が揃う。");
                sb.AppendLine($"> 主軸の regβ は「この品を持つとランの band が +N 上がる」という**絶対量**で、");
                sb.AppendLine($"> カタログに何が入っていようが値の意味が変わらない。");
                sb.AppendLine($">");
                sb.AppendLine($"> **なぜ z-score をやめたか。** z の 0 はプール平均なので、 しきい値は「絶対」を名乗りながら");
                sb.AppendLine($"> 実際には**相対**だった。 帰結は 2 つ ── **1 品を強化すると平均が上がって別の品が定義上 降格する**、");
                sb.AppendLine($"> **弱い品を削除すると平均が上がって罠帯の割合が増える**（実測シミュレーション: 182品 罠 2% → 130品 罠 10%）。");
                sb.AppendLine($"> これは 2026-08-17 に S/A/B のパーセンタイル枠を廃止した理由「枠が固定だと強い品が増えたぶん既存品が");
                sb.AppendLine($"> **定義上**降格する」と**同じ病**で、 相対性が枠から指標の中へ移動しただけだった。");
                sb.AppendLine($">");
                sb.AppendLine($"> 移行後は **「罠」= 寄与が実質ゼロ以下** という絶対的な意味を持つ。 **帯の定員は保証しない** ──");
                sb.AppendLine($"> 全品が優秀なら全員が上位帯でよい。 人数を絞るのは購入判断（金と枠）の仕事であって、 評価軸の仕事ではない。");
                sb.AppendLine($">");
                sb.AppendLine($"> **補助 2 成分は交絡を含む**ので重みは小さく、 実質タイブレークに留めてある");
                sb.AppendLine($"> （regβ の SD 0.133 に対し 0.15×lift6F の寄与は ±0.02 程度）。");
                sb.AppendLine($"> lift5F は 2026-09-04 に廃止（因果基準との順位相関 +0.119 ＝ ほぼ無情報）。");
                sb.AppendLine($"> formΔ は「撃破段数」で単位が違うためスコアから外し、 表示のみに回した。");
                sb.AppendLine($"> 未収束/低サンプル品は絶対値を推定できないので `Unlearned` を返す ──");
                sb.AppendLine($"> **0 を返してはいけない**（買われない → 統計が溜まらない → 永久に 0、 で詰む）。");
                sb.AppendLine($"> BOT 挙動: **絶対取得帯 = リロールしてでも入手**、 余裕時帯以上 = 予算が許せば購入、 それ未満 = 取得優先度なし。");
                sb.AppendLine($"> 購入は準パワーの降順 → 同値なら ΔPower/G (コスパ)。 Power 帯が上がると足切り (`{PowerCuts[3]:F2}`→`{PowerCuts[2]:F2}`→`{PowerCuts[1]:F2}`) も上がる。");
                sb.AppendLine();
                sb.AppendLine("- **フェーズタグ**:");
                sb.AppendLine("  - **環境スケール非依存 (z-score ベース)** — debuffOff/debuffOn で lift の絶対値スケールが大きく異なるため、 プール内 z-score で相対化");
                sb.AppendLine($"  - 本MDのプール統計: lift5F μ={_phaseStats.mean5:F3} σ={_phaseStats.std5:F3} / lift6F μ={_phaseStats.mean6:F3} σ={_phaseStats.std6:F3} / lift7F μ={_phaseStats.mean7:F3} σ={_phaseStats.std7:F3} / formΔ μ={_phaseStats.meanF:F3} σ={_phaseStats.stdF:F3}");
                sb.AppendLine("  - **[OP]** = 次のいずれか (オーバーパワー候補)");
                sb.AppendLine("    - (1) z5/z6/z7 全部 ≥ 1.2σ **かつ acq6F ≥ 500** (プール上位 ~12% × 3指標)");
                sb.AppendLine("    - (2) アーリー条件 と レイト条件 を **同時に満たす** (序盤も終盤も強い実質OP)");
                sb.AppendLine($"  - **[高難度]/[低難度]** = デバフ on/off で **準パワーが激変** する品 (**OPに次ぐ優先**)。 両プロファイルの準パワーを直接比較");
                sb.AppendLine($"    - **[高難度]** = score(debuffOn) − score(debuffOff) ≥ +{DiffScoreGap:F2} / **[低難度]** = ≤ −{DiffScoreGap:F2}");
                sb.AppendLine($"    - {(_diffScore.Count > 0 ? $"算出済 (両プロファイルの準パワー突合 {_diffScore.Count}件)" : "未算出 (debuffOff/debuffOn いずれかの score_assignment.json 不足 → 本タグ無効)")}");
                sb.AppendLine("  - **[アーリー]** = z5 ≥ 0.8σ かつ z5 − z7 ≥ 0.7σ (序盤偏重)");
                sb.AppendLine("  - **[レイト]** = 次のいずれか — 終盤偏重 (lift7F or formΔ ベース、 非対称 z 閾値)");
                sb.AppendLine("    - (a) zForm ≥ 1.5σ (ヴェスカ段突破力が圧倒的 → アーリー判定より優先)");
                sb.AppendLine("    - (b) z7 ≥ 0.3σ かつ z7 − z5 ≥ 0.3σ");
                sb.AppendLine("    - (c) zForm ≥ 0.6σ かつ zForm − z5×0.5 ≥ 0.3σ");
                sb.AppendLine("  - **[ミッド]** = アーリー/レイト 非該当 かつ z6 ≥ 0.3σ (6Fバンド特化、 中盤に明確に効く)");
                sb.AppendLine("  - **[バランス]** = 上記いずれも非該当 (regβ/offL6F で底上げ、 フェーズ偏り無く広く貢献)");
                sb.AppendLine("  - 無印 = いずれも該当しない (特殊枠: regβ や offL6F だけで S/A/B 入りした品)");
                sb.AppendLine("- **lift6F**: 6F到達ラン同士で「取得した群」と「未取得群」の bandScore 平均差分");
                sb.AppendLine("- **lift5F / lift7F**: 同上の 5F到達ラン/7F到達ラン 版 (フェーズ特化評価用)。 **lift5F はスコアから外した** (因果一致 +0.119) が、 フェーズタグの判定には引き続き使う");
                sb.AppendLine("- **offL6F**: 6F到達ラン同士で「提示された群」vs「提示されなかった群」差 (出現バイアスを除去した参考値)");
                sb.AppendLine("- **regβ**: 全アイテム同時 Ridge 回帰の係数±SE (他アイテム取得を統制した上での純粋寄与)。 **全ランで fit** ── 6F限定版は表示せず、 準パワーにも入れない");
                sb.AppendLine($"  - 回帰の健全性: `{ItemRegression.LastSummary}`");
                sb.AppendLine("  - **split-half r** = 行を偶奇で 2 分割して独立に fit した β の相関 (信用品のみ)。 **0.80 を割ったら regβ の重みを上げてはいけない**");
                sb.AppendLine("- **clrΔ6F**: 6F到達ラン同士の (7F以降クリア率) 差");
                sb.AppendLine("- **formΔ**: ヴェスカ連戦の撃破段数 差");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                // PhaseTag 用プール統計 (z-score 化で環境スケール差を吸収)
                ComputePhaseStats(pool);
                // 高難度/低難度 タグ用: デバフ on/off の Lift6F z-score 差を算出
                ComputeDifficultySensitivity();

                // 準パワー降順の通し表 (Tier セクション分割は 2026-08-17b に廃止)
                WriteRankedTable(sb, pool);

                // シナジー探索（別枠）: 単体 Tier では拾えないセット相乗を可視化
                WriteSynergySection(sb, sf, learningRoot);

                // サンプル不足: 集計対象外だがアイテムは存在する (acq6F < 30)
                var lowSample = new List<ItemLearningStats.ItemAggregate>();
                foreach (var a in sf.items)
                {
                    if (a == null || string.IsNullOrEmpty(a.id)) continue;
                    if (ItemLearningStats.ExcludedFromLift.Contains(a.id)) continue;
                    if (ItemLearningStats.DeletedItems.Contains(a.id)) continue;
                    if (a.acq6FRuns >= MinAcq6F) continue;  // 集計対象は上で表示済
                    lowSample.Add(a);
                }
                lowSample.Sort((x, y) => y.acq6FRuns.CompareTo(x.acq6FRuns));
                sb.AppendLine($"## サンプル不足 ({lowSample.Count}個, acq6F < {MinAcq6F})");
                sb.AppendLine();
                sb.AppendLine("→ 評価には信頼性不足。 acq6F が増えれば自動的に上の通し表へ載る。");
                sb.AppendLine();
                if (lowSample.Count > 0)
                {
                    var lowRows = new List<string[]>();
                    foreach (var a in lowSample) lowRows.Add(BuildRow(a, 0, false));
                    WritePaddedTable(sb, UnifiedHeaders, lowRows);
                }
                sb.AppendLine();

                // 武器 T4 の別枠 (2026-09-05)。 通し表には載せない ── 理由はここに書く
                WriteWeaponTierSection(sb, weaponPool);

                // 進化チェーン武器 参考セクション (T1-T3、 Tier評価対象外だがミッド性能だけは可視化)
                WriteChainWeaponReference(sb, sf);

                // 除外リスト
                sb.AppendLine("## 集計除外リスト");
                sb.AppendLine();
                sb.AppendLine("以下はチェーン進行アイテム/初期装備のため lift 計算から除外:");
                foreach (var id in ItemLearningStats.ExcludedFromLift)
                    sb.AppendLine($"- `{DisplayName(id)}` (id: `{id}`)");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine($"*生成: `LearnedPriorityProvider.WriteTierListMarkdown` (自動)*");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[LearnedPriorityProvider] TierList markdown write fail: {e.Message}"); }
        }

        /// <summary>帯の出入りを BotJudgmentLog に追記。 2026-08-17b: 「S級/A級」は
        /// 枠ではなく**しきい値の越境**を指すようになった (定員が無いので同時に何品でも昇格しうる)。</summary>
        private static void LogIfChanged(HashSet<string> prevTop, HashSet<string> prevHigh, bool prevFallback,
            ItemLearningStats.StatsFile sf)
        {
            // フォールバック→学習 or 学習→フォールバック 切替は別扱い
            bool fallbackFlip = prevFallback != _useFallback;
            var nowTop  = new HashSet<string>(TopSet());
            var nowHigh = new HashSet<string>(HighSet());
            var addedTop = new List<string>();   foreach (var id in nowTop)   if (!prevTop.Contains(id))  addedTop.Add(id);
            var removedTop = new List<string>(); foreach (var id in prevTop)  if (!nowTop.Contains(id))   removedTop.Add(id);
            var addedHigh = new List<string>();  foreach (var id in nowHigh)  if (!prevHigh.Contains(id)) addedHigh.Add(id);
            var removedHigh = new List<string>();foreach (var id in prevHigh) if (!nowHigh.Contains(id))  removedHigh.Add(id);

            // 初回ロード (前状態空) は記録しない (起動ノイズ防止)
            // ただし学習モードへの遷移は記録
            if (prevTop.Count == 0 && prevHigh.Count == 0 && prevFallback && !_useFallback)
                fallbackFlip = true;
            else if (prevTop.Count == 0 && prevHigh.Count == 0) return;

            if (!fallbackFlip && addedTop.Count == 0 && removedTop.Count == 0
                              && addedHigh.Count == 0 && removedHigh.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"## {BotJudgmentLog.Now()} — L1 準パワー帯 変化 (累積バッチ {sf.totalBatches}, ラン {sf.totalRuns})");
            sb.AppendLine();
            if (fallbackFlip)
                sb.AppendLine(_useFallback
                    ? "- **モード**: 動的学習 → 手書きフォールバックへ後退"
                    : "- **モード**: 手書き → 動的学習へ切替");
            if (addedTop.Count    > 0) sb.AppendLine($"- **絶対取得帯へ昇格** (≥{PowerCuts[0]:F2}): {string.Join(", ", addedTop)}");
            if (removedTop.Count  > 0) sb.AppendLine($"- **絶対取得帯から降格**: {string.Join(", ", removedTop)}");
            if (addedHigh.Count   > 0) sb.AppendLine($"- **強推帯へ昇格** (≥{PowerCuts[1]:F2}): {string.Join(", ", addedHigh)}");
            if (removedHigh.Count > 0) sb.AppendLine($"- **強推帯から降格**: {string.Join(", ", removedHigh)}");
            sb.AppendLine();
            sb.AppendLine($"  (現状: 全{_ranked.Count}品 / 絶対取得 {nowTop.Count} / 強推 {nowHigh.Count} / 余裕時 {CountAtLeast(PowerCuts[2])} / 中立以上 {CountAtLeast(PowerCuts[3])})");
            sb.AppendLine();
            sb.AppendLine("---");
            BotJudgmentLog.Append(sb.ToString());
        }
    }
}
