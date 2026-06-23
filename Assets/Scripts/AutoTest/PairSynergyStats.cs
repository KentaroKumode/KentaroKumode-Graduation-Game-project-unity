using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// シナジー探索（別枠・任意ペア発掘）。 セット未登録の2アイテム間シナジーを
    /// 「提示条件付き2×2（DiD）」で測る。
    ///
    /// 方法（妥当性の核心）:
    ///   ペア(A,B)の母集団を「両方が提示された(offered∪acquired)ラン」に限定 → 出現バイアスを除去。
    ///   その中で (A取得?, B取得?) の 2×2 セルに bandScore を集計。
    ///   interaction = avg(both) − avg(Aのみ) − avg(Bのみ) + avg(両方なし)
    ///              （= 差分の差分。 加法的な主効果を相殺し、 純粋な相互作用だけを残す）
    ///   正 = 相乗（一緒に取ると単体の和を超える） / 負 = 冗長・反目。
    ///
    /// 注意: 「取得するか」は BOT のポリシー依存で完全ランダムではないため、 選択交絡は残る
    ///       （offered 条件付けで出現バイアスは除去できる）。 厳密な因果ではなく「探索＝候補抽出」用。
    ///       上位/下位ペアを人間/L2 が吟味する前段として使う。
    ///
    /// 肥大対策: 学習本体 item_stats.json とは別ファイル synergy_pairs.json に保存（累積）。
    ///   候補プールは ExcludedFromLift / DeletedItems を弾く。 共起回数で枝刈り＋上位キャップ。
    /// </summary>
    public static class PairSynergyStats
    {
        public const string FileName = "synergy_pairs.json";

        /// <summary>保存時に残す最小「両提示」回数（これ未満のペアは捨てる）。</summary>
        public const int MinKeepOffered = 50;
        /// <summary>保存するペア数の上限（両提示回数の多い順に残す）。</summary>
        public const int MaxPairs = 3000;
        /// <summary>MD で報告する各セルの最小サンプル（4セル全てこれ以上で初めて interaction を信頼）。</summary>
        public const int MinCellForReport = 15;

        // ペアキーの区切り（item id には現れない制御文字）。
        private static readonly string Sep = "";

        // bandScore は 0..12 の有界量。 二乗和(bandSq)が無い旧データ用に、 分散の保守的フォールバックσ。
        public const double SigmaFallback = 3.0;

        [Serializable]
        public class PairRec
        {
            public string a = "";
            public string b = "";
            // index: 0=両方なし / 1=Aのみ / 2=Bのみ / 3=両方
            public int[] n = new int[4];
            public double[] band = new double[4];
            public double[] bandSq = new double[4]; // 各セルの bandScore 二乗和（標準誤差用）

            public int Total => n[0] + n[1] + n[2] + n[3];
            public bool AllCells(int min) => n[0] >= min && n[1] >= min && n[2] >= min && n[3] >= min;
            /// <summary>4セルの最小サンプル数。</summary>
            public int MinCell() => System.Math.Min(System.Math.Min(n[0], n[1]), System.Math.Min(n[2], n[3]));

            public double Avg(int i) => n[i] > 0 ? band[i] / n[i] : 0;
            /// <summary>差分の差分（純粋相互作用）。</summary>
            public double Interaction() => Avg(3) - Avg(1) - Avg(2) + Avg(0);

            /// <summary>セル i の bandScore 分散（二乗和欠落時は保守的フォールバック）。</summary>
            private double VarCell(int i)
            {
                if (n[i] <= 0) return 0;
                double m = band[i] / n[i];
                if (bandSq[i] <= 0) return SigmaFallback * SigmaFallback; // 旧データ＝分散不明 → 保守的
                double v = bandSq[i] / n[i] - m * m;
                return v > 0 ? v : 0;
            }

            /// <summary>interaction（4平均の和差）の標準誤差。 SE = sqrt(Σ var_i / n_i)。</summary>
            public double SE()
            {
                double s = 0;
                for (int i = 0; i < 4; i++) if (n[i] > 0) s += VarCell(i) / n[i];
                return System.Math.Sqrt(s);
            }

            /// <summary>2σ有意か（|interaction| ≥ 2·SE）。</summary>
            public bool Significant() => System.Math.Abs(Interaction()) >= 2.0 * SE();
        }

        [Serializable]
        public class PairFile
        {
            public string updatedAt = "";
            public int totalRuns;                       // 累積で投入した有効ラン数
            public List<PairRec> pairs = new List<PairRec>();
        }

        public static PairFile Load(string learningRoot)
        {
            try
            {
                string path = Path.Combine(learningRoot, FileName);
                if (File.Exists(path))
                {
                    var pf = JsonUtility.FromJson<PairFile>(File.ReadAllText(path, Encoding.UTF8));
                    if (pf != null)
                    {
                        if (pf.pairs == null) pf.pairs = new List<PairRec>();
                        foreach (var p in pf.pairs)
                        {
                            if (p.n == null || p.n.Length < 4) p.n = new int[4];
                            if (p.band == null || p.band.Length < 4) p.band = new double[4];
                        }
                        return pf;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[PairSynergyStats] load fail: {e.Message}"); }
            return new PairFile();
        }

        /// <summary>1バッチ分の RunRec[] からペア共起を累積し、 synergy_pairs.json を更新。</summary>
        public static void Ingest(string learningRoot, IList<AutoRunner.RunRec> recs)
        {
            if (recs == null || string.IsNullOrEmpty(learningRoot)) return;
            try
            {
                var pf = Load(learningRoot);
                var map = new Dictionary<string, PairRec>(pf.pairs.Count * 2 + 16);
                foreach (var p in pf.pairs)
                    if (p != null && !string.IsNullOrEmpty(p.a) && !string.IsNullOrEmpty(p.b))
                        map[p.a + Sep + p.b] = p;

                var excluded = ItemLearningStats.ExcludedFromLift;
                var deleted  = ItemLearningStats.DeletedItems;

                int validRuns = 0;
                foreach (var r in recs)
                {
                    if (r == null || r.bandScore < 0) continue; // CRASH/DEADLOCK 除外
                    validRuns++;
                    var acquired = r.acquiredItemsEver ?? new HashSet<string>();
                    var offered  = r.offeredItemsEver  ?? new HashSet<string>();

                    // in-play = (offered ∪ acquired) かつ 評価対象（除外品/削除品を弾く）
                    var inPlay = new List<string>();
                    var seen = new HashSet<string>();
                    void AddIf(string id)
                    {
                        if (string.IsNullOrEmpty(id) || seen.Contains(id)) return;
                        if (excluded.Contains(id) || deleted.Contains(id)) return;
                        seen.Add(id); inPlay.Add(id);
                    }
                    foreach (var id in offered)  AddIf(id);
                    foreach (var id in acquired) AddIf(id);
                    inPlay.Sort(StringComparer.Ordinal); // a<b 正準順

                    for (int i = 0; i < inPlay.Count; i++)
                    {
                        string a = inPlay[i];
                        bool hasA = acquired.Contains(a);
                        for (int j = i + 1; j < inPlay.Count; j++)
                        {
                            string b = inPlay[j];
                            string key = a + Sep + b;
                            if (!map.TryGetValue(key, out var pr))
                            {
                                pr = new PairRec { a = a, b = b };
                                map[key] = pr;
                            }
                            int idx = (hasA ? 1 : 0) + (acquired.Contains(b) ? 2 : 0);
                            pr.n[idx]++;
                            pr.band[idx] += r.bandScore;
                        }
                    }
                }
                pf.totalRuns += validRuns;

                // 枝刈り（両提示回数が少なすぎるペアを捨てる）＋ 上位キャップ
                var all = new List<PairRec>(map.Values);
                all.RemoveAll(p => p.Total < MinKeepOffered);
                all.Sort((x, y) => y.Total.CompareTo(x.Total));
                if (all.Count > MaxPairs) all = all.GetRange(0, MaxPairs);
                pf.pairs = all;
                pf.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                Directory.CreateDirectory(learningRoot);
                File.WriteAllText(Path.Combine(learningRoot, FileName),
                    JsonUtility.ToJson(pf, false), new UTF8Encoding(false));
                Debug.Log($"[PairSynergyStats] ペア更新: {pf.pairs.Count}組保持 (累積{pf.totalRuns}ラン, +{validRuns})");
            }
            catch (Exception e) { Debug.LogWarning($"[PairSynergyStats] ingest fail: {e.Message}"); }
        }
    }
}
