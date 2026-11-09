using System.Collections.Generic;
using GameLoop;
using ItemSystem.PassiveSkills;
using UnityEngine;

namespace MetaProgression.Relics
{
    /// <summary>
    /// 持ち込んだ遺物 (最大 3 個) とセット効果を各システムへ渡す中央ヘルパー。 正本: docs/GAME.md §15-5 (2026-11-10 再編)。
    /// MetaBuffApplicator と同じ形 (静的 getter 群)。 呼び出し側の引数 (run, ctx) は旧仕様から残しているが、 今は使っていない
    /// (条件付きの効果は ctx だけを見る)。
    /// </summary>
    public static class RelicApplicator
    {
        /// <summary>[測定] 持ち込みを差し替える (null = セーブの持ち込みを使う)。 AutoRunner が測定のプリセットを入れる。</summary>
        public static List<Relic> Override;

        private static List<Relic> Equipped()
        {
            if (Override != null) return Override;
            var st = MetaProgressManager.Instance?.State;
            return st != null ? st.EquippedRelics() : _empty;
        }
        private static readonly List<Relic> _empty = new List<Relic>();

        private static int Step(RelicStat s)
        {
            int n = 0;
            foreach (var r in Equipped()) if (r != null) n += r.StepOf(s);
            return n;
        }
        /// <summary>段を合計してから実数値にする (段の表は線形なので、 1 個ずつ足しても同じ)。 充電だけは別 (下)。</summary>
        private static int Value(RelicStat s)
        {
            int v = 0;
            foreach (var r in Equipped())
            {
                if (r == null) continue;
                if (r.MainStat == s) v += RelicCatalog.ValueOf(s, r.MainStep);
                for (int i = 0; i < r.SubCount; i++) if (r.subStats[i] == (int)s) v += RelicCatalog.ValueOf(s, r.subSteps[i]);
            }
            return v;
        }

        /// <summary>指定セットを何個持ち込んでいるか。</summary>
        public static int SetCount(RelicSet set)
        {
            int n = 0;
            foreach (var r in Equipped()) if (r != null && r.Set == set) n++;
            return n;
        }
        public static bool Has2(RelicSet s) => SetCount(s) >= 2;
        public static bool Has3(RelicSet s) => SetCount(s) >= 3;

        // ============================================================
        //  ラン開始・戦闘開始に 1 回
        // ============================================================

        public static int GetMaxHpBonus(RunState run) => Value(RelicStat.MaxHp);

        /// <summary>防御 +N。 戦闘開始時に playerDefenseBase へ足す。</summary>
        public static int GetDefense() => Value(RelicStat.Defense);

        public static int GetOpeningCharge(RunState run)
        {
            int v = 0;
            foreach (var r in Equipped()) if (r != null && r.MainStat == RelicStat.Charge) v += RelicCatalog.ChargeOpening(r.MainStep);
            return v;
        }

        public static int GetOpeningBleed(RunState run)
            => Value(RelicStat.OpeningBleed) + (Has2(RelicSet.Kessho) ? RelicCatalog.Kessho2Bleed : 0);

        public static int GetOpeningPoison(RunState run)
            => Value(RelicStat.OpeningPoison) + (Has2(RelicSet.Shoki) ? RelicCatalog.Shoki2Poison : 0);

        /// <summary>敵の段階の遅れ (ターン)。 戦闘開始時に YachtRoleEffects.AddEscalationDelay で入れる。</summary>
        public static int GetEscalationDelay() => Value(RelicStat.EscalationDelay);

        // ============================================================
        //  戦闘中に毎ターン参照するもの
        // ============================================================

        public static int GetAttackBonus(RunState run, CombatContext ctx) => Value(RelicStat.Attack);

        /// <summary>与ダメ +N% (血晶 3 個: 出血 10 以上の敵へ +20% を含む)。</summary>
        public static float GetOutgoingPct(RunState run, CombatContext ctx)
        {
            float p = Value(RelicStat.DamagePct) * 0.01f;
            if (ctx != null && ctx.enemyBleedStacks >= RelicCatalog.Kessho3BleedMin && Has3(RelicSet.Kessho)) p += RelicCatalog.Kessho3DmgPct * 0.01f;
            return p;
        }

        public static float GetCritRatePct(RunState run, CombatContext ctx) => Value(RelicStat.CritRate) * 0.01f;

        /// <summary>会心倍率の加算 (基準 2.0 に対する %・心眼 2 個の +30% を含む)。</summary>
        public static float GetCritMultBonus(RunState run, CombatContext ctx)
            => (Value(RelicStat.CritMult) + (Has2(RelicSet.Shingan) ? RelicCatalog.Shingan2CritMultPct : 0)) * 0.01f * 2.0f;

        public static int GetArmorPen(RunState run, CombatContext ctx) => Value(RelicStat.ArmorPen);

        /// <summary>被ダメ −N% (割合軽減)。 念のため 0.85 で頭打ち。</summary>
        public static float GetDamageReductionPct(RunState run, CombatContext ctx)
            => Mathf.Min(0.85f, Value(RelicStat.DamageReduction) * 0.01f);

        /// <summary>吸血 (主攻撃のダメージの N%)。 ctx.lifestealPct へ毎ターン足す。</summary>
        public static float GetLifestealPct() => Value(RelicStat.Lifesteal) * 0.01f;

        /// <summary>血贄: 出血が与えたダメージの N% を回復 (敵味方問わず・今はプレイヤーの出血は無い)。</summary>
        public static float GetBloodOfferingPct() => Value(RelicStat.BloodOffering) * 0.01f;

        public static float GetRinkaiCurrentHpPct(RunState run, CombatContext ctx) => Value(RelicStat.Rinkai) * 0.01f;

        /// <summary>毎ターンの充電 (符の段 4 で +2・電流 2 個で +2)。</summary>
        public static int GetChargePerTurn(RunState run, CombatContext ctx)
        {
            int v = Has2(RelicSet.Denryu) ? RelicCatalog.Denryu2ChargePerTurn : 0;
            foreach (var r in Equipped()) if (r != null && r.MainStat == RelicStat.Charge) v += RelicCatalog.ChargePerTurn(r.MainStep);
            return v;
        }

        /// <summary>ブロックに挿した出目 +N%。</summary>
        public static float GetBlockDicePct() => Value(RelicStat.BlockDicePct) * 0.01f;

        /// <summary>シールドの獲得量 +N% (堅牢 2 個の +20% を含む)。 consShield の増加に掛ける。</summary>
        public static float GetShieldGainPct()
            => (Value(RelicStat.ShieldGainPct) + (Has2(RelicSet.Kenro) ? RelicCatalog.Kenro2ShieldGainPct : 0)) * 0.01f;

        /// <summary>臨界の獲得量 +N% (炉温 2 個)。</summary>
        public static float GetHeatGainPct() => Has2(RelicSet.Roon) ? RelicCatalog.Roon2HeatPct * 0.01f : 0f;

        /// <summary>爆発のたびのシールド (炉温 3 個)。</summary>
        public static int GetBurstShield() => Has3(RelicSet.Roon) ? RelicCatalog.Roon3BurstShield : 0;

        /// <summary>毒が残る割合の上乗せ (瘴気 3 個)。 上限は <see cref="RelicCatalog.PoisonKeepCap"/>。</summary>
        public static int GetPoisonKeepBonus() => Has3(RelicSet.Shoki) ? RelicCatalog.Shoki3KeepPct : 0;

        /// <summary>過充電の間の被ダメ −N (電流 3 個)。 ターンの始めに playerFlatDamageReduction へ足す。</summary>
        public static int GetOverchargeFlatReduce(CombatContext ctx)
            => ctx != null && ctx.IsOvercharged() && Has3(RelicSet.Denryu) ? RelicCatalog.Denryu3FlatReduce : 0;

        /// <summary>ターン始めの減衰で残るシールドの上乗せ % (堅牢 3 個)。</summary>
        public static int GetShieldKeepBonus() => Has3(RelicSet.Kenro) ? RelicCatalog.Kenro3ShieldKeepPct : 0;

        /// <summary>前のターンに会心が出たら、 このターンの会心率 +N% (心眼 3 個)。</summary>
        public static bool HasShingan3 => Has3(RelicSet.Shingan);
        public const string ShinganCritKey = "relic.shingan.lastCrit";

        // ============================================================
        //  表示
        // ============================================================

        public static string DescribeEquipped()
        {
            var eq = Equipped();
            if (eq.Count == 0) return "遺物なし";
            var parts = new List<string>();
            foreach (var r in eq) parts.Add(r.Describe());
            for (int s = 0; s < RelicCatalog.SetCount; s++)
            {
                int n = SetCount((RelicSet)s);
                if (n >= 2) parts.Add(RelicCatalog.SetNames[s] + " " + n + " 個");
            }
            return string.Join(" | ", parts);
        }
    }
}
