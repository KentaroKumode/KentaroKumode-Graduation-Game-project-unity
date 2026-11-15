using System;

namespace GameLoop
{
    /// <summary>
    /// 層進入時に表示する層タイトル／サブタイトル（確定文言の正本: docs/GAME.md §5）。
    /// ビジュアル演出は未実装。UI バナーは <see cref="OnShow"/> を購読して後付けする。
    /// 表示の発火は GameManager の層進入地点（EnterFloor）から行う。
    /// </summary>
    public static class LayerTitles
    {
        public readonly struct Entry
        {
            public readonly string title;
            public readonly string subtitle;
            public Entry(string title, string subtitle) { this.title = title; this.subtitle = subtitle; }
        }

        // floor 1..8（index = floor-1）。
        //
        // **2026-11-16: 副題を書き直した** (Gemini の案から作者が選んだ)。 第五層は「掘り止め」→「裂け目」(大穴の底の裂け目へ近づく層・灰の玉座の続きとして・§19)。
        //   最終段は「終層　Null Point / Signal lost」→「Null Point / すべての始まりにして終着点」(作者の決定)。
        //
        // **2026-09-14: 「終層 Null Point / Signal lost」は 8 層へ移した。**
        //   7 層にボスは居なくなり、 終端は〈門〉になる。 ヴェスカが座るのは
        //   門が転移させた先 ── 大穴の続きではない場所なので、 層の名前も
        //   大穴の数え方 (第N層) から外れる。
        private static readonly Entry[] Floors =
        {
            new Entry("第一層　陽の差す浅層", "振り返れば、まだ入口の光と街の音が届く"),
            new Entry("第二層　群れの領分",   "石の通路に、無数の足音が反響する"),
            new Entry("第二層　毒の沼",       "陽はもう届かない。ぬかるみが靴底を捕まえる"),
            new Entry("第四層　鏡像の界",     "影が、ひとつだけ多い"),
            new Entry("第三層　審きの淵",     "多くの者がここで折れ、生き残りは酒場へ還る"),
            new Entry("第四層　灰の玉座",     "音もなく降る灰が、帰りの足跡を埋めていく"),
            new Entry("第五層　裂け目",       "落ちかけた水滴が、空中で光ったまま止まっている"),
            new Entry("Null Point",           "すべての始まりにして終着点"),
        };

        /// <summary>UI バナー配線用フック（ビジュアルは後付け）。引数 = (title, subtitle)。</summary>
        public static event Action<string, string> OnShow;

        public static Entry ForFloor(int floor)
        {
            if (floor == 0) return new Entry("序層　古い倉庫", "潜る前に、なまった手足の感覚を確かめておく");   // 題は仮置き (2026-11-05)
            int i = floor - 1;
            if (i < 0 || i >= Floors.Length) return new Entry($"第{floor}層", "");
            return Floors[i];
        }

        /// <summary>OnShow を発火（UI 未配線時は何も起きない）。ログは呼び出し側で行う。</summary>
        public static void Raise(Entry e) => OnShow?.Invoke(e.title, e.subtitle);
    }
}
