using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AutoTest.EditorTools
{
    /// <summary>
    /// <b>外部スイープ (Tools/nav_elite_sweep.py) の進捗を Unity のコンソールに出す</b> (2026-09-19)。
    ///
    /// <para>スイープは Editor の外 (Python + worker プロセス) で走るので、 そのままでは
    /// Unity 側に何も出ず、 進んでいるのか止まっているのか分からない。 ここで 20 秒ごとに
    /// <c>AutoRunLogs/sweep/</c> を見て、 <b>いま書き込みが続いているセッション</b>の
    /// 各チャンク <c>progress.txt</c> (completed=N) を合算して 1 行出す。</para>
    ///
    /// <para><b>追う対象は毎回選び直す。</b> 完了時に書かれる <c>SESSION</c> の有無だけで「走行中」と
    /// 判定すると、 途中で止めたスイープ (SESSION を書かない) に張り付いて 0 を出し続ける。
    /// 最後の書き込みが <see cref="LiveSec"/> 以内のセッションだけを走行中とみなす。</para>
    /// </summary>
    [InitializeOnLoad]
    public static class SweepProgressConsole
    {
        private const double IntervalSec = 20;
        private const double StallSec = 120;
        /// <summary>progress.txt / response.json の最終書き込みがこれより古いセッションは走行中とみなさない。
        /// worker の起動 (学習データ読み込み) は 20 秒程度なので余裕を持たせる。</summary>
        private const double LiveSec = 300;

        private static double _next;
        private static string _session;
        private static int _lastDone = -1;
        private static double _lastChange, _start;

        static SweepProgressConsole()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _next) return;
            _next = now + IntervalSec;
            try { Poll(now); } catch (Exception e) { Debug.LogWarning("[スイープ進捗] 読み取り失敗: " + e.Message); }
        }

        private static void Poll(double now)
        {
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AutoRunLogs", "sweep");
            if (!Directory.Exists(root)) return;

            // 追っているセッションが完了したら 1 回だけ報告して手放す
            if (_session != null && File.Exists(Path.Combine(_session, "SESSION")))
            {
                var (d, t, _) = Count(_session);
                Debug.Log($"[スイープ進捗] 完了 {Path.GetFileName(_session)}  {d:N0}/{t:N0} ラン  {(DateTime.Now - Directory.GetCreationTime(_session)).TotalSeconds:F0}s");
                _session = null;
            }

            string live = PickLive(root);
            if (live == null)
            {
                _session = null;   // 走っているものが無い (止めた・放置) なら黙る
                return;
            }
            if (live != _session)
            {
                _session = live; _start = now; _lastDone = -1; _lastChange = now;
            }

            var (done, total, arms) = Count(_session);
            if (done != _lastDone) { _lastDone = done; _lastChange = now; }
            // 経過時間はセッションフォルダの作成時刻から数える (追い始めた時刻を起点にすると、
            //   途中から拾ったときに 1 回目の速度が「完了数 / 数秒」で跳ね上がる)。
            double el = Math.Max(1, (DateTime.Now - Directory.GetCreationTime(_session)).TotalSeconds);
            double rate = done / el;
            string eta = rate > 0 ? $"{(total - done) / rate / 60:F1} 分" : "?";
            Debug.Log($"[スイープ進捗] {Path.GetFileName(_session)}  {done:N0}/{total:N0} ラン ({(double)done / Math.Max(1, total):P0})"
                    + $"  {rate:F1} ラン/秒  残り約 {eta}  | {arms}");
            if (now - _lastChange >= StallSec)
                Debug.LogWarning($"[スイープ進捗] {now - _lastChange:F0} 秒間 進捗なし ── スタックの疑い (各チャンクの worker.log を確認)");
        }

        /// <summary>未完了で、 直近 <see cref="LiveSec"/> 以内にチャンクの書き込みがあるセッションのうち最新。</summary>
        private static string PickLive(string root)
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var s in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(s, "SESSION"))) continue;
                DateTime last = DateTime.MinValue;
                foreach (var d in Directory.GetDirectories(s))
                {
                    foreach (var f in new[] { "progress.txt", "response.json", "job.json" })
                    {
                        string p = Path.Combine(d, f);
                        if (File.Exists(p))
                        {
                            var t = File.GetLastWriteTime(p);
                            if (t > last) last = t;
                        }
                    }
                }
                if ((DateTime.Now - last).TotalSeconds > LiveSec) continue;
                if (last > bestTime) { bestTime = last; best = s; }
            }
            return best;
        }

        private static readonly Regex RunsRe = new Regex("\"runs\"\\s*:\\s*(\\d+)");
        private static readonly Regex DoneRe = new Regex("completed=(\\d+)");

        /// <summary>セッション内の全チャンクを合算。 返り値: 完了ラン, 総ラン, アーム別の内訳文字列。</summary>
        private static (int done, int total, string arms) Count(string session)
        {
            int done = 0, total = 0;
            var byArm = new System.Collections.Generic.SortedDictionary<string, int[]>();
            foreach (var d in Directory.GetDirectories(session))
            {
                string job = Path.Combine(d, "job.json");
                if (!File.Exists(job)) continue;
                var m = RunsRe.Match(File.ReadAllText(job));
                if (!m.Success) continue;
                int size = int.Parse(m.Groups[1].Value);
                int c = 0;
                if (File.Exists(Path.Combine(d, "response.json"))) c = size;
                else
                {
                    string pp = Path.Combine(d, "progress.txt");
                    if (File.Exists(pp))
                    {
                        var pm = DoneRe.Match(File.ReadAllText(pp));
                        if (pm.Success) c = Math.Min(size, int.Parse(pm.Groups[1].Value));
                    }
                }
                string arm = Regex.Replace(Path.GetFileName(d), "_c\\d+$", "");
                if (!byArm.TryGetValue(arm, out var a)) byArm[arm] = a = new int[2];
                a[0] += c; a[1] += size;
                done += c; total += size;
            }
            string arms = string.Join("  ", byArm.Select(kv => $"{kv.Key} {kv.Value[0]:N0}/{kv.Value[1]:N0}"));
            return (done, total, arms);
        }
    }
}
