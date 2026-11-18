namespace GameLoop
{
    /// <summary>
    /// 目の範囲と、 役の段 (Tier) の型だけを残した入れ物。
    ///
    /// <para><b>出目パーツ (面 36 種 × Tier・ADR-0011) は 2026-11-18 にやめた (ADR-0015)。</b>
    /// 賽を 1 個装備して 3 回振る形になり、 面は賽 (items.json の diceFaces) が持つ。 装備と面は <see cref="EquippedDie"/>。
    /// 店の出目パーツの枠・休憩で面を外す仕組み・facepart_N_tN の id も同じ日に消した。</para>
    ///
    /// <para><see cref="Tier"/> は役の判定 (<c>YachtRoles</c>・<c>YachtRoleEffects</c>) がまだ読んでいる。
    /// 今は全部の面が T3 (2 個そろいもゾロ目も効く・作者 p9) で渡る。 役の作り直し (ADR-0015 手順 2) で外す。</para>
    ///
    /// <para><b>効果は出目が確定した後にだけ働く。</b>「この面はワイルド」「別の目として扱う」は禁止 (ADR-0015 の 2 も同じ線)。</para>
    /// </summary>
    public static class DiceFaceParts
    {
        /// <summary>面になれる出目の範囲。</summary>
        public const int MinFace = 1;
        public const int MaxFace = 9;

        /// <summary>役の段。 <see cref="Tier.None"/> は「面を持たない」番兵（配列の既定値）。</summary>
        public enum Tier { None = 0, T1 = 1, T2 = 2, T3 = 3, T4 = 4 }

        /// <summary>最初の賽の面 (<see cref="EquippedDie.StartingFaces"/> と同じ)。 面が読めない文脈の既定値。</summary>
        public static int[] StartingFaces => EquippedDie.StartingFaces;
    }
}
