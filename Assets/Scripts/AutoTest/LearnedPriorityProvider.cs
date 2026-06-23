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
        // ===== 設定 (6段階 Tier: S=絶対取得 / A/B/C/D/E=統計表示のみ) =====
        /// <summary>採用判定の最低 acq6F (これ未満はノイズ扱い)。</summary>
        public const int MinAcq6F = 30;
        // 2026-05-31: 絶対閾値方式 → パーセンタイル方式に変更 (S:A:B:C:D:E ≒ 1:2:3:2:1:1)
        //   従来 (絶対閾値 lift6F >= 0.10 等) は環境ごとの分布形に合わず C 級が肥大化していた。
        //   Score 降順でソート済み pool に対し、 各Tierへ比率で配分。 残りが E。
        //   合計が 1.0 を超えない範囲で調整可能。 S>=Aを下回らないよう注意。
        public const double TierRatioS = 0.05;
        public const double TierRatioA = 0.10;
        public const double TierRatioB = 0.30;
        public const double TierRatioC = 0.25;
        public const double TierRatioD = 0.15;
        // E = 残り (約15%)
        /// <summary>データ不足判定: 動的S候補がこの数未満なら手書きフォールバック。
        /// 2026-05-31: 0 = 判定実質撤廃 (50バッチ50000ランの動的データを信頼)。
        /// item_stats.json 自体が無い/空の場合のみフォールバックする。</summary>
        public const int MinDynamicItemsToTrust = 0;

        // ===== 状態 =====
        private static bool _loaded;
        /// <summary>2026-06-23 案 A: アイテム別の連続値 lift スコア (Tier バケットより細粒度)。
        /// ItemLearningStats から算出した raw score × confidence (sqrt) をそのまま保持。
        /// 同 Tier 内序列の判定や同家系 Lv の細粒度比較に用いる。</summary>
        private static Dictionary<string, double> _itemContinuousScore = new Dictionary<string, double>();
        private static HashSet<string> _dynS = new HashSet<string>();
        private static HashSet<string> _dynA = new HashSet<string>();
        private static HashSet<string> _dynB = new HashSet<string>();
        private static HashSet<string> _dynC = new HashSet<string>();  // 0 <= lift < 0.02
        private static HashSet<string> _dynD = new HashSet<string>();  // -0.10 <= lift < 0
        private static HashSet<string> _dynE = new HashSet<string>();  // lift < -0.10 (致命罠)
        private static bool _useFallback = true;  // true なら手書きのみ
        private static string _lastLoadedSummary = "(未読み込み)";

        // ===== 公開 API =====
        public static string LastLoadedSummary => _lastLoadedSummary;
        public static bool UsingFallback => _useFallback;

        /// <summary>バッチ起動時に1回呼ぶ。 item_stats.json をロードして dynS/dynA を構築。</summary>
        public static void EnsureLoaded(string learningRoot = null)
        {
            if (_loaded) return;
            _loaded = true;
            Reload(learningRoot);
        }

        /// <summary>強制再読み込み。 リスト変化を検出して BotJudgmentLog に追記。
        /// <paramref name="writeMarkdown"/>=false なら BALANCE_TIER_LIST.md を書かない
        /// (AIルーチン学習モードで Tier表を凍結したまま BOT用の S/A/B だけ更新する用途)。</summary>
        public static void Reload(string learningRoot = null, bool writeMarkdown = true)
        {
            // 旧スナップショット (差分検出用)
            var prevS = new HashSet<string>(_dynS);
            var prevA = new HashSet<string>(_dynA);
            bool prevFallback = _useFallback;

            _dynS.Clear();
            _dynA.Clear();
            _dynB.Clear();
            _dynC.Clear();
            _dynD.Clear();
            _dynE.Clear();
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
                var sf = ItemLearningStats.LoadOrNew(path);
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

                // 除外リスト適用 + minAcq6F フィルタ → スコア用プール確定。
                // (Score の z-score 正規化に使う μ/σ はこの確定プールで 1 回だけ算出する)
                var pool = new List<ItemLearningStats.ItemAggregate>();
                foreach (var a in sf.items)
                {
                    if (a == null || string.IsNullOrEmpty(a.id)) continue;
                    if (ItemLearningStats.ExcludedFromLift.Contains(a.id)) continue;
                    if (ItemLearningStats.DeletedItems.Contains(a.id)) continue; // 削除済は表示・分類対象外
                    if (a.acq6FRuns < MinAcq6F) continue;
                    pool.Add(a);
                }

                // ============================================================
                //  総合 Score (2026-06-03 Option B: z-score 正規化)
                //  従来は生値に重みを掛けていたため、 σ が桁違いに大きい lift5F が
                //  名目重み 0.10 でもランキングを支配し、 regβ(0.30) の実効influenceが埋没していた。
                //  各指標をプールσで正規化し、 「重み = ランキング分散の取り分」を成立させる。
                //   regβ_sig 30% / lift6F 25% / lift5F 10% / lift7F 10% / offL6F 15% / formΔ 10%
                //   ※ clrΔ6F は debuffOn 環境では機能しないため削除。
                //   regβ 未収束時は β 重みを lift6F に転送 (従来踏襲)。
                //   信頼度: sqrt(min(1, acq6F/500)) — 中サンプル品 (300-500) を救う。
                //   一致ボーナス: 主要4指標が全部「0基準の同符号」(=助ける/害する一致) で ±25% (符号は生値で判定)。
                // ============================================================

                // regβ: 収束品のみ 2σ 有意ゲート (|b|≥2se で b、 それ以外 0)。 未収束は converged=false。
                double RegGated(ItemLearningStats.ItemAggregate a, out bool converged)
                {
                    if (ItemRegression.TryGetCoef(a.id, out double b, out double se))
                    { converged = true; return Math.Abs(b) >= 2 * se ? b : 0; }
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

                double Score(ItemLearningStats.ItemAggregate a)
                {
                    double regWeight   = 0.30;
                    double lift6Weight = 0.25;
                    double lift5Weight = 0.10;
                    double lift7Weight = 0.10;

                    double reg = RegGated(a, out bool converged);
                    double zReg;
                    if (converged) zReg = Z(reg, muReg, sdReg);
                    else { zReg = 0; lift6Weight += regWeight; regWeight = 0; } // 未収束 → β 重みを lift6F へ

                    double rawScore = regWeight   * zReg
                                    + lift6Weight * Z(a.Lift6F, mu6, sd6)
                                    + lift5Weight * Z(a.Lift5F, mu5, sd5)
                                    + lift7Weight * Z(a.Lift7F, mu7, sd7)
                                    + 0.15        * Z(CapOff(a), muOff, sdOff)
                                    + 0.10        * Z(a.FormsLift6F, muForm, sdForm);

                    // 全指標一致ボーナス: 主要4指標が全部「0より上/下」(=助ける/害する) で揃えば ±25%。
                    // z だと「平均比」になり意味が変わるため、 符号は生値で判定する。
                    if (Math.Abs(rawScore) > 0.05)
                    {
                        double[] signals = {
                            (converged && reg != 0) ? reg : a.Lift6F,
                            a.Lift5F, a.Lift6F, a.Lift7F
                        };
                        bool allPos = true, allNeg = true;
                        foreach (var s in signals) { if (s <= 0) allPos = false; if (s >= 0) allNeg = false; }
                        if (allPos || allNeg) rawScore *= 1.25;
                    }

                    // sqrt 信頼度: サンプル少品を線形より緩く減衰
                    double confidence = Math.Sqrt(Math.Min(1.0, a.acq6FRuns / 500.0));
                    return rawScore * confidence;
                }

                // 連続値 score をキャッシュ (RawScore より細粒度の判定に使う)
                _itemContinuousScore.Clear();
                foreach (var a in pool) _itemContinuousScore[a.id] = Score(a);
                pool.Sort((x, y) => Score(y).CompareTo(Score(x)));

                // パーセンタイル配分 (1:2:3:2:1:1 ≒ 10:20:30:20:10:10%)
                int n = pool.Count;
                int sCap = System.Math.Max(1, (int)System.Math.Round(n * TierRatioS));
                int aCap = System.Math.Max(1, (int)System.Math.Round(n * TierRatioA));
                int bCap = System.Math.Max(1, (int)System.Math.Round(n * TierRatioB));
                int cCap = System.Math.Max(1, (int)System.Math.Round(n * TierRatioC));
                int dCap = System.Math.Max(1, (int)System.Math.Round(n * TierRatioD));
                int idx = 0;
                foreach (var a in pool)
                {
                    if (idx < sCap)                                        _dynS.Add(a.id);
                    else if (idx < sCap + aCap)                            _dynA.Add(a.id);
                    else if (idx < sCap + aCap + bCap)                     _dynB.Add(a.id);
                    else if (idx < sCap + aCap + bCap + cCap)              _dynC.Add(a.id);
                    else if (idx < sCap + aCap + bCap + cCap + dCap)       _dynD.Add(a.id);
                    else                                                   _dynE.Add(a.id);
                    idx++;
                }

                // データ不足判定
                if (_dynS.Count < MinDynamicItemsToTrust)
                {
                    _lastLoadedSummary = $"動的S候補 {_dynS.Count}個 < {MinDynamicItemsToTrust} → 手書きフォールバック";
                    _dynS.Clear(); _dynA.Clear(); _dynB.Clear(); _dynC.Clear(); _dynD.Clear(); _dynE.Clear();
                    // フォールバックでも MD は最新の集計で書き出す (Tier欄は空表示)。 writeMarkdown=false なら凍結。
                    if (writeMarkdown)
                        try { WriteTierListMarkdown(sf, pool, learningRoot); } catch (Exception ee)
                        { Debug.LogWarning($"[LearnedPriorityProvider] fallback MD write fail: {ee.Message}"); }
                    return;
                }

                _useFallback = false;
                // 現プロファイルの Tier 配置を保存 (高難度/低難度 タグの単純Tier比較に他プロファイルが参照)
                SaveTierAssignment(learningRoot);
                _lastLoadedSummary = $"動的学習 採用: S={_dynS.Count} A={_dynA.Count} (累積バッチ {sf.totalBatches}, ラン {sf.totalRuns})";
                Debug.Log($"[LearnedPriorityProvider] {_lastLoadedSummary}");
                if (_dynS.Count > 0)
                {
                    var sList = new List<string>(_dynS);
                    Debug.Log($"[LearnedPriorityProvider] 動的S: {string.Join(", ", sList)}");
                }

                // 差分検出 → BotJudgmentLog に追記
                LogIfChanged(prevS, prevA, prevFallback, sf);
                // 人間向け Tier リスト Markdown を出力 (変更有無に関わらず最新で上書き)。 writeMarkdown=false なら凍結。
                if (writeMarkdown)
                    WriteTierListMarkdown(sf, pool, learningRoot);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LearnedPriorityProvider] 読み込み失敗: {e.Message} → 手書きフォールバック");
                _useFallback = true;
                _lastLoadedSummary = $"例外 → 手書きフォールバック: {e.Message}";
            }
        }

        public static bool IsSRank(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            // 剣の舞ピース文脈ブースト: 既に 2 枚以上所持していればピース全てを S 扱い (BD 集約を優先誘導)
            if (IsDanceBoostedToS(id)) return true;
            EnsureLoaded();
            if (_useFallback) return PriorityItemList.IsSRank(id);
            return _dynS.Contains(id);
        }

        /// <summary>
        /// BOT 挙動上の "A級" 判定。 ユーザー指定:「S だけ絶対取得、 A/B/C は統計のみ分別」。
        /// → BOT は A+B (lift6F >= 0.02 の中位2層) を「余裕があれば取る」 扱いにする。
        /// C 帯は IsARank 対象外 (取得優先度なし)。
        /// </summary>
        public static bool IsARank(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLoaded();
            if (_useFallback) return PriorityItemList.IsARank(id);
            return _dynA.Contains(id) || _dynB.Contains(id);
        }

        public static bool IsPriority(string id) => IsSRank(id) || IsARank(id);

        /// <summary>2026-06-23 案A: 連続値の lift ベース DecisionScore。
        /// Tier バケット (RawScore) より細粒度。 同 Tier 内の優劣 (LV3 vs LV4 等) を反映できる。
        /// 戻り値: 概ね -2.0 〜 +3.0 範囲 (z-score 加重和 × 信頼度)。 サンプル不足品は 0。
        /// 家系内 Lv 補正: PassiveSkillRegistry から family/Lv を読み、 Lv に応じて +0/0.5/1.0/1.5 加点。</summary>
        public static float LiftScore(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            EnsureLoaded();
            float baseScore = 0f;
            if (_itemContinuousScore.TryGetValue(id, out double v)) baseScore = (float)v;
            // 家系内 Lv 補正: 同 Tier 内でも上位 Lv を確実に優先
            float lvBonus = 0f;
            var data = InventorySystem.ItemDatabase.Instance?.GetItem(id);
            if (data?.passiveSkills != null)
            {
                int maxLv = 0;
                foreach (var ps in data.passiveSkills)
                {
                    if (string.IsNullOrEmpty(ps.internalName)) continue;
                    var (_, lv) = InventorySystem.PassiveSkills.PassiveSkillRegistry.GetFamilyLevel(ps.internalName);
                    if (lv > maxLv) maxLv = lv;
                }
                if (maxLv > 0) lvBonus = (maxLv - 1) * 0.5f; // LV1=0, LV2=0.5, LV3=1.0, LV4=1.5
            }
            return baseScore + lvBonus;
        }

        /// <summary>RawScore: アイテム単体の素 Tier スコア。 2026-06-22 細分化: S=4 / A=3 / B=2 / C=1 / D-E=0。
        /// InventoryPower 算定など 「所持価値」 評価向け。 重複ペナルティは含めない。
        /// 細分化により B↔A↔S 入替が ΔPower に反映される (Λ farming や upgrade trade の評価精度UP)。</summary>
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
            if (_dynS.Contains(id)) return 4;
            if (_dynA.Contains(id)) return 3;
            if (_dynB.Contains(id)) return 2;
            if (_dynC.Contains(id)) return 1;
            return 0; // D / E は罠枠
        }

        public static int Score(string id)
        {
            // 剣の舞ピース文脈ブースト: 3 枚以上所持なら全アイテム最優先 (集約一手前)
            if (IsDanceForcedTop(id)) return 100;
            // 2026-06-22 細分化: S=4 / A=3 / B=2 / C=1 と整合させる (購入優先度判断にも反映)
            int baseScore = RawScore(id);
            // 重複/下位互換ペナルティ: アイテムの全パッシブが既に発動中 (同名 or 下位 Lv) なら -2
            // 他がそれ以上にゴミなら「マイナスでも仕方なく買う」 ─ 0床は付けない
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

        // ===== 高難度/低難度 タグ (デバフ on/off の単純な Tier 比較) =====
        // debuffOff(低難度) と debuffOn(高難度) で各アイテムが実際に配置された Tier (S..E) を直接比較する。
        // 各プロファイルの Reload 時に tier_assignment.json を保存しておき、 それを突合:
        //   rank(debuffOn) − rank(debuffOff) ≥ +DiffTierGap → [高難度] (高難度で明確に格上)
        //                                    ≤ −DiffTierGap → [低難度] (低難度で明確に格上 = 高難度では格下げ)
        // 優先度: OP > 高難度/低難度 > その他 (アーリー/レイト/ミッド/バランス)。
        // ※ 表示Tierそのものを比較するので「低難度品が高難度S級に居座る」ような矛盾が起きない。
        public const int DiffTierGap = 3; // 何Tier以上ずれたらタグを付けるか (S=6..E=1 の段差)

        private static Dictionary<string, int> _diffTierRank = new Dictionary<string, int>();

        private static int TierRank(string tier)
        {
            switch (tier)
            {
                case "S": return 6; case "A": return 5; case "B": return 4;
                case "C": return 3; case "D": return 2; case "E": return 1;
                default:  return 0;
            }
        }

        [Serializable] private class TierEntry { public string id; public string tier; }
        [Serializable] private class TierRankFile { public List<TierEntry> items = new List<TierEntry>(); }

        /// <summary>現プロファイルの Tier 配置 (_dynS.._dynE) を tier_assignment.json に保存。
        /// 高難度/低難度 タグの「単純なTier比較」のために他プロファイルから参照される。</summary>
        private static void SaveTierAssignment(string learningRoot)
        {
            try
            {
                var f = new TierRankFile();
                void Add(HashSet<string> set, string t) { foreach (var id in set) f.items.Add(new TierEntry { id = id, tier = t }); }
                Add(_dynS, "S"); Add(_dynA, "A"); Add(_dynB, "B"); Add(_dynC, "C"); Add(_dynD, "D"); Add(_dynE, "E");
                Directory.CreateDirectory(learningRoot);
                File.WriteAllText(Path.Combine(learningRoot, "tier_assignment.json"),
                    JsonUtility.ToJson(f, true), new System.Text.UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[LearnedPriorityProvider] tier_assignment 保存失敗: {e.Message}"); }
        }

        /// <summary>指定プロファイルの tier_assignment.json を {id → rank(S=6..E=1)} で読む。</summary>
        private static Dictionary<string, int> LoadTierRanks(MetaProfile p)
        {
            var result = new Dictionary<string, int>();
            try
            {
                string path = Path.GetFullPath(Path.Combine(MetaProfileHelper.LearningRootFor(p), "tier_assignment.json"));
                if (!File.Exists(path)) return result;
                var f = JsonUtility.FromJson<TierRankFile>(File.ReadAllText(path));
                if (f?.items == null) return result;
                foreach (var e in f.items)
                    if (e != null && !string.IsNullOrEmpty(e.id)) result[e.id] = TierRank(e.tier);
            }
            catch (Exception e) { Debug.LogWarning($"[LearnedPriorityProvider] tier_assignment 読込失敗({p}): {e.Message}"); }
            return result;
        }

        /// <summary>デバフ on/off の Tier 差を全アイテムについて計算しキャッシュ。
        /// 両プロファイルの tier_assignment.json が揃ったときのみ算出 (どちらか欠けると空 = タグ無し)。</summary>
        private static void ComputeDifficultySensitivity()
        {
            _diffTierRank = new Dictionary<string, int>();
            var off = LoadTierRanks(MetaProfile.BuffOn_DebuffOff);
            var on  = LoadTierRanks(MetaProfile.BuffOn_DebuffOn);
            if (off.Count == 0 || on.Count == 0) return;
            foreach (var kv in on)
                if (off.TryGetValue(kv.Key, out int roff))
                    _diffTierRank[kv.Key] = kv.Value - roff; // >0 = 高難度で格上
        }

        /// <summary>デバフ Tier差タグ。 [高難度]/[低難度]/null。</summary>
        private static string DifficultyTag(string id)
        {
            if (string.IsNullOrEmpty(id) || !_diffTierRank.TryGetValue(id, out int d)) return null;
            if (d >= DiffTierGap)  return "[高難度]";
            if (d <= -DiffTierGap) return "[低難度]";
            return null;
        }

        /// <summary>
        /// S/A/B 級アイテムにゲームフェーズタグ付与。 環境スケール非依存 (z-score ベース)。
        ///
        ///   OP       : z5/z6/z7 全部 ≥ 1.2σ かつ acq6F ≥ 500 (プール上位 ~12% × 3指標)
        ///   高難度/低難度 : OP に次ぐ優先。 debuffOn/Off の **実Tier(S..E)差** が ±DiffTierGap 以上
        ///                  (高難度で格上=[高難度] / 低難度で格上=[低難度])。 該当すればアーリー/レイト等より優先して返す。
        ///   アーリー  : z5 ≥ 1.0 かつ (z5 − z7) ≥ 1.0 (序盤に偏重)
        ///   レイト   : 次のいずれか — 終盤偏重 (lift7F or formΔ ベース)
        ///     (a) zForm ≥ 1.5 (覚者形態突破力が圧倒的 → アーリー判定より優先)
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
        ///   覚者形態突破数差 (formΔ) は純粋に終盤性能なので二次条件として有効。
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

        // 全 Tier 共通カラムヘッダ (横軸統一で見比べやすく、 等幅フォントでも揃うようパディング)
        private static readonly string[] UnifiedHeaders =
            { "アイテム", "フェーズ", "acq6F", "lift5F", "lift6F", "lift7F", "offL6F", "regβ", "formΔ" };

        // ===== 表示名解決 =====
        // 仕様 (2026-05-31):
        //   ・末尾ローマ数字 (I/II/III/IV/V/VI/VII/VIII/IX/X) → "汎用パッシブ:<元名>"
        //   ・それ以外 → items.json から id→name 解決 (なければ id そのまま)
        // 目的: 表内で「raw ID (cons_regen_1)」と「ユニーク名 (倍音のクロック)」が混在して見づらいのを解消。
        private static Dictionary<string, string> _itemNameCache;
        private static readonly Regex _romanSuffix = new Regex(@"(?:^|[^A-Za-z])(I{1,3}|IV|VI{0,3}|IX|X)$");
        private static readonly Regex _weaponIdRx = new Regex(@"^([a-z]+)_t([1-4])$");
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
                if (!_weaponIdRx.IsMatch(a.id)) continue; // 武器階梯のみ
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
            if (string.IsNullOrEmpty(id)) return "";
            var m = _weaponIdRx.Match(id);
            if (!m.Success) return "";
            if (!_weaponCatJp.TryGetValue(m.Groups[1].Value, out var jp)) return "";
            int t;
            if (!int.TryParse(m.Groups[2].Value, out t) || t < 1 || t > 4) return "";
            return $"[{jp}{_tierRomanNum[t]}]";
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

        private static string[] BuildRow(ItemLearningStats.ItemAggregate a, bool boldLift6, bool showTag)
        {
            string lift6 = boldLift6 ? $"**{a.Lift6F:F2}**" : a.Lift6F.ToString("F2");
            string regB = ItemRegression.TryGetCoef(a.id, out double bv, out double se)
                          ? $"{bv:+0.00;-0.00}±{se:F2}" : "—";
            return new[]
            {
                "`" + DisplayName(a.id) + "`",
                showTag ? PhaseTag(a) : "",
                a.acq6FRuns.ToString(),
                a.Lift5F.ToString("F2"),
                lift6,
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

        /// <summary>Tier 1段の表セクションを sb に追記。</summary>
        private static void WriteTierSection(StringBuilder sb, string tierLabel, string description,
            HashSet<string> tierSet, List<ItemLearningStats.ItemAggregate> pool)
        {
            sb.AppendLine($"## {tierLabel}級 (動的{tierSet.Count}個) — {description}");
            sb.AppendLine();
            if (tierSet.Count == 0) { sb.AppendLine("*(該当なし)*"); sb.AppendLine(); return; }
            bool bold = tierLabel == "S";
            bool showTag = tierLabel == "S" || tierLabel == "A" || tierLabel == "B";
            var rows = new List<string[]>();
            foreach (var a in pool)
            {
                if (!tierSet.Contains(a.id)) continue;
                rows.Add(BuildRow(a, bold, showTag));
            }
            WritePaddedTable(sb, UnifiedHeaders, rows);
            sb.AppendLine();
        }

        /// <summary>
        /// 人間向け Tier リスト Markdown を BALANCE_TIER_LIST.md に上書き出力。
        /// `pool` は除外/サンプル不足を弾いた lift6F 降順のリスト。
        /// </summary>
        private static void WriteTierListMarkdown(ItemLearningStats.StatsFile sf, List<ItemLearningStats.ItemAggregate> pool, string learningRoot)
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                    $"BALANCE_TIER_LIST_{MetaProfileHelper.CurrentSuffix}.md"));
                var sb = new StringBuilder();
                sb.AppendLine("# 動的 Tier リスト (L1学習による自動生成)");
                sb.AppendLine();
                sb.AppendLine("> 編集禁止: バッチ実行ごとに自動更新される。 手書き手動指定は [PriorityItemList.cs](Assets/Scripts/AutoTest/PriorityItemList.cs) を編集 (フォールバック時のみ使用)。");
                sb.AppendLine($"> 最終更新: **{sf.updatedAt}** / 累積バッチ: **{sf.totalBatches}** / 累積ラン: **{sf.totalRuns}**");
                sb.AppendLine($"> モード: **{(_useFallback ? "手書きフォールバック" : "動的学習採用")}** / 採用基準: `acq6F ≥ {MinAcq6F}`");
                sb.AppendLine($"> Tier配分 (パーセンタイル): `S {TierRatioS:P0}` / `A {TierRatioA:P0}` / `B {TierRatioB:P0}` / `C {TierRatioC:P0}` / `D {TierRatioD:P0}` / `E 残り (約{1-TierRatioS-TierRatioA-TierRatioB-TierRatioC-TierRatioD:P0})`");
                sb.AppendLine($"> **分類軸**: 総合Score = [0.30×z(regβ_sig) + 0.25×z(lift6F) + 0.10×z(lift5F) + 0.10×z(lift7F) + 0.15×z(offL6F|>1|=0) + 0.10×z(formΔ)] × sqrt(min(1, acq6F/500)) × (全指標同符号なら ×1.25)。**各指標はプール内 z-score(=(x−μ)/σ) で正規化**してから重み付けする（2026-06-03 Option B）。これにより重みが実効influenceの取り分そのものになり、σの大きい lift5F の独走を防ぎ regβ を意図通り効かせる。clrΔ6F は debuffOn 環境で機能しないため削除。フロア別 lift で 5層/7層特化アイテムも公平評価。");
                sb.AppendLine($"> BOT 挙動: **S = 絶対取得 (リロール対象)**、 A+B = 余裕時取得、 C/D/E = 取得優先度なし (D/Eは罠分類)");
                sb.AppendLine();
                sb.AppendLine("- **フェーズタグ** (S/A/B 級のみ、 C/D/E は信頼度不足のため非表示):");
                sb.AppendLine("  - **環境スケール非依存 (z-score ベース)** — debuffOff/debuffOn で lift の絶対値スケールが大きく異なるため、 プール内 z-score で相対化");
                sb.AppendLine($"  - 本MDのプール統計: lift5F μ={_phaseStats.mean5:F3} σ={_phaseStats.std5:F3} / lift6F μ={_phaseStats.mean6:F3} σ={_phaseStats.std6:F3} / lift7F μ={_phaseStats.mean7:F3} σ={_phaseStats.std7:F3} / formΔ μ={_phaseStats.meanF:F3} σ={_phaseStats.stdF:F3}");
                sb.AppendLine("  - **[OP]** = 次のいずれか (オーバーパワー候補)");
                sb.AppendLine("    - (1) z5/z6/z7 全部 ≥ 1.2σ **かつ acq6F ≥ 500** (プール上位 ~12% × 3指標)");
                sb.AppendLine("    - (2) アーリー条件 と レイト条件 を **同時に満たす** (序盤も終盤も強い実質OP)");
                sb.AppendLine($"  - **[高難度]/[低難度]** = デバフ on/off で **配置Tierが激変** する品 (**OPに次ぐ優先**)。 両プロファイルの実Tier(S..E)を直接比較");
                sb.AppendLine($"    - **[高難度]** = rank(debuffOn) − rank(debuffOff) ≥ +{DiffTierGap} Tier (高難度で明確に格上) / **[低難度]** = ≤ −{DiffTierGap} Tier (低難度で明確に格上)");
                sb.AppendLine($"    - {(_diffTierRank.Count > 0 ? $"算出済 (両プロファイルのTier突合 {_diffTierRank.Count}件)" : "未算出 (debuffOff/debuffOn いずれかの tier_assignment.json 不足 → 本タグ無効)")}");
                sb.AppendLine("  - **[アーリー]** = z5 ≥ 0.8σ かつ z5 − z7 ≥ 0.7σ (序盤偏重)");
                sb.AppendLine("  - **[レイト]** = 次のいずれか — 終盤偏重 (lift7F or formΔ ベース、 非対称 z 閾値)");
                sb.AppendLine("    - (a) zForm ≥ 1.5σ (覚者形態突破力が圧倒的 → アーリー判定より優先)");
                sb.AppendLine("    - (b) z7 ≥ 0.3σ かつ z7 − z5 ≥ 0.3σ");
                sb.AppendLine("    - (c) zForm ≥ 0.6σ かつ zForm − z5×0.5 ≥ 0.3σ");
                sb.AppendLine("  - **[ミッド]** = アーリー/レイト 非該当 かつ z6 ≥ 0.3σ (6Fバンド特化、 中盤に明確に効く)");
                sb.AppendLine("  - **[バランス]** = 上記いずれも非該当 (regβ/offL6F で底上げ、 フェーズ偏り無く広く貢献)");
                sb.AppendLine("  - 無印 = いずれも該当しない (特殊枠: regβ や offL6F だけで S/A/B 入りした品)");
                sb.AppendLine("- **lift6F**: 6F到達ラン同士で「取得した群」と「未取得群」の bandScore 平均差分");
                sb.AppendLine("- **lift5F / lift7F**: 同上の 5F到達ラン/7F到達ラン 版 (フェーズ特化評価用)");
                sb.AppendLine("- **offL6F**: 6F到達ラン同士で「提示された群」vs「提示されなかった群」差 (出現バイアスを除去した参考値)");
                sb.AppendLine("- **regβ**: 全アイテム同時線形回帰の係数±SE (他アイテム取得を統制した上での純粋寄与)");
                sb.AppendLine("- **clrΔ6F**: 6F到達ラン同士の (7F以降クリア率) 差");
                sb.AppendLine("- **formΔ**: 覚者連戦の撃破形態数 差");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                // PhaseTag 用プール統計 (z-score 化で環境スケール差を吸収)
                ComputePhaseStats(pool);
                // 高難度/低難度 タグ用: デバフ on/off の Lift6F z-score 差を算出
                ComputeDifficultySensitivity();

                // 各Tier 表 (共通形式)
                WriteTierSection(sb, "S", "🟥 絶対取得 (BOT がリロールしてでも入手)", _dynS, pool);
                WriteTierSection(sb, "A", "🟧 強推 (統計分別・BOT は A+B を余裕時取得)", _dynA, pool);
                WriteTierSection(sb, "B", "🟨 中位 (統計分別・BOT は A+B を余裕時取得)", _dynB, pool);

                // C級 (中立 0 ≤ lift < 0.02)
                var cList = new List<ItemLearningStats.ItemAggregate>();
                var dList = new List<ItemLearningStats.ItemAggregate>();
                var eList = new List<ItemLearningStats.ItemAggregate>();
                foreach (var a in pool)
                {
                    if (_dynC.Contains(a.id)) cList.Add(a);
                    else if (_dynD.Contains(a.id)) dList.Add(a);
                    else if (_dynE.Contains(a.id)) eList.Add(a);
                }
                cList.Sort((x, y) => y.Lift6F.CompareTo(x.Lift6F));
                dList.Sort((x, y) => x.Lift6F.CompareTo(y.Lift6F)); // 罠は悪い順
                eList.Sort((x, y) => x.Lift6F.CompareTo(y.Lift6F));

                void EmitTrapList(string title, List<ItemLearningStats.ItemAggregate> list, bool boldLift)
                {
                    sb.AppendLine(title);
                    sb.AppendLine();
                    if (list.Count == 0) { sb.AppendLine("*(該当なし)*"); sb.AppendLine(); return; }
                    var rows = new List<string[]>();
                    foreach (var a in list) rows.Add(BuildRow(a, boldLift, false));
                    WritePaddedTable(sb, UnifiedHeaders, rows);
                    sb.AppendLine();
                }
                EmitTrapList($"## C級-中立 ({cList.Count}個) — 🟦 中位 (パーセンタイル {TierRatioS+TierRatioA+TierRatioB:P0} 〜 {TierRatioS+TierRatioA+TierRatioB+TierRatioC:P0})", cList, false);
                EmitTrapList($"## D級-軽罠 ({dList.Count}個) — 🟪 下位 (パーセンタイル {TierRatioS+TierRatioA+TierRatioB+TierRatioC:P0} 〜 {TierRatioS+TierRatioA+TierRatioB+TierRatioC+TierRatioD:P0})", dList, false);
                EmitTrapList($"## E級-致命罠 ({eList.Count}個) — 💀 最下位 (パーセンタイル {TierRatioS+TierRatioA+TierRatioB+TierRatioC+TierRatioD:P0} 〜 100%、 最優先で調整 or 削除候補)", eList, true);

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
                sb.AppendLine("→ 評価には信頼性不足。 acq6F が増えれば自動的に S/A/B/C へ分類される。");
                sb.AppendLine();
                if (lowSample.Count > 0)
                {
                    var lowRows = new List<string[]>();
                    foreach (var a in lowSample) lowRows.Add(BuildRow(a, false, false));
                    WritePaddedTable(sb, UnifiedHeaders, lowRows);
                }
                sb.AppendLine();

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

        private static void LogIfChanged(HashSet<string> prevS, HashSet<string> prevA, bool prevFallback,
            ItemLearningStats.StatsFile sf)
        {
            // フォールバック→学習 or 学習→フォールバック 切替は別扱い
            bool fallbackFlip = prevFallback != _useFallback;
            // S/A の集合差分
            var addedS = new List<string>(); foreach (var id in _dynS) if (!prevS.Contains(id)) addedS.Add(id);
            var removedS = new List<string>(); foreach (var id in prevS) if (!_dynS.Contains(id)) removedS.Add(id);
            var addedA = new List<string>(); foreach (var id in _dynA) if (!prevA.Contains(id)) addedA.Add(id);
            var removedA = new List<string>(); foreach (var id in prevA) if (!_dynA.Contains(id)) removedA.Add(id);

            // 初回ロード (前状態空) は記録しない (起動ノイズ防止)
            // ただし学習モードへの遷移は記録
            if (prevS.Count == 0 && prevA.Count == 0 && prevFallback && !_useFallback)
                fallbackFlip = true;
            else if (prevS.Count == 0 && prevA.Count == 0) return;

            if (!fallbackFlip && addedS.Count == 0 && removedS.Count == 0
                              && addedA.Count == 0 && removedA.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"## {BotJudgmentLog.Now()} — L1 動的Tier 変化 (累積バッチ {sf.totalBatches}, ラン {sf.totalRuns})");
            sb.AppendLine();
            if (fallbackFlip)
                sb.AppendLine(_useFallback
                    ? "- **モード**: 動的学習 → 手書きフォールバックへ後退"
                    : "- **モード**: 手書き → 動的学習へ切替");
            if (addedS.Count   > 0) sb.AppendLine($"- **S級 昇格** (BOT絶対取得対象): {string.Join(", ", addedS)}");
            if (removedS.Count > 0) sb.AppendLine($"- **S級 降格**: {string.Join(", ", removedS)}");
            if (addedA.Count   > 0) sb.AppendLine($"- **A級 昇格**: {string.Join(", ", addedA)}");
            if (removedA.Count > 0) sb.AppendLine($"- **A級 降格**: {string.Join(", ", removedA)}");
            sb.AppendLine();
            sb.AppendLine($"  (現状: S {_dynS.Count}個 / A {_dynA.Count}個 / B {_dynB.Count}個 / C {_dynC.Count}個 / D {_dynD.Count}個 / E {_dynE.Count}個)");
            sb.AppendLine();
            sb.AppendLine("---");
            BotJudgmentLog.Append(sb.ToString());
        }
    }
}
