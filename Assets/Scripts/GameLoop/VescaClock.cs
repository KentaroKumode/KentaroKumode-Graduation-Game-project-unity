using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// 博士の「止まらない時計」(2026-11-13・〈さびれた観測所・再訪・写し〉の 5 つ目の選択肢・docs/GAME.md §17 / §13-6)。
    ///
    /// <para><b>持ち方</b>: ランの中のフラグ (<see cref="RunState.ownedFlags"/> の <see cref="ItemIds.VescaClock"/>)。 ownedFlags はランの始め
    /// (<see cref="RunState.Initialize"/>) に空になるので次の潜行には持ち越さない。 中断セーブ (<see cref="RunSave"/>) は RunState を全部写すので、
    /// 戻しても残る。 測定の口 <see cref="GameHooks.VescaClockHeldAtStart"/> はランの始めにこのフラグを入れるだけ (同じ状態を通る)。</para>
    ///
    /// <para><b>効き目</b>: 持ったまま七層のヴェスカの戦闘 (4 つの段) に入ると、 その段のダウンの目盛りに HP 80% の目盛りを 1 つ足す
    /// (値の正本は <c>CombatSystem.BossParams.VescaClockNotch</c>・足す所は <c>CombatManager.ResetBossDown</c> の 1 か所)。
    /// 画面・BOT は、 戦闘が持つ目盛りの並び (<c>CombatManager.Down.notches</c>) をそのまま読む。</para>
    ///
    /// <para>文 (下の定数) は作者の確定文 (2026-11-13)。 書き換えない。</para>
    /// </summary>
    public static class VescaClock
    {
        /// <summary>ヴェスカの状態の欄に出す名前。</summary>
        public const string Name = ItemIds.VescaClock;
        /// <summary>ヴェスカの状態の欄に出す説明 (debuff_desc)。</summary>
        public const string Description = "体勢を崩してダウンする機会が、各段で一回ずつ増える。";
        /// <summary>最初の段 (VescaP1) の開始時に 1 回だけ出す地の文 (battle_line)。</summary>
        public const string BattleLine = "重くよどんだ底の空気の中で、懐の時計だけが休まず針を刻んでいる。";
        /// <summary>続けて出すヴェスカの台詞 (vesca_line)。</summary>
        public const string VescaLine = "「……聞き覚えのある音だな。まだ動いていたのか、その片割れは」";

        /// <summary>このランで時計を預かっているか。</summary>
        public static bool Held(RunState run) => run?.ownedFlags != null && run.ownedFlags.Contains(ItemIds.VescaClock);

        /// <summary>時計を預かる (イベント効果〈止まらない時計を預かる〉・測定の口から呼ぶ)。 乱数は引かない。</summary>
        public static void Take(RunState run)
        {
            if (run?.ownedFlags == null) return;
            if (run.ownedFlags.Add(ItemIds.VescaClock)) Debug.Log("[観測所] 止まらない時計を預かった (このランのヴェスカ戦で効く)");
        }
    }
}
