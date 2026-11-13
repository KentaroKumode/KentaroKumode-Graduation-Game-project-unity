using System.Collections.Generic;
using ItemSystem.PassiveSkills.Effects;

namespace AutoTest
{
    /// <summary>
    /// 先読み BOT (Super) が先のターンを再現するための、 武器の段 (剣・盾) の効果の写し (2026-11-05)。
    ///
    /// <para>これが無かった間、 Super の先読みは〈切り返し〉〈不滅の刃〉〈シールドバッシュ〉〈城壁〉〈堅守の構え〉〈黎明〉が
    /// 一切起きないものとして動いており、 家系の強みを引き出す配線を取れなかった (同じシードで Optimal 253 件 対 Super 193 件)。
    /// 率・定数は本体の効果クラスのものをそのまま読む ── 写しの数字を持つと本体を直した時に静かに古くなる。</para>
    /// </summary>
    public struct WeaponRungModel
    {
        public bool guardCut, undying, steadfast, daybreak;
        public float bashRate, bulwarkRate;

        // ---- 配線で決まる軸の品 (2026-11-07)。 本体は OnWired で足すので ctx.attackDicePct (配線前) には入らない ----
        /// <summary>〈当て布の籠手〉: ブロック 2 個以上でシールド +3 (敵の攻撃の前に入る)。</summary>
        public bool paddedBracer;
        /// <summary>〈二股の導線〉: 充電端子の出目のこの割合が攻撃にも加わる (ctx.chargeToAttackPct)。</summary>
        public float chargeToAtkPct;
        /// <summary>〈盾押しの心得〉: 攻撃の端子を挿したターン、 シールドのこの割合が攻撃に加わる (堅牢 × 2%)。</summary>
        public float shieldPushPct;

        public bool Any => guardCut || undying || steadfast || daybreak || bashRate > 0f || bulwarkRate > 0f;

        /// <summary>所持している段から作る。 無い段は 0 / false。</summary>
        public static WeaponRungModel From(IReadOnlyList<string> owned)
        {
            var m = new WeaponRungModel();
            if (owned == null) return m;
            for (int i = 0; i < owned.Count; i++)
            {
                string id = owned[i];
                if (id == PaddedBracer.Id) m.paddedBracer = true;
                else if (id == "切り返し") m.guardCut = true;
                else if (id == "不滅の刃") m.undying = true;
                else if (id == "堅守の構え") m.steadfast = true;
                else if (id == "黎明") m.daybreak = true;
                else
                {
                    for (int s = 0; s < ShieldBashHelper.SkillIds.Length; s++)
                        if (id == ShieldBashHelper.SkillIds[s] && ShieldBashHelper.Rates[s] > m.bashRate) m.bashRate = ShieldBashHelper.Rates[s];
                    for (int s = 0; s < BulwarkHelper.SkillIds.Length; s++)
                        if (id == BulwarkHelper.SkillIds[s] && BulwarkHelper.Rates[s] > m.bulwarkRate) m.bulwarkRate = BulwarkHelper.Rates[s];
                }
            }
            return m;
        }
    }
}
