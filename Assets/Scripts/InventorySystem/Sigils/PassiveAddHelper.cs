using UnityEngine;
using GameLoop;
using InventorySystem.Sigils;

namespace InventorySystem.Helpers
{
    /// <summary>
    /// パッシブアイテム取得時に <see cref="RunState.ownedPassiveItems"/> と
    /// <see cref="RunState.passiveSigils"/> を同時更新するための薄いヘルパー。
    ///
    /// 既存コードに散らばっている <c>run.ownedPassiveItems.Add(id)</c> 呼び出しを
    /// なるべくこちらへ寄せて、刻印ロール忘れを防ぐ。
    /// </summary>
    public static class PassiveAddHelper
    {
        /// <summary>パッシブアイテムを所持リストへ追加。非ユニーク(internalName が "uniq_" で始まらない)
        /// なら刻印を1個ロールして並列リストに追加。</summary>
        /// <returns>付与された刻印 (ユニーク等で付かなかった場合は null)</returns>
        public static PassiveSigil AddPassiveItem(RunState run, string itemId)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return null;
            run.ownedPassiveItems.Add(itemId);

            // ユニーク扱いの ID は刻印ロール対象外
            if (itemId.StartsWith("uniq_"))
            {
                run.passiveSigils.Add(null);
                return null;
            }

            var sigil = SigilRoller.Roll();
            run.passiveSigils.Add(sigil);
            Debug.Log($"[Sigil] {itemId} に刻印付与: {sigil.DisplayLabel}");
            return sigil;
        }

        /// <summary>所持リストから index 指定で除去 (刻印側も同期)。</summary>
        public static void RemoveAt(RunState run, int index)
        {
            if (run == null || index < 0) return;
            if (index < run.ownedPassiveItems.Count)
                run.ownedPassiveItems.RemoveAt(index);
            if (index < (run.passiveSigils?.Count ?? 0))
                run.passiveSigils.RemoveAt(index);
        }
    }
}
