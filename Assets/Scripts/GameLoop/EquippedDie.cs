using System.Collections.Generic;
using ItemSystem;

namespace GameLoop
{
    /// <summary>
    /// 装備している賽 (ADR-0015・2026-11-18)。 <b>持つ賽は 1 個で、 その 1 個を毎ターン 3 回振る。</b>
    ///
    /// <para>賽は items.json の品 (<c>category: "Dice"</c>・<c>diceFaces</c> が面) で、 id = 表示名。
    /// 装備は <see cref="RunState.equippedDiceId"/> の 1 つだけが持つ (空なら <see cref="ItemIds.StartingDie"/>)。
    /// 出目パーツ (面 36 種 × Tier・ADR-0011) と休憩で面を外す仕組みは 2026-11-18 にやめた。</para>
    ///
    /// <para><b>役</b>: Tier をやめ、 賽がその目の面を持っていれば 2 個そろいもゾロ目も効く (作者 2026-11-18・p9)。
    /// 役の判定 (<c>YachtRoles</c>) はまだ Tier を読むので、 ここで全部の面を T3 として渡している (手順 2 で役の作り直しと一緒に外す)。</para>
    /// </summary>
    public static class EquippedDie
    {
        /// <summary>最初の賽の面。 items.json が読めない文脈 (テスト・Run の無い所) で使う。</summary>
        public static readonly int[] StartingFaces = { 2, 3, 4, 5 };

        /// <summary>装備中の賽の id。 空なら最初の賽。</summary>
        public static string IdOf(RunState run)
            => run == null || string.IsNullOrEmpty(run.equippedDiceId) ? ItemIds.StartingDie : run.equippedDiceId;

        /// <summary>その id が賽か (items.json の category が Dice)。</summary>
        public static bool IsDie(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var it = ItemDatabase.Instance?.GetItem(id);
            return it != null && it.category == ItemCategory.Dice;
        }

        /// <summary>賽の面 (複製)。 読めなければ最初の賽の面。</summary>
        public static int[] FacesOf(string id)
        {
            var it = string.IsNullOrEmpty(id) ? null : ItemDatabase.Instance?.GetItem(id);
            var f = it != null ? it.diceFaces : null;
            return (int[])(f != null && f.Length > 0 ? f : StartingFaces).Clone();
        }

        /// <summary>面配列と、 面添字ごとの Tier を対で組む (戦闘の面の解決地点・GatherPlayerCombatStats)。
        /// 役は全部の面で 2 個そろいもゾロ目も効く (T3)。</summary>
        public static void Build(RunState run, out int[] faces, out DiceFaceParts.Tier[] tiers)
        {
            faces = FacesOf(IdOf(run));
            tiers = new DiceFaceParts.Tier[faces.Length];
            for (int i = 0; i < tiers.Length; i++) tiers[i] = DiceFaceParts.Tier.T3;
        }

        /// <summary>賽を装備する (店で買う・イベントで交換する・付与)。 前の賽は手元に残らない (持つのは 1 個)。</summary>
        public static void Equip(RunState run, string id)
        {
            if (run == null || !IsDie(id)) return;
            run.equippedDiceId = id;
        }

        /// <summary>表示名。</summary>
        public static string Label(string id)
        {
            var it = string.IsNullOrEmpty(id) ? null : ItemDatabase.Instance?.GetItem(id);
            return it != null && !string.IsNullOrEmpty(it.displayName) ? it.displayName : (id ?? "");
        }

        /// <summary>店に並べてよい賽 (最初の賽と、 いま装備している賽は並べない)。</summary>
        public static bool Offerable(CompleteItemData it, RunState run)
            => it != null && it.category == ItemCategory.Dice
               && it.internalName != ItemIds.StartingDie && it.internalName != IdOf(run);

        /// <summary>全部の賽の id (店に並ぶか・付与の対象かは呼び手が決める)。</summary>
        public static List<string> AllIds()
        {
            var list = new List<string>();
            var db = ItemDatabase.Instance;
            var items = db?.GetItemsByCategory(ItemCategory.Dice);
            if (items != null) foreach (var it in items) if (it != null) list.Add(it.internalName);
            return list;
        }
    }
}
