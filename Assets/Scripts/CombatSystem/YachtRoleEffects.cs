using System.Collections.Generic;
using InventorySystem.PassiveSkills;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>役の適用結果。 端子合計への加算は ref で返せないのでここに集約する。</summary>
    public struct RoleOutcome
    {
        public int attackSumDelta;      // 攻撃端子の合計へ加算
        public int blockSumDelta;       // ブロック端子の合計へ加算
        public bool instantWin;         // 〈極〉が発動した (踏みとどまりを貫通する印)
        public int yachtChunkPct;       // 〈極〉: 敵の最大HP の N% を軽減無視で削る (出目依存)
        // [削除 2026-08-22] 〈過不足なし〉の確定キルは〈拮抗〉へのリワークで廃止。
        //   新しい効果は ctx.outgoingDamageMultiplier への加算なので RoleOutcome を経由しない。
        public bool damageZero;         // 〈大階〉〈相殺〉: このターンの被ダメ 0
        public bool damageHalf;         // 〈束〉ブロック: 被ダメ半減
        public bool enemyStunned;       // 〈大束〉: 敵に次ターン行動不能
        public bool freeRerollNextTurn; // 〈中階〉: 次ターンのリロール 1 回無料
    }

    /// <summary>
    /// ADR-0010: 成立した役の**効果を適用する**。 判定は <see cref="YachtRoles"/> が行い、
    /// ここは「切ると決まった役」を受け取って ctx へ書き込むだけ。
    ///
    /// **既存フィールドへ加算する形に寄せてある** ── 新フィールドを増やすと
    /// CombatContext の 3 区分規約 (perTurn/decay/persistent) の管理対象が膨らむため。
    /// 効果表の正本は docs/adr/0010-yacht-roles.md 柱4。
    /// </summary>
    public static class YachtRoleEffects
    {
        /// <summary>〈極〉の削り割合 = **出目合計 × これ** (最大HP比・%)。
        ///
        /// <para><b>2026-08-22 に出目依存化。</b> 旧仕様は「通常敵は即勝利 / ボスは一律 40%」で、
        /// <b>出目の大小を完全に無視していた</b> ── 1,1,1,1,1 と 6,6,6,6,6 が同価値。
        /// そのため BOT が期待値を捨てて低い目で揃えに行き、 発動が設計想定 0.12回/ラン に対し
        /// <b>実測 2.49回/ラン (約21倍)</b> になっていた。</para>
        ///
        /// <para><b>フラットな固定ダメージにはしない。</b> 敵HP は 201〜4,418 と 22 倍の幅があり、
        /// 層が進むほど武器倍率も乗る。 固定値だと 1層で過剰・7層で無意味になる。
        /// 最大HP 比なら層に依存しない。</para>
        ///
        /// <para>出目合計は 5〜30 (平均 17.5)。 通常敵 ×4 なら 20%/70%/<b>120%=確殺</b>、
        /// ボス ×2 なら 10%/35%/60% ── <b>6 揃いは通常敵を確実に落とし、 1 揃いはただの削り</b>。
        /// ボスの平均 35% は旧仕様の一律 40% とほぼ同じで、 平均据え置きのまま取引だけが増える。</para></summary>
        public const int YachtPctPerPipNormal = 4;
        /// <summary><see cref="YachtPctPerPipNormal"/> のボス版。 層ボス・7層の各形態に適用。</summary>
        public const int YachtPctPerPipBoss = 2;

        /// <summary>〈束〉(ブロック側) の被ダメ倍率。</summary>
        public const float BlockTripleDamageMultiplier = 0.5f;

        // 次ターンへ持ち越す役の効果は **nextTurnBuffs 辞書**へ載せる。
        //   CombatContext の 3 区分規約 (perTurn/decay/persistent) に「set 1 箇所・read 1 箇所の
        //   一時値はフィールドを増やさず辞書へ」とあるため。 BeginNewTurn が currentBuffs へ移す。
        /// <summary>〈中階〉: 次ターンのリロール 1 回目を無料にする。</summary>
        public const string FreeRerollKey = "roleFreeReroll";
        /// <summary>〈大束〉: 次ターン、 敵は攻撃しない。</summary>
        public const string EnemyStunKey = "roleEnemyStun";

        /// <summary>役を切って効果を適用する。 <paramref name="terminal"/> は端子役のときだけ意味を持つ
        /// (Attack / Block)。 手札役・配線役では無視される。</summary>
        public static void Apply(RoleKind role, DiceTerminal terminal, CombatContext ctx,
                                 int[] group, int attackSum, int blockSum, int enemyAttackValue,
                                 ref RoleOutcome outcome)
        {
            if (ctx == null) return;
            // [計装] 与ダメ出どころ別。 役は全攻撃で「所持」扱い (切れる機会は毎ターンある)。
            string dsKey = "役〈" + YachtRoles.NameOf(role) + "〉";
            DmgSourceDiag.Universal.Add(dsKey);
            var dsBefore = DmgSourceDiag.Take(ctx);
            ApplyCore(role, terminal, ctx, group, attackSum, blockSum, enemyAttackValue, ref outcome);
            DmgSourceDiag.Attribute(dsKey, dsBefore, ctx);
        }

        static void ApplyCore(RoleKind role, DiceTerminal terminal, CombatContext ctx,
                              int[] group, int attackSum, int blockSum, int enemyAttackValue,
                              ref RoleOutcome outcome)
        {
            bool atk = terminal == DiceTerminal.Attack;
            int n = group?.Length ?? 0;

            switch (role)
            {
                // ── 端子役 ──
                case RoleKind.AllDifferent:                       // 散: 合計 +(本数×2)
                    if (atk) outcome.attackSumDelta += n * 2; else outcome.blockSumDelta += n * 2;
                    break;

                case RoleKind.Pair:                               // 対: ペアの出目合計の半分
                {
                    int bonus = PairBonus(group);
                    if (atk) outcome.attackSumDelta += bonus; else outcome.blockSumDelta += bonus;
                    break;
                }

                case RoleKind.AllEven:                            // 偶: 攻=合計×1.3 / 防=余剰の半分をシールド化
                    if (atk)
                    {
                        int sum = Sum(group);
                        outcome.attackSumDelta += Mathf.RoundToInt(sum * 0.3f);
                    }
                    else
                    {
                        int excess = Mathf.Max(0, blockSum - enemyAttackValue);
                        int shield = excess / 2;
                        if (shield > 0) { ctx.consShield += shield; ctx.shieldGainedTotal += shield; ShieldDiag.Note("役〈偶〉", shield); }
                    }
                    break;

                case RoleKind.AllOdd:                             // 奇: 攻=会心率+25% / 防=反射20%
                    if (atk) ctx.critRatePctAdd += 0.25f;
                    else ctx.vescaShieldReflectRate = Mathf.Max(ctx.vescaShieldReflectRate, 0.20f);
                    break;

                case RoleKind.SmallRun:                           // 小階: 攻=貫通+30% / 防=敵の次T攻撃-3
                    if (atk) ctx.armorPenPct += 0.30f;
                    else ctx.mutualEnemyAttackReduction += 3;
                    break;

                case RoleKind.Triple:                             // 束: 攻=会心確定 / 防=被ダメ半減
                    if (atk) ctx.forceCritical = true;
                    else outcome.damageHalf = true;
                    break;

                case RoleKind.SkipRun:                            // 飛階: 攻=軽減無視の固定ダメ+(本数×3) / 防=反射30%
                    if (atk) ctx.fixedDamageToEnemy += n * 3;
                    else ctx.vescaShieldReflectRate = Mathf.Max(ctx.vescaShieldReflectRate, 0.30f);
                    break;

                // ── 手札役 (盤面全体) ──
                case RoleKind.TwoPair:   ctx.AddCharge(8);  break;   // 二対
                case RoleKind.MediumRun:                             // 中階
                    ctx.AddCharge(12);
                    outcome.freeRerollNextTurn = true;
                    break;
                case RoleKind.FullHouse:                             // 満: 手札合計の半分を各端子へ
                {
                    // 旧仕様は一律 +10。 手札合計は 7 (1,1,1,2,2) 〜 28 (6,6,6,5,5) と幅が広く、
                    //   ここを出目依存にすると「低い満で切るか高い満を待つか」の取引が生まれる。
                    //   平均 17.5 → +9 で、 旧 +10 とほぼ同じ。
                    int bonus = (Sum(group) + 1) / 2;
                    if (attackSum > 0) outcome.attackSumDelta += bonus;
                    if (blockSum  > 0) outcome.blockSumDelta  += bonus;
                    break;
                }
                case RoleKind.LargeRun:  outcome.damageZero = true;  break;   // 大階
                case RoleKind.Quad:      outcome.enemyStunned = true; break;   // 大束
                case RoleKind.Yacht:                                          // 極
                {
                    // 5 個同値なので出目合計は 5〜30。 **通常敵とボスで係数が違う**
                    //   ── 6 揃いは通常敵を確実に落とし (120%)、 ボスには 60% に留まる。
                    int sum = Sum(group);
                    bool isBoss = !string.IsNullOrEmpty(ctx.bossId);
                    outcome.instantWin = true;
                    outcome.yachtChunkPct = sum * (isBoss ? YachtPctPerPipBoss : YachtPctPerPipNormal);
                    // [計装] **どの目で揃えたか。** 出目依存化の狙いは「追う価値が出目に比例する」
                    //   ことなので、 発動回数ではなく**平均の面**が上がったかで判定する。
                    int n5 = group != null && group.Length > 0 ? group.Length : 5;
                    YachtFiredCount++;
                    YachtSumTotal += sum;
                    YachtPctTotal += outcome.yachtChunkPct;
                    int face = System.Math.Max(1, System.Math.Min(15, sum / n5));
                    YachtByFace[face]++;
                    break;
                }

                // ── 配線役 (盤面全体) ──
                case RoleKind.Balance:                                        // 均: 両端子 +5
                    outcome.attackSumDelta += 5;
                    outcome.blockSumDelta  += 5;
                    break;
                case RoleKind.Offset:                                         // 相殺
                    outcome.damageZero = true;
                    ctx.AddCharge(10);
                    break;
                case RoleKind.MatchedStrike:                                  // 拮抗: 与ダメ +50%
                    // **相手の攻撃値ちょうどに合わせて殴り返す**。 攻撃合計 = 敵攻撃値 で成立。
                    //   `ResolveRoles` は ProcessDamage より前に走るので、 このターンの
                    //   ダメージに乗る (既存の慣用: 瞬間研磨剤が `+= 1.5` する枠と同じ)。
                    //   1.0 基準の倍率で BeginNewTurn がリセットする。
                    ctx.outgoingDamageMultiplier += 0.5f;
                    break;
            }
        }

        /// <summary>〈対〉の底上げ量 = **ペアを構成したダイスの出目合計 ÷ 2 (切り上げ)**。
        /// 3 個同値なら 3 本すべてが「ペア」に参加しているとみなす (対と束が両立するため)。
        ///
        /// <para><b>2026-08-22 に出目依存化。</b> 旧仕様は「参加本数 × 2」で、
        /// 1 のペアも 6 のペアも一律 +4 ＝ <b>出目の大小を無視していた</b>。
        /// いまは 1,1 → +1 / 3,3〜4,4 → +4 / 6,6 → +6 で、 平均は旧仕様と同じまま
        /// 「低い対で切るか高い対を待つか」の取引が生まれる。</para></summary>
        private static int PairBonus(int[] group)
        {
            if (group == null) return 0;
            int sum = 0;
            for (int i = 0; i < group.Length; i++)
            {
                int c = 0;
                for (int j = 0; j < group.Length; j++) if (group[j] == group[i]) c++;
                if (c >= 2) sum += group[i];
            }
            return (sum + 1) / 2;
        }

        private static int Sum(int[] v)
        {
            if (v == null) return 0;
            int s = 0; for (int i = 0; i < v.Length; i++) s += v[i];
            return s;
        }

        // ============================================================
        //  計装 (ADR-0010 Verification ④: 役が死んでいないこと)
        // ============================================================

        /// <summary>役ごとの「成立した回数」。 index = (int)RoleKind。</summary>
        public static readonly long[] FormedCount = new long[16];
        /// <summary>役ごとの「実際に切った回数」。 index = (int)RoleKind。</summary>
        public static readonly long[] FiredCount = new long[16];
        /// <summary>役の判定が走ったターン数 (分母)。</summary>
        public static long TurnsEvaluated;

        /// <summary>[計装] 〈極〉を**どの目で揃えたか**。 出目依存化 (2026-08-22) の狙いは
        /// 「揃えに行く価値が出目に比例する」ことなので、 **発動回数ではなく平均の面**で判定する。
        /// 旧仕様では 1 揃いも 6 揃いも即勝利だったため、 分布は一様に近いはず。</summary>
        public static long YachtFiredCount, YachtSumTotal, YachtPctTotal;
        /// <summary>面ごとの〈極〉発動数 (index = 面の値)。</summary>
        public static readonly long[] YachtByFace = new long[16];

        public static string DescribeYachtFaces()
        {
            if (YachtFiredCount <= 0) return "";
            var sb = new System.Text.StringBuilder();
            double n = YachtFiredCount;
            sb.AppendLine($"【〈極〉の面分布】発動 {YachtFiredCount:N0}"
                        + $" / 平均出目合計 {YachtSumTotal / n:F1} (5〜30)"
                        + $" / 平均削り {YachtPctTotal / n:F1}%");
            sb.Append("  面別:");
            for (int f = 1; f < YachtByFace.Length; f++)
                if (YachtByFace[f] > 0)
                    sb.Append($" {f}={100.0 * YachtByFace[f] / n:F1}%");
            sb.AppendLine();
            sb.AppendLine("  ※出目依存化が効いていれば高い面へ偏るはず。 一様なら追い方が変わっていない。");
            return sb.ToString();
        }

        /// <summary>[計装] 〈極〉の装備ダイス別 発動数。 ADR-0010 は 1ラン 0.12 回を想定しているが
        /// 実測は 8.7 回 (72 倍)。 柱5 の法則「大束・極は面の重複に超線形で反応する」
        /// (面の種類が 4 以下だと大束 6〜10%) が効いているなら、
        /// 5 面連番 (拾/磐) やパリティ特化 (奇/偶) に偏るはず。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, long> YachtByDice
            = new System.Collections.Generic.Dictionary<string, long>();

        /// <summary>この戦闘で各役が発動した回数 (index = (int)RoleKind)。
        /// FinishCombat で装備ダイス別へ積んでから 0 に戻す中継。
        /// **役の発動地点でシングルトンを引かないため**にこの形にしている
        /// （直接 GameManager.Instance を引く実装は 1 ラン 8.7 回 × ラン数だけ解決が走る）。</summary>
        public static readonly int[] FiredThisCombat = new int[16];

        // ============================================================
        //  発動タイミング (温存に余地があるか)
        // ============================================================
        //  `SuperCombatAI.WouldFire` は 16 役中 11 役が「成立した瞬間に必ず切る」で、
        //  温存の判断を持っていない。 **温存に価値があるのかを数える。**
        //
        //  **単位は「戦闘」ではなく「役エポック」** ── `usedRoles` がクリアされない期間。
        //  現行ルールでは 7層の 4 連戦は形態ごとにクリアされるので 1 形態 = 1 エポック、
        //  <see cref="CombatManager"/> の連戦持ち越し規則を立てると連戦全体が 1 エポックになる。
        //  戦闘単位で数えると、 前者では p2〜p4 の切り直しを取りこぼし、
        //  後者では地平の長さを 4 倍に見誤る。 **どちらのルールでも正しく数えるための単位。**
        //
        //  平均だけでは判断を誤る (2026-08-22 に実際に誤った)。 **エポック長で層別する。**

        private static readonly int[] _fireTurn = new int[16];    // このエポックで切ったターン (0 = 未発動)
        private static readonly int[] _fireAtk = new int[16];     // その時の敵攻撃値
        private static readonly int[] _postForms = new int[16];   // 発動後に再成立した回数
        private static readonly int[] _postMaxAtk = new int[16];  // 発動後に観測した最大敵攻撃
        private static readonly bool[] _holdBetter = new bool[16];// 発動後に「より重いターンで成立」したか
        private static int _epochStart, _epochLast, _epochMaxAtk;

        /// <summary>エポック長の層。 0 = 短 (≤8T) / 1 = 中 (9-16T) / 2 = 長 (17T+)。</summary>
        private static int BucketOf(int len) => len <= 8 ? 0 : (len <= 16 ? 1 : 2);
        private static readonly string[] BucketName = { "短 ≤8T", "中 9-16T", "長 17T+" };

        private static long[][] New3x16()
        {
            var a = new long[3][];
            for (int b = 0; b < 3; b++) a[b] = new long[16];
            return a;
        }
        private static readonly long[][] AccFired = New3x16();       // 発動したエポック数
        private static readonly long[][] AccFireTurnRel = New3x16(); // エポック内での発動位置
        private static readonly long[][] AccEpochLen = New3x16();
        private static readonly long[][] AccFireAtk = New3x16();
        private static readonly long[][] AccPostMaxAtk = New3x16();  // 発動後の最大敵攻撃
        private static readonly long[][] AccPostForms = New3x16();   // 発動後の再成立回数
        private static readonly long[][] AccHoldBetter = New3x16();  // **見送っていれば確実に得だった件数**
        private static readonly long[] AccEpochs = new long[3];      // 層ごとのエポック数

        /// <summary>役判定のたびに呼ぶ。 エポックの範囲と最大敵攻撃を更新する。</summary>
        public static void NoteTurn(int turn, int enemyAtk)
        {
            if (_epochStart == 0) _epochStart = turn;
            if (turn > _epochLast) _epochLast = turn;
            if (enemyAtk > _epochMaxAtk) _epochMaxAtk = enemyAtk;
            for (int i = 0; i < 16; i++)
                if (_fireTurn[i] > 0 && turn > _fireTurn[i] && enemyAtk > _postMaxAtk[i])
                    _postMaxAtk[i] = enemyAtk;
        }

        /// <summary>役が成立するたびに呼ぶ (使用済みでも呼ぶ)。
        /// **発動後の再成立を数えるのが目的** ── 「見送っても後で取り返せたか」の直接測定。
        /// 二項分布の概算では答えが出ない (2026-08-22 に概算で誤った)。</summary>
        public static void NoteFormed(RoleKind k, int turn, int enemyAtk)
        {
            int i = (int)k;
            if (_fireTurn[i] <= 0 || turn <= _fireTurn[i]) return;
            _postForms[i]++;
            if (enemyAtk > _fireAtk[i]) _holdBetter[i] = true;
        }

        /// <summary>役を切った地点を控える。 エポック長が確定するのは
        /// <see cref="FlushRoleEpoch"/> なので、 ここでは記録だけする。</summary>
        public static void NoteFireTiming(RoleKind k, int turn, int enemyAtk)
        {
            int i = (int)k;
            if (_fireTurn[i] != 0) return;   // 1 エポック 1 回
            _fireTurn[i] = turn;
            _fireAtk[i] = enemyAtk;
            _postMaxAtk[i] = enemyAtk;
        }

        /// <summary>役エポックを締める。 **`usedRoles` を実際にクリアする地点と、 戦闘終了時**の
        /// 両方から呼ぶ。 クリアしないルールなら前者は呼ばれず、 エポックが伸びる。</summary>
        public static void FlushRoleEpoch()
        {
            if (_epochStart == 0) { ResetEpoch(); return; }
            int len = System.Math.Max(1, _epochLast - _epochStart + 1);
            int b = BucketOf(len);
            AccEpochs[b]++;
            for (int i = 0; i < 16; i++)
            {
                if (_fireTurn[i] > 0)
                {
                    AccFired[b][i]++;
                    AccFireTurnRel[b][i] += _fireTurn[i] - _epochStart + 1;
                    AccEpochLen[b][i] += len;
                    AccFireAtk[b][i] += _fireAtk[i];
                    AccPostMaxAtk[b][i] += _postMaxAtk[i];
                    AccPostForms[b][i] += _postForms[i];
                    if (_holdBetter[i]) AccHoldBetter[b][i]++;
                }
            }
            ResetEpoch();
        }

        private static void ResetEpoch()
        {
            System.Array.Clear(_fireTurn, 0, 16);
            System.Array.Clear(_fireAtk, 0, 16);
            System.Array.Clear(_postForms, 0, 16);
            System.Array.Clear(_postMaxAtk, 0, 16);
            System.Array.Clear(_holdBetter, 0, 16);
            _epochStart = _epochLast = _epochMaxAtk = 0;
        }

        /// <summary>装備ダイス別・役別の発動数。 key = ダイス ID、 値 = 長さ 16 の配列。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, long[]> ByDice
            = new System.Collections.Generic.Dictionary<string, long[]>();
        /// <summary>装備ダイス別の戦闘数 (発動率の分母)。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, long> CombatsByDice
            = new System.Collections.Generic.Dictionary<string, long>();

        /// <summary>戦闘終了時に、 この戦闘の役発動を装備ダイス別へ積む。</summary>
        public static void FlushByDice(string diceId)
        {
            string key = string.IsNullOrEmpty(diceId) ? "(none)" : diceId;
            CombatsByDice.TryGetValue(key, out long c);
            CombatsByDice[key] = c + 1;

            if (!ByDice.TryGetValue(key, out var arr)) { arr = new long[16]; ByDice[key] = arr; }
            for (int i = 0; i < 16; i++)
            {
                if (FiredThisCombat[i] > 0) arr[i] += FiredThisCombat[i];
                FiredThisCombat[i] = 0;
            }
            // 戦闘が終わればエポックも必ず終わる。 形態交代側からも呼ばれる。
            FlushRoleEpoch();
        }

        /// <summary>役をいつ切っているか。 **温存に価値があるかを数値で出す。**
        ///
        /// エスカレーションで後のターンほど敵攻撃は重い。 防御系の役が「戦闘中の最大攻撃」より
        /// はるかに軽いターンで消えていれば、 温存の余地がそのぶんある ──
        /// 逆に比が 1 に近ければ、 いま既にほぼ最良の地点で切れており温存の実装余地は小さい。</summary>
        public static string DescribeFireTiming()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【役の発動タイミング (温存に余地があるか) ─ **役エポック単位・長さ層別**】");
            sb.AppendLine($"  エポック数: 短 {AccEpochs[0]:N0} / 中 {AccEpochs[1]:N0} / 長 {AccEpochs[2]:N0}"
                        + "   ※usedRoles がクリアされない期間 = 判断の地平");
            for (int b = 0; b < 3; b++)
            {
                if (AccEpochs[b] == 0) continue;
                bool any = false;
                for (int i = 0; i < 16; i++) if (AccFired[b][i] > 0) { any = true; break; }
                if (!any) continue;
                sb.AppendLine();
                sb.AppendLine($"  ── {BucketName[b]} (エポック {AccEpochs[b]:N0}) ──");
                sb.AppendLine("  役         | 発動数 | 位置% | 発動時攻撃 | 発動後最大 |   比 | 発動後の再成立 | **見送り優位**");
                foreach (var k in YachtRoles.All)
                {
                    int i = (int)k;
                    long n = AccFired[b][i];
                    if (n == 0) continue;
                    double pos = AccEpochLen[b][i] > 0
                               ? 100.0 * AccFireTurnRel[b][i] / AccEpochLen[b][i] : 0;
                    double fa = AccFireAtk[b][i] / (double)n;
                    double pm = AccPostMaxAtk[b][i] / (double)n;
                    double pf = AccPostForms[b][i] / (double)n;
                    double hb = 100.0 * AccHoldBetter[b][i] / n;
                    sb.AppendLine($"  {YachtRoles.NameOf(k),-9} | {n,6} | {pos,5:F0} | {fa,10:F1}"
                                + $" | {pm,10:F1} | {(pm > 0 ? fa / pm : 0),4:F2} | {pf,14:F2}"
                                + $" | {hb,6:F1}%");
                }
            }
            sb.AppendLine();
            sb.AppendLine("  ※**見送り優位** = 発動後にその役がより重いターンで再成立した割合。");
            sb.AppendLine("    後知恵を使うので「完璧な条件付き方策が取れる上限」。ここが小さければ温存の余地は無い。");
            sb.AppendLine("  ※比 = 発動時攻撃 ÷ 発動後に来た最大攻撃。1 なら既に最良の地点で切れている。");
            return sb.ToString();
        }

        /// <summary>ダイス別・役別の 1 戦あたり発動回数。</summary>
        public static string DescribeByDice(int runs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【ダイス別 役の発動 (1戦あたり)】");
            sb.Append("  ダイス".PadRight(22)).Append("戦闘数".PadLeft(8));
            for (int i = 0; i < 16; i++) sb.Append(YachtRoles.NameOf((RoleKind)i).PadLeft(7));
            sb.AppendLine();

            var keys = new System.Collections.Generic.List<string>(CombatsByDice.Keys);
            keys.Sort((a, b) => CombatsByDice[b].CompareTo(CombatsByDice[a]));
            foreach (var k in keys)
            {
                long c = CombatsByDice[k];
                ByDice.TryGetValue(k, out var arr);
                sb.Append(("  " + k).PadRight(22)).Append(c.ToString().PadLeft(8));
                for (int i = 0; i < 16; i++)
                {
                    double v = (arr != null && c > 0) ? arr[i] / (double)c : 0;
                    sb.Append(v.ToString("F3").PadLeft(7));
                }
                sb.AppendLine();
            }
            sb.AppendLine($"  ラン {runs} / ※〈極〉は ADR-0010 の想定 0.12 回/ラン");
            return sb.ToString();
        }

        public static void ResetStats()
        {
            System.Array.Clear(FormedCount, 0, FormedCount.Length);
            System.Array.Clear(FiredCount, 0, FiredCount.Length);
            YachtByDice.Clear();
            ByDice.Clear();
            CombatsByDice.Clear();
            System.Array.Clear(FiredThisCombat, 0, FiredThisCombat.Length);
            YachtFiredCount = YachtSumTotal = YachtPctTotal = 0;
            System.Array.Clear(YachtByFace, 0, YachtByFace.Length);
            ResetEpoch();
            System.Array.Clear(AccEpochs, 0, AccEpochs.Length);
            for (int b = 0; b < 3; b++)
            {
                System.Array.Clear(AccFired[b], 0, 16);
                System.Array.Clear(AccFireTurnRel[b], 0, 16);
                System.Array.Clear(AccEpochLen[b], 0, 16);
                System.Array.Clear(AccFireAtk[b], 0, 16);
                System.Array.Clear(AccPostMaxAtk[b], 0, 16);
                System.Array.Clear(AccPostForms[b], 0, 16);
                System.Array.Clear(AccHoldBetter[b], 0, 16);
            }
            TurnsEvaluated = 0;
        }

        /// <summary>ダイス別〈極〉発動の内訳。 発動数の多い順。</summary>
        public static string DescribeYachtByDice(int runs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【〈極〉ダイス別 発動数】");
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, long>>(YachtByDice);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            long tot = 0; foreach (var kv in list) tot += kv.Value;
            foreach (var kv in list)
                sb.AppendLine($"  {kv.Key,-22} {kv.Value,8}  ({(tot > 0 ? 100.0 * kv.Value / tot : 0),5:F1}%)");
            sb.AppendLine($"  合計 {tot} / {runs} ラン ＝ 1ラン {(runs > 0 ? tot / (double)runs : 0):F2} 回"
                        + "  (ADR-0010 の想定 0.12 回)");
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
