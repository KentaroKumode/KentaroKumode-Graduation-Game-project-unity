using System;

namespace GameLoop
{
    /// <summary>[計測] ランの集計 (ボス突入時の状態・〈門〉の 3 工程・リロールの成否・強盗・強奪/剛胆のドロップ)。 BOT のレポートだけが読む。 ゲームの結果は変えない。 2026-10-29 に GameManager から分離。</summary>
    public static class RunStats
    {
        /// <summary>[計装] ボス突入時の状態を層別に積む (index 0 = 1層)。
        /// <b>「休憩で HP は戻るのに突破率が落ちる」の正体を掴むため。</b>
        /// 戻らない資源 (物資・消耗品) が消えているなら、 ここに差が出る。</summary>
        public static readonly long[] BossEntryCount = new long[9];
        public static readonly long[] BossEntryProvision = new long[9];
        public static readonly long[] BossEntryProvisionTierSum = new long[9];  // 0=平穏..3=絶望
        public static readonly long[] BossEntryConsumables = new long[9];
        public static readonly long[] BossEntryHpPct = new long[9];
        public static readonly long[] BossEntryPassives = new long[9];
        /// <summary>ボス突入時の装備力 (AutoTest.InventoryPower)。 **戦闘力そのもの。**</summary>
        public static readonly long[] BossEntryPower = new long[9];

        public static void ResetBossEntryStats()
        {
            Array.Clear(BossEntryCount, 0, BossEntryCount.Length);
            Array.Clear(BossEntryProvision, 0, BossEntryProvision.Length);
            Array.Clear(BossEntryProvisionTierSum, 0, BossEntryProvisionTierSum.Length);
            Array.Clear(BossEntryConsumables, 0, BossEntryConsumables.Length);
            Array.Clear(BossEntryHpPct, 0, BossEntryHpPct.Length);
            Array.Clear(BossEntryPassives, 0, BossEntryPassives.Length);
            Array.Clear(BossEntryPower, 0, BossEntryPower.Length);
        }

        /// <summary>[計装] 〈門〉の 3 工程の結果 (2026-09-14)。
        ///
        /// <b>「払った」と「払えなかった」を分ける。</b> BOT へ accept=true を渡しても、
        /// 資源が足りなければ拒否と同じ扱いで欠陥が付く。 これを分けないと
        /// 「払うアーム」の結果が<b>払えた率と代償の重さの混合</b>になり、
        /// どちらが効いているのか読めない。
        ///
        /// <c>GateProvisionBefore/After</c> は取り置き方策 (<c>AutoRunner.gateProvisionReserveFromFloor</c>)
        /// が効いているかの検算用。 払った後の物資が 45 (悲観・上限恒久ロック) を
        /// 割っていないかをここで見る。</summary>
        public static long GateReached;

        public static long GateBloodPaid, GateRelicsPaid, GateTransferPaid;

        public static long GateProvisionBefore, GateProvisionAfter;

        public static long GateMaxHpPaid, GateRelicsBurned;

        public static void ResetGateStats()
        {
            GateReached = 0;
            GateBloodPaid = GateRelicsPaid = GateTransferPaid = 0;
            GateProvisionBefore = GateProvisionAfter = 0;
            GateMaxHpPaid = GateRelicsBurned = 0;
        }

        /// <summary>[計装 2026-09-17] リロールが不発になった理由。
        /// 0=フェーズ不一致 / 1=Instance無し / 2=TryReroll が false / 3=成功。
        /// **余剰リロールの 21.6% が「失敗」で抜けており、 そのとき残金 224.5G もある。**</summary>
        public static readonly long[] RerollOutcome = new long[4];

        public static void ResetRerollOutcome() { System.Array.Clear(RerollOutcome, 0, 4); }

        /// <summary>[計装 2026-09-12] 値下げ交渉の勝敗と戦利品総数。 試行回数は
        /// <c>ShopManager.RobberyAttempts</c>。 <b>強盗は 1 ラン 1 回まで</b>なので
        /// 試行数 = 実行したラン数。 static はドメインリロードで消えるのでバッチ開始時に 0 へ戻すこと。</summary>
        public static long RobberyWins, RobberyLosses, RobberyLootTotal;

        /// <summary>強盗の戦利品のうち **実際に所持へ入った数**。 RobberyLootTotal は試行回数。</summary>
        public static long RobberyLootApplied;

        /// <summary>[計装 2026-09-13] 強奪トラックの確率ドロップ発生数 (バッチ内累積)。</summary>
        public static long PlunderDrops;

        /// <summary>[計装] 〈剛胆〉のエリート追加ドロップ数。</summary>
        public static long ValorEliteDrops;

        /// <summary>[計装] エリート戦に入った回数 (格上げが効いているかを見る)。</summary>
        public static long ValorEliteFights;
    }
}
