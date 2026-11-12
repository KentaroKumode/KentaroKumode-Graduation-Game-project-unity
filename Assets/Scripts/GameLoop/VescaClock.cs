using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 博士の「止まらない時計」(2026-11-13・〈さびれた観測所・再訪・写し〉の 5 つ目の選択肢・docs/GAME.md §17 / §6-15)。
    ///
    /// <para><b>持ち方</b>: <b>秘宝</b> (items.json の〈止まらない時計〉・<see cref="RunState.ownedPassiveItems"/> の <see cref="ItemIds.VescaClock"/>)。
    /// 2026-11-13 にフラグ (ownedFlags) から秘宝へ変えた ── 持ち物の画面に見えるように。 秘宝なのでランの始めに空になり、 次の潜行には持ち越さない。
    /// 中断セーブ (<see cref="RunSave"/>) は RunState を全部写すので、 戻しても残る。 入れるのは <see cref="Take"/> の 1 か所
    /// (イベント効果〈止まらない時計を預かる〉と測定の口 <see cref="GameHooks.VescaClockHeldAtStart"/>)。
    /// 配布の山に出ない (<c>ItemSystem.Shop.EventOnlyItemFilter</c>)・預かり物 (<see cref="ItemIds.Entrusted"/>: 図鑑は手に入れるまで枠も出さない)・
    /// 手放せない品 (<see cref="ItemIds.Undroppable"/>: 売れない・取り替えに出さない・ランダムに失わない)・進行用の品 (<see cref="ItemIds.ProgressionItems"/>: 〈門〉・大罪・BOT の学習から外す)。</para>
    ///
    /// <para><b>効き目</b>: 品そのものは何も発火しない (skill も axis も持たない)。 持ったまま七層のヴェスカの戦闘 (4 つの段) に入ると、
    /// その段のダウンの目盛りに HP 80% の目盛りを 1 つ足す (値の正本は <c>CombatSystem.BossParams.VescaClockNotch</c>・
    /// 足す所は <c>CombatManager.ResetBossDown</c> の 1 か所)。 画面・BOT は、 戦闘が持つ目盛りの並び (<c>CombatManager.Down.notches</c>) をそのまま読む。</para>
    ///
    /// <para>文 (下の定数) は作者の確定文 (2026-11-13)。 書き換えない。 ヴェスカの台詞は作者の決定で置かない (2026-11-13)。</para>
    /// </summary>
    public static class VescaClock
    {
        /// <summary>ヴェスカの状態の欄に出す名前。</summary>
        public const string Name = ItemIds.VescaClock;
        /// <summary>ヴェスカの状態の欄に出す説明 (debuff_desc)。 秘宝の説明 (items.json) と同じ中身を、 敵の側から言う。</summary>
        public const string Description = "体勢を崩してダウンする機会が、各段で一回ずつ増える。";
        /// <summary>最初の段 (VescaP1) の開始時に 1 回だけ戦闘ログに出す地の文 (battle_line)。</summary>
        public const string BattleLine = "重くよどんだ底の空気の中で、懐の時計だけが休まず針を刻んでいる。";

        /// <summary>このランで時計を秘宝として持っているか。</summary>
        public static bool Held(RunState run) => run?.ownedPassiveItems != null && run.ownedPassiveItems.Contains(ItemIds.VescaClock);

        /// <summary>時計を預かる (イベント効果〈止まらない時計を預かる〉・測定の口から呼ぶ)。 秘宝として足す (重複・取得済みは足さない)。 乱数は引かない。
        /// 足したら true。</summary>
        public static bool Take(RunState run)
        {
            if (run == null) return false;
            if (run.ownedPassiveItems == null) run.ownedPassiveItems = new System.Collections.Generic.List<string>();
            bool added = ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, ItemIds.VescaClock);
            if (added) Debug.Log("[観測所] 止まらない時計を預かった (秘宝・このランのヴェスカ戦で効く)");
            return added;
        }
    }
}
