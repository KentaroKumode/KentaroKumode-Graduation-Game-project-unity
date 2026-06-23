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
            void Dmg() => Add(MetaBuffKind.OutgoingDamagePct, 5);
            void Major(MetaBuffKind k, int amount = 1) => Add(k, amount, true);

            // 設計サマリ:
            //   HP 30, Gold 5, Dmg(コモン) 10×+5% (cap +50%), 大スキル 13 → 計 58
            //   大スキルは重複統合・1段でフル効果。 final(Lv58) は会心ダメージ+100%。
            //
            // ===== 序盤 (Lv 1-20) =====
            Hp(); Hp(); Gold();                                          // 1-3
            Dmg();                                                       // 4 (+5%)
            Hp(); Hp(); Hp();                                            // 5-7
            Major(MetaBuffKind.StartMaterial, 3);                        // 8 (max=3 一括)
            Hp(); Hp(); Hp();                                            // 9-11
            Major(MetaBuffKind.CombatGoldBonus, 2);                      // 12 (max=2 一括)
            Hp(); Hp(); Hp();                                            // 13-15
            Major(MetaBuffKind.DamageReduce, 2);                         // 16 (max=-2 一括)
            Hp(); Gold(); Hp();                                          // 17-19
            Major(MetaBuffKind.BossExtraNormal);                         // 20

            // ===== ミッド (Lv 21-40) =====
            Hp(); Hp();                                                  // 21-22
            Major(MetaBuffKind.HopeLossReduce, 3);                       // 23 (戦闘後の希望減少 -3 一括)
            Hp(); Hp();                                                  // 24-25
            Major(MetaBuffKind.StartingPassiveItem);                     // 26 (開幕パッシブ ノーマル)
            Major(MetaBuffKind.ShopRobberyUnlock);                       // 27
            Hp(); Hp();                                                  // 28-29
            Major(MetaBuffKind.CritLevelUp, 2);                          // 30 (max=+2 一括)
            Hp(); Gold(); Hp();                                          // 31-33
            Dmg();                                                       // 34 (+10%累積)
            Hp();                                                        // 35
            Dmg();                                                       // 36 (+15%累積)
            Hp(); Hp();                                                  // 37-38
            Dmg();                                                       // 39 (+20%累積)
            Hp(); Hp();                                                  // 40-41

            // ===== 終盤 (Lv 42-59) =====
            Major(MetaBuffKind.DiceTotal);                               // 42 (+1)
            Hp();                                                        // 43
            Major(MetaBuffKind.LastStandHpLossDisable);                  // 44 (ラストスタンド 最大HP低下無効)
            Major(MetaBuffKind.BossExtraRare);                           // 45 (ボス追加報酬を レア化)
            Gold();                                                      // 46
            Dmg();                                                       // 47 (+25%累積)
            Hp();                                                        // 48
            Dmg();                                                       // 49 (+30%累積)
            Gold();                                                      // 50
            Major(MetaBuffKind.BossRestHealAndUpgrade);                  // 51 (ボス前休憩 回復+強化)
            Dmg();                                                       // 52 (+35%累積)
            Dmg();                                                       // 53 (+40%累積)
            Hp();                                                        // 54
            Dmg();                                                       // 55 (+45%累積)
            Hp();                                                        // 56
            Dmg();                                                       // 57 (+50%累積=cap)
            Major(MetaBuffKind.CritDamageBonus, 100);                    // 58 final: 会心ダメージ +100% (他の会心ダメージバフと加算合成)

            // 件数チェック（TotalSteps でなければ設計ミス）
            if (list.Count != TotalSteps)
                UnityEngine.Debug.LogError($"[MetaBuffTrack] step 数が不正: {list.Count} / {TotalSteps}");

            return list;
        }
    }
}
