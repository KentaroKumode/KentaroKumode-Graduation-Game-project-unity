using System.Collections.Generic;
using CombatSystem;
using ItemSystem.PassiveSkills.Effects;

namespace AutoTest
{
    /// <summary>
    /// BOT (Optimal / Super の配線の見積もり) が読む、 武器のスキルの写し (2026-11-05・2026-11-21 武器のリワーク)。
    ///
    /// <para>率・定数は本体の効果クラス (WeaponFamilyEffects.cs) と <see cref="CombatRules"/> をそのまま読む ── 写しの数字を持つと本体を直した時に静かに古くなる。</para>
    /// </summary>
    public struct WeaponRungModel
    {
        public bool guardCut, undying, steadfast, daybreak, reckless, execute, vital2nd, muga, garyo;
        public int bashRatePct, bulwarkRatePct, bulwarkJust, duelPct, shatterPct, chainRatePct;
        public bool bashAll;

        /// <summary>所持している段から作る。 無い段は 0 / false。</summary>
        public static WeaponRungModel From(IReadOnlyList<string> owned)
        {
            var m = new WeaponRungModel();
            if (owned == null) return m;
            for (int i = 0; i < owned.Count; i++)
            {
                string id = owned[i];
                switch (id)
                {
                    case "切り返し": m.guardCut = true; continue;
                    case "不滅の刃": m.undying = true; continue;
                    case "堅守の構え": m.steadfast = true; continue;
                    case "黎明": m.daybreak = true; continue;
                    case "捨て身": m.reckless = true; continue;
                    case "処刑": m.execute = true; continue;
                    case "急所": m.vital2nd = true; continue;
                    case "無我無心": m.muga = true; continue;
                    case "画竜点睛": m.garyo = true; continue;
                }
                int s = System.Array.IndexOf(ShieldBash.Ids, id);
                if (s >= 0 && ShieldBash.RatePct[s] > m.bashRatePct) { m.bashRatePct = ShieldBash.RatePct[s]; m.bashAll = ShieldBash.SpendAll[s]; }
                s = System.Array.IndexOf(Bulwark.Ids, id);
                if (s >= 0 && Bulwark.RatePct[s] > m.bulwarkRatePct) { m.bulwarkRatePct = Bulwark.RatePct[s]; m.bulwarkJust = Bulwark.JustShield[s]; }
                s = System.Array.IndexOf(Duelist.Ids, id);
                if (s >= 0) m.duelPct = System.Math.Max(m.duelPct, Duelist.FinalPct[s]);
                s = System.Array.IndexOf(ArmorBreak.Ids, id);
                if (s >= 0) m.shatterPct = System.Math.Max(m.shatterPct, ArmorBreak.ShatterPct[s]);
                s = System.Array.IndexOf(ChainBlade.Ids, id);
                if (s >= 0) m.chainRatePct = System.Math.Max(m.chainRatePct, ChainBlade.RatePct[s]);
            }
            return m;
        }

        /// <summary>配線で決まる武器の最終与ダメージ +% (撃ごと・本体の各クラスが ctx.hitFinalDamagePct へ足す分の合計)。
        /// <paramref name="solo"/> = 単体戦 / <paramref name="execBoost"/> = 敵の残り HP が処刑の線以下 / <paramref name="shattered"/> = 〈鎧砕き〉で 0 にした後で防御 0 /
        /// <paramref name="undyingAfter"/> = このターンで続いた後の〈不滅の刃〉の連続数。</summary>
        public int FinalPct(bool solo, int aCount, int bCount, bool execBoost, bool shattered, int undyingAfter)
        {
            int p = 0;
            if (solo) p += duelPct;
            if (reckless && aCount > 0 && bCount == 0) p += Recklessness.Pct;
            if (execute && execBoost) p += CombatRules.ExecutionFinalPct;
            if (shattered) p += shatterPct;
            if (undying && aCount > 0 && bCount > 0) p += CombatRules.UndyingPct(undyingAfter);
            return p;
        }
    }
}
