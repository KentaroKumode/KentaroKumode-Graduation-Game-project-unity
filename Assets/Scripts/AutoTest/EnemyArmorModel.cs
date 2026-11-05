using System;
using CombatSystem;
using ItemSystem.PassiveSkills;
using ItemSystem.PassiveSkills.Effects;

namespace AutoTest
{
    /// <summary>
    /// 先読み BOT (Super / Exact) が「先のターンの敵」を再現するための規則の写し (2026-11-04)。
    ///
    /// <para>中身は全部<b>公開された規則</b>だけ: 大技・構え・先手の周期 (予告に出る)、 割れる/積み上がる装甲 (敵の説明に出る)。
    /// 先の出目は知らないので予知ではない。 計算は本体の関数 (<see cref="Escalation"/>・<see cref="DefenseFormula"/>) と
    /// パッシブの定数をそのまま使う ── 写しの数字を持つと、 本体を直した時に静かに古くなる。</para>
    ///
    /// <para>防御は「持続する分」(<c>eDef</c>・盤面の状態として持ち回る) と「構えの増減」(ターン番号だけで決まる) に分けて扱う。
    /// 本体の <c>CombatManager.ApplyGuardCycle</c> と同じ分け方。</para>
    /// </summary>
    public struct EnemyArmorModel
    {
        public EnemyData enemy;          // 読むだけ (周期・倍率)。 共有インスタンスなので書き換えない
        public int pen;                  // プレイヤーの貫通
        public bool cracking, stacking;  // 割れる装甲 / 積み上がる装甲
        public int armorBreak;           // 斧〈鎧砕き〉: ダメージを入れたターンの終わりに、 敵の防御から引く量 (所持していなければ 0)

        /// <summary>盤面から作る。 <paramref name="baseDef"/> = 今の防御から構えの増減を除いた「持続する分」。</summary>
        public static EnemyArmorModel From(EnemyData e, CombatContext ctx, out int baseDef)
        {
            var m = new EnemyArmorModel { enemy = e, pen = ctx != null ? Math.Max(0, ctx.playerArmorPen) : 0 };
            if (e?.passiveSkills != null)
                foreach (var p in e.passiveSkills)
                {
                    if (p == null) continue;
                    if (p.internalName == "CrackingArmor") m.cracking = true;
                    else if (p.internalName == "StackingArmor") m.stacking = true;
                }
            var owned = PassiveSkillManager.Instance?.ActivePlayerSkillIds;
            if (owned != null)
                for (int i = 0; i < owned.Count; i++)
                    for (int s = 0; s < ArmorBreakHelper.SkillIds.Length; s++)
                        if (owned[i] == ArmorBreakHelper.SkillIds[s]) m.armorBreak = Math.Max(m.armorBreak, ArmorBreakHelper.Amounts[s]);
            int now = ctx != null ? Math.Max(0, ctx.enemyDefense) : Math.Max(0, e?.defense ?? 0);
            float delta = 0f;
            if (ctx != null) ctx.accumulatedValues.TryGetValue("enemy.guardDelta", out delta);
            int d = (int)delta;
            // 構えのターン (d > 0): 今の防御から足した分を引く ／ 隙のターン (d < 0): 本体が引いた量 (= その時の持続分) を戻す
            baseDef = d >= 0 ? Math.Max(0, now - d) : -d;
            return m;
        }

        /// <summary>そのターンの防御 (構え: +guardDefense / 構えの次: 0)。</summary>
        public int DefAt(int eDef, int turn)
        {
            if (enemy == null || enemy.guardPeriod <= 0) return eDef;
            if (Escalation.IsGuardTurn(enemy, turn)) return eDef + Math.Max(0, enemy.guardDefense);
            if (Escalation.IsAfterGuardTurn(enemy, turn)) return 0;
            return eDef;
        }

        /// <summary>与ダメ (防御の前) を、 そのターンの防御で軽減した値。</summary>
        public int Mitigate(int dmg, int eDef, int turn)
            => DefenseFormula.Apply(dmg, DefenseFormula.Effective(DefAt(eDef, turn), pen));

        /// <summary>そのターンの防御の軽減率 (静的評価用)。</summary>
        public float ReductionAt(int eDef, int turn)
            => DefenseFormula.Reduction(DefenseFormula.Effective(DefAt(eDef, turn), pen));

        /// <summary>敵の攻撃値に掛かる、 周期の倍率 (大技 × 構え × 先手)。 段階の倍率は含まない。</summary>
        public float AttackFactor(int turn)
            => enemy == null ? 1f
             : Escalation.HeavyFactor(enemy, turn) * Escalation.GuardAttackFactor(enemy, turn) * Escalation.OpeningFactor(enemy, turn);

        /// <summary>周期の倍率の 1 周期ぶんの平均 (静的評価用・先手は短いので含めない)。</summary>
        public float AvgCycleFactor()
        {
            if (enemy == null) return 1f;
            float heavy = enemy.heavyPeriod <= 0 ? 1f
                : ((enemy.heavyPeriod - 1) * (enemy.heavyWindupMul > 0f ? enemy.heavyWindupMul : 1f) + (enemy.heavyMul > 0f ? enemy.heavyMul : 1f)) / enemy.heavyPeriod;
            float guard = enemy.guardPeriod <= 0 ? 1f
                : ((enemy.guardPeriod - 1) + (enemy.guardAttackMul > 0f ? enemy.guardAttackMul : 1f)) / enemy.guardPeriod;
            return heavy * guard;
        }

        /// <summary>ターンの終わり: 割れる装甲・自分の〈鎧砕き〉 (ダメージを入れたら −) / 積み上がる装甲 (+)。</summary>
        public int EndOfTurn(int eDef, bool damaged)
        {
            if (cracking && damaged) eDef = Math.Max(0, eDef - CrackingArmor.Crack);
            if (armorBreak > 0 && damaged) eDef = Math.Max(0, eDef - armorBreak);
            if (stacking && eDef < StackingArmor.Cap) eDef = Math.Min(StackingArmor.Cap, eDef + StackingArmor.Grow);
            return eDef;
        }
    }
}
