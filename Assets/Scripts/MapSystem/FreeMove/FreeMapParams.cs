namespace MapSystem.FreeMove
{
    /// <summary>
    /// 自由移動マップの数値。 正本は docs/GAME.md §5 (仮置きの由来は §23-21)。
    /// 参照実装は docs/design/map-prototype.html の <c>const P = {...}</c> ── 値はそのまま写してある。
    ///
    /// <para><b>UnityEngine に依存しない。</b> この名前空間の型はすべて素の C# で、
    /// 乱数は <see cref="GameLoop.GameRng"/> のキー付き抽選だけを使う (同じシード・同じ操作なら同じ結果)。</para>
    ///
    /// <para>小数の定数は <c>float</c> リテラルで持ち、 計算の途中だけ double に上げる
    /// (CLAUDE.md「小数の定数は生成時に float で確定」)。</para>
    /// </summary>
    public static class FreeMapParams
    {
        // ── 時間 ───────────────────────────────────────────────
        /// <summary>1 刻み ＝ 0.1 手番。 中身はこの刻みでしか進まない (表示の実時間とは切り離す)。</summary>
        public const float Tick = 0.1f;
        /// <summary>1 手番あたりの刻み数。 手番の処理 (察知・痕跡・囮) はこの周期で走る。</summary>
        public const int TicksPerTurn = 10;
        /// <summary>順速で 1 刻みにかける実時間 (秒)。 1 手番 ≒ 6 秒 (2026-09-28 ユーザー指定)。</summary>
        public const float SecondsPerTickNormal = 0.6f;
        /// <summary>早送りで 1 刻みにかける実時間 (秒)。 順速の 10 倍。</summary>
        public const float SecondsPerTickFast = 0.06f;

        // ── 層の広さ・生成 ─────────────────────────────────────
        public const float Width = 11.6f;
        public const float Height = 6.6f;
        public const float StartX = 0.5f;
        public const float StartY = 3.3f;
        /// <summary>層の点の数 (始点・終点を含む)。 §23-21「25〜30」。</summary>
        public const int NodeCount = 28;
        /// <summary>点どうしの最小間隔。</summary>
        public const float MinNodeSpacing = 1.15f;
        /// <summary>最小全域木の後に足す道の、 長さの上限と採用率。</summary>
        public const float ExtraRoadMaxLen = 1.9f;
        public const float ExtraRoadChance = 0.45f;
        /// <summary>生成のやり直し上限 (山岳で分断された層は引き直す)。</summary>
        public const int GenerateAttempts = 40;

        // ── 移動の費用 (物資 / 距離) ──────────────────────────
        public const int CostRoad = 10;
        public const int CostOffRoad = 20;
        public const int CostFlare = 5;
        /// <summary>払底中 (物資 0) の HP / 距離。 道の外は 2。</summary>
        public const int HpPayRoad = 1;
        public const int HpPayOffRoad = 2;

        // ── 索敵 ───────────────────────────────────────────────
        public const float VisionNone = 1f;
        public const float VisionFlare = 5f;
        public const float VisionStone = 4f;
        public const float VisionScout = 3f;
        public const int ScoutTurns = 2;
        /// <summary>魔石の「かなり近い」の距離 (これ以下が 1〜2)。</summary>
        public const float StoneNearDist = 2f;

        // ── 気配と察知 ─────────────────────────────────────────
        public const int KehaiRoad = 2;
        public const int KehaiBattle = 2;
        public const int BattleKehaiTurns = 2;
        public const int DetectBase = 2;
        /// <summary>照明なしの察知距離の上限 (＝魔石の範囲。 予兆なしに見つかることがない)。</summary>
        public const int DetectCap = 4;
        public const float BattleNoise = 4f;
        public const float CacheNoise = 3f;

        // ── 徘徊エネミー ───────────────────────────────────────
        public const float FoePatrolSpeed = 0.5f;
        public const float FoeChaseSpeed = 1.0f;
        public const int FoeLostTurns = 3;
        public const float CatchDist = 0.45f;
        /// <summary>巡回中の敵が痕跡に気づく距離。</summary>
        public const float TrackNoticeDist = 0.6f;
        /// <summary>縄張りの点の数 (6〜9)。</summary>
        public const int TerritoryMin = 6;
        public const int TerritoryMax = 9;
        /// <summary>縄張りの中心を置ける x の範囲 (層の中ほど)。</summary>
        public const float TerritoryCenterMinX = 3.2f;
        public const float TerritoryCenterMarginRight = 2.8f;
        /// <summary>縄張りに入れない始点からの距離。 試作には無い (2026-09-28 本実装で追加) ──
        /// 縄張りが始点の 1〜2 距離まで伸びる層があり、 前哨基地で立っているだけで察知された。</summary>
        public const float TerritoryStartClearance = 3f;

        // ── 痕跡 ───────────────────────────────────────────────
        public const int TrackRoad = 3;
        public const int TrackOffRoad = 1;

        // ── 逃げる ─────────────────────────────────────────────
        public const int FleeProvision = 100;
        /// <summary>徘徊エネミーの連戦から逃げた後、 追跡が始まるまでの手番。</summary>
        public const int FleeFreezeTurns = 2;

        // ── 囮・罠 ─────────────────────────────────────────────
        public const int DecoyTurns = 6;
        public const int DecoyDelay = 1;
        public const float DecoyNoise = 4f;
        public const float DecoyBreak = 1.0f;
        public const int TrapFreezeTurns = 1;
        public const float TrapHitDist = 0.35f;
        public const int ShopDecoyPrice = 6;
        public const int ShopTrapPrice = 8;
        public const int StartDecoys = 1;
        public const int StartTraps = 1;

        // ── 物資の補給 ─────────────────────────────────────────
        public const int RewardBattle = 30;
        public const int RewardElite = 80;
        public const int RewardBoss = 150;
        /// <summary>徘徊エネミー (連戦) を倒した時の物資。</summary>
        public const int RewardGauntlet = 300;
        /// <summary>補給庫 (拠) を漁った時の物資。</summary>
        public const int RewardCache = 200;
        /// <summary>前哨基地の物資。</summary>
        public const int RewardOutpost = 200;
        /// <summary>ショップで 1G ＝ 10 物資。</summary>
        public const int ProvisionPerGold = 10;
    }

    /// <summary>移動の速さ。 §23-21「忍び足 / 通常 / 急ぐ」。 移動中も切り替えられる。</summary>
    public enum MoveSpeed
    {
        /// <summary>時間 2 倍・道の上でも気配 0。</summary>
        Sneak = 0,
        Normal = 1,
        /// <summary>時間半分・物資 1.5 倍・気配 +1。</summary>
        Hurry = 2,
    }

    public static class MoveSpeedExt
    {
        /// <summary>距離 1 あたりの手番。</summary>
        public static float TimeMul(this MoveSpeed s)
            => s == MoveSpeed.Sneak ? 2f : s == MoveSpeed.Hurry ? 0.5f : 1f;

        /// <summary>物資の倍率。</summary>
        public static float CostMul(this MoveSpeed s) => s == MoveSpeed.Hurry ? 1.5f : 1f;

        /// <summary>移動中の気配 (道の上の +2 を置き換える)。</summary>
        public static int Kehai(this MoveSpeed s, bool onRoad)
        {
            if (s == MoveSpeed.Sneak) return 0;
            int k = onRoad ? FreeMapParams.KehaiRoad : 0;
            return s == MoveSpeed.Hurry ? k + 1 : k;
        }

        /// <summary>1 刻みで進む距離。 順速 0.1・忍び足 0.05・急ぐ 0.2。</summary>
        public static double StepPerTick(this MoveSpeed s) => (double)FreeMapParams.Tick / s.TimeMul();

        public static string DisplayName(this MoveSpeed s)
            => s == MoveSpeed.Sneak ? "忍び足" : s == MoveSpeed.Hurry ? "急ぐ" : "通常";
    }
}
