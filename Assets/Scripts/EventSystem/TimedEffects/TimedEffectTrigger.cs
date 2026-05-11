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
    }
}
