using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 絵の読み込みと捨て方 (2026-11-15 に作り直した・作者の決定「全部を層の頭で読む。 層の頭は完全な待機時間なので、 そこで読む」)。
    ///
    /// <para><b>層の頭で読む</b>: <see cref="FxLayerPreload"/> が、 層に入った時 (ロード画面) に、 その層で使い得る絵を全部 <see cref="Request"/> する。
    /// 読み込みは止まらない: ファイルは Resources.LoadAsync で読み、 解凍と配列への写し (<see cref="FromFxb"/>) は主スレッドの外。
    /// 主スレッドでするのは、 読めたファイルの中身を受け取る所と、 出来た絵をキャッシュへ入れる所だけ (<see cref="Pump"/>・毎フレーム)。
    /// 層を出る時 (次の層・ランの終わり) に <see cref="SetLayerSet"/> が、 次の層で使わない絵を捨てる。</para>
    ///
    /// <para><b>保険</b>: 先に読めていない絵を使う瞬間 (<see cref="Load"/>) は、 その場で読む (読みかけならその完了を待つ)。 エディタと開発ビルドでは 1 行の警告。
    /// QA の口 (層を経ずに戦闘へ入る) と、 戦闘の画面を使わない時はこの道。</para>
    /// </summary>
    public sealed partial class FxSheet
    {
        private const string Dir = "Game2DFx/";
        private static readonly Dictionary<string, FxSheet> Cache = new Dictionary<string, FxSheet>();

        /// <summary>読みかけの絵 1 枚: ファイルの読み込み (主スレッドの外で進む) → 解釈 (別のスレッド) → キャッシュへ。</summary>
        private sealed class Pending
        {
            public string name; public ResourceRequest req; public System.Threading.Tasks.Task<FxSheet> task;
            public System.Diagnostics.Stopwatch wall; public double mainMs;
        }
        private static readonly List<Pending> Queue = new List<Pending>();
        /// <summary>この層で使い得る絵 (<see cref="SetLayerSet"/>)。 戦闘が終わっても捨てない。</summary>
        private static readonly HashSet<string> LayerSet = new HashSet<string>();

        /// <summary>同時に読みかけにしておく数 (解凍の作業用の配列が同時に膨らみすぎないように)。</summary>
        private const int InFlight = 4;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Cache.Clear(); Queue.Clear(); LayerSet.Clear(); QaLastLoadFrame = -1; QaLastLoadName = null; }

        /// <summary>使う瞬間に読む。 先に読めていなければここで読み (読みかけなら完了を待ち)、 エディタと開発ビルドでは 1 行の警告を出す (どの絵を何ミリ秒で)。
        /// 無ければ null (警告を 1 度だけ出す)。</summary>
        public static FxSheet Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var s)) return s;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int qi = Queue.FindIndex(p => p.name == name);
            if (qi >= 0) { var p = Queue[qi]; Queue.RemoveAt(qi); s = Finish(p, true); }
            else s = LoadNow(name);
            NoteLoad(name);
            if (s != null) WarnLate(s, sw.Elapsed.TotalMilliseconds);
            return s;
        }

        /// <summary>その場で読む (止まる)。 <see cref="Load"/> の保険と、 QA の口で戦闘の前にまとめて読む時に使う。</summary>
        private static FxSheet LoadNow(string name)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var a = Resources.Load<TextAsset>(Dir + name + ".fxb");
            if (a == null) { Debug.LogWarning("[FxSheet] Resources/" + Dir + name + ".fxb が無い"); return Cache[name] = null; }
            var bytes = a.bytes; Resources.UnloadAsset(a);
            FxSheet s;
            try { s = FromFxb(name, bytes); }
            catch (System.Exception e) { Debug.LogWarning(e.Message); return Cache[name] = null; }
            s.LoadMs = s.MainMs = (float)sw.Elapsed.TotalMilliseconds;
            return Cache[name] = s;
        }

        /// <summary>止まらずに読み始める (読んであるか読みかけなら何もしない)。 読み終わりは <see cref="Pump"/> が拾う。</summary>
        public static void Request(string name)
        {
            if (string.IsNullOrEmpty(name) || Cache.ContainsKey(name) || Queue.Exists(p => p.name == name)) return;
            Queue.Add(new Pending { name = name, wall = System.Diagnostics.Stopwatch.StartNew() });
        }

        /// <summary>まとめて止まって読む (QA の口: 層を経ずに戦闘へ入る時・止まってよい所)。</summary>
        public static void LoadAllNow(IEnumerable<string> names)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n) || Cache.ContainsKey(n)) continue;
                int qi = Queue.FindIndex(p => p.name == n);
                if (qi >= 0) { var p = Queue[qi]; Queue.RemoveAt(qi); Finish(p, true); } else LoadNow(n);
            }
        }

        /// <summary>読みかけの数 (0 = 頼まれた絵は全部読めた)。</summary>
        public static int PendingCount => Queue.Count;

        /// <summary>毎フレーム (<see cref="FxLayerPreload"/> から): 読めたファイルを解釈へ回し、 解釈が済んだ絵をキャッシュへ入れる。 主スレッドで重い事はしない。</summary>
        public static void Pump()
        {
            int started = 0;
            for (int i = 0; i < Queue.Count; i++)
            {
                var p = Queue[i];
                if (p.task != null)
                {
                    if (!p.task.IsCompleted) { started++; continue; }
                    Queue.RemoveAt(i--); Finish(p, false); continue;
                }
                if (p.req == null)
                {
                    if (started >= InFlight) continue;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    p.req = Resources.LoadAsync<TextAsset>(Dir + p.name + ".fxb");
                    p.mainMs += sw.Elapsed.TotalMilliseconds; started++; continue;
                }
                started++;
                if (!p.req.isDone) continue;
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                var a = p.req.asset as TextAsset;
                if (a == null) { Queue.RemoveAt(i--); Debug.LogWarning("[FxSheet] Resources/" + Dir + p.name + ".fxb が無い"); Cache[p.name] = null; continue; }
                var bytes = a.bytes; Resources.UnloadAsset(a);
                string nm = p.name;
#if UNITY_WEBGL && !UNITY_EDITOR
                p.task = System.Threading.Tasks.Task.FromResult(FromFxb(nm, bytes));      // WebGL はスレッドを使えない
#else
                p.task = System.Threading.Tasks.Task.Run(() => FromFxb(nm, bytes));
#endif
                p.mainMs += sw2.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>読みかけを仕上げてキャッシュへ入れる。 wait = 終わるまで待つ (保険の読み込み)。</summary>
        private static FxSheet Finish(Pending p, bool wait)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (p.task == null)
            {
                if (p.req == null) return LoadNow(p.name);
                var a = p.req.asset as TextAsset;      // 読みかけの LoadAsync は、 asset を見るとその場で読み終わるまで待つ
                if (a == null) { Debug.LogWarning("[FxSheet] Resources/" + Dir + p.name + ".fxb が無い"); return Cache[p.name] = null; }
                var bytes = a.bytes; Resources.UnloadAsset(a); string nm = p.name;
                p.task = System.Threading.Tasks.Task.FromResult(FromFxbSafe(nm, bytes));
            }
            if (wait) p.task.Wait();
            FxSheet s = null;
            if (p.task.IsFaulted) Debug.LogWarning("[FxSheet] " + p.name + " を読めない: " + p.task.Exception?.InnerException?.Message);
            else s = p.task.Result;
            if (s != null) { s.MainMs = (float)(p.mainMs + sw.Elapsed.TotalMilliseconds); s.LoadMs = (float)p.wall.Elapsed.TotalMilliseconds; }
            return Cache[p.name] = s;
        }

        private static FxSheet FromFxbSafe(string name, byte[] bytes)
        {
            try { return FromFxb(name, bytes); } catch (System.Exception e) { Debug.LogWarning(e.Message); return null; }
        }

        /// <summary>もう読んであるか。</summary>
        public static bool IsLoaded(string name) => !string.IsNullOrEmpty(name) && Cache.ContainsKey(name);

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void WarnLate(FxSheet s, double ms)
        {
            Debug.LogWarning($"[FxSheet] 先に読めていない絵を使う瞬間に読んだ: {s.Name} ({ms:0.0} ms)");
        }

        // ── 確認用: 使う瞬間に読んだ (止まった) 絵 ──
        /// <summary>使う瞬間に読んだ最後の絵と、 そのフレーム (Time.frameCount)。 戦闘中の最大のフレーム時間を、 どの絵の読み込みで出たかに結び付ける (Game2DBattleFx)。</summary>
        public static int QaLastLoadFrame = -1;
        public static string QaLastLoadName;
        private static void NoteLoad(string name) { QaLastLoadFrame = Time.frameCount; QaLastLoadName = name; }

        // ── 捨てる基準 (1 か所) ──

        /// <summary>戦闘も層もまたいで残す絵 (小さくて毎戦闘使う物): 武器・会心・毒・出血。</summary>
        public static bool KeepAcrossBattles(string name)
            => name.StartsWith("weapon-") || name == "crit-hit" || name == "poison-tick" || name == "bleed-tick";

        /// <summary>層に入った時: この層で使い得る絵を names にし、 読み始める。 前の層の絵のうち、 names に無く <see cref="KeepAcrossBattles"/> でも無い物を捨てる
        /// (読みかけの物も捨てる)。 空の一覧 = 層を出た (ランの終わり)。</summary>
        public static void SetLayerSet(IEnumerable<string> names)
        {
            LayerSet.Clear();
            foreach (var n in names) if (!string.IsNullOrEmpty(n)) LayerSet.Add(n);
            var drop = new List<string>();
            foreach (var k in Cache.Keys) if (!LayerSet.Contains(k) && !KeepAcrossBattles(k)) drop.Add(k);
            foreach (var k in drop) Cache.Remove(k);
            Queue.RemoveAll(p => !LayerSet.Contains(p.name) && !KeepAcrossBattles(p.name));      // 解釈中の物は、 終わっても入れない (持っている参照が無くなれば GC が回収する)
            foreach (var n in LayerSet) Request(n);
        }

        /// <summary>この層の一覧に 1 枚足す (層の途中で武器の家系が替わった時の武器の絵)。</summary>
        public static void AddToLayer(string name) { if (string.IsNullOrEmpty(name)) return; LayerSet.Add(name); Request(name); }

        /// <summary>戦闘が終わった時: 層の一覧にも <see cref="KeepAcrossBattles"/> にも無い絵 (保険の読み込み・QA の口で読んだ物) を捨てる。</summary>
        public static void UnloadAfterBattle()
        {
            var drop = new List<string>();
            foreach (var k in Cache.Keys) if (!LayerSet.Contains(k) && !KeepAcrossBattles(k)) drop.Add(k);
            foreach (var k in drop) Cache.Remove(k);
        }

        /// <summary>読み込んである絵 (確認用・null = 無かった絵は除く)。</summary>
        public static IEnumerable<FxSheet> Loaded { get { foreach (var s in Cache.Values) if (s != null) yield return s; } }
    }
}
