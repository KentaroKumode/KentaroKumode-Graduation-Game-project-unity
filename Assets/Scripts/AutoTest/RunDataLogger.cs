using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// ItemRegression (全アイテム同時OLS) のためのラン単位生データ永続化。
    /// 形式: bandScore|reachedFloor|id1,id2,...  (1行=1ラン、 行末=改行)
    /// ファイル: learningRoot/regression_runs.csv (累積追記)
    /// ローテーション: MaxRows を超えたら古い (Rows - TrimTo) 行を破棄して TrimTo 行に縮小
    ///
    /// <para><b>2026-09-04 上限引き上げ</b> (20万/10万 → 50万/40万)。 <see cref="ItemRegression"/> が
    /// 行を溜めずストリームで舐めるようになったのでメモリが行数に依存しなくなり、 上限は
    /// ディスクだけの問題になった。 実測 14.5 万行で 59MB なので 50 万行 ≒ 200MB。
    /// β の精度は N で決まる ── 25 万ラン 学習の全量を捨てずに使えるようにする。</para>
    /// </summary>
    public static class RunDataLogger
    {
        public const string FileName = "regression_runs.csv";
        public const int MaxRows = 500000;
        public const int TrimTo = 400000;

        public static void AppendBatch(string learningRoot, IList<AutoRunner.RunRec> recs)
        {
            try
            {
                if (recs == null || recs.Count == 0) return;
                Directory.CreateDirectory(learningRoot);
                string path = Path.Combine(learningRoot, FileName);
                var sb = new StringBuilder();
                foreach (var r in recs)
                {
                    if (r == null || r.bandScore < 0) continue;
                    sb.Append(r.bandScore).Append('|').Append(r.reachedFloor).Append('|');
                    if (r.acquiredItemsEver != null)
                    {
                        bool first = true;
                        foreach (var id in r.acquiredItemsEver)
                        {
                            if (string.IsNullOrEmpty(id)) continue;
                            if (!first) sb.Append(',');
                            sb.Append(id);
                            first = false;
                        }
                    }
                    sb.Append('\n');
                }
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                TryRotate(path);
            }
            catch (Exception e) { Debug.LogWarning($"[RunDataLogger] append fail: {e.Message}"); }
        }

        private static void TryRotate(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length <= MaxRows) return;
                int drop = lines.Length - TrimTo;
                var kept = new string[TrimTo];
                Array.Copy(lines, drop, kept, 0, TrimTo);
                File.WriteAllLines(path, kept, new UTF8Encoding(false));
                Debug.Log($"[RunDataLogger] rotated {lines.Length} → {TrimTo} rows");
            }
            catch { /* best effort */ }
        }

        /// <summary>回帰計算用の直近行制限。
        /// <b>2026-09-04: <see cref="ItemRegression"/> はこの経路を使わなくなった</b> ──
        /// 自前でストリームするので上限が無い。 旧実装はここで末尾 5 万行に切られ、
        /// さらに 6F 到達で絞って N=7891 ── 蓄積 14.5 万行の 5.4% しか使っていなかった。
        /// この定数と <see cref="LoadAll"/> は他の呼び出し元のために残置。</summary>
        public const int LoadRecentRows = 50000;

        /// <summary>累積データを読み込み (直近 LoadRecentRows 行のみ)。 6F未到達も含む (フィルタは呼び出し側)。</summary>
        public static List<RunRow> LoadAll(string learningRoot)
        {
            // 計装のみ (既定 OFF)。 回帰OLS の内数 ── パースが重いのか行列計算が重いのかで
            // 打ち手 (キャッシュの粒度) が変わるので分けて測る。
            long phase = AutoTest.Ultra.UltraPhaseClock.Begin();
            try { return LoadAllCore(learningRoot); }
            finally { AutoTest.Ultra.UltraPhaseClock.End("└ └ └ csv読込", phase); }
        }

        private static List<RunRow> LoadAllCore(string learningRoot)
        {
            var list = new List<RunRow>();
            string path = Path.Combine(learningRoot, FileName);
            if (!File.Exists(path)) return list;
            try
            {
                // 末尾 LoadRecentRows 行のみリングバッファ風に保持
                var ring = new string[LoadRecentRows];
                int count = 0; int head = 0;
                using (var sr = new StreamReader(path, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        ring[head] = line;
                        head = (head + 1) % LoadRecentRows;
                        if (count < LoadRecentRows) count++;
                    }
                }
                int start = count < LoadRecentRows ? 0 : head;
                for (int k = 0; k < count; k++)
                {
                    var line = ring[(start + k) % LoadRecentRows];
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    if (!int.TryParse(parts[0], out int bs)) continue;
                    if (!int.TryParse(parts[1], out int rf)) continue;
                    var ids = parts[2].Length == 0 ? Array.Empty<string>() : parts[2].Split(',');
                    list.Add(new RunRow { bandScore = bs, reachedFloor = rf, items = ids });
                }
            }
            catch (Exception e) { Debug.LogWarning($"[RunDataLogger] load fail: {e.Message}"); }
            return list;
        }

        public struct RunRow
        {
            public int bandScore;
            public int reachedFloor;
            public string[] items;
        }
    }
}
