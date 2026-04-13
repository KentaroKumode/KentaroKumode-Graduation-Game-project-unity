using System;
using System.Collections.Generic;

namespace InventorySystem.Sigils
{
    /// <summary>
    /// 刻印アイテムの定義データ（JSONデシリアライズ用）
    /// 刻印は1×1サイズでグリッド上に配置し、
    /// 武器に隣接しているとき戦闘中ボーナスを付与する
    /// </summary>
    [Serializable]
    public class SigilItemData
    {
        public string id;              // 内部ID (例: "sigil_pursuit")
        public string displayName;     // 表示名 (例: "追撃の刻印")
        public string description;     // 説明文
        public string rarity;          // "common" / "rare" / "epic"
        public List<SigilEffect> effects; // 付与する効果リスト
    }

    /// <summary>
    /// 刻印が付与する個別の効果
    /// </summary>
    [Serializable]
    public class SigilEffect
    {
        /// <summary>
        /// 効果種別:
        /// pursuit, counter, might, fortitude, insight, vitality,
        /// bleed, threatReduce, diceFace, critBonus, healOnWin
        /// </summary>
        public string type;
        public int value;
    }
}
