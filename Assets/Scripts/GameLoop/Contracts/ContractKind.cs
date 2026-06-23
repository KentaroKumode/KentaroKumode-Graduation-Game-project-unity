namespace GameLoop.Contracts
{
    /// <summary>
    /// 旅団契約の種別 ── 全 12 旅団。
    /// 数値・効果・敵対/協力関係の正本は docs/specs/contracts.md。
    /// </summary>
    public enum ContractKind
    {
        Mercenaries = 0,      // 傭兵団
        SupplyCaravan = 1,    // 補給キャラバン
        MerchantsLeague = 2,  // 商業連合隊
        Missionaries = 3,     // 宣教師
        Knights = 4,          // 騎士
        Assassins = 5,        // 暗殺教団
        Alchemist = 6,        // 旅する錬金術師
        WanderingDoctor = 7,  // 放浪医術官
        OrphanCircus = 8,     // 捨て子のサーカス団
        BodyDoubles = 9,      // 影武者一座
        Hunters = 10,         // 狩猟旅団
        Tacticians = 11,      // 戦術家
    }
}
