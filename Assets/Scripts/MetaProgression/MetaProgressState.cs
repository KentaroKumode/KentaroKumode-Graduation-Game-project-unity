using System.Collections.Generic;

namespace MetaProgression
{
    /// <summary>
    /// プレイヤーのメタ進行状態。PlayerPrefs に JSON で保存される。
    /// </summary>
    [System.Serializable]
    public class MetaProgressState
    {
        public int tokens;
        public int currentLevel;          // 0〜115。currentLevel 段までは購入済み。
        public List<int> activeDebuffs = new List<int>(); // 有効化中の MetaDebuffLevel 値

        // 大スキルの累積カウント（バフトラック内の重複可スキル用）
        public int refundLevel;           // 0〜3 (5%/10%/15%)
        public int critLevel;             // 0〜3 (+1/+2/+3)
        public bool bossExtraNormalUnlocked;
        public bool bossExtraRareUnlocked;

        // 双蛇のダイス〈永劫〉: 勝利した戦闘数をランを跨いで永続蓄積。RecalculateFromTrack では触れない。
        public int eternalStacks;

        // === 集計値（currentLevel 進行に応じて再計算） ===
        public int hpBonus;
        public int goldBonus;
        public int diceTotalBonus;
        public int damageReduce;          // 最大2
        public int hungerReduce;          // 最大3
        public int startMaterial;         // 最大3
        public int combatGoldBonus;       // 最大2 (raw 上限。GetCombatGoldBonus でさらに Min(2) クランプ)
        public int floorClearHeal;        // 最大2 (フロアクリア時HP回復量)
        public bool divineProtectUnlocked;       // 1戦闘1回ロール敗北→引き分け化
        public bool startingPassiveItemUnlocked; // 開幕パッシブ獲得
        public bool treasureChestGoldUnlocked;   // 宝箱マスでゴールドも獲得
        public bool shopRobberyUnlocked;         // ショップで「値下げ」交渉(=強盗) 行動が可能

        public bool HasDebuff(MetaDebuffLevel lv) => activeDebuffs != null && activeDebuffs.Contains((int)lv);

        public float ShopPriceMultiplier => HasDebuff(MetaDebuffLevel.Lv1_困窮した商隊) ? 1.25f : 1f;

        /// <summary>currentLevel から各集計値を再計算する。</summary>
        public void RecalculateFromTrack()
        {
            hpBonus = 0;
            goldBonus = 0;
            diceTotalBonus = 0;
            damageReduce = 0;
            hungerReduce = 0;
            startMaterial = 0;
            combatGoldBonus = 0;
            floorClearHeal = 0;
            refundLevel = 0;
            critLevel = 0;
            bossExtraNormalUnlocked = false;
            bossExtraRareUnlocked = false;
            divineProtectUnlocked = false;
            startingPassiveItemUnlocked = false;
            treasureChestGoldUnlocked = false;
            shopRobberyUnlocked = false;

            for (int lv = 1; lv <= currentLevel; lv++)
            {
                var step = MetaBuffTrack.Get(lv);
                if (step == null) continue;
                ApplyStep(step);
            }
        }

        private void ApplyStep(MetaBuffStep step)
        {
            switch (step.kind)
            {
                case MetaBuffKind.Hp:               hpBonus += step.amount; break;
                case MetaBuffKind.Gold:             goldBonus += step.amount; break;
                case MetaBuffKind.DiceTotal:        diceTotalBonus += step.amount; break;
                case MetaBuffKind.DamageReduce:     damageReduce += step.amount; break;
                case MetaBuffKind.HungerReduce:     hungerReduce += step.amount; break;
                case MetaBuffKind.StartMaterial:    startMaterial += step.amount; break;
                case MetaBuffKind.CombatGoldBonus:  combatGoldBonus += step.amount; break;
                case MetaBuffKind.BossExtraNormal:  bossExtraNormalUnlocked = true; break;
                case MetaBuffKind.BossExtraRare:    bossExtraRareUnlocked = true; break;
                case MetaBuffKind.RefundLevelUp:    refundLevel = UnityEngine.Mathf.Min(3, refundLevel + step.amount); break;
                case MetaBuffKind.CritLevelUp:      critLevel = UnityEngine.Mathf.Min(3, critLevel + step.amount); break;
                case MetaBuffKind.DivineProtect:    divineProtectUnlocked = true; break;
                case MetaBuffKind.StartingPassiveItem: startingPassiveItemUnlocked = true; break;
                case MetaBuffKind.FloorClearHeal:   floorClearHeal = UnityEngine.Mathf.Min(2, floorClearHeal + step.amount); break;
                case MetaBuffKind.TreasureChestGold: treasureChestGoldUnlocked = true; break;
                case MetaBuffKind.ShopRobberyUnlock: shopRobberyUnlocked = true; break;
            }
        }
    }
}
