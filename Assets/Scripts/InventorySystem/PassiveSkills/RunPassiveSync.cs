using System.Collections.Generic;
using GameLoop;

namespace InventorySystem.PassiveSkills
{
    /// <summary>
    /// RunState.ownedPassiveItems と装備中アイテムを結合して
    /// PassiveSkillManager に同期するブリッジ。
    ///
    /// 名前付き固有パッシブ（PassiveItemRegistry）は別系統で run.ownedPassiveItems から
    /// 直接駆動されるため、ここでは ItemDatabase に登録があり passiveSkills を持つアイテムだけを処理する。
    /// </summary>
    public static class RunPassiveSync
    {
        /// <summary>
        /// 戦闘開始時に呼ぶ。装備品 + ラン中所持パッシブを統合して
        /// PassiveSkillManager.RefreshActiveSkills に流す。
        /// </summary>
        public static void RefreshFromRun(RunState run, ItemEquipHandler equip)
        {
            var list = new List<CompleteItemData>();
            var db = ItemDatabase.Instance;

            // 装備中の Weapon / Armor / Dice
            if (equip != null)
            {
                AddIfNotNull(list, equip.GetCurrentEquipment(ItemCategory.Weapon));
                AddIfNotNull(list, equip.GetCurrentEquipment(ItemCategory.Armor));
                AddIfNotNull(list, equip.GetCurrentEquipment(ItemCategory.Dice));
            }

            // ラン中所持パッシブ（イベント・ボス追加報酬・ショップ購入で増える）
            if (run != null && run.ownedPassiveItems != null && db != null)
            {
                foreach (var id in run.ownedPassiveItems)
                {
                    var data = db.GetItem(id);
                    if (data?.passiveSkills == null || data.passiveSkills.Count == 0) continue;

                    // 名前付き固有パッシブ（PassiveItemRegistry に登録済み）は PassiveItemManager で
                    // 別経路発動するため、PassiveSkillManager 側には流さず二重発火を防ぐ。
                    if (PassiveItems.PassiveItemRegistry.Get(id) != null) continue;

                    list.Add(data);
                }
            }

            PassiveSkillManager.Instance?.RefreshActiveSkills(list);
        }

        private static void AddIfNotNull(List<CompleteItemData> list, CompleteItemData item)
        {
            if (item != null) list.Add(item);
        }
    }
}
