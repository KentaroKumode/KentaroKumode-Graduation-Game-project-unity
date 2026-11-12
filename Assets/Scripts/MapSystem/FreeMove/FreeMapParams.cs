namespace MapSystem.FreeMove
{
    /// <summary>
    /// 航行マップの数値 (ターン制・2026-10-18)。 参照実装は docs/design/map-grid-prototype.html の <c>const P = {...}</c> ──
    /// 値はそのまま写してある。 正本は docs/GAME.md §5。 数値は全部仮置き (動かす時は採否を取ってから測定する)。
    ///
    /// <para><b>UnityEngine に依存しない。</b> この名前空間の型は素の C# で、 乱数は
    /// <see cref="GameLoop.GameRng"/> のキー付き抽選だけを使う (同じシード・同じ操作なら同じ結果)。</para>
    ///
    /// <para>連続時間の自由移動 (刻み・速さ・道の外の移動・照明の継続・使い魔の巡航ミサイル) は 2026-10-18 に廃止した。
    /// 移動は<b>道でつながった隣の点へ 1 歩</b>。 自分が 1 歩動くと追跡者が 1 度動く。 戦闘・イベントはターンを使わない。</para>
    /// </summary>
    public static class FreeMapParams
    {
        // ── 層の広さ・生成 ─────────────────────────────────────
        public const float Width = 16.4f;
        public const float Height = 9.4f;
        public const float StartX = 0.5f;

        // ── 危険の帯 (2026-11-04) ──────────────────────────────
        //   層を左右で 3 つの帯に分ける。 始点 (左端) 側が安全、 終点 (右端・ボス) 側が危険。
        //   安全: 脅威度の低い敵だけ・エリートと 2 体戦を置かない ／ 中間: 今どおり ／ 危険: 脅威度の低い敵を外す。 敵の数値は変えない (倍率なし)。
        public const int BandSafe = 0, BandMiddle = 1, BandDanger = 2;

        /// <summary>点の横の位置 → 帯。 始点の x を 0・終点の x を 1 とした割合で、 1/3 と 2/3 で切る。</summary>
        public static int BandOf(float x)
        {
            float t = (x - StartX) / (Width - 0.5f - StartX);
            return t < 1f / 3f ? BandSafe : t < 2f / 3f ? BandMiddle : BandDanger;
        }
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
        /// <para>2026-11-04: 10 → 50。 戦闘が物資を生まなくなった (下の補給) のと合わせて、 寄り道が物資の赤字になるように。
        /// 以前は戦闘 1 回 (+30) で 3 歩ぶんが返り、 戦うほど物資が増えて毎層マップを取り尽くしていた。</para>
        /// <para>2026-11-09: デノミで 50 → 1 (1 歩 = 1)。</para>
        public const int MoveSupply = 1;
        /// <summary>物資が足りない分は HP で払う。 物資 1 あたりの HP (2026-11-09 デノミ: 旧「物資 10 = HP 1」と同じ重さ)。</summary>
        public const int HpPerSupply = 5;
        /// <summary>削る砂の前線の内側で、 1 歩に物資 +1 が追加でかかる確率 (2026-11-09: 旧「1.5 倍」をデノミ後の 1 で表す)。</summary>
        public const float SandExtraChance = 0.5f;

        // ── 索敵 ───────────────────────────────────────────────
        /// <summary>何もしていない時に見える範囲 (道で何歩先まで)。</summary>
        public const int Vision = 1;
        public const int FlareRange = 3;
        public const int FlareTurns = 3;
        public const int FlareSupply = 1;
        /// <summary>〈補給線〉(〈兵站〉r10) で 1 回跳ぶ物資。</summary>
        public const int RelaySupply = 1;
        public const int ReconRange = 2;
        /// <summary>魔石の反応の段: これ以下の歩数が「かなり近い」「遠い」。 それより外は反応なし。</summary>
        public const int StoneNear = 2;
        public const int StoneFar = 4;

        // ── 追跡者 ─────────────────────────────────────────────
        /// <summary>補足 (察知) の歩数。 照明中は <see cref="FlareRange"/>。</summary>
        public const int DetectRange = 2;
        /// <summary>このターン数だけ補足できなければ見失う。</summary>
        public const int QuietLose = 3;
        /// <summary>召喚者 (動かない) の補足の歩数 (2026-11-04)。 動けない代わりに遠くから気づく。</summary>
        public const int SummonerDetectRange = 5;
        /// <summary>追跡の上限 (2026-11-04)。 このターン数だけ追い続けたら諦めて巡回へ戻る。
        /// 同じ速さの追跡者は、 下がり続けるプレイヤーと永遠に距離が保たれてしまうため。</summary>
        public const int ChaseGiveUp = 8;
        /// <summary>諦めた後、 このターン数のあいだは補足しない (すぐ補足し直して上限が意味を失わないように)。</summary>
        public const int ChaseRest = 4;
        /// <summary>使い魔がプレイヤーを見つける歩数。</summary>
        public const int FamiliarSee = 1;
        /// <summary>召喚者が使い魔を放つ間隔 (ターン)。</summary>
        public const int SummonEvery = 3;
        /// <summary>痕跡が残るターン。</summary>
        public const int TraceLife = 4;

        // ── 前線 (天候) ────────────────────────────────────────
        /// <summary>前線が進む間隔 (ターン) と、 1 回に進む距離 (層の広さ 16.4 のうち)。 前線は「現象の届く範囲」で罰は無い。</summary>
        public const int FrontEvery = 5;
        public const float FrontStep = 2.4f;

        // ── 嵐 (2026-11-13・前線の置き換え・<see cref="Storm"/>) ─────────
        //   数値は全部最初の置き値 (動かす時は採否を取ってから測定する)。 測定の口 GameHooks.StormEnabled = False では読まない (前線のまま)。
        /// <summary>静かな時間 (嵐が来るまでの手番)。 深さ 6 (画面の第 4 層) は 0 (<see cref="Storm.QuietTurns"/>)。</summary>
        public const int StormQuietTurns = 20;
        /// <summary>フェーズ 1〜3 の長さ (手番)。 フェーズ 4 はボスに入るまで続く。</summary>
        public const int StormPhase1Turns = 10, StormPhase2Turns = 10, StormPhase3Turns = 5;
        /// <summary>フェーズ 1 / 2 の終わりの輪の半径 (全部の点を含む半径に対する割合)。</summary>
        public const double StormPhase1EndRadius = 0.6, StormPhase2EndRadius = 0.3;
        /// <summary>フェーズ 2 の終わりの輪の中心を、 ボスの点からずらす長さ (フェーズ 2 の終わりの半径に対する割合の幅)。 1 未満なので、 ボスの点は輪の中に残る。</summary>
        public const double StormOffsetMin = 0.3, StormOffsetMax = 0.7;
        /// <summary>嵐の中の点へ動く (・嵐の中で待つ / 偵察する) たびに払う HP。</summary>
        public const int StormHp = 3;
        /// <summary>落石のような雹の嵐: 嵐の中の点への 1 歩の HP の追加。</summary>
        public const int StormHailHp = 1;
        /// <summary>削る砂の嵐: 嵐の中の点への 1 歩の物資の追加 (確定・乱数を引かない)。</summary>
        public const int StormSandSupply = 1;
        /// <summary>朱の雪の嵐: 嵐の中の点への 1 歩にかかる手番。</summary>
        public const int StormSnowTurns = 2;
        /// <summary>嵐の中の点で始まった戦闘の敵の〈嵐〉: 敵の攻撃 +N (戦闘を通して)。</summary>
        public const int StormEnemyAttack = 3;
        /// <summary>嵐の層のボスの点 (内側から選ぶ・2026-11-13) の、 始点からの道の歩数の下限 (実装役の決定)。 満たす内側の点が無い層は 1 歩ずつ下げる。</summary>
        public const int StormBossMinHops = 5;
        /// <summary>外周の層 (凸包) の辺から、 これ以内の直線距離にある点も外周として扱う (辺にほぼ乗っていて見た目で外周に見える点・2026-11-13・点の最小間隔 1.15 の約 1/4)。</summary>
        public const double StormOuterEdgeSlack = 0.3;

        // ── 縄張り ─────────────────────────────────────────────
        public const int TerritoryMin = 7;
        public const int TerritoryMax = 11;
        /// <summary>縄張りの中心を置ける x の範囲 (層の中ほど)。</summary>
        public const float TerritoryCenterMinX = 3.2f;
        public const float TerritoryCenterMarginRight = 2.8f;
        /// <summary>縄張りに入れない始点からの距離。</summary>
        public const float TerritoryStartClearance = 3f;
        /// <summary>縄張りを避ける道のために縄張りを削った時の下限 (2026-11-13・<see cref="TerritoryBypass"/>)。 これに満たなければ、
        /// 縄張りに隣り合う点を中心に近い順に足す。 削らなかった層には効かない (生成の 3〜11 点のまま)。 仮置き。</summary>
        public const int TerritoryTrimMin = 5;
        /// <summary>削って足しても、 縄張りがこれに満たなければ、 その層には追跡者を置かない (2026-11-13)。 生成が受け入れる最小 (3 点) と同じ。</summary>
        public const int TerritoryHardMin = 3;

        // ── 休憩 ───────────────────────────────────────────────
        /// <summary>多めに回復する休憩 (+1 ターン) の回復割合。 通常は層の修飾 (restHealMultiplier・既定 0.3) で 0 ターン。</summary>
        public const float RestMoreRatio = 0.6f;
        public const int RestMoreTurns = 1;

        // ── 逃げる ─────────────────────────────────────────────
        public const int FleeProvision = 2;
        /// <summary>追跡者の戦闘から逃げた後、 追跡が始まるまでのターン。</summary>
        public const int FleeFreezeTurns = 2;

        // ── 囮罠 (囮と罠を 1 つに統合・2026-10-18) ─────────────
        /// <summary>置いた点が残るターン。 過ぎたら効果が切れる。</summary>
        public const int LureLife = 8;
        /// <summary>追跡者がこの歩数以内に居れば、 置いた瞬間に気を引かれる (警戒して向かう)。</summary>
        public const int LureNotice = 6;
        /// <summary>踏んだ追跡者を足止めするターン。</summary>
        public const int LureHoldTurns = 2;
        public const int ShopLurePrice = 8;
        public const int StartLures = 2;

        // ── 物資の補給 ─────────────────────────────────────────
        /// <summary>通常戦・エリート戦の物資 (2026-11-04: 30 / 80 → 0)。 物資は店で買って補給する。
        /// 戦闘が物資を生むと、 戦うほど探索が延びて金・品・戦闘数が全部膨らんだ。</summary>
        public const int RewardBattle = 0;
        public const int RewardElite = 0;
        public const int RewardBoss = 3;
        /// <summary>追跡者を倒した時の物資 (2026-11-12・#35: 6 → 3)。</summary>
        public const int RewardPursuer = 3;
        /// <summary>前哨基地の物資。</summary>
        public const int RewardOutpost = 4;
        /// <summary>ショップの物資 1 の値段 (G)。 2026-11-04 の「1 包 100 = 4G」を 2026-11-09 のデノミで 1 = 2G に。</summary>
        public const int ProvisionPrice = 2;
    }
}
