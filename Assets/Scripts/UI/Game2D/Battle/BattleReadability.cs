using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 戦闘の画面の読みやすさの直し方 (2026-11-16・見比べ用)。 戦闘の背景を新しい絵 10 枚 (<see cref="BattleBackdrops"/>) にしたら、
    /// 仮の背景より明るく、 液晶の中の戦闘の画面 (<see cref="Game2DBattleLcd"/>) のカードの外の字・敵の頭上の数・床の出目が背景に紛れる所が出た。
    /// 直し方を 2 つ作り、 作者が実機で見比べて選ぶ:
    /// <list type="bullet">
    /// <item><b>A (表示側で浮かせる・<see cref="Outline"/>)</b>: カードの外の字の暗い縁取りを 1 ドット → 2 ドット (角を落とした丸い形) にする。
    /// 敵の頭上の印 (予告の絵と数・毒・出血・構え) にも同じ縁取り。 床の出目に暗い縁 (左上 1 ドット・右下 2 ドット = 1 ドットの落ち影)。</item>
    /// <item><b>B (背景を暗くして出す・<see cref="DimBackdrop"/>)</b>: 戦闘ごとの背景の画素を一律 <see cref="Dim"/> 倍 (既定 <see cref="DefaultDim"/>) にして下地にする。
    /// 絵のファイルは変えない (読み込んだ後に <see cref="Game2DBattleFx"/> が掛ける)。 4 色の絵が暗い 4 色になるだけ (混色・ディザは足さない)。</item>
    /// </list>
    /// <para><b>見比べ用の口</b>: 決まったら片方を既定 (常に掛ける) にし、 この切り替え (<see cref="Current"/>・<see cref="Dim"/> を変える口) と
    /// QaBattle.SetReadability / SetBackdropDim は消す。 既定は <see cref="Mode.None"/> (作者が選ぶまで今の見え方を変えない)。</para>
    /// </summary>
    public static class BattleReadability
    {
        /// <summary>0 = 無し / 1 = A (縁取り) / 2 = B (背景を暗く) / 3 = A+B。</summary>
        public enum Mode { None = 0, Outline = 1, DimBackdrop = 2, Both = 3 }

        /// <summary>B の既定の明るさ (背景の画素に掛ける倍率)。</summary>
        public const float DefaultDim = 0.7f;

        /// <summary>今の直し方 (見比べ用・既定は無し)。</summary>
        public static Mode Current = Mode.None;
        private static float _dim = DefaultDim;
        /// <summary>B の明るさ (0.1〜1)。</summary>
        public static float Dim { get => _dim; set => _dim = Mathf.Clamp(value, 0.1f, 1f); }

        /// <summary>A (字・頭上の印の 2 ドットの縁取りと、 床の出目の縁) を掛けるか。</summary>
        public static bool Outline => (Current & Mode.Outline) != 0;
        /// <summary>B (背景を暗く) を掛けるか。</summary>
        public static bool DimBackdrop => (Current & Mode.DimBackdrop) != 0;
        /// <summary>今の背景に掛ける倍率 (B が切なら 1)。</summary>
        public static float BackdropMul => DimBackdrop ? _dim : 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = Mode.None; _dim = DefaultDim; }

        /// <summary>A の縁取り: 中心から (dx, dy) の所を打つか (半径 2・四隅を落とした 5×5 = 21 点。 字の形に沿って 2 ドット太る)。</summary>
        public static bool InRim2(int dx, int dy) => !(Mathf.Abs(dx) == 2 && Mathf.Abs(dy) == 2);
    }
}
