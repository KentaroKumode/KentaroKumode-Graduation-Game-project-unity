using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// ボスのダウン (2026-11-13・GAME.md §6-15): <b>「ダウン中は発動しない」を名乗る敵のパッシブの一覧</b> (opt-in)。
    ///
    /// <para>ダウンで止まるのは敵の攻撃だけ。 敵のパッシブは、 ダウン中も普段どおり動く (ユーザーの決定: 止まる物と止まらない物の線引きを説明できないので、
    /// 一括で止める案は取り消した・GAME.md §24)。 ギミックとして止めたいパッシブだけが、 ここに SkillId を足して名乗る。</para>
    ///
    /// <para><b>決まり</b>: ここに足したパッシブは、 enemies.json の説明文 (description) に「ダウン中は発動しない。」と書く
    /// (説明文と実装を 1 対 1 にする)。 足す時は GAME.md §6-15 の一覧にも書く。 いまは空 (= 誰も名乗らない・挙動は名乗りの口を足す前と同じ)。</para>
    ///
    /// <para>止める所は発火の入口 <c>PassiveSkillManager.FireEnemyTrigger</c> の 1 か所 (名乗ったパッシブはダウンのターンのどのトリガーでも鳴らない)。</para>
    /// </summary>
    public static class BossDownPassives
    {
        /// <summary>ダウン中は発動しない敵のパッシブ (SkillId)。 いまは空。</summary>
        private static readonly HashSet<string> SkipDuringDown = new HashSet<string>
        {
        };

        /// <summary>このパッシブが「ダウン中は発動しない」を名乗っているか。</summary>
        public static bool SkipsDuringDown(string skillId) => skillId != null && SkipDuringDown.Contains(skillId);
    }
}
