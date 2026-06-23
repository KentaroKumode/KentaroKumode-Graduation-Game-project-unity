using System;

namespace GameLoop
{
    /// <summary>
    /// 層進入時に表示する層タイトル／サブタイトル（確定文言の正本: docs/specs/map-run.md）。
    /// ビジュアル演出は未実装。UI バナーは <see cref="OnShow"/> を購読して後付けする。
    /// 表示の発火は GameManager の層進入地点（EnterFloor／EnterLambda）から行う。
    /// </summary>
    public static class LayerTitles
    {
        public readonly struct Entry
        {
            public readonly string title;
            public readonly string subtitle;
            public Entry(string title, string subtitle) { this.title = title; this.subtitle = subtitle; }
        }

        // floor 1..7（index = floor-1）。Λ層は LambdaEntry。
        private static readonly Entry[] Floors =
        {
            new Entry("第一層　陽の差す浅層", "振り返れば、まだ入口の光が見える"),
            new Entry("第二層　群れの領分",   "石の通路に、無数の足音が反響する"),
            new Entry("第三層　毒の沼",       "空気が変わる。息をするたび、喉の奥が湿る"),
            new Entry("第四層　鏡像の界",     "影が、ひとつだけ多い"),
            new Entry("第五層　審きの淵",     "多くの者が、ここで引き返す。引き返せた者は"),
            new Entry("第六層　灰の玉座",     "滅びを見届ける番人が、ここに座している"),
            new Entry("終層　Null Point",     "Signal lost"),
        };

        private static readonly Entry LambdaEntry =
            new Entry("時間の狭間", "同じ角を、もう何度曲がっただろう");

        /// <summary>UI バナー配線用フック（ビジュアルは後付け）。引数 = (title, subtitle)。</summary>
        public static event Action<string, string> OnShow;

        public static Entry ForFloor(int floor)
        {
            int i = floor - 1;
            if (i < 0 || i >= Floors.Length) return new Entry($"第{floor}層", "");
            return Floors[i];
        }

        public static Entry ForLambda() => LambdaEntry;

        /// <summary>OnShow を発火（UI 未配線時は何も起きない）。ログは呼び出し側で行う。</summary>
        public static void Raise(Entry e) => OnShow?.Invoke(e.title, e.subtitle);
    }
}
