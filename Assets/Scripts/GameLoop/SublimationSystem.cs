using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 〈昇華〉システム。
    ///
    /// 強化素材(pt)を支払い、対象パッシブの【刻印を除去する代わりに】その主効果を
    /// 「永久パッシブ」(run.ascendedPassiveIds) としてグリッド外に付与する。グリッド枠が1個空く。
    ///
    /// - 対象: 刻印を持つ Passive カテゴリのみ（刻印を対価に永久化する＝刻印無し/uniq/チェーンは不可）。
    /// - コスト: 逓増 n個目 = n pt（ソフトキャップ。ハードキャップ無し）。素材収入と武器強化が分母。
    /// - 任意タイミング実行可（葛藤はタイミングでなくコスト＝逓増×武器との食い合いに宿す）。
    /// - 発動: RunPassiveSync / PassiveItemManager が owned∪ascended を走査して戦闘で適用。
    ///   容量/トリアージ/重複表示は owned のみ（ascended は枠を消費しない）。
    /// </summary>
    public static class SublimationSystem
    {
        /// <summary>確信チェーン等、 昇華で動かしてはいけないID（所持判定が壊れるため保護）。</summary>
        private static readonly HashSet<string> Protected = new HashSet<string>
        {
            "真理", "決意", "根拠のない確信", "苦難の予言", "苦難の確信", "真理の予兆",
        };

        /// <summary>次の昇華に必要なpt（n個目 = n pt）。</summary>
        public static int Cost(RunState run) => (run?.sublimationCount ?? 0) + 1;

        /// <summary>ownedIndex のパッシブを昇華可能か。</summary>
        public static bool CanSublimate(RunState run, int ownedIndex)
        {
            if (run?.ownedPassiveItems == null) return false;
            if (ownedIndex < 0 || ownedIndex >= run.ownedPassiveItems.Count) return false;
            string id = run.ownedPassiveItems[ownedIndex];
            if (string.IsNullOrEmpty(id) || Protected.Contains(id)) return false;
            // 刻印を持つもののみ（刻印を対価に永久化）。並列配列 passiveSigils[ownedIndex] が非null。
            if (run.passiveSigils == null || ownedIndex >= run.passiveSigils.Count
                || run.passiveSigils[ownedIndex] == null) return false;
            // パッシブカテゴリ限定（武器/ダイス/消費は対象外）
            var def = InventorySystem.ItemDatabase.Instance?.GetItem(id);
            if (def == null) return false;
            if (def.category != InventorySystem.ItemCategory.Passive
                && def.category != InventorySystem.ItemCategory.PassiveItem) return false;
            return run.weaponMaterials >= Cost(run);
        }

        /// <summary>昇華を実行。 成功で true。</summary>
        public static bool Sublimate(RunState run, int ownedIndex)
        {
            if (!CanSublimate(run, ownedIndex)) return false;
            string id = run.ownedPassiveItems[ownedIndex];
            int cost = Cost(run);
            // グリッドから除去（刻印も並列配列ごと除去＝刻印消滅）
            InventorySystem.Helpers.PassiveAddHelper.RemoveAt(run, ownedIndex);
            if (run.ascendedPassiveIds == null) run.ascendedPassiveIds = new List<string>();
            run.ascendedPassiveIds.Add(id);
            run.weaponMaterials -= cost;
            run.sublimationCount++;
            UnityEngine.Debug.Log($"[昇華] {id} を永久化 (コスト{cost}pt / 累計{run.sublimationCount} / 残素材{run.weaponMaterials})");
            return true;
        }
    }
}
