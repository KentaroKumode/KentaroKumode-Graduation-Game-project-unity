using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25) の表 (デバフ) と七つの大罪の効果を各システムへ届ける中央ヘルパー。 受けたカードの値は <see cref="ChallengeCards"/> が持ち、
    /// ここは「その値を各システムが使う形」に直すだけ。 受けていない札の値は 0 (= 効果なし)。
    /// 戦闘の撃の輪・端子の解決 (本体と BOT が同じ式を通る所) が読む値は <see cref="CombatSystem.CardRules"/> が戦闘の始めに写す。
    /// <para>2026-11-22 フルリワーク: 〈最後の審判〉〈先手を取られる〉〈怒り〉〈蝕み〉〈敵のシールド〉〈敵の防御〉〈エスカレーションの上がり幅〉と旧大罪の混ぜた効果を撤去 (§24)。</para>
    /// </summary>
    public static class MetaDebuffApplicator
    {
        private static float CardV(string kind) => ChallengeCards.Value(kind);
        private static int CardI(string kind) => (int)ChallengeCards.Value(kind);

        // ── 敵の攻撃 (すべて予告の前に掛かる・Escalation.GetMultiplier) ──

        /// <summary>〈猛攻〉: 敵の攻撃の上乗せ (%)。</summary>
        public static float GetEnemyAttackPct() => CardV(ChallengeCards.EnemyAtk);
        /// <summary>〈先手必殺〉: ボス戦の各形態の最初のターンの敵の攻撃の上乗せ (%)。</summary>
        public static float GetOpenerPct(int phaseTurn, bool bossFight) => bossFight && phaseTurn == 1 ? CardV(ChallengeCards.Opener) : 0f;
        /// <summary>コルヴェンの憤怒: ボス戦の各形態の最初のターンの敵の攻撃の上乗せ (%)。</summary>
        public static float GetWrathAttackPct(int phaseTurn, bool bossFight) => bossFight && phaseTurn == 1 ? ChallengeCards.Value2(ChallengeCards.SinWrath) : 0f;
        /// <summary>〈刻限〉: エスカレーションの段の閾値を前へずらすターン数。</summary>
        public static int GetEscalationShift() => CardI(ChallengeCards.Deadline);
        /// <summary>〈刻限〉III: ボス戦ではエスカレーションが毎ターン進むか。</summary>
        public static bool BossEscalatesEveryTurn() => ChallengeCards.Value2(ChallengeCards.Deadline) > 0f;
        /// <summary>〈致命的な一撃〉: 大技の倍率の上乗せ。</summary>
        public static float GetHeavyMulBonus() => CardV(ChallengeCards.Heavy);
        /// <summary>〈連撃〉: 2 回目の攻撃の威力 (%)。 0 = 連撃しない。</summary>
        public static float GetTwinStrikePct() => CardV(ChallengeCards.Twin);

        // ── 七つの大罪 (IV・5 層の間だけ・GAME.md §15-4) ──

        /// <summary>カイロスの傲慢 ── 防御の端子を挿した出目の削り (%)。</summary>
        public static float GetSinBlockCutPct() => CardV(ChallengeCards.SinPride);
        /// <summary>カイロスの傲慢 ── シールドの獲得の削り (%)。</summary>
        public static float GetSinShieldCutPct() => ChallengeCards.Value2(ChallengeCards.SinPride);
        /// <summary>ヤルノクの嫉妬 ── 残り HP の割合がこちらのほうが敵より高い間の、 敵の攻撃の上乗せ (%)。</summary>
        public static float GetEnvyPct()
        {
            float v = CardV(ChallengeCards.SinEnvy);
            if (v <= 0f) return 0f;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null || ctx.playerMaxHP <= 0 || ctx.enemyMaxHP <= 0) return 0f;
            return ctx.playerCurrentHP * (long)ctx.enemyMaxHP > ctx.enemyCurrentHP * (long)ctx.playerMaxHP ? v : 0f;
        }
        /// <summary>ムシュファの強欲 ── 持っている秘宝 1 つにつきの敵の攻撃の上乗せ (%) × 秘宝の数 (2026-11-23・Escalation.GetMultiplier で予告の前に掛ける)。 受けていなければ 0。</summary>
        public static float GetGreedAttackPct()
        {
            float v = CardV(ChallengeCards.SinGreed);
            if (v <= 0f) return 0f;
            int n = GameManager.Instance?.Run?.ownedPassiveItems?.Count ?? 0;
            return v * n;
        }
        /// <summary>コルヴェンの憤怒 ── ボスの各形態の最初のターンに削る現在 HP (%)。 受けていなければ 0。</summary>
        public static float GetWrathHpCutPct() => CardV(ChallengeCards.SinWrath);
        /// <summary>クェシナの怠惰 ── 全部の出目から引く量 (下限 1)。</summary>
        public static int GetSlothDicePenalty() => CardI(ChallengeCards.SinSloth);
        /// <summary>ムシュファの強欲 ── 店を使えない (受けるのは 5 層の入口なので、 5 層の店だけ)。</summary>
        public static bool ShopsClosedBySin() => ChallengeCards.Held(ChallengeCards.SinGreed) != null;
        /// <summary>クァディルの色欲 ── 秘宝の見送り換金ができない。</summary>
        public static bool SkipCashBlocked() => ChallengeCards.Held(ChallengeCards.SinLust) != null;

        /// <summary>各戦闘の 1 ターン目から数えて <paramref name="battleTurn"/> ターン目に止める手札の端子の数 (〈目詰まり〉とクェシナの怠惰 (毎ターン) の多い方・CombatManager.DrawTerminalHand)。</summary>
        public static int GetClogStops(int battleTurn)
        {
            int n = 0;
            var c = ChallengeCards.Held(ChallengeCards.Clog);
            if (c != null && battleTurn >= 1 && battleTurn <= (int)c.value2) n = (int)c.value;
            if (battleTurn >= 1) n = System.Math.Max(n, (int)ChallengeCards.Value2(ChallengeCards.SinSloth));   // 怠惰は毎ターン (2026-11-23・answer20)
            return n;
        }

        /// <summary>〈徒労〉の戦闘のデバフ (%・2026-11-14・GameHooks.ToilDamageDownPct)。 〈徒労〉を持っていなければ 0。</summary>
        public static int GetToilDamageCutPct()
        {
            int pct = GameHooks.ToilDamageDownPct;
            if (pct <= 0) return 0;
            var d = GameManager.Instance?.Run?.timedDebuffs;
            bool has = d != null && d.TryGetValue(EventSystem.TimedEffects.BattleBoonRule.ToilName, out int n) && n > 0;
            return EventSystem.TimedEffects.BattleBoonRule.ToilDamageDownPct(has, pct);
        }

        /// <summary>与ダメージの削りの枠 (%): 〈徒労〉だけ (2026-11-22 に怠惰の与ダメ −30% を撤去・BattleBoonRule.OutgoingCutPct の枠は残す)。</summary>
        public static int GetOutgoingCutPct() => EventSystem.TimedEffects.BattleBoonRule.OutgoingCutPct(0, GetToilDamageCutPct());

        /// <summary>プレイヤーが敵に与えるダメージ −N% (主攻撃・毒・出血・爆発・固定ダメ・反射・品の直接、 全部)。 〈徒労〉の戦闘のデバフ (2026-11-14) の枠。 無ければ素通し。</summary>
        public static int ApplyOutgoingCut(int dmg)
            => dmg > 0 ? EventSystem.TimedEffects.BattleBoonRule.ApplyOutgoingCut(dmg, GetOutgoingCutPct()) : dmg;

        // ── 敵の耐久 ──

        /// <summary>〈厚い皮〉: 敵の最大 HP 倍率 (連戦の全段)。</summary>
        public static float GetEnemyHpMultiplier() => 1f + CardV(ChallengeCards.EnemyHp) / 100f;
        /// <summary>〈鋼の皮膚〉: 敵が各戦闘 1 回だけ致命傷を HP1 で耐えるか。 発動ターンの敵攻撃は行わない。</summary>
        public static bool EnemySurvivesFirstLethal() => ChallengeCards.Held(ChallengeCards.Steel) != null;
        /// <summary>〈鋼の皮膚〉II のボスの最大 HP 倍率。 ボスの +N% は〈厚い皮〉と同じ溜まりに足す (掛け算にしない)。 敵の HP 倍率の後に掛かるので比で返す。</summary>
        public static float GetBossHpMultiplier()
        {
            float boss = ChallengeCards.Value2(ChallengeCards.Steel);
            if (boss <= 0f) return 1f;
            float hp = CardV(ChallengeCards.EnemyHp);
            return (100f + hp + boss) / (100f + hp);
        }
        /// <summary>〈再生〉: 敵が毎ターン回復する、 最大 HP に対する割合 (%)。</summary>
        public static float GetEnemyRegenPct() => CardV(ChallengeCards.Regen);
        /// <summary>〈受身〉: ボスのダウン中に受けるダメージの倍率 (受けていなければ <see cref="CombatSystem.BossDown.TakenMul"/>)。</summary>
        public static float GetDownTakenMul() => ChallengeCards.Held(ChallengeCards.Ukemi) is ChallengeCards.Card c ? c.value : CombatSystem.BossDown.TakenMul;
        /// <summary>〈受身〉III: ボスの目盛りの間隔の上乗せ (%)。</summary>
        public static float GetDownNotchSpacingPct() => ChallengeCards.Value2(ChallengeCards.Ukemi);

        // ── 手 ──

        /// <summary>〈重い賽〉: 振り直し 1 回の充電の上乗せ (無料の振り直しには足さない)。</summary>
        public static int GetRerollSurcharge() => CardI(ChallengeCards.HeavyDice);
        /// <summary>〈浅い鞄〉II: 消耗品の同時所持の上限 (0 = 制限なし)。</summary>
        public static int GetConsumableCarryCap() => (int)ChallengeCards.Value2(ChallengeCards.Bag);
        /// <summary>〈浅い鞄〉: 1 戦闘で使える消耗品の数 (0 = 制限なし)。</summary>
        public static int GetBattleConsumableCap() => CardI(ChallengeCards.Bag);
        /// <summary>あらゆる回復に掛かる倍率 (消耗品・前哨基地・休憩・戦闘中・層の入口の全回復)。 〈深い傷〉／〈手当て〉／トゥルハドの暴食。</summary>
        public static float GetHealMultiplier()
            => (1f - CardV(ChallengeCards.Heal) / 100f)
             * (1f + CardV(ChallengeCards.HealUp) / 100f)
             * (1f - CardV(ChallengeCards.SinGluttony) / 100f);

        // ── 経済 ──

        /// <summary>〈通行料〉: 層を移るたびに失う所持金の割合。</summary>
        public static float GetFloorTollRatio() => CardV(ChallengeCards.Money) / 100f;
        /// <summary>〈暴騰〉: 店の品物の値段の倍率。</summary>
        public static float GetShopPriceMultiplier() => 1f + CardV(ChallengeCards.Price) / 100f;

        /// <summary>秘宝の見送り換金の額に挑戦カードを掛ける: 色欲 = 0 (見送れない) / 〈買い叩き〉−N% (四捨五入) / 〈目利き〉+N% (切り上げ)。</summary>
        public static int ApplySkipGold(int gold)
        {
            if (gold <= 0) return gold;
            if (SkipCashBlocked()) return 0;
            float low = CardV(ChallengeCards.Lowball), up = CardV(ChallengeCards.Appraise);
            if (low > 0f) gold = UnityEngine.Mathf.RoundToInt(gold * (1f - low / 100f));
            if (up > 0f) gold = UnityEngine.Mathf.CeilToInt(gold * (1f + up / 100f));
            int meta = MetaBuffApplicator.GetSkipCashPct();      // 整備盤〈端数回収回路〉〈商隊ネットワーク〉 (2026-11-23・切り上げ)
            if (meta > 0) gold = UnityEngine.Mathf.CeilToInt(gold * (1f + meta / 100f));
            return gold;
        }
    }
}
