using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// イベントの背景の絵の対応表 (2026-11-16)。 正本はデータ: Resources/<see cref="ResourcePath"/>.txt (1 行 = 「イベント名<c>TAB</c>絵の名前」・# の行と空行は読まない)。
    /// 絵は同じフォルダ (Resources/Game2DBackdrops) の <c>&lt;名前&gt;.png.bytes</c> で、 戦闘の背景と同じ読み方 (<see cref="Battle.BattleBackdrops.Load"/>・加工せず 1 画素ずつ写す)。
    /// イベント名をコードに書かない (表のファイルだけが持つ)。 <b>UnityEngine に依存しない</b> (検算 Tools/event_page_check がこのファイルをそのままコンパイルし、
    /// 全イベントに対応がある・表の名前が全部実在する・絵が全部ある、 を見る)。
    /// </summary>
    public static class EventBackdropTable
    {
        /// <summary>表の置き場 (Resources の下・拡張子 .txt を除く)。 イベントの定義 (Resources/Events/*.txt) と同じ所に置くと、 定義のファイルを全部読む検算の道具が表を
        /// イベントとして読んでしまうので、 絵と同じフォルダに置く。</summary>
        public const string ResourcePath = "Game2DBackdrops/event_backdrops";

        /// <summary>表の文を読む (イベント名 → 絵の名前)。 同じイベント名が 2 度あれば後の行。 形の崩れた行 (タブが無い・どちらかが空) は bad に入れて飛ばす。</summary>
        public static Dictionary<string, string> Parse(string text, List<string> bad = null)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return d;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                int t = line.IndexOf('\t');
                string name = t > 0 ? line.Substring(0, t).Trim() : "", art = t > 0 ? line.Substring(t + 1).Trim() : "";
                if (name.Length == 0 || art.Length == 0) { bad?.Add(line); continue; }
                d[name] = art;
            }
            return d;
        }
    }
}
