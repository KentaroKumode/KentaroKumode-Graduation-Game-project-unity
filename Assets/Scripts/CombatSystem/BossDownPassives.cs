using System.Collections.Generic;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>
    /// ボスのダウン (2026-11-13・GAME.md §6-15): ダウンのターンに<b>敵のパッシブを止める</b>規則の正本。
    /// 発火の入口 (<see cref="PassiveSkillManager.FireEnemyTrigger"/>) が、 ダウンのターン (<see cref="BossDown.ActiveNow"/>) に
    /// ここで「止めない」と言われた物だけを鳴らす。 パッシブごとに分岐を足さない。
    ///
    /// <para>基準: そのターンに敵が何かを<b>する</b>物 (こちらへのダメージ・状態の付与・自分の回復や強化・スタックを積む・
    /// 出目や端子への干渉・予兆) は止める。 常に効いている値 (被ダメの軽減の割合・追撃の無効など、 「そうである」物) は止めない。</para>
    /// </summary>
    public static class BossDownPassives
    {
        /// <summary>止めないトリガー: 敵が受けるダメージを軽くする (〈灰塵の外殻〉などの軽減の割合 = そうである物)・追撃の無効 (〈飛翔〉〈不動〉)・
        /// 戦闘の始め (段の交代で鳴る初期化・交代の時はダウンを解いている)。</summary>
        private static readonly HashSet<PassiveSkillTrigger> KeepTriggers = new HashSet<PassiveSkillTrigger>
        {
            PassiveSkillTrigger.OnPreReceiveDamage,
            PassiveSkillTrigger.OnPrePursuitDamage,
            PassiveSkillTrigger.OnBattleStart,
        };

        /// <summary>止めないパッシブ (どのトリガーでも): 戦闘の仕組みそのものと、 状態の記録だけをする物。
        /// <list type="bullet">
        /// <item>〈連続実験〉ContinuousExperiment: HP 0 で次の段へ移す予約 (止めると段の移行が遅れる)。</item>
        /// <item>〈天与の才〉DivineTalent: 閾値を割ったことの記録 (踏みとどまり自体は CombatManager が常に掛ける)。 止めると記録が遅れ、 割った閾値で踏みとどまり続ける。
        ///   「次のターン遺物 2 つ」は〈遺物学者〉の抽選なので、 そちらが止まっていれば明けたターンに回る (迷った・仮)。</item>
        /// <item>狂暴化 Berserk: T50 以降の「回復封印・受けるダメージ ×3」を毎ターン掛け直す (そうである物・止めるとダウンのターンだけ敵が固くなる・迷った・仮)。</item>
        /// </list></summary>
        private static readonly HashSet<string> KeepSkills = new HashSet<string>
        {
            "ContinuousExperiment",
            "DivineTalent",
            "Berserk",
        };

        /// <summary>ダウンのターンに、 この敵のパッシブを鳴らすか。</summary>
        public static bool Fires(string skillId, PassiveSkillTrigger trigger)
            => KeepTriggers.Contains(trigger) || (skillId != null && KeepSkills.Contains(skillId));
    }
}
