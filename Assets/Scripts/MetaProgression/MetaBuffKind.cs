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
        HopeLossReduce,      // -1 戦闘後の希望ゲージ減少（最大-3、0未満にはならない）※ADR-0002 で飢餓は希望に統合
        StartMaterial,       // +1 開幕の武器強化素材
        CombatGoldBonus,     // +1 戦闘勝利時の追加ゴールド
        BossExtraNormal,     // 大スキル: ボス撃破時、追加でノーマルパッシブ獲得
        BossExtraRare,       // 大スキル: 上の追加報酬をレアパッシブに昇格
        RefundLevelUp,       // 大スキル: ショップ特売品 出現数+1 (Lv1/2/3 で 1/2/3 個、 20-60% 割引)
        CritLevelUp,         // 大スキル: 会心ダイス補正レベル+1（+1/+2/+3）
        StartingPassiveItem, // 大スキル: 開幕でノーマルパッシブを1個獲得
        FloorClearHeal,      // フロアクリア時に +1 HP回復（最大2段=+2）
        TreasureChestGold,   // 大スキル: 宝箱マスでゴールドも獲得（撤廃された宝箱ゴールドの復活）
        ShopRobberyUnlock,   // 大スキル: ショップで「値下げ」交渉(=強盗) 行動が可能になる
        OutgoingDamagePct,   // 与ダメージ +5% (cap 50%・10段)。outgoingDamageMultiplier に加算される=他%倍率と加算合成
        LastStandHpLossDisable,    // 大スキル: ラストスタンド発動時の最大HP半減を無効化（満タンで生還）
        BossRestHealAndUpgrade,    // 大スキル: フロアボス前の休憩エリアで回復と強化が同時に行える
        CritDamageBonus,           // 大スキル(final): 会心ダメージ +X% (amount=100 で +100%、 他バフと加算合成)
    }
}
