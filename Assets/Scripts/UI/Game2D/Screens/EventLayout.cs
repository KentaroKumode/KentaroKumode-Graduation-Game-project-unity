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
        /// <summary>イベントの画面の左右の余白 (dot・<b>イベントだけの値</b>)。 板・列・字はこの内側。 背景の絵だけは画面いっぱい。
        /// <para>液晶の安全域 (<see cref="LcdSafeArea"/>・左右 16) より 8 dot 狭い (2026-11-16・作者の承認): 液晶の外形を湾曲に合わせる今の既定 (_CurveFit = 1) では
        /// 絵が 1 dot も画面の外へ出ないので、 左右の欠けを避ける余白は要らなくなった。 広げた分は本文の折り返しの幅と選択肢の名前の幅に回した。
        /// 安全域そのもの (結末の画面などが読む) は作者が承認した配置のために変えないので、 イベントだけがこの値を持つ。
        /// 上下は安全域のまま (板と列は上下の中央なので端に寄らない)。</para>
        /// <para>経緯: 見本の配置 (端から 4 dot) は旧い合わせ方 (f = 0) の湾曲で左の字・右の札・下の縁が欠け、 一度は安全域 (16) まで寄せた。</para></summary>
        public const int EdgeX = 8;
        public const int SafeL = EdgeX, SafeR = ScreenW - EdgeX, SafeT = LcdSafeArea.T, SafeB = LcdSafeArea.B;

        // ── 本文の板 (左下: 左の余白 EdgeX と下の余白 SafeB に付ける) ──
        //   経緯 (どれも 2026-11-16): 見本どおり左下 → 作者の決定で上下の中央 (液晶の湾曲 (角ほど強い・旧い合わせ方 f = 0) で板の左の縁が斜めに倒れて見えたため)
        //   → 作者の決定で左下へ戻した: 背景の絵 22 枚は「主役を上半分に置く」構図で作ってあり、 上下の中央の板が主役の大半を隠した (Captures/event_bg/ingame_sheet.png)。
        //   液晶の外形を湾曲に合わせた今 (f = 1) は縁の倒れが弱まっている見込みなので、 絵を優先する。
        //   四隅の楔 (f = 1 で画面の角が背景色になる所) との間: 板の左下の角 (8, 259) は画面の (17.3, 254.0) に見え、 楔の縁 (絵の外形) まで横 7・縦 9.6 dot 空く (k = 0.07)
        public const int PlateW = 278, PlateH = 158;
        public const int PlateX = SafeL, PlateY = SafeB - PlateH;
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
        /// <summary>列は本文の板の右から右の余白 (<see cref="SafeR"/>) まで。</summary>
        public const int ColX = PlateX + PlateW + PlateColGap, ColW = SafeR - ColX;
        /// <summary>箱の高さと箱の間。</summary>
        public const int BoxH = 21, BoxGap = 4;
        /// <summary>箱の左端から番号・名前の字の左端。 札は箱の右端から ChipR 内側で右寄せ。</summary>
        public const int NumDx = 5, NameDx = 15, ChipR = 5;
        /// <summary>札と札の間・名前と札の間。</summary>
        public const int ChipGap = 5, NameChipGap = 4;
        /// <summary>砂時計の絵の幅と、 絵と数の間。</summary>
        public const int HourglassW = 7, HourglassGap = 2;
        /// <summary>列の真ん中を置く高さ (本文の板の上端あたり・板を左下に置いた時と同じ考え方)。</summary>
        public const int ColCenterY = PlateY + 20;
        /// <summary>説明の板: 行数の上限・内側の余白 (上下・左右)。</summary>
        public const int NoteLines = 2, NotePadY = 3, NotePadX = 6;
        public const int NoteW = ColW - 2 * NotePadX;
        /// <summary>選んでいる箱の左端の帯の幅 (液晶のにじみで細い縁が弱く見えても、 どれを選んでいるか分かる印)。</summary>
        public const int SelBarW = 2;

        // ── 上の帯 (航行の数値・2026-11-16 作者の承認) ──
        //   画面の上辺に付ける (y 0・作者の指示「もっと上辺ギチギチまで」)。 液晶の外形を湾曲に合わせた今 (f = 1) は絵が画面の外へ出ないので、 y 0 の行も欠けない
        //   (上辺の中央では絵の y 0 が画面の y 0 にちょうど重なる)。 左右はイベントの余白 (x 8〜472)。
        //   四隅の楔との関係 (k = 0.07): 帯の左上の角 (8, 0) は画面の (18.9, 6.4)、 右上 (472, 0) は (461.1, 6.4) に見え (絵の角 (0, 0) は (12.0, 6.8))、 どちらも絵の外形 (楔の縁) の上に乗る
        //   ── 絵の上端 (y 0) の行は外形そのものなので、 帯の上の縁は全長で外形に沿って弓なりに見え、 楔の色には掛からない (角を落とす必要は無い)。
        /// <summary>帯の上端・高さ・左右の内側の余白・値と値の間・ラベルと値の間。</summary>
        public const int BarY = 0, BarH = 16, BarPadX = 8, BarItemGap = 12, BarLabelGap = 4;
        public const int BarX = SafeL, BarW = SafeR - SafeL;

        /// <summary>帯に並べる値 1 つ。 label = 暗い字 (null なら値だけ・ゴールド「36G」)・value = 明るい字・warn = 全部を警告の色 (補足中)。</summary>
        public struct BarItem { public string label, value; public bool warn; }

        /// <summary>値 1 つの幅。</summary>
        public static int BarItemW(BarItem it, Func<char, int> adv)
            => (string.IsNullOrEmpty(it.label) ? 0 : Width(it.label, adv) + BarLabelGap) + Width(it.value, adv);

        /// <summary>帯の中身の幅 (左右の余白を含む)。 これが <see cref="BarW"/> 以下なら収まる。</summary>
        public static int BarContentW(List<BarItem> items, Func<char, int> adv)
        {
            int w = 2 * BarPadX;
            if (items == null) return w;
            for (int i = 0; i < items.Count; i++) w += BarItemW(items[i], adv) + (i > 0 ? BarItemGap : 0);
            return w;
        }

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
