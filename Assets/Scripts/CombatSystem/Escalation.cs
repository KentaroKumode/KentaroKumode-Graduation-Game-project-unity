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
            int s = 0;
            for (int i = 0; i < Thresholds.Length; i++)
                if (turn > ThresholdAt(i)) s++;
            return s;
        }

        /// <summary>いまの戦闘がボス戦か (CombatManager が戦闘開始時に立てる)。 挑戦カード「ボス戦ではエスカレーションが毎ターン進む」が読む。</summary>
        public static bool BossFight;

        /// <summary>i 段目の閾値ターン。 挑戦カードのボス戦の毎ターン進行 (閾値 1/2/3/4) を反映する。</summary>
        private static int ThresholdAt(int i)
        {
            if (BossFight && MetaProgression.MetaDebuffApplicator.BossEscalatesEveryTurn()) return i + 1;
            return Thresholds[i];
        }

        /// <summary>次の段階閾値ターン。最終段階なら -1 (計器盤の残ターン表示用)。</summary>
        public static int NextThresholdTurn(int turn)
        {
            for (int i = 0; i < Thresholds.Length; i++)
            {
                int th = ThresholdAt(i);
                if (turn <= th) return th;
            }
            return -1;
        }

        /// <summary>プロファイル×ターンの攻撃値倍率。</summary>
        public static float GetMultiplier(string profile, int turn)
        {
            // 挑戦カード (§23-25): すべて予告の前に掛かる (ブロックで受けられる・BOT の先読みにも見える)
            //   エスカレーションの上がり幅 +N%: 段の倍率の 1 を超えた分だけを伸ばす (段 0 は 1 のまま)
            float curve = CurveMultiplier(profile, turn);
            float rise = MetaProgression.MetaDebuffApplicator.GetEscalationRisePct();
            if (rise > 0f && curve > 1f) curve = 1f + (curve - 1f) * (1f + rise / 100f);
            int phaseTurn = turn - PhaseStartTurn + 1;
            return curve * (1f + MetaProgression.MetaDebuffApplicator.GetEnemyAttackPct() / 100f)
                 * (1f + MetaProgression.MetaDebuffApplicator.GetAmbushPct(phaseTurn) / 100f)                // 〈先手を取られる〉
                 * (1f + MetaProgression.MetaDebuffApplicator.GetRagePct(phaseTurn) / 100f)                  // 〈怒り〉
                 * (1f + MetaProgression.MetaDebuffApplicator.GetOpenerPct(phaseTurn, BossFight) / 100f)     // 〈開幕の大技〉
                 * (1f + MetaProgression.MetaDebuffApplicator.GetEnvyPct() / 100f);                         // 大罪:嫉妬 (今の HP の割合で決まる)
        }

        /// <summary>いまの形態が始まったターン (CombatManager が戦闘開始時に 1、 ヴェスカの形態を替えるたびに立てる)。 「形態の最初の N ターン」を数える。</summary>
        public static int PhaseStartTurn = 1;

        /// <summary>段階の曲線だけの倍率。 挑戦カード「敵の攻撃 +N%」は <see cref="GetMultiplier"/> がここに掛ける
        /// (予告の前・EnemyAttackValue の 1 回の四捨五入の中。 BOT の先読みも同じ関数を通るので見える)。</summary>
        private static float CurveMultiplier(string profile, int turn)
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
            raw *= GuardAttackFactor(e, combatTurn > 0 ? combatTurn : turn);
            raw *= OpeningFactor(e, combatTurn > 0 ? combatTurn : turn);
            return Math.Max(0, Mathf.RoundToInt(GetMultiplier(e.EffectiveEscalationProfile, turn) * raw));
        }

        // ---- 大技サイクル (EnemyData.heavyPeriod・2026-09-19) ----

        /// <summary>敵の周期の起点のずれ (2026-11-13・ボスのダウン)。 大技・構え・灰燼の王〈業火の予兆〉の周期は <c>戦闘のターン − これ</c> で数える。
        /// 既定 0 (= 戦闘のターンのまま・旧い作りと同じ)。 ボスがダウンしたターンの頭に CombatManager がそのターン番号を入れる
        /// (ダウンが明けた次のターンが周期の 1 ターン目)。 戦闘の始めに 0 へ戻す。 BOT の見積もりも同じ値を読む。</summary>
        public static int HeavyCycleOffset;

        /// <summary>周期を数えるターン (戦闘のターンから <see cref="HeavyCycleOffset"/> を引いた値)。</summary>
        public static int HeavyCycleTurn(int combatTurn) => combatTurn - HeavyCycleOffset;

        /// <summary>そのターンが大技か。 周期 N なら (起点から数えて) T = N, 2N, 3N …。</summary>
        public static bool IsHeavyTurn(EnemyData e, int combatTurn)
        {
            int c = HeavyCycleTurn(combatTurn);
            return e != null && e.heavyPeriod > 0 && c > 0 && c % e.heavyPeriod == 0;
        }

        /// <summary>次の大技まで何ターンか (0 = このターンが大技 / −1 = 大技なし・ダウンのターン)。</summary>
        public static int TurnsToHeavy(EnemyData e, int combatTurn)
        {
            int c = HeavyCycleTurn(combatTurn);
            if (e == null || e.heavyPeriod <= 0 || c <= 0) return -1;
            int r = c % e.heavyPeriod;
            return r == 0 ? 0 : e.heavyPeriod - r;
        }

        // ---- 構え (EnemyData.guardPeriod・2026-11-04) ----

        //   構えの周期も、 ボスのダウンが明けたら頭から (大技と同じ起点 HeavyCycleOffset を読む・2026-11-13。 起点 0 なら今までどおり)。

        /// <summary>そのターンに構えているか。 周期 N なら (起点から数えて) T = N, 2N …。</summary>
        public static bool IsGuardTurn(EnemyData e, int combatTurn)
        {
            int c = HeavyCycleTurn(combatTurn);
            return e != null && e.guardPeriod > 0 && c > 0 && c % e.guardPeriod == 0;
        }

        /// <summary>構えを解いた隙のターン (構えの次のターン)。 防御 0。</summary>
        public static bool IsAfterGuardTurn(EnemyData e, int combatTurn)
        {
            int c = HeavyCycleTurn(combatTurn);
            return e != null && e.guardPeriod > 0 && c > 1 && (c - 1) % e.guardPeriod == 0;
        }

        /// <summary>次の構えまで何ターンか (0 = このターンが構え / −1 = 構えない・ダウンのターン)。</summary>
        public static int TurnsToGuard(EnemyData e, int combatTurn)
        {
            int c = HeavyCycleTurn(combatTurn);
            if (e == null || e.guardPeriod <= 0 || c <= 0) return -1;
            int r = c % e.guardPeriod;
            return r == 0 ? 0 : e.guardPeriod - r;
        }

        /// <summary>構えのターンの攻撃倍率。 構えない敵・構えていないターンは 1。</summary>
        public static float GuardAttackFactor(EnemyData e, int combatTurn)
            => IsGuardTurn(e, combatTurn) && e.guardAttackMul > 0f ? e.guardAttackMul : 1f;

        /// <summary>先手 (最初の数ターンだけ攻撃が重い) の倍率。 なしの敵は 1。</summary>
        public static float OpeningFactor(EnemyData e, int combatTurn)
            => e != null && e.openingTurns > 0 && combatTurn > 0 && combatTurn <= e.openingTurns && e.openingAttackMul > 0f ? e.openingAttackMul : 1f;

        /// <summary>攻撃値へ掛ける大技サイクルの倍率。 大技なしの敵は 1。</summary>
        public static float HeavyFactor(EnemyData e, int combatTurn)
        {
            if (e == null || e.heavyPeriod <= 0) return 1f;
            if (IsHeavyTurn(e, combatTurn)) return e.heavyMul > 0f ? e.heavyMul + MetaProgression.MetaDebuffApplicator.GetHeavyMulBonus() : 1f;   // 挑戦カード: 大技の倍率 +N
            return e.heavyWindupMul > 0f ? e.heavyWindupMul : 1f;
        }
    }
}
