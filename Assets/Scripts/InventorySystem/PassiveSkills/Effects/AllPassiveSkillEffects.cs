namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  汎用パッシブ — 追撃（Pursuit）: 与ダメージ追加
    // ============================================================

    // 追撃 I-IV: 旧=与ダメ+固定 (筋力と役割被り) / 新=与ダメの N% を追加 (乗算・筋力=加算と分離)
    // 数値: I=+15% / II=+30% / III=+50% / IV=+100% (LEG は完全/万華等の outgoing+1.0 と同格)
    // 実装: 新モードでは outgoingDamageMultiplier に加算 = 与ダメチェーン末端で乗算される
    internal static class PursuitHelper
    {
        public static void Apply(CombatContext ctx, int flatOld, float pctNew)
        {
            if (CombatSystem.CombatManager.UseMutualAttackPipeline)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += pctNew;
            }
            else
            {
                ctx.finalDamage += flatOld;
            }
        }
    }

    /// <summary>追撃I — 新: 与ダメ+15% (旧: +2 固定)</summary>
    public class PursuitI : IPassiveSkillEffect
    {
        public string SkillId => "PursuitI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => PursuitHelper.Apply(ctx, 2, 0.15f);
    }

    /// <summary>追撃II — 新: 与ダメ+30% (旧: +4 固定)</summary>
    public class PursuitII : IPassiveSkillEffect
    {
        public string SkillId => "PursuitII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => PursuitHelper.Apply(ctx, 4, 0.30f);
    }

    /// <summary>追撃III — 新: 与ダメ+50% (旧: +6 固定)</summary>
    public class PursuitIII : IPassiveSkillEffect
    {
        public string SkillId => "PursuitIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => PursuitHelper.Apply(ctx, 6, 0.50f);
    }

    /// <summary>追撃IV — 新: 与ダメ+40% (2026-09-20: +100% から) (旧: +8 固定)</summary>
    public class PursuitIV : IPassiveSkillEffect
    {
        public string SkillId => "烈刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        // 2026-09-20: +100% → +40%。 無条件で常時乗るため実効も +100% で、 条件付きの札と釣り合わなかった。
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => PursuitHelper.Apply(ctx, 8, 0.40f);
    }

    // ============================================================
    //  汎用パッシブ — 反撃（Counter）
    // ============================================================

    // 反撃 I-IV: 旧=ロール敗北時発火 / 新=被ダメ発生時発火 (収支トリガーではなく被弾で発火)。
    // 新法 OnPreReceiveDamage は ProcessDamage 内で発火し、fixedDamageToEnemy に積むと
    // counterFixed として ExecuteTurnMutual が敵HPへ反射する。ctx.finalDamage > 0 の被ダメ発生時のみ発火
    // (完全防御・シールド吸収で被ダメ0の時は反撃なし)。
    internal static class CounterHelper
    {
        public static void Apply(PassiveSkillTrigger t, CombatContext ctx, int dmg)
        {
            if (CombatSystem.CombatManager.UseMutualAttackPipeline)
            {
                if (t == PassiveSkillTrigger.OnPreReceiveDamage && ctx.finalDamage > 0)
                    ctx.fixedDamageToEnemy += dmg;
            }
            else
            {
                if (t == PassiveSkillTrigger.OnRollLose)
                    ctx.fixedDamageToEnemy += dmg;
            }
        }
    }

    /// <summary>反撃I — 新: 被ダメ時に敵へ軽減不可2ダメ (旧: ロール敗北時)</summary>
    public class CounterI : IPassiveSkillEffect
    {
        public string SkillId => "CounterI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CounterHelper.Apply(t, ctx, 2);
    }

    /// <summary>反撃II — 新: 被ダメ時に敵へ軽減不可4ダメ (旧: ロール敗北時)</summary>
    public class CounterII : IPassiveSkillEffect
    {
        public string SkillId => "CounterII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CounterHelper.Apply(t, ctx, 4);
    }

    /// <summary>反撃III — 新: 被ダメ時に敵へ軽減不可6ダメ (旧: ロール敗北時)</summary>
    public class CounterIII : IPassiveSkillEffect
    {
        public string SkillId => "CounterIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CounterHelper.Apply(t, ctx, 6);
    }

    /// <summary>反撃IV — 新: 被ダメ時に敵へ軽減不可8ダメ (旧: ロール敗北時)</summary>
    public class CounterIV : IPassiveSkillEffect
    {
        public string SkillId => "CounterIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollLose, PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => CounterHelper.Apply(t, ctx, 8);
    }

    // ============================================================
    //  汎用パッシブ — 筋力（Might）: 各ダイス出目+N
    // ============================================================

    // 筋力I-IV: 旧=ダイス合計+N (ロール勝負を有利化) / 新=攻撃力+N (配線後の atkBase に加算)。
    // AddPlayerAttackOrDiceBonus が UseMutualAttackPipeline で自動分岐する。

    /// <summary>筋力II — 攻撃+3 (旧: ダイス合計+3)</summary>
    public class MightII : IPassiveSkillEffect
    {
        public string SkillId => "MightII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null) ctx.AddPlayerAttackOrDiceBonus(3);
        }
    }

    /// <summary>筋力III — 攻撃+4 (旧: ダイス合計+4)</summary>
    public class MightIII : IPassiveSkillEffect
    {
        public string SkillId => "MightIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null) ctx.AddPlayerAttackOrDiceBonus(4);
        }
    }

    /// <summary>筋力IV — 攻撃+6 (旧: ダイス合計+6)</summary>
    public class MightIV : IPassiveSkillEffect
    {
        public string SkillId => "MightIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice != null) ctx.AddPlayerAttackOrDiceBonus(6);
        }
    }

    // ============================================================
    //  汎用パッシブ — 頑強（Fortitude）
    // ============================================================


    /// <summary>頑強II — 被ダメージ-2</summary>
    public class FortitudeII : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 2);
        }
    }

    /// <summary>頑強III — 被ダメージ-3</summary>
    public class FortitudeIII : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 3);
        }
    }

    /// <summary>頑強IV — 被ダメージ-4</summary>
    public class FortitudeIV : IPassiveSkillEffect
    {
        public string SkillId => "FortitudeIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage > 0) ctx.finalDamage = System.Math.Max(0, ctx.finalDamage - 4);
        }
    }

    // ============================================================
    //  素のステータス品 (2026-09-15)
    //
    //  **ラダーではない。** Might/Insight のような I〜IV の段階品ではなく、
    //  1 品 1 効果の独立した品として置く。 汎用ラダーはアイテムとしては
    //  1 ラダー 1 品まで解体済み (GAME.md §9-2b) なので、 段を足すのは再生産になる。
    //
    //  **なぜ要ったか。** BRONZE 21 品のうち発動条件なしで常時乗るのは 3 品、
    //  SILVER 18 品では 1 品しかなかった。 残りは臨界メーター・充電・毒・出血の
    //  ギミック品で、 <b>そのビルドを既に組んでいないと数値がゼロ</b>。
    //  1〜3 層は BRONZE/SILVER が主体なので、 まだ何者でもない状態の棚に
    //  そのランで機能する品がほとんど並ばなかった。
    //
    //  **値は実測のレバレッジから決めている** (10,000 ラン・107 万攻撃):
    //      atkBase 36.10 → 最終与ダメ 150.12  ＝ <b>×4.16</b> (最大観測 ×51.67)
    //      敵の攻撃 89.9 回/ラン  平均ブロック 10.37  平均 lossBase 15.67
    //  この倍率のせいで「小さな素のステータス」が小さくならない ──
    //      攻撃 +1      → 最終与ダメ +2.8%
    //      被ダメ −1    → 被弾すべてに乗って −6.4%   (メタ防御 2 段ぶん)
    //      与ダメ +1%   → +0.6%                     ← 穏やか
    //  **攻撃加算と固定被ダメ軽減は「基礎的な小品」には置けない。**
    //  与ダメージ% と少量のシールドだけがこの帯で扱える量だった。
    // ============================================================





    // ============================================================
    //  汎用パッシブ — 心眼（Insight）
    // ============================================================



    /// <summary>心眼III — 会心ダイス+3</summary>
    public class InsightIII : IPassiveSkillEffect
    {
        public string SkillId => "InsightIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.critRatePctAdd += 0.15f; }
    }

    /// <summary>心眼IV (2026-07-15 リワーク: 会心ダイス+5 → +4 + 会心ダメージ+20%) — 会心確率と会心倍率の両立LEG</summary>
    public class InsightIV : IPassiveSkillEffect
    {
        public string SkillId => "InsightIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.critRatePctAdd += 0.20f;     // 会心率 +20%
            ctx.criticalMultiplier += 0.2f;  // 会心倍率+0.2 (=会心ダメ+20%)
        }
    }

    // ============================================================
    //  汎用パッシブ — 活力（Vitality）
    // ============================================================


    /// <summary>活力II — ターン終了時HP+2回復</summary>
    public class VitalityII : IPassiveSkillEffect
    {
        public string SkillId => "VitalityII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 2);
        }
    }

    /// <summary>活力III — ターン終了時HP+3回復</summary>
    public class VitalityIII : IPassiveSkillEffect
    {
        public string SkillId => "VitalityIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 3);
        }
    }

    /// <summary>活力IV (2026-05-30 LEG1.3倍化: +4 → +5) — ターン終了時HP+5回復</summary>
    public class VitalityIV : IPassiveSkillEffect
    {
        public string SkillId => "VitalityIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + 5);
        }
    }

    // ============================================================
    //  ユニークパッシブ — 盾系
    // ============================================================

    /// <summary>パリィ (2026-07-15 リワーク) — 戦闘中、被ダメージを1回無効化。
    /// 発動後3ターン経過で再発動可能。旧: scratch 無効化 (新モデルで scratch 廃止に伴い転生)。</summary>
    public class Parry : IPassiveSkillEffect
    {
        public string SkillId => "パリィ";
        private const string CooldownKey = "parry_ready_turn"; // 次に使えるターン数 (currentTurn がこれ以上なら発動)
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int readyTurn = (int)ctx.GetAccumulated(CooldownKey); // 0 = 未発動 → 発動可能
            if (ctx.currentTurn < readyTurn) return;
            ctx.finalDamage = 0;
            ctx.accumulatedValues[CooldownKey] = ctx.currentTurn + 3; // 3T 経過後に再発動可能
            UnityEngine.Debug.Log($"[パリィ] 被ダメ無効化 (次回発動可能: T{ctx.currentTurn + 3})");
        }
    }

    /// <summary>聖なる守り (2026-07-15 リワーク) — HP≥50% で被ダメ-20% (守り) / HP<50% で与ダメ+20% (奉戦)。
    /// 「余裕がある間は守り、追い詰められたら攻めに転じる」HP参照の二相型。</summary>
    /// <summary>衛士の慣い (旧「聖なる守り」・2026-09-05 リワーク) — 戦闘開始時 シールド +10。
    /// 以降、 ターン終了時にシールドが 3 未満なら +4 補充する。
    ///
    /// <para><b>旧実装は HP 半分を境に 被ダメ−20% / 与ダメ+20% を切り替えるものだった。</b>
    /// 「窮すれば強い」という別の性格の効果で、 盾家系の終端としては筋が通っていない。</para>
    ///
    /// <para>シールドに寄せたのは、 <b>シールドが軽減無視ダメージを肩代わりする</b>ため
    /// (2026-08-15 決定・<see cref="CombatContext.ShieldAbsorbsUnmitigable"/>)。
    /// 盾家系だけが自前でシールドを供給し続けられる ＝ DOT や固定ダメの通し方が他家系と変わる。
    /// シールドバッシュ系アイテムの弾にもなるので、 「盾を残すこと自体が価値」という
    /// 既存の設計 ([[project_shield_absorbs_unmitigable]]) と方向が揃う。</para></summary>
    public class HolyShield : IPassiveSkillEffect
    {
        private const int OpeningShield = 10;
        private const int RefillBelow = 3;
        private const int RefillAmount = 4;

        public string SkillId => "衛士の慣い";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnTurnEnd,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                ctx.consShield += OpeningShield; CombatSystem.ShieldDiag.Note("衛士の慣い(開幕)", OpeningShield);
                ctx.shieldGainedTotal += OpeningShield;
                return;
            }
            if (ctx.consShield < RefillBelow)
            {
                ctx.consShield += RefillAmount; CombatSystem.ShieldDiag.Note("衛士の慣い(補充)", RefillAmount);
                ctx.shieldGainedTotal += RefillAmount;
            }
        }
    }

    // ============================================================
    //  ユニークパッシブ — 剣系
    // ============================================================

    /// <summary>虚空 (2026-07-29 リワーク) — 攻撃端子に接続したダイスが 0 本のターン、
    /// **殻が 1 枚剥がれる**。 積むほど被ダメ軽減が薄れ、 敵への刻みが増える。
    ///
    ///   軽減 = max(0, 90 − 15n)%      n=0:90% … n=6:0%
    ///   刻み = **現在 HP** の (5 + 2n)%  n=0:5% … n=8:21%（上限）
    ///
    /// 旧実装は `nullifyAllDamage` で双方のダメージを 0 にしており、
    /// **攻めなければ無敵**という無条件・無制限のロックだった (実測: 7層 p4 戦の 88.8% のターンで発動し、
    /// 敵攻撃 506 が 8.5 まで潰れていた)。 敵の火力調整が原理的に無効化されるため撤去。
    /// 刻みを現在 HP 基準にしたので指数的に減衰し、 **これ単独では敵を倒し切れない** ──
    /// 守り続けても最後は殴る必要がある。
    /// 旧々: ダイス差≤3で双方ダメ0+固定3。「拮抗の間合い」の意味論を配線判断で再定義。
    /// 旧: ダイス差≤3で双方ダメ0+固定3。「拮抗の間合い」の意味論を配線判断で再定義。
    /// 新: 配線集計後の OnPreDealDamage で発火し、attackDiceCount==0 なら発動。
    /// ExecuteTurnMutual が nullifyAllDamage フラグを敵攻撃側にも波及させる。</summary>
    public class VoidStance : IPassiveSkillEffect
    {
        /// <summary>殻の初期軽減率(%)からスタックごとに引く量。 90 → 0 まで 6 段。</summary>
        public const int ReducePerStack = 15;
        /// <summary>刻み(現在HP%)の初期値とスタックごとの増分。</summary>
        public const int ChipBase = 5;
        public const int ChipPerStack = 2;
        /// <summary>スタック上限。 n=8 で 軽減0% / 刻み21%。</summary>
        public const int StackCap = 8;

        public string SkillId => "VoidStance";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPreDealDamage,   // 新: 配線集計後・自攻撃前
            PassiveSkillTrigger.OnPostRoll,        // 旧: ロール直後
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (CombatSystem.CombatManager.UseMutualAttackPipeline)
            {
                if (trigger != PassiveSkillTrigger.OnPreDealDamage) return;
                int atkDice = (int)ctx.GetAccumulated("mutualAttackDiceCount");
                if (atkDice != 0) return;

                int n = System.Math.Min(StackCap, ctx.voidStanceStacks);
                // 殻: 積むほど薄くなる。 **0 にはできない**ので完全無敵は成立しない。
                ctx.voidStanceDamageMul = System.Math.Max(0f, 90 - ReducePerStack * n) / 100f;
                // 刻み: 積むほど増えるが **現在 HP 基準**なので漸近するだけ。 単独では倒し切れない。
                int chip = ctx.CurrentHpRatioDamageToEnemy((ChipBase + ChipPerStack * n) / 100f);
                ctx.fixedDamageToEnemy += chip;
                if (ctx.voidStanceStacks < StackCap) ctx.voidStanceStacks++;
                UnityEngine.Debug.Log($"[虚空] 攻撃端子0 → 殻{n}枚目: 被ダメ×{ctx.voidStanceDamageMul:F2} "
                                    + $"/ 現在HP{ChipBase + ChipPerStack * n}% = {chip} 軽減不能 "
                                    + $"(次スタック {ctx.voidStanceStacks})");
                return;
            }
            // 旧: OnPostRoll 中は diceDifference 未確定 → 合計から自前で差を取る
            if (trigger != PassiveSkillTrigger.OnPostRoll) return;
            if (System.Math.Abs(ctx.playerDiceTotal - ctx.enemyDiceTotal) <= 3)
            {
                ctx.nullifyAllDamage = true;
                ctx.fixedDamageToEnemy += 3;
            }
        }
    }

    // ============================================================
    //  ユニークパッシブ — 斧系
    // ============================================================

    /// <summary>復讐 (2026-07-15 リワーク) — 被ダメージ時、攻撃力+1 蓄積 (上限10・戦闘中持続・勝利リセットなし)。
    /// 旧: 敗北でダイス+1蓄積 (勝利リセット)、OnPostRoll でダイス合計/攻撃に加算。
    /// 蓄積キー "frenzyDiceBonus" は流用 (BeginNewTurn では accumulatedValues をリセットしないため戦闘内持続)。</summary>
    /// <summary>復讐 (2026-09-05 リワーク) — 被弾するたびに 攻撃 +2 を蓄積 (上限 10 スタック = +20・戦闘中持続)。
    ///
    /// <para><b>旧実装は <c>finalDamage += スタック数</c> だった。</b> 上限 10 スタックでも最大 +10 の
    /// 定額加算で、 与ダメが 3 桁に乗る帯では誤差でしかない。 <b>攻撃値側へ移し、 1 スタック +2 にした</b> ──
    /// 攻撃値は会心と与ダメ倍率の手前にあるので、 斧の他のラダー (猛り・大鉈) と乗算で噛み合う。</para>
    ///
    /// <para>加算先が <c>mutualAttackBonus</c> になったので、 適用は <b>OnPostRoll</b> でなければならない
    /// (ADR-0009 §9.2: atkBase は配線直後に確定する)。 旧実装の OnPreDealDamage のままだと
    /// **一切乗らない**ので、 トリガーを移していることに注意。</para></summary>
    public class Frenzy : IPassiveSkillEffect
    {
        public string SkillId => "復讐";
        private const int MaxStack = 10;
        private const int AttackPerStack = 2;
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPreReceiveDamage, // 蓄積
            PassiveSkillTrigger.OnPostRoll,         // 適用 (攻撃値はここでしか乗らない)
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnPreReceiveDamage:
                    // 被ダメが発生する時にスタック+1 (上限10・完全防御時は不発)
                    if (ctx.finalDamage <= 0) return;
                    int cur = (int)ctx.GetAccumulated("frenzyDiceBonus");
                    if (cur < MaxStack)
                        ctx.accumulatedValues["frenzyDiceBonus"] = cur + 1;
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    int stacks = (int)ctx.GetAccumulated("frenzyDiceBonus");
                    if (stacks > 0) ctx.AddPlayerAttackOrDiceBonus(stacks * AttackPerStack);
                    break;
            }
        }
    }

    // ============================================================
    //  ユニークパッシブ — 短剣系
    // ============================================================

    /// <summary>処刑 (2026-09-05 増補) — 勝利時、次ターン敵最大ダイス1固定 (最強ダイスを潰す)。
    /// 加えて <b>敵の残HP が 25% 以下なら 与ダメ +80%</b>。
    ///
    /// <para>ダイス潰しは短剣家系の「デバフ」側、 残HP しきい値は「一撃必殺」側。
    /// 賞金首狩り (アイテム・8〜28% で即死) とは<b>枠が違う</b> ── あちらは処刑、
    /// こちらは倍率なので、 重ねたときに即死判定が二重になることはない。</para></summary>
    /// <para><b>2026-09-19: 「残HP 25% 以下で与ダメ +80%」は items.json の stats へ移した</b>
    /// (<c>enemyHpPctMax: 25</c>)。 ここに残るのはダイス潰しだけで、 ステータス部分と一緒に
    /// <see cref="StatModifierEffect"/> が包んで登録する。 25% は賞金首狩り IV (28%) より内側。</para>
    public class Execute : IPassiveSkillEffect
    {
        public string SkillId => "処刑";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin };
        // クラス名と同名メソッドを避けるため明示的インターフェース実装
        void IPassiveSkillEffect.Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.pendingDiceOverrides.Add(
                new DiceOverrideRequest(DiceOverrideRequest.TargetDice.Highest, 1, SkillId));
        }
    }

    /// <summary>蝕夜 — オーバーダメ×4を蓄積→戦闘開始時に確定ダメで放出。
    /// 蓄積はラン中ずっと持続し、 戦闘ごとに膨張する (雪だるま式)。
    /// ラン跨ぎでは IRunResettable でリセット (StartNewRun で発火)。</summary>
    public class Nightfall : IPassiveSkillEffect, IRunResettable
    {
        public string SkillId => "蝕夜";
        private int persistentOverdamage = 0;
        public void ResetRunState() { persistentOverdamage = 0; }
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPostDealDamage,
            PassiveSkillTrigger.OnBattleStart
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                if (persistentOverdamage > 0)
                    ctx.fixedDamageToEnemy += persistentOverdamage;
                return;
            }
            if (ctx.overDamageAccumulated > 0)
            {
                // 過剰ダメを 400% (×4) で持ち越し (dagger_t4 強化)
                persistentOverdamage += ctx.overDamageAccumulated * 4;
                ctx.overDamageAccumulated = 0;
            }
        }
    }

    /// <summary>出血 — ダメージを与えるたび、敵に出血+1（ターン1回制限を撤廃）</summary>
    public class Sting : IPassiveSkillEffect
    {
        public string SkillId => "Sting";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.AddEnemyBleed(1);
        }
    }

    // (Ignite / CurseBind / Abyss は 2026-07-18 削除: dead_staff / curse_t1〜t4 全廃に伴う死コード掃除)

    // ============================================================
    //  ダイス固有パッシブ
    // ============================================================








    // ============================================================
    //  汎用パッシブ — 吸血（Lifesteal）: ロール勝利時、最終与ダメの2/4/6/8%回復
    //  ctx.lifestealPct に加算し、CombatManager 勝利分岐が totalDmg×pct を HealPlayer で回復
    //  （負傷/回復封印/天衣無縫を尊重）。毎ターン0リセットのため OnTurnStart で再適用。
    // ============================================================
    public class LifestealI : IPassiveSkillEffect
    {
        public string SkillId => "LifestealI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.02f; }
    }
    public class LifestealIII : IPassiveSkillEffect
    {
        public string SkillId => "LifestealIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.06f; }
    }
    public class LifestealIV : IPassiveSkillEffect
    {
        // 2026-05-30 LEG 1.3倍化: 8% → 10%
        public string SkillId => "LifestealIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.lifestealPct += 0.10f; }
    }

    // ============================================================
    //  汎用パッシブ — 不屈（Indomitable）リワーク 2026-07-15:
    //  自HPが閾値以下の時、被ダメ-30%。Tier で発動閾値が広がる (低Tierほど窮地限定)。
    //  Lv1: HP≤20% / Lv2: HP≤30% / Lv3: HP≤40% / Lv4: HP≤60%
    // ============================================================
    internal static class IndomitableHelper
    {
        public static void Apply(CombatContext ctx, int thresholdPct)
        {
            if (ctx.finalDamage <= 0 || ctx.playerMaxHP <= 0) return;
            if (ctx.playerCurrentHP * 100 > ctx.playerMaxHP * thresholdPct) return;
            ctx.finalDamage = UnityEngine.Mathf.CeilToInt(ctx.finalDamage * 0.7f);
        }
    }
    /// <summary>不屈I — HP≤20% で被ダメ-30%</summary>
    public class IndomitableI : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => IndomitableHelper.Apply(ctx, 20);
    }
    /// <summary>不屈II — HP≤30% で被ダメ-30%</summary>
    public class IndomitableII : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => IndomitableHelper.Apply(ctx, 30);
    }
    /// <summary>不屈IV — HP≤60% で被ダメ-30%</summary>
    public class IndomitableIV : IPassiveSkillEffect
    {
        public string SkillId => "IndomitableIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => IndomitableHelper.Apply(ctx, 60);
    }

    // ============================================================
    //  汎用パッシブ — シールドバッシュ（ShieldBash）リワーク 2026-07-15:
    //  攻撃時、残存シールドの 20/40/60/80% を攻撃力ボーナスとして加算 (Ceil)。
    //  Lv4 のみ攻撃後にシールド+5 追加 (盾を殴りつけて減らしても、殴り返して補充する意)。
    //  従来の「勝利時、与ダメの N% をシールド化」(shieldOnWinPct) は廃止。
    // ============================================================
    internal static class ShieldBashHelper
    {
        public static void OnDeal(CombatContext ctx, float pct)
        {
            // [計装 2026-08-15] **残存シールドが火力そのもの**という構造の実測。
            //   軽減無視をシールドで肩代わりさせたところ 5層ボス戦が 14.7 → 15.6 ターンに伸びた。
            //   チップが毎ターン盾を削る = ここのボーナスが消える、 が疑い。
            //   発動しなかったケース (盾 0) も数えないと「効かなくなった」ことが見えない。
            GameLoop.RunChronicle.NoteShieldBash(ctx, ctx.consShield <= 0
                ? 0 : UnityEngine.Mathf.CeilToInt(ctx.consShield * pct));
            if (ctx.finalDamage <= 0 || ctx.consShield <= 0) return;
            int bonus = UnityEngine.Mathf.CeilToInt(ctx.consShield * pct);
            if (bonus > 0) ctx.finalDamage += bonus;
        }
    }
    /// <summary>シールドバッシュI — 攻撃時、残存シールドの20%を攻撃力に加算</summary>
    public class ShieldBashI : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ShieldBashHelper.OnDeal(ctx, 0.20f);
    }
    /// <summary>シールドバッシュII — 攻撃時、残存シールドの40%を攻撃力に加算</summary>
    public class ShieldBashII : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ShieldBashHelper.OnDeal(ctx, 0.40f);
    }
    /// <summary>シールドバッシュIII — 攻撃時、残存シールドの60%を攻撃力に加算</summary>
    public class ShieldBashIII : IPassiveSkillEffect
    {
        public string SkillId => "ShieldBashIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => ShieldBashHelper.OnDeal(ctx, 0.60f);
    }
    /// <summary>シールドバッシュIV — 攻撃時、残存シールドの80%を攻撃力に加算 + 攻撃後シールド+5</summary>
    public class ShieldBashIV : IPassiveSkillEffect
    {
        public string SkillId => "内から落ちた城盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            ShieldBashHelper.OnDeal(ctx, 0.80f); // 残存シールドの80% を攻撃力に (加算前に算出)
            CombatSystem.ShieldDiag.Note("シールドバッシュIV", 5); ctx.consShield += 5;                  // 攻撃後 (加算後) にシールド補充
            ctx.shieldGainedTotal += 5;
        }
    }

    /// <summary>盾解放 (ShieldRelease) — 2026-07-16 追加。
    /// 攻撃時、残存シールドを全て消費し、消費量と同値を攻撃力に加算 (100% 変換)。
    /// ShieldBash が「シールド維持しつつ継続火力」なのに対し、こちらは「シールド全消費で撃破局面の爆発」。
    /// Shield ビルドに二つ目のペイオフ経路を提供 (シールドを防御 or 攻撃のリソースとして選択できる二相性)。
    /// 敵HP <= 予定与ダメ+残存シールド の時に Bot が発動判断する運用を想定 (現状は無条件消費・実装単純化)。</summary>
    public class ShieldRelease : IPassiveSkillEffect
    {
        public string SkillId => "捨盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.consShield <= 0) return;
            // 撃破可能な局面のみ発動 (盾を防御のまま残す方が良いケースが多いため)
            int projected = ctx.finalDamage + ctx.consShield;
            if (projected < ctx.enemyCurrentHP) return; // 撃破できない → 温存
            int consumed = ctx.consShield;
            ctx.finalDamage += consumed;
            ctx.consShield = 0;
            UnityEngine.Debug.Log($"[盾解放] シールド{consumed}全消費 → 攻撃力+{consumed} (最終ダメ{ctx.finalDamage})");
        }
    }

    // ============================================================
    //  汎用パッシブ — 貸与された時間（LentTime）リワーク版:
    //   敗北時、 被ダメの 15/30/45/60% を「貸与時間」として肩代わり (軽減) し蓄積。
    //   蓄積が最大HP×同割合に達すると、 一括清算ではなく Tier別 3/4/5/6ターンに分割して
    //   毎ターン終了時 軽減不可ダメを支払う。 分割中にロール勝利1回で残債務帳消し (返済免除)。
    //   上限到達中にさらに被弾しても新規借入は受け付けない (返済中ロック)。
    // ============================================================
    internal static class LentTimeHelper
    {
        // Tier別 分割ターン数
        public static int PaybackTurns(int tier)
        {
            switch (tier) { case 1: return 3; case 2: return 4; case 3: return 5; case 4: return 6; default: return 4; }
        }
        // OnRollWin: 分割返済中なら帳消し / 蓄積中なら帳消し
        public static void OnWin(CombatContext ctx)
        {
            if (ctx.lentTimePaybackRemainTurns > 0 || ctx.lentTimeStacks > 0)
                UnityEngine.Debug.Log($"[貸与された時間] 勝利→ 帳消し ({ctx.lentTimeStacks + ctx.lentTimePaybackTotal} ダメ免除)");
            ctx.lentTimeStacks = 0;
            ctx.lentTimePaybackRemainTurns = 0;
            ctx.lentTimePaybackTotal = 0;
        }
        // OnPreReceiveDamage: 被ダメの pct を肩代わり蓄積、 上限到達で分割返済モードへ移行
        // ユーザー指定: 返済支払いがあったターンは新規借入を行わない
        public static void OnReceive(CombatContext ctx, float pct, int tier)
        {
            if (ctx.lentTimePaybackRemainTurns > 0) return; // 返済中は借入ロック
            if (ctx.lentTimePaidThisTurn) return;            // 同ターンに支払い済みなら借入もしない
            int dmg = ctx.finalDamage;
            if (dmg <= 0) return;
            int portion = UnityEngine.Mathf.CeilToInt(dmg * pct);
            ctx.finalDamage = System.Math.Max(0, dmg - portion);
            ctx.lentTimeStacks += portion;
            ctx.lentTimeTier = tier;
            int cap = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * pct);
            if (ctx.lentTimeStacks >= cap)
            {
                ctx.lentTimePaybackTotal = ctx.lentTimeStacks;
                ctx.lentTimePaybackRemainTurns = PaybackTurns(tier);
                ctx.lentTimeStacks = 0;
                UnityEngine.Debug.Log($"[貸与された時間] 上限到達 → {ctx.lentTimePaybackRemainTurns}T 分割返済開始 (総債務{ctx.lentTimePaybackTotal})");
            }
        }
        // OnTurnEnd: 分割返済の1回分を支払い (HP下限1ガード + 蓄積停止フラグセット)
        public static void OnTurnEnd(CombatContext ctx)
        {
            if (ctx.lentTimePaybackRemainTurns <= 0) return;
            int payment = UnityEngine.Mathf.CeilToInt((float)ctx.lentTimePaybackTotal / ctx.lentTimePaybackRemainTurns);
            // HP下限保護: 現HP > 1 でないと支払いしない、 また支払い量を HP-1 まで丸める
            int hpFloor = System.Math.Max(0, ctx.playerCurrentHP - 1);
            int actualPay = System.Math.Min(payment, hpFloor);
            if (actualPay > 0)
            {
                ctx.fixedDamageToPlayer += actualPay;
                ctx.lentTimePaidThisTurn = true; // このターン新規借入禁止
            }
            ctx.lentTimePaybackTotal = System.Math.Max(0, ctx.lentTimePaybackTotal - payment); // 元本は予定通り減らす
            ctx.lentTimePaybackRemainTurns--;
            UnityEngine.Debug.Log($"[貸与された時間] T終了支払 -{actualPay} (予定{payment}, HP1下限保護, 残債{ctx.lentTimePaybackTotal} / 残{ctx.lentTimePaybackRemainTurns}T)");
        }
    }
    public class LentTimeI : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.15f, 1);
        }
    }
    public class LentTimeII : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.30f, 2);
        }
    }
    public class LentTimeIII : IPassiveSkillEffect
    {
        public string SkillId => "LentTimeIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.45f, 3);
        }
    }
    public class LentTimeIV : IPassiveSkillEffect
    {
        public string SkillId => "貸与された時間";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage, PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnRollWin) LentTimeHelper.OnWin(ctx);
            else if (t == PassiveSkillTrigger.OnTurnEnd) LentTimeHelper.OnTurnEnd(ctx);
            else LentTimeHelper.OnReceive(ctx, 0.60f, 4);
        }
    }




    // ============================
    //  ダイス固有パッシブ（新規）
    // ============================


    // ============================
    //  武器Tier段階補正（ダイス合計フラット加算。少ダイス低Tierを底上げし進行を線形化）
    // ============================


    /// <summary>天工開物 — 武器強化のたび強化素材を1つ返還（実効果は GameManager.TryUpgradeWeapon の所持判定）。
    /// 戦闘パッシブとしては何もしない no-op（表示名解決＋未登録警告の抑止用に登録する）。</summary>
    public class TenkouKaibutsu : IPassiveSkillEffect
    {
        public string SkillId => "天工開物";
        public PassiveSkillTrigger[] Triggers => System.Array.Empty<PassiveSkillTrigger>();
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { }
    }


    /// <summary>黄金卿の剣 (2026-05-31 v3 消費Gold基準) — 消費した累積ゴールド×0.002 を outgoing に加算。
    /// 500Gで+1.0 (×2)、 1000Gで+2.0 (×3)。 「使えば使うほど強くなる」軸への切替。
    /// 旧仕様 (保有Gold×0.04) は「貯めるほど強い → 出費抑制 = 戦略歪み」だったため変更。
    ///
    /// **2026-08-10 経済リスケールで係数を 0.01 → 0.002 へ。** 支出額が約 5 倍になったので、
    /// 係数を据え置くと効果が 5 倍になる。 **同じ「支出額に対する強さ」を保つための追随**であって、
    /// このアイテムの強弱を変える意図ではない。</summary>
    public class GoldKingBlade : IPassiveSkillEffect
    {
        public string SkillId => "溶かし金の領主剣";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int spent = GameLoop.GameManager.Instance?.Run?.coinsSpent ?? 0;
            if (spent <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.002f * spent;
        }
    }

    // ============================================================
    //  2026-06-03 新規追加アイテム
    // ============================================================



    // ============================================================
    //  2026-06-05 会心バリエーション（会心を「ただ×2」から質の違う一撃へ）
    //  発火: OnCriticalDamage（会心成立後・×criticalMultiplier 適用前）／一部 OnCriticalCheck（会心判定前）
    // ============================================================

    /// <summary>裂傷の刃心 (BRONZE) — 会心時、敵に出血+2。会心倍率の+100%ごとに+1（×2.0で+1, ×3.0で+2）。血路の旗と相乗。</summary>
    public class LacerationCore : IPassiveSkillEffect
    {
        public string SkillId => "医家の反り刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            int bonus = UnityEngine.Mathf.FloorToInt(UnityEngine.Mathf.Max(0f, ctx.criticalMultiplier - 1f));
            ctx.AddEnemyBleed(2 + bonus);
        }
    }

    /// <summary>防殻の一閃 (BRONZE) — 会心時、その会心ダメージの5%をシールド化（攻めの会心がわずかな守りになる）。</summary>
    public class GuardFlash : IPassiveSkillEffect
    {
        public string SkillId => "鏡返しの小盾";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // OnCriticalDamage は ×criticalMultiplier 適用前に発火するため、会心後ダメを自前で算出。
            int critDmg = UnityEngine.Mathf.CeilToInt((ctx.finalDamage + ctx.pursuitDamage) * ctx.criticalMultiplier);
            int shield = UnityEngine.Mathf.CeilToInt(critDmg * 0.05f);
            if (shield > 0) { ctx.consShield += shield; ctx.shieldGainedTotal += shield; CombatSystem.ShieldDiag.Note("鏡返しの小盾", shield); }
        }
    }

    /// <summary>急所穿ち (SILVER) — 会心時、軽減無視ダメージ+5を追加で与える（硬い敵に刺さる防御貫通の追い打ち）。</summary>
    public class VitalPierce : IPassiveSkillEffect
    {
        public string SkillId => "鎧縫いの針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.fixedDamageToEnemy += 5;
        }
    }



    /// <summary>連環の極み (LEGENDARY) — 会心するたび会心倍率+0.2（戦闘中累積）。会心スノーボール。</summary>
    public class ChainApex : IPassiveSkillEffect
    {
        public string SkillId => "連環の指輪";
        private const string StackKey = "chainApexStacks"; // 戦闘中持続（accumulatedValues は戦闘開始でのみリセット）
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnCriticalCheck)
                ctx.criticalMultiplier += 0.2f * ctx.GetAccumulated(StackKey); // 毎ターン再取得される倍率へ累積分を再適用
            else
                ctx.AddAccumulated(StackKey, 1f); // 会心成立ごとに+1スタック
        }
    }

    // 武器Tier段階補正 (旧: ダイス合計 / 新: 攻撃力・自動分岐)

    // ============================================================
    //  汎用パッシブ — 利刃（BladeEdge）リワーク 2026-07-15:
    //  攻撃時、敵シールドを 5/10/15/20 削る。Lv4 のみ会心ダメージ+20%も追加。
    //  「敵シールド」= 現状 SaintGeorges の sg_shield のみ (対シュヴァリエ特効)。
    //  将来的にシールド持ちボスが増えたら同キー統合 or enemy_shield 汎用化を検討。
    //  従来の armorPen/winMinDamage は廃止 (基礎防御% の意味論は保持されるが利刃系統は移行)。
    // ============================================================
    internal static class BladeEdgeHelper
    {
        public const string EnemyShieldKey = "sg_shield"; // 現状は SaintGeorges 唯一
        public static void Strip(CombatContext ctx, int amount)
        {
            float cur = ctx.GetAccumulated(EnemyShieldKey);
            if (cur <= 0) return;
            float after = System.Math.Max(0f, cur - amount);
            ctx.accumulatedValues[EnemyShieldKey] = after;
            UnityEngine.Debug.Log($"[利刃] 敵シールド -{amount} ({cur:F0}→{after:F0})");
        }
    }
    /// <summary>利刃I — 攻撃時、敵シールドを-5</summary>
    public class BladeEdgeI : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BladeEdgeHelper.Strip(ctx, 5);
    }
    /// <summary>利刃II — 攻撃時、敵シールドを-10</summary>
    public class BladeEdgeII : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BladeEdgeHelper.Strip(ctx, 10);
    }
    /// <summary>利刃III — 攻撃時、敵シールドを-15</summary>
    public class BladeEdgeIII : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeIII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) => BladeEdgeHelper.Strip(ctx, 15);
    }
    /// <summary>利刃IV — 攻撃時、敵シールドを-20 + 会心ダメージ+20%</summary>
    public class BladeEdgeIV : IPassiveSkillEffect
    {
        public string SkillId => "BladeEdgeIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage, PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPreDealDamage) BladeEdgeHelper.Strip(ctx, 20);
            else ctx.criticalMultiplier += 0.2f; // 会心ダメージ+20%
        }
    }

    // ============================================================
    //  処刑・対タンク・シナジー触媒（2026-05-29 追加）
    // ============================================================

    /// <summary>賞金首狩り (リワーク 2026-05-30) — ターン終了時、敵HPが閾値%以下なら即処刑。
    /// 通常敵: 閾値 10/20/30/40% / ボス: 閾値 5/10/15/20% (耐性)。
    /// 報酬: 処刑時 最大HP×(lv×3%) 回復 + GOLD 1/2/3/4 (全Tier獲得)。</summary>
    internal static class BountyHunterHelper
    {
        public static bool IsBoss(CombatContext ctx)
        {
            var enemy = CombatSystem.CombatManager.Instance?.CurrentEnemy;
            return enemy != null && GameLoop.BossIds.IsBoss(enemy.id);
        }
        public static void Try(CombatContext ctx, int normalPct, int bossPct, int lv)
        {
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            int thresholdPct = IsBoss(ctx) ? bossPct : normalPct;
            if (ctx.enemyCurrentHP * 100 > ctx.enemyMaxHP * thresholdPct) return;
            if (!ctx.CanExecuteEnemy()) return;
            ctx.enemyCurrentHP = 0;

            int heal = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.03f * lv);
            if (heal > 0)
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);

            int gold = lv; // Lv1-4 で +1/2/3/4 GOLD (全Tier獲得)
            if (gold > 0)
            {
                var run = GameLoop.GameManager.Instance?.Run;
                if (run != null) GameLoop.GoldIncome.Gain(run, gold, "賞金首狩り", applyLastStandFilter: false);
            }
            UnityEngine.Debug.Log($"[賞金首狩り] 敵HP{thresholdPct}%以下 → 処刑 (HP+{heal}, +{gold}G, ボス={IsBoss(ctx)})");
        }
    }

    public class BountyHunterI : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 10, 5, 1); }
    }
    public class BountyHunterII : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 20, 10, 2); }
    }
    public class BountyHunterIII : IPassiveSkillEffect
    {
        public string SkillId => "百一人切りの鉈";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 30, 15, 3); }
    }
    public class BountyHunterIV : IPassiveSkillEffect
    {
        public string SkillId => "BountyHunterIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { BountyHunterHelper.Try(ctx, 40, 20, 4); }
    }

    /// <summary>治癒阻害（Silver）— 敵が得る回復量を50%減少。</summary>
    public class GrievousI : IPassiveSkillEffect
    {
        public string SkillId => "GrievousI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.enemyHealReductionPct = 0.5f; }
    }
    /// <summary>治癒遮断（Gold）— 敵が得る回復量を完全に無効化。</summary>
    public class GrievousII : IPassiveSkillEffect
    {
        public string SkillId => "GrievousII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx) { ctx.enemyHealReductionPct = 1.0f; }
    }


    /// <summary>天極 — 出目が全て同値（ゾロ目）なら会心を確定させ、会心倍率+1.0。
    ///
    /// <para><b>2026-09-05: 唯一の担い手だった〈六面天頂儀〉を削除したので、 現在この効果は発火経路が無い。</b>
    /// 理由は発火率 ── 素の 6 面 × 5 個で <c>6/6^5 = 1/1296</c>、 1 ラン 260 ターン前後を回して
    /// 期待 0.2 回 ＝ <b>5 ラン に 1 回</b>。 準パワー -0.74 は「弱い」のではなく「読まれていない」。
    /// 旧・血令 (ゾロ目で ×2.5) / 旧・運命 (全ダイス最大で ×2) と同じ壊れ方で、 同日に 3 件まとめて処理した。</para>
    ///
    /// <para><b>全ダイス同値/全ダイス最大を発火条件にしないこと。</b> このダイス数では成立しない。
    /// class は残置 (BuildPersona 等の id 参照が生きているため)。</para></summary>
    public class ApexCrit : IPassiveSkillEffect
    {
        public string SkillId => "ApexCrit";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length < 2) return;
            int first = ctx.playerDice[0];
            for (int i = 1; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] != first) return; // ゾロ目でなければ無効
            ctx.forceCritical = true;       // 会心確定
            ctx.criticalMultiplier += 1.0f; // 会心倍率+1
        }
    }

    /// <summary>重畳 (2026-05-31 v5 %ベース) — ロール勝利毎に与ダメ倍率を加算。
    /// Lv1-4: 勝利毎 +2/4/5/10%、 上限 +20/40/60/80% (outgoing 加算)。
    /// stack 値は % 値 (整数) で保存し、 OnDeal で outgoing += stack/100 を反映。</summary>
    internal static class ConquerorHelper
    {
        public const string Key = "conqueror_stack";

        public static void OnWin(CombatContext ctx, int perWinPct, int capPct)
        {
            int s = (int)ctx.GetAccumulated(Key);
            if (s < capPct) ctx.accumulatedValues[Key] = System.Math.Min(capPct, s + perWinPct);
        }
        public static void OnDeal(CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int pct = (int)ctx.GetAccumulated(Key);
            if (pct <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += pct / 100f;
        }
    }
    public class ConquerorI : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorI";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 2, 20); else ConquerorHelper.OnDeal(ctx); }
    }
    public class ConquerorII : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorII";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 4, 40); else ConquerorHelper.OnDeal(ctx); }
    }
    public class ConquerorIII : IPassiveSkillEffect
    {
        public string SkillId => "征服者の戦旗";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 5, 60); else ConquerorHelper.OnDeal(ctx); }
    }
    public class ConquerorIV : IPassiveSkillEffect
    {
        public string SkillId => "ConquerorIV";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnRollWin, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        { if (t == PassiveSkillTrigger.OnRollWin) ConquerorHelper.OnWin(ctx, 10, 100); else ConquerorHelper.OnDeal(ctx); }
    }

    /// <summary>命脈 (再強化 2026-05-30 v2): 戦闘開始時 max HP×10% 回復 + HP50% 割れ瞬間に
    /// シールド (max HP×50%) を獲得 (1戦闘1回)。 LEG責任ある量に底上げ。</summary>
    public class Lifeline : IPassiveSkillEffect
    {
        public string SkillId => "二拍目の心臓";
        private const string UsedKey = "lifeline_used";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnPostReceiveDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerMaxHP <= 0) return;
            if (trigger == PassiveSkillTrigger.OnBattleStart)
            {
                int heal = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.10f);
                int before = ctx.playerCurrentHP;
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + heal);
                int gained = ctx.playerCurrentHP - before;
                if (gained > 0) UnityEngine.Debug.Log($"[命脈] 戦闘開始時 +{gained} HP");
                return;
            }
            if (ctx.GetAccumulated(UsedKey) > 0) return;
            if (ctx.playerCurrentHP <= 0) return;
            if (ctx.playerCurrentHP * 100 > ctx.playerMaxHP * 50) return;
            ctx.accumulatedValues[UsedKey] = 1;
            int shield = UnityEngine.Mathf.CeilToInt(ctx.playerMaxHP * 0.50f);
            ctx.consShield += shield;
            CombatSystem.ShieldDiag.Note("二拍目の心臓", shield);
            ctx.shieldGainedTotal += shield;
            UnityEngine.Debug.Log($"[命脈] HP50%割れ → シールド+{shield}");
        }
    }


    /// <summary>共鳴 — 発動中パッシブ 1 個につき与ダメ +1% (outgoing に加算)。
    ///
    /// <para><b>2026-09-20: 「5 個を超えた分 × 5%」から「全数 × 1%」へ。</b> 実測で発動中パッシブが
    /// 平均 44 個あり、 実効 +196% (ボス戦 +239%) と全出どころで突出していた。 閾値を外して係数を 1/5 に。</para></summary>
    public class Resonance : IPassiveSkillEffect
    {
        public const float PctPerSkill = 0.01f;
        public string SkillId => "百鳴りの共振箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int n = PassiveSkillManager.Instance?.ActivePlayerSkillCount ?? 0;
            if (n <= 0) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += PctPerSkill * n;
        }
    }


    // ============================================================
    //  竜閃（ユニーク武器）— 安定性の対極の斬鉄剣
    // ============================================================

    /// <summary>無我無心 — カスタムダイス以外の補正を一切受けない（戦闘中持続）。</summary>
    public class MugaMushin : IPassiveSkillEffect
    {
        public string SkillId => "無我無心";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            ctx.rollPurity = true;
        }
    }

    /// <summary>画竜点睛 — 旧: 出目最大なら「ロール即勝利＋(出目+10)＋会心確定」。
    /// 新: ロール勝負なし → 出目最大なら「会心確定＋攻撃+(出目+10)」 (即勝利部分は削除、火力バーストは維持)。
    /// garyoProc/garyoDieValue は旧法の ApplyWinDamageModifiers が上書きで発火するため、
    /// 新法では OnPreDealDamage で mutualAttackBonus+(出目+10) + 会心確定にする。</summary>
    public class GaryoTensei : IPassiveSkillEffect
    {
        public string SkillId => "画竜点睛";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (ctx.playerDice == null || ctx.playerDice.Length == 0) return;

            // その時点で取りうる最大値: カスタムダイス装備中はその最大面、無ければ playerDiceMax
            int maxVal;
            if (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
            {
                maxVal = ctx.equippedDiceFaces[0];
                for (int i = 1; i < ctx.equippedDiceFaces.Length; i++)
                    if (ctx.equippedDiceFaces[i] > maxVal) maxVal = ctx.equippedDiceFaces[i];
            }
            else
            {
                maxVal = ctx.playerDiceMax > 0 ? ctx.playerDiceMax : 6;
            }

            // ダイス数1前提だが、いずれかが最大値なら発動
            int hit = 0;
            for (int i = 0; i < ctx.playerDice.Length; i++)
                if (ctx.playerDice[i] >= maxVal) { hit = ctx.playerDice[i]; break; }

            if (hit > 0)
            {
                if (CombatSystem.CombatManager.UseMutualAttackPipeline)
                {
                    // 新: 攻撃+(出目+10) + 会心確定 (即勝利は消失。バースト火力のみ保持)
                    //   **ヘルパーを通す (2026-09-14 修正)。** 直接 mutualAttackBonus を叩いていたため
                    //   SkillId 別の計装 (CombatContext.AttackBonusBySkill) から漏れており、
                    //   atkBase の「パッシブ加算」36.9 の内訳を読み違えた。
                    //   加算先は同じなので挙動は変わらない。
                    ctx.AddPlayerAttackOrDiceBonus(hit + 10);
                    ctx.forceCritical = true;
                    UnityEngine.Debug.Log($"[画竜点睛] 出目{hit}=最大 → 攻撃+{hit + 10} + 会心確定");
                }
                else
                {
                    ctx.garyoProc = true;
                    ctx.garyoDieValue = hit;
                    UnityEngine.Debug.Log($"[画竜点睛] 発動 出目{hit}=最大 → 即勝利＋({hit}+10)会心");
                }
            }
        }
    }

    // ============================================================
    //  [剣の舞] セット（2026-06-04）— Passive カテゴリのシナジー武器群。
    //  4枚インベントリ集約で〈ブレイドダンス〉(BladeDance) に変化（GameLoop.SwordDanceSet）。
    //  ダイス合計加算は OnPostRoll（RecomputeDiceTotals 後＝確実に効く）で行う。
    //  run 参照は GameLoop.GameManager.Instance.Run（StepHeal/Eternal と同じ手法）。
    // ============================================================

    /// <summary>サーベル・ワルツ (BRONZE): ダイス合計+1。
    /// 他の[剣の舞]がインベントリにも昇華にも存在しないとき、戦闘開始時にHPを半減（孤剣のリスク）。
    /// ショップでの[剣の舞]出現率上昇は ShopManager 側のフックで処理。</summary>
    public class SaberWaltz : IPassiveSkillEffect
    {
        public string SkillId => "剣舞譜「円舞」";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostRoll,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // [撤去 2026-09-14] 孤剣ペナルティ (戦闘開始HP -10%)。
            //   4 枚とも LEGENDARY へ揃えた結果、 1 枚目が「10G のデメリット付き」になり
            //   BOT の購入率が 16.6% ── 棚に 0.61 枚/ラン 出ているのに取得は 0.10 枚/ラン で、
            //   <b>揃える前に見送られて集約が 0%</b> になっていた。
            //   「揃えば強い、 単体でも損しない」へ寄せる。 集めに行く動機はここで作る。
            //   (2026-09-05 に HP半減 → -10% へ緩和した経緯があり、 今回で全廃。)
            if (trigger == PassiveSkillTrigger.OnBattleStart) return;
            // OnPostRoll: 攻撃+1 (旧: ダイス合計+1)
            ctx.AddPlayerAttackOrDiceBonus(1);
        }
    }

    /// <summary>エスパーダ・パソドブレ (SILVER): 自分と敵のダイス合計に+5。
    /// 与えるダメージ+20%（outgoing）／受けるダメージ+20%（OnPreReceiveで×1.2）。
    /// 自他+5でダイス差は不変だが、合計値を読む効果（血令/断罪の天秤/万華 等）と高め合う両刃。</summary>
    public class EspadaPasodoble : IPassiveSkillEffect
    {
        public string SkillId => "エスパーダ・パソドブレ";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPreReceiveDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger == PassiveSkillTrigger.OnPostRoll)
            {
                // 旧: 自他ダイス合計+5 (差分不変で「合計」参照系と相乗) / 新: 自攻撃+5 のみ
                // (敵ダイス合計+5 は新モデルではロール勝負がないため機能喪失 = 廃止)
                ctx.AddPlayerAttackOrDiceBonus(5);
                if (!CombatSystem.CombatManager.UseMutualAttackPipeline)
                    ctx.enemyDiceTotal += 5;
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += 0.2f; // 与ダメ+20%（毎ターンリセット→再適用）
                return;
            }
            // OnPreReceiveDamage: 被ダメ+20%（毎被弾イベントで都度乗る＝累積しない）
            if (ctx.finalDamage > 0)
                ctx.finalDamage = UnityEngine.Mathf.CeilToInt(ctx.finalDamage * 1.2f);
        }
    }

    /// <summary>フルーレ・バレエ (BRONZE): ダイス合計+3。
    /// 「戦闘に敗北したとき、このアイテムを廃棄し最大HPを1にして生還」は救済チェーン
    /// (GameLoop.LastStand.TryConsumeRevival) 側で処理する。ここでは火力部分のみ。</summary>
    public class FleuretBallet : IPassiveSkillEffect
    {
        public string SkillId => "フルーレ・バレエ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            // 旧: ダイス合計+3 / 新: 攻撃+3
            ctx.AddPlayerAttackOrDiceBonus(3);
        }
    }

    /// <summary>ファコン・タンゴ (LEGENDARY): 2026-06-20 効果変更。
    /// 攻撃+2。 <b>2026-09-14: 孤剣ペナルティ (戦闘開始時 最大HP-1) を撤去。</b></summary>
    public class FalconTango : IPassiveSkillEffect
    {
        public string SkillId => "ファコン・タンゴ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };

        /// <summary>攻撃加算量。 <b>[撤去 2026-09-14] 孤剣ペナルティ (戦闘開始時 最大HP-1)。</b>
        ///
        /// <para>この札は<b>効果がペナルティだけ</b>だったので、 単に外すと効果ゼロの札になる。
        /// 10G の LEGENDARY が無効果では買われず、 ペナルティを外した意味が消えるため、
        /// セット共通の書式 (ワルツ +1 / バレエ +3 / パソドブレ +5) に合わせて最小限の上振れを持たせる。
        /// <b>この値だけが今回の追加分</b>なので、 不要ならここを 0 にすれば元の「無効果」に戻る。</para></summary>
        public const int AttackBonus = 2;

        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            if (trigger != PassiveSkillTrigger.OnPostRoll) return;
            if (AttackBonus > 0) ctx.AddPlayerAttackOrDiceBonus(AttackBonus);
        }
    }

    // ============================================================
    //  鈍器系 (Bludgeon・2026-07-15 新規・4-2-2-1 テンプレで 9 種)
    //  差別化: 会心を犠牲/放棄することで非会心火力を伸ばすトレードオフ型。
    //  ctx.nonCritOutgoingMultiplier に加算し、ProcessDamage 内で isCritical=false 時のみ適用。
    //  会心特化ビルドの下位互換になっていた通常火力ビルドを独立軸化する。
    // ============================================================




    /// <summary>無心の刃 (MindlessBlade) — 会心を発生させない (critSuppressed)、非会心ダメ+30% (2026-09-20: +80% から)。
    /// 会心確率を完全に潰す代わりに、非会心火力を大幅増。純粋鈍器ビルドの核。</summary>
    public class MindlessBlade : IPassiveSkillEffect
    {
        public string SkillId => "読めずの無心刃";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalCheck, PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnCriticalCheck) ctx.critSuppressed = true; // 会心封印 (2026-07-26: -99 から移行)
            else if (ctx.finalDamage > 0) ctx.nonCritOutgoingMultiplier += 0.30f;   // 2026-09-20: +80% → +30%
        }
    }

    /// <summary>溜め打ち (Windup) — 攻撃時+1蓄積 (上限10)、非会心攻撃時 蓄積×10% outgoing、会心発生でリセット。
    /// 「非会心を積むほど強くなり、会心事故で崩れる」蓄積型。</summary>
    public class Windup : IPassiveSkillEffect
    {
        public string SkillId => "重さを増す拳套";
        private const string StackKey = "windup_stack";
        private const int MaxStack = 10;
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnPreDealDamage,   // 蓄積を適用
            PassiveSkillTrigger.OnPostDealDamage,  // 蓄積++ or リセット
        };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnPreDealDamage)
            {
                if (ctx.finalDamage <= 0) return;
                int stacks = (int)ctx.GetAccumulated(StackKey);
                if (stacks > 0) ctx.nonCritOutgoingMultiplier += 0.10f * stacks;
            }
            else // OnPostDealDamage
            {
                if (ctx.isCritical) ctx.accumulatedValues[StackKey] = 0;
                else
                {
                    int cur = (int)ctx.GetAccumulated(StackKey);
                    if (cur < MaxStack) ctx.accumulatedValues[StackKey] = cur + 1;
                }
            }
        }
    }

    // ============================================================
    //  毒系 (2026-07-15 新規・4-2-2-1 テンプレで 9 種)
    //  差別化: 拘束・妨害中心。DoT は副次 (1/stack)、主効果は「麻痺毒 = 敵攻撃-N」による攻撃力ドレイン。
    //  減衰なし・上限5・遅効型 = 長期戦のキル手段 = 毒殺 (5到達で20%削り)。
    //  炎上 (短期爆発) / 出血 (継戦積み) / 毒 (拘束) で三役分離。
    // ============================================================

    /// <summary>毒塗り (PoisonCoat) — 戦闘開始時、敵に毒+1。最遅の起爆剤。</summary>
    public class PoisonCoat : IPassiveSkillEffect
    {
        public string SkillId => "緑染みの下拵え小刀";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.AddStatus(StatusTarget.Enemy, "poison", 1);
        }
    }

    /// <summary>腐蝕の一撃 (CorrosiveStrike) — 攻撃時、10% で敵毒+1。稀だが継続で必ず積む。</summary>
    public class CorrosiveStrike : IPassiveSkillEffect
    {
        public string SkillId => "CorrosiveStrike";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            if (GameLoop.GameRng.Value("passive.poisonProc") < 0.10f)
                ctx.AddStatus(StatusTarget.Enemy, "poison", 1);
        }
    }

    /// <summary>蛇の血 (SerpentBlood) — 会心時、敵毒+2。会心系と組んで加速。</summary>
    public class SerpentBlood : IPassiveSkillEffect
    {
        public string SkillId => "素手禁じの蛇血瓶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnCriticalDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.AddStatus(StatusTarget.Enemy, "poison", 2);
        }
    }

    /// <summary>毒の霧 (VenomFog) — 3ターン毎に敵毒+1 (時間が味方)。ターンレース系との相剋を狙う。</summary>
    public class VenomFog : IPassiveSkillEffect
    {
        public string SkillId => "主より長い香炉";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.currentTurn <= 0 || ctx.currentTurn % 3 != 0) return;
            ctx.AddStatus(StatusTarget.Enemy, "poison", 1);
        }
    }

    /// <summary>毒殺者 (AssassinToxin) — 敵毒=5 (上限) に到達したら、敵HP×20% を軽減不能で削る (1戦闘1回)。
    /// キル手段としての "5到達即死判定" (実際は 20% 削り = 実質瀕死化)。</summary>
    public class AssassinToxin : IPassiveSkillEffect
    {
        public string SkillId => "石抜きの毒指輪";
        private const string UsedKey = "assassin_toxin_used";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.GetAccumulated(UsedKey) > 0) return;
            if (ctx.GetStatus(StatusTarget.Enemy, "poison") < 5) return;
            if (ctx.enemyCurrentHP <= 0 || ctx.enemyMaxHP <= 0) return;
            int dmg = ctx.RatioDamageToEnemy(0.20f);
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
            ctx.accumulatedValues[UsedKey] = 1;
            UnityEngine.Debug.Log($"[毒殺者] 敵毒=5到達 → HP×20% = {dmg} 軽減不能ダメ → 敵HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>毒液噴射 (VenomBurst) — 攻撃時、敵毒2消費で 消費数×5 の軽減不能ダメ (2消費=10)。
    /// 積んだ毒を火力に変換 (BloodStrike の毒版・小型)。</summary>
    /// <summary>毒液噴射 (2026-09-05 リワーク) — 攻撃時、 <b>敵の毒スタック × 2</b> を軽減無視で追加。
    /// <b>毒は消費しない。</b>
    ///
    /// <para>旧: 毒を 2 消費して固定 10 ダメージ。 毒は減衰なし・上限10・stacks×1 の DOT なので、
    /// <b>消費する設計だと毒そのものと食い合う</b> ── 2 スタック (=2ダメ/T が永続) を
    /// 10 ダメージ 1 回に替える取引は、 3 ターン以上続く戦闘では損になる。</para>
    ///
    /// <para>シールドバッシュが「盾を消費せず残存量を読む」ことで<b>盾を持ち続けること自体を火力にした</b>
    /// (§3-A・2026-07-15 リワーク) のと同じ形へ揃えた。 毒を積む動きとペイオフが同じ方向を向く。</para></summary>
    public class VenomBurst : IPassiveSkillEffect
    {
        /// <summary>毒 1 スタックあたりの追加ダメージ。 上限 10 スタックで +20。</summary>
        private const int PerStack = 2;

        public string SkillId => "跡地庭師の霧吹き";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int p = ctx.GetStatus(StatusTarget.Enemy, "poison");
            if (p <= 0) return;
            ctx.fixedDamageToEnemy += p * PerStack;
        }
    }

    /// <summary>麻痺毒 (Paralysis) — 敵毒スタック分、敵攻撃-N (拘束の中核)。
    /// 5スタックで敵攻撃-5 = 拘束ビルドの主効果。CurseBind と重ねられる。</summary>
    public class Paralysis : IPassiveSkillEffect
    {
        public string SkillId => "岸上げ用の麻痺瓶";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int stacks = ctx.GetStatus(StatusTarget.Enemy, "poison");
            if (stacks <= 0) return;
            ctx.mutualEnemyAttackReduction += stacks;
        }
    }

    /// <summary>遅効の呪 (SlowVenomCurse) — T5以降、毎T敵毒+1 (長期戦保険)。
    /// 減衰なしと相乗し、10Tあれば 5+初期分 = キャップ埋まりで毒殺判定へ。</summary>
    public class SlowVenomCurse : IPassiveSkillEffect
    {
        public string SkillId => "SlowVenomCurse";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.currentTurn < 5) return;
            ctx.AddStatus(StatusTarget.Enemy, "poison", 1);
        }
    }

    // (炎上系 8 種 Kindling/FlamingWeapon/InfernoOrder/Stoke/Immortalflame/InfernoManifest/FlameEdge/EverBurning は
    //  2026-07-18 削除: Burn 全廃 → 臨界 (Rinkai) 軸に置換済み)

    // ============================================================
    //  出血系 拡張 (2026-07-15 追加・充電系と同じ 4-2-2-1 テンプレ)
    //  既存: Sting (Gen) / LacerationCore (Gen) / BloodPathBanner (状態バフ)
    //  新規 6 種で合計 9 種のキーワードビルド化
    // ============================================================

    /// <summary>紅蓮の刃 (CrimsonBlade) — 与ダメ時、敵HP割合で出血付与
    /// (≥50%: +1 / <50%: +2 / <25%: +3)。追い込み時ほど加速する出血ジェネレータ。</summary>
    public class CrimsonBlade : IPassiveSkillEffect
    {
        public string SkillId => "手負い追いの山刀";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.enemyMaxHP <= 0) return;
            int add;
            if (ctx.enemyCurrentHP * 4 <= ctx.enemyMaxHP) add = 3;         // <25%
            else if (ctx.enemyCurrentHP * 2 <= ctx.enemyMaxHP) add = 2;    // <50%
            else add = 1;                                                    // ≥50%
            ctx.AddEnemyBleed(add);
        }
    }

    /// <summary>絞り出し (Wringing) — ターン終了時、敵出血スタック×2 の追加固定ダメ (軽減無視)。
    /// 通常の出血DoT (BattleModifierManager) と二段構えで削る。</summary>
    public class Wringing : IPassiveSkillEffect
    {
        public string SkillId => "逆綴じの止血帯";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.enemyBleedStacks <= 0 || ctx.enemyCurrentHP <= 0) return;
            int dmg = ctx.enemyBleedStacks * 2;
            ctx.enemyCurrentHP = System.Math.Max(0, ctx.enemyCurrentHP - dmg);
            UnityEngine.Debug.Log($"[絞り出し] 敵出血×2 = {dmg} 追加ダメ (軽減無視) → 敵HP={ctx.enemyCurrentHP}");
        }
    }

    /// <summary>血の一撃 (BloodStrike) — 攻撃時、敵出血1消費で 与ダメ+スタック値×3 (消費前スタック数×3)。
    /// 積んだ出血を火力に変換するバースト。スタック多いほど爆発力大。</summary>
    public class BloodStrike : IPassiveSkillEffect
    {
        public string SkillId => "末頁の血花太刀";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.enemyBleedStacks <= 0) return;
            int stacks = ctx.enemyBleedStacks; // 消費前の値で計算
            ctx.enemyBleedStacks--;
            int bonus = stacks * 3;
            ctx.finalDamage += bonus;
            UnityEngine.Debug.Log($"[血の一撃] 出血-1 (残{ctx.enemyBleedStacks}) → 与ダメ+{bonus} (基{stacks}×3)");
        }
    }

    /// <summary>止血阻害 (AntiClotting) — 敵出血の自然減衰 (-1/T) を無効化。永続蓄積型ビルドの中核。</summary>
    public class AntiClotting : IPassiveSkillEffect
    {
        public string SkillId => "医書裏の開き針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.bleedDecayDisabled = true;
            UnityEngine.Debug.Log("[止血阻害] 敵出血の自然減衰を無効化 (永続蓄積)");
        }
    }

    /// <summary>血の宿命 (BloodFate) — 戦闘開始時、敵に出血+2。無料の初撃・出血起点。</summary>
    /// <summary>血の宿命 (2026-09-05 増補) — 戦闘開始時 出血+2、 <b>以降 毎ターン +1</b>。
    ///
    /// <para>出血は毎ターン 1 減衰するので、 開幕 +2 だけでは 2 ターンで消える一発ものだった
    /// (実測 準パワー -0.66 / regβ -0.005 ＝ ほぼ無効)。 毎ターン +1 は減衰と釣り合うので、
    /// <b>戦闘のあいだ 2 スタックを維持し続ける</b>形になる。</para></summary>
    public class BloodFate : IPassiveSkillEffect
    {
        public string SkillId => "先血の腕輪";
        public PassiveSkillTrigger[] Triggers => new[]
        { PassiveSkillTrigger.OnBattleStart, PassiveSkillTrigger.OnTurnStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            ctx.AddEnemyBleed(t == PassiveSkillTrigger.OnBattleStart ? 2 : 1);
        }
    }

    // ============================================================
    //  充電系 (ADR-0009 柱5・2026-07-15 追加)
    //  充電キー "mutualCharge" は ctx.GetCharge/AddCharge/ConsumeCharge/IsOvercharged 経由で操作。
    //  過充電 = charge >= ChargeMax (10)。新パイプライン専用 (旧では no-op)。
    // ============================================================

    /// <summary>蓄電池 — 戦闘開始時、充電+3。</summary>
    public class Battery : IPassiveSkillEffect
    {
        public string SkillId => "工廠の材料箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            ctx.AddCharge(3);
        }
    }

    /// <summary>発電機 — 被ダメージ時、充電+1 (完全防御時は不発)。</summary>
    public class Generator : IPassiveSkillEffect
    {
        public string SkillId => "無限モーター";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            if (ctx.finalDamage > 0) ctx.AddCharge(1);
        }
    }

    /// <summary>触媒 — 与ダメージ時、充電+1。</summary>
    public class Catalyst : IPassiveSkillEffect
    {
        public string SkillId => "呼雷粉";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            if (ctx.finalDamage > 0) ctx.AddCharge(1);
        }
    }

    /// <summary>雷雲 — ロール後、25% で充電+1 (自ロール毎に判定)。</summary>
    public class Thundercloud : IPassiveSkillEffect
    {
        public string SkillId => "Thundercloud";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            if (GameLoop.GameRng.Value("passive.chargeProc") < 0.25f) ctx.AddCharge(1);
        }
    }

    /// <summary>火花 — 攻撃時、充電1消費で与ダメ+5 (足りなければ不発)。</summary>
    public class Spark : IPassiveSkillEffect
    {
        public string SkillId => "焦げ柄の点火スパナ";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            if (ctx.finalDamage <= 0) return;
            if (ctx.ConsumeCharge(1)) ctx.finalDamage += 5;
        }
    }

    /// <summary>雷撃 — 攻撃時、充電3消費で与ダメ +30% (足りなければ不発)。 2026-09-20: +50% → +30%。</summary>
    public class LightningStrike : IPassiveSkillEffect
    {
        public string SkillId => "逆さ避雷針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            if (ctx.finalDamage <= 0) return;
            if (!ctx.ConsumeCharge(3)) return;
            if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
            ctx.outgoingDamageMultiplier += 0.3f;
        }
    }

    /// <summary>短絡 (2026-07-16 改名: 旧 Criticality → ShortCircuit)。
    /// 過充電状態 (charge≥7) が3ターン続いたら会心確定、充電を全消費してクールダウン。
    /// OnPostRoll でストリーク計測、閾値到達で OnCriticalCheck 時に会心強制+リセット。
    /// 「臨界」キーワードは別軸 (自メーター爆発型) に割り当てるためリネーム。</summary>
    /// <summary>短絡 (2026-09-05 リワーク) — <b>そのターンに消費した充電の 50% を攻撃値へ加算</b>。
    ///
    /// <para>旧: 過充電 (充電7以上) が 3 ターン続いたら会心確定＋充電全消費。
    /// 「溜め続ける」ことが条件なのに、 <b>充電の主な使い道はリロール (ADR-0010) で消費すること</b>
    /// ── 溜めと使いが正面から食い合っており、 実測 準パワー -0.88 と成立していなかった。</para>
    ///
    /// <para>新は逆向きに、 <b>払った充電が火力になる</b>。 リロールを回すほど攻撃が伸びるので、
    /// 充電軸の「振り直して手を作る」動きとペイオフが同じ方向を向く。</para>
    ///
    /// <para>OnPostRoll で読むのは、 充電の消費 (リロール) が<b>配線より前</b>のフェーズで
    /// 完了しているから (§9.1 step4 → step5)。 攻撃値への加算は OnPostRoll でしか乗らない。</para></summary>
    public class ShortCircuit : IPassiveSkillEffect
    {
        /// <summary>消費充電の何割を攻撃へ回すか。</summary>
        private const float ReturnRate = 0.50f;

        public string SkillId => "三度不良の銅線";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostRoll };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int spent = (int)ctx.GetAccumulated(CombatContext.ChargeSpentThisTurnKey);
            if (spent <= 0) return;
            int gain = UnityEngine.Mathf.FloorToInt(spent * ReturnRate);
            if (gain <= 0) return;
            ctx.AddPlayerAttackOrDiceBonus(gain);
            UnityEngine.Debug.Log($"[短絡] 消費充電 {spent} → 攻撃 +{gain}");
        }
    }

    /// <summary>予備電源 — 戦闘中、充電が0になった時、一度だけ充電を全回復。
    /// OnTurnStart / OnTurnEnd で判定 (消費機構が動くフェーズを両方カバー)。</summary>
    public class BackupPower : IPassiveSkillEffect
    {
        public string SkillId => "雷壺";
        private const string UsedKey = "backup_power_used";
        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnTurnStart,
            PassiveSkillTrigger.OnTurnEnd,
        };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!CombatSystem.CombatManager.UseMutualAttackPipeline) return;
            if (ctx.GetAccumulated(UsedKey) > 0) return;
            if (ctx.GetCharge() > 0) return;
            ctx.AddCharge(CombatSystem.CombatManager.ChargeMax); // 全回復 (=最大)
            ctx.accumulatedValues[UsedKey] = 1;
            UnityEngine.Debug.Log($"[予備電源] 充電0検知 → 充電満タン ({CombatSystem.CombatManager.ChargeMax}) に復旧");
        }
    }

    // ============================================================
    //  臨界系 (Rinkai) 2026-07-16 追加: メーター蓄積型 (Burn 削除後の新軸)
    //  attackSum を毎T末に meter へ加算、閾値 (基準50) 到達で 次T の攻撃に +BurstDamage (基準50)。
    //  ヘルパー: ctx.rinkaiThreshold/rinkaiBurstDamage/rinkaiAfterglow/rinkaiRadiationBonus 経由。
    //  「基礎値」を確立する起点パッシブ = 発火 (Ignition) — 開始時 threshold=50, burstDamage=50 に initialise。
    //  他パッシブは基礎値を修飾 (LowerThreshold で 35 に等)。
    //  9種: Ignition/Conduction/Radiation/CriticalPressure/ChainCombustion/Afterglow/LowerThreshold/NoCooldown/UnyieldingHeat
    // ============================================================

    /// <summary>臨界の基礎値を初期化 (どの Rinkai パッシブでも1度呼ばれれば有効化される)。</summary>
    internal static class RinkaiInit
    {
        public const int DefaultThreshold = 50;
        public const int DefaultBurstDamage = 50;
        public static void EnsureEnabled(CombatContext ctx)
        {
            if (ctx.rinkaiThreshold <= 0) ctx.rinkaiThreshold = DefaultThreshold;
            if (ctx.rinkaiBurstDamage <= 0) ctx.rinkaiBurstDamage = DefaultBurstDamage;
        }
    }

    /// <summary>発火 (Ignition) — 戦闘開始時、臨界メーター +15 (初速ジャンプ・軸の起点パッシブ)。</summary>
    public class Ignition : IPassiveSkillEffect
    {
        public string SkillId => "余分に乾いた火口箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiMeter += 15;
            UnityEngine.Debug.Log($"[発火] 開幕 メーター+15 (現{ctx.rinkaiMeter}/{ctx.rinkaiThreshold})");
        }
    }

    /// <summary>熱伝導 (Conduction) — 被ダメ時、その量だけ臨界メーターに加算 (受けても積む)。</summary>
    public class Conduction : IPassiveSkillEffect
    {
        public string SkillId => "炉番の火床外套";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiConductionEnabled = true;
            UnityEngine.Debug.Log("[熱伝導] 被ダメ量を臨界メーターに追加加算");
        }
    }

    /// <summary>輻射 (Radiation) — 攻撃配線した T、臨界メーターに +5 追加 (通常加算に上乗せ)。</summary>
    public class Radiation : IPassiveSkillEffect
    {
        public string SkillId => "過熱石";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiRadiationBonus += 5;
            UnityEngine.Debug.Log($"[輻射] 攻撃配線Tのメーター追加+{ctx.rinkaiRadiationBonus}");
        }
    }

    /// <summary>臨界圧 (CriticalPressure) — 臨界爆発時の flat damage を 50→80 に強化。</summary>
    public class CriticalPressure : IPassiveSkillEffect
    {
        public string SkillId => "溢れを取る鋳型";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiBurstDamage = System.Math.Max(ctx.rinkaiBurstDamage, 80);
            UnityEngine.Debug.Log($"[臨界圧] 爆発ダメ強化 → {ctx.rinkaiBurstDamage}");
        }
    }

    /// <summary>連鎖爆発 (ChainCombustion) — 臨界爆発発動T の攻撃は会心確定。</summary>
    public class ChainCombustion : IPassiveSkillEffect
    {
        public string SkillId => "帳簿外の連鎖爆発";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiCritOnBurst = true;
            UnityEngine.Debug.Log("[連鎖爆発] 臨界爆発時 会心確定");
        }
    }

    /// <summary>余熱 (Afterglow) — 臨界爆発後、meter が 0 でなく 20 残る (連続爆発容易)。</summary>
    public class Afterglow : IPassiveSkillEffect
    {
        public string SkillId => "朝にも熱い竈";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiAfterglow = System.Math.Max(ctx.rinkaiAfterglow, 20);
            UnityEngine.Debug.Log($"[余熱] 爆発後 meter 残置 → {ctx.rinkaiAfterglow}");
        }
    }

    /// <summary>降下閾値 (LowerThreshold) — 臨界爆発の閾値を 50→35 に。 到達速度アップ。</summary>
    public class LowerThreshold : IPassiveSkillEffect
    {
        public string SkillId => "気短な早沸かし釜";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            ctx.rinkaiThreshold = System.Math.Min(ctx.rinkaiThreshold, 35);
            UnityEngine.Debug.Log($"[降下閾値] 閾値 → {ctx.rinkaiThreshold}");
        }
    }

    /// <summary>不冷却 (NoCooldown) — 爆発後 meter が 0/afterglow でなく half (threshold/2) 残る。爆発連鎖強化。</summary>
    public class NoCooldown : IPassiveSkillEffect
    {
        public string SkillId => "恒熱の炉壁";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            RinkaiInit.EnsureEnabled(ctx);
            int half = ctx.rinkaiThreshold / 2;
            ctx.rinkaiAfterglow = System.Math.Max(ctx.rinkaiAfterglow, half);
            UnityEngine.Debug.Log($"[不冷却] 爆発後 meter 残置 → {ctx.rinkaiAfterglow} (threshold {ctx.rinkaiThreshold} の半分)");
        }
    }

    /// <summary>不朽の熱 (UnyieldingHeat) — 臨界メーター≥40 の間に致命ダメを受けた時、
    /// HP=1 で踏みとどまり meter を全消費 (Burn Immortalflame の遺伝子継承)。 1戦闘1回。</summary>
    public class UnyieldingHeat : IPassiveSkillEffect
    {
        public string SkillId => "七日目の熾";
        public PassiveSkillTrigger[] Triggers => new[] {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPreReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnBattleStart)
            {
                RinkaiInit.EnsureEnabled(ctx);
                ctx.rinkaiUnyieldingUsed = false;
                return;
            }
            if (ctx.rinkaiUnyieldingUsed) return;
            if (ctx.finalDamage <= 0) return;
            if (ctx.rinkaiMeter < 40) return;
            if (ctx.finalDamage < ctx.playerCurrentHP) return; // 致命ダメでなければ発動しない
            int lethalDmg = ctx.finalDamage;
            ctx.finalDamage = System.Math.Max(0, ctx.playerCurrentHP - 1);
            int consumed = ctx.rinkaiMeter;
            ctx.rinkaiMeter = 0;
            ctx.rinkaiUnyieldingUsed = true;
            UnityEngine.Debug.Log($"[不朽の熱] 致命ダメ{lethalDmg} → HP=1 踏みとどまり + メーター{consumed}全消費");
        }
    }

    /// <summary>ブレイドダンス (特殊・4枚集約の変化先): 戦闘突入ごとに[剣先]スタック+1（最大99・ラン中持続）。
    /// ダイス合計に剣先スタック分を加算。与ダメージ時に剣先スタック分HP回復、被ダメージ時に剣先スタック分の
    /// 軽減不可ダメージを相手へ。スタックはランを跨がず IRunResettable でリセット。</summary>
    public class BladeDance : IPassiveSkillEffect, IRunResettable
    {
        public string SkillId => "ブレイドダンス";
        private int kensaki = 0; // 剣先スタック（ラン中持続）

        public void ResetRunState() { kensaki = 0; }

        /// <summary>4枚集約による BD 取得時の初期スタック付与 (現在層 × 3)。
        /// 取得が後半層に偏る構造のため、 集約成功への報酬として「現時点の層数 × 3」 をプリロードする。
        /// 既に kensaki > seed の場合は上書きしない (後から再取得→上書きで減少を防ぐ)。</summary>
        public void SeedOnAcquire(int floor)
        {
            int seed = System.Math.Min(99, System.Math.Max(0, floor) * 5);
            if (seed > kensaki) kensaki = seed;
            UnityEngine.Debug.Log($"[ブレイドダンス] 取得時シード: floor={floor} → kensaki={kensaki}");
        }

        public PassiveSkillTrigger[] Triggers => new[]
        {
            PassiveSkillTrigger.OnBattleStart,
            PassiveSkillTrigger.OnPostRoll,
            PassiveSkillTrigger.OnPostDealDamage,
            PassiveSkillTrigger.OnPostReceiveDamage,
        };
        public void Execute(PassiveSkillTrigger trigger, CombatContext ctx)
        {
            switch (trigger)
            {
                case PassiveSkillTrigger.OnBattleStart:
                    kensaki = System.Math.Min(99, kensaki + 1);
                    UnityEngine.Debug.Log($"[ブレイドダンス] 戦闘突入: 剣先スタック → {kensaki}");
                    break;
                case PassiveSkillTrigger.OnPostRoll:
                    // 旧: ダイス合計+剣先 / 新: 攻撃+剣先
                    if (kensaki > 0) ctx.AddPlayerAttackOrDiceBonus(kensaki);
                    break;
                case PassiveSkillTrigger.OnPostDealDamage:
                    // 与ダメージ時: 剣先スタック分HP回復
                    if (kensaki > 0 && ctx.finalDamage > 0)
                        ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + kensaki);
                    break;
                case PassiveSkillTrigger.OnPostReceiveDamage:
                    // 被ダメージ時: 剣先スタック分の軽減不可ダメージを相手へ
                    if (kensaki > 0 && ctx.finalDamage > 0)
                        ctx.fixedDamageToEnemy += kensaki;
                    break;
            }
        }
    }

}
