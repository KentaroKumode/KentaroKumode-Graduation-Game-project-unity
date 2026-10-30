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

        /// <summary>このターン、 爆発で次の攻撃に乗る回数 (アクセス用キー・[perTurn] ではなく使ったら 0)。</summary>
        public const string BurstCountKey = "kw.burstCount";
        /// <summary>閾値を超えて溜まっていた臨界 (〈溢れ取りの鋳型〉が読む)。</summary>
        public const string BurstExcessKey = "kw.burstExcess";
        /// <summary>爆発に上乗せするダメージ (品が積む。 爆発を使ったら 0)。</summary>
        public const string BurstBonusKey = "kw.burstBonus";
        /// <summary>この攻撃が爆発を使った (〈荒砥〉〈爆破コンテストの賞金〉が読む)。 毎ターン攻撃の前に書き直す。</summary>
        public const string BurstThisAttackKey = "kw.burstThisAttack";
        /// <summary>直近に爆発を使ったターンと、 そのダメージ (〈二度目の打金〉が読む)。</summary>
        public const string BurstAppliedTurnKey = "kw.burstAppliedTurn";
        public const string BurstAppliedDmgKey = "kw.burstAppliedDmg";

        /// <summary>臨界を有効にする (どれか 1 つの臨界の品が戦闘開始時に呼べば動き出す)。</summary>
        public static void EnableHeat(CombatContext ctx)
        {
            if (ctx.rinkaiThreshold <= 0) ctx.rinkaiThreshold = Effects.RinkaiInit.DefaultThreshold;
            if (ctx.rinkaiBurstDamage <= 0) ctx.rinkaiBurstDamage = Effects.RinkaiInit.DefaultBurstDamage;
        }

        /// <summary>臨界を n 溜める。 閾値をまたいだら爆発を決め、 出来事を配る。
        /// 爆発は次の攻撃に乗る (CombatManager が <see cref="BurstCountKey"/> を見て使う)。
        /// 〈帳簿外の連爆〉(rinkaiMultiBurst) があれば臨界を閾値ぶんだけ減らし、 溜まっている限り何度でも爆発する。</summary>
        public static void AddHeat(CombatContext ctx, int n)
        {
            if (ctx == null || n <= 0 || ctx.rinkaiThreshold <= 0) return;
            ctx.rinkaiMeter += n;
            int guard = 0;
            while (ctx.rinkaiMeter >= ctx.rinkaiThreshold && guard++ < 50)
            {
                int th = ctx.rinkaiThreshold;
                int excess = ctx.rinkaiMeter - th;
                ctx.rinkaiMeter = ctx.rinkaiMultiBurst ? excess : 0;
                ctx.rinkaiBurstActive = true;
                ctx.accumulatedValues[BurstCountKey] = ctx.GetAccumulated(BurstCountKey) + 1;
                ctx.accumulatedValues[BurstExcessKey] = ctx.rinkaiMultiBurst ? 0 : excess;
                Debug.Log($"[臨界] 閾値{th} 到達 → 爆発 (超過 {excess}・残 {ctx.rinkaiMeter})");
                Raise(ctx, PassiveSkillTrigger.OnRinkaiBurst, th);
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
            else ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
        }

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
