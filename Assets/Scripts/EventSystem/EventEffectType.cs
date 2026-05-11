namespace EventSystem
{
    /// <summary>
    /// イベント結果の効果種別。テキストパーサが各エフェクト文字列をこのenumに分類する。
    /// </summary>
    public enum EventEffectType
    {
        None,                    // 「なし」
        HpDelta,                 // HP+N / HP-N
        HpFullHeal,              // HP全回復
        HpSetTo,                 // HPがNになる
        MaxHpDelta,              // 最大HP+N / 最大HP-N
        GoldDelta,               // ゴールド+N / ゴールド-N
        HungerDelta,             // 空腹度+N / 空腹度-N
        KarmaGain,               // カルマ獲得
        MaterialDelta,           // 武器強化素材+N / 武器強化素材-N
        ArmorDurabilityLoss,     // 防具の耐久値減少
        TimedBuff,               // 時限バフ[xxx]を獲得
        TimedDebuff,             // 時限デバフ[xxx]を獲得
        PermanentDebuff,         // 永続デバフ[xxx]を獲得
        GainPassiveItem,         // パッシブアイテム獲得 / パッシブアイテム[xxx]を獲得
        GainConsumableItem,      // 消費アイテム獲得 / 消費アイテム[xxx]を獲得
        GainSpecificItem,        // アイテム[xxx]を獲得（カテゴリ未指定）
        GainFlag,                // フラグアイテム[xxx]を獲得
        DiscardFlag,             // フラグアイテム[xxx]を廃棄
        DiscardPassiveItem,      // パッシブアイテム[xxx]を廃棄
        EnterCombat,             // 戦闘に突入
        EnterEliteCombat,        // エリートとの戦闘に突入
        RandomEvent,             // ランダムなイベント発生
        Probability,             // 確率分岐の親（child のリストを持つ）
    }
}
