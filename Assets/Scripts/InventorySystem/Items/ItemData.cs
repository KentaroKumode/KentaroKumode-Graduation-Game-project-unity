namespace InventorySystem
{
    /// <summary>
    /// パッシブスキル（JSONデシリアライズ用）
    /// </summary>
    [System.Serializable]
    public class PassiveSkillJson
    {
        public string internalName;
        public string skillName;
        public string description;
    }

    /// <summary>
    /// JSONからのデシリアライズ用データ構造
    /// JsonUtility.FromJson で使用
    /// </summary>
    [System.Serializable]
    public class ItemDataJson
    {
        public string id;
        public string name;          // JSONキー "name" に対応
        public string description;
        public string flavorText;
        public string category;
        public string rarity;
        public int sizeX;
        public int sizeY;
        public int diceCount;        // ダイス数（Weaponのみ）
        public int diceMax;          // ダイス最大出目（Weaponのみ）
        public int criticalRate;     // 会心率の分子（1～9、分母は9）
        public int basePrice;        // 設定中央価格（購入/売却額はシステムが±25%で算出）
        public PassiveSkillJson[] passiveSkills;
    }
    
    /// <summary>
    /// JSON配列のルートオブジェクト
    /// </summary>
    [System.Serializable]
    public class ItemDataListJson
    {
        public ItemDataJson[] items;
    }
}
