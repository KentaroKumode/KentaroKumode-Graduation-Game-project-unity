using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// コマを並べたエフェクトの絵 (スプライトシート) を戦闘演出の作図面へ貼る (試作・2026-11-01 / 2026-11-14 にボスと武器の絵を足した)。
    /// 絵は pixelfx (https://github.com/patch-gg/pixelfx・MIT) の効果を、 液晶の見た目へ寄せて書き出したもの
    /// (光のぼかし → 網点・色は 5 色の段・半透明なし)。 書き出しの道具:
    /// 会心・毒・出血は Tools/pixelfx/dicebound/export_game.py、 ボスの大技・通常の技・武器の攻撃は export_fx.py
    /// (見本と同じ Tools/pixelfx/lab/lcd_frames.mjs で焼き、 画素を変えずにコマごとの中身のある範囲だけ切り出して並べた物)。
    ///
    /// <para>置き場所は Resources/Game2DFx/: 絵は <c>名前.png.bytes</c> (中身は PNG。 .png は git の管理外なので bytes で持つ)、
    /// コマの位置は <c>名前.json</c> (Aseprite 形式・meta.pixelfx に fps と基準点)。 切り出した物は、 コマの中の位置を
    /// <c>spriteSourceSize</c> に持つ (無ければ 0,0 = コマ全体)。 拡縮はしない (1 画素 = 1 dot)。</para>
    ///
    /// <para><b>持ち方</b> (2026-11-14 に軽くした): 読み込んだ後は、 コマごとに「不透明な画素の位置と色」を持つ (書き出しは不透明か透明かの 2 つだけ)。
    /// ただし、 コマの中で一番多い色が横に続いている所 (<see cref="RunMin"/> 画素以上) は、 1 画素ずつではなく「横の帯 (始まりと長さ) + その色 1 つ」で持つ
    /// (白飛び・消灯・暗転のべた塗りが 1 行 1 本の帯になる)。 帯にした方が軽い時だけ選ぶ。 帯と残りの画素は重ならず、 どれも上から塗る (または足す) だけなので、
    /// 貼った結果は 1 画素ずつ持った時と同じ (順番に依らない)。</para>
    ///
    /// <para><b>読み込みと捨て方</b>: <see cref="Preload"/> = 先に読む (戦闘の入りなど)・<see cref="Load"/> = 使う瞬間 (先に読めていなければここで読み、
    /// エディタと開発ビルドでは 1 行の警告)。 戦闘が終わったら <see cref="UnloadAfterBattle"/> が、 毎戦闘使う小さな絵 (<see cref="KeepAcrossBattles"/>) 以外を捨てる。</para>
    /// </summary>
    public sealed class FxSheet
    {
        [System.Serializable] private class R { public int x, y, w, h; }
        [System.Serializable] private class F { public R frame; public R spriteSourceSize; public int duration; }
        [System.Serializable] private class P { public int fps; public int frames; public int[] frameSize; public int[] pivot; public bool loop; }
        [System.Serializable] private class M { public P pixelfx; }
        [System.Serializable] private class J { public F[] frames; public M meta; }

        /// <summary>帯にする最短の長さ (画素)。 これより短い同色の並びは 1 画素ずつ持つ。</summary>
        public const int RunMin = 4;

        /// <summary>コマ 1 枚: 不透明な画素の、 コマの中の位置 (x | y &lt;&lt; 16) と色 (<see cref="BattleCanvas.Pack"/> と同じ形)、
        /// と、 下地の色 <c>runCol</c> の横の帯 (始まり x | y &lt;&lt; 16 と長さ)。 帯が無いコマは runPos = null。</summary>
        private struct Frame { public int[] pos; public uint[] col; public int[] runPos, runLen; public uint runCol; }
        private readonly Frame[] _frames;
        public readonly int W, H, PivotX, PivotY;
        public readonly bool Loop;
        public readonly string Name;

        /// <summary>1 コマの長さ (秒)。 書き出しの fps (既定 1 秒 8 コマ・王の着弾だけ 24 コマ)。</summary>
        public readonly float FrameSeconds;
        public int FrameCount => _frames.Length;
        public float Duration => _frames.Length * FrameSeconds;

        /// <summary>読み込んだ後に持っている量の見積もり (バイト・配列の中身だけ) / 帯を使わなかった時の量 / 読み込みに掛かった時間 (ミリ秒)。</summary>
        public long Bytes { get; private set; }
        public long BytesPlain { get; private set; }
        public float LoadMs { get; private set; }

        private FxSheet(string name, Color32[] px, int sw, int sh, J j)
        {
            Name = name;
            var p = j.meta.pixelfx;
            W = p.frameSize[0]; H = p.frameSize[1]; PivotX = p.pivot[0]; PivotY = p.pivot[1]; Loop = p.loop;
            FrameSeconds = 1f / Mathf.Max(1, p.fps);
            _frames = new Frame[j.frames.Length];
            var pos = new List<int>(); var col = new List<uint>(); var rp = new List<int>(); var rl = new List<int>();
            var count = new Dictionary<uint, int>();
            for (int k = 0; k < j.frames.Length; k++)
            {
                var r = j.frames[k].frame; var s = j.frames[k].spriteSourceSize;
                int ox = s != null ? s.x : 0, oy = s != null ? s.y : 0;
                // 一番多い色 (下地の候補)
                count.Clear(); uint top = 0; int topN = 0, opaque = 0;
                for (int y = 0; y < r.h; y++)
                {
                    int row = (sh - 1 - (r.y + y)) * sw;      // GetPixels32 は下の行から
                    for (int x = 0; x < r.w; x++)
                    {
                        var c = px[row + r.x + x]; if (c.a == 0) continue;
                        uint v = BattleCanvas.Pack(c.r, c.g, c.b); opaque++;
                        count.TryGetValue(v, out int n); count[v] = ++n;
                        if (n > topN) { topN = n; top = v; }
                    }
                }
                pos.Clear(); col.Clear(); rp.Clear(); rl.Clear();
                for (int y = 0; y < r.h; y++)
                {
                    int row = (sh - 1 - (r.y + y)) * sw;
                    for (int x = 0; x < r.w; x++)
                    {
                        var c = px[row + r.x + x]; if (c.a == 0) continue;
                        uint v = BattleCanvas.Pack(c.r, c.g, c.b);
                        if (v == top && topN >= RunMin)
                        {
                            int e = x; while (e + 1 < r.w) { var d = px[row + r.x + e + 1]; if (d.a == 0 || BattleCanvas.Pack(d.r, d.g, d.b) != top) break; e++; }
                            if (e - x + 1 >= RunMin) { rp.Add((ox + x) | ((oy + y) << 16)); rl.Add(e - x + 1); x = e; continue; }
                        }
                        pos.Add((ox + x) | ((oy + y) << 16)); col.Add(v);
                    }
                }
                long plain = opaque * 8L, withRuns = pos.Count * 8L + rp.Count * 8L;
                BytesPlain += plain;
                if (rp.Count > 0 && withRuns < plain)
                {
                    _frames[k] = new Frame { pos = pos.ToArray(), col = col.ToArray(), runPos = rp.ToArray(), runLen = rl.ToArray(), runCol = top };
                    Bytes += withRuns;
                }
                else
                {
                    // 帯が得にならないコマ (まばらな網点など): 今までどおり 1 画素ずつ。 帯にした画素を戻す
                    pos.Clear(); col.Clear();
                    for (int y = 0; y < r.h; y++)
                    {
                        int row = (sh - 1 - (r.y + y)) * sw;
                        for (int x = 0; x < r.w; x++)
                        {
                            var c = px[row + r.x + x]; if (c.a == 0) continue;
                            pos.Add((ox + x) | ((oy + y) << 16)); col.Add(BattleCanvas.Pack(c.r, c.g, c.b));
                        }
                    }
                    _frames[k] = new Frame { pos = pos.ToArray(), col = col.ToArray() };
                    Bytes += plain;
                }
            }
        }

        private static readonly Dictionary<string, FxSheet> Cache = new Dictionary<string, FxSheet>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>使う瞬間に読む。 先に読めていなければここで読み、 エディタと開発ビルドでは 1 行の警告を出す (どの絵を何ミリ秒で)。 無ければ null (警告を 1 度だけ出す)。</summary>
        public static FxSheet Load(string name) => Get(name, true);

        /// <summary>先に読んでおく (戦闘の入りなど・止まっても目立たない所で)。 読んだ物を返す。</summary>
        public static FxSheet Preload(string name) => Get(name, false);

        /// <summary>もう読んであるか。</summary>
        public static bool IsLoaded(string name) => !string.IsNullOrEmpty(name) && Cache.ContainsKey(name);

        private static FxSheet Get(string name, bool late)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var s)) return s;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var img = Resources.Load<TextAsset>("Game2DFx/" + name + ".png");
            var json = Resources.Load<TextAsset>("Game2DFx/" + name);
            if (img == null || json == null) { Debug.LogWarning("[FxSheet] Resources/Game2DFx/" + name + " が無い"); return Cache[name] = null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(img.bytes);
            s = new FxSheet(name, tex.GetPixels32(), tex.width, tex.height, JsonUtility.FromJson<J>(json.text));
            Object.Destroy(tex);
            Resources.UnloadAsset(img); Resources.UnloadAsset(json);
            s.LoadMs = (float)sw.Elapsed.TotalMilliseconds;
            WarnLate(late, s);
            return Cache[name] = s;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void WarnLate(bool late, FxSheet s)
        {
            if (late) Debug.LogWarning($"[FxSheet] 先に読めていない絵を使う瞬間に読んだ: {s.Name} ({s.LoadMs:0.0} ms)");
        }

        // ── 捨てる基準 (1 か所) ──

        /// <summary>戦闘をまたいで残す絵 (小さくて毎戦闘使う物): 武器・会心・毒・出血。 それ以外 (ボスの大技・技の絵) は戦闘が終わったら捨てる。</summary>
        public static bool KeepAcrossBattles(string name)
            => name.StartsWith("weapon-") || name == "crit-hit" || name == "poison-tick" || name == "bleed-tick";

        /// <summary>戦闘が終わった時: <see cref="KeepAcrossBattles"/> 以外をキャッシュから外す (持っている参照が無くなれば GC が回収する)。</summary>
        public static void UnloadAfterBattle()
        {
            var drop = new List<string>();
            foreach (var k in Cache.Keys) if (!KeepAcrossBattles(k)) drop.Add(k);
            foreach (var k in drop) Cache.Remove(k);
        }

        /// <summary>読み込んである絵 (確認用・null = 無かった絵は除く)。</summary>
        public static IEnumerable<FxSheet> Loaded { get { foreach (var s in Cache.Values) if (s != null) yield return s; } }

        // ── 貼る ──

        /// <summary>経過 t 秒のコマを、 基準点が (x, y) に来るように貼る。 終わっていたら false。
        /// add = 光として足し込む (会心の光。 戦闘演出の発光と同じ加算) / false = 上から塗る (毒・出血の滴・ボスと武器の絵 ── 見本の paste と同じ)。</summary>
        public bool Draw(BattleCanvas c, float t, int x, int y, bool add = true)
        {
            int k = Mathf.FloorToInt(t / FrameSeconds);
            if (k < 0) return true;
            if (k >= _frames.Length) { if (!Loop) return false; k %= _frames.Length; }
            DrawFrame(c, k, x - PivotX, y - PivotY, add);
            return true;
        }

        /// <summary>コマ k (範囲外は何もしない) を、 コマの左上が (left, top) に来るように貼る。 帯 → 残りの画素の順 (重ならないので順は結果に効かない)。</summary>
        public void DrawFrame(BattleCanvas c, int k, int left, int top, bool add = false)
        {
            if (k < 0 || k >= _frames.Length) return;
            var f = _frames[k]; var fb = c.FB;
            if (f.runPos != null)
            {
                uint v = f.runCol;
                for (int i = 0; i < f.runPos.Length; i++)
                {
                    int y = top + (f.runPos[i] >> 16);
                    if (y < 0 || y >= BattleCanvas.H) continue;
                    if (!add && c.clipY0 >= 0 && (y < c.clipY0 || y >= c.clipY1)) continue;
                    int x0 = left + (f.runPos[i] & 0xffff), x1 = x0 + f.runLen[i];
                    if (x0 < 0) x0 = 0; if (x1 > BattleCanvas.W) x1 = BattleCanvas.W;
                    int row = y * BattleCanvas.W;
                    if (add) for (int x = x0; x < x1; x++) c.PutRgb(x, y, v & 255, (v >> 8) & 255, (v >> 16) & 255, 1f, true);
                    else for (int x = x0; x < x1; x++) fb[row + x] = v;
                }
            }
            for (int i = 0; i < f.pos.Length; i++)
            {
                int x = left + (f.pos[i] & 0xffff), y = top + (f.pos[i] >> 16);
                if (x < 0 || y < 0 || x >= BattleCanvas.W || y >= BattleCanvas.H) continue;
                if (add) { uint v = f.col[i]; c.PutRgb(x, y, v & 255, (v >> 8) & 255, (v >> 16) & 255, 1f, true); }
                else if (c.clipY0 < 0 || (y >= c.clipY0 && y < c.clipY1)) fb[y * BattleCanvas.W + x] = f.col[i];
            }
        }

        /// <summary>コマ k に画素が 1 つも無いか (範囲外も true)。</summary>
        public bool Empty(int k) => k < 0 || k >= _frames.Length || (_frames[k].pos.Length == 0 && _frames[k].runPos == null);
    }
}
