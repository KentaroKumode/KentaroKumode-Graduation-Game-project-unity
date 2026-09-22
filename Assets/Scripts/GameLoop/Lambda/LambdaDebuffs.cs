using System.Collections.Generic;
using GameLoop;

namespace GameLoop.Lambda
{
    /// <summary>
    /// Λ層（時間の狭間）の「次元の乱れ」由来恒久デバフの ID 定数。
    /// run.lambdaDebuffs のキーとして使う。段階(1〜3)は値側に持つ。
    /// </summary>
    public static class LambdaDebuffIds
    {
        // **lv1/lv2 は当たり枠、 lv3 は重い。 ただし lv3 単体では死なない** (2026-09-20)。
        //   〈迫りくる死〉だけが lv3 = 即死枠で、 深掘りの賭けはこの 1 本を引くかどうかに懸かる。
        //   他の 6 種は<b>重ねて初めて壊れる</b>強さに置く ── 7 種すべてが致命的だと
        //   どれを引いても同じになり、 引きの内容に判断が乗らない (深さが死のカウントダウンになる)。
        public const string HeavySteps        = "重い足取り";   // 出目合計 -2/-4 (1T) / **-8 (2T)**
        public const string VagueFeel         = "微妙な手応え"; // 敵への最終ダメージ -5/-10/**-25**%
        public const string IrritatingFoe     = "苛立つ強敵";   // 経過 5/4/**2** ターン毎に敵ダイス合計 +1
        public const string Distraction       = "注意散漫";     // 会心率の上限 50%/35% / **会心の枝を持たない**
        public const string MercifulExecution = "慈悲の処刑";   // 被弾後 HP が最大の 5/10/**20**% 以下なら即死
        public const string NerveDerangement  = "神経錯乱";     // 戦闘開始から 3/5/**10** ターン目開始まで消費不可
        public const string ImpendingDeath    = "迫りくる死";   // lv1/2 無効・lv3 で戦闘開始時 HP を 1 に

        /// <summary>抽選プール（全7種）。</summary>
        public static readonly string[] All =
        {
            HeavySteps, VagueFeel, IrritatingFoe, Distraction,
            MercifulExecution, NerveDerangement, ImpendingDeath,
        };
    }

    /// <summary>
    /// Λデバフを各システムから参照する薄いヘルパー。段階(1〜3)に応じた効果量を返す。
    /// 値は run.lambdaDebuffs に格納された段階を読むだけで、副作用は持たない。
    /// </summary>
    public static class LambdaDebuffEffects
    {
        private static int Lv(RunState run, string id) => run != null ? run.GetLambdaDebuffLevel(id) : 0;

        // --- 重い足取り: 序盤の出目合計デルタ(負値)。lv1/2/3 → -2/-4/**-8** ---
        //   lv1/2 は **1 ターン目だけ**、 lv3 のみ **2 ターン**にわたって効く (GetHeavyStepsTurns)。
        public static int GetFirstTurnDiceDelta(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.HeavySteps);
            switch (lv) { case 1: return -2; case 2: return -4; case 3: return -8; default: return 0; }
        }

        /// <summary>重い足取りが効く最後のターン。 lv1/2 は 1 ターン目のみ、 lv3 は 2 ターン目まで。
        /// 0 なら効かない。</summary>
        public static int GetHeavyStepsTurns(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.HeavySteps);
            return lv <= 0 ? 0 : (lv >= 3 ? 2 : 1);
        }

        // --- 微妙な手応え: 与ダメージ倍率。lv1/2/3 → 0.95/0.90/**0.75**（無し=1.0） ---
        public static float GetDamageDealtMult(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.VagueFeel);
            switch (lv) { case 1: return 0.95f; case 2: return 0.90f; case 3: return 0.75f; default: return 1f; }
        }

        // --- 苛立つ強敵: 敵ダイス +1 の発生間隔(ターン)。lv1/2/3 → 5/4/**2**（無し=0=無効） ---
        public static int GetIrritatingInterval(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.IrritatingFoe);
            switch (lv) { case 1: return 5; case 2: return 4; case 3: return 2; default: return 0; }
        }

        /// <summary>苛立つ強敵による現ターンの敵ダイス合計加算量（= floor(turn / interval)）。</summary>
        public static int GetIrritatingDiceBonus(RunState run, int currentTurn)
        {
            int interval = GetIrritatingInterval(run);
            if (interval <= 0 || currentTurn <= 0) return 0;
            return currentTurn / interval;
        }

        // --- 注意散漫: 実効会心率そのものの上限。lv1/2/3 → 50%/35%/0%（無し=1f=無効） ---
        // 2026-07-27: 旧「会心分子(X/9)の上限 8/6/4」から変更。分子/分母モデル廃止 (会心リバランス) により
        // レガシー分子側の clamp では critRatePctAdd / メタ精密を素通りさせてしまうため、
        // ResolveCritRate 後の最終値に対する天井へ移した (= 全ての会心率ソースに効く)。
        public static float GetCritRateCap(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.Distraction);
            // lv3 は会心そのものを持たせない (NoCritBranch) ので、 天井は 0。
            // lv1/2 は 0.30/0.20 → **0.50/0.35** (2026-09-20)。 武器の素の会心率は 15〜40% なので、
            //   0.20 の天井はほぼ全ビルドに刺さり「当たり枠」になっていなかった。
            //   0.50 は会心を積み増したビルドの頭だけを削り、 0.35 で上位武器 (デュランダル 35 / ノクタリア 40) に届く。
            switch (lv) { case 1: return 0.50f; case 2: return 0.35f; case 3: return 0f; default: return 1f; }
        }

        // --- 慈悲の処刑: 被弾後の即死HP割合閾値。lv1/2/3 → 0.05/0.10/**0.20**（無し=0=無効） ---
        public static float GetMercifulExecThreshold(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.MercifulExecution);
            switch (lv) { case 1: return 0.05f; case 2: return 0.10f; case 3: return 0.20f; default: return 0f; }
        }

        // --- 神経錯乱: このターン未満では消費不可。lv1/2/3 → 3/5/**10**（無し=0=制限なし） ---
        public static int GetConsumableLockUntilTurn(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.NerveDerangement);
            switch (lv) { case 1: return 3; case 2: return 5; case 3: return 10; default: return 0; }
        }

        /// <summary>注意散漫 lv3: <b>攻撃が会心の判定を持たない。</b> 会心倍率は乗らないが、
        /// <b>非会心の枝は通る</b> ── 会心ビルドだけを止め、 鈍器ビルドは生き残る。
        /// lv1/2 の「会心率の天井」を 0 まで詰めたものと等価で、 単体では致命ではない。</summary>
        public static bool NoCritBranch(RunState run) => Lv(run, LambdaDebuffIds.Distraction) >= 3;

        // --- 迫りくる死: lv3 でのみ戦闘開始時 HP=1（lv1/2 は猶予で無効） ---
        public static bool ImpendingDeathActive(RunState run)
            => Lv(run, LambdaDebuffIds.ImpendingDeath) >= 3;

        /// <summary><b>次の付与でいずれかが lv3 へ届く確率。</b> 0〜1。
        ///
        /// <para>Λ は「どこまで潜るか」が唯一の判断なのに、 プレイヤーにも BOT にも
        /// 渡せる尺度が「いま背負っている本数」しかなかった。 lv3 が崖である以上
        /// 本当に要るのは<b>次の一歩で崖を踏む確率</b>で、 <c>GrantRandom</c> が
        /// lv3 未満から一様に引く以上この値は厳密に計算できる ──
        /// <c>(lv2 の数) / (lv3 未満の数)</c>。 引いてから降りるのでは遅い
        /// (もう崖の上にいる) ので、 撤退条件はこの確率のしきい値で表す。</para>
        ///
        /// <para>全部 lv3 なら付与そのものが起きないので 0 を返す。</para></summary>
        public static float NextLv3Chance(RunState run)
        {
            if (run == null || run.lambdaDebuffs == null) return 0f;
            int candidates = 0, atLv2 = 0;
            foreach (var id in LambdaDebuffIds.All)
            {
                int lv = run.GetLambdaDebuffLevel(id);
                if (lv >= 3) continue;
                candidates++;
                if (lv == 2) atLv2++;
            }
            if (candidates == 0) return 0f;
            return (float)atLv2 / candidates;
        }

        // ============================================================
        //  付与（次元の乱れ閾値到達時に呼ぶ）
        // ============================================================

        /// <summary>ランダムなΛデバフを1つ付与（既存なら段階+1、最大3）。付与したIDを返す。</summary>
        public static string GrantRandom(RunState run, System.Random rng = null)
        {
            if (run == null) return null;
            if (run.lambdaDebuffs == null) run.lambdaDebuffs = new Dictionary<string, int>();

            var pool = LambdaDebuffIds.All;
            // 既に lv3 のものは抽選対象から除外（段階上限のため）。全て lv3 なら付与なし。
            var candidates = new List<string>();
            foreach (var id in pool)
                if (run.GetLambdaDebuffLevel(id) < 3) candidates.Add(id);
            if (candidates.Count == 0) return null;

            int idx = (rng != null) ? rng.Next(candidates.Count)
                                     : GameLoop.GameRng.RangeAuto("LambdaDebuffs.1", 0, candidates.Count);
            string pick = candidates[idx];
            int cur = run.GetLambdaDebuffLevel(pick);
            run.lambdaDebuffs[pick] = System.Math.Min(3, cur + 1);
            return pick;
        }
    }
}
