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
        PassiveItem
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
        public int count = 1;        // ダイスの数
        public int minValue = 1;     // 最小値
        public int maxValue = 6;     // 最大値
        
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
    /// アイテムサイズ
    /// </summary>
    [Serializable]
    public class ItemSize
    {
        public int x = 1;
        public int y = 1;
    }

    /// <summary>
    /// 統合型アイテムデータ構造
    /// </summary>
    [System.Serializable]
    public class ItemDataV2
    {
        [Header("基本情報")]
        public string internalName = "";      // 内部名（iron_sword等）
        public string displayName = "";      // 表示名
        public ItemCategory category;
        public ItemRarity rarity;
        public GameObject fbxModel;          // FBXモデル（1:1紐づけ）
        
        [Header("説明")]
        [TextArea(2, 4)]
        public string description = "";      // 説明文
        
        [Header("サイズ")]
        public ItemSize size;
        
        [Header("価格設定")]
        public PriceRange buyPrice;          // 購入価格範囲
        public PriceRange sellPrice;         // 売却価格範囲
        
        [Header("武器データ（武器のみ）")]
        public DiceConfig weaponDice;
        public List<PassiveEffect> weaponPassives = new List<PassiveEffect>();
        
        [Header("パッシブアイテムデータ")]
        public List<PassiveEffect> passiveEffects = new List<PassiveEffect>();
        
        [Header("クエストアイテムデータ（クエストのみ）")]
        [TextArea(1, 3)]
        public string flavorText = "";       // フレーバーテキスト
        public string skillName = "";        // スキル名
        
        [System.NonSerialized] 
        public Sprite icon;
        [System.NonSerialized] 
        public GameObject equipMarkPrefab;
        
        public ItemDataV2()
        {
            size = new ItemSize();
            buyPrice = new PriceRange();
            sellPrice = new PriceRange();
            weaponDice = new DiceConfig();
        }
        
        /// <summary>
        /// 武器かどうか
        /// </summary>
        public bool IsWeapon => category == ItemCategory.Weapon;
        
        /// <summary>
        /// パッシブアイテムかどうか
        /// </summary>
        public bool IsPassive => category == ItemCategory.Passive || category == ItemCategory.PassiveItem;
        
        /// <summary>
        /// クエストアイテムかどうか
        /// </summary>
        public bool IsQuest => category == ItemCategory.Quest;
        
        /// <summary>
        /// 現在の購入価格を取得
        /// </summary>
        public int GetCurrentBuyPrice() => buyPrice.GetRandomValue();
        
        /// <summary>
        /// 現在の売却価格を取得
        /// </summary>
        public int GetCurrentSellPrice() => sellPrice.GetRandomValue();
        
        // 後方互換性プロパティ（ItemDataV2レベル）
        /// <summary>
        /// ダイス情報（後方互換性）
        /// </summary>
        public DiceConfig weaponStats => IsWeapon ? weaponDice : null;
        
        /// <summary>
        /// 武器ダイス情報の有無
        /// </summary>
        public bool hasWeaponStats => IsWeapon && weaponDice != null;
        
        /// <summary>
        /// 経済データ（後方互換性）
        /// </summary>
        public EconomyData economy => new EconomyData 
        { 
            baseValue = GetCurrentSellPrice(),
            sellMultiplier = 1.0f,
            buyMultiplier = 1.0f 
        };
    }

    /// <summary>
    /// レアリティに基づく色取得ユーティリティ
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
                case ItemRarity.LEGENDARY: return new Color(1f, 0.5f, 0f); // オレンジ
                case ItemRarity.MYTHIC: return Color.cyan;
                default: return Color.gray;
            }
        }
    }

    /// <summary>
    /// 統合型アイテムデータ（後方互換性）
    /// </summary>
    public class CompleteItemData : ItemDataV2
    {
        public CompleteItemData()
        {
        }
        
        // 後方互換性プロパティ
        public string managementId 
        { 
            get => internalName; 
            set => internalName = value; 
        }
        
        public int sizeX 
        { 
            get => size.x; 
            set => size.x = value; 
        }
        
        public int sizeY 
        { 
            get => size.y; 
            set => size.y = value; 
        }
        
        // 武器ステータス（ダイスから計算）
        public int attack => IsWeapon ? weaponDice.RollDice() : 0;
        public int defense => 0; // 必要に応じて拡張
        public int health => 0;  // 必要に応じて拡張
        public int mana => 0;    // 必要に応じて拡張
        
        // 機能判定プロパティ
        public bool IsEquippable => category == ItemCategory.Weapon || category == ItemCategory.Armor;
        public bool IsUsable => category == ItemCategory.Consumable;
        public bool IsConsumable => category == ItemCategory.Consumable;
        public bool HasStats => IsWeapon || category == ItemCategory.Armor;
        
        // 後方互換性のためのプロパティ
        public string id => internalName; // idプロパティを追加
        
        // アセット参照
        public GameObject cardModel => fbxModel;
        public Sprite iconSprite => icon;
        public GameObject modelPrefab => fbxModel;
        public Sprite itemIcon => icon;
        
        // 経済データ
        public EconomyData economy => new EconomyData 
        { 
            baseValue = GetCurrentSellPrice(),
            sellMultiplier = 1.0f,
            buyMultiplier = 1.0f 
        };
        
        // ダイス情報（後方互換性）
        public DiceConfig weaponStats => IsWeapon ? weaponDice : null;
        public bool hasWeaponStats => IsWeapon && weaponDice != null;
    }

    /// <summary>
    /// 後方互換性のためのEconomyData
    /// </summary>
    [Serializable]
    public class EconomyData
    {
        public int baseValue = 0;
        public float sellMultiplier = 1.0f;
        public float buyMultiplier = 1.0f;
    }
}
