namespace ItemSystem.PassiveSkills
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

        // === 相互攻撃モデル (ADR-0009) ===
        /// <summary>配線の確定直後・自攻撃の解決より前 (2026-10-28 追加)。
        /// 端子ごとの本数は accumulatedValues の "mutualAttackDiceCount" / "mutualBlockDiceCount" /
        /// "mutualChargeDiceCount" / "mutualSpecialDiceCount"、 ブロック量は blockSumThisTurn で読める。
        /// 攻撃値への加算 (AddPlayerAttackOrDiceBonus) はここでもまだ乗る。 **末尾に足すこと** ── 途中に挟むと序数がずれる。</summary>
        OnWired,

        // === キーワードの出来事 (2026-10-31 アイテム再編) ===
        //   Keywords (Kw) が量の変化を見て PassiveSkillManager.RaiseKeyword で配る。 量は Kw.EventAmount(ctx) で読む。
        //   1 つの出来事から続く連鎖の中では、 同じ品は 1 度しか働かない (GAME.md §11)。
        /// <summary>臨界が閾値に達して爆発が 1 回決まった (量 = その爆発で使った臨界)。</summary>
        OnRinkaiBurst,
        /// <summary>充電が実際に増えた (量 = 増えた分)。</summary>
        OnChargeGain,
        /// <summary>充電を使った・リロールを含む (量 = 使った分)。</summary>
        OnChargeSpend,
        /// <summary>過充電: 上限を超えて捨てられた (量 = 捨てられた分)。</summary>
        OnOvercharge,
        /// <summary>シールドが増えた (量 = 増えた分)。</summary>
        OnShieldGain,
        /// <summary>シールドが攻撃を吸収した (量 = 吸収した分)。</summary>
        OnShieldAbsorb,
        /// <summary>ターン開始の半減でシールドを失った (量 = 失った分)。</summary>
        OnShieldDecay,
        /// <summary>シールドが吸収で 0 になった (量 = 割られる前の量)。</summary>
        OnShieldBreak,
        /// <summary>敵の毒が増えた (量 = 増えた分)。</summary>
        OnPoisonGain,
        /// <summary>毒のダメージが入った (量 = そのダメージ)。</summary>
        OnPoisonTick,
        /// <summary>敵の毒が上限に達した。</summary>
        OnPoisonCap,
        /// <summary>出血が自然に減った (量 = 減った分)。</summary>
        OnBleedDecay,
        /// <summary>出血を品の効果で使った (量 = 使った分)。</summary>
        OnBleedConsume,
        /// <summary>敵の出血が 0 になった。</summary>
        OnBleedZero,
    }
}
