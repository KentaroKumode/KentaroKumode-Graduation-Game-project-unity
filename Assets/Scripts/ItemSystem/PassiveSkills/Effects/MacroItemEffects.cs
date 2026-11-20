using GameLoop;
using ItemSystem.PassiveSkills.Keywords;

namespace ItemSystem.PassiveSkills.Effects
{
    // ============================================================
    //  嵐・店・金のマクロ秘宝 (2026-11-20・item-overhaul-spec §9・名前は仮・数値は仮) の戦闘の側。
    //  数える側 (戦闘の外) は RunState の数 (receiptRemovals・abacusStacks) と、 嵐の段 (FreeMapSim.StormPhase)。
    //  戦闘の外だけで効く品 (風見鶏・天幕・手形・紹介状・鑑定書・交換帳) は効果クラスを持たない (GameManager / ShopManager が所持を見る)。
    // ============================================================

    /// <summary>気圧計のペンダント: 戦闘開始時の嵐の段 1 につき、 シールド +5・この戦闘の攻撃 +2 (段 3 ならシールド +15・攻撃 +6)。 段は戦闘開始時に読んで、 戦闘中は変えない。</summary>
    public class MacroBarometer : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.MacroBarometer;
        public const int ShieldPerPhase = 5, AttackPerPhase = 2;
        /// <summary>[persistent] 戦闘開始時の嵐の段。</summary>
        private const string PhaseKey = "macro.barometer.phase";
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(ctx != null ? Rw.Count(ctx, PhaseKey) : GameManager.Instance?.FreeMap?.StormPhase ?? 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart)
            {
                int ph = GameManager.Instance?.FreeMap?.StormPhase ?? 0;
                ctx.accumulatedValues[PhaseKey] = ph;
                if (ph <= 0) return;
                Rw.Shield(ctx, ShieldPerPhase * ph, SkillId);
                Rw.Log($"[気圧計のペンダント] 嵐の段 {ph}: シールド +{ShieldPerPhase * ph}・攻撃 +{AttackPerPhase * ph}");
                return;
            }
            int n = Rw.Count(ctx, PhaseKey);
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(AttackPerPhase * n);
        }
    }

    /// <summary>研ぎ代の領収書: 店または休憩で山札から端子を 1 枚抜くたび、 ランの間ずっと攻撃 +2 (累積・RunState.receiptRemovals・GameManager.NoteReceiptRemoval)。</summary>
    public class MacroReceipt : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.MacroReceipt;
        public const int PerRemoval = 2;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(PerRemoval * (Rw.Run?.receiptRemovals ?? 0));
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Run?.receiptRemovals ?? 0;
            if (n > 0) ctx.AddPlayerAttackOrDiceBonus(PerRemoval * n);
        }
    }

    /// <summary>相場師の算盤: 店に入って最初のリロール (軸指定を含む) をした時だけ、 以後の戦闘開始時に充電 +3・臨界 +5 (累積・このランの間・RunState.abacusStacks・ShopManager.TryReroll)。
    /// 設計書の「上限 10 回分」は、 積み上げを機会の少なさ (店の数) で止める形に変えた (合意 7)。</summary>
    public class MacroAbacus : IPassiveSkillEffect, IRelicDisplay
    {
        public string SkillId => ItemIds.MacroAbacus;
        public const int ChargePer = 3, HeatPer = 5;
        public RelicView View(CombatContext ctx, RelicEnv env) => RelicView.Count(Rw.Run?.abacusStacks ?? 0);
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int n = Rw.Run?.abacusStacks ?? 0;
            if (n <= 0) return;
            ctx.AddCharge(ChargePer * n);
            Kw.EnableHeat(ctx);
            Kw.AddHeat(ctx, HeatPer * n);
            Rw.Log($"[相場師の算盤] {n} 回分: 充電 +{ChargePer * n}・臨界 +{HeatPer * n}");
        }
    }
}
