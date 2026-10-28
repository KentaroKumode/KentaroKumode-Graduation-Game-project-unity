namespace MapSystem.FreeMove
{
    /// <summary>
    /// 追跡者の性質 (手番制・2026-10-18)。 層ごとに 1 つ引く。 参照実装は docs/design/map-grid-prototype.html の <c>PROFILES</c>。
    ///
    /// <para>追跡者は全員「巡回 → (痕跡に気づいて) 警戒 → (補足して) 追跡 → 見失う」を行い、
    /// 性質は<b>味変</b>として使い魔の有無・足の速さ・捕まった時の戦闘の重さだけを変える。
    /// 新しい性質は特性の組み合わせで足す。</para>
    ///
    /// <para><b>プレイヤーには性質が明かされない</b>（見え方から推理する）。 BOT も読まない。</para>
    ///
    /// <para>数値は全部仮置き。 動かす時は採否を取ってから測定する (§23-21)。</para>
    /// </summary>
    public sealed class FoeProfile
    {
        public string id;
        public string displayName;
        public string description;
        /// <summary>本体が 1 手番に進む歩数 (0 = 動かない)。</summary>
        public int foeSteps = 1;
        /// <summary>使い魔が 1 手番に進む歩数 (0 = 使い魔なし)。</summary>
        public int familiarSteps;
        /// <summary>補足している間、 一定間隔で使い魔を放つ。</summary>
        public bool summons;
        /// <summary>猟犬: 最初から居る足の速い使い魔。 倒しても消えず本体の元へ戻る。</summary>
        public bool hound;
        /// <summary>捕まった時の戦闘が重い。 追跡者の専用エネミーの強さに掛かる。</summary>
        public bool heavyFight;

        public bool HasFamiliars => summons || hound;

        public FoeProfile Clone() => (FoeProfile)MemberwiseClone();

        // ── 性質 ──────────────────────────────────────────────

        /// <summary>召喚者: 動かない。 補足すると数手ごとに使い魔を放つ。</summary>
        public static FoeProfile Summoner => new FoeProfile
        { id = "summoner", displayName = "召喚者", description = "動かない。補足すると数手ごとに使い魔を放つ",
          foeSteps = 0, familiarSteps = 1, summons = true };

        /// <summary>猟犬使い: 足の速い使い魔 (1 手に 2 歩) と 2 体で最短距離から追う。</summary>
        public static FoeProfile Houndmaster => new FoeProfile
        { id = "hound", displayName = "猟犬使い", description = "足の速い使い魔と2体で、最短距離から追ってくる",
          foeSteps = 1, familiarSteps = 2, hound = true };

        /// <summary>疾走者: 使い魔なし。 1 手番に 2 歩。</summary>
        public static FoeProfile Sprinter => new FoeProfile
        { id = "sprinter", displayName = "疾走者", description = "使い魔は使わないが、1手に2歩動く",
          foeSteps = 2 };

        /// <summary>重装: 使い魔なし。 1 手番に 1 歩だが、 捕まった時の戦闘が重い。</summary>
        public static FoeProfile Brute => new FoeProfile
        { id = "brute", displayName = "重装", description = "使い魔は使わない。捕まった時の戦闘が重い",
          foeSteps = 1, heavyFight = true };

        /// <summary>その層で引ける性質。 3 層は疾走者を外す (1 手に 2 歩は初見で振り切れない)。</summary>
        public static FoeProfile[] PoolFor(int floor)
            => floor <= 3
                ? new[] { Summoner, Houndmaster, Brute }
                : new[] { Summoner, Houndmaster, Sprinter, Brute };

        public const string RngKey = "map.free.profile";

        /// <summary>層の性質を引く (GameRng のキー <see cref="RngKey"/>・連番 = 層)。</summary>
        public static FoeProfile ForFloor(int floor)
        {
            var pool = PoolFor(floor);
            return pool[GameLoop.GameRng.Range(0, pool.Length, RngKey, floor)];
        }
    }
}
