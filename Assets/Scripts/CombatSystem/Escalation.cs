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

        /// <summary>逆巻きの賽 (ADR-0015・2026-11-18) で下げた段数。 今の段階 = ターンから出た段階 (<see cref="StageOf"/>) − これ。
        /// 戦闘の始めに 0 へ戻す (<see cref="HeavyCycleOffset"/> と同じ所)。 BOT の見積もりも同じ値を読む。</summary>
        public static int StageOffset;
        /// <summary>今の段階がこれより下がらない (−4 = 段階の数の鏡)。</summary>
        public const int MinStage = -4;

        /// <summary>今の段階 (逆巻きの賽で下げた分を引いた値・<see cref="MinStage"/> 〜 4)。 マイナスの段階は自分の得 (<see cref="NegativeStageBuff"/>)、 敵の攻撃は段階 0 のまま。</summary>
        public static int EffectiveStage(int turn) => Math.Max(MinStage, StageOf(turn) - StageOffset);

        /// <summary>マイナスの段階 k (1〜4) で自分の与ダメに足す割合 (標準の曲線の鏡: −1 で +30%・−4 で +130%)。</summary>
        public static float NegativeStageBuff(int k) => k <= 0 ? 0f : CurveStd[Math.Min(k, CurveStd.Length - 1)] - 1f;

        /// <summary>いまの戦闘がボス戦か (CombatManager が戦闘開始時に立てる)。 挑戦カード「ボス戦ではエスカレーションが毎ターン進む」が読む。</summary>
        public static bool BossFight;

        /// <summary>i 段目の閾値ターン。 挑戦カード〈刻限〉: 閾値を N ターン前へ (5/10/15/20 → 4/9/14/19・3/8/13/18)、 III はボス戦で毎ターン進む (閾値 1/2/3/4)。</summary>
        private static int ThresholdAt(int i)
        {
            if (BossFight && MetaProgression.MetaDebuffApplicator.BossEscalatesEveryTurn()) return i + 1;
            int shift = MetaProgression.MetaDebuffApplicator.GetEscalationShift();
            return shift > 0 ? Math.Max(i + 1, Thresholds[i] - shift) : Thresholds[i];
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
            float curve = CurveMultiplier(profile, turn);
            int phaseTurn = turn - PhaseStartTurn + 1;
            return curve * (1f + MetaProgression.MetaDebuffApplicator.GetEnemyAttackPct() / 100f)                    // 〈猛攻〉
                 * (1f + MetaProgression.MetaDebuffApplicator.GetOpenerPct(phaseTurn, BossFight) / 100f)            // 〈先手必殺〉
                 * (1f + MetaProgression.MetaDebuffApplicator.GetWrathAttackPct(phaseTurn, BossFight) / 100f)       // コルヴェンの憤怒
                 * (1f + MetaProgression.MetaDebuffApplicator.GetEnvyPct() / 100f)                                 // ヤルノクの嫉妬 (今の HP の割合で決まる)
                 * (1f + MetaProgression.MetaDebuffApplicator.GetGreedAttackPct() / 100f);                         // ムシュファの強欲 (持っている秘宝の数で決まる)
        }

        /// <summary>いまの形態が始まったターン (CombatManager が戦闘開始時に 1、 ヴェスカの形態を替えるたびに立てる)。 「形態の最初の N ターン」を数える。</summary>
        public static int PhaseStartTurn = 1;

        /// <summary>段階の曲線だけの倍率。 挑戦カード「敵の攻撃 +N%」は <see cref="GetMultiplier"/> がここに掛ける
        /// (予告の前・EnemyAttackValue の 1 回の四捨五入の中。 BOT の先読みも同じ関数を通るので見える)。</summary>
        private static float CurveMultiplier(string profile, int turn)
        {
            int stage = Mathf.Clamp(EffectiveStage(turn), 0, CurveStd.Length - 1);   // 逆巻きの賽で下げた分も (敵は段階 0 より下がらない)
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
        /// <param name="combatTurn">大技サイクルを数える実ターン (鎮静香の遅延を受けない)。 省略時は turn。</param>
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
        /// 既定 0 (= 戦闘のターンのまま・旧い作りと同じ)。 ボスのダウンのターンがチャージか山場のターンに重なった時 (<see cref="BossDown.CancelsCycle"/>)、
        /// そのターンの頭に CombatManager がそのターン番号を入れる (ダウンが明けた次のターンが周期の 1 ターン目)。 それ以外のダウンでは触らない (2026-11-14〜)。
        /// 戦闘の始めに 0 へ戻す。 BOT の見積もりも同じ値を読む。</summary>
        public static int HeavyCycleOffset;

        /// <summary>周期を数えるターン (戦闘のターンから <see cref="HeavyCycleOffset"/> を引いた値)。</summary>
        public static int HeavyCycleTurn(int combatTurn) => combatTurn - HeavyCycleOffset;

        /// <summary>ボスの HP の割合 (灰燼の王の〈業火〉の周期が HP 50% で 4 → 3 ターンに変わる・2026-11-14)。 CombatManager がターンの頭に入れ、 戦闘の終わりに 1 へ戻す。
        /// BOT の先のターンの見込みも同じ値を読む (先のターンも今の HP の割合で数える)。</summary>
        public static float HeavyHpRatio = 1f;

        /// <summary>そのターンが大技か。 周期 N なら (起点から数えて) T = N, 2N, 3N …。 灰燼の王の〈業火〉は旧〈業火の予兆〉の数え方。</summary>
        public static bool IsHeavyTurn(EnemyData e, int combatTurn)
            => e != null && e.heavyPeriod > 0 && TurnsToHeavy(e, combatTurn) == 0;

        /// <summary>次の大技まで何ターンか (0 = このターンが大技 / −1 = 大技なし・ダウンのターン)。</summary>
        public static int TurnsToHeavy(EnemyData e, int combatTurn)
        {
            if (e == null || e.heavyPeriod <= 0) return -1;
            if (e.heavyCycle == EnemyData.HeavyCycleEmber)      // 灰燼の王の〈業火〉: 旧〈業火の予兆〉の数え方 (同じ起点 HeavyCycleOffset を読む)
                return ItemSystem.PassiveSkills.Effects.EmberOmenTiming.TurnsToOmen(combatTurn, HeavyHpRatio);
            return HeavyCycle.TurnsToHeavy(e.heavyPeriod, HeavyCycleTurn(combatTurn));   // 式は HeavyCycle (Unity なしの検算と共有)
        }

        /// <summary>いまの大技の周期 (ターン)。 灰燼の王の〈業火〉は HP で 4 / 3。 大技なしは 0。</summary>
        public static int HeavyPeriodNow(EnemyData e)
            => e == null || e.heavyPeriod <= 0 ? 0
             : e.heavyCycle == EnemyData.HeavyCycleEmber ? ItemSystem.PassiveSkills.Effects.EmberOmenTiming.PeriodFor(HeavyHpRatio) : e.heavyPeriod;

        // ---- 構え (EnemyData.guardPeriod・2026-11-04) ----

        //   構えの周期も、 大技と同じ起点 HeavyCycleOffset を読む (2026-11-13。 起点 0 なら今までどおり)。 ボスのダウンが構えか、 その 1 ターン前に重なった時だけ頭に戻る (2026-11-14・BossDown.CancelsCycle)。

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

        /// <summary>チャージのターンか (大技の 1 ターン前 = <see cref="TurnsToHeavy"/> が 1)。 画面の「次: 大技」・予告の turnsToHeavy = 1 と同じターン。
        /// 周期が 1 の敵 (毎ターン大技) にはチャージのターンが無い。</summary>
        public static bool IsChargeTurn(EnemyData e, int combatTurn)
            => e != null && HeavyPeriodNow(e) >= 2 && TurnsToHeavy(e, combatTurn) == 1;

        /// <summary>このターンはチャージで、 敵が攻撃しないか (2026-11-14・作者の決定「チャージは攻撃しないでいい」・測定の口 <c>GameHooks.HeavyChargeNoAttack</c>)。
        /// 真なら <see cref="HeavyFactor"/> が 0 (攻撃値 = 予告 = 0) で、 CombatManager はダウンと同じ「攻撃しない」の枝を通す。</summary>
        public static bool IsChargeNoAttack(EnemyData e, int combatTurn)
            => GameLoop.GameHooks.HeavyChargeNoAttack && IsChargeTurn(e, combatTurn);

        /// <summary>攻撃値へ掛ける大技サイクルの倍率。 大技なしの敵は 1。
        /// 周期の 3 つのターン: 大技 = <see cref="EnemyData.heavyMul"/> (+ 挑戦カードの大技の倍率 +N)・チャージ = 0 (攻撃しない・<see cref="IsChargeNoAttack"/>)・
        /// 小技 (どちらでもないターン) = <see cref="EnemyData.heavyLightMul"/>。 口でチャージの攻撃を戻した時 (旧い作り) は、 チャージも小技の倍率。</summary>
        public static float HeavyFactor(EnemyData e, int combatTurn)
        {
            if (e == null || e.heavyPeriod <= 0) return 1f;
            if (TenyoHeavy.NoAttackAt(e, combatTurn)) return 0f;      // ヴェスカの天与の盾・指輪 (2026-11-15): 攻撃しない大技
            int toHeavy = TurnsToHeavy(e, combatTurn);
            float heavyWithBonus = toHeavy == 0 && e.heavyMul > 0f
                ? e.heavyMul + MetaProgression.MetaDebuffApplicator.GetHeavyMulBonus() : 0f;   // 挑戦カード: 大技の倍率 +N
            return HeavyCycle.FactorAt(HeavyPeriodNow(e), toHeavy, e.heavyMul, heavyWithBonus, e.heavyLightMul, GameLoop.GameHooks.HeavyChargeNoAttack);
        }
    }
}
