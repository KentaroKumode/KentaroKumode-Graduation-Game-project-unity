namespace MetaProgression.Achievements
{
    /// <summary>実績の系統 (ギャラリーの画面の見出し)。 並びはこの順。 名前は <see cref="AchievementCatalog.CategoryName"/>。
    /// <b>値の数字はセーブに載らない</b> (定義の欄だけ) ので、 並べ替えてよい。</summary>
    public enum AchievementCategory
    {
        Progress = 0,     // 進行
        Ending,           // 結末
        Challenge,        // 挑戦カード
        CombatSkill,      // 戦闘の腕前
        Keyword,          // キーワード
        DiceRole,         // 出目と役
        Terminal,         // 端子
        ClassWeapon,      // 職業と武器
        Navigation,       // 航行
        ShopEconomy,      // 店と経済
        Restriction,      // 縛り
        RelicPanel,       // 遺物と整備パネル
        RecordEvent,      // 記録とイベント
        Play,             // 遊び
    }

    /// <summary>実績 1 個の定義 (正本は <see cref="AchievementCatalog"/>・docs/GAME.md §15-6 はその写し)。
    /// 解除の状態は持たない ── 解除済みか・いつ解除したかは <see cref="AchievementService"/> に聞く。</summary>
    public sealed class AchievementDefinition
    {
        /// <summary>保存に使う id。 <b>変えると解除済みが消える</b> (発売後に変える時は対応表を作る)。</summary>
        public readonly string id;
        /// <summary>名前 (取った後・隠しでない物は取る前も見せる)。</summary>
        public readonly string displayName;
        /// <summary>条件の文 (画面の層の番号で書く)。</summary>
        public readonly string description;
        /// <summary>隠し: 取るまで名前も条件も「？？？」 (<see cref="AchievementService.IsMasked"/>)。</summary>
        public readonly bool hidden;
        /// <summary>系統 (ギャラリーの見出し)。</summary>
        public readonly AchievementCategory category;
        /// <summary>並び順 (<see cref="AchievementCatalog.All"/> の中の位置・0 始まり)。 系統の中もこの順に並べる。</summary>
        public readonly int order;
        /// <summary>結末の番号 (END1〜5)。 結末の実績でなければ 0。 END1 = 英雄の帰還・END2 = 無明の剣・END3 = 灰の玉座・END4 = 帰路・END5 = 終始。</summary>
        public readonly int endingNo;
        /// <summary>難しさ ★1 (自然に取れる) 〜 ★5 (やり込み)。 目安で、 判定には使わない。</summary>
        public readonly int stars;

        public AchievementDefinition(string id, string displayName, string description, AchievementCategory category,
                                     int order, int stars, bool hidden = false, int endingNo = 0)
        {
            this.id = id;
            this.displayName = displayName;
            this.description = description;
            this.category = category;
            this.order = order;
            this.stars = stars;
            this.hidden = hidden;
            this.endingNo = endingNo;
        }

        /// <summary>結末の実績か。</summary>
        public bool IsEnding => endingNo > 0;
    }
}
