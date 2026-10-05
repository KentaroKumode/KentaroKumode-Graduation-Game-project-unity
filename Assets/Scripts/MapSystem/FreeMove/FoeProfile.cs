namespace MapSystem.FreeMove
{
    /// <summary>
    /// 徘徊エネミーの性質 (2026-09-28)。 層ごとに 1 つ引く。 正本は docs/GAME.md §5-9。
    ///
    /// <para>性質は「特性の組」で持つ ── 追跡中の速さ・見失うまでの手番・追跡中は魔石に反応しない・
    /// 近ければ道を外れて飛び掛かる・使い魔 (巡航ミサイル) を放つ。 新しい性質は特性の組み合わせで足す。</para>
    ///
    /// <para><b>プレイヤーには性質が明かされない</b>（見え方から推理する）。 BOT も読まない。</para>
    ///
    /// <para>数値は全部仮置き。 動かす時は採否を取ってから測定する (§23-21)。</para>
    /// </summary>
    public sealed class FoeProfile
    {
        public string id;
        public string displayName;
        /// <summary>巡回の速さ (距離 / 手番)。</summary>
        public float patrolSpeed = FreeMapParams.FoePatrolSpeed;
        /// <summary>警戒・追跡の速さ (距離 / 手番)。</summary>
        public float chaseSpeed = FreeMapParams.FoeChaseSpeed;
        /// <summary>この手番数だけ察知できなければ見失う。</summary>
        public int lostTurns = FreeMapParams.FoeLostTurns;
        /// <summary>追跡中 (足止め中も) は魔石に一切反応しない。 察知された瞬間の合図 (Detected) も出ない。</summary>
        public bool silentWhileChasing;
        /// <summary>追跡中、 プレイヤーがこの距離以内なら道を外れて直進する (0 = しない)。 離れたら最寄りの点から道へ戻る。</summary>
        public float lungeRange;
        /// <summary>同時に飛ばせる使い魔の数 (0 = 放たない)。 察知した瞬間に 1 体放つ。</summary>
        public int familiarMax;
        /// <summary>使い魔を放ってから次を放てるまでの手番。</summary>
        public int familiarCooldownTurns = FreeMapParams.FamiliarCooldownTurns;

        public bool LaunchesFamiliars => familiarMax > 0;

        public FoeProfile Clone() => (FoeProfile)MemberwiseClone();

        // ── 既定の性質 ────────────────────────────────────────
        /// <summary>徘徊者: 基準。 5 層以降は使い魔を 1 体放つ (<see cref="ForFloor"/>)。</summary>
        public static FoeProfile Standard => new FoeProfile { id = "standard", displayName = "徘徊者" };

        /// <summary>猟犬: 追跡中の速さ 1.6・見失うまで 5 手番。 振り切るのに急ぐが要る。</summary>
        public static FoeProfile Hound => new FoeProfile { id = "hound", displayName = "猟犬", chaseSpeed = 1.6f, lostTurns = 5 };

        /// <summary>影: 追跡中は魔石に反応しない (察知された合図も出ない)。 追われていると気づいた時には近い。</summary>
        public static FoeProfile Stalker => new FoeProfile { id = "stalker", displayName = "影", chaseSpeed = 1.2f, silentWhileChasing = true };

        /// <summary>使役者: 使い魔を 2 体まで・4 手番ごとに放つ。 本体は遅い。</summary>
        public static FoeProfile Summoner => new FoeProfile
        { id = "summoner", displayName = "使役者", chaseSpeed = 0.8f, familiarMax = 2, familiarCooldownTurns = 4 };

        /// <summary>飛び掛かり: 追跡中、 距離 2.5 以内なら道を外れて直進する。 道の外へ逃げても詰められる。</summary>
        public static FoeProfile Pouncer => new FoeProfile { id = "pouncer", displayName = "飛び掛かり", chaseSpeed = 1.4f, lungeRange = 2.5f };

        /// <summary>その層で引ける性質。 3 層は穏やかなものだけ (使役者は使い魔の顔見せ)。</summary>
        public static FoeProfile[] PoolFor(int floor)
            => floor <= 3
                ? new[] { Standard, Hound, Stalker, Summoner }
                : new[] { Standard, Hound, Stalker, Summoner, Pouncer };

        public const string RngKey = "map.free.profile";

        /// <summary>層の性質を引く (GameRng のキー <see cref="RngKey"/>・連番 = 層)。 5 層以降は使い魔を持たない性質にも
        /// 1 体ぶんの使い魔を持たせる ── 深い層の徘徊エネミーはどれも巡航ミサイルを撃てる。</summary>
        public static FoeProfile ForFloor(int floor)
        {
            var pool = PoolFor(floor);
            var p = pool[GameLoop.GameRng.Range(0, pool.Length, RngKey, floor)];
            if (floor >= 5 && p.familiarMax == 0) p.familiarMax = 1;
            return p;
        }
    }
}
