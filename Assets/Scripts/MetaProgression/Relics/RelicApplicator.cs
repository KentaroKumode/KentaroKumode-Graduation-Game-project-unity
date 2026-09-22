using GameLoop;
using InventorySystem.PassiveSkills;
using UnityEngine;

namespace MetaProgression.Relics
{
    /// <summary>
    /// 装備中の遺物の効果を各システムへ渡す中央ヘルパー。 正本: docs/GAME.md §15-5。
    /// MetaBuffApplicator と同じ形（静的 getter 群）に揃えてあるので、
    /// 呼び出し側はメタバフの隣に 1 行足すだけで済む。
    ///
    /// **刻印はメイン枠にだけ掛かる。** 条件を満たしていない間はメイン枠の軸が 0 を返し、
    /// サブ枠は常に働く（遺物が丸ごと死に装備にならないようにするため）。
    /// 刻印付きのメインは段 7 として評価する。
    /// </summary>
    public static class RelicApplicator
    {
        private static RolledRelic Equipped => MetaProgressManager.Instance?.State?.EquippedRelic;

        /// <summary>刻印の条件を満たしているか。 刻印なしなら常に true。
        ///
        /// ctx が無い文脈（ラン開始時の最大HP など）では、 毎ターン判定の 2 条件は
        /// **成立扱い**にする ── ラン開始時にはまだ配線もオーバーロードも起きていないため。</summary>
        public static bool MainActive(RunState run, CombatContext ctx)
        {
            var r = Equipped;
            if (r == null || !r.IsCursed) return true;
            switch (r.Curse)
            {
                case RelicCurse.NoConsumables:
                    return CountConsumables(run) == 0;
                case RelicCurse.LowGold:
                    return run == null || run.coins <= 10;
                case RelicCurse.NoBlockWiring:
                    return ctx == null || !ctx.blockWiredThisTurn;
                case RelicCurse.NoRoleFired:
                    // ADR-0010: 〈オーバーロード未使用〉の後継。 **役を 1 つも切っていないターン**。
                    //   評価順の注意 ── 役の判定 (5b) より前に呼ばれる遺物 (充電の毎ターン成分) では
                    //   まだ空なので常に成立する。 旧〈オーバーロード未使用〉も同じ順序だった。
                    return ctx == null || ctx.firedRolesThisTurn.Count == 0;
                default:
                    return true;
            }
        }

        private static int CountConsumables(RunState run)
            => run?.ownedConsumables != null ? run.ownedConsumables.Count : 0;

        /// <summary>指定軸の段。 載っていなければ 0。
        /// メイン枠なら刻印の条件を見て、 満たしていなければ 0 を返す。</summary>
        private static int StepOf(RelicAxis axis, RunState run, CombatContext ctx)
        {
            var r = Equipped;
            if (r == null) return 0;
            int i = r.IndexOf(axis);
            if (i < 0) return 0;
            if (i == 0 && !MainActive(run, ctx)) return 0;   // 刻印の条件を満たしていない
            return r.EffectiveStepAt(i);
        }

        /// <summary>指定軸の実数値。 載っていない／刻印の条件を満たしていなければ 0。</summary>
        private static int ValueOf(RelicAxis axis, RunState run, CombatContext ctx)
        {
            int step = StepOf(axis, run, ctx);
            return step > 0 ? RelicAxisCatalog.ValueOf(axis, step) : 0;
        }

        // ============================================================
        //  戦闘中に毎ターン参照するもの
        // ============================================================

        /// <summary>攻撃+N。 ctx.mutualAttackBonus へ加算する。</summary>
        public static int GetAttackBonus(RunState run, CombatContext ctx)
            => ValueOf(RelicAxis.Attack, run, ctx);

        /// <summary>与ダメージ+N%。 他の outgoing% と同じ pool へ additive。
        /// 〈渇き〉〈刻限〉分を含む（軸は重複しないので同時に載ることは無い）。</summary>
        public static float GetOutgoingPct(RunState run, CombatContext ctx)
            => ValueOf(RelicAxis.DamagePct, run, ctx) * 0.01f
             + GetHopeBurnPct(run, ctx)
             + GetLongBattlePct(run, ctx);

        /// <summary>会心率+N%。 0.0〜0.28 を返す（ctx.critRatePctAdd へ加算）。</summary>
        public static float GetCritRatePct(RunState run, CombatContext ctx)
            => ValueOf(RelicAxis.CritRatePct, run, ctx) * 0.01f;

        /// <summary>会心倍率+N%。 **基準 2.0 に対する**加算量を返す（段6 の +90% なら +1.8）。
        /// 2026-08-03 に基準 3.0→2.0（[MetaBuffApplicator.GetCriticalMultiplier]）。
        /// 基準3.0 のときこの軸は段6 で実効 ×1.53 と 12軸で突出し、 最弱軸との差が 4.2 倍あった。</summary>
        public static float GetCritMultBonus(RunState run, CombatContext ctx)
            => ValueOf(RelicAxis.CritMultPct, run, ctx) * 0.01f * 2.0f
             + GetLambdaResonanceBonus(run, ctx);

        /// <summary>〈Λ共鳴〉の会心倍率加算。 **Λ層で終えた戦闘数 × 段の値%** を、
        /// CritMultPct と同じ「基準 2.0 に対する加算」へ換算して返す。
        ///
        /// 難易度 25 以上でしか出ない軸なので、 低難易度では常に 0。 Λ へ潜らなければ 0。
        /// 賭けに乗った時だけ大きく返る形にしてある (期待値を平坦に薄めない)。
        /// 例: 段6 (+14%/戦) で Λ を 10 戦こなすと +140% → 基準 2.0 に対し +2.8。</summary>
        public static float GetLambdaResonanceBonus(RunState run, CombatContext ctx)
        {
            if (run == null) return 0f;
            int step = StepOf(RelicAxis.LambdaResonance, run, ctx);
            if (step <= 0) return 0f;
            int perCombat = RelicAxisCatalog.ValueOf(RelicAxis.LambdaResonance, step);
            return run.lambdaCombatsFinished * perCombat * 0.01f * 2.0f;
        }

        /// <summary>〈渇き〉希望が <see cref="RelicAxisCatalog.HopeBurnThreshold"/> 以下の間の
        /// 与ダメージ +N%。 条件を外れていれば 0。
        ///
        /// **絶対値で判定する。** hopeCap は一方向のラチェット（希望 45 以下で上限 45、
        /// 20 以下で上限 20 に固定＝それ以上回復できない）なので、 比にすると閾値も
        /// 40→18→8 と一緒に下がり、 希望が閾値へ永久に追いつかない。
        /// 実測 (計装 33,864 判定): 比だった頃の発動率は **6.6%** しかなかった。</summary>
        public static float GetHopeBurnPct(RunState run, CombatContext ctx)
        {
            if (run == null) return 0f;
            int step = StepOf(RelicAxis.HopeBurn, run, ctx);
            if (step <= 0) return 0f;
            // [計装] 「弱い」のか「そもそも発動していない」のかを数字で分けるための計数。
            //   実測では倍率を 3 回上げても与ダメが動かず、 原因の切り分けができなかった。
            HopeBurnChecks++;
            HopeBurnHopeSum += run.hope;
            HopeBurnCapSum  += Mathf.Max(1, run.hopeCap);
            // **絶対値で判定する。** hopeCap 比にすると、 希望が 45 を割った時の cap ロック
            //   (45以下で上限45 / 20以下で上限20) で閾値まで一緒に下がり、 永久に条件を
            //   満たさない (RelicAxis の HopeBurnThreshold 参照)。
            if (run.hope > RelicAxisCatalog.HopeBurnThreshold) return 0f;
            HopeBurnActive++;
            return RelicAxisCatalog.ValueOf(RelicAxis.HopeBurn, step) * 0.01f;
        }

        // ── 〈渇き〉の計装。 スイープが読んでレポートへ出す。 ResetHopeBurnStats で 0 に戻す ──
        /// <summary>〈渇き〉を載せた状態で与ダメ計算が走った回数。</summary>
        public static long HopeBurnChecks;
        /// <summary>そのうち条件 (希望 ≤ hopeCap×閾値) を満たしていた回数。</summary>
        public static long HopeBurnActive;
        /// <summary>判定時の希望の合計 (平均を出すため)。</summary>
        public static long HopeBurnHopeSum;
        /// <summary>判定時の hopeCap の合計 (平均を出すため)。</summary>
        public static long HopeBurnCapSum;

        public static void ResetHopeBurnStats()
        { HopeBurnChecks = HopeBurnActive = HopeBurnHopeSum = HopeBurnCapSum = 0; }

        /// <summary>「発動率 x% / 判定時の平均希望 y (上限 z)」の 1 行。</summary>
        public static string DescribeHopeBurnStats()
            => HopeBurnChecks == 0 ? "〈渇き〉判定なし"
             : $"〈渇き〉発動率 {HopeBurnActive * 100.0 / HopeBurnChecks:F1}% "
             + $"({HopeBurnActive}/{HopeBurnChecks}) / 判定時の平均希望 "
             + $"{HopeBurnHopeSum / (double)HopeBurnChecks:F1} (上限 {HopeBurnCapSum / (double)HopeBurnChecks:F1}, "
             + $"閾値 {RelicAxisCatalog.HopeBurnThreshold} 固定)";

        /// <summary>〈刻限〉経過ターン数 × N% の与ダメージ加算。 ctx が無ければ 0。
        ///
        /// **エスカレーション段階ではなく経過ターン数を直接見る**。 段階連動は実測で
        /// 平均 0.29（全ターンの 77.5% が段階 0）にしかならず死に軸だった (§24)。</summary>
        public static float GetLongBattlePct(RunState run, CombatContext ctx)
        {
            if (ctx == null) return 0f;
            int step = StepOf(RelicAxis.LongBattle, run, ctx);
            if (step <= 0) return 0f;
            int turn = Mathf.Max(0, ctx.currentTurn);
            return turn * RelicAxisCatalog.ValueOf(RelicAxis.LongBattle, step) * 0.01f;
        }

        /// <summary>〈背水〉の条件 ── HP が最大の
        /// <see cref="RelicAxisCatalog.LastBreathThreshold"/> 以下か。
        ///
        /// **ctx 側の HP を見る。** run.playerHP は戦闘中は同期されず、 戦闘終了時に書き戻される
        /// （CombatManager が playerHP をローカルに持ち、 ctx.playerCurrentHP へ都度反映する）。</summary>
        private static bool LastBreathActive(CombatContext ctx)
            => ctx != null && ctx.playerMaxHP > 0
            && ctx.playerCurrentHP <= ctx.playerMaxHP * RelicAxisCatalog.LastBreathThreshold;

        /// <summary>〈背水〉低HP 中の被ダメージ −N%。 条件を外れていれば 0。</summary>
        public static float GetLastBreathPct(RunState run, CombatContext ctx)
        {
            if (!LastBreathActive(ctx)) return 0f;
            int step = StepOf(RelicAxis.LastBreath, run, ctx);
            if (step <= 0) return 0f;
            return RelicAxisCatalog.ValueOf(RelicAxis.LastBreath, step) * 0.01f;
        }

        /// <summary>〈背水〉低HP 中の吸収 ── 与ダメージの N% を回復。 ctx.lifestealPct へ加算する。
        ///
        /// **自己減衰する**: 回復して閾値を超えた瞬間に条件が切れるので、 閾値の周りで振動し、
        /// 際限なく硬くなる方向へは走らない。 これが「賭け」として成立する理由でもある ──
        /// 低HP に留まる限りしか働かないので、 安全域へ戻れば手放すことになる。</summary>
        public static float GetLastBreathLifestealPct(RunState run, CombatContext ctx)
        {
            if (!LastBreathActive(ctx)) return 0f;
            int step = StepOf(RelicAxis.LastBreath, run, ctx);
            if (step <= 0) return 0f;
            return RelicAxisCatalog.LastBreathLifesteal(step) * 0.01f;
        }

        /// <summary>防御貫通+N%。 0.0〜0.28 を返す（ctx.armorPenPct へ加算）。</summary>
        public static float GetArmorPenPct(RunState run, CombatContext ctx)
            => ValueOf(RelicAxis.ArmorPenPct, run, ctx) * 0.01f;

        /// <summary>被ダメージ−N%。 **割合軽減**であって定額ではない
        /// （定額だと参照被ダメ 7.7/T に対し段6 が 78% 軽減になり序盤を無力化した・§24）。
        /// 〈背水〉分を含む。 両方載ることは無い（軸は重複しない）が、 念のため 0.85 で頭打ち。</summary>
        public static float GetDamageReductionPct(RunState run, CombatContext ctx)
            => Mathf.Min(0.85f,
                   ValueOf(RelicAxis.DamageReductionPct, run, ctx) * 0.01f
                 + GetLastBreathPct(run, ctx));

        /// <summary>臨界爆発時に追加する、 敵の**現在**HP に対する割合 (0.0〜0.07)。</summary>
        public static float GetRinkaiCurrentHpPct(RunState run, CombatContext ctx)
            => ValueOf(RelicAxis.Rinkai, run, ctx) * 0.01f;

        /// <summary>毎ターンの充電加算。 段 4 以上でのみ 0 より大きい。</summary>
        public static int GetChargePerTurn(RunState run, CombatContext ctx)
        {
            int step = StepOf(RelicAxis.Charge, run, ctx);
            return step > 0 ? RelicAxisCatalog.ChargePerTurn(step) : 0;
        }

        // ============================================================
        //  戦闘開始時に 1 回だけ参照するもの
        // ============================================================

        /// <summary>開幕シールド+N。</summary>
        public static int GetOpeningShield(RunState run)
            => ValueOf(RelicAxis.OpeningShield, run, null);

        /// <summary>開幕の充電。</summary>
        public static int GetOpeningCharge(RunState run)
        {
            int step = StepOf(RelicAxis.Charge, run, null);
            return step > 0 ? RelicAxisCatalog.ChargeOpening(step) : 0;
        }

        /// <summary>開幕出血+N。 短期戦専用（ボス戦では N ターンで枯れる）。</summary>
        public static int GetOpeningBleed(RunState run)
            => ValueOf(RelicAxis.OpeningBleed, run, null);

        /// <summary>開幕毒+N。 減衰しないので長期戦向き。 上限 10 は AddStatus 側でクランプされる。</summary>
        public static int GetOpeningPoison(RunState run)
            => ValueOf(RelicAxis.OpeningPoison, run, null);

        // ============================================================
        //  ラン開始時に 1 回だけ参照するもの
        // ============================================================

        /// <summary>最大HP+N。 RunState.Initialize 直後に足す。</summary>
        public static int GetMaxHpBonus(RunState run)
            => ValueOf(RelicAxis.MaxHp, run, null);

        /// <summary>〈渇き〉を装備しているときの**開幕希望の上限**。 非装備なら 0（＝制限なし）。
        ///
        /// この軸は「希望 ≤ hopeCap×40% の間だけ与ダメ+N%」という条件付きだが、
        /// 開幕 100 のままだと序盤に一度も条件を満たさない ── 実測で 1〜3層の 1攻撃与ダメが
        /// 遺物なし (52.1) に対し 53.6 ＝ **+2.5% しか動かず、ほぼ発動していなかった**。
        /// 倍率を +63%→+126% に倍化しても結果は変わらなかった（発動しない区間では 0 のため）。
        ///
        /// そこで**軸自身が発動条件を作る**。 開幕から発動圏に居る代わりに、
        /// 希望という資源を丸ごと前借りする（横移動の自由度・発狂までの余裕を失う）。
        /// 賭けとして自己完結し、 他のバランスには一切影響しない。</summary>
        public static int GetHopeBurnStartHope(RunState run)
        {
            // ctx が無い文脈なので刻印は成立扱い（RelicApplicator の規約）。
            int step = StepOf(RelicAxis.HopeBurn, run, null);
            return step > 0 ? RelicAxisCatalog.HopeBurnStartHope : 0;
        }

        /// <summary>run を持たない呼び出し口（RunState.Initialize の途中）用。</summary>
        public static int GetHopeBurnStartHope() => GetHopeBurnStartHope(null);

        // ============================================================
        //  デバッグ表示
        // ============================================================

        public static string DescribeEquipped()
        {
            var r = Equipped;
            return r == null ? "遺物なし" : r.Describe();
        }
    }
}
