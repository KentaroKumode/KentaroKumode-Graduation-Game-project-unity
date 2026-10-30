using System.Collections.Generic;
using GameLoop;
using ItemSystem.PassiveSkills;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>役の適用結果。 戦闘の解決地点 (CombatManager) が読むものだけをここに集約する。</summary>
    public struct RoleOutcome
    {
        /// <summary>〈極〉が発動した (踏みとどまりを貫通する印)。</summary>
        public bool yachtFired;
        /// <summary>〈極〉: 敵の最大HP の N% を軽減無視で削る。 面数 × 30% (ボス・ヴェスカは × 15%)。</summary>
        public float yachtChunkPct;
        /// <summary>〈追撃〉: 与えたダメージの何割を追加で与えるか (0.3 / 1.0)。 0 なら無し。</summary>
        public float extraDamageRatio;
        /// <summary>〈追撃〉ゾロ目: 追加分を<b>確定</b>ダメージ (軽減無視) にする。 false は通常ダメージに上乗せ。</summary>
        public bool extraDamageTrue;
        /// <summary>〈追撃〉T4: 追加ダメージが会心判定を持つ。</summary>
        public bool extraDamageCanCrit;
        /// <summary>〈守り〉: このターンの被ダメを何 % 減らすか (25 / 75)。</summary>
        public int damageTakenCutPct;
        /// <summary>〈守り〉T4: 軽減した分の半分をシールドにする。</summary>
        public bool guardToShield;
        /// <summary>〈癒し〉: 回復量。</summary>
        public int healAmount;
        /// <summary>〈癒し〉T4: HP 上限を超えた回復分をシールドにする。</summary>
        public bool healOverflowToShield;
        /// <summary>〈封じ〉ゾロ目: 敵に次ターン行動不能 (この行動で ctx.nextTurnBuffs に積む)。</summary>
        public bool enemyStunned;
    }

    /// <summary>
    /// ADR-0011: 成立した役の<b>効果を適用する</b>。 判定は <see cref="YachtRoles"/>。
    ///
    /// <para><b>既存フィールドへ加算する形に寄せてある</b> ── 新フィールドを増やすと
    /// CombatContext の 3 区分規約 (perTurn/decay/persistent) の管理対象が膨らむため。
    /// 次ターンへ持ち越す分は <c>nextTurnBuffs</c> 辞書へ載せる (BeginNewTurn が currentBuffs へ移す)。</para>
    ///
    /// <para>効果表の正本は docs/adr/0011-three-dice-matching-roles.md。 数値は叩き台 (バランス調整で見直す)。</para>
    /// </summary>
    public static class YachtRoleEffects
    {
        // ── 〈極〉(目 1) ──
        /// <summary>〈極〉(ゾロ目) の面数あたり割合 (最大HP 比・%)。 通常敵・エリート。</summary>
        public const float YachtPctPerFaceNormal = 30f;
        /// <summary>〈極〉(ゾロ目) の面数あたり割合。 ボス・ヴェスカの各形態。</summary>
        public const float YachtPctPerFaceBoss = 15f;
        /// <summary>T4 の 2 個そろい〈極〉の面数あたり割合 (通常)。 ボスは半分。</summary>
        public const float YachtPairPctPerFaceNormal = 5f;

        // ── 持ち越しキー (nextTurnBuffs → currentBuffs) ──
        /// <summary>〈特別〉2 個そろい: 次ターンのリロール N 回を無料にする (値 = 回数)。</summary>
        public const string FreeRerollKey = "roleFreeReroll";
        /// <summary>〈封じ〉ゾロ目: 次ターン、 敵は攻撃しない。</summary>
        public const string EnemyStunKey = "roleEnemyStun";
        /// <summary>〈封じ〉2 個そろい: 次ターンの敵の攻撃を N% 減らす (値 = N)。</summary>
        public const string EnemyAtkCutKey = "roleEnemyAtkCut";

        // ── 数値の叩き台 ──
        const int CutPairPct = 40;          // 封じ 2 個そろい
        const float AttackMulPair = 0.25f;  // 攻め 2 個そろい: 与ダメ +25%
        const float AttackMulTriple = 0.5f; // 攻め ゾロ目: ×1.5
        const int GuardPairPct = 25, GuardTriplePct = 75;
        const int ChargePair = 3;
        const int HealPair = 2, HealTriple = 6;
        const int GoldPairPct = 5, GoldTriplePct = 30;
        const int GoldAttackCap = 10;       // 富 T4: 所持ゴールドの 5% (最大 10) を攻撃 +N
        const int EscalationDelayPair = 2;  // 時間 2 個そろい: 次の段階を 2 ターン先へ
        const int FreeRerollPair = 2;       // 特別 2 個そろい

        /// <summary>役を適用する。 <paramref name="tier"/> はその目の面の Tier (T4 の固有効果の判定に使う)。</summary>
        public static void Apply(RoleKind role, DiceFaceParts.Tier tier, CombatContext ctx, ref RoleOutcome outcome)
        {
            if (ctx == null) return;
            // [計装] 与ダメ出どころ別。
            string dsKey = "役〈" + YachtRoles.NameOf(role) + "〉";
            DmgSourceDiag.Universal.Add(dsKey);
            var dsBefore = DmgSourceDiag.Take(ctx);
            ApplyCore(role, tier, ctx, ref outcome);
            DmgSourceDiag.Attribute(dsKey, dsBefore, ctx);
        }

        static void ApplyCore(RoleKind role, DiceFaceParts.Tier tier, CombatContext ctx, ref RoleOutcome outcome)
        {
            int face = YachtRoles.FaceOf(role);
            bool triple = YachtRoles.IsTriple(role);
            bool t4 = tier == DiceFaceParts.Tier.T4;

            switch (face)
            {
                case 1:   // 特別: 無料リロール / 〈極〉
                    if (!triple)
                    {
                        ctx.nextTurnBuffs[FreeRerollKey] = FreeRerollPair;
                        if (t4) FireYacht(ctx, ref outcome, YachtPairPctPerFaceNormal, YachtPairPctPerFaceNormal * 0.5f);
                    }
                    else FireYacht(ctx, ref outcome, YachtPctPerFaceNormal, YachtPctPerFaceBoss);
                    break;

                case 2:   // 封じ: 敵の次の攻撃 −40% / 次ターン行動しない
                    if (!triple) ctx.nextTurnBuffs[EnemyAtkCutKey] = CutPairPct;
                    else { ctx.nextTurnBuffs[EnemyStunKey] = 1f; outcome.enemyStunned = true; }
                    // T4: 封じた敵は、 攻撃だけでなくパッシブ・ターン効果も次のターン止まる。
                    //   enemyPassivesDisabledTurns は BeginNewTurn で 1 ずつ減るので、 2 で「このターンの残り + 次のターン」。
                    if (t4) ctx.enemyPassivesDisabledTurns = Mathf.Max(ctx.enemyPassivesDisabledTurns, 2);
                    break;

                case 3:   // 追撃: 与えたダメージの 30% を追加 / 100% を追加確定ダメージ
                    outcome.extraDamageRatio = triple ? 1.0f : 0.3f;
                    outcome.extraDamageTrue = triple;
                    outcome.extraDamageCanCrit = t4;
                    break;

                case 4:   // 攻め: 与ダメ +25% / ×1.5 ＋会心確定
                    ctx.outgoingDamageMultiplier += triple ? AttackMulTriple : AttackMulPair;
                    if (triple) ctx.forceCritical = true;
                    // T4: 敵の防御を無視する (ADR-0013: 防御値をすべて剥がす貫通)。
                    if (t4) ctx.playerArmorPen += DefenseFormula.IgnoreAll;
                    break;

                case 5:   // 守り: 被ダメ −25% / −75%
                    outcome.damageTakenCutPct = triple ? GuardTriplePct : GuardPairPct;
                    outcome.guardToShield = t4;
                    break;

                case 6:   // 時間: エスカレーションの次の段階を 2 ターン先へ / 段階を 1 つ戻す
                    if (!triple) AddEscalationDelay(ctx, EscalationDelayPair);
                    else AddEscalationDelay(ctx, TurnsToRetreatOneStage(ctx));
                    // T4: 時間を力に変える ── エスカレーションの現在の段階 × 2 を攻撃 +N に加算する。
                    if (t4) ctx.mutualAttackBonus += CurrentStage(ctx) * 2;
                    break;

                case 7:   // 充電: +3 / 上限まで満たす
                {
                    int cur = ctx.GetCharge();
                    int cap = CombatManager.ChargeMax + MetaProgression.MetaBuffApplicator.GetChargeCapBonus();
                    int gain = triple ? cap : ChargePair;
                    ctx.AddCharge(gain);
                    // T4: 上限を超えた分を、 このターンの攻撃 +N に変える。
                    if (t4)
                    {
                        int overflow = Mathf.Max(0, cur + gain - cap);
                        if (overflow > 0) ctx.mutualAttackBonus += overflow;
                    }
                    break;
                }

                case 8:   // 癒し: HP +2 / +6
                    outcome.healAmount += triple ? HealTriple : HealPair;
                    outcome.healOverflowToShield = t4;
                    break;

                case 9:   // 富: ゴールド報酬 +5% / +30%
                {
                    var run = GameManager.Instance?.Run;
                    if (run != null) run.roleGoldBonusPct += triple ? GoldTriplePct : GoldPairPct;
                    // T4 (2 個そろい): 所持ゴールドの 5% (最大 10) を攻撃 +N に加算。
                    if (t4 && !triple && run != null)
                        ctx.mutualAttackBonus += Mathf.Min(GoldAttackCap, Mathf.FloorToInt(run.coins * 0.05f));
                    break;
                }
            }
        }

        /// <summary>〈極〉: 敵の最大HP の (面数 × pct)% を軽減無視で削る。 ボスは <paramref name="bossPctPerFace"/>。
        /// 即勝利は持たない ── 面が多いほど出にくく、 出れば強い (出やすさに反比例)。</summary>
        static void FireYacht(CombatContext ctx, ref RoleOutcome outcome, float normalPctPerFace, float bossPctPerFace)
        {
            int faces = ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0
                ? ctx.equippedDiceFaces.Length : DiceFaceParts.StartingFaces.Length;
            bool isBoss = !string.IsNullOrEmpty(ctx.bossId);
            outcome.yachtFired = true;
            outcome.yachtChunkPct += faces * (isBoss ? bossPctPerFace : normalPctPerFace);
            YachtFiredCount++;
            YachtPctTotal += outcome.yachtChunkPct;
        }

        // ── エスカレーション操作 (時間) ──
        static int Delay(CombatContext ctx) => (int)ctx.GetAccumulated(CombatManager.CoolantDelayKey);

        /// <summary>エスカレーションの次の段階を turns ターン先へ送る。 役〈時間〉と〈貸与された時間〉が使う。</summary>
        public static void AddEscalationDelay(CombatContext ctx, int turns)
        {
            if (turns <= 0) return;
            ctx.accumulatedValues[CombatManager.CoolantDelayKey] = ctx.GetAccumulated(CombatManager.CoolantDelayKey) + turns;
        }

        /// <summary>現在 (このターン) のエスカレーションの段階。</summary>
        static int CurrentStage(CombatContext ctx)
            => Escalation.StageOf(Mathf.Max(1, ctx.currentTurn - Delay(ctx)));

        /// <summary>次のターンの段階を 1 つ戻すのに要る遅延の追加量。 既に段階 0 なら 0。</summary>
        static int TurnsToRetreatOneStage(CombatContext ctx)
        {
            int next = Mathf.Max(1, ctx.currentTurn + 1 - Delay(ctx));
            int stage = Escalation.StageOf(next);
            if (stage <= 0) return 0;
            int t = next;
            while (t > 1 && Escalation.StageOf(t) >= stage) t--;   // 1 つ前の段階の最後のターン
            return next - t;
        }

        // ============================================================
        //  計装 (役が死んでいないこと)
        // ============================================================

        /// <summary>役ごとの「成立した回数」。 index = (int)RoleKind。</summary>
        public static readonly long[] FormedCount = new long[YachtRoles.Count];
        /// <summary>役ごとの「実際に効いた回数」(役は自動発動なので成立と同数)。</summary>
        public static readonly long[] FiredCount = new long[YachtRoles.Count];
        /// <summary>役の判定が走ったターン数 (分母)。</summary>
        public static long TurnsEvaluated;

        /// <summary>[計装] 〈極〉の発動数と削り割合の合計。</summary>
        public static long YachtFiredCount;
        public static double YachtPctTotal;

        /// <summary>この戦闘で各役が発動した回数 (index = (int)RoleKind)。
        /// FinishCombat で装備ダイス別へ積んでから 0 に戻す中継。</summary>
        public static readonly int[] FiredThisCombat = new int[YachtRoles.Count];

        /// <summary>装備ダイス別・役別の発動数。 key = ダイス ID、 値 = 長さ <see cref="YachtRoles.Count"/> の配列。</summary>
        public static readonly Dictionary<string, long[]> ByDice = new Dictionary<string, long[]>();
        /// <summary>装備ダイス別の戦闘数 (発動率の分母)。</summary>
        public static readonly Dictionary<string, long> CombatsByDice = new Dictionary<string, long>();

        /// <summary>戦闘終了時に、 この戦闘の役発動を装備ダイス別へ積む。</summary>
        public static void FlushByDice(string diceId)
        {
            string key = string.IsNullOrEmpty(diceId) ? "(none)" : diceId;
            CombatsByDice.TryGetValue(key, out long c);
            CombatsByDice[key] = c + 1;

            if (!ByDice.TryGetValue(key, out var arr)) { arr = new long[YachtRoles.Count]; ByDice[key] = arr; }
            for (int i = 0; i < YachtRoles.Count; i++)
            {
                if (FiredThisCombat[i] > 0) arr[i] += FiredThisCombat[i];
                FiredThisCombat[i] = 0;
            }
        }

        public static void ResetStats()
        {
            System.Array.Clear(FormedCount, 0, FormedCount.Length);
            System.Array.Clear(FiredCount, 0, FiredCount.Length);
            ByDice.Clear();
            CombatsByDice.Clear();
            System.Array.Clear(FiredThisCombat, 0, FiredThisCombat.Length);
            YachtFiredCount = 0; YachtPctTotal = 0;
            TurnsEvaluated = 0;
        }

        /// <summary>〈極〉の発動数と平均削り割合。</summary>
        public static string DescribeYachtFaces()
        {
            if (YachtFiredCount <= 0) return "";
            return $"【〈極〉】発動 {YachtFiredCount:N0} / 平均削り {YachtPctTotal / YachtFiredCount:F1}%\n";
        }

        /// <summary>ダイス別・役別の 1 戦あたり発動回数。</summary>
        public static string DescribeByDice(int runs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【ダイス別 役の発動 (1戦あたり)】");
            sb.Append("  ダイス".PadRight(22)).Append("戦闘数".PadLeft(8));
            for (int i = 0; i < YachtRoles.Count; i++) sb.Append(YachtRoles.NameOf((RoleKind)i).PadLeft(8));
            sb.AppendLine();

            var keys = new List<string>(CombatsByDice.Keys);
            keys.Sort((a, b) => CombatsByDice[b].CompareTo(CombatsByDice[a]));
            foreach (var k in keys)
            {
                long c = CombatsByDice[k];
                ByDice.TryGetValue(k, out var arr);
                sb.Append(("  " + k).PadRight(22)).Append(c.ToString().PadLeft(8));
                for (int i = 0; i < YachtRoles.Count; i++)
                {
                    double v = (arr != null && c > 0) ? arr[i] / (double)c : 0;
                    sb.Append(v.ToString("F3").PadLeft(8));
                }
                sb.AppendLine();
            }
            sb.AppendLine($"  ラン {runs}");
            return sb.ToString();
        }

        /// <summary>「役名 成立率% 発動率%」を 1 行ずつ。 実測 0% の役が無いかを見るため。</summary>
        public static string DescribeStats()
        {
            if (TurnsEvaluated == 0) return "役の判定なし";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"役の成立/発動 (判定 {TurnsEvaluated} ターン)");
            sb.AppendLine("役          | 成立率 | 発動率 | 成立数 | 発動数");
            foreach (var k in YachtRoles.All)
            {
                int i = (int)k;
                sb.AppendLine($"{YachtRoles.NameOf(k),-10} | "
                            + $"{100.0 * FormedCount[i] / TurnsEvaluated,5:F1}% | "
                            + $"{100.0 * FiredCount[i] / TurnsEvaluated,5:F1}% | "
                            + $"{FormedCount[i],6} | {FiredCount[i],6}");
            }
            return sb.ToString();
        }
    }
}
