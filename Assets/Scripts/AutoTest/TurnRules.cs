using ItemSystem.PassiveSkills.Effects;
using UnityEngine;

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
        /// <summary>剣〈切り返し〉: 攻撃と防御の端子を両方挿したターン、 ブロック合計 × <see cref="GuardCut.Rate"/> を攻撃へ。</summary>
        public static int GuardCutBonus(int blockSum) => CombatSystem.CombatRules.GuardCutBonus(blockSum);

        /// <summary>盾〈シールドバッシュ〉: 全部攻撃のターン、 シールドを使い切って × 率 を攻撃へ。</summary>
        public static int BashBonus(int shield, float rate) => CombatSystem.CombatRules.BashBonus(shield, rate);

        /// <summary>剣〈不滅の刃〉が与ダメ倍率の池へ足す量 (+0.08 × 連続数)。 <paramref name="streakAfter"/> = このターンで続いた後の連続数。
        /// <b>本体は池 (ctx.outgoingDamageMultiplier・加算) へ足す</b> (WeaponFamilyEffects.UndyingBlade → Fam.Outgoing)。
        /// 池と別に掛けると、 池が大きいほど過大になる (#122)。</summary>
        public static float UndyingAdd(int streakAfter) => UndyingBlade.PerTurn * streakAfter;

        /// <summary>剣〈不滅の刃〉の与ダメ倍率 (池が 1 のときの値 = 1 + <see cref="UndyingAdd"/>)。 池が 1 でないなら
        /// <c>(池 + UndyingAdd) / 池</c> を使うこと (#122)。</summary>
        public static float UndyingMul(int streakAfter) => 1f + UndyingAdd(streakAfter);

        /// <summary>盾〈城壁〉: 完全に受け止めたら (ブロック ≥ 敵の攻撃)、 余ったブロック × 率 をシールドへ。 受け止められなければ 0。</summary>
        public static int BulwarkGain(int block, int enemyAtk, float rate)
            => CombatSystem.CombatRules.BulwarkGain(block, enemyAtk, rate);

        /// <summary>盾〈黎明〉: HP が減らなかったターンに敵の最大 HP を削る量。 敵の攻撃が <c>PerUnit</c> 未満なら 0。
        /// 呼ぶ側が「HP が減らなかった」を確かめてから足すこと。</summary>
        public static int DaybreakDamage(int enemyMaxHp, int enemyAtk)
        {
            if (Daybreak.NextAttackPctPerUnit > 0f) return 0;   // [2026-11-10 試験] 溜めて次の主攻撃へ乗せる形のときは、 敵の HP を直接削らない
            int units = enemyAtk / Daybreak.PerUnit;
            return units > 0 ? CombatSystem.CombatRules.DaybreakCut(enemyMaxHp, units) : 0;
        }

        /// <summary>[2026-11-10 試験] 盾〈黎明〉の溜める形 (<see cref="Daybreak.NextAttackPctPerUnit"/> &gt; 0): HP が減らなかったターンに溜まる
        /// 「次の主攻撃 +%」の価値を、 先のターンの 1 ターンの与ダメの見込み <paramref name="nextMain"/> × 溜まる率で見積もる。 溜める形でなければ 0。</summary>
        public static int DaybreakDeferredValue(float nextMain, int enemyAtk)
        {
            float pct = Daybreak.NextAttackPctPerUnit;
            int units = enemyAtk / Daybreak.PerUnit;
            if (pct <= 0f || units <= 0 || nextMain <= 0f) return 0;
            return Mathf.RoundToInt(nextMain * pct * units);
        }

        /// <summary>処刑人の烙印: 攻撃終了時の HP が最大の 20% 以下なら即死。 <paramref name="hpAfterHit"/> = 被弾後の HP。</summary>
        public static bool ExecuteKills(int hpAfterHit, int maxHp) => CombatSystem.CombatRules.ExecuteKills(hpAfterHit, maxHp);
    }
}
