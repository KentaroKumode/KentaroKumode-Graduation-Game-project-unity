using InventorySystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>
    /// プレイヤー攻撃/防御スタンス（ADR-0006）。ターン頭・**ロール前**に選ぶ（出目を見てからは不可＝壊れる）。
    ///
    /// - 攻撃優先（Attack）：現状の計算式そのまま。
    /// - 防御優先（Defense）：与ダメージ-90%（主ダメージのみ。反撃/業火/血令 等の固定ダメは対象外）。
    ///   受ける最終ダメージ-50%（全軽減/シールドの後、最後に適用）。＝負け前提の「耐えターン」。
    ///
    /// BOT判定（ADR-0006）：ロール前の推定勝率 estWinProb（CombatManager が正規近似で算出）と、
    /// 学習閾値で決める。実効閾値 = DefendWinProb + DefendHpBias×(1−HP割合)。estWinProb がこれ未満なら防御。
    /// 閾値は <see cref="DefendWinProbProvider"/>/<see cref="DefendHpBiasProvider"/> 経由で BOT(AutoRunner) が
    /// `PolicyParameters.Current` を結線（L2学習で最適化）。未結線（実プレイヤー/UI）は既定値、最終的には UI が選択上書き。
    /// </summary>
    public static class PlayerStance
    {
        public enum Kind { Attack = 0, Defense = 1 }

        public const float DefenseWinDamageMult = 0.1f;  // 与ダメージ-90%
        public const float DefenseLossDamageMult = 0.5f; // 受ける最終ダメージ-50%

        // 学習されない時の既定閾値（実プレイヤー/UI未配線時）
        public const float DefaultDefendWinProb = 0.35f;
        public const float DefaultDefendHpBias = 0.30f;

        /// <summary>防御に入る勝率閾値の供給元（BOTが PolicyParameters.Current を結線）。null=既定。</summary>
        public static System.Func<float> DefendWinProbProvider;
        /// <summary>HP低下時の閾値引き上げ量の供給元。null=既定。</summary>
        public static System.Func<float> DefendHpBiasProvider;

        /// <summary>UI 配線用（引数=選ばれたスタンス）。ビジュアルは後付け。</summary>
        public static event System.Action<Kind> OnChoose;

        /// <summary>ターン頭に呼ぶ。推定勝率＋学習閾値でスタンスを選び ctx へ反映（UI実装後は人間の選択に差し替え）。</summary>
        public static Kind Choose(CombatContext ctx, int playerHP, int playerMaxHP, float estWinProb)
        {
            if (ctx == null) return Kind.Attack;
            float t = DefendWinProbProvider != null ? DefendWinProbProvider() : DefaultDefendWinProb;
            float bias = DefendHpBiasProvider != null ? DefendHpBiasProvider() : DefaultDefendHpBias;
            float hpRatio = playerMaxHP > 0 ? (float)playerHP / playerMaxHP : 1f;
            float effThreshold = t + bias * (1f - hpRatio); // 瀕死ほど早めに防御
            Kind k = (estWinProb < effThreshold) ? Kind.Defense : Kind.Attack;
            ctx.playerStanceDefense = (k == Kind.Defense);
            OnChoose?.Invoke(k);
            return k;
        }

        public static string Label(Kind k) => k == Kind.Defense ? "防御優先" : "攻撃優先";
    }
}
