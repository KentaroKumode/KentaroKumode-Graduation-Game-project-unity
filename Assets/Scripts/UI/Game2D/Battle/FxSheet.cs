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
    /// <para>読み込んだ後は、 コマごとに「不透明な画素の位置と色」だけを持つ (書き出しは不透明か透明かの 2 つだけ・半透明は無い)。
    /// 全画面のコマが多いので、 絵 1 枚の画素を丸ごと持つより軽く、 貼るのも速い。</para>
    /// </summary>
    public sealed class FxSheet
    {
        [System.Serializable] private class R { public int x, y, w, h; }
        [System.Serializable] private class F { public R frame; public R spriteSourceSize; public int duration; }
        [System.Serializable] private class P { public int fps; public int frames; public int[] frameSize; public int[] pivot; public bool loop; }
        [System.Serializable] private class M { public P pixelfx; }
        [System.Serializable] private class J { public F[] frames; public M meta; }

        /// <summary>コマ 1 枚: 不透明な画素の、 コマの中の位置 (x | y &lt;&lt; 16) と色 (<see cref="BattleCanvas.Pack"/> と同じ形)。</summary>
        private struct Frame { public int[] pos; public uint[] col; }
        private readonly Frame[] _frames;
        public readonly int W, H, PivotX, PivotY;
        public readonly bool Loop;

        /// <summary>1 コマの長さ (秒)。 書き出しの fps (既定 1 秒 8 コマ・王の着弾だけ 24 コマ)。</summary>
        public readonly float FrameSeconds;
        public int FrameCount => _frames.Length;
        public float Duration => _frames.Length * FrameSeconds;

        private FxSheet(Color32[] px, int sw, int sh, J j)
        {
            var p = j.meta.pixelfx;
            W = p.frameSize[0]; H = p.frameSize[1]; PivotX = p.pivot[0]; PivotY = p.pivot[1]; Loop = p.loop;
            FrameSeconds = 1f / Mathf.Max(1, p.fps);
            _frames = new Frame[j.frames.Length];
            var pos = new List<int>(); var col = new List<uint>();
            for (int k = 0; k < j.frames.Length; k++)
            {
                var r = j.frames[k].frame; var s = j.frames[k].spriteSourceSize;
                int ox = s != null ? s.x : 0, oy = s != null ? s.y : 0;
                pos.Clear(); col.Clear();
                for (int y = 0; y < r.h; y++)
                {
                    int row = (sh - 1 - (r.y + y)) * sw;      // GetPixels32 は下の行から
                    for (int x = 0; x < r.w; x++)
                    {
                        var c = px[row + r.x + x];
                        if (c.a == 0) continue;
                        pos.Add((ox + x) | ((oy + y) << 16)); col.Add(BattleCanvas.Pack(c.r, c.g, c.b));
                    }
                }
                _frames[k] = new Frame { pos = pos.ToArray(), col = col.ToArray() };
            }
        }

        private static readonly Dictionary<string, FxSheet> Cache = new Dictionary<string, FxSheet>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>Resources/Game2DFx/ から読む。 無ければ null (警告を 1 度だけ出す)。</summary>
        public static FxSheet Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var s)) return s;
            var img = Resources.Load<TextAsset>("Game2DFx/" + name + ".png");
            var json = Resources.Load<TextAsset>("Game2DFx/" + name);
            if (img == null || json == null) { Debug.LogWarning("[FxSheet] Resources/Game2DFx/" + name + " が無い"); return Cache[name] = null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(img.bytes);
            s = new FxSheet(tex.GetPixels32(), tex.width, tex.height, JsonUtility.FromJson<J>(json.text));
            Object.Destroy(tex);
            Resources.UnloadAsset(img); Resources.UnloadAsset(json);
            return Cache[name] = s;
        }

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

        /// <summary>コマ k (範囲外は何もしない) を、 コマの左上が (left, top) に来るように貼る。</summary>
        public void DrawFrame(BattleCanvas c, int k, int left, int top, bool add = false)
        {
            if (k < 0 || k >= _frames.Length) return;
            var f = _frames[k]; var fb = c.FB;
            for (int i = 0; i < f.pos.Length; i++)
            {
                int x = left + (f.pos[i] & 0xffff), y = top + (f.pos[i] >> 16);
                if (x < 0 || y < 0 || x >= BattleCanvas.W || y >= BattleCanvas.H) continue;
                if (add) { uint v = f.col[i]; c.PutRgb(x, y, v & 255, (v >> 8) & 255, (v >> 16) & 255, 1f, true); }
                else if (c.clipY0 < 0 || (y >= c.clipY0 && y < c.clipY1)) fb[y * BattleCanvas.W + x] = f.col[i];
            }
        }

        /// <summary>コマ k に画素が 1 つも無いか (範囲外も true)。</summary>
        public bool Empty(int k) => k < 0 || k >= _frames.Length || _frames[k].pos.Length == 0;
    }
}
