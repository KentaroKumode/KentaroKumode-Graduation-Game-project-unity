using UnityEngine;
using System.Collections.Generic;
using System;

namespace InventorySystem
{
    /// <summary>
    /// アイテムカテゴリ
    /// </summary>
    public enum ItemCategory
    {
        Weapon,
        Armor,
        Accessory,
        Consumable,
        Material,
        Quest,
        Misc,
        Passive,
        PassiveItem,
        Dice
    }

    /// <summary>
    /// アイテム希少性
    /// </summary>
    public enum ItemRarity
    {
        BRONZE,
        SILVER,
        GOLD,
        LEGENDARY,
        MYTHIC
    }

    /// <summary>
    /// 価格範囲
    /// </summary>
    [Serializable]
    public class PriceRange
    {
        public int min = 0;
        public int max = 0;
        
        public int GetRandomValue()
        {
            return UnityEngine.Random.Range(min, max + 1);
        }
        
        public override string ToString()
        {
            return min == max ? $"{min}" : $"{min}～{max}";
        }
    }

    /// <summary>
    /// ダイス設定
    /// </summary>
    [Serializable]
    public class DiceConfig
    {
        public int count = 1;
        public int minValue = 1;
        public int maxValue = 6;
        
        public int RollDice()
        {
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total += UnityEngine.Random.Range(minValue, maxValue + 1);
            }
            return total;
        }
        
        public override string ToString()
        {
            return $"{count}d{minValue}-{maxValue}";
        }
    }

    /// <summary>
    /// パッシブ効果
    /// </summary>
    [Serializable]
    public class PassiveEffect
    {
        public string effectName = "";
        public string description = "";
        public float value = 0f;
    }

    /// <summary>
    /// パッシブスキル（内部名＋表示名＋説明文）
    /// </summary>
    [Serializable]
    public class PassiveSkill
    {
        public string internalName = "";
        public string skillName = "";
        public string description = "";
        
        public PassiveSkill() { }
        public PassiveSkill(string internalName, string name, string desc)
        {
            this.internalName = internalName;
            skillName = name;
            description = desc;
        }
        
        public override string ToString()
        {
            return $"[{internalName}] {skillName}: {description}";
        }
    }

    /// <summary>
    /// アイテムサイズ
    /// </summary>
    [Serializable]
    public class ItemSize
    {
        public int x = 1;
        public int y = 1;
    }

    /// <summary>
    /// 経済データ
    /// </summary>
    [Serializable]
    public class EconomyData
    {
        public int baseValue = 0;
        public float sellMultiplier = 1.0f;
        public float buyMultiplier = 1.0f;
    }

    /// <summary>
    /// アイテムデータ基底クラス
    /// </summary>
    [Serializable]
    public class ItemDataV2
    {
        [Header("基本情報")]
        public string internalName = "";
        public string displayName = "";
        public ItemCategory category;
        public ItemRarity rarity;
        public GameObject fbxModel;
        
        [Header("説明")]
        [TextArea(2, 4)]
        public string description = "";
        
        [Header("サイズ")]
        public ItemSize size;
        
        [Header("価格設定")]
        public PriceRange buyPrice;
        public PriceRange sellPrice;
        
        [Header("武器データ（武器のみ）")]
        public DiceConfig weaponDice;
        public int criticalRate;     // 会心率の分子（1～9、分母は9）
        public int attackPower;      // 武器の素火力（#2 案A'：勝利base = attackPower + floor(|差|/3)）

        [Header("ダイスデータ（ダイスのみ）")]
        public int[] diceFaces;      // カスタムダイスの面配列 (例: {1,2,3,4,5,6})
        public string roleName = "";         // ロール名（タンク/ナイト/バーサーカー/アサシン）
        public string roleDescription = "";  // ロール説明
        public List<PassiveEffect> weaponPassives = new List<PassiveEffect>();
        
        [Header("パッシブアイテムデータ")]
        public List<PassiveEffect> passiveEffects = new List<PassiveEffect>();
        
        [Header("パッシブスキル")]
        public List<PassiveSkill> passiveSkills = new List<PassiveSkill>();
        
        [Header("クエストアイテムデータ（クエストのみ）")]
        [TextArea(1, 3)]
        public string flavorText = "";
        public string skillName = "";
        
        [NonSerialized] public Sprite icon;
        [NonSerialized] public GameObject equipMarkPrefab;
        
        // 経済データキャッシュ
        [NonSerialized] private EconomyData _economyCache;
        
        public ItemDataV2()
        {
            size = new ItemSize();
            buyPrice = new PriceRange();
            sellPrice = new PriceRange();
            weaponDice = new DiceConfig();
        }
        
        // カテゴリ判定
        public bool IsWeapon => category == ItemCategory.Weapon;
        public bool IsPassive => category == ItemCategory.Passive || category == ItemCategory.PassiveItem;
        public bool IsQuest => category == ItemCategory.Quest;
        
        // 価格取得
        public int GetCurrentBuyPrice() => buyPrice.GetRandomValue();
        public int GetCurrentSellPrice() => sellPrice.GetRandomValue();
        
        // 武器ダイス情報
        public DiceConfig weaponStats => IsWeapon ? weaponDice : null;
        public bool hasWeaponStats => IsWeapon && weaponDice != null;
        
        /// <summary>
        /// 経済データ（キャッシュ済み）
        /// </summary>
        public EconomyData economy
        {
            get
            {
                if (_economyCache == null)
                {
                    _economyCache = new EconomyData();
                }
                _economyCache.baseValue = GetCurrentSellPrice();
                _economyCache.sellMultiplier = 1.0f;
                _economyCache.buyMultiplier = 1.0f;
                return _economyCache;
            }
        }
    }

    /// <summary>
    /// レアリティカラーユーティリティ
    /// </summary>
    public static class RarityColorUtility
    {
        public static Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.BRONZE: return new Color(0.8f, 0.5f, 0.2f);
                case ItemRarity.SILVER: return Color.white;
                case ItemRarity.GOLD: return Color.yellow;
                case ItemRarity.LEGENDARY: return new Color(1f, 0.5f, 0f);
                case ItemRarity.MYTHIC: return Color.cyan;
                default: return Color.gray;
            }
        }
    }

    /// <summary>
    /// 統合型アイテムデータ（ItemDataV2 を拡張）
    /// 後方互換性プロパティと機能判定を提供
    /// </summary>
    public class CompleteItemData : ItemDataV2
    {
        /// <summary>
        /// ItemDataV2 から CompleteItemData を生成
        /// </summary>
        public static CompleteItemData FromItemDataV2(ItemDataV2 source)
        {
            return new CompleteItemData
            {
                internalName = source.internalName,
                displayName = source.displayName,
                description = source.description,
                category = source.category,
                rarity = source.rarity,
                fbxModel = source.fbxModel,
                flavorText = source.flavorText,
                skillName = source.skillName,
                size = source.size,
                buyPrice = source.buyPrice,
                sellPrice = source.sellPrice,
                weaponDice = source.weaponDice,
                criticalRate = source.criticalRate,
                roleName = source.roleName,
                roleDescription = source.roleDescription,
                weaponPassives = source.weaponPassives,
                passiveEffects = source.passiveEffects,
                passiveSkills = source.passiveSkills,
                diceFaces = source.diceFaces,
                icon = source.icon,
                equipMarkPrefab = source.equipMarkPrefab,
            };
        }
        // === 後方互換性エイリアス（CoinSystem等の外部参照用） ===
        
        /// <summary>管理ID（CoinSystem互換）</summary>
        public string managementId 
        { 
            get => internalName; 
            set => internalName = value; 
        }
        
        /// <summary>アイテムID</summary>
        public string id => internalName;
        
        /// <summary>サイズX（PlacementValidator互換）</summary>
        public int sizeX 
        { 
            get => size.x; 
            set => size.x = value; 
        }
        
        /// <summary>サイズY（PlacementValidator互換）</summary>
        public int sizeY 
        { 
            get => size.y; 
            set => size.y = value; 
        }
        
        // === ステータスプロパティ ===
        
        /// <summary>攻撃力（ダイスロール結果）</summary>
        public int attack => IsWeapon ? weaponDice.RollDice() : 0;
        
        /// <summary>設定中央価格</summary>
        public int basePrice => (buyPrice.min + buyPrice.max + sellPrice.min + sellPrice.max) / 4;
        
        // === 機能判定 ===
        
        public bool IsEquippable => category == ItemCategory.Weapon || category == ItemCategory.Armor || category == ItemCategory.Dice;
        public bool IsDice => category == ItemCategory.Dice;
        public bool IsUsable => category == ItemCategory.Consumable;
        public bool IsConsumable => category == ItemCategory.Consumable;
        public bool HasStats => IsWeapon || category == ItemCategory.Armor;
        
        // === アセット参照エイリアス ===
        
        /// <summary>カードモデル（fbxModelのエイリアス）</summary>
        public GameObject cardModel => fbxModel;
        
        /// <summary>アイコン（CoinSystem互換）</summary>
        public Sprite iconSprite => icon;
        
        /// <summary>モデルプレハブ（CoinSystem互換）</summary>
        public GameObject modelPrefab
        {
            get => fbxModel;
            set => fbxModel = value;
        }
        
        /// <summary>アイテムアイコン（UI互換）</summary>
        public Sprite itemIcon => icon;
    }
}
