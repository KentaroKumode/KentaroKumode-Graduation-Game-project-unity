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
        /// <summary>通常の戦闘 (エリート・ボス・追跡者・使い魔・イベントの戦闘・偽の商人・値下げ交渉でない) に勝った時に 1 減る (2026-11-13・〈徒労〉〈覚悟〉)。
        /// 逃げた戦闘・負けた戦闘では鳴らない。 効果は報酬を配る側 (<c>GameManager.TakeNormalBattleBoon</c>) が減らす前の残りを見て決める。
        /// 鳴らすのは <see cref="TimedEffectManager.OnNormalBattleWon"/> だけ。</summary>
        NormalBattleWon,
    }
}
