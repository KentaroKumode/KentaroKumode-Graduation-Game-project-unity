namespace CombatSystem
{
    /// <summary>出血の軸の品のうち、 端子の解決 (<see cref="TerminalCards.Resolve"/> の 5f) の中で効く物と、 本体の撃の輪・BOT の見積もりが読む物 (2026-11-21・full-item-rework-ledger §④)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.bleedItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="BleedCore.ItemsOf"/>)。</summary>
    [System.Flags]
    public enum BleedItems
    {
        None = 0,
        LeadAxe = 1 << 0,     // 鉛入りの握斧: 4 以上の目を挿した攻撃の端子の撃 出血 +2
        Pendant = 1 << 1,     // 血日に澄む佩玉: 出血 6 以上なら最初の撃 出血 +3・攻撃 +3
        Surgeon = 1 << 2,     // 医家の反り刃: HP が満タンか 50% 以下の敵への撃 出血 +2 (1 ターン 3 回)
        Clasp = 1 << 3,       // 返し刃の留め金: 攻撃の端子を 2 枚以上発動したターン 撃ごとに出血 +1
        Chipped = 1 << 4,     // 欠け刃のナイフ: 撃ごとに出血 +1
        Rift = 1 << 5,        // 裂け目の楔: 1〜3 の目を挿した攻撃の端子の撃 出血 +3
        Saw = 1 << 6,         // 鋸刃の山刀: 撃ごとに出血 +1 / +2 / +4 (敵の HP 50% 以上 / 未満 / 25% 未満)
        FirstBlood = 1 << 7,  // 先血の腕輪: 最初の撃 出血 +1
        Needles = 1 << 8,     // 血止め知らずの針箱: 2 撃目以降の撃の出血 +1
        OpenNeedle = 1 << 9,  // 医書裏の開き針: 出血 10 につき撃ごとに攻撃 +2
        Fury = 1 << 10,       // 激情の刃: 3 撃目以降の撃に 出血の半分 (最大 +15) の攻撃
        Cleaver = 1 << 11,    // 百人切りの鉈: 撃ごとに出血 +1・出血 12 以上で HP の線を下回れば処刑
        Sakura = 1 << 12,     // 血桜: 出血 12 以上なら 2 撃目以降 出血 2 を使い その撃の最終与ダメ +出血 × 2% (最大 +120%)
        Press = 1 << 13,      // 紅の搾り具: 出血 15 以上で撃が当たると 1/3 を使い × 8 の軽減不可
        Crystal = 1 << 14,    // 涸れぬ血晶: 出血 5 以上の時 与える出血 ×2
        Wedge = 1 << 15,      // 血の楔: 出血 20 以上で全部の撃の最終与ダメ +40%
    }

    /// <summary>
    /// 出血の軸 (2026-11-21・full-item-rework-spec §3 軸4) の式の<b>正本</b>。 撃を重ねるほど出血が増え、 出血の多い敵を殴るほど 1 撃が重くなる。
    ///
    /// <para><b>撃ごとの出血</b>: 攻撃の端子 1 枚 = 1 撃 (ADR-0016・【再起動】の撃も 1 撃)。 撃ごとに与える出血は 5f (<see cref="ApplyToTerminals"/>) が
    /// <see cref="TerminalOutcome.attackHitBleed"/> へ書き、 本体の撃の輪 (CombatManager.Turn) が<b>その撃が当たった直後</b>に敵へ入れる (次の撃は増えた出血を見る)。
    /// 敵の HP・出血の条件は<b>配線の時点</b>の値で見る (撃の輪の中では敵の HP はまだ減っていない)。 画面の見込みと BOT は同じ Resolve を通る。</para>
    /// <para>このファイルは UnityEngine に依存しない。</para>
    /// </summary>
    public static class BleedCore
    {
        /// <summary>5f の量。</summary>
        public const int LeadAxeBleed = 2, LeadAxeFace = 4, PendantNeed = 6, PendantBleed = 3, PendantAtk = 3, SurgeonBleed = 2, SurgeonMax = 3,
            ClaspBleed = 1, ClaspNeed = 2, ChippedBleed = 1, RiftBleed = 3, RiftFaceMax = 3, FirstBloodBleed = 1, NeedlesBleed = 1,
            OpenNeedleEvery = 10, OpenNeedleAtk = 2, FuryFromHit = 2, FuryMax = 15, CleaverBleed = 1;
        /// <summary>〈鋸刃の山刀〉: 敵の HP 50% 以上 / 50% 未満 / 25% 未満の出血。</summary>
        public const int SawHigh = 1, SawMid = 2, SawLow = 4;
        /// <summary>〈百人切りの鉈〉: 処刑に要る出血・出血 1 につき最大 HP の 1.2% (千分率 12)・上限 25% (ボスは半分にした後で頭打ち)。</summary>
        public const int CleaverNeed = 12, CleaverPermillePerBleed = 12, CleaverMaxPermille = 250;
        /// <summary>〈血桜〉: 要る出血・1 撃で使う出血・消費前の出血 1 につき +%・上限 %。</summary>
        public const int SakuraNeed = 12, SakuraSpend = 2, SakuraPctPer = 2, SakuraMaxPct = 120;
        /// <summary>〈紅の搾り具〉: 要る出血・使った出血 1 につきのダメージ。 使う量は 1/3 (切り上げ)。</summary>
        public const int PressNeed = 15, PressMul = 8;
        /// <summary>〈涸れぬ血晶〉: 与える出血が倍になる出血 (2026-11-22 neon 収束: 5 → 10)。</summary>
        public const int CrystalNeed = 10;
        /// <summary>〈血の楔〉: 全部の撃の最終与ダメ +% になる出血・その %。</summary>
        public const int WedgeNeed = 20, WedgePct = 40;

        /// <summary>〈紅の搾り具〉が使う出血 (1/3・切り上げ)。</summary>
        public static int PressSpend(int bleed) => bleed > 0 ? (bleed + 2) / 3 : 0;

        /// <summary>〈血桜〉: 消費前の出血 <paramref name="bleed"/> の撃の最終与ダメの上乗せ (%)・要る出血に届かなければ 0。</summary>
        public static int SakuraPct(int bleed) => bleed >= SakuraNeed ? System.Math.Min(SakuraMaxPct, bleed * SakuraPctPer) : 0;

        /// <summary>〈涸れぬ血晶〉: 今の出血 <paramref name="bleed"/> の時に与える出血 <paramref name="n"/> の実際の量。</summary>
        public static int CrystalGain(int n, int bleed, bool crystal) => crystal && bleed >= CrystalNeed ? n * 2 : n;

        /// <summary>〈百人切りの鉈〉: 出血 <paramref name="bleed"/> の時に処刑できる HP の線 (これ以下なら倒す・届かなければ −1)。</summary>
        public static int CleaverKillLine(int bleed, int maxHp, bool boss)
        {
            if (bleed < CleaverNeed || maxHp <= 0) return -1;
            long pm = (long)bleed * CleaverPermillePerBleed;
            if (boss) pm /= 2;
            pm = System.Math.Min(CleaverMaxPermille, pm);
            return (int)(maxHp * pm / 1000);
        }

        /// <summary>所持品から出血の品の旗を作る (戦闘の始め)。</summary>
        public static BleedItems ItemsOf(GameLoop.RunState run)
        {
            var f = BleedItems.None;
            void Own(string id, BleedItems b) { if (ItemSystem.PassiveSkills.AxisRules.Owns(run, id)) f |= b; }
            Own(GameLoop.ItemIds.BleedLeadAxe, BleedItems.LeadAxe); Own(GameLoop.ItemIds.BleedPendant, BleedItems.Pendant);
            Own(GameLoop.ItemIds.BleedSurgeon, BleedItems.Surgeon); Own(GameLoop.ItemIds.BleedClasp, BleedItems.Clasp);
            Own(GameLoop.ItemIds.BleedChipped, BleedItems.Chipped); Own(GameLoop.ItemIds.BleedRift, BleedItems.Rift);
            Own(GameLoop.ItemIds.BleedSaw, BleedItems.Saw); Own(GameLoop.ItemIds.BleedFirstBlood, BleedItems.FirstBlood);
            Own(GameLoop.ItemIds.BleedNeedles, BleedItems.Needles); Own(GameLoop.ItemIds.BleedOpenNeedle, BleedItems.OpenNeedle);
            Own(GameLoop.ItemIds.BleedFury, BleedItems.Fury); Own(GameLoop.ItemIds.BleedCleaver, BleedItems.Cleaver);
            Own(GameLoop.ItemIds.BleedSakura, BleedItems.Sakura); Own(GameLoop.ItemIds.BleedPress, BleedItems.Press);
            Own(GameLoop.ItemIds.BleedCrystal, BleedItems.Crystal); Own(GameLoop.ItemIds.BleedWedge, BleedItems.Wedge);
            return f;
        }

        /// <summary>Resolve の 5f (全部の発動・再起動が決まった後)。 撃ごとの出血 (<see cref="TerminalOutcome.attackHitBleed"/>) と撃ごとの攻撃 +N を書く (本体・画面の見込み・BOT が同じ)。
        /// <paramref name="dice"/> = 振った目 (挿した目そのもので見る)。 出血・敵の HP は配線の時点の値 (<paramref name="env"/>)。</summary>
        internal static void ApplyToTerminals(TerminalDef[] defs, int[] dice, in TerminalEnv env, TerminalBattleState st, TerminalOutcome o)
        {
            int n = o.attackHits.Count;
            o.attackHitBleed.Clear();
            for (int h = 0; h < n; h++) o.attackHitBleed.Add(0);
            var f = st.bleedItems;
            if (f == BleedItems.None || n == 0) return;
            int b = env.enemyBleed, hp = env.enemyHp, max = env.enemyMaxHp;
            int attackFired = 0;
            foreach (int s in o.fired) if (s >= 0 && s < defs.Length && defs[s]?.kind == TerminalKind.Attack) attackFired++;
            bool surgeonHp = max > 0 && (hp >= max || hp * 2 <= max);
            int saw = max <= 0 ? SawHigh : hp * 4 < max ? SawLow : hp * 2 < max ? SawMid : SawHigh;
            int surgeonLeft = SurgeonMax;
            for (int h = 0; h < n; h++)
            {
                int slot = h < o.attackHitSlot.Count ? o.attackHitSlot[h] : -1;
                int fMin = int.MaxValue, fMax = 0;
                for (int i = 0; i < dice.Length && i < o.slotOfDie.Length; i++)
                    if (o.slotOfDie[i] == slot) { if (dice[i] < fMin) fMin = dice[i]; if (dice[i] > fMax) fMax = dice[i]; }
                int g = 0, atk = 0;
                if ((f & BleedItems.Chipped) != 0) g += ChippedBleed;
                if ((f & BleedItems.Cleaver) != 0) g += CleaverBleed;
                if ((f & BleedItems.LeadAxe) != 0 && fMax >= LeadAxeFace) g += LeadAxeBleed;
                if ((f & BleedItems.Rift) != 0 && fMin >= 1 && fMin <= RiftFaceMax) g += RiftBleed;
                if ((f & BleedItems.Clasp) != 0 && attackFired >= ClaspNeed) g += ClaspBleed;
                if ((f & BleedItems.Saw) != 0) g += saw;
                if ((f & BleedItems.Surgeon) != 0 && surgeonHp && surgeonLeft > 0) { g += SurgeonBleed; surgeonLeft--; }
                if ((f & BleedItems.FirstBlood) != 0 && h == 0) g += FirstBloodBleed;
                if ((f & BleedItems.Pendant) != 0 && h == 0 && b >= PendantNeed) { g += PendantBleed; atk += PendantAtk; }
                if ((f & BleedItems.Needles) != 0 && h >= 1 && g > 0) g += NeedlesBleed;
                if ((f & BleedItems.OpenNeedle) != 0) atk += OpenNeedleAtk * (b / OpenNeedleEvery);
                if ((f & BleedItems.Fury) != 0 && h >= FuryFromHit) atk += System.Math.Min(FuryMax, b / 2);
                o.attackHitBleed[h] = g;
                if (atk > 0) { o.attackHits[h] += atk; o.attackAdd += atk; }
            }
        }
    }
}
