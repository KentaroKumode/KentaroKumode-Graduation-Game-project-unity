namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// パッシブスキルが発動するタイミング（トリガー条件）
    /// 各スキルは1つ以上のトリガーに反応する
    /// </summary>
    public enum PassiveSkillTrigger
    {
        // === 戦闘フロー ===
        /// <summary>戦闘開始時</summary>
        OnBattleStart,
        /// <summary>ターン開始時</summary>
        OnTurnStart,
        /// <summary>ターン終了時</summary>
        OnTurnEnd,
        /// <summary>戦闘終了時</summary>
        OnBattleEnd,

        // === ダイスロール ===
        /// <summary>ダイスロール直前（ダイス補正用）</summary>
        OnPreRoll,
        /// <summary>ダイスロール直後（結果確定前）</summary>
        OnPostRoll,

        // === 勝敗判定 ===
        /// <summary>ロール勝利時</summary>
        OnRollWin,
        /// <summary>ロール敗北時</summary>
        OnRollLose,
        /// <summary>ロール引き分け時</summary>
        OnRollDraw,

        // === ダメージ計算 ===
        /// <summary>ダメージ計算前（自分が与える側）</summary>
        OnPreDealDamage,
        /// <summary>ダメージ計算前（自分が受ける側）</summary>
        OnPreReceiveDamage,
        /// <summary>ダメージ確定後（自分が与えた側）</summary>
        OnPostDealDamage,
        /// <summary>ダメージ確定後（自分が受けた側）</summary>
        OnPostReceiveDamage,

        // === 追撃/scratch ===
        /// <summary>追撃ダメージ計算前</summary>
        OnPrePursuitDamage,
        /// <summary>scratchダメージ計算前（勝利時のthreat削りダメージ）</summary>
        OnPreScratchDamage,

        // === 会心 ===
        /// <summary>会心判定時</summary>
        OnCriticalCheck,
        /// <summary>会心ダメージ計算時</summary>
        OnCriticalDamage,

        // === 状態異常 ===
        /// <summary>状態異常を付与する時</summary>
        OnApplyStatusEffect,
        /// <summary>状態異常を受ける時</summary>
        OnReceiveStatusEffect,

        // === 装備 ===
        /// <summary>装備した時（常駐効果の適用）</summary>
        OnEquip,
        /// <summary>装備を外した時</summary>
        OnUnequip,
    }
}
