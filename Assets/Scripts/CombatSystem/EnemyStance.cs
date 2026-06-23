using InventorySystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>
    /// 敵スタンス（ADR-0005）。毎ターン頭にランダムで二択を提示（テレグラフ）し、ロール力とダメージ出力を
    /// アンチ相関で振る。プレイヤーはロール前に読んで #1 振り直し（希望消費）の判断ができる。
    ///
    /// - 強ロール・低火力（HighRollLowDmg）：**今のダイス＝基準**でロール、被ダメ ×LowDamageMult。
    /// - 弱ロール・高火力（LowRollHighDmg）：**期待値が約0.65倍になるよう面を縮めて実際に振る**（結果の事後倍率ではない）、被ダメ ×HighDamageMult。
    ///
    /// 反映先：ロール＝弱スタンス時に敵ダイスの最大出目を <see cref="WeakRollMax"/> に縮めて RollDice（CombatManager）／
    /// ダメージ＝ctx.enemyStanceDamageMult（ApplyLossDamageModifiers）。ビジュアルUIは <see cref="OnTelegraph"/> 後付け。
    /// 対象：通常・エリート・ボス全て（数値は BOT 較正前提）。
    /// </summary>
    public static class EnemyStance
    {
        public enum Kind { None = 0, HighRollLowDmg = 1, LowRollHighDmg = 2 }

        // === 調整値（暫定・BOT較正前提） ===
        public const float WeakRollRatio = 0.65f; // 弱ロールの期待値（強ロール=1.0基準の約6〜7割）
        public const float LowDamageMult = 0.5f;  // 強ロール時の被ダメ倍率
        public const float HighDamageMult = 1.6f; // 弱ロール時の被ダメ倍率

        /// <summary>弱ロール時に用いる縮小後の最大出目。期待値(=(max+1)/2)が ratio 倍になるよう面を縮める。
        /// ratio は省略時 WeakRollRatio(0.65)、 ボス別にチューナー調整値を渡す。最低1（1d1=常に1）。実ロールを弱くする。</summary>
        public static int WeakRollMax(int baseMax, float ratio = WeakRollRatio)
        {
            if (baseMax <= 1) return 1;
            int m = UnityEngine.Mathf.RoundToInt(ratio * (baseMax + 1)) - 1;
            return UnityEngine.Mathf.Clamp(m, 1, baseMax);
        }

        /// <summary>固有面ダイス(署名ダイス)の弱ロール版。各面を ratio 倍に縮める（最低1）。
        /// ratio 省略時は WeakRollRatio。 面構成を保ったまま縮小する。</summary>
        public static int[] WeakRollFaces(int[] faces, float ratio = WeakRollRatio)
        {
            if (faces == null || faces.Length == 0) return faces;
            var shrunk = new int[faces.Length];
            for (int i = 0; i < faces.Length; i++)
                shrunk[i] = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(faces[i] * ratio));
            return shrunk;
        }

        /// <summary>UI テレグラフ配線用（引数=選ばれたスタンス）。ビジュアルは後付け。</summary>
        public static event System.Action<Kind> OnTelegraph;

        /// <summary>ターン頭に呼ぶ。ランダムに二択を選び ctx へ反映し、テレグラフを発火する。
        /// ロールの弱体化は CombatManager がロール時に WeakRollMax で実施（kind を見て判断）。</summary>
        public static Kind Apply(CombatContext ctx)
        {
            if (ctx == null) return Kind.None;
            Kind k = UnityEngine.Random.value < 0.5f ? Kind.HighRollLowDmg : Kind.LowRollHighDmg;
            // 高火力スタンスの被ダメ倍率(=ボス攻撃力)・弱ロール比をボス別にチューナーから取得（非ボス/未調整は既定1.6/0.65）。
            float highMult = AutoTest.BossTuning.Param(ctx.bossId, AutoTest.BossParam.StanceAtkMult);
            ctx.enemyStanceDamageMult = (k == Kind.HighRollLowDmg) ? LowDamageMult : highMult;
            ctx.enemyStanceKind = (int)k;
            ctx.enemyStanceWeakRollRatio = AutoTest.BossTuning.Param(ctx.bossId, AutoTest.BossParam.WeakRollRatio);
            OnTelegraph?.Invoke(k);
            return k;
        }

        public static string Label(Kind k)
            => k == Kind.HighRollLowDmg ? "強ロール・低火力"
             : k == Kind.LowRollHighDmg ? "弱ロール・高火力" : "—";
    }
}
