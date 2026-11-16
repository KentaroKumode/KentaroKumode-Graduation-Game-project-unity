using System;
using System.Collections.Generic;
using EventSystem;

namespace UI.Game2D
{
    /// <summary>
    /// イベントの画面の配置 (2026-11-16・作者が見本 Captures/event_layout/sheet_v2.png で承認)。 寸法の式は<b>この 1 か所</b>で決める
    /// ── 描く側 (<see cref="EventScreenView"/>) と検算 (Tools/event_page_check) が同じ式を使う。 <b>UnityEngine に依存しない</b>。
    /// 単位は 480×270 の格子の dot。 字の送り幅・行送りは呼ぶ側が渡す (<see cref="Game2DJpFont.Advance"/>・<see cref="Game2DJpFont.LineH"/>)。
    ///
    /// <para>画面全体に背景の絵 → 左下に本文の板 (題・頁の位置・区切り線・本文 8 行・操作の行) → 右に選択肢の列
    /// (箱 = 番号・名前 1 行・右端に見せる代償の札。 選んでいる箱の下に説明の板 1〜2 行)。 選択肢は最後の頁だけ。</para>
    /// </summary>
    public static class EventLayout
    {
        public const int ScreenW = 480, ScreenH = 270;
        /// <summary>板・列・字を収める範囲 = 液晶の安全域 (<see cref="LcdSafeArea"/>・結末の画面と同じ左右 16・上下 9)。 背景の絵だけは画面いっぱい。
        /// 2026-11-16 の実機で、 見本の配置 (端から 4 dot) は液晶の湾曲 (旧い合わせ方 f = 0) で左の字・右の札・下の縁が欠けた。 外形を湾曲に合わせる今の既定 (f = 1) では欠けない
        /// (広げる余地は左右それぞれ 16 dot まで・上下は板が中央なので関係ない ── 広げるかは作者が決める)。</summary>
        public const int SafeL = LcdSafeArea.L, SafeR = LcdSafeArea.R, SafeT = LcdSafeArea.T, SafeB = LcdSafeArea.B;

        // ── 本文の板 (安全域の左端・画面の上下の中央) ──
        //   2026-11-16 作者の決定: 左下の角に付けると、 液晶の湾曲 (角ほど強い) で板の左の縁が斜めに倒れて見えた。 上下の中央なら縁は上下対称の弓なりで倒れない
        public const int PlateW = 276, PlateH = 158;
        public const int PlateX = SafeL, PlateY = (ScreenH - PlateH) / 2;
        /// <summary>本文の板と選択肢の列の間。</summary>
        public const int PlateColGap = 4;
        /// <summary>板の内側の左右の余白。</summary>
        public const int Pad = 8;
        /// <summary>板の上端から: 題の字の上端・区切り線・本文の 1 行目の上端。</summary>
        public const int TitleDy = 6, RuleDy = 21, BodyDy = 26;
        /// <summary>操作の行 (左に案内の字・右にボタン) の高さと、 板の下端からの余白。</summary>
        public const int OpH = 16, OpBottom = 4;
        /// <summary>操作の行のボタンの左右の余白と、 ボタンの間。</summary>
        public const int BtnPad = 6, BtnGap = 4;

        /// <summary>本文の折り返しの幅。</summary>
        public const int BodyW = PlateW - 2 * Pad;
        public static int OpY => PlateY + PlateH - OpBottom - OpH;

        // ── 選択肢の列 (右) ──
        /// <summary>列は本文の板の右から安全域の右端まで。</summary>
        public const int ColX = PlateX + PlateW + PlateColGap, ColW = SafeR - ColX;
        /// <summary>箱の高さと箱の間。</summary>
        public const int BoxH = 21, BoxGap = 4;
        /// <summary>箱の左端から番号・名前の字の左端。 札は箱の右端から ChipR 内側で右寄せ。</summary>
        public const int NumDx = 5, NameDx = 15, ChipR = 5;
        /// <summary>札と札の間・名前と札の間。</summary>
        public const int ChipGap = 5, NameChipGap = 4;
        /// <summary>砂時計の絵の幅と、 絵と数の間。</summary>
        public const int HourglassW = 7, HourglassGap = 2;
        /// <summary>列の真ん中を置く高さ (画面の上下の中央・本文の板と同じ理由)。</summary>
        public const int ColCenterY = ScreenH / 2;
        /// <summary>説明の板: 行数の上限・内側の余白 (上下・左右)。</summary>
        public const int NoteLines = 2, NotePadY = 3, NotePadX = 6;
        public const int NoteW = ColW - 2 * NotePadX;
        /// <summary>選んでいる箱の左端の帯の幅 (液晶のにじみで細い縁が弱く見えても、 どれを選んでいるか分かる印)。</summary>
        public const int SelBarW = 2;

        /// <summary>字を高さ h の箱の上下の真ん中に置く下げ幅 (cell = 字の高さ)。</summary>
        public static int TextDy(int h, int cell) => (h - cell + 1) / 2;

        /// <summary>本文の板に入る本文の行数 (行送り lineH で、 本文の下端が操作の行より 2 dot 上に収まる数)。</summary>
        public static int BodyLinesFit(int lineH) => (OpY - 2 - (PlateY + BodyDy)) / lineH;

        /// <summary>説明の板の高さ (行 0 = 板を出さない)。</summary>
        public static int NoteH(int lines, int lineH) => lines <= 0 ? 0 : lines * lineH + 2 * NotePadY;

        /// <summary>列の高さ (箱 n 個 + 説明の板 noteH)。</summary>
        public static int ColumnH(int n, int noteH) => n <= 0 ? 0 : n * BoxH + (n - 1) * BoxGap + (noteH > 0 ? BoxGap + noteH : 0);

        /// <summary>列の上端。 列の真ん中を <see cref="ColCenterY"/> に置き、 安全域の上下 (<see cref="SafeT"/>・<see cref="SafeB"/>) にはみ出さないよう寄せる。
        /// maxNoteH = どの箱を選んでも足りるよう取る説明の板の高さ (選びかえで列の上端が動かないように)。</summary>
        public static int ColumnTop(int n, int maxNoteH)
        {
            int h = ColumnH(n, maxNoteH);
            int y = ColCenterY - h / 2;
            if (y + h > SafeB) y = SafeB - h;
            if (y < SafeT) y = SafeT;
            return y;
        }

        /// <summary>列が画面に収まるか (上端を寄せても下にはみ出さない)。</summary>
        public static bool ColumnFits(int n, int maxNoteH) => ColumnTop(n, maxNoteH) >= SafeT && ColumnTop(n, maxNoteH) + ColumnH(n, maxNoteH) <= SafeB;

        /// <summary>札 1 枚の幅。</summary>
        public static int ChipW(EventChoice.CostChip c, Func<char, int> adv) => (c.turns ? HourglassW + HourglassGap : 0) + Width(c.text, adv);

        /// <summary>札を並べた幅 (札の間を含む)。 無ければ 0。</summary>
        public static int ChipsW(List<EventChoice.CostChip> chips, Func<char, int> adv)
        {
            if (chips == null || chips.Count == 0) return 0;
            int w = 0;
            foreach (var c in chips) w += ChipW(c, adv);
            return w + (chips.Count - 1) * ChipGap;
        }

        /// <summary>名前に使える幅 (札の幅 chipsW を引いた残り)。 これを超える名前は末尾を「…」で切る。</summary>
        public static int NameRoom(int chipsW) => ColW - NameDx - ChipR - (chipsW > 0 ? chipsW + NameChipGap : 0);

        public static int Width(string s, Func<char, int> adv)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0; foreach (var ch in s) w += adv(ch);
            return w;
        }
    }
}
