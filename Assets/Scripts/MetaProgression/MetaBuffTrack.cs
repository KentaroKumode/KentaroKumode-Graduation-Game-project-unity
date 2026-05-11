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
        public const int TotalSteps = 115;

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

            // ===== 序盤 (Lv 1-30) =====
            Hp(); Hp(); Hp(); Gold(); Hp(); Hp(); Gold(); Hp(); Hp();   // 1-9
            Major(MetaBuffKind.StartMaterial);                           // 10
            Hp(); Hp(); Hp(); Hp(); Hp(); Gold(); Hp(); Hp(); Gold();    // 11-19
            Major(MetaBuffKind.CombatGoldBonus);                         // 20
            Hp(); Hp(); Hp(); Gold();                                    // 21-24
            Major(MetaBuffKind.DamageReduce);                            // 25
            Hp(); Hp(); Hp(); Gold();                                    // 26-29
            Major(MetaBuffKind.BossExtraNormal);                         // 30

            // ===== 中盤 (Lv 31-70) =====
            Hp(); Hp(); Hp(); Hp(); Gold(); Hp(); Hp(); Hp(); Gold();    // 31-39
            Major(MetaBuffKind.StartMaterial);                           // 40
            Hp(); Hp(); Hp(); Hp(); Gold(); Hp(); Hp(); Hp(); Gold();    // 41-49
            Major(MetaBuffKind.RefundLevelUp);                           // 50 (5%)
            Hp(); Hp(); Hp(); Hp();                                      // 51-54
            Major(MetaBuffKind.HungerReduce);                            // 55
            Hp(); Hp(); Hp(); Gold();                                    // 56-59
            Major(MetaBuffKind.CombatGoldBonus);                         // 60
            Hp(); Hp(); Hp(); Hp();                                      // 61-64
            Major(MetaBuffKind.DiceTotal);                               // 65
            Hp(); Hp(); Hp(); Gold();                                    // 66-69
            Major(MetaBuffKind.CritLevelUp);                             // 70 (+1)

            // ===== 終盤 (Lv 71-115) =====
            Hp(); Hp(); Hp(); Hp(); Gold(); Hp(); Hp(); Hp(); Gold();    // 71-79
            Major(MetaBuffKind.DamageReduce);                            // 80 (max)
            Hp(); Hp(); Hp(); Gold();                                    // 81-84
            Major(MetaBuffKind.RefundLevelUp);                           // 85 (10%)
            Hp(); Hp(); Hp(); Gold();                                    // 86-89
            Major(MetaBuffKind.HungerReduce);                            // 90
            Add(MetaBuffKind.CombatGoldBonus);                           // 91 (3個目=最大)
            Hp(); Hp(); Gold();                                          // 92-94
            Major(MetaBuffKind.StartMaterial);                           // 95 (max)
            Hp(); Hp(); Gold();                                          // 96-98
            Gold();                                                      // 99（旧 CombatGoldBonus、+3 cap で代替）
            Major(MetaBuffKind.CritLevelUp);                             // 100 (+2)
            Add(MetaBuffKind.DiceTotal);                                 // 101
            Gold();                                                      // 102
            Hp();                                                        // 103（旧 CombatGoldBonus、+3 cap のため削除）
            Add(MetaBuffKind.DiceTotal);                                 // 104
            Major(MetaBuffKind.HungerReduce);                            // 105 (max)
            Add(MetaBuffKind.DiceTotal);                                 // 106
            Hp(); Gold();                                                // 107-108
            Add(MetaBuffKind.DiceTotal);                                 // 109 (max)
            Major(MetaBuffKind.BossExtraRare);                           // 110 (final big skill 1)
            Hp(); Hp();                                                  // 111-112
            Major(MetaBuffKind.RefundLevelUp);                           // 113 (15%, final)
            Hp();                                                        // 114 (HP max)
            Major(MetaBuffKind.CritLevelUp);                             // 115 (+3, final)

            // 件数チェック（115 でなければ設計ミス）
            if (list.Count != TotalSteps)
                UnityEngine.Debug.LogError($"[MetaBuffTrack] step 数が不正: {list.Count} / {TotalSteps}");

            return list;
        }
    }
}
