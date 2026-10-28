using System;
using System.Collections.Generic;

namespace GameLoop
{
    /// <summary>
    /// 本体が外 (BOT・計測) へ判断や記録を委ねる口。 <b>本体は AutoTest の型を参照しない</b> (2026-10-29)。
    ///
    /// <para>どの口も既定は null で、 本体はそのとき自分の既定の振る舞いだけで動く。
    /// BOT は AutoRunner の生成時に差し込み、 破棄時に外す (<c>AutoTest.BotHooks</c>)。
    /// したがって通常のプレイでは BOT の学習データも集計も一切効かない。</para>
    /// </summary>
    public static class GameHooks
    {
        // ===== 判断 (プレイヤーの選択を代行する口) =====

        /// <summary>開幕パッシブの提示 (〈兵站〉r10 の 2〜3 択) から 1 つ選ぶ。 戻り値は添字。
        /// null なら先頭 (選択画面が付くまでの仮)。</summary>
        public static Func<IReadOnlyList<string>, int> ChooseStartingPassive;

        /// <summary>自動装備の武器評価に足す点 (武器 id → 加点)。 null なら 0。</summary>
        public static Func<string, float> WeaponEvBonus;

        // ===== 計測 (結果を変えない) =====

        /// <summary>ボス戦に入る時点の戦力値 (層別集計 <see cref="RunStats.BossEntryPower"/> に足す)。 null なら 0。</summary>
        public static Func<RunState, int> MeasureRunPower;

        /// <summary>会心率を決めた直後 (逓減前の加算合計, 実効率)。</summary>
        public static Action<float, float> OnCritRateResolved;

        /// <summary>ショップが段付き武器を並べた (家系, 段)。</summary>
        public static Action<string, int> OnShopTierOffered;

        /// <summary>ショップが強化枠を並べた。</summary>
        public static Action OnShopUpgradeOffered;

        /// <summary>ショップで強化を買った (上がった段数)。</summary>
        public static Action<int> OnShopUpgradeBought;

        /// <summary>全部の口を外す。</summary>
        public static void Clear()
        {
            ChooseStartingPassive = null;
            WeaponEvBonus = null;
            MeasureRunPower = null;
            OnCritRateResolved = null;
            OnShopTierOffered = null;
            OnShopUpgradeOffered = null;
            OnShopUpgradeBought = null;
        }
    }
}
