using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// L1.5: イベント選択肢の勝率学習。
    ///
    /// キー: "eventId|choiceIndex"
    /// 値: そのキーを選んだランの bandScore 平均 / 試行回数
    /// 同 eventId 内で choiceIndex 間の差分を見れば「どの選択肢が勝つラン傾向に寄与するか」が出る。
    ///
    /// EventChoiceScorer は AutoRunner と同じ判定経路を通るため、 学習が偏ると Self-fulfilling になり得る。
    /// したがって 「最終得点 = ベース得点 + 学習補正α × 観測リフト」 のように **加算ボーナスとして** 使う。
    /// </summary>
    public static class EventChoiceLearningStats
    {
        public const int MinTrials = 30;     // この試行数未満は集計から除外

        [Serializable]
        public class ChoiceAggregate
        {
            public string key;               // "eventId|index"
            public int trials;
            public double bandScoreSum;
            public int fullClearCount;       // R11+R12
            public double BandAvg => trials > 0 ? bandScoreSum / trials : 0;
            public double FullClearRate => trials > 0 ? (double)fullClearCount / trials : 0;
        }

        [Serializable]
        public class StatsFile
        {
            public string updatedAt = "";
            public int totalBatches;
            public int totalRuns;
            public List<ChoiceAggregate> entries = new List<ChoiceAggregate>();
            public List<BatchSnap> batches = new List<BatchSnap>();
        }

        [Serializable]
        public class BatchSnap
        {
            public int runs;
            public List<ChoiceAggregate> entries = new List<ChoiceAggregate>();
        }

        public const int MaxBatchesRetained = 50;

        // ===== バッチ集計 =====
        public static StatsFile IngestBatch(string learningRoot, IList<AutoRunner.RunRec> recs)
        {
            string path = Path.Combine(learningRoot, "event_stats.json");
            var sf = LoadOrNew(path);
            if (recs == null) return sf;

            // 同等のサイズゲートを適用 (50ラン未満で更新しない)
            int valid = 0;
            foreach (var r in recs) if (r != null && r.bandScore >= 0) valid++;
            if (valid < ItemLearningStats.MinBatchForL1)
            {
                Debug.Log($"[EventChoiceLearningStats] サイズゲート: 有効{valid}ラン < {ItemLearningStats.MinBatchForL1} → スキップ");
                return sf;
            }

            // 新バッチ分のみの ChoiceAggregate を作成
            var byKey = new Dictionary<string, ChoiceAggregate>();

            foreach (var r in recs)
            {
                if (r == null || r.bandScore < 0) continue;
                if (r.eventChoicesMade == null) continue;
                bool fullClear = r.bandScore >= 11;
                // 同一ランで同じキーが複数回出ても1回として扱う (重複ラン重みを避ける)
                var seen = new HashSet<string>(r.eventChoicesMade);
                foreach (var k in seen)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    if (!byKey.TryGetValue(k, out var a))
                    {
                        a = new ChoiceAggregate { key = k };
                        byKey[k] = a;
                    }
                    a.trials++;
                    a.bandScoreSum += r.bandScore;
                    if (fullClear) a.fullClearCount++;
                }
            }

            // バッチスナップショット追加 + 上限超過分削除
            var snap = new BatchSnap { runs = recs.Count, entries = new List<ChoiceAggregate>(byKey.Values) };
            if (sf.batches == null) sf.batches = new List<BatchSnap>();
            sf.batches.Add(snap);
            while (sf.batches.Count > MaxBatchesRetained) sf.batches.RemoveAt(0);

            // entries (表示用累積) を batches から再構築
            var merged = new Dictionary<string, ChoiceAggregate>();
            foreach (var b in sf.batches)
                foreach (var a in b.entries)
                {
                    if (!merged.TryGetValue(a.key, out var dst))
                    {
                        dst = new ChoiceAggregate { key = a.key };
                        merged[a.key] = dst;
                    }
                    dst.trials += a.trials;
                    dst.bandScoreSum += a.bandScoreSum;
                    dst.fullClearCount += a.fullClearCount;
                }
            sf.entries = new List<ChoiceAggregate>(merged.Values);
            sf.totalBatches = sf.batches.Count;
            int rt = 0; foreach (var b in sf.batches) rt += b.runs;
            sf.totalRuns = rt;
            sf.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Directory.CreateDirectory(learningRoot);
            File.WriteAllText(path, JsonUtility.ToJson(sf, true), new UTF8Encoding(false));
            return sf;
        }

        public static StatsFile LoadOrNew(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var txt = File.ReadAllText(path, Encoding.UTF8);
                    var sf = JsonUtility.FromJson<StatsFile>(txt);
                    if (sf != null) return sf;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[EventChoiceLearningStats] load fail: {e.Message}"); }
            return new StatsFile();
        }

        // ===== ランタイム参照 (EventChoiceScorer 用) =====
        // キー: "eventId|index" → 観測 lift (= その選択肢の bandAvg − その event 全選択肢の bandAvg)
        private static Dictionary<string, float> _liftMap;
        private static bool _loaded;

        public static void Reload(string learningRoot = null)
        {
            _liftMap = new Dictionary<string, float>();
            _loaded = true;
            try
            {
                if (string.IsNullOrEmpty(learningRoot))
                    learningRoot = MetaProfileHelper.LearningRoot();
                string path = Path.GetFullPath(Path.Combine(learningRoot, "event_stats.json"));
                if (!File.Exists(path)) return;
                var sf = LoadOrNew(path);
                if (sf == null || sf.entries == null) return;

                // event_id 単位でグルーピングしてリフトを計算
                var byEvent = new Dictionary<string, List<ChoiceAggregate>>();
                foreach (var a in sf.entries)
                {
                    if (a == null || string.IsNullOrEmpty(a.key)) continue;
                    int bar = a.key.IndexOf('|');
                    if (bar <= 0) continue;
                    string eid = a.key.Substring(0, bar);
                    if (!byEvent.TryGetValue(eid, out var list))
                    {
                        list = new List<ChoiceAggregate>();
                        byEvent[eid] = list;
                    }
                    list.Add(a);
                }

                foreach (var kv in byEvent)
                {
                    var list = kv.Value;
                    if (list.Count < 2) continue; // 比較不能

                    double sum = 0; int n = 0;
                    foreach (var a in list)
                    {
                        if (a.trials < MinTrials) continue;
                        sum += a.bandScoreSum; n += a.trials;
                    }
                    if (n <= 0) continue;
                    double mean = sum / n;
                    foreach (var a in list)
                    {
                        if (a.trials < MinTrials) continue;
                        _liftMap[a.key] = (float)(a.BandAvg - mean);
                    }
                }
                Debug.Log($"[EventChoiceLearningStats] 学習リフト構築: {_liftMap.Count}件 (累積バッチ {sf.totalBatches})");
            }
            catch (Exception e) { Debug.LogWarning($"[EventChoiceLearningStats] Reload fail: {e.Message}"); }
        }

        /// <summary>学習されたリフト(=この選択肢の bandScore差分)。 未学習なら0。</summary>
        public static float GetLearnedLift(string eventId, int choiceIndex)
        {
            if (!_loaded) Reload();
            if (_liftMap == null) return 0f;
            string key = (eventId ?? "") + "|" + choiceIndex;
            return _liftMap.TryGetValue(key, out var lift) ? lift : 0f;
        }
    }
}
