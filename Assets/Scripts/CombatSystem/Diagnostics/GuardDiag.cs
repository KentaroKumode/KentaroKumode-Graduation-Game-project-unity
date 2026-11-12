namespace CombatSystem
{
    /// <summary>[計装] 〈防御〉の軽減が<b>どれだけの量に触れているか</b>。
    ///
    /// <para><b>なぜ要るか。</b> 防御は 3%/段 → 4%/段 に上げても 10,000 ラン 中 14 ランしか
    /// 結果が変わらなかった。 原因として 2 つの説があり、 コードを読むだけでは決まらない。
    ///   ① 切り上げに飲まれている (オペランドが小さい)
    ///   ② そもそもこの経路を通る被ダメが一部でしかない
    ///      (固定ダメージ / かすり / DOT / 停滞 は別経路 ── 当時の〈停滞する時間〉は敵の攻撃への加算だった)
    /// 数字が無いまま「たぶん①」で直すと、 今日すでに 2 回やった
    /// 「読みだけで判断して外す」を繰り返す。 両方を分けて数える。</para>
    ///
    /// <para>AutoRunner がバッチ中 <c>logEnabled=false</c> にするのでログでは数えられない。
    /// 静的カウンタで持ち、 ワーカーの応答に載せる。</para></summary>
    public static class GuardDiag
    {
        /// <summary>敵の攻撃値の総和 (軽減前)。</summary>
        public static long AtkBefore;
        /// <summary>同 (軽減後)。 差が〈防御〉が実際に削った量。</summary>
        public static long AtkAfter;
        /// <summary>ブロック出目の総和。</summary>
        public static long BlockSum;
        /// <summary>ブロックを抜けて被ダメ計算へ入った量の総和。</summary>
        public static long LossBase;
        /// <summary>攻撃回数。</summary>
        public static long Attacks;
        /// <summary>軽減後にブロックで<b>全吸収</b>された回数 (lossBase == 0)。</summary>
        public static long FullyBlocked;

        /// <summary>[計装 2026-11-11] ヴェスカ連戦の敵の直接攻撃 1 回ごとの段:
        /// [0]回数 [1]素の攻撃値 [2]敵の攻撃を下げる効果の後 (呪縛・封じ・割合減・失血衰弱) [3]プレイヤーの防御の後
        /// [4]ブロックの後 (lossBase) [5]被ダメ −N (定額) で減った量 [6]被ダメ −% (遺物ほか) で減った量 [7]シールドが吸った量
        /// [8]最終的に HP が減った量 [9]ブロックの出目の合計。 [4] − [5] − [6] − [7] − [8] = その他 (役・虚空・仕込み刃・上限など)。</summary>
        public static readonly double[] Cut7F = new double[10];

        /// <summary>[計装 2026-11-12 #103] ヴェスカ〈停滞する時間〉で止まった端子。 段 (p1..p4) × 8:
        /// [0]ターン [1]新しく引いた枚数 [2]止まった枚数 [3]うち常設 (〈主幹〉) [4]前のターンに止まって手札に残った枚数
        /// [5]1 枚以上止まったターン [6]2 枚以上止まったターン [7]予備 (0)。 1 枚あたりの止まる率 = [2] ÷ [1]。
        /// (旧 2026-11-11 版は「配線と停滞スタック」を数えていた。 スタックの廃止で中身を入れ替えた)</summary>
        public static readonly double[] Stag7F = new double[32];
        public static void NoteStag7F(string bossId, int drawn, int stopped, int stoppedMains, int held)
        {
            if (bossId == null || !bossId.StartsWith(GameLoop.BossIds.Layer7Prefix)) return;
            int st = System.Math.Max(1, GameLoop.BossIds.VescaStageOf(bossId)) - 1, o = st * 8;
            Stag7F[o]++; Stag7F[o + 1] += drawn; Stag7F[o + 2] += stopped; Stag7F[o + 3] += stoppedMains; Stag7F[o + 4] += held;
            if (stopped >= 1) Stag7F[o + 5]++;
            if (stopped >= 2) Stag7F[o + 6]++;
        }
        public static bool InVesca()
        {
            var c = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            return c != null && c.bossId != null && c.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix);
        }
        public static void NoteCut7F(int raw, int beforeDef, int afterDef, int block, int lossBase, int hpLoss)
        {
            if (!InVesca()) return;
            Cut7F[0]++; Cut7F[1] += raw; Cut7F[2] += beforeDef; Cut7F[3] += afterDef; Cut7F[4] += lossBase; Cut7F[8] += hpLoss; Cut7F[9] += block;
        }

        public static void Reset()
        {
            AtkBefore = AtkAfter = BlockSum = LossBase = Attacks = FullyBlocked = 0;
            System.Array.Clear(Cut7F, 0, Cut7F.Length);
            System.Array.Clear(Stag7F, 0, Stag7F.Length);
        }

        public static void Note(int atkBefore, int atkAfter, int blockSum, int lossBase)
        {
            Attacks++;
            AtkBefore += atkBefore;
            AtkAfter += atkAfter;
            BlockSum += blockSum;
            LossBase += lossBase;
            if (lossBase <= 0) FullyBlocked++;
        }
    }
}
