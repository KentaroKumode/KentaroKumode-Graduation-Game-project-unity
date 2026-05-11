namespace MetaProgression
{
    /// <summary>
    /// 恒久バフの種別。1段階獲得ごとの効果は MetaBuffTrack 内で定義される。
    /// 大スキル（ボス追加報酬・返金・会心）はそれぞれ「次のレベルへ昇格」する形でカウントする。
    /// </summary>
    public enum MetaBuffKind
    {
        Hp,                  // +1 HP
        Gold,                // +1 開幕ゴールド
        DiceTotal,           // +1 ダイス合計値補正
        DamageReduce,        // -1 被ダメージ（最大-2、0未満にはならない）
        HungerReduce,        // -1 飢餓ダメージ（最大-3、0未満にはならない）
        StartMaterial,       // +1 開幕の武器強化素材
        CombatGoldBonus,     // +1 戦闘勝利時の追加ゴールド
        BossExtraNormal,     // 大スキル: ボス撃破時、追加でノーマルパッシブ獲得
        BossExtraRare,       // 大スキル: 上の追加報酬をレアパッシブに昇格
        RefundLevelUp,       // 大スキル: 購入返金確率レベル+1（5/10/15%）
        CritLevelUp,         // 大スキル: 会心ダイス補正レベル+1（+1/+2/+3）
    }
}
