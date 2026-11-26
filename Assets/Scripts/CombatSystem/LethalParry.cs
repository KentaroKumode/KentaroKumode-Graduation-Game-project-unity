using System;

namespace CombatSystem
{
    /// <summary>
    /// [2026-11-26] 致死パリィ ＆ 〈決死〉の純粋な規則と戦闘中の状態。
    /// 敵の攻撃による致死ダメージ時に発動し、成功するとそのターン終了まで完全無敵（被ダメ0・敵の命中時効果不発・シールド未消費）となり、
    /// 次のターン（同じ戦闘限り）に主攻撃の最終与ダメージが ×(1 + 0.20 × ラン中成功回数) になる（ダウン ×2.0 と掛け合わせ）。
    /// 猶予フレーム（60fps換算実時間）はラン中で成功するたびに 12 → 4 → 2 → 1 → 1... へ縮む。
    /// オートパリィ（設定 OFF 時・およびヘッドレス BOT）はヒル関数 P(N) = N^3 / (N^3 + 8^3) で判定する。
    /// </summary>
    public static class LethalParry
    {
        public static readonly int[] WindowFrames = { 12, 4, 2, 1 };
        public const float AutoParryN0 = 8f;
        public const float CueDurationSeconds = 0.60f;
        public const int DesperatePctPerSuccess = 20;

        /// <summary>現在のラン中成功回数に対する猶予フレーム数（60fps 換算）。</summary>
        public static int WindowFramesFor(int successCount)
        {
            int idx = Math.Max(0, Math.Min(successCount, WindowFrames.Length - 1));
            return WindowFrames[idx];
        }

        /// <summary>現在のラン中成功回数に対する猶予秒数（実時間）。</summary>
        public static float WindowSecondsFor(int successCount) => WindowFramesFor(successCount) / 60f;

        /// <summary>オートパリィ成功率 P(N) = N^3 / (N^3 + 8^3)。12f=77.1%, 4f=11.1%, 2f=1.5%, 1f=0.20%。</summary>
        public static float AutoParryChance(int successCount)
        {
            float n = WindowFramesFor(successCount);
            float n3 = n * n * n;
            const float n03 = AutoParryN0 * AutoParryN0 * AutoParryN0;
            return n3 / (n3 + n03);
        }

        /// <summary>成功回数に対する〈決死〉の最終与ダメージ倍率（独立乗算）。</summary>
        public static float DesperateMul(int successCount) => successCount > 0 ? 1f + (DesperatePctPerSuccess * 0.01f) * successCount : 1f;

        /// <summary>成功回数に対する〈決死〉のボーナス %。</summary>
        public static int DesperateBonusPct(int successCount) => successCount > 0 ? DesperatePctPerSuccess * successCount : 0;

        // ===== 戦闘中の状態（1 戦闘限り・BossDown と同じ static 方式）=====

        /// <summary>このターン、パリィ成功により完全無敵（不死）か。</summary>
        public static bool InvincibleThisTurn;
        /// <summary>次のターン（同じ戦闘内）に〈決死〉を付与する予約。</summary>
        public static bool PendingDesperateNextTurn;
        /// <summary>今ターン有効な〈決死〉の成功回数（0 = 無効）。</summary>
        public static int DesperateCountNow;

        public static float DesperateMulNow => DesperateCountNow > 0 ? DesperateMul(DesperateCountNow) : 1f;
        public static int DesperateBonusPctNow => DesperateBonusPct(DesperateCountNow);

        /// <summary>主攻撃の最終与ダメージに〈決死〉倍率を掛ける（BossDown.ApplyTakenNow の直後）。</summary>
        public static int ApplyDesperateNow(int dmg)
            => DesperateCountNow <= 0 || dmg <= 0 ? dmg : (int)Math.Ceiling(dmg * DesperateMulNow);

        /// <summary>戦闘開始・終了時にリセット（〈決死〉は戦闘をまたがない）。</summary>
        public static void ResetCombat()
        {
            InvincibleThisTurn = false;
            PendingDesperateNextTurn = false;
            DesperateCountNow = 0;
        }

        /// <summary>ターン開始時に呼ぶ。前ターンにパリィ成功していれば今ターン〈決死〉を発動。</summary>
        public static void BeginTurn(int runSuccessCount)
        {
            InvincibleThisTurn = false;
            if (PendingDesperateNextTurn)
            {
                PendingDesperateNextTurn = false;
                DesperateCountNow = Math.Max(1, runSuccessCount);
            }
            else
            {
                DesperateCountNow = 0;
            }
        }

        /// <summary>パリィ成功時に呼ぶ。成功回数は呼び出し側（RunState）で +1 済みの値を渡す。</summary>
        public static void OnParrySucceeded(int newRunSuccessCount)
        {
            InvincibleThisTurn = true;
            PendingDesperateNextTurn = true;
            LethalParryDiag.NoteAttempt(newRunSuccessCount - 1, true);
        }

        public static void OnParryFailed(int currentRunSuccessCount)
        {
            LethalParryDiag.NoteAttempt(currentRunSuccessCount, false);
        }
    }

    /// <summary>バッチ計測用の致死パリィ計装（乱数を引かない）。</summary>
    public static class LethalParryDiag
    {
        public static long Attempts, Successes;
        public static readonly long[] AttemptsByIdx = new long[5];
        public static readonly long[] SuccessesByIdx = new long[5];

        public static void Reset()
        {
            Attempts = 0; Successes = 0;
            Array.Clear(AttemptsByIdx, 0, AttemptsByIdx.Length);
            Array.Clear(SuccessesByIdx, 0, SuccessesByIdx.Length);
        }

        public static void NoteAttempt(int prevSuccessCount, bool ok)
        {
            Attempts++;
            if (ok) Successes++;
            int i = Math.Max(0, Math.Min(prevSuccessCount, AttemptsByIdx.Length - 1));
            AttemptsByIdx[i]++;
            if (ok) SuccessesByIdx[i]++;
        }

        public static string Line()
            => Attempts == 0 ? ""
             : $"[致死パリィ] 発動 {Attempts} 回 / 成功 {Successes} 回 ({100.0 * Successes / Attempts:0.0}%)"
             + $" [1回目 {SuccessesByIdx[0]}/{AttemptsByIdx[0]}・2回目 {SuccessesByIdx[1]}/{AttemptsByIdx[1]}・3回目 {SuccessesByIdx[2]}/{AttemptsByIdx[2]}・4回目+ {SuccessesByIdx[3] + SuccessesByIdx[4]}/{AttemptsByIdx[3] + AttemptsByIdx[4]}]";
    }
}
