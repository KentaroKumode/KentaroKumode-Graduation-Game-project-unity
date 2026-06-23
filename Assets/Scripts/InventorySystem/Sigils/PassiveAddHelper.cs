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

            // パッシブアイテムの重複禁止: 現所持 or このランで取得済(売却/廃棄した物も含む)なら付与しない。
            // 武器/ダイス/消費は対象外（カテゴリで判定）。匿名取得("")やDB未登録は従来どおり許可。
            if (IsPassiveCategory(itemId)
                && (run.ownedPassiveItems.Contains(itemId)
                    || (run.seenPassiveItemIds != null && run.seenPassiveItemIds.Contains(itemId))))
            {
                Debug.Log($"[PassiveAdd] 重複のため取得スキップ: {itemId}");
                return null;
            }

            run.ownedPassiveItems.Add(itemId);
            run.seenPassiveItemIds?.Add(itemId); // 以後の重複取得を禁止（廃棄後も残す）

            // ユニーク扱いの ID は刻印ロール対象外
            if (itemId.StartsWith("uniq_"))
            {
                run.passiveSigils.Add(null);
                return null;
            }

            var sigil = SigilRoller.Roll();
            run.passiveSigils.Add(sigil);
            Debug.Log($"[Sigil] {itemId} に刻印付与: {sigil.DisplayLabel}");

            // [剣の舞] を取得し、インベントリに4枚揃ったら〈ブレイドダンス〉へ変化させる。
            // （Finale=ブレイドダンスは IsDance=false のため再帰しない）
            if (SwordDanceSet.IsDance(itemId))
                SwordDanceSet.TryTransform(run);

            return sigil;
        }

        /// <summary>itemId が「パッシブ」カテゴリか（重複禁止の対象）。DB未登録/匿名は false（=従来どおり許可）。</summary>
        private static bool IsPassiveCategory(string itemId)
        {
            var def = ItemDatabase.Instance?.GetItem(itemId);
            if (def == null) return false;
            return def.category == ItemCategory.Passive || def.category == ItemCategory.PassiveItem;
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
