using System.Collections.Generic;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// 層ごとのバフ/デバフ定義。
    /// 前哨基地到着時に適用され、フロア中持続する。
    /// </summary>
    public class FloorModifier
    {
        public string id;
        public string displayName;
        public string description;

        // === 空腹度 ===
        public int hungerMaxBonus;              // 空腹上限加算（正=バフ）
        public float starvationDamageOverride;  // -1=変更なし、0以上=上書き

        // === 経済 ===
        public float coinRewardMultiplier = 1f; // 報酬コイン倍率
        public float shopPriceMultiplier = 1f;  // ショップ価格倍率

        // === 戦闘ステータス ===
        public int critRateBonus;               // 会心率加算
        public int maxHPBonus;                  // MaxHP加算（負=デバフ）
        public int diceMaxBonus;                // ダイス最大値加算（負=デバフ）

        // === 戦闘後/ターン効果 ===
        public int postCombatDamage;            // 戦闘終了後の固定ダメージ
        public float restHealMultiplier = 0.3f; // 休憩回復倍率（デフォ30%）
        public int defeatDamageReduction;       // 敗北時被ダメ軽減

        // === 6層（裏ボス）専用 ===
        public int perTurnSelfDamage;           // 毎ターン自傷
        public int enemyPerTurnHeal;            // 敵の毎ターン回復
        public int maxHPBonusFlat;              // MaxHP固定加算（前哨基地用）
    }

    /// <summary>
    /// 全層のFloorModifier定義。
    /// </summary>
    public static class FloorModifierDatabase
    {
        private static readonly Dictionary<int, FloorModifier> modifiers = new Dictionary<int, FloorModifier>();

        static FloorModifierDatabase()
        {
            // ============================================================
            //  1層 — 黄昏の口「残光の加護」
            //  探索の余裕。空腹10→13で横移動3回まで安全。
            // ============================================================
            Register(new FloorModifier
            {
                id = "twilight_gate",
                displayName = "残光の加護",
                description = "空腹度上限+3",
                hungerMaxBonus = 3,
            });

            // ============================================================
            //  2層 — 嘆きの共鳴
            //  ガラスキャノン化。crit+2は短剣/斧に恩恵、MaxHP-5は盾に痛い。
            // ============================================================
            Register(new FloorModifier
            {
                id = "wailing_echo",
                displayName = "嘆きの共鳴",
                description = "会心率+2 / MaxHP-5",
                critRateBonus = 2,
                maxHPBonus = -5,
            });

            // ============================================================
            //  3層 — 瘴気侵蝕
            //  勝っても削られる。休憩の価値が上がる。
            // ============================================================
            Register(new FloorModifier
            {
                id = "miasma_erosion",
                displayName = "瘴気侵蝕",
                description = "戦闘終了後に固定2ダメージ / 休憩回復量30%→50%",
                postCombatDamage = 2,
                restHealMultiplier = 0.5f,
            });

            // ============================================================
            //  4層 — 煉獄の刻印
            //  飢餓地獄。空腹10→7、飢餓ダメ2倍。直進でも3マス飢える。
            // ============================================================
            Register(new FloorModifier
            {
                id = "purgatory_brand",
                displayName = "煉獄の刻印",
                description = "空腹度上限-3 / 空腹時ダメージ×2 / 戦闘後3ダメージ / ショップ価格+20%",
                hungerMaxBonus = -3,
                starvationDamageOverride = 0.2f,
                postCombatDamage = 3,
                shopPriceMultiplier = 1.2f,
            });

            // ============================================================
            //  5層 — 地獄門
            //  出力低下。ダイス最大値9→8はゾロ目確率微増(BloodDecree追い風)。
            // ============================================================
            Register(new FloorModifier
            {
                id = "hell_gate",
                displayName = "地獄門",
                description = "全ダイス最大値-1 / 敗北時の被ダメージ-2 / ショップ価格+40%",
                diceMaxBonus = -1,
                defeatDamageReduction = 2,
                shopPriceMultiplier = 1.4f,
            });

            // ============================================================
            //  6層 — 奈落の底（裏ボス）
            //  前哨基地: HP全回復+MaxHP+5
            //  デバフマス「深淵の洗礼」: 毎ターン自傷1/会心-3/敵毎ターンHP+3
            // ============================================================
            Register(new FloorModifier
            {
                id = "abyss_floor",
                displayName = "深淵の洗礼",
                description = "毎ターン自傷1 / 会心率-3 / 敵が毎ターンHP+3回復",
                maxHPBonusFlat = 5,
                perTurnSelfDamage = 1,
                critRateBonus = -3,
                enemyPerTurnHeal = 3,
            });
        }

        private static void Register(FloorModifier mod)
        {
            int floor = modifiers.Count + 1;
            modifiers[floor] = mod;
        }

        /// <summary>指定フロアのModifierを取得（なければnull）</summary>
        public static FloorModifier Get(int floor)
        {
            modifiers.TryGetValue(floor, out var mod);
            return mod;
        }

        /// <summary>全Modifier一覧</summary>
        public static IReadOnlyDictionary<int, FloorModifier> All => modifiers;
    }
}
