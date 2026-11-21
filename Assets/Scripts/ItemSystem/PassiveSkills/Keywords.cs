using UnityEngine;

namespace ItemSystem.PassiveSkills.Keywords
{
    /// <summary>
    /// キーワード (臨界・充電・シールド・毒・出血) の増減を 1 か所に集め、 品へ出来事として配る (2026-10-31 アイテム再編)。
    ///
    /// <para><b>量を変えるときはここを通す。</b> 直接フィールドを書き換えると、 その量を読む品
    /// (「爆発のたび」「充電を得るたび」など) に出来事が届かない。 充電は CombatContext.AddCharge / ConsumeCharge、
    /// シールドの増加は consShield の setter、 毒は AddStatus、 出血は AddEnemyBleed がそれぞれ出来事を出す。</para>
    ///
    /// <para>出来事は <see cref="PassiveSkillManager.RaiseKeyword"/> が配り、 量は <see cref="EventAmount"/> で読む。
    /// 1 つの出来事から続く連鎖の中では、 同じ品は 1 度しか働かない。</para>
    /// </summary>
    public static class Kw
    {
        /// <summary>出来事の量 (配っている間だけ有効)。</summary>
        public const string AmountKey = "kw.amount";

        /// <summary>いま配っている出来事の量。</summary>
        public static int EventAmount(CombatContext ctx) => (int)ctx.GetAccumulated(AmountKey);

        /// <summary>出来事を配る。 戦闘中で、 ctx が今の戦闘のものであるときだけ。</summary>
        public static void Raise(CombatContext ctx, PassiveSkillTrigger trigger, int amount)
        {
            if (ctx == null || amount <= 0) return;
            var m = PassiveSkillManager.Instance;
            if (m == null || !ReferenceEquals(m.Context, ctx)) return;
            m.RaiseKeyword(trigger, amount);
        }

        // ============================================================
        //  臨界 (式の正本は CombatSystem.HeatCore・2026-11-21・full-item-rework-ledger §⑤)
        // ============================================================

        /// <summary>[persistent・戦闘開始で 0] この戦闘で爆発した回数の累計 (端子〈爆炎斬り〉・〈火花読みの眼鏡〉〈烈刃〉〈希望の灯片〉が読む)。</summary>
        public const string BurstTotalKey = "kw.burstTotal";
        /// <summary>いま決まった爆発で閾値を超えて溢れていた臨界 (〈溢れ取りの鋳型〉が読む・〈帳簿外の爆薬〉なら 0)。</summary>
        public const string BurstExcessKey = "kw.burstExcess";
        /// <summary>直近に爆発したターン (【過熱】= このターンに爆発した・<see cref="Overheated"/>)。</summary>
        public const string BurstTurnKey = "kw.burstTurn";
        /// <summary>[persistent・戦闘開始で 0] この戦闘で、 爆発のダメージそのもので敵が倒れた (1/0・〈爆破祭の小切手〉が読む)。</summary>
        public const string BurstKilledKey = "kw.burstKilled";
        /// <summary>いま決まった爆発が超臨界か (1/0・爆発の出来事を配っている間に読む)。 超臨界爆発 = 閾値の 1.5 倍以上の臨界で起きた爆発 (HeatCore.SuperAtPct)。</summary>
        public const string BurstSuperKey = "kw.burstSuper";
        /// <summary>いま決まった爆発 1 回の敵の防御の後のダメージ (HeatCore.Burst.landed・爆発の出来事を配っている間に読む)。</summary>
        public const string BurstDamageNowKey = "kw.burstDamageNow";

        /// <summary>【過熱】: このターンに爆発が起きた。</summary>
        public static bool Overheated(CombatContext ctx) => ctx != null && ctx.currentTurn > 0 && (int)ctx.GetAccumulated(BurstTurnKey) == ctx.currentTurn;

        /// <summary>臨界を有効にする (臨界の品を持つ戦闘は戦闘開始時に本体が呼ぶ・端子が臨界を出した時も)。 閾値 30 (〈早沸かし釜〉−12)・〈恒温の竈〉は閾値の半分から。</summary>
        public static void EnableHeat(CombatContext ctx)
        {
            if (ctx == null || ctx.rinkaiThreshold > 0) return;
            var f = HeatItemsNow();
            ctx.rinkaiThreshold = CombatSystem.HeatCore.StartThreshold(f);
            if (CombatSystem.HeatCore.Has(f, CombatSystem.HeatItems.Oven)) ctx.rinkaiMeter = System.Math.Max(ctx.rinkaiMeter, ctx.rinkaiThreshold / 2);
        }

        /// <summary>今の戦闘の臨界の品の旗 (端子の山札の状態・戦闘の外は None)。</summary>
        public static CombatSystem.HeatItems HeatItemsNow() => CombatSystem.CombatManager.Instance?.TerminalDeck?.state.heatItems ?? CombatSystem.HeatItems.None;

        /// <summary>ctx から爆発の盤面を作る (本体・BOT・画面の見込みの入口)。</summary>
        public static CombatSystem.HeatCore.State HeatState(CombatContext ctx)
        {
            return new CombatSystem.HeatCore.State
            {
                items = HeatItemsNow(), meter = ctx.rinkaiMeter, threshold = ctx.rinkaiThreshold,
                kilnCut = (int)ctx.GetAccumulated(CombatSystem.HeatCore.KilnCutKey),
                coalUsed = Effects.Psn.TurnN(ctx, CombatSystem.HeatCore.CoalKey), burstsTurn = Effects.Psn.TurnN(ctx, CombatSystem.HeatCore.TurnBurstsKey),
                burstsTotal = (int)ctx.GetAccumulated(BurstTotalKey), stack = ctx.burstStackBonus, flat = ctx.burstFlatBonus, enemyDefense = ctx.enemyDefense,
                doubled = ctx.currentTurn > 0 && (int)ctx.GetAccumulated(CombatSystem.HeatCore.DoubleTurnKey) == ctx.currentTurn,
                enemyDown = CombatSystem.BossDown.ActiveNow,
                relicPct = MetaProgression.Relics.RelicApplicator.GetHeatGainPct(),   // 遺物 炉温 2 個: 臨界の獲得量 +20% (切り上げ)
            };
        }

        private static void StoreHeat(CombatContext ctx, in CombatSystem.HeatCore.State s)
        {
            ctx.rinkaiMeter = s.meter; ctx.rinkaiThreshold = s.threshold; ctx.burstStackBonus = s.stack;
            ctx.accumulatedValues[CombatSystem.HeatCore.KilnCutKey] = s.kilnCut;
            ctx.accumulatedValues[BurstTotalKey] = s.burstsTotal;
            Effects.Psn.AddTurnN(ctx, CombatSystem.HeatCore.CoalKey, s.coalUsed - Effects.Psn.TurnN(ctx, CombatSystem.HeatCore.CoalKey));
            Effects.Psn.AddTurnN(ctx, CombatSystem.HeatCore.TurnBurstsKey, s.burstsTurn - Effects.Psn.TurnN(ctx, CombatSystem.HeatCore.TurnBurstsKey));
        }

        /// <summary>[計装 2026-11-19] 1 回の (いちばん外側の) 足し込みで起きた爆発の回数の最大と、 その時の出どころ・量・溜まり。 BOT がランの始めに空にする。</summary>
        public static int HeatMaxBurstsOneAdd;
        public static string HeatMaxBurstsInfo = "";
        private static int _heatOuter;

        /// <summary>臨界を n 溜める (<see cref="CombatSystem.HeatCore.Gain"/>)。 閾値に届いたら爆発し、 その場でダメージを出し (ADR-0016・CombatManager.LandBurst)、
        /// ボスの次の目盛りにひびを入れ (〈柱折りの発破筒〉は即座に正規のダウン)、 出来事を配る。 src = 出どころの名前 (計装にだけ使う)。</summary>
        public static void AddHeat(CombatContext ctx, int n, string src = null)
        {
            if (ctx == null || n <= 0 || ctx.rinkaiThreshold <= 0) return;
            bool outer = _heatOuter == 0;
            int n0 = n, meterBefore = ctx.rinkaiMeter, burstsBefore = (int)ctx.GetAccumulated(BurstTotalKey);
            _heatOuter++;
            try { AddHeatCore(ctx, n); }
            finally
            {
                _heatOuter--;
                if (outer)
                {
                    int bursts = (int)ctx.GetAccumulated(BurstTotalKey) - burstsBefore;
                    if (bursts > HeatMaxBurstsOneAdd)
                    {
                        HeatMaxBurstsOneAdd = bursts;
                        string who = src ?? (string.IsNullOrEmpty(CombatContext.CurrentSkillId) ? "本体" : CombatContext.CurrentSkillId);
                        HeatMaxBurstsInfo = $"{who} +{n0} (溜まり {meterBefore}・閾値 {ctx.rinkaiThreshold}・ターン {ctx.currentTurn}) → 爆発 {bursts} 回";
                    }
                }
            }
        }

        private static void AddHeatCore(CombatContext ctx, int n)
        {
            var s = HeatState(ctx);
            CombatSystem.HeatCore.Gain(ref s, n);
            StoreHeat(ctx, in s);
            while (CombatSystem.HeatCore.TryBurst(ref s, out var b))
            {
                StoreHeat(ctx, in s);
                ctx.accumulatedValues[BurstExcessKey] = b.excess;
                ctx.accumulatedValues[BurstTurnKey] = ctx.currentTurn;
                ctx.accumulatedValues[BurstSuperKey] = b.super ? 1 : 0;
                ctx.accumulatedValues[BurstDamageNowKey] = b.landed;
                CombatSystem.CombatManager.BurstFired++;   // [計装]
                Debug.Log($"[臨界] 閾値{b.threshold} 到達 → {(b.super ? "超臨界" : "")}爆発 (溜まり {b.meterAt}・超過 {b.excess}・残 {s.meter}・ダメージ {b.damage} → 防御の後 {b.landed}・この戦闘 {s.burstsTotal} 回目)");
                var pm = PassiveSkillManager.Instance;
                var cm = pm != null && ReferenceEquals(pm.Context, ctx) ? CombatSystem.CombatManager.Instance : null;
                if (cm != null)
                {
                    if (cm.LandBurst(ctx, b.landed)) ctx.accumulatedValues[BurstKilledKey] = 1;
                    // 爆発はボスの次の目盛りにひびを入れる (標準)。 〈柱折りの発破筒〉はそのひびを即座に正規のダウンへ割る
                    cm.CrackBossNotch("臨界の爆発");
                    if (CombatSystem.HeatCore.Has(s.items, CombatSystem.HeatItems.Pillar) && cm.BreakBossCrack("柱折りの発破筒")) CombatSystem.CombatManager.BurstBreakFired++;
                }
                Raise(ctx, PassiveSkillTrigger.OnRinkaiBurst, b.threshold);
                if (b.super) Raise(ctx, PassiveSkillTrigger.OnSuperBurst, b.landed);
                int relicSh = MetaProgression.Relics.RelicApplicator.GetBurstShield();   // 遺物 炉温 3 個: 爆発のたびにシールド +6
                if (relicSh > 0) { ctx.consShield += relicSh; ctx.shieldGainedTotal += relicSh; CombatSystem.ShieldDiag.Note("遺物〈炉温〉3", relicSh); }
                s = HeatState(ctx);   // 出来事の品が臨界を動かしても、 続きは今の値から
            }
        }

        // ============================================================
        //  シールド
        // ============================================================

        /// <summary>シールドで dmg を吸収し、 吸収した量を返す。 吸収と割れ (0 になった) の出来事を配る。</summary>
        public static int AbsorbShield(CombatContext ctx, int dmg, bool isAttack)
        {
            if (ctx == null || dmg <= 0 || ctx.consShield <= 0) return 0;
            int before = ctx.consShield;
            int absorbed = System.Math.Min(before, dmg);
            ctx.consShield = before - absorbed;
            Raise(ctx, PassiveSkillTrigger.OnShieldAbsorb, absorbed);
            if (ctx.consShield <= 0) Raise(ctx, PassiveSkillTrigger.OnShieldBreak, before);
            return absorbed;
        }

        /// <summary>品・端子の効果でシールドを n 使う (〈破城斧〉・端子〈重装突撃〉)。 使った量を返し、 出来事 OnShieldSpend を配る。 割れの出来事は配らない (割られたのではない)。</summary>
        public static int SpendShield(CombatContext ctx, int n)
        {
            if (ctx == null || n <= 0 || ctx.consShield <= 0) return 0;
            int used = System.Math.Min(n, ctx.consShield);
            ctx.consShield -= used;
            Raise(ctx, PassiveSkillTrigger.OnShieldSpend, used);
            return used;
        }

        // ============================================================
        //  出血
        // ============================================================

        /// <summary>この戦闘の出血の最大値 (実績〈大出血〉が読む)。</summary>
        public const string BleedMaxKey = "kw.bleedMax";

        /// <summary>品の効果で出血を n 使う。 使った分を返す。</summary>
        public static int ConsumeBleed(CombatContext ctx, int n)
        {
            if (ctx == null || n <= 0 || ctx.enemyBleedStacks <= 0) return 0;
            int used = System.Math.Min(n, ctx.enemyBleedStacks);
            ctx.enemyBleedStacks -= used;
            CombatSystem.CombatManager.BleedSpendFired++;   // [計装]
            Raise(ctx, PassiveSkillTrigger.OnBleedConsume, used);
            if (ctx.enemyBleedStacks == 0) Raise(ctx, PassiveSkillTrigger.OnBleedZero, 1);
            return used;
        }

        // ============================================================
        //  会心
        // ============================================================

        /// <summary>この戦闘で出した会心の回数 (〈戦果の天秤〉が読む)。</summary>
        public const string CritCountKey = "kw.critCount";
        /// <summary>直近の会心した主攻撃に実際に掛かった会心倍率 (敵の会心耐性・超過会心率・〈天眼の鍔〉の ×1.25 込み・PassiveSkillManager.ProcessDamage が書く)。 〈天眼の鍔〉が読む。</summary>
        public const string LastCritMulKey = "kw.lastCritMul";

        // ============================================================
        //  ボスのダウン
        // ============================================================

        /// <summary>いま配っている OnBossDown がひびの猶予で割れた分か (1 = ひび / 0 = 通常攻撃・出来事を配っている間に読む・2026-11-21)。</summary>
        public const string BossDownCrackKey = "kw.bossDownCrack";

        // ============================================================
        //  敵への軽減不可ダメージ
        // ============================================================

        /// <summary>CombatManager がターンを解決している最中か (その間、 敵の HP は CombatManager の手元にある)。</summary>
        public const string ResolvingKey = "kw.resolving";
        /// <summary>解決中に品が入れた敵へのダメージ (CombatManager が攻撃の前とターンの最後に手元の HP へ反映して 0 に戻す)。</summary>
        public const string PendingEnemyDamageKey = "kw.pendingEnemyDmg";

        /// <summary>敵へ軽減不可ダメージを入れる。 ターンの解決中は CombatManager に預け、 それ以外は直接 HP を減らす。
        /// 出来事はターンのどこでも起きうるので、 HP を直接書くと CombatManager の手元の値に上書きされて消える。</summary>
        public static void DamageEnemy(CombatContext ctx, int dmg)
        {
            if (ctx == null || dmg <= 0) return;
            if (ctx.GetAccumulated(ResolvingKey) > 0) Add(ctx, PendingEnemyDamageKey, dmg);
            else ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - CombatSystem.BossDown.ApplyTakenNow(MetaProgression.MetaDebuffApplicator.ApplyOutgoingCut(ApplyFinalDamage(ctx, dmg))));   // 預けた分は HpLossDiag.Lose で掛かる (最終与ダメージ倍率・ボスのダウンの ×1.5 も)
        }

        /// <summary>最終与ダメージ倍率 (2026-11-20・GAME.md §23-32)。 すべての倍率の後・敵の軽減パッシブと防御の前に掛ける (切り捨て)。
        /// 掛ける口は大罪:怠惰 (<see cref="MetaProgression.MetaDebuffApplicator.ApplyOutgoingCut"/>) と同じ所: 主攻撃 = CombatManager.Damage.cs・
        /// 主攻撃以外 = HpLossDiag.Lose・<see cref="DamageEnemy"/> の直接の口・〈逆綴じの止血帯〉。 BOT の見積もり (AutoRunner.Combat.cs) も同じ位置。</summary>
        public static int ApplyFinalDamage(CombatContext ctx, int dmg, int extraPct = 0)
        {
            int pct = ctx != null ? ctx.finalDamagePct + extraPct : 0;   // extraPct = 主攻撃のこの 1 撃だけの上乗せ (ctx.hitFinalDamagePct・〈加重の拳套〉)
            return dmg > 0 && pct != 0 ? (int)((long)dmg * (100 + pct) / 100) : dmg;
        }

        /// <summary>今の戦闘の ctx で <see cref="ApplyFinalDamage"/> (戦闘の外では素通し)。</summary>
        public static int ApplyFinalDamageNow(int dmg) => ApplyFinalDamage(PassiveSkillManager.Instance?.Context, dmg);

        /// <summary>預かっている敵へのダメージを取り出して 0 に戻す。</summary>
        public static int TakePendingEnemyDamage(CombatContext ctx)
        {
            int d = (int)ctx.GetAccumulated(PendingEnemyDamageKey);
            if (d != 0) ctx.accumulatedValues[PendingEnemyDamageKey] = 0;
            return d;
        }

        // ============================================================
        //  戦闘を通して積む値 (品ごとのキー)
        // ============================================================

        /// <summary>戦闘を通して積む値を足す。</summary>
        public static void Add(CombatContext ctx, string key, float n)
            => ctx.accumulatedValues[key] = ctx.GetAccumulated(key) + n;

        /// <summary>積み上げた量を step ごとに数え、 新しく満ちた回数を返す (端数は持ち越す)。</summary>
        public static int Tally(CombatContext ctx, string key, int add, int step)
        {
            if (add <= 0 || step <= 0) return 0;
            int before = (int)ctx.GetAccumulated(key);
            int after = before + add;
            ctx.accumulatedValues[key] = after;
            return after / step - before / step;
        }
    }
}
