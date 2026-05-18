using UnityEngine;
using CombatSystem;

namespace InventorySystem
{
    /// <summary>
    /// アイテム使用処理 - CombatManager統合版
    /// 
    /// <para><b>実装済み効果:</b></para>
    /// <list type="bullet">
    ///   <item>HP回復系: 小回復ポーション(+25), 回復ポーション(+50), 大回復ポーション(+100), 完全回復エリクサー(完全回復)</item>
    ///   <item>特殊効果: 魔法の巻物(完全回復 + 戦闘中MaxHP+10)</item>
    ///   <item>汎用効果: アイテム名・価格ベース自動効果判定</item>
    /// </list>
    /// 
    /// <para><b>システム統合:</b></para>
    /// - CombatManager: HP操作・戦闘状態管理
    /// - PassiveSkillManager: CombatContext同期
    /// - InventorySoundManager: 効果音再生
    /// 
    /// <para><b>使い方:</b></para>
    /// 1. UseItem() でアイテム使用開始
    /// 2. ApplyItemEffect() で効果適用
    /// 3. インベントリからアイテム自動削除
    /// </summary>
    public class ItemUseHandler : MonoBehaviour
    {
        /// <summary>
        /// アイテムを使用
        /// </summary>
        public void UseItem(CompleteItemData item, ItemSlot slot)
        {
            if (item == null || !item.IsUsable)
            {
                Debug.LogWarning("[ItemUseHandler] Item is not usable");
                return;
            }

            // 恒久デバフ「クェシナの怠惰」: 戦闘開始から3T間、消費アイテム使用不可
            if (item.category == ItemCategory.Consumable)
            {
                var run = GameLoop.GameManager.Instance?.Run;
                var combat = CombatManager.Instance;
                if (combat != null && combat.IsCombatActive
                    && combat.CurrentCombatTurn >= 1 && combat.CurrentCombatTurn <= 3
                    && MetaProgression.PermanentDebuffEffects.HasSloth(run))
                {
                    Debug.LogWarning($"[ItemUseHandler] {MetaProgression.PermanentDebuffIds.Sloth}: 戦闘開始から3T間は消費アイテム使用不可 (現在 {combat.CurrentCombatTurn}T)");
                    return;
                }
            }

            // 確認ダイアログ（後で実装）
            // TODO: WarningDialogで確認

            // 使用実行
            ExecuteUse(item, slot);
        }
        
        /// <summary>
        /// 使用を実行
        /// </summary>
        private void ExecuteUse(CompleteItemData item, ItemSlot slot)
        {
            // アイテム効果を適用
            ApplyItemEffect(item);
            
            // アイテムを削除
            if (slot != null)
            {
                int x = slot.GridX;
                int y = slot.GridY;
                CompleteItemData itemData = slot.ItemData; // アイテムデータを取得
                
                slot.ClearItem();
                
                // イベント発火
                if (itemData != null)
                {
                    InventoryManager.Instance?.RemoveItem(x, y, itemData);
                }
            }
            
            // 効果音
            InventorySoundManager.Instance?.PlayItemUse();
            
            Debug.Log($"[ItemUseHandler] Used: {item.displayName}");
        }
        
        /// <summary>
        /// アイテム効果を適用
        /// </summary>
        private void ApplyItemEffect(CompleteItemData item)
        {
            if (!item.IsConsumable)
            {
                Debug.LogWarning($"[ItemUseHandler] Non-consumable item used: {item.displayName}");
                return;
            }

            // CombatManagerインスタンスを取得
            var combatManager = CombatManager.Instance;
            if (combatManager == null)
            {
                Debug.LogWarning("[ItemUseHandler] CombatManager not found! Item effects may be limited.");
            }

            // アイテムID別効果処理
            bool effectApplied = ApplySpecificItemEffect(item.id, combatManager);
            
            if (!effectApplied)
            {
                // フォールバック: 汎用効果処理
                ApplyGenericItemEffect(item, combatManager);
            }

            Debug.Log($"[ItemUseHandler] ✨ Applied effect for: {item.displayName}");
        }

        /// <summary>
        /// 特定アイテムID用の効果処理
        /// </summary>
        /// <returns>効果が適用された場合true</returns>
        private bool ApplySpecificItemEffect(string itemId, CombatManager combatManager)
        {
            // 新消費アイテム体系（cons_* / uniq_*）は統一ルータへ。
            // 戦闘中なら ctx 即時、戦闘外なら RunState pending に振り分けられる。
            if (!string.IsNullOrEmpty(itemId)
                && (itemId.StartsWith("cons_") || itemId.StartsWith("uniq_")))
            {
                var run = GameLoop.GameManager.Instance?.Run;
                return GameLoop.Consumables.ApplyDirect(run, itemId);
            }

            switch (itemId)
            {
                case "magic_scroll":
                    // 古びた治癒の巻物: HP全回復 + 一時的MaxHP+5
                    if (combatManager != null)
                    {
                        int currentHP = combatManager.PlayerHP;
                        int maxHP = combatManager.PlayerMaxHP;

                        // HP全回復
                        int healAmount = maxHP - currentHP;
                        if (healAmount > 0)
                        {
                            combatManager.HealPlayer(healAmount);
                            Debug.Log($"[ItemUseHandler] 🧙‍♂️ 治癒の巻物: Full heal ({healAmount} HP)");
                        }

                        // 一時的MaxHP増加（戦闘中のみ）
                        if (combatManager.IsCombatActive)
                        {
                            combatManager.BoostPlayerMaxHP(5);
                            Debug.Log("[ItemUseHandler] 🧙‍♂️ 治癒の巻物: MaxHP boosted (+5)");
                        }
                    }
                    return true;

                case "healing_potion":
                    // 回復ポーション: HP+18
                    combatManager?.HealPlayer(18);
                    Debug.Log("[ItemUseHandler] 🧪 Healing Potion: Restored 18 HP");
                    return true;

                case "minor_healing_potion":
                    // 小回復ポーション: HP+8
                    combatManager?.HealPlayer(8);
                    Debug.Log("[ItemUseHandler] 🧪 Minor Healing Potion: Restored 8 HP");
                    return true;

                case "greater_healing_potion":
                    // 大回復ポーション: HP+35
                    combatManager?.HealPlayer(35);
                    Debug.Log("[ItemUseHandler] 🧪 Greater Healing Potion: Restored 35 HP");
                    return true;

                case "full_heal_elixir":
                    // 完全回復エリクサー: HP完全回復
                    if (combatManager != null)
                    {
                        int currentHP = combatManager.PlayerHP;
                        int maxHP = combatManager.PlayerMaxHP;
                        int healAmount = maxHP - currentHP;
                        
                        if (healAmount > 0)
                        {
                            combatManager.HealPlayer(healAmount);
                            Debug.Log($"[ItemUseHandler] ✨ Full Heal Elixir: Complete restoration ({healAmount} HP)");
                        }
                        else
                        {
                            Debug.Log("[ItemUseHandler] ✨ Full Heal Elixir used, but HP already full");
                        }
                    }
                    return true;

                default:
                    return false; // 未実装アイテム
            }
        }

        /// <summary>
        /// 汎用効果処理（未実装アイテム用フォールバック）
        /// </summary>
        private void ApplyGenericItemEffect(CompleteItemData item, CombatManager combatManager)
        {
            // アイテム名から効果を推測
            string itemName = item.displayName.ToLower();
            
            if (itemName.Contains("回復") || itemName.Contains("heal") || itemName.Contains("potion"))
            {
                // 回復系: ベース価格に基づくHP回復
                int healAmount = Mathf.Clamp(item.basePrice / 10, 10, 100);
                combatManager?.HealPlayer(healAmount);
                Debug.Log($"[ItemUseHandler] 🔄 Generic heal effect: {healAmount} HP (based on price: {item.basePrice})");
            }
            else if (itemName.Contains("力") || itemName.Contains("力強") || itemName.Contains("strength"))
            {
                // 強化系: 戦闘中一時バフ（今後実装）
                Debug.Log("[ItemUseHandler] 💪 Strength effect detected (TODO: implement buff system)");
            }
            else
            {
                // デフォルト効果: 小回復
                combatManager?.HealPlayer(20);
                Debug.Log($"[ItemUseHandler] ✨ Default consumable effect: 20 HP restored");
            }
        }
    }
}
