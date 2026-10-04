namespace InventorySystem.PassiveSkills.Effects
{
    // ============================================================
    //  コンボピック (2026-10-04)
    //
    //  「単品では地味で、 相方を拾うと成立する」品。 設計の正本は docs/design/combo-picks.md。
    //   ・輪 (互いが互いを何度も起こす): 毒の連鎖爆破 / 共鳴する炉心 / 血の祝祭
    //   ・組 (入口を足して出口を太くする): 出血の帳簿 / 毒と充電の輪 / 盾の再装填 / 血の契約
    //  どれも**決定的に毎ターン乗る** (確率で当たる火力は分散に埋もれるので使わない)。
    //  「〜が付くたび」の数え方は CombatContext の入口 (AddEnemyBleed / AddStatus / GainShield /
    //  SelfInflictHpLoss) に置いてあり、 効果クラスは**ルールのフラグを代入する**だけ (加算しない)。
    //  戦闘開始時に何度呼ばれても結果が同じになるように、 ここのフラグ設定はすべて代入。
    // ============================================================

    // ------------------------------------------------------------
    //  出血の帳簿: 刃毀れの鑢 (入口) × 血の帳簿 (出口)
    // ------------------------------------------------------------

    /// <summary>刃毀れの鑢 (SILVER) — 被ダメージ時と攻撃時に、 敵に出血 +1。 単体でも出血で削る品。</summary>
    public class DullFile : IPassiveSkillEffect
    {
        public string SkillId => "刃毀れの鑢";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPostDealDamage, PassiveSkillTrigger.OnPostReceiveDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;   // 攻撃を配線していない / 被ダメージが出ていないターンは付かない
            ctx.AddEnemyBleed(1);
        }
    }

    /// <summary>血の帳簿 (GOLD) — 敵に出血が新たに付くたび、 この戦闘中の攻撃 +1 (上限 <see cref="Cap"/>)。
    /// 数えるのは「付与の回数」で、 スタック数ではない (1 回の付与 = +1)。
    /// 上限は自傷の出血 (死に際の算盤) と組んだ時の暴走止め: 1 ターンに +3 ずつ積み上がると 3 ターンで +9 になる。</summary>
    public class BleedLedger : IPassiveSkillEffect
    {
        public const int Cap = 10;
        public string SkillId => "血の帳簿";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0) return;
            int bonus = System.Math.Min(Cap, ctx.bleedApplications);
            if (bonus <= 0) return;
            ctx.finalDamage += bonus;
            UnityEngine.Debug.Log($"[血の帳簿] 出血の付与 {ctx.bleedApplications} 回 → 攻撃 +{bonus} (上限 {Cap})");
        }
    }

    // ------------------------------------------------------------
    //  毒と充電の輪: 毒の蓄電器 × 振り直しの毒針
    //    リロール → 毒 → 充電 → またリロール。 毒の上限 (10) で止まる
    // ------------------------------------------------------------

    /// <summary>毒の蓄電器 (SILVER) — 敵の毒が**実際に増える**たび、 充電 +1。</summary>
    public class PoisonCapacitor : IPassiveSkillEffect
    {
        public string SkillId => "毒の蓄電器";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { ctx.chargePerPoisonIncrease = 1; }
    }

    /// <summary>振り直しの毒針 (GOLD) — リロールするたび、 敵に毒 +1。
    /// 〈石抜きの毒指輪〉(毒 5 で敵 HP の 20%) とは、 リロール 5 回で届く。 単体でも強い恐れがあるので測定対象。</summary>
    public class RerollNeedle : IPassiveSkillEffect
    {
        public string SkillId => "振り直しの毒針";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { ctx.poisonPerReroll = 1; }
    }

    // ------------------------------------------------------------
    //  盾の再装填: 錆びた留め金 (+ 騎士団の切り札 / 内から落ちた城盾)
    // ------------------------------------------------------------

    /// <summary>錆びた留め金 (SILVER) — シールドが減ったターン (被弾でも、 攻撃に変えた時でも)、
    /// 次のターンの開始時に**減った量の半分**を取り戻す。
    /// 減った量は「ターン頭の残り + そのターンの獲得 − ターン末の残り」で測る。
    /// 減る経路は書き換え箇所が散っていて入口が無いが、 獲得は <see cref="CombatContext.GainShield"/> に集約したので差分で足りる。</summary>
    public class RustyClasp : IPassiveSkillEffect
    {
        private const string BuffKey = "rustyClaspRegain";
        private const string StartKey = "rustyClaspStart";
        private const string GainKey = "rustyClaspGain0";
        public string SkillId => "錆びた留め金";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnStart, PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (t == PassiveSkillTrigger.OnTurnStart)
            {
                // 取り戻しを先に入れてから、 そのターンの基準点を取る (取り戻した分を「獲得」に数えない)
                if (ctx.currentBuffs.TryGetValue(BuffKey, out float regain) && regain > 0f)
                {
                    int got = ctx.GainShield((int)regain);
                    UnityEngine.Debug.Log($"[錆びた留め金] 前ターンに減った分の半分 +{got} を取り戻す");
                }
                ctx.accumulatedValues[StartKey] = ctx.consShield;
                ctx.accumulatedValues[GainKey] = ctx.shieldGainedTotal;
                return;
            }
            int start = (int)ctx.GetAccumulated(StartKey);
            int gained = ctx.shieldGainedTotal - (int)ctx.GetAccumulated(GainKey);
            int lost = start + gained - ctx.consShield;
            if (lost >= 2) ctx.nextTurnBuffs[BuffKey] = lost / 2;
        }
    }

    // ------------------------------------------------------------
    //  血の契約: 血契の釘 (自傷で攻撃) × 死に際の算盤 (HP の減りを出血に)
    // ------------------------------------------------------------

    /// <summary>血契の釘 (GOLD) — 攻撃を配線したターン、 HP を 2 失い、 攻撃 +5。
    /// HP は 1 を床にする。 **払えない時 (HP 1) は攻撃も乗らない** (代償を払わずに得をさせない)。</summary>
    public class BloodPactNail : IPassiveSkillEffect
    {
        public const int HpCost = 2, AttackBonus = 5;
        public string SkillId => "血契の釘";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.GetAccumulated("mutualAttackDiceCount") <= 0f) return;   // 攻撃を配線したターンだけ
            if (ctx.finalDamage <= 0) return;
            int paid = ctx.SelfInflictHpLoss(HpCost);
            if (paid <= 0) return;
            ctx.finalDamage += AttackBonus;
            UnityEngine.Debug.Log($"[血契の釘] HP -{paid} → 攻撃 +{AttackBonus}");
        }
    }

    /// <summary>死に際の算盤 (GOLD) — HP が減るたび (敵の攻撃で被ダメージが出た回・自傷を 1 回ずつ数える)、 敵に出血 +1。
    /// ターン末にまとめて付ける (1 回の付与 = 1 として〈血の帳簿〉にも数えられる)。</summary>
    public class DeathAbacus : IPassiveSkillEffect
    {
        public string SkillId => "死に際の算盤";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            for (int i = 0; i < ctx.hpLossEventsThisTurn; i++) ctx.AddEnemyBleed(1);
        }
    }

    /// <summary>開き直りの胸当て (SILVER) — 敵の予告攻撃が自分の現在 HP の半分以上のターン、 攻撃 +6。
    /// 予告は完全情報テレグラフで開示される値を使う。</summary>
    public class DesperateBreastplate : IPassiveSkillEffect
    {
        public const int AttackBonus = 6;
        public string SkillId => "開き直りの胸当て";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnPreDealDamage };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.finalDamage <= 0 || ctx.playerCurrentHP <= 0) return;
            if (ctx.telegraphedEnemyAttack * 2 < ctx.playerCurrentHP) return;
            ctx.finalDamage += AttackBonus;
        }
    }

    // ------------------------------------------------------------
    //  選んだ 3 つ (会心 / 毒 / 役)
    // ------------------------------------------------------------

    /// <summary>澄んだ音叉 (SILVER) — 会心したターンの次のターン、 1 回目のリロールが無料になる。
    /// 役〈中階〉と同じ <see cref="CombatSystem.YachtRoleEffects.FreeRerollKey"/> を使う (無料は 1 回目だけ・個数に上限は付けない)。</summary>
    public class ClearTuningFork : IPassiveSkillEffect
    {
        public string SkillId => "澄んだ音叉";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.playerCritThisTurn) ctx.nextTurnBuffs[CombatSystem.YachtRoleEffects.FreeRerollKey] = 1f;
        }
    }

    /// <summary>煮詰めた毒の膠 (SILVER) — 敵に毒が乗っている間、 シールドの獲得 +25%。
    /// 獲得は <see cref="CombatContext.GainShield"/> を通るので、 すべての獲得経路に掛かる。</summary>
    public class BoiledVenomGlue : IPassiveSkillEffect
    {
        public string SkillId => "煮詰めた毒の膠";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { ctx.shieldGainPoisonPct = 0.25f; }
    }

    /// <summary>賽の貯金箱 (SILVER) — 役を切ったターン、 充電 +2。 部分役を狙う刻みリロールの原資が戻る。</summary>
    public class DiceSavingsBox : IPassiveSkillEffect
    {
        public string SkillId => "賽の貯金箱";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (ctx.firedRolesThisTurn.Count > 0) ctx.AddCharge(2);
        }
    }

    // ------------------------------------------------------------
    //  輪 1: 毒の連鎖爆破 — 触媒の坩堝 × 脱皮する毒壺 (+ 石抜きの毒指輪)
    //    毒を倍にして 5 を超える → 毒殺 → 半分に戻る → また倍にして超える …
    // ------------------------------------------------------------

    /// <summary>触媒の坩堝 (LEGENDARY) — ターン終了時、 敵の毒を 2 倍にする (上限は毒の上限 10)。
    /// ターン**終了**時にしたのは、 次ターン開始の DOT と毒殺 (OnTurnStart) が倍になった後の値を見るため
    /// (同じ OnTurnStart に置くと発火順に結果が左右される)。 毒が増えた扱いなので〈毒の蓄電器〉にも数えられる。</summary>
    public class CatalystCrucible : IPassiveSkillEffect
    {
        public string SkillId => "触媒の坩堝";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            int added = ctx.MultiplyEnemyPoison(2);
            if (added > 0) UnityEngine.Debug.Log($"[触媒の坩堝] 毒 ×2 (+{added})");
        }
    }

    /// <summary>脱皮する毒壺 (GOLD) — 〈石抜きの毒指輪〉の毒殺を**何度でも**撃てるようにし、 撃つたびに毒を半分にする。
    /// 効果の本体は <see cref="AssassinToxin"/> が <see cref="CombatContext.assassinRearm"/> を読んで切り替える。
    /// 指輪を持っていなければ何も起きない (単体では地味な品)。</summary>
    public class MoltingVenomJar : IPassiveSkillEffect
    {
        public string SkillId => "脱皮する毒壺";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { ctx.assassinRearm = true; }
    }

    // ------------------------------------------------------------
    //  輪 2: 共鳴する炉心 — 臨界 × 会心 × 充電
    //    爆発 → 会心 (連鎖爆発なら確定) → 充電を払ってメーターを満たす → 次ターンも爆発 …
    // ------------------------------------------------------------

    /// <summary>共鳴する炉心 (LEGENDARY) — 臨界爆発を撃ったターンに会心したら、 充電を <see cref="ChargeCost"/> 払って
    /// 次ターンの爆発を再び予約する (メーターを満たす)。
    /// 起動条件を重くしてある: 爆発 + 会心 + 充電の 3 つが揃わないと回らない。 充電はリロールの原資と食い合うので、
    /// 回し続けるには充電ビルドが要る。 自然にメーターが閾値へ届いて予約済みの時は払わない。</summary>
    public class ResonantCore : IPassiveSkillEffect
    {
        public const int ChargeCost = 6;
        public string SkillId => "共鳴する炉心";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnTurnEnd };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx)
        {
            if (!ctx.rinkaiBurstedThisTurn || !ctx.playerCritThisTurn) return;
            if (ctx.rinkaiBurstActive) return;                 // 臨界メーターが自力で届いていれば払わない
            if (!ctx.ConsumeCharge(ChargeCost)) return;
            ctx.rinkaiBurstActive = true;
            UnityEngine.Debug.Log($"[共鳴する炉心] 爆発 + 会心 → 充電 -{ChargeCost} で次ターンの爆発を再予約");
        }
    }

    // ------------------------------------------------------------
    //  輪 3: 血の祝祭 — 会心 × 出血 (医家の反り刃 / 血日に澄む佩玉)
    //    会心 → 出血 +2 → 出血 3 以上で会心率 +5% → 会心 … の輪に、 会心した回の出血ダメージ 2 倍を足す
    // ------------------------------------------------------------

    /// <summary>血の祝祭 (GOLD) — 会心したターンは、 敵の出血ダメージをもう 1 回適用する。
    /// 適用は CombatManager.BleedTickDamage (新旧パイプラインの出血解決 4 か所を 1 つにまとめた関数)。</summary>
    public class BloodFeast : IPassiveSkillEffect
    {
        public string SkillId => "血の祝祭";
        public PassiveSkillTrigger[] Triggers => new[] { PassiveSkillTrigger.OnBattleStart };
        public void Execute(PassiveSkillTrigger t, CombatContext ctx) { ctx.bleedEchoOnCrit = true; }
    }
}
