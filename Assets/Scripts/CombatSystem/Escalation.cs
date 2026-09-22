using System;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>
    /// ADR-0009 柱4: 段階エスカレーションの正本。
    /// 敵攻撃値 (基礎値 + ダイス寄与) 全体に段階倍率を掛ける
    /// ── 基礎値のみに掛けるとシールド蓄積系を追い越せずグラインド無敵化する
    /// (ADR-0009 Verification 結果 2.)。
    /// 数値の出典: tools/adr0009_sim.py スイープ (2026-07-15)。変更時は再スイープすること。
    /// </summary>
    public static class Escalation
    {
        public const string ProfileStd = "std";
        public const string ProfileRush = "rush";
        public const string ProfileGentle = "gentle";
        public const string ProfileSpike = "spike";

        /// <summary>段階閾値。ターン数がこの値を超えるたびに次段階へ (T5/10/15/20)。</summary>
        public static readonly int[] Thresholds = { 5, 10, 15, 20 };

        // 標準 = シミュレートの "mid" 曲線 (rush はガードを、gentle はグラインド課金を壊す)
        //
        // **2026-08-16: {1.00,1.30,1.60,1.95,2.30} → {1.00,1.20,1.40,1.60,1.80} へ緩和。**
        //   実測 (0pt・1000ラン) の段階別ターン比:
        //     雑魚/精鋭 S0 80.8% / S1 17.2% / S2以上 2.0%
        //     ボス      S0 51.1% / S1 28.1% / S2以上 20.7%
        //   後半 3 段は**実質ボス専用**で、雑魚戦の 8 割は倍率 1.00 のまま終わる。
        //   したがってここを緩めても道中は動かず、 長期戦のボスだけが緩む。
        //   閾値 (T5/10/15/20) は据え置き ── 全戦闘の平均は 5.3T で設計時の想定どおりであり、
        //   「閾値が古い」という仮説は実測で否定済み。 動かすべきは倍率の側だった。
        //
        // **2026-09-08: 上の緩和を巻き戻す。** {1.00,1.20,1.40,1.60,1.80} → 元の
        //   {1.00,1.30,1.60,1.95,2.30}。 訓練済み BOT (Optimal) の 7層クリアが 66.9% に達し、
        //   **band 11 に 66.9% が張り付いて**いた (設計目標は人間で「2割強」)。 08-16 の緩和と
        //   ボス前 Shop+Rest 確定配置、 09-06 の罠アイテム 50 品削除 (48%→65%) が積み上がった結果。
        //   上のコメントのとおり後半 3 段は**実質ボス専用** (雑魚戦の 8 割は S0 のまま) なので、
        //   戻しても道中は動かず、 天井に張り付いている **7 層ボスにだけ効く**。
        //   Λ デバフ間隔の短縮 (GameManager.LambdaDebuffInterval 3→2) と同時に入れている。
        private static readonly float[] CurveStd = { 1.00f, 1.30f, 1.60f, 1.95f, 2.30f };
        private static readonly float[] CurveRush = { 1.00f, 1.40f, 1.80f, 2.20f, 2.60f };
        private static readonly float[] CurveGentle = { 1.00f, 1.15f, 1.30f, 1.45f, 1.60f };

        /// <summary>スパイク型: このターンのみ倍率が跳ねる (断罪周期の後継)。暫定値・スイープ対象。</summary>
        public const int SpikeTurn = 12;
        public const float SpikeMultiplier = 2.5f;

        /// <summary>現在ターンの段階 (0〜4)。turn は 1 始まり。
        ///
        /// **挑戦デバフ〈天変地異〉は閾値を前倒しする** (2026-08-08)。 段階そのものや倍率は
        /// 変えず、 到達が T1 で 1 / T2 で 2 / T3 で 3 ターン早まる。 全戦闘に効き、
        /// 長引くほど重くなるので**序盤に偏らない**。 旧効果 (ボス戦の最初 N 撃 ×1.5) は
        /// 40 ターン級のボス戦で誤差にしかならず、 単独 T3 で対照群を上回る=実質無料だった。</summary>
        public static int StageOf(int turn)
        {
            int shift = MetaProgression.MetaDebuffApplicator.GetEscalationTurnShift();
            int s = 0;
            for (int i = 0; i < Thresholds.Length; i++)
                if (turn > Mathf.Max(1, Thresholds[i] - shift)) s++;
            return s;
        }

        /// <summary>次の段階閾値ターン。最終段階なら -1 (計器盤の残ターン表示用)。</summary>
        public static int NextThresholdTurn(int turn)
        {
            int shift = MetaProgression.MetaDebuffApplicator.GetEscalationTurnShift();
            for (int i = 0; i < Thresholds.Length; i++)
            {
                int th = Mathf.Max(1, Thresholds[i] - shift);
                if (turn <= th) return th;
            }
            return -1;
        }

        /// <summary>プロファイル×ターンの攻撃値倍率。</summary>
        public static float GetMultiplier(string profile, int turn)
        {
            int stage = StageOf(turn);
            switch (profile)
            {
                case ProfileRush: return CurveRush[stage];
                case ProfileGentle: return CurveGentle[stage];
                case ProfileSpike:
                    return turn == SpikeTurn ? Mathf.Max(CurveStd[stage], SpikeMultiplier)
                                             : CurveStd[stage];
                default: return CurveStd[stage];
            }
        }

        /// <summary>ADR-0009 柱2: 敵攻撃値 = round(倍率 × (基礎攻撃値 + β×ダイスロール合計))。
        /// ブロック減算前の生値。</summary>
        /// <summary>敵 (通常/エリート/ボス 共通) の攻撃値。
        ///
        /// **2026-07-28: ダイス寄与率 β (attackDiceWeight, 既定 0.3) を撤去した。**
        /// 隠し係数で出目を目減りさせるのは、 予告に出る数字と盤上のダイスが一致しなくなるため禁止。
        /// 火力が過剰なら **署名ダイスそのものをナーフする** (§13-2 の L3 学習レバー)。
        ///
        /// 算出は 3 段のみ:
        ///   1. 規定の基礎攻撃力 (baseAttack、 未指定なら threat)
        ///   2. **このターンのダイス合計値** (パッシブ補正込みの実値をそのまま加算)
        ///   3. パッシブ等の追加分・倍率・バフ/デバフ (エスカレーション段階倍率を含む)
        /// この結果がそのまま予告値になる。</summary>
        /// <param name="combatTurn">大技サイクルを数える実ターン (冷却材の遅延を受けない)。 省略時は turn。</param>
        public static int EnemyAttackValue(EnemyData e, int turn, int diceRollTotal, int combatTurn = -1)
        {
            float raw = e.EffectiveBaseAttack + diceRollTotal;
            raw *= HeavyFactor(e, combatTurn > 0 ? combatTurn : turn);
            // 挑戦デバフ〈天変地異〉(2026-08-10 リワーク): **ボスの攻撃値のみ** ×1.50。
            //   旧効果 (エスカレーション閾値の前倒し) は 3pt で −2.7pt / p=0.185 の死に段だった ──
            //   通常戦が平均 3 ターン弱で終わるため、 閾値を 2T 早めても大半の戦闘に届かない。
            //   ボス戦は長く全ターンに効くので、 ここなら確実に盤面へ出る。
            float bossMul = GameLoop.BossIds.IsBoss(e.id)
                          ? MetaProgression.MetaDebuffApplicator.GetBossAttackMultiplier(turn) : 1f;
            return Math.Max(0, Mathf.RoundToInt(GetMultiplier(e.EffectiveEscalationProfile, turn) * raw * bossMul));
        }

        // ---- 大技サイクル (EnemyData.heavyPeriod・2026-09-19) ----

        /// <summary>そのターンが大技か。 周期 N なら T = N, 2N, 3N …。</summary>
        public static bool IsHeavyTurn(EnemyData e, int combatTurn)
            => e != null && e.heavyPeriod > 0 && combatTurn > 0 && combatTurn % e.heavyPeriod == 0;

        /// <summary>次の大技まで何ターンか (0 = このターンが大技 / −1 = 大技なし)。</summary>
        public static int TurnsToHeavy(EnemyData e, int combatTurn)
        {
            if (e == null || e.heavyPeriod <= 0 || combatTurn <= 0) return -1;
            int r = combatTurn % e.heavyPeriod;
            return r == 0 ? 0 : e.heavyPeriod - r;
        }

        /// <summary>攻撃値へ掛ける大技サイクルの倍率。 大技なしの敵は 1。</summary>
        public static float HeavyFactor(EnemyData e, int combatTurn)
        {
            if (e == null || e.heavyPeriod <= 0) return 1f;
            if (IsHeavyTurn(e, combatTurn)) return e.heavyMul > 0f ? e.heavyMul : 1f;
            return e.heavyWindupMul > 0f ? e.heavyWindupMul : 1f;
        }
    }
}
