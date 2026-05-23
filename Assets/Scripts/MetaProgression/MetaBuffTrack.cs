using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// バフトラックの 115段定義 + コスト式。コードで直接定義する（SO は使わない）。
    /// レイアウトは設計表に基づく:
    ///   序盤(1-30) / 中盤(31-70) / 終盤(71-115) で大スキルを区切り位置に配置。
    /// </summary>
    public static class MetaBuffTrack
    {
        public const int TotalSteps = 58;

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

            // ===== 中盤 (Lv 21-40) =====
            Hp(); Hp();                                                  // 21-22
            Major(MetaBuffKind.HungerReduce);                            // 23 (-1)
            Hp(); Hp();                                                  // 24-25
            Major(MetaBuffKind.StartMaterial);                           // 26 (2nd)
            Hp(); Hp();                                                  // 27-28
            Major(MetaBuffKind.CritLevelUp);                             // 29 (+1)
            Hp(); Gold(); Hp();                                          // 30-32
            Major(MetaBuffKind.RefundLevelUp);                           // 33 (5%)
            Hp();                                                        // 34
            Major(MetaBuffKind.StartMaterial);                           // 35 (max=3)
            Hp(); Hp();                                                  // 36-37
            Major(MetaBuffKind.CombatGoldBonus);                         // 38 (max=2)
            Hp(); Hp();                                                  // 39-40

            // ===== 終盤 (Lv 41-58) =====
            Major(MetaBuffKind.DiceTotal);                               // 41 (max=+1)
            Hp();                                                        // 42
            Major(MetaBuffKind.StartingPassiveItem);                     // 43
            Major(MetaBuffKind.TreasureChestGold);                       // 44
            Gold();                                                      // 45
            Major(MetaBuffKind.RefundLevelUp);                           // 46 (10%)
            Hp();                                                        // 47
            Major(MetaBuffKind.HungerReduce);                            // 48 (-2)
            Gold();                                                      // 49
            Major(MetaBuffKind.DamageReduce);                            // 50 (max=-2)
            Major(MetaBuffKind.FloorClearHeal);                          // 51 (max=+2)
            Major(MetaBuffKind.HungerReduce);                            // 52 (max=-3)
            Major(MetaBuffKind.DivineProtect);                           // 53
            Hp();                                                        // 54
            Major(MetaBuffKind.BossExtraRare);                           // 55
            Hp();                                                        // 56
            Major(MetaBuffKind.RefundLevelUp);                           // 57 (15%, max)
            Major(MetaBuffKind.CritLevelUp);                             // 58 (+2, max, final)

            // 件数チェック（TotalSteps でなければ設計ミス）
            if (list.Count != TotalSteps)
                UnityEngine.Debug.LogError($"[MetaBuffTrack] step 数が不正: {list.Count} / {TotalSteps}");

            return list;
        }
    }
}
