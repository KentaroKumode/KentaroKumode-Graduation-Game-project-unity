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
        /// <summary>〈追撃〉: 与えたダメージの何割を追加で与えるか (0.5 / 1.5)。 0 なら無し。</summary>
        public float extraDamageRatio;
        /// <summary>〈追撃〉ゾロ目: 追加分を<b>確定</b>ダメージ (軽減無視) にする。 false は通常ダメージに上乗せ。</summary>
        public bool extraDamageTrue;
        /// <summary>追加ダメージが会心判定を持つ (旧〈追撃〉T4・2026-11-18 から立てる役は無い)。</summary>
        public bool extraDamageCanCrit;
        /// <summary>〈守り〉: このターンの被ダメを何 % 減らすか (40 / 100)。</summary>
        public int damageTakenCutPct;
        /// <summary>軽減した分の半分をシールドにする (旧〈守り〉T4・2026-11-18 から立てる役は無い)。</summary>
        public bool guardToShield;
        /// <summary>〈癒し〉: 回復量。</summary>
        public int healAmount;
        /// <summary>HP 上限を超えた回復分をシールドにする (旧〈癒し〉T4・2026-11-18 から立てる役は無い)。</summary>
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
    /// <para>効果の正本はこのファイル (実コード)。 一覧は DAMAGE_CALC_REFERENCE.md の「役」の段に写す (数値を変えたら同時に直す)。
    /// ADR-0011 の表は決定当時の叩き台の記録。 数値は叩き台 (バランス調整で見直す)。</para>
    /// </summary>
    public static class YachtRoleEffects
    {
        // ── 〈極〉(目 1) ──
        /// <summary>〈極〉(ゾロ目) の面数あたり割合 (最大HP 比・%)。 通常敵・エリート。</summary>
        public const float YachtPctPerFaceNormal = 30f;
        /// <summary>〈極〉(ゾロ目) の面数あたり割合。 ボス・ヴェスカの各形態。</summary>
        public const float YachtPctPerFaceBoss = 15f;
        /// <summary>〈特別〉2 個そろいの小さな〈極〉の面数あたり割合 (通常)。 ボスは半分。 旧 T4 の固有効果を本体へ移した (ADR-0015)。</summary>
        public const float YachtPairPctPerFaceNormal = 5f;

        // ── 持ち越しキー (nextTurnBuffs → currentBuffs) ──
        /// <summary>〈特別〉2 個そろい: 次ターンのリロール N 回を無料にする (値 = 回数)。</summary>
        public const string FreeRerollKey = "roleFreeReroll";
        /// <summary>〈封じ〉ゾロ目: 次ターン、 敵は攻撃しない。</summary>
        public const string EnemyStunKey = "roleEnemyStun";
        /// <summary>〈封じ〉2 個そろい: 次ターンの敵の攻撃を N% 減らす (値 = N)。</summary>
        public const string EnemyAtkCutKey = "roleEnemyAtkCut";
        /// <summary>役の軸の品 (〈負け越しの賽〉〈雷毒の導管〉〈鏡合わせの盤〉): 次のターンのブロック +N (値 = N・BeginNewTurn が品のブロック +N へ入れる)。</summary>
        public const string BlockNextKey = "roleBlockNext";

        // ── 数値 (ADR-0015・作者 2026-11-18。 小さい目ほど役が強く、 大きい目ほど数字が強い。 仮の値) ──
        const int CutPairPct = 40;          // 封じ 2 個そろい
        const float AttackMulPair = 0.40f;  // 攻め 2 個そろい: 与ダメ +40% (旧 +25%)
        const float AttackMulTriple = 1.0f; // 攻め ゾロ目: ×2.0 (旧 ×1.5)
        const float PursuitPair = 0.5f;     // 追撃 2 個そろい: 与えたダメージの 50% (旧 30%)
        const float PursuitTriple = 1.5f;   // 追撃 ゾロ目: 150% を確定ダメージ (旧 100%)
        const int GuardPairPct = 40, GuardTriplePct = 100;   // 守り (旧 25 / 75)。 ゾロ目はそのターンの敵の攻撃を受けない
        const int ChargePair = 3;
        const int HealPair = 2, HealTriple = 6;
        const int GoldPairPct = 5, GoldTriplePct = 30;
        const int EscalationDelayPair = 2;  // 時間 2 個そろい: 次の段階を 2 ターン先へ
        const int FreeRerollPair = 2;       // 特別 2 個そろい
        /// <summary>封じ: 敵のパッシブ・ターン効果を止めるターン (BeginNewTurn で 1 ずつ減る → 2 で「このターンの残り + 次のターン」)。 旧 T4 を本体へ移した。</summary>
        const int SealPassiveTurns = 2;

        /// <summary>役を適用する。 <paramref name="tier"/> は使わない (ADR-0015 で Tier をやめた・呼び手の互換のため残す)。</summary>
        public static void Apply(RoleKind role, DiceFaceParts.Tier tier, CombatContext ctx, ref RoleOutcome outcome)
        {
            if (ctx == null) return;
            // [計装] 与ダメ出どころ別。
            string dsKey = "役〈" + YachtRoles.NameOf(role) + "〉";
            DmgSourceDiag.Universal.Add(dsKey);
            var dsBefore = DmgSourceDiag.Take(ctx);
            ApplyCore(role, ctx, ref outcome);
            DmgSourceDiag.Attribute(dsKey, dsBefore, ctx);
        }

        static void ApplyCore(RoleKind role, CombatContext ctx, ref RoleOutcome outcome)
        {
            if (role == RoleKind.Straight)
            {
                // 連なり (2026-11-20): そのターン、 挿した端子がすべて倍化する。 効果は端子の解決 (TerminalCards.Resolve) が持つので、 ここでは何もしない
                //   (旧: 次のターンの振り直し 1 回無料・手札 +1 枚)
                return;
            }

            int face = YachtRoles.FaceOf(role);
            bool triple = YachtRoles.IsTriple(role);

            switch (face)
            {
                case 1:   // 特別: 無料の振り直し 2 回 + 小さな〈極〉(面数 × 5%) / 〈極〉
                    if (!triple)
                    {
                        AddNextTurn(ctx, FreeRerollKey, FreeRerollPair);   // 足し合わせる (〈鏡合わせの盤〉〈揃いの拍子木〉の +1 と重なる・2026-11-21)
                        FireYacht(ctx, ref outcome, YachtPairPctPerFaceNormal, YachtPairPctPerFaceNormal * 0.5f);
                    }
                    else FireYacht(ctx, ref outcome, YachtPctPerFaceNormal, YachtPctPerFaceBoss);
                    break;

                case 2:   // 封じ: 敵の次の攻撃 −40% / 次ターン行動しない。 どちらも敵のパッシブ・ターン効果を次のターンまで止める
                    if (!triple) ctx.nextTurnBuffs[EnemyAtkCutKey] = CutPairPct;
                    else { ctx.nextTurnBuffs[EnemyStunKey] = 1f; outcome.enemyStunned = true; }
                    ctx.enemyPassivesDisabledTurns = Mathf.Max(ctx.enemyPassivesDisabledTurns, SealPassiveTurns);
                    break;

                case 3:   // 追撃: 与えたダメージの 50% を追加 / 150% を追加確定ダメージ
                    //   同じターンに 2 回働けば足し合わせる (〈万象のゾロ目〉〈鏡合わせの盤〉〈宿命の刻み目〉・2026-11-21)。 ゾロ目が 1 回でも働けば確定ダメージ
                    outcome.extraDamageRatio += triple ? PursuitTriple : PursuitPair;
                    outcome.extraDamageTrue |= triple;
                    break;

                case 4:   // 攻め: 与ダメ +40% / ×2.0 ＋会心確定
                    ctx.outgoingDamageMultiplier += triple ? AttackMulTriple : AttackMulPair;
                    if (triple) ctx.forceCritical = true;
                    break;

                case 5:   // 守り: 被ダメ −40% / −100%。 同じターンに 2 回働けば掛け合わせる (−40% を 2 回で −64%・2026-11-21)
                    outcome.damageTakenCutPct = 100 - (100 - outcome.damageTakenCutPct) * (100 - (triple ? GuardTriplePct : GuardPairPct)) / 100;
                    break;

                case 6:   // 時間: エスカレーションの次の段階を 2 ターン先へ / 段階を 1 つ戻す
                    if (!triple) AddEscalationDelay(ctx, EscalationDelayPair);
                    else AddEscalationDelay(ctx, TurnsToRetreatOneStage(ctx));
                    break;

                case 7:   // 充電: +3 / 上限まで満たす
                {
                    int cap = CombatManager.ChargeMax + MetaProgression.MetaBuffApplicator.GetChargeCapBonus();
                    ctx.AddCharge(triple ? cap : ChargePair);
                    break;
                }

                case 8:   // 癒し: HP +2 / +6 (古銭の賽は +3)
                    outcome.healAmount += (triple ? HealTriple : HealPair) + DiePassives.RoleHealBonus(GameManager.Instance?.Run);
                    break;

                case 9:   // 富: ゴールド報酬 +5% / +30% (9 の面を持つ賽は今は無い)
                {
                    var run = GameManager.Instance?.Run;
                    if (run != null) run.roleGoldBonusPct += triple ? GoldTriplePct : GoldPairPct;
                    break;
                }
            }
        }

        /// <summary>次のターンへ持ち越す値に足す (nextTurnBuffs・無ければ 0 から)。</summary>
        public static void AddNextTurn(CombatContext ctx, string key, float add)
            => ctx.nextTurnBuffs[key] = (ctx.nextTurnBuffs.TryGetValue(key, out float v) ? v : 0f) + add;

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
        public static int TurnsToRetreatOneStage(CombatContext ctx)
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
