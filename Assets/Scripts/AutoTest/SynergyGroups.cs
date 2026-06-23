namespace AutoTest
{
    /// <summary>
    /// シナジー探索（tier評価の別枠）の対象グループ定義。
    /// アイテム単体の lift では「セットで化ける品」を取りこぼすため、
    /// 既知のシナジーセットを登録し、 メンバー所持数 k 別の bandScore を別途集計する。
    ///
    /// member id は items.json の id（= RunRec.acquiredItemsEver に入る値）と一致させること。
    /// 新しいセットを追加したら All に1行足すだけ。
    /// </summary>
    public static class SynergyGroups
    {
        public class Group
        {
            public readonly string id;        // 集計キー（安定した英字ID）
            public readonly string name;      // 表示名
            public readonly string[] members; // メンバーの item id
            public readonly string note;      // 設計メモ（MDに併記）

            public Group(string id, string name, string[] members, string note = "")
            {
                this.id = id; this.name = name; this.members = members; this.note = note;
            }
        }

        public static readonly Group[] All =
        {
            new Group("sword_dance", "[剣の舞]", GameLoop.SwordDanceSet.All,
                "4枚集約で〈ブレイドダンス〉へ変化。サーベルは単独だと戦闘開始HP半減のリスクを負うため、soloΔが負・synΔが正なら設計通り（集める価値が立証される）。"),
            new Group("yokyo", "[佯狂者]", GameLoop.YokyoSet.All,
                "発狂(希望0)連動。フルセットで燃え尽き＋スケール。k増加でbandが伸びるほど、組み立てる価値が高い。"),
        };
    }
}
