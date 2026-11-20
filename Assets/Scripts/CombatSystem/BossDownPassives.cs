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
    /// (説明文と実装を 1 対 1 にする)。 足す時は GAME.md §6-15 の一覧にも書く。</para>
    ///
    /// <para>止める所は発火の入口 <c>PassiveSkillManager.FireEnemyTrigger</c> の 1 か所 (名乗ったパッシブはダウンのターンのどのトリガーでも鳴らない)。</para>
    /// </summary>
    public static class BossDownPassives
    {
        /// <summary>ダウン中は発動しない敵のパッシブ (SkillId)。 2026-11-20 に 5 つ (docs/system-overhaul-spec.md §3-1)。
        /// 止めた時の後始末 (毒沼の主の毒 −2・ヴェスカの前のターンの遺物を消す) は <c>CombatManager.BeginBossDownTurn</c>。</summary>
        private static readonly HashSet<string> SkipDuringDown = new HashSet<string>
        {
            "MiasmaCorrosion",       // 毒沼の主〈毒の侵蝕〉(ダウンに入った時、 毒 −2)
            "MirrorTwinsResponse",   // 鏡の双子〈鏡映の応答〉
            "JudgmentFlames",        // 審判官〈審判の炎〉
            "AshRegrowth",           // 灰燼の王〈灰の再生〉
            "RelicScholar",          // ヴェスカ〈遺物学者〉
        };

        /// <summary>このパッシブが「ダウン中は発動しない」を名乗っているか。</summary>
        public static bool SkipsDuringDown(string skillId) => skillId != null && SkipDuringDown.Contains(skillId);
    }
}
