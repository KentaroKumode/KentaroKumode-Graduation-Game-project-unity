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

        // === 経済 ===
        public float coinRewardMultiplier = 1f; // 報酬コイン倍率
        public float shopPriceMultiplier = 1f;  // ショップ価格倍率

        // === 戦闘ステータス ===
        public float critRatePctBonus;          // 会心率加算 (%)。 武器の会心率にだけ掛かり 0% で止まる
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
            // [削除 2026-10-17] 1 層〈残光の加護〉（空腹度上限 +3 だけ）・2 層〈嘆きの共鳴〉・4 層〈煉獄の刻印〉。
            //   空腹は廃止、 2 層と 4 層は層ごと削除済み (GAME.md §4-1)。 層番号は登録の順番ではなく明示で渡す。

            // ============================================================
            //  3層 — 瘴気侵蝕
            //  勝っても削られる。休憩の価値が上がる。
            // ============================================================
            Register(3, new FloorModifier
            {
                id = "miasma_erosion",
                displayName = "瘴気侵蝕",
                description = "戦闘終了後に固定1ダメージ / 休憩回復量30%→50%",
                postCombatDamage = 1,
                restHealMultiplier = 0.5f,
            });

            // ============================================================
            //  5層 — 地獄門
            //  出力低下。ダイス最大値9→8はゾロ目確率微増(BloodDecree追い風)。
            // ============================================================
            Register(5, new FloorModifier
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
            Register(6, new FloorModifier
            {
                id = "abyss_floor",
                displayName = "深淵の洗礼",
                description = "毎ターン自傷1 / 会心率-15% / 敵が毎ターンHP+3回復",
                maxHPBonusFlat = 5,
                perTurnSelfDamage = 1,
                critRatePctBonus = -15f,
                enemyPerTurnHeal = 3,
            });
        }

        private static void Register(int floor, FloorModifier mod)
        {
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
