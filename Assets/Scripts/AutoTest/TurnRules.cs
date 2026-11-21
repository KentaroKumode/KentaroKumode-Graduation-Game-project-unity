using ItemSystem.PassiveSkills.Effects;

namespace AutoTest
{
    /// <summary>
    /// BOT の 1 ターンの見積もりに使う、 <b>物理的な規則</b>だけを集めた純粋な関数 (2026-11-05)。
    ///
    /// <para>Optimal の配線評価 (<c>AutoRunner.Combat</c>) と Super の先読み (旧 <c>SuperCombatAI.ApplyTurn</c>・2026-11-12 に削除) が
    /// 同じゲームの規則を別々に書いていて、 片方にだけ入った規則 (武器の段・防ぎ切り) が取り残される原因になっていた。
    /// 規則の式はここ 1 箇所に置き、 両方が呼ぶ。 <b>効用の重みや価値判断 (ペルソナ・時間コスト・加点) はここに入れない</b> ──
    /// それは方策ごとに違ってよいもの。</para>
    ///
    /// <para>式の正本は本体の <see cref="CombatSystem.CombatRules"/> (2026-11-12〜)。 ここは BOT 向けの窓口で、 本体の効果クラスと同じ関数を呼ぶ。 式も数字も写しを持たない。</para>
    /// </summary>
    public static class TurnRules
    {
        /// <summary>剣〈切り返し〉: 攻撃と防御の端子を両方挿したターン、 ブロック合計 × 0.6 (切り上げ) を攻撃へ。</summary>
        public static int GuardCutBonus(int blockSum) => CombatSystem.CombatRules.GuardCutBonus(blockSum);

        /// <summary>盾〈シールドバッシュ〉: 防御なしのターン、 シールド (半分 / 全部) を使った量 × 率% を攻撃へ。</summary>
        public static int BashBonus(int spent, int ratePct) => CombatSystem.CombatRules.BashBonus(spent, ratePct);

        /// <summary>盾〈城壁〉: ジャスト防御なら固定量・完全に受け止めたら余ったブロック × 率%・それ以外 0。</summary>
        public static int BulwarkGain(int block, int enemyAtk, int ratePct, bool just, int justShield)
            => CombatSystem.CombatRules.BulwarkGain(block, enemyAtk, ratePct, just, justShield);

        /// <summary>盾〈黎明〉: HP が減らなかったターンに敵の最大 HP を削る量 (ジャスト防御なら 8%)。 敵の攻撃が <c>PerUnit</c> 未満なら 0。
        /// 呼ぶ側が「HP が減らなかった」を確かめてから足すこと。</summary>
        public static int DaybreakDamage(int enemyMaxHp, int enemyAtk, bool just)
        {
            int units = enemyAtk / Daybreak.PerUnit;
            return units > 0 ? CombatSystem.CombatRules.DaybreakCut(enemyMaxHp, units, just) : 0;
        }

        /// <summary>処刑人の烙印: 攻撃終了時の HP が最大の 20% 以下なら即死。 <paramref name="hpAfterHit"/> = 被弾後の HP。</summary>
        public static bool ExecuteKills(int hpAfterHit, int maxHp) => CombatSystem.CombatRules.ExecuteKills(hpAfterHit, maxHp);
    }
}
