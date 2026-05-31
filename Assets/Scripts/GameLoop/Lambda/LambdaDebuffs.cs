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
        public const string HeavySteps        = "重い足取り";   // 1ターン目のダイス合計 -2/-4/-6
        public const string VagueFeel         = "微妙な手応え"; // 敵への最終ダメージ -5/-10/-15%
        public const string IrritatingFoe     = "苛立つ強敵";   // 経過 5/4/3 ターン毎に敵ダイス合計 +1
        public const string Distraction       = "注意散漫";     // 会心の出目上限(分子)が 8/6/4 に減少
        public const string MercifulExecution = "慈悲の処刑";   // 被弾後 HP が最大の 5/10/15% 以下なら即死
        public const string NerveDerangement  = "神経錯乱";     // 戦闘開始から 3/5/7 ターン目開始まで消費不可
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

        // --- 重い足取り: 1ターン目のダイス合計デルタ(負値)。lv1/2/3 → -2/-4/-6 ---
        public static int GetFirstTurnDiceDelta(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.HeavySteps);
            return lv <= 0 ? 0 : -2 * lv;
        }

        // --- 微妙な手応え: 与ダメージ倍率。lv1/2/3 → 0.95/0.90/0.85（無し=1.0） ---
        public static float GetDamageDealtMult(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.VagueFeel);
            return lv <= 0 ? 1f : 1f - 0.05f * lv;
        }

        // --- 苛立つ強敵: 敵ダイス +1 の発生間隔(ターン)。lv1/2/3 → 5/4/3（無し=0=無効） ---
        public static int GetIrritatingInterval(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.IrritatingFoe);
            switch (lv) { case 1: return 5; case 2: return 4; case 3: return 3; default: return 0; }
        }

        /// <summary>苛立つ強敵による現ターンの敵ダイス合計加算量（= floor(turn / interval)）。</summary>
        public static int GetIrritatingDiceBonus(RunState run, int currentTurn)
        {
            int interval = GetIrritatingInterval(run);
            if (interval <= 0 || currentTurn <= 0) return 0;
            return currentTurn / interval;
        }

        // --- 注意散漫: 会心分子(X/9)の上限。lv1/2/3 → 8/6/4（無し=9） ---
        public static int GetCritNumeratorCap(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.Distraction);
            switch (lv) { case 1: return 8; case 2: return 6; case 3: return 4; default: return 9; }
        }

        // --- 慈悲の処刑: 被弾後の即死HP割合閾値。lv1/2/3 → 0.05/0.10/0.15（無し=0=無効） ---
        public static float GetMercifulExecThreshold(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.MercifulExecution);
            return lv <= 0 ? 0f : 0.05f * lv;
        }

        // --- 神経錯乱: このターン未満では消費不可。lv1/2/3 → 3/5/7（無し=0=制限なし） ---
        public static int GetConsumableLockUntilTurn(RunState run)
        {
            int lv = Lv(run, LambdaDebuffIds.NerveDerangement);
            switch (lv) { case 1: return 3; case 2: return 5; case 3: return 7; default: return 0; }
        }

        // --- 迫りくる死: lv3 でのみ戦闘開始時 HP=1（lv1/2 は猶予で無効） ---
        public static bool ImpendingDeathActive(RunState run)
            => Lv(run, LambdaDebuffIds.ImpendingDeath) >= 3;

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
                                     : UnityEngine.Random.Range(0, candidates.Count);
            string pick = candidates[idx];
            int cur = run.GetLambdaDebuffLevel(pick);
            run.lambdaDebuffs[pick] = System.Math.Min(3, cur + 1);
            return pick;
        }
    }
}
