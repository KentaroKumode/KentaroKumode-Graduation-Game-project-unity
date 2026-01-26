namespace InventorySystem
{
    /// <summary>
    /// アイテムカテゴリー
    /// </summary>
    public enum ItemCategory
    {
        Weapon,         // 武器
        Armor,          // 防具
        PassiveItem,    // パッシブアイテム
        Material,       // 素材
        Consumable,     // 消費アイテム
        Quest           // クエストアイテム
    }
    
    /// <summary>
    /// アイテムレアリティ
    /// </summary>
    public enum ItemRarity
    {
        Bronze,   // ブロンズ
        Silver,   // シルバー
        Gold,     // ゴールド
        Mythic    // ミシック
    }
}
