namespace MapSystem.FreeMove
{
    /// <summary>
    /// 航行マップの数値 (手番制・2026-10-18)。 参照実装は docs/design/map-grid-prototype.html の <c>const P = {...}</c> ──
    /// 値はそのまま写してある。 正本は docs/GAME.md §5。 数値は全部仮置き (動かす時は採否を取ってから測定する)。
    ///
    /// <para><b>UnityEngine に依存しない。</b> この名前空間の型は素の C# で、 乱数は
    /// <see cref="GameLoop.GameRng"/> のキー付き抽選だけを使う (同じシード・同じ操作なら同じ結果)。</para>
    ///
    /// <para>連続時間の自由移動 (刻み・速さ・道の外の移動・照明の継続・使い魔の巡航ミサイル) は 2026-10-18 に廃止した。
    /// 移動は<b>道でつながった隣の点へ 1 歩</b>。 自分が 1 歩動くと追跡者が 1 度動く。 戦闘・イベントは手番を使わない。</para>
    /// </summary>
    public static class FreeMapParams
    {
        // ── 層の広さ・生成 ─────────────────────────────────────
        public const float Width = 16.4f;
        public const float Height = 9.4f;
        public const float StartX = 0.5f;
        /// <summary>層の点の数 (始点・終点を含む)。 試作どおり 50 (GAME.md の旧記述は 28)。</summary>
        public const int NodeCount = 50;
        /// <summary>点どうしの最小間隔。</summary>
        public const float MinNodeSpacing = 1.15f;
        /// <summary>最小全域木の後に足す道の、 長さの上限と採用率。</summary>
        public const float ExtraRoadMaxLen = 1.9f;
        public const float ExtraRoadChance = 0.45f;
        /// <summary>行き止まり (道 1 本) を潰す 2 段の到達距離。</summary>
        public const float DeadEndReach1 = 2.6f;
        public const float DeadEndReach2 = 4.0f;
        /// <summary>近道を足す対象の直線距離の上限と、 道だと何倍以上遠回りなら足すか。</summary>
        public const float ShortcutMaxLen = 2.2f;
        public const float ShortcutDetour = 2f;
        /// <summary>山岳の数 = MountainMin + [0, MountainExtra)。</summary>
        public const int MountainMin = 3;
        public const int MountainExtra = 3;
        /// <summary>生成のやり直し上限 (山岳で分断された層・行き止まりが残った層は引き直す)。</summary>
        public const int GenerateAttempts = 40;

        // ── 移動 ───────────────────────────────────────────────
        /// <summary>1 歩 (隣の点へ) の物資。 距離によらず一定。</summary>
        public const int MoveSupply = 10;
        /// <summary>物資が足りない分は HP で払う。 物資 10 あたりの HP。</summary>
        public const int SupplyPerHp = 10;

        // ── 索敵 ───────────────────────────────────────────────
        /// <summary>何もしていない時に見える範囲 (道で何歩先まで)。</summary>
        public const int Vision = 1;
        public const int FlareRange = 3;
        public const int FlareTurns = 3;
        public const int FlareSupply = 50;
        public const int ReconRange = 2;
        /// <summary>魔石の反応の段: これ以下の歩数が「かなり近い」「遠い」。 それより外は反応なし。</summary>
        public const int StoneNear = 2;
        public const int StoneFar = 4;

        // ── 追跡者 ─────────────────────────────────────────────
        /// <summary>補足 (察知) の歩数。 照明中は <see cref="FlareRange"/>。</summary>
        public const int DetectRange = 2;
        /// <summary>この手番数だけ補足できなければ見失う。</summary>
        public const int QuietLose = 3;
        /// <summary>使い魔がプレイヤーを見つける歩数。</summary>
        public const int FamiliarSee = 1;
        /// <summary>召喚者が使い魔を放つ間隔 (手番)。</summary>
        public const int SummonEvery = 3;
        /// <summary>痕跡が残る手番。</summary>
        public const int TraceLife = 4;

        // ── 前線 (天候) ────────────────────────────────────────
        /// <summary>前線が進む間隔 (手番) と、 1 回に進む距離 (層の広さ 16.4 のうち)。 前線は「現象の届く範囲」で罰は無い。</summary>
        public const int FrontEvery = 5;
        public const float FrontStep = 2.4f;

        // ── 縄張り ─────────────────────────────────────────────
        public const int TerritoryMin = 7;
        public const int TerritoryMax = 11;
        /// <summary>縄張りの中心を置ける x の範囲 (層の中ほど)。</summary>
        public const float TerritoryCenterMinX = 3.2f;
        public const float TerritoryCenterMarginRight = 2.8f;
        /// <summary>縄張りに入れない始点からの距離。</summary>
        public const float TerritoryStartClearance = 3f;

        // ── 休憩 ───────────────────────────────────────────────
        /// <summary>多めに回復する休憩 (+1 手番) の回復割合。 通常は層の修飾 (restHealMultiplier・既定 0.3) で 0 手番。</summary>
        public const float RestMoreRatio = 0.6f;
        public const int RestMoreTurns = 1;

        // ── 逃げる ─────────────────────────────────────────────
        public const int FleeProvision = 100;
        /// <summary>追跡者の戦闘から逃げた後、 追跡が始まるまでの手番。</summary>
        public const int FleeFreezeTurns = 2;

        // ── 囮罠 (囮と罠を 1 つに統合・2026-10-18) ─────────────
        /// <summary>置いた点が残る手番。 過ぎたら効果が切れる。</summary>
        public const int LureLife = 8;
        /// <summary>追跡者がこの歩数以内に居れば、 置いた瞬間に気を引かれる (警戒して向かう)。</summary>
        public const int LureNotice = 6;
        /// <summary>踏んだ追跡者を足止めする手番。</summary>
        public const int LureHoldTurns = 2;
        public const int ShopLurePrice = 8;
        public const int StartLures = 2;

        // ── 物資の補給 ─────────────────────────────────────────
        public const int RewardBattle = 30;
        public const int RewardElite = 80;
        public const int RewardBoss = 150;
        /// <summary>追跡者を倒した時の物資。</summary>
        public const int RewardPursuer = 300;
        /// <summary>前哨基地の物資。</summary>
        public const int RewardOutpost = 200;
        /// <summary>ショップで 1G ＝ 10 物資。</summary>
        public const int ProvisionPerGold = 10;
    }
}
