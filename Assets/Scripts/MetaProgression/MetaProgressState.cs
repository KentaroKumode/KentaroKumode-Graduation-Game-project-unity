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

        // === 読み物（WorldVignettes）解禁用カウンタ ===
        // JsonUtility は Dictionary<,> を非対応のため、並列リストで持つ。
        // アクセスは VignetteUnlockState の GetEndingClearCount / IsEndingMaxDiffCleared 経由。
        /// <summary>エンド別クリア回数: キーリスト（"end1"〜"end5"）。endingClearValues と並列。</summary>
        public List<string> endingClearKeys   = new List<string>();
        /// <summary>エンド別クリア回数: 値リスト。endingClearKeys と並列。</summary>
        public List<int>    endingClearValues = new List<int>();
        /// <summary>最高難度クリア済エンドのIDリスト（要素が存在 = true）。</summary>
        public List<string> endingMaxDiffCleared = new List<string>();
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
        public int hopeLossReduce;        // 戦闘後の希望減少を軽減 (最大3、 ADR-0002)
        public int startMaterial;         // 最大3
        public int combatGoldBonus;       // 最大2 (raw 上限。GetCombatGoldBonus でさらに Min(2) クランプ)
        public int floorClearHeal;        // 最大2 (フロアクリア時HP回復量)
        public bool startingPassiveItemUnlocked; // 開幕パッシブ獲得
        public bool treasureChestGoldUnlocked;   // 宝箱マスでゴールドも獲得
        public bool shopRobberyUnlocked;         // ショップで「値下げ」交渉(=強盗) 行動が可能
        public int outgoingDamagePct;            // 与ダメージ +X% (最大50)。CombatManager 側で outgoingDamageMultiplier に加算
        public bool lastStandHpLossDisabled;     // ラストスタンド発動時の最大HP半減を無効化
        public bool bossRestHealAndUpgradeUnlocked; // フロアボス前の休憩エリアで回復+強化
        /// <summary>会心ダメージ +X% (Lv58 final、 amount=100 で +1.0)。 CombatContext.criticalMultiplier に直接加算される。
        /// 他の会心ダメージバフ (HopeSystem苦悩・パッシブ等) とは加算合成され、 同時計算される。</summary>
        public float critDamageBonus;

        public bool HasDebuff(MetaDebuffLevel lv) => activeDebuffs != null && activeDebuffs.Contains((int)lv);

        public float ShopPriceMultiplier => HasDebuff(MetaDebuffLevel.Lv1_困窮した商隊) ? 1.25f : 1f;

        /// <summary>currentLevel から各集計値を再計算する。</summary>
        public void RecalculateFromTrack()
        {
            hpBonus = 0;
            goldBonus = 0;
            diceTotalBonus = 0;
            damageReduce = 0;
            hopeLossReduce = 0;
            startMaterial = 0;
            combatGoldBonus = 0;
            floorClearHeal = 0;
            refundLevel = 0;
            critLevel = 0;
            bossExtraNormalUnlocked = false;
            bossExtraRareUnlocked = false;
            startingPassiveItemUnlocked = false;
            treasureChestGoldUnlocked = false;
            shopRobberyUnlocked = false;
            outgoingDamagePct = 0;
            lastStandHpLossDisabled = false;
            bossRestHealAndUpgradeUnlocked = false;
            critDamageBonus = 0f;

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
                case MetaBuffKind.HopeLossReduce:   hopeLossReduce += step.amount; break;
                case MetaBuffKind.StartMaterial:    startMaterial += step.amount; break;
                case MetaBuffKind.CombatGoldBonus:  combatGoldBonus += step.amount; break;
                case MetaBuffKind.BossExtraNormal:  bossExtraNormalUnlocked = true; break;
                case MetaBuffKind.BossExtraRare:    bossExtraRareUnlocked = true; break;
                case MetaBuffKind.RefundLevelUp:    refundLevel = UnityEngine.Mathf.Min(3, refundLevel + step.amount); break;
                case MetaBuffKind.CritLevelUp:      critLevel = UnityEngine.Mathf.Min(3, critLevel + step.amount); break;
                case MetaBuffKind.StartingPassiveItem: startingPassiveItemUnlocked = true; break;
                case MetaBuffKind.FloorClearHeal:   floorClearHeal = UnityEngine.Mathf.Min(2, floorClearHeal + step.amount); break;
                case MetaBuffKind.TreasureChestGold: treasureChestGoldUnlocked = true; break;
                case MetaBuffKind.ShopRobberyUnlock: shopRobberyUnlocked = true; break;
                case MetaBuffKind.OutgoingDamagePct: outgoingDamagePct = UnityEngine.Mathf.Min(50, outgoingDamagePct + step.amount); break;
                case MetaBuffKind.LastStandHpLossDisable: lastStandHpLossDisabled = true; break;
                case MetaBuffKind.BossRestHealAndUpgrade: bossRestHealAndUpgradeUnlocked = true; break;
                case MetaBuffKind.CritDamageBonus: critDamageBonus += step.amount / 100f; break;
            }
        }
    }
}
