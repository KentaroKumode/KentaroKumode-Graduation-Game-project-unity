using System.Collections.Generic;

namespace InventorySystem.Shop
{
    /// <summary>
    /// イベント・フラグ専用アイテムのID集合。
    /// このリストに含まれる ID は ショップ/宝箱/ランダムドロップ から除外される。
    /// （特定のイベント選択でのみ獲得可能な「ストーリー的アイテム」）
    ///
    /// 登録済み:
    /// - 名前付きパッシブ: ちいさな灯火 / 決意 / 英雄の意志 / 幸運の硬貨 / 相棒の魂
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
            "根拠のない確信",
            "真理",
            "英雄の意志",
            "幸運の硬貨",
            "相棒の魂",
            "心軽めの巡礼杖",
            // "記憶の砂時計" 2026-07-17 削除
            "激情の刃",
            "希望の灯片",
            "十五年目の計測器",
            // "怪しい水" / "古い歯車" は 2026-07-17 削除
            // 以前 EventOnly に含まれていた 23 個（黄昏の懐中時計など6個 + 後続17個）は
            // 固有イベントを持たないため、通常プールへ参加可能とする目的で除外リストから外した。
            // 必要なら個別に再追加可能。

            // フラグアイテム連鎖（システム的に内部状態で扱うべき）
            "苦難の予言",
            "苦難の確信",
            "迷い犬の首輪",
            "忠犬ノクト",

            // [剣の舞] 4枚集約の変化先。通常入手・ショップ・ランダム配布の対象外（変化でのみ獲得）。
            "ブレイドダンス",

            // 職業スターター消耗品 (2026-06-28)。ラン開始時に選択職業へ 1 個配布する専用品。
            // basePrice=0 のためショップに漏れると 0G 陳列になる ── 必ず除外を維持すること。
            GameLoop.ClassStarter.PolishId,
            GameLoop.ClassStarter.OathId,
            GameLoop.ClassStarter.PainkillerId,
            GameLoop.ClassStarter.DaggerId,

            // シュヴァリエのレイピア (剣聖撃破の固有ドロップ。ランダム配布対象外)
            GameLoop.ItemIds.ChevalierRapier,
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
