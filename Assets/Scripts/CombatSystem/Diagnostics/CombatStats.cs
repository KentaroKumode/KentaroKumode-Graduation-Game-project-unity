using System;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>[計測] 戦闘の集計 (与ダメの内訳・段別倍率・層別の与ダメと収支・初回ロール・リロール)。 BOT のレポートだけが読む。 ゲームの結果は変えない。 2026-10-29 に CombatManager から分離。</summary>
    public static class CombatStats
    {
        /// <summary>与ダメージの内訳計測 (診断用)。 「BOT の実効火力がどこから来ているか」を
        /// 実測するために置く。 §15-5 の遺物単位は参照状態 (atkBase 17 / 与ダメ 21.4) を
        /// **机上で組み立てて**較正したが、 実機と 5 倍ずれていたことが 2026-08-03 に判明した。
        /// 二度と机上の参照状態で較正しないための計測点。
        /// [0]件数 [1]武器素火力 [2]攻撃端子出目 [3]パッシブ加算 [4]消費 [5]遺物
        /// [6]atkBase合計 [7]最終与ダメ合計 [8]会心回数 [9]固定ダメ合計 [10]追撃合計</summary>
        public static readonly double[] DamageBreakdown = new double[19];

        /// <summary>**7層 (ヴェスカ連戦) だけ**の内訳。 添字は <see cref="DamageBreakdown"/> と同じ。
        /// 7層は 4 形態が 1 戦闘なので、 **戦闘の開始/終了の差分**で連戦全体が採れる ──
        /// 書き込み地点 (19 箇所) には一切触らないので本体の計測を壊さない。
        ///
        /// <para>2026-08-22: 同一シードで Optimal が 7層 p3 の与ダメ/T を **+20%** 上回るのに、
        /// 攻撃本数も攻撃出目計もほぼ同じ (1.46/17.3 対 1.44/17.0)。 会心系の役の発動数も同一。
        /// **倍率のどこかに差がある**が特定できていない。 それを切り分けるための計装。</para></summary>
        public static readonly double[] DamageBreakdown7F = new double[19];

        public static void ResetDamageBreakdown()
        {
            for (int i = 0; i < DamageBreakdown7F.Length; i++) DamageBreakdown7F[i] = 0;
            for (int i = 0; i < DamageBreakdown.Length; i++) DamageBreakdown[i] = 0;
            for (int i = 0; i < DamageStages.Length; i++) DamageStages[i] = 0;
            ResetFloorDamage();
        }

        /// <summary>与ダメ倍率の**段別**実測 (診断用・2026-08-03)。
        /// 総倍率 ×4.98 のうち 会心 1.78 × outgoing 2.21 = 3.93 しか説明できず、
        /// 残差 ×1.27 の所在が不明だったため、 修飾チェーンの各段の**通過後合計**を積む。
        /// 隣り合う段の比 = その段の平均倍率。 憶測せずに済ませるための計測点。
        /// [0]件数
        /// [1]A atkBase                 [2]B damageBonus+OnPreDealDamage 後
        /// [3]C 追撃加算後              [4]D 会心/鈍器 後 (ProcessDamage の返り値)
        /// [5]E outgoing 乗算後         [6]F 鬼火の油+攻撃バースト後
        /// [7]G 練度不足後              [8]H 敵軽減パッシブ後
        /// [9]I 敵の防御値後            [10]J (旧 利刃 余剰貫通・2026-10-28 撤去。添字維持のため I と同値)
        /// [11]K (撤去済み。添字維持のため J と同値) [12]L 脆弱後
        /// [13]M 最低保証後             [14]N 狂暴化後
        /// [15]O 俊敏/防御スタンス後 (最終)
        /// [16]会心倍率の合計(会心時のみ) [17]会心回数
        /// [18]鈍器発動 [19]研磨剤発動 [20]脆弱発動 [21]狂暴化発動
        /// [22]乗算時点の outgoing 合計 (研磨剤込み) [23](旧 余剰貫通率の合計・未使用)</summary>
        public static readonly double[] DamageStages = new double[24];

        /// <summary>ProcessDamage は勝敗・引分・敵側でも呼ばれるので、
        /// **プレイヤー勝利の主攻撃のときだけ** DamageStages を積むためのゲート。</summary>
        public static bool DiagWinAttack;

        /// <summary>層 × 敵種別ごとの与ダメ分布 (診断用・2026-08-03)。
        /// 全層平均の 137 だけ見て敵HPを決めると序盤を壊すので、 **層別に取る**。
        /// 添字: [floor 0..7][kind 0=通常/1=エリート/2=ボス][項目]
        /// 項目 [0]攻撃回数 [1]与ダメ合計(主+固定) [2]最小 [3]最大 [4]敵maxHP合計 [5]ターン番号合計
        /// [6]戦闘数(ターン1の攻撃を数えた近似)</summary>
        public static readonly double[,,] FloorDamage = new double[8, 3, 7];

        /// <summary>分位を出すための度数分布。 バケット幅 10、 0〜790+ を 80 段。
        /// 平均だけだと「最低/最高」が答えられないので度数で持つ。</summary>
        public static readonly int[,,] FloorDamageHist = new int[8, 3, 80];

        public static void ResetFloorDamage()
        {
            System.Array.Clear(FloorDamage, 0, FloorDamage.Length);
            System.Array.Clear(FloorDamageHist, 0, FloorDamageHist.Length);
            System.Array.Clear(TurnStats, 0, TurnStats.Length);
        }

        /// <summary>層 × 敵種別ごとの **ターン収支** (診断用・2026-08-03)。
        /// 通常戦を 3〜4T にしたことで敵が初めて手番を得るようになったので、
        /// 「被ダメがどれだけ増えたか」と「BOT がブロック端子へ配線し始めたか」を同時に見る。
        /// 後者が動いていれば ADR-0009 のジレンマが起動した証拠で、 耐久側を触る必要はない。
        /// 添字: [floor][kind][項目]
        /// [0]ターン数 [1]ブロック配線したターン数 [2]攻撃ダイス本数 [3]ブロックダイス本数
        /// [4]充電ダイス本数 [5]被ダメ合計 [6]敵攻撃値合計 [7]ブロック出目合計
        /// [8]ターン終了時HP合計 [9]ctx.playerMaxHP 合計 [10]Run.playerMaxHP 合計
        /// ※ [9] と [10] を分けているのは、 道中で平均最大HPが半減して見える件が
        ///    「実際にランの最大HPが減っている」のか「戦闘コンテキストへのコピーがずれている」のかを
        ///    切り分けるため (2026-08-04)。</summary>
        public static readonly double[,,] TurnStats = new double[8, 3, 11];

        /// <summary>計装用の層添字 (1..7)。 添字 0 は旧 Λ層 (2026-10-28 廃止) の枠で、 いまは使わない。</summary>
        public static int StatFloorIndex()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            return Mathf.Clamp(run?.Depth ?? 1, 1, 7);
        }

        /// <summary>[計装] **初回ロールの素の 5個同値率**。 リロール 0 回のターンで極が 4.8% 出ており、
        /// 理論値 0.077% (5個の d6) の 62 倍。 振り直していない以上ここで決まるはずなので、
        /// 本数・面配列の実体・出目分布のどれが効いているかを直に採る。</summary>
        public static long FirstRollCount, FirstRollYacht, FirstRollDiceCountSum, FirstRollFaceLenSum, FirstRollNoFaces;

        public static readonly long[] FirstRollByDiceCount = new long[16];

        public static readonly long[] FirstRollYachtByDiceCount = new long[16];

        public static readonly long[] FirstRollFaceHist = new long[32];

        public static void ResetFirstRollStats()
        {
            FirstRollCount = FirstRollYacht = FirstRollDiceCountSum = FirstRollFaceLenSum = FirstRollNoFaces = 0;
            System.Array.Clear(FirstRollByDiceCount, 0, FirstRollByDiceCount.Length);
            System.Array.Clear(FirstRollYachtByDiceCount, 0, FirstRollYachtByDiceCount.Length);
            System.Array.Clear(FirstRollFaceHist, 0, FirstRollFaceHist.Length);
        }

        public static void NoteFirstRoll(int count, ItemSystem.PassiveSkills.CombatContext ctx, int[] dice)
        {
            FirstRollCount++;
            FirstRollDiceCountSum += count;
            if (count >= 0 && count < FirstRollByDiceCount.Length) FirstRollByDiceCount[count]++;

            int faceLen = (ctx != null && ctx.equippedDiceFaces != null) ? ctx.equippedDiceFaces.Length : 0;
            FirstRollFaceLenSum += faceLen;
            if (faceLen == 0) FirstRollNoFaces++;

            if (dice != null)
                foreach (var v in dice)
                    if (v >= 0 && v < FirstRollFaceHist.Length) FirstRollFaceHist[v]++;

            if (YachtRoles.MaxSameCount(dice) >= 3)
            {
                FirstRollYacht++;
                if (count >= 0 && count < FirstRollYachtByDiceCount.Length) FirstRollYachtByDiceCount[count]++;
            }
        }

        /// <summary>[計装] リロールの実測。 0=回数 / 1=振り直したダイス総数 / 2=消費充電。
        /// ADR-0010 Verification で「リロール経済が回っているか」を見るため。</summary>
        public static readonly long[] RerollStats = new long[3];

        /// <summary>[計装] 装備維持費。 Due=請求額 / Paid=実際に払えた額。
        /// 差が大きいほど「充電が足りずに踏み倒している」＝維持費が重すぎる目安。</summary>
        public static long PassiveUpkeepDue, PassiveUpkeepPaid;

        /// <summary>[計装] 1 ターンあたりのリロール回数の分布 (添字 = 回数、 8 以上は 8 に丸め)。
        /// **平均では分布が分からない** ── 全体平均 0.67 回/ターンでも、
        /// 極が成立する少数のターンだけ 3〜5 回刻んでいる可能性がある。
        /// 「2 回制限」が拘束力を持つかは、 3 回以上の帯の厚みで決まる。</summary>
        public static readonly long[] RerollPerTurn = new long[9];

        /// <summary>そのターンで〈極〉が成立したときのリロール回数分布 (同上)。
        /// 上の分布と比べて右に寄っていれば、 **振り直しが極の追跡に使われている**。</summary>
        public static readonly long[] RerollPerTurnYacht = new long[9];

        private static int KindIndex(ItemSystem.PassiveSkills.CombatContext ctx)
            => ctx == null ? 0
             : ctx.currentEnemyKind == EnemyKind.Boss ? 2
             : ctx.currentEnemyKind == EnemyKind.Elite ? 1 : 0;

        /// <summary>層×種別のターン収支を 1 ターンぶん記録する。</summary>
        public static void RecordTurn(ItemSystem.PassiveSkills.CombatContext ctx, int atkDice, int blkDice, int chgDice,
                                      int blockSum, int enemyAtkValue)
        {
            if (ctx == null) return;
            int f = StatFloorIndex();
            int k = KindIndex(ctx);
            TurnStats[f, k, 0]++;
            if (blkDice > 0) TurnStats[f, k, 1]++;
            TurnStats[f, k, 2] += atkDice;
            TurnStats[f, k, 3] += blkDice;
            TurnStats[f, k, 4] += chgDice;
            TurnStats[f, k, 5] += ctx.playerDamageThisTurn;
            TurnStats[f, k, 6] += enemyAtkValue;
            TurnStats[f, k, 7] += blockSum;
            TurnStats[f, k, 8] += ctx.playerCurrentHP;
            TurnStats[f, k, 9] += ctx.playerMaxHP;
            TurnStats[f, k, 10] += GameLoop.GameManager.Instance?.Run?.playerMaxHP ?? 0;
        }

        /// <summary>層×種別の与ダメを 1 攻撃ぶん記録する。</summary>
        public static void RecordFloorDamage(ItemSystem.PassiveSkills.CombatContext ctx, int dealt, int fixedDmg, int enemyMaxHp)
        {
            int f = StatFloorIndex();
            int k = KindIndex(ctx);
            double v = dealt + fixedDmg;
            if (FloorDamage[f, k, 0] == 0) { FloorDamage[f, k, 2] = v; FloorDamage[f, k, 3] = v; }
            else
            {
                if (v < FloorDamage[f, k, 2]) FloorDamage[f, k, 2] = v;
                if (v > FloorDamage[f, k, 3]) FloorDamage[f, k, 3] = v;
            }
            FloorDamage[f, k, 0]++;
            FloorDamage[f, k, 1] += v;
            FloorDamage[f, k, 4] += enemyMaxHp;
            FloorDamage[f, k, 5] += ctx != null ? ctx.currentTurn : 0;
            if (ctx != null && ctx.currentTurn == 1) FloorDamage[f, k, 6]++;
            FloorDamageHist[f, k, Mathf.Clamp((int)(v / 10.0), 0, 79)]++;
        }
    }
}
