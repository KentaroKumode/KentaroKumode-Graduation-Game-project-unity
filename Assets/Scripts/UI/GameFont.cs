using TMPro;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// ゲームで使うフォントの指定は<b>ここだけ</b> (2026-11-04)。 フォントを変える時はこのファイルの定数を変える。
    ///
    /// <para>字は 2 通りの描き方で出る。 どちらも同じフォント (x8y12pxDenkiChip) から作る。</para>
    /// <list type="bullet">
    /// <item>TextMesh Pro の字 (<see cref="Tmp"/>): 起動の「TOUCH DISPLAY」・職業選択・図鑑・層のタイトル。
    ///   各画面はフォントの欄を持たず、 文字を作る時に <see cref="Apply"/> を呼ぶ。</item>
    /// <item>液晶の GUI のドット字 (<see cref="Game2D.Game2DJpFont"/>): 航行・戦闘・ロード画面・場面の画面。 テクスチャへ 1 ドットずつ打つので SDF ではなく、
    ///   <see cref="TmpPath"/> の元の ttf を <see cref="DotPx"/> で 2 値に焼いた <see cref="DotPath"/> を読む。
    ///   焼くのは <c>Tools/bake_jpfont.py</c> で、 この 3 つの定数をこのファイルから読む (フォントを変えたら焼き直す)。</item>
    /// </list>
    /// <para>DenkiChip に無い字 (66 字・2026-11-04) は拡張フォント x8y12pxDenkiChip Ext が持つ。 ドット絵の元は <c>Tools/fontext/glyphs.txt</c>、
    ///   .ttf は <c>Tools/fontext/build_ext_font.py</c> が作る。 TMP では拡張の SDF (<see cref="ExtTmpPath"/>) を本体の fallback につなぎ、
    ///   ドット字は焼く時に本体と拡張をまとめる。</para>
    /// <para>(旧: 画面ごとに mihiPixelmoji・LiberationSans・名前で探した日本語フォント・参照の切れたフォントが混在していた)</para>
    /// </summary>
    public static class GameFont
    {
        /// <summary>TextMesh Pro のフォント (Resources からの道・拡張子なし)。</summary>
        public const string TmpPath = "Fonts/x8y12pxDenkiChip SDF";
        /// <summary>本体に無い字を持つ拡張フォントの TextMesh Pro のフォント (Resources からの道)。 <see cref="Tmp"/> が本体の fallback につなぐ。</summary>
        public const string ExtTmpPath = "Fonts/x8y12pxDenkiChipExt SDF";
        /// <summary>液晶のドット字 (Resources からの道・拡張子なし・中身は .bytes)。</summary>
        public const string DotPath = "Fonts/x8y12pxDenkiChip_dot";
        /// <summary>ドット字を焼く大きさ (px)。 元のフォントの 1 ドットが 1 px になる大きさ (DenkiChip は unitsPerEm 1200 = 12 ドット)。</summary>
        public const int DotPx = 12;

        private static TMP_FontAsset _tmp;
        private static bool _warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _tmp = null; _warned = false; }

        /// <summary>TextMesh Pro のフォント。 無ければ null (警告を 1 度だけ出し、 TMP の既定のフォントのままになる)。</summary>
        public static TMP_FontAsset Tmp
        {
            get
            {
                if (_tmp != null) return _tmp;
                _tmp = Resources.Load<TMP_FontAsset>(TmpPath);
                if (_tmp == null && !_warned) { _warned = true; Debug.LogWarning("[GameFont] Resources/" + TmpPath + " が無い"); }
                if (_tmp != null) AttachExt(_tmp);
                return _tmp;
            }
        }

        /// <summary>拡張フォントを本体の fallback の先頭につなぐ (既にあれば何もしない)。 本体の SDF を作り直しても、 ここでつなぎ直る。</summary>
        private static void AttachExt(TMP_FontAsset main)
        {
            var ext = Resources.Load<TMP_FontAsset>(ExtTmpPath);
            if (ext == null) { Debug.LogWarning("[GameFont] Resources/" + ExtTmpPath + " が無い (DenkiChip に無い字が豆腐になる)"); return; }
            if (main.fallbackFontAssetTable == null) main.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (!main.fallbackFontAssetTable.Contains(ext)) main.fallbackFontAssetTable.Insert(0, ext);
        }

        /// <summary>文字にゲームのフォントを付ける (マテリアルもフォントのものへ揃える)。</summary>
        public static void Apply(TMP_Text t)
        {
            var f = Tmp;
            if (t == null || f == null) return;
            t.font = f;
            if (f.material != null) t.fontSharedMaterial = f.material;
        }
    }
}
