using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// バフトラックの 115段定義 + コスト式。コードで直接定義する（SO は使わない）。
    /// レイアウトは設計表に基づく:
    ///   序盤(1-30) / ミッド(31-70) / 終盤(71-115) で大スキルを区切り位置に配置。
    /// </summary>
    public static class MetaBuffTrack
    {
        public const int TotalSteps = 59;

        private static readonly List<MetaBuffStep> steps = BuildSteps();

        public static IReadOnlyList<MetaBuffStep> Steps => steps;

        /// <summary>1-indexed で取得（level=1〜115）。範囲外は null。</summary>
        public static MetaBuffStep Get(int level)
            => (level >= 1 && level <= steps.Count) ? steps[level - 1] : null;

        /// <summary>level 番目の段階を購入するためのトークンコスト。</summary>
        public static int CalcCost(int level) => 30 + level * 2;

        // ============================================================
        //  レイアウト
        // ============================================================

        private static List<MetaBuffStep> BuildSteps()
        {
            var list = new List<MetaBuffStep>(TotalSteps);

            // ヘルパー
            void Add(MetaBuffKind k, int amount = 1, bool major = false)
                => list.Add(new MetaBuffStep { kind = k, amount = amount, isMajor = major });
            void Hp() => Add(MetaBuffKind.Hp);
            void Gold() => Add(MetaBuffKind.Gold);
            void Major(MetaBuffKind k, int amount = 1) => Add(k, amount, true);

            // 設計サマリ:
            //   HP 30, Gold 5, SM 3, CGB 2, DR 2, DT 1, CL 2, RL 3, HR 3,
            //   BEN 1, BER 1, DP 1, SPI 1, FCH 2, TCG 1 → 計 58
            //   (HP ナーフ: 60→30。出力面ナーフ: DT 3→1, CL 3→2, CritDamageBoost 撤廃)
            //
            // ===== 序盤 (Lv 1-20) =====
            Hp(); Hp(); Gold();                                          // 1-3
            Major(MetaBuffKind.FloorClearHeal);                          // 4 (+1)
            Hp(); Hp(); Hp();                                            // 5-7
            Major(MetaBuffKind.StartMaterial);                           // 8 (1st)
            Hp(); Hp(); Hp();                                            // 9-11
            Major(MetaBuffKind.CombatGoldBonus);                         // 12 (1st)
            Hp(); Hp(); Hp();                                            // 13-15
            Major(MetaBuffKind.DamageReduce);                            // 16 (-1)
            Hp(); Gold(); Hp();                                          // 17-19
            Major(MetaBuffKind.BossExtraNormal);                         // 20

            // ===== ミッド (Lv 21-40) =====
            Hp(); Hp();                                                  // 21-22
            Major(MetaBuffKind.HungerReduce);                            // 23 (-1)
            Hp(); Hp();                                                  // 24-25
            Major(MetaBuffKind.StartMaterial);                           // 26 (2nd)
            Major(MetaBuffKind.ShopRobberyUnlock);                       // 27 (ミッド 大スキル: 値下げ交渉=強盗 解禁)
            Hp(); Hp();                                                  // 28-29
            Major(MetaBuffKind.CritLevelUp);                             // 30 (+1)
            Hp(); Gold(); Hp();                                          // 31-33
            Major(MetaBuffKind.RefundLevelUp);                           // 34 (5%)
            Hp();                                                        // 35
            Major(MetaBuffKind.StartMaterial);                           // 36 (max=3)
            Hp(); Hp();                                                  // 37-38
            Major(MetaBuffKind.CombatGoldBonus);                         // 39 (max=2)
            Hp(); Hp();                                                  // 40-41

            // ===== 終盤 (Lv 42-59) =====
            Major(MetaBuffKind.DiceTotal);                               // 42 (max=+1)
            Hp();                                                        // 43
            Major(MetaBuffKind.StartingPassiveItem);                     // 44
            Major(MetaBuffKind.TreasureChestGold);                       // 45
            Gold();                                                      // 46
            Major(MetaBuffKind.RefundLevelUp);                           // 47 (10%)
            Hp();                                                        // 48
            Major(MetaBuffKind.HungerReduce);                            // 49 (-2)
            Gold();                                                      // 50
            Major(MetaBuffKind.DamageReduce);                            // 51 (max=-2)
            Major(MetaBuffKind.FloorClearHeal);                          // 52 (max=+2)
            Major(MetaBuffKind.HungerReduce);                            // 53 (max=-3)
            Major(MetaBuffKind.DivineProtect);                           // 54
            Hp();                                                        // 55
            Major(MetaBuffKind.BossExtraRare);                           // 56
            Hp();                                                        // 57
            Major(MetaBuffKind.RefundLevelUp);                           // 58 (15%, max)
            Major(MetaBuffKind.CritLevelUp);                             // 59 (+2, max, final)

            // 件数チェック（TotalSteps でなければ設計ミス）
            if (list.Count != TotalSteps)
                UnityEngine.Debug.LogError($"[MetaBuffTrack] step 数が不正: {list.Count} / {TotalSteps}");

            return list;
        }
    }
}
