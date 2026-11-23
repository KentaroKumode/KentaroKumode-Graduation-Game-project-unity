namespace CombatSystem
{
    /// <summary>臨界の軸の品のうち、 爆発の式・端子の解決 (<see cref="TerminalCards.Resolve"/> の 5g)・BOT と画面の見込みが読む物 (2026-11-21・full-item-rework-ledger §⑤)。
    /// 戦闘の始めに所持品から <see cref="TerminalBattleState.heatItems"/> へ入れる (CombatManager.BeginTerminalDeck・<see cref="ItemsOf"/>)。</summary>
    [System.Flags]
    public enum HeatItems
    {
        None = 0,
        Stone = 1 << 0,        // 過熱石: 攻撃の端子に 2 個以上挿したターン 臨界 +6 (3 個とも攻撃なら +12)
        Breastplate = 1 << 1,  // 燻りの革胸当て: 防御の端子を発動するたび 臨界 +4・ブロック +2
        Poker = 1 << 2,        // 火守りの火搔き棒: 攻撃以外の端子に挿したダイス 1 個につき 臨界 +6
        Spanner = 1 << 3,      // 点火スパナ: 撃ごとに 臨界 +4
        Whetstone = 1 << 4,    // 荒砥: 撃ごとに 臨界 +2
        Oven = 1 << 5,         // 恒温の竈: 戦闘開始時 閾値の半分から・爆発の後は 8 から
        Primer = 1 << 6,       // 二度詰めの雷管: 爆発のたび この戦闘の爆発の基礎 +4 (最大 +32)
        Coal = 1 << 7,         // 焚き足しの炭: 臨界を得るたび +1 (1 ターン 6 回)
        Mold = 1 << 8,         // 溢れ取りの鋳型: 溢れた臨界 1 につき爆発 +2
        Ledger = 1 << 9,       // 帳簿外の爆薬: 閾値ぶんだけ減らして続けて爆発 (1 ターン 5 回)
        Kettle = 1 << 10,      // 早沸かし釜: 閾値 −6
        Spark = 1 << 11,       // 火花読みの眼鏡: 爆発は敵の防御を全部無視・爆発の回数 × 5% を掛ける
        Ashen = 1 << 12,       // 八日目の灰燼: 臨界 40 以上で起きた爆発 ×1.6
        Flint = 1 << 13,       // 燧石の指輪: 攻撃 3 撃以上 (【再起動】を含む) のターン 臨界の獲得 ×2・爆発のたび次の爆発 +25%
        Kiln = 1 << 14,        // 暴れ窯の火床: 超臨界爆発 ×2.0・超臨界のたび閾値 −4 (最大 −16)
        Pillar = 1 << 15,      // 柱折りの発破筒: 爆発のひびを即座に正規のダウンへ割る・ダウン中の敵への爆発 ×2.0
    }

    /// <summary>
    /// 臨界の軸 (2026-11-21・full-item-rework-spec §3 軸5) の式の<b>正本</b>。 本体 (Keywords.Kw.AddHeat・CombatManager.LandBurst)・
    /// BOT の配線の見積もり・画面の見込みは同じ <see cref="Gain"/> / <see cref="TryBurst"/> / <see cref="Forecast"/> を通る。
    ///
    /// <para><b>爆発</b>: 臨界が閾値 (既定 <see cref="Threshold"/>) に届くと、 その場で基礎 <see cref="BurstBase"/> のダメージ
    /// (敵の防御の <see cref="DefensePenPct"/>% を無視) を出し、 ボスの次の目盛りにひびを入れる。 爆発が起きたターンは【過熱】。
    /// 超臨界爆発 = 閾値の <see cref="SuperAtPct"/>% 以上の臨界で起きた爆発 (×1.5・〈暴れ窯の火床〉で ×2.0)。</para>
    /// <para>このファイルは UnityEngine に依存しない。</para>
    /// </summary>
    public static class HeatCore
    {
        // 閾値 30 → 36 (2026-11-22 段 3 の較正 4・臨界の腕が寄せなしの約 2 倍 / ターンの終わりの攻撃の合計が毎ターン溜まる)
        // → 32 (2026-11-22 neon 収束: ターンの終わりの臨界を攻撃の合計から「攻撃に挿した出目の合計の半分」へ下げたのと合わせて)
        public const int Threshold = 32, ThresholdFloor = 12, BurstBase = 25, DefensePenPct = 50;   // 爆発の基礎 30 → 24 (final6) → 26 (answer9) → 25 (answer12)
        public const int SuperAtPct = 150, SuperMulPct = 165, KilnSuperMulPct = 260;   // 超臨界 ×1.5 → ×1.65 (answer9)・暴れ窯 ×2.0 → ×2.5 (2026-11-22 final4) → ×2.0 (final5) → ×2.25 (final6) → ×2.60 (answer12)
        public const int StoneTwo = 8, StoneAll = 16, BreastHeat = 4, BreastBlock = 2, PokerPerDie = 6, SpannerPerHit = 3, WhetPerHit = 2;   // 過熱石 +6/+12 → +8/+16・スパナ +2 → +4・荒砥 +1 → +2 (final4)・スパナ +4 → +3 (final6)
        public const int OvenRestart = 8, PrimerAdd = 4, CoalAdd = 1, CoalMax = 6, MoldPerExcess = 3, LedgerMax = 5, KettleCut = 4;   // 炭 +2 → +1・釜 −8 → −6 (2026-11-22 段 3 の較正 2)・雷管 +6/48 → +4/32・鋳型 +3 → +2 (neon 収束)・雷管 +4/32 → +6/上限なし (final4) → +3/上限なし (final5) → +5 (answer9)・answer12: 雷管 +4・鋳型 +3・釜 −4
        public const int SparkPctPerBurst = 10,   // 2026-11-22 段 3 の較正 2: 15 → 8 (長い戦闘で伸び続ける) → neon 収束 5 → final4 8 → final5 5 → answer9 7 → answer13 10
                          AshenNeed = 40, AshenMulPct = 175, FlintPctPerBurst = 25, KilnStep = 4, KilnMax = 16, PillarDownMulPct = 250;   // 柱折り ×2.0 → ×2.5 (final6)
        /// <summary>〈燧石の指輪〉: 攻撃の撃 (攻撃の端子の発動・【再起動】の撃も 1 つ) がこの数以上のターン (2026-11-22 neon 収束: 攻撃の端子 2 枚 → 3 撃)。</summary>
        public const int FlintAttackHits = 3;
        /// <summary>ターンの終わりの臨界 = 攻撃の端子に挿した出目の合計 (<see cref="TerminalOutcome.attackPips"/>) ÷ これ (切り捨て・2026-11-22 neon 収束: 旧 攻撃の端子の合計 attackAdd)。</summary>
        public const int EndGainDiv = 2;
        /// <summary>ターンの終わりに得る臨界 (本体 CombatManager.Turn・<see cref="Forecast"/> が同じ式)。 基礎攻撃・端子の倍率・品の攻撃 +N は入らない。</summary>
        public static int EndGain(TerminalOutcome o) => o == null ? 0 : o.attackPips / EndGainDiv;

        /// <summary>[persistent・ctx の accumulatedValues] 〈暴れ窯の火床〉で下げた閾値 / [ターンごと] このターンの爆発の回数・〈焚き足しの炭〉を使った回数 / 〈燧石の指輪〉が働くターン。</summary>
        public const string KilnCutKey = "heat.kilnCut", TurnBurstsKey = "heat.turnBursts", CoalKey = "heat.coal", DoubleTurnKey = "heat.doubleTurn";

        /// <summary>1 回の爆発を決める盤面 (本体は ctx から <see cref="Load"/> で作り、 BOT・画面は写しで <see cref="Forecast"/> へ渡す)。</summary>
        public struct State
        {
            public HeatItems items;
            public int meter, threshold, kilnCut, coalUsed, burstsTurn, burstsTotal, stack, flat, enemyDefense;
            public bool doubled, enemyDown;
            /// <summary>遺物〈炉温〉2 個: 臨界の獲得量 +% (切り上げ)。</summary>
            public float relicPct;
        }

        /// <summary>1 回の爆発。 damage = 敵の防御の前 / landed = 敵の防御の後 (最終与ダメ倍率・ダウンの ×1.5 は HP を減らす口 HpLossDiag が掛ける)。</summary>
        public struct Burst
        {
            public int threshold, meterAt, excess, damage, landed;
            public bool super;
        }

        public static bool Has(HeatItems f, HeatItems b) => (f & b) != 0;

        /// <summary>所持品から臨界の品の旗を作る (戦闘の始め)。</summary>
        public static HeatItems ItemsOf(GameLoop.RunState run)
        {
            var f = HeatItems.None;
            void Own(string id, HeatItems b) { if (ItemSystem.PassiveSkills.AxisRules.Owns(run, id)) f |= b; }
            Own(GameLoop.ItemIds.HeatStone, HeatItems.Stone); Own(GameLoop.ItemIds.HeatBreastplate, HeatItems.Breastplate);
            Own(GameLoop.ItemIds.HeatPoker, HeatItems.Poker); Own(GameLoop.ItemIds.HeatSpanner, HeatItems.Spanner);
            Own(GameLoop.ItemIds.HeatWhetstone, HeatItems.Whetstone); Own(GameLoop.ItemIds.HeatOven, HeatItems.Oven);
            Own(GameLoop.ItemIds.HeatPrimer, HeatItems.Primer); Own(GameLoop.ItemIds.HeatCoal, HeatItems.Coal);
            Own(GameLoop.ItemIds.HeatMold, HeatItems.Mold); Own(GameLoop.ItemIds.HeatLedger, HeatItems.Ledger);
            Own(GameLoop.ItemIds.HeatKettle, HeatItems.Kettle); Own(GameLoop.ItemIds.HeatSpark, HeatItems.Spark);
            Own(GameLoop.ItemIds.HeatAshen, HeatItems.Ashen); Own(GameLoop.ItemIds.HeatFlint, HeatItems.Flint);
            Own(GameLoop.ItemIds.HeatKiln, HeatItems.Kiln); Own(GameLoop.ItemIds.HeatPillar, HeatItems.Pillar);
            return f;
        }

        /// <summary>戦闘の始めの閾値 (〈早沸かし釜〉−6・下限 <see cref="ThresholdFloor"/>)。</summary>
        public static int StartThreshold(HeatItems f) => System.Math.Max(ThresholdFloor, Threshold - (Has(f, HeatItems.Kettle) ? KettleCut : 0));

        /// <summary>臨界を n 得る (〈焚き足しの炭〉+1 → 〈燧石の指輪〉×2 → 遺物 +%)。 戻り値 = 実際に足した量。</summary>
        public static int Gain(ref State s, int n)
        {
            if (n <= 0 || s.threshold <= 0) return 0;
            if (Has(s.items, HeatItems.Coal) && s.coalUsed < CoalMax) { n += CoalAdd; s.coalUsed++; }
            if (s.doubled) n *= 2;
            if (s.relicPct > 0f) n += (int)System.Math.Ceiling(n * s.relicPct);
            s.meter += n;
            return n;
        }

        /// <summary>閾値に届いていれば 1 回爆発する (臨界・回数・〈二度詰めの雷管〉・〈暴れ窯の火床〉の閾値を進める)。 届いていない・〈帳簿外の爆薬〉の 1 ターン 5 回に達していれば false。</summary>
        public static bool TryBurst(ref State s, out Burst b)
        {
            b = default;
            if (s.threshold <= 0 || s.meter < s.threshold) return false;
            bool chain = Has(s.items, HeatItems.Ledger);
            if (chain && s.burstsTurn >= LedgerMax) return false;
            b.threshold = s.threshold; b.meterAt = s.meter;
            b.super = (long)s.meter * 100 >= (long)s.threshold * SuperAtPct;
            b.excess = chain ? 0 : s.meter - s.threshold;
            s.meter = chain ? s.meter - s.threshold : 0;
            if (Has(s.items, HeatItems.Oven)) s.meter = System.Math.Max(s.meter, OvenRestart);
            s.burstsTurn++; s.burstsTotal++;
            b.damage = Damage(in s, in b);
            b.landed = VsDefense(b.damage, s.enemyDefense, s.items);
            if (Has(s.items, HeatItems.Primer)) s.stack += PrimerAdd;
            if (b.super && Has(s.items, HeatItems.Kiln) && s.kilnCut < KilnMax)
            {
                int c = System.Math.Min(KilnStep, KilnMax - s.kilnCut);
                s.kilnCut += c; s.threshold = System.Math.Max(ThresholdFloor, s.threshold - c);
            }
            return true;
        }

        /// <summary>爆発 1 回の敵の防御の前のダメージ (<see cref="TryBurst"/> が回数を進めた後に呼ぶ)。
        /// 基礎 30 + 〈二度詰めの雷管〉+ 〈天工開物〉+ 〈溢れ取りの鋳型〉→ 超臨界 → 〈火花読みの眼鏡〉→ 〈燧石の指輪〉→ 〈八日目の灰燼〉→ 〈柱折りの発破筒〉(切り捨て)。</summary>
        public static int Damage(in State s, in Burst b)
        {
            long d = BurstBase + CardRules.BurstBaseAdd + s.stack + s.flat + (Has(s.items, HeatItems.Mold) ? MoldPerExcess * b.excess : 0);   // 挑戦カード〈予熱〉III: 基礎 +10
            if (b.super) d = d * (Has(s.items, HeatItems.Kiln) ? KilnSuperMulPct : SuperMulPct) / 100;
            if (Has(s.items, HeatItems.Spark)) d = d * (100 + SparkPctPerBurst * s.burstsTotal) / 100;
            if (Has(s.items, HeatItems.Flint) && s.doubled) d = d * (100 + FlintPctPerBurst * (s.burstsTurn - 1)) / 100;
            if (Has(s.items, HeatItems.Ashen) && b.meterAt >= AshenNeed) d = d * AshenMulPct / 100;
            if (Has(s.items, HeatItems.Pillar) && s.enemyDown) d = d * PillarDownMulPct / 100;
            return (int)System.Math.Min(int.MaxValue, d);
        }

        /// <summary>爆発のダメージを敵の防御に通す (防御の <see cref="DefensePenPct"/>% を無視・〈火花読みの眼鏡〉は全部無視)。</summary>
        public static int VsDefense(int dmg, int enemyDefense, HeatItems f)
            => Has(f, HeatItems.Spark) ? dmg : DefenseFormula.Apply(dmg, enemyDefense - enemyDefense * System.Math.Min(100, DefensePenPct + CardRules.BurstPenAddPct) / 100);   // 挑戦カード〈予熱〉: 無視 +N% (100% まで)

        /// <summary>Resolve の 5g (全部の発動・再起動が決まった後)。 配線で決まる臨界 (<see cref="TerminalOutcome.heatGains"/>・1 つが 1 回の「臨界を得る」)・
        /// 〈燻りの革胸当て〉のブロック・〈燧石の指輪〉の倍 (<see cref="TerminalOutcome.heatDouble"/>) を書く (本体・画面の見込み・BOT が同じ)。</summary>
        internal static void ApplyToTerminals(TerminalDef[] defs, int[] dice, TerminalBattleState st, TerminalOutcome o)
        {
            var f = st.heatItems;
            if (f == HeatItems.None) return;
            int blkFired = 0;
            foreach (int s in o.fired) if (s >= 0 && s < defs.Length && defs[s]?.kind == TerminalKind.Block) blkFired++;
            foreach (int s in o.rebooted) if (s >= 0 && s < defs.Length && defs[s]?.kind == TerminalKind.Block) blkFired++;
            if (Has(f, HeatItems.Stone))
            {
                if (dice.Length >= 3 && o.attackDice >= dice.Length) o.heatGains.Add(StoneAll);
                else if (o.attackDice >= 2) o.heatGains.Add(StoneTwo);
            }
            if (Has(f, HeatItems.Breastplate))
                for (int i = 0; i < blkFired; i++) { o.heatGains.Add(BreastHeat); o.blockAdd += BreastBlock; }
            if (Has(f, HeatItems.Poker))
            {
                int n = o.blockDice + o.chargeDice + o.supportDice;
                if (n > 0) o.heatGains.Add(PokerPerDie * n);
            }
            for (int h = 0; h < o.attackHits.Count; h++)
            {
                if (Has(f, HeatItems.Spanner)) o.heatGains.Add(SpannerPerHit);
                if (Has(f, HeatItems.Whetstone)) o.heatGains.Add(WhetPerHit);
            }
            o.heatDouble = Has(f, HeatItems.Flint) && o.attackHits.Count >= FlintAttackHits;      // 撃 = 攻撃の端子の発動 + 【再起動】の撃
        }

        /// <summary>見込みの結果: 配線の時 (攻撃の前・【過熱】になる) の爆発の回数・ターンの終わり (攻撃に挿した出目の合計の半分) の爆発の回数・
        /// 爆発のダメージの合計 (敵の防御の後)・終わった後の臨界。</summary>
        public struct Outlook
        {
            public int burstsWired, burstsEnd, landed, meterAfter;
        }

        /// <summary>この配線で起きる爆発の見込み (BOT の配線の評価・画面の右の列)。 本体と同じ順 (端子の臨界 → 品の臨界を 1 つずつ → ターンの終わりに <see cref="EndGain"/>) で
        /// <see cref="Gain"/> と <see cref="TryBurst"/> を通す。 <paramref name="s"/> は写し (書き換えてよい)。
        /// shortcut: ターンの頭・敵の攻撃の後に品が足す臨界 (〈炉番の外套〉〈早沸かし釜〉) とひびの値打ちは見ない。 要るなら敵の予告から足す。</summary>
        public static Outlook Forecast(State s, TerminalOutcome o)
        {
            var r = new Outlook();
            if (s.threshold <= 0 && (o.heat > 0 || o.heatGains.Count > 0)) s.threshold = StartThreshold(s.items);
            if (s.threshold <= 0) return r;
            s.doubled |= o.heatDouble;
            r.burstsWired = Step(ref s, o.heat, ref r.landed);
            foreach (int g in o.heatGains) r.burstsWired += Step(ref s, g, ref r.landed);
            r.burstsEnd = Step(ref s, EndGain(o), ref r.landed);
            r.meterAfter = s.meter;
            return r;
        }

        /// <summary>臨界を n 得て、 届いた分だけ爆発する (本体の Kw.AddHeat と同じ 1 回)。 戻り値 = 爆発の回数・<paramref name="landed"/> に敵の防御の後のダメージを足す。</summary>
        public static int Step(ref State s, int n, ref int landed)
        {
            if (Gain(ref s, n) <= 0) return 0;
            int k = 0;
            while (TryBurst(ref s, out var b)) { k++; landed += b.landed; }
            return k;
        }
    }
}
