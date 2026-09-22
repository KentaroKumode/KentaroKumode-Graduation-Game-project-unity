namespace EventSystem.TimedEffects
{
    /// <summary>
    /// 時限バフ・デバフが効果を発揮するタイミング種別。
    /// </summary>
    public enum TimedEffectTrigger
    {
        /// <summary>戦闘開始時に1回適用、適用後にチャージ-1</summary>
        CombatStart,
        /// <summary>毎ロール時に適用（自動消費なし、CombatEnd で-1）</summary>
        OnRoll,
        /// <summary>毎ターン終了時に適用（自動消費なし、CombatEnd で-1）</summary>
        OnTurnEnd,
        /// <summary>戦闘終了時に1回適用、適用後にチャージ-1</summary>
        CombatEnd,
        /// <summary>マップ移動時に適用、適用後にチャージ-1</summary>
        OnMapMove,
        /// <summary>層に入った時に適用 (2026-09-15)。 **1 ランに 8 回しか鳴らない**ので、
        /// 1 回あたりの量はマップ移動系より大きくてよい (移動は 1 ラン 30〜40 回)。</summary>
        OnFloorEnter,
    }
}
