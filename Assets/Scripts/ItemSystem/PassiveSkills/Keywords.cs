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
        //  臨界
        // ============================================================

        /// <summary>[persistent・戦闘開始で 0] この戦闘で爆発した回数の累計 (端子〈爆炎斬り〉が読む・ADR-0014)。</summary>
        public const string BurstTotalKey = "kw.burstTotal";
        /// <summary>閾値を超えて溜まっていた臨界 (〈溢れ取りの鋳型〉が読む)。</summary>
        public const string BurstExcessKey = "kw.burstExcess";
        /// <summary>直近に爆発したターン (〈二度目の打金〉が読む)。</summary>
        public const string BurstTurnKey = "kw.burstTurn";
        /// <summary>[persistent・戦闘開始で 0] この戦闘で、 爆発のダメージそのもので敵が倒れた (1/0・〈爆破祭の小切手〉が読む)。</summary>
        public const string BurstKilledKey = "kw.burstKilled";
        /// <summary>いま決まった爆発が超臨界か (1/0・爆発の出来事を配っている間に読む)。 超臨界爆発 = 閾値を引く前の臨界が閾値の 2 倍以上 (2026-11-20・§23-32)。</summary>
        public const string BurstSuperKey = "kw.burstSuper";
        /// <summary>いま決まった爆発 1 回のダメージ (<see cref="BurstBase"/>・超臨界なら ×1.5・爆発の出来事を配っている間に読む)。</summary>
        public const string BurstDamageNowKey = "kw.burstDamageNow";

        /// <summary>臨界を有効にする (どれか 1 つの臨界の品が戦闘開始時に呼べば動き出す)。</summary>
        public static void EnableHeat(CombatContext ctx)
        {
            if (ctx.rinkaiThreshold <= 0) ctx.rinkaiThreshold = Effects.RinkaiInit.DefaultThreshold;
            if (ctx.rinkaiBurstDamage <= 0) ctx.rinkaiBurstDamage = Effects.RinkaiInit.DefaultBurstDamage;
        }

        /// <summary>爆発 1 回の基礎 (2026-11-07)。 〈炉温〉の②なら 15 + 炉温 / 2、 なければ 30。
        /// 〈二度詰めの雷管〉の上乗せを足す。 品は ctx.rinkaiBurstDamage を直接書かない ── 書く品どうしの順番で値が変わったため。</summary>
        public static int BurstBase(CombatContext ctx)
        {
            int b = ctx.burstCritRule ? 15 + ctx.AxisDepth(AxisRules.Heat) / 2 : ctx.rinkaiBurstDamage;
            return b + ctx.burstStackBonus + ctx.burstFlatBonus;   // 〈天工開物〉の上乗せ (2026-11-20)
        }

        /// <summary>臨界を n 溜める。 閾値をまたいだら爆発し、 その場でダメージを出して (ADR-0016)、 出来事を配る。
        /// 〈帳簿外の爆薬〉(rinkaiMultiBurst) があれば臨界を閾値ぶんだけ減らし、 溜まっている限り何度でも爆発する。</summary>
        /// <summary>[計装 2026-11-19] 1 回の (いちばん外側の) 足し込みで起きた爆発の回数の最大と、 その時の出どころ・量・溜まり。 BOT がランの始めに空にする。</summary>
        public static int HeatMaxBurstsOneAdd;
        public static string HeatMaxBurstsInfo = "";
        private static int _heatOuter;

        /// <summary>src = 出どころの名前 (null なら働いている品の名前・品の外なら「本体」)。 計装にだけ使う。</summary>
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
                        HeatMaxBurstsInfo = $"{who} +{n0} (溜まり {meterBefore}・閾値 {ctx.rinkaiThreshold}・帳簿外 {(ctx.rinkaiMultiBurst ? "あり" : "なし")}・ターン {ctx.currentTurn}) → 爆発 {bursts} 回";
                    }
                }
            }
        }

        private static void AddHeatCore(CombatContext ctx, int n)
        {
            n += ctx.heatGainBonus;   // 〈炉温〉の①: 臨界を得るたび +1
            float relicHeat = MetaProgression.Relics.RelicApplicator.GetHeatGainPct();   // 遺物 炉温 2 個: 臨界の獲得量 +20% (切り上げ)
            if (relicHeat > 0f) n += (int)System.Math.Ceiling(n * relicHeat);
            ctx.rinkaiMeter += n;
            // 1 回の足し込みで爆発できる回数に上限は無い (2026-11-19・作者の決定で旧い「最大 50 回」を削除)。
            //   〈帳簿外の爆薬〉でも 1 回ごとに閾値 (≥ 1) ずつ減るので必ず止まる。
            while (ctx.rinkaiThreshold > 0 && ctx.rinkaiMeter >= ctx.rinkaiThreshold)
            {
                int th = ctx.rinkaiThreshold;
                // 超臨界爆発 (2026-11-20・§23-32): 閾値を引く前の臨界が閾値の 2 倍以上。 その爆発のダメージ ×1.5 (上乗せ = 基礎の半分・切り捨て)
                bool super = ctx.rinkaiMeter >= 2 * th;
                int excess = ctx.rinkaiMeter - th;
                ctx.rinkaiMeter = ctx.rinkaiMultiBurst ? excess : 0;
                ctx.accumulatedValues[BurstTotalKey] = ctx.GetAccumulated(BurstTotalKey) + 1;
                ctx.accumulatedValues[BurstExcessKey] = ctx.rinkaiMultiBurst ? 0 : excess;
                ctx.accumulatedValues[BurstTurnKey] = ctx.currentTurn;
                int burstDmg = BurstBase(ctx);
                if (super) burstDmg += burstDmg / 2;
                ctx.accumulatedValues[BurstSuperKey] = super ? 1 : 0;
                ctx.accumulatedValues[BurstDamageNowKey] = burstDmg;
                Debug.Log($"[臨界] 閾値{th} 到達 → {(super ? "超臨界" : "")}爆発 (超過 {excess}・残 {ctx.rinkaiMeter}・ダメージ {burstDmg})");
                // ADR-0016: 閾値に届いたその場でダメージを出す (軽減無視・倍率は乗らない)。 旧: 次の攻撃の固定ダメの枠に乗せていた
                var pm = PassiveSkillManager.Instance;
                var cm = pm != null && ReferenceEquals(pm.Context, ctx) ? CombatSystem.CombatManager.Instance : null;
                if (cm != null && cm.LandBurst(ctx, burstDmg)) ctx.accumulatedValues[BurstKilledKey] = 1;
                Raise(ctx, PassiveSkillTrigger.OnRinkaiBurst, th);
                if (super) Raise(ctx, PassiveSkillTrigger.OnSuperBurst, burstDmg);
                int relicSh = MetaProgression.Relics.RelicApplicator.GetBurstShield();   // 遺物 炉温 3 個: 爆発のたびにシールド +6
                if (relicSh > 0) { ctx.consShield += relicSh; ctx.shieldGainedTotal += relicSh; CombatSystem.ShieldDiag.Note("遺物〈炉温〉3", relicSh); }
                if (!ctx.rinkaiMultiBurst) break;
            }
        }

        // ============================================================
        //  シールド
        // ============================================================

        /// <summary>吸収が敵の直接攻撃によるものか (〈鏡返しの小盾〉は攻撃のときだけ返す)。</summary>
        public const string AbsorbIsAttackKey = "kw.absorbIsAttack";
        /// <summary>吸収に応じて敵へ返すダメージ (品が積み、 CombatManager が敵の攻撃の解決で使って 0 に戻す)。</summary>
        public const string ReflectKey = "kw.reflect";

        /// <summary>シールドで dmg を吸収し、 吸収した量を返す。 吸収と割れ (0 になった) の出来事を配る。</summary>
        public static int AbsorbShield(CombatContext ctx, int dmg, bool isAttack)
        {
            if (ctx == null || dmg <= 0 || ctx.consShield <= 0) return 0;
            int before = ctx.consShield;
            int absorbed = System.Math.Min(before, dmg);
            ctx.consShield = before - absorbed;
            ctx.accumulatedValues[AbsorbIsAttackKey] = isAttack ? 1 : 0;
            Raise(ctx, PassiveSkillTrigger.OnShieldAbsorb, absorbed);
            if (ctx.consShield <= 0) Raise(ctx, PassiveSkillTrigger.OnShieldBreak, before);
            ctx.accumulatedValues[AbsorbIsAttackKey] = 0;
            return absorbed;
        }

        // ============================================================
        //  出血
        // ============================================================

        /// <summary>この戦闘の出血の最大値 (〈血車〉が読む)。</summary>
        public const string BleedMaxKey = "kw.bleedMax";

        /// <summary>品の効果で出血を n 使う。 使った分を返す。</summary>
        public static int ConsumeBleed(CombatContext ctx, int n)
        {
            if (ctx == null || n <= 0 || ctx.enemyBleedStacks <= 0) return 0;
            int used = System.Math.Min(n, ctx.enemyBleedStacks);
            ctx.enemyBleedStacks -= used;
            Raise(ctx, PassiveSkillTrigger.OnBleedConsume, used);
            if (ctx.enemyBleedStacks == 0) Raise(ctx, PassiveSkillTrigger.OnBleedZero, 1);
            return used;
        }

        // ============================================================
        //  会心
        // ============================================================

        /// <summary>この戦闘で出した会心の回数 (〈戦果の天秤〉が読む)。</summary>
        public const string CritCountKey = "kw.critCount";
        /// <summary>直近の会心した主攻撃に実際に掛かった会心倍率 (敵の会心耐性・〈寸鉄〉〈心眼〉の② 込み・PassiveSkillManager.ProcessDamage が書く)。 〈天眼の鍔〉が読む。</summary>
        public const string LastCritMulKey = "kw.lastCritMul";

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
