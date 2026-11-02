using System.Collections.Generic;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// コマを並べたエフェクトの絵 (スプライトシート) を戦闘演出の作図面へ貼る (試作・2026-11-01)。
    /// 絵は pixelfx (https://github.com/patch-gg/pixelfx・MIT) の効果を、 液晶の見た目へ寄せて書き出したもの
    /// (光のぼかし → 網点・色は戦闘画面の暖色 5 色・半透明なし)。 書き出しの道具は Tools/pixelfx/dicebound/export_game.py。
    ///
    /// <para>置き場所は Resources/Game2DFx/: 絵は <c>名前.png.bytes</c> (中身は PNG。 .png は git の管理外なので bytes で持つ)、
    /// コマの位置は <c>名前.json</c> (Aseprite 形式・meta.pixelfx に fps と基準点)。 拡縮はしない (1 画素 = 1 dot)。</para>
    /// </summary>
    public sealed class FxSheet
    {
        [System.Serializable] private class R { public int x, y, w, h; }
        [System.Serializable] private class F { public R frame; public int duration; }
        [System.Serializable] private class P { public int fps; public int frames; public int[] frameSize; public int[] pivot; public bool loop; }
        [System.Serializable] private class M { public P pixelfx; }
        [System.Serializable] private class J { public F[] frames; public M meta; }

        private readonly Color32[] _px; private readonly int _sw;
        private readonly F[] _frames;
        public readonly int W, H, PivotX, PivotY;
        public readonly bool Loop;

        /// <summary>1 コマの長さ (秒)。 書き出しの fps (既定 1 秒 8 コマ)。</summary>
        public readonly float FrameSeconds;
        public int FrameCount => _frames.Length;
        public float Duration => _frames.Length * FrameSeconds;

        private FxSheet(Texture2D tex, J j)
        {
            _px = tex.GetPixels32(); _sw = tex.width;
            _frames = j.frames;
            var p = j.meta.pixelfx;
            W = p.frameSize[0]; H = p.frameSize[1]; PivotX = p.pivot[0]; PivotY = p.pivot[1]; Loop = p.loop;
            FrameSeconds = 1f / Mathf.Max(1, p.fps);
        }

        private static readonly Dictionary<string, FxSheet> Cache = new Dictionary<string, FxSheet>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>Resources/Game2DFx/ から読む。 無ければ null (警告を 1 度だけ出す)。</summary>
        public static FxSheet Load(string name)
        {
            if (Cache.TryGetValue(name, out var s)) return s;
            var img = Resources.Load<TextAsset>("Game2DFx/" + name + ".png");
            var json = Resources.Load<TextAsset>("Game2DFx/" + name);
            if (img == null || json == null) { Debug.LogWarning("[FxSheet] Resources/Game2DFx/" + name + " が無い"); return Cache[name] = null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(img.bytes);
            s = new FxSheet(tex, JsonUtility.FromJson<J>(json.text));
            Object.Destroy(tex);
            return Cache[name] = s;
        }

        /// <summary>経過 t 秒のコマを、 基準点が (x, y) に来るように貼る。 終わっていたら false。
        /// add = 光として足し込む (会心の光。 戦闘演出の発光と同じ加算) / false = 上から塗る (毒・出血の滴。 足すと明るい敵の上で消える)。</summary>
        public bool Draw(BattleCanvas c, float t, int x, int y, bool add = true)
        {
            int k = Mathf.FloorToInt(t / FrameSeconds);
            if (k < 0) return true;
            if (k >= _frames.Length) { if (!Loop) return false; k %= _frames.Length; }
            var r = _frames[k].frame;
            int ox = x - PivotX, oy = y - PivotY, sh = _px.Length / _sw;
            for (int j = 0; j < r.h; j++)
            {
                int row = (sh - 1 - (r.y + j)) * _sw;      // GetPixels32 は下の行から
                for (int i = 0; i < r.w; i++)
                {
                    var p = _px[row + r.x + i];
                    if (p.a == 0) continue;
                    c.PutRgb(ox + i, oy + j, p.r, p.g, p.b, 1f, add);
                }
            }
            return true;
        }
    }
}
