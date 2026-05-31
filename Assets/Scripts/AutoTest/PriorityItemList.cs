using System.Collections.Generic;

namespace AutoTest
{
    /// <summary>
    /// BOT の「攻略上、特に欲しい」アイテム選定（データ駆動・2026-05-30 v2）。
    /// 11000ラン累積の lift6F / formΔ 統計を元に、武器とアイテムを分離して Tier 付け。
    /// PassiveItemEffects のID と items.json の internalName を尊重。
    /// </summary>
    public static class PriorityItemList
    {
        // ============================================================
        //  武器カテゴリ (Weapons)
        // ============================================================

        /// <summary>武器S級: lift6F 最強格。 sword_t3 / axe_t3 / shield_t3 / curse_t1。
        /// curse_t1 は BRONZE だが acq6F=3896 で覚者連戦に寄与（formΔ +0.85）。</summary>
        public static readonly HashSet<string> WeaponSRank = new HashSet<string>
        {
            "sword_t3",      // 銀の長剣
            "axe_t3",        // 血塗りの戦斧
            "shield_t3",     // 聖騎士の盾
            "curse_t1",      // 呪いの短剣
        };

        /// <summary>武器A級: 次点。 Tier 2 メインラインと dagger_t3。</summary>
        public static readonly HashSet<string> WeaponARank = new HashSet<string>
        {
            "sword_t2",      // 鍛鉄の剣
            "axe_t2",        // 猛斧
            "dagger_t3",     // 千手の戦刃
            "shield_t4",     // ドーンブリンガー (small N だが lift6F +0.78)
            "sword_t4",      // 寂滅
            "axe_t4",        // 血帝廻天
            "curse_t4",      // 呪蝕の深淵
            "ryusen",        // 竜閃
        };

        // ============================================================
        //  アイテムカテゴリ (Passives + Dice + Consumables)
        // ============================================================

        /// <summary>アイテムS級: lift6F ≥ 0.07 かつ formΔ ≥ +0.4 の本物の貢献者。</summary>
        public static readonly HashSet<string> ItemSRank = new HashSet<string>
        {
            "dice_perfection",   // 完全性のダイス (L6=0.16, formΔ=+0.66)
            "dice_destiny",      // 運命のダイス (L6=0.15, formΔ=+0.45)
            "dice_greed",        // 貪欲のダイス (L6=0.07, formΔ=+1.18 ★)
            "titans_armband",    // 剛力IV (L6=0.07)
        };

        /// <summary>アイテムA級: lift6F ≥ 0.03 で正の formΔ。</summary>
        public static readonly HashSet<string> ItemARank = new HashSet<string>
        {
            // パッシブ (Tier-line 高位)
            "giants_armband",    // 剛力III
            "iron_armband",      // 剛力II
            "strength_belt",     // 剛力I (acq6F=5572 で安定して効く)
            "黄金の天秤",        // 戦果の秤 (+5G/勝)
            "吸血III",
            "吸血IV",
            "商人の符牒",
            "希望の灯片",        // ← リワーク後想定 (無敗勝利で maxHP+2 永続)
            // ダイス
            "dice_star",         // 星のダイス
            "dice_flame",        // 偏りのダイス
            "dice_twinsnake",    // 双蛇のダイス
            // 消費
            "cons_dice_3",       // 天秤のダイス粉
            "uniq_appraise",     // 鑑定の眼鏡
        };

        // ============================================================
        //  後方互換 API
        // ============================================================

        /// <summary>武器・アイテム合算した S 級セット（旧 SRank 互換）。</summary>
        public static readonly HashSet<string> SRank = BuildUnion(WeaponSRank, ItemSRank);

        /// <summary>武器・アイテム合算した A 級セット（旧 ARank 互換）。</summary>
        public static readonly HashSet<string> ARank = BuildUnion(WeaponARank, ItemARank);

        private static HashSet<string> BuildUnion(HashSet<string> a, HashSet<string> b)
        {
            var r = new HashSet<string>(a);
            foreach (var x in b) r.Add(x);
            return r;
        }

        // ============================================================
        //  判定 API
        // ============================================================

        public static bool IsSRank(string id) => !string.IsNullOrEmpty(id) && SRank.Contains(id);
        public static bool IsARank(string id) => !string.IsNullOrEmpty(id) && ARank.Contains(id);
        public static bool IsPriority(string id) => IsSRank(id) || IsARank(id);

        public static bool IsWeaponS(string id) => !string.IsNullOrEmpty(id) && WeaponSRank.Contains(id);
        public static bool IsWeaponA(string id) => !string.IsNullOrEmpty(id) && WeaponARank.Contains(id);
        public static bool IsItemS(string id)   => !string.IsNullOrEmpty(id) && ItemSRank.Contains(id);
        public static bool IsItemA(string id)   => !string.IsNullOrEmpty(id) && ItemARank.Contains(id);

        /// <summary>S=2, A=1, それ以外=0 のスコア（同枠比較に使う）。</summary>
        public static int Score(string id)
        {
            if (IsSRank(id)) return 2;
            if (IsARank(id)) return 1;
            return 0;
        }
    }
}
