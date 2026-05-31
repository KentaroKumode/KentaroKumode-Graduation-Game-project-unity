namespace AutoTest
{
    /// <summary>
    /// L2 評価関数。 6F〜7Fを滑らかな勾配でブレンドした composite score。
    ///
    /// 設計:
    ///   ・bandScore (1〜12) を基礎値とする
    ///   ・6F到達 (band >= 9)        +0.5
    ///   ・7F到達 (band >= 10, R10+)  +1.0
    ///   ・実クリア (band >= 11, R11/R12) +2.0
    ///   ・解脱  (band == 12)        +0.5
    ///   ・CRASH/DEADLOCK は集計除外 (composite = NaN扱い、 caller がスキップ)
    ///
    /// 結果範囲:
    ///   R1a (1F死) = 1.0
    ///   R8  (5Fクリア) = 8.0
    ///   R8b (6Fクリア) = 8.5
    ///   R9  (6F死) = 9.5
    ///   R10 (7F死) = 11.5
    ///   R11 (7Fクリア) = 14.5
    ///   R12 (解脱) = 16.0
    ///
    /// → R10 vs R11 の差が 3.0 (大きく評価)、 R11 vs R12 の差が 1.5 (解脱ボーナス控えめ)
    /// → R8/R8b の差は 0.5 (些細)、 6F以降の進行段階差が大きく刻まれる
    /// </summary>
    public static class PolicyObjective
    {
        public const float Weight6F      = 0.5f;
        public const float Weight7F      = 1.0f;
        public const float WeightClear   = 2.0f;
        public const float WeightGedatsu = 0.5f;

        /// <summary>1ランの composite score を返す。 CRASH/DEADLOCK は float.NaN。</summary>
        public static float Compute(AutoRunner.RunRec r)
        {
            if (r == null) return float.NaN;
            int b = r.bandScore;
            if (b < 0) return float.NaN; // CRASH/DEADLOCK

            float s = b;
            if (b >= 9)  s += Weight6F;      // 6F以降の何らか
            if (b >= 10) s += Weight7F;      // 7F到達
            if (b >= 11) s += WeightClear;   // クリア
            if (b == 12) s += WeightGedatsu; // 解脱
            return s;
        }
    }
}
