using System.Collections.Generic;

namespace InventorySystem.Shop
{
    /// <summary>
    /// イベント・フラグ専用アイテムのID集合。
    /// このリストに含まれる ID は ショップ/宝箱/ランダムドロップ から除外される。
    /// （特定のイベント選択でのみ獲得可能な「ストーリー的アイテム」）
    ///
    /// 登録済み:
    /// - 名前付きパッシブ: ちいさな灯火 / 決意 / 英雄の意志 / 血の盟約 / 幸運の硬貨 / 相棒の魂
    /// - フラグアイテム連鎖: 苦難の予言 / 苦難の確信 / 迷い犬の首輪 / 忠犬ノクト
    ///
    /// 注意: items.json に登録される時点で確定する ID と一致させること。
    /// 現状はイベントテキストの []内表記＝ID 想定。
    /// </summary>
    public static class EventOnlyItemFilter
    {
        public static readonly HashSet<string> ExcludedIds = new HashSet<string>
        {
            // 名前付き固有パッシブ（イベント限定獲得）
            "ちいさな灯火",
            "決意",
            "英雄の意志",
            "血の盟約",
            "幸運の硬貨",
            "相棒の魂",
            "巡礼者の杖",
            "記憶の砂時計",
            "激情の刃",
            "希望の灯片",
            "黄昏の懐中時計",
            "苦難の刻印",
            "死神の数珠",
            "嵐の徽章",
            "沈黙の剣帯",
            "灯心の鈴",

            // フラグアイテム連鎖（システム的に内部状態で扱うべき）
            "苦難の予言",
            "苦難の確信",
            "迷い犬の首輪",
            "忠犬ノクト",
        };

        /// <summary>このIDがランダム配布から除外されるか判定。</summary>
        public static bool IsExcluded(string id)
        {
            return !string.IsNullOrEmpty(id) && ExcludedIds.Contains(id);
        }

        /// <summary>このアイテムがランダム配布対象か判定。</summary>
        public static bool IsAllowed(CompleteItemData item)
        {
            if (item == null) return false;
            return !IsExcluded(item.internalName);
        }
    }
}
