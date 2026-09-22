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

            // 段階式進行の対象武器（剣/斧/短剣/盾/呪い）は静的パッシブを使わず、後で動的付与する。
            string weaponId = run?.equippedWeaponId;
            bool dynamicWeapon = WeaponProgression.IsProgressionWeapon(weaponId);

            // 装備中の Weapon / Armor / Dice（equipHandler 優先）
            CompleteItemData weaponItem = null, armorItem = null, diceItem = null;
            if (equip != null)
            {
                if (!dynamicWeapon) weaponItem = equip.GetCurrentEquipment(ItemCategory.Weapon);
                armorItem = equip.GetCurrentEquipment(ItemCategory.Armor);
                diceItem = equip.GetCurrentEquipment(ItemCategory.Dice);
            }

            // フォールバック: ItemEquipHandler が無い/未装備のヘッドレス実行(AutoRunner)では
            // RunState の自動装備IDから武器・ダイスのパッシブを解決する。
            // （これが無いと装備ダイス／非進行武器の passiveSkills がバッチで発火しない）
            if (db != null && run != null)
            {
                if (!dynamicWeapon && weaponItem == null && !string.IsNullOrEmpty(run.equippedWeaponId))
                    weaponItem = db.GetItem(run.equippedWeaponId);
                if (diceItem == null && !string.IsNullOrEmpty(run.equippedDiceId))
                    diceItem = db.GetItem(run.equippedDiceId);
            }

            AddIfNotNull(list, weaponItem);
            AddIfNotNull(list, armorItem);
            AddIfNotNull(list, diceItem);

            // ラン中所持パッシブ（イベント・ボス追加報酬・ショップ購入で増える）
            if (run != null && run.ownedPassiveItems != null && db != null)
            {
                foreach (var id in run.ownedPassiveItems)
                {
                    var data = db.GetItem(id);
                    if (data?.passiveSkills == null || data.passiveSkills.Count == 0) continue;
                    // **武器は装備枠からだけ効かせる (2026-09-20 修正)。** 購入・入手した武器も
                    //   ownedPassiveItems に入るため、 ここで拾うと (a) 装備していない予備の武器のスキルが発動し、
                    //   (b) 装備中の段階式武器は静的 skills と動的付与 (WeaponProgression) で二重に発火していた。
                    //   実測: 攻撃の 97% で武器が所持一覧にあり、 〈果たし合い〉はほぼ常時 2 重 (額面の 2 倍)。
                    if (data.category == ItemCategory.Weapon) continue;

                    // 名前付き固有パッシブ（PassiveItemRegistry に登録済み）は PassiveItemManager で
                    // 別経路発動するため、PassiveSkillManager 側には流さず二重発火を防ぐ。
                    if (PassiveItems.PassiveItemRegistry.Get(id) != null) continue;

                    list.Add(data);
                }
            }

            // 〈昇華〉済み永久パッシブ（グリッド外）も戦闘では owned と同様に発動させる。
            if (run != null && run.ascendedPassiveIds != null && db != null)
            {
                foreach (var id in run.ascendedPassiveIds)
                {
                    var data = db.GetItem(id);
                    if (data?.passiveSkills == null || data.passiveSkills.Count == 0) continue;
                    if (data.category == ItemCategory.Weapon) continue;   // 武器は装備枠からだけ (上と同じ)
                    if (PassiveItems.PassiveItemRegistry.Get(id) != null) continue; // ITimedEffectは別経路
                    list.Add(data);
                }
            }

            PassiveSkillManager.Instance?.RefreshActiveSkills(list);

            // 段階式進行武器: 家系＋段階(+plus)から算出したパッシブを動的に追加登録
            if (dynamicWeapon)
            {
                int plus = run?.weaponPlus ?? 0;
                foreach (var id in WeaponProgression.Compute(weaponId, plus))
                    PassiveSkillManager.Instance?.AddSkillById(id, WeaponProgression.DisplayName(id));
            }
        }

        private static void AddIfNotNull(List<CompleteItemData> list, CompleteItemData item)
        {
            if (item != null) list.Add(item);
        }
    }
}
