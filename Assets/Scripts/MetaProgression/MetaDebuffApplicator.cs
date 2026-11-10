using GameLoop;

namespace MetaProgression
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25) の効果を各システムへ届ける中央ヘルパー。 受けたカードの値は <see cref="ChallengeCards"/> が持ち、
    /// ここは「その値を各システムが使う形」に直すだけ。 受けていない札の値は 0 (= 効果なし)。
    /// <para>2026-11-10: 旧の配点方式 (§15-2・軸ごとに Tier を選んで点を積む) と、 その軸の効果・計装・旧〈七つの大罪〉の付与を撤去した (#9・§24)。</para>
    /// </summary>
    public static class MetaDebuffApplicator
    {
        private static float CardV(string kind) => ChallengeCards.Value(kind);
        private static int CardI(string kind) => (int)ChallengeCards.Value(kind);

        // ── 敵の攻撃 (すべて予告の前に掛かる・Escalation.GetMultiplier) ──

        /// <summary>敵の攻撃の上乗せ (%)。 〈最後の審判〉の刻限の後は、 その札の値を同じ溜まりに足す。</summary>
        public static float GetEnemyAttackPct()
        {
            float p = CardV(ChallengeCards.EnemyAtk);
            if (ChallengeCards.Held(ChallengeCards.Judge) != null && IsPastJudgmentDeadline(GameManager.Instance?.Run)) p += CardV(ChallengeCards.Judge);
            return p;
        }
        /// <summary>〈先手を取られる〉: 戦闘 (形態) の最初の数ターン (札の 2 つ目の量) の敵の攻撃の上乗せ (%)。 <paramref name="phaseTurn"/> は形態の中の何ターン目か。</summary>
        public static float GetAmbushPct(int phaseTurn)
        {
            var c = ChallengeCards.Held(ChallengeCards.Ambush);
            return c != null && phaseTurn >= 1 && phaseTurn <= (int)c.value2 ? c.value : 0f;
        }
        /// <summary>〈怒り〉: 形態の中で 1 ターン進むごとの敵の攻撃の上乗せ (%・加算)。 1 ターン目は 0。</summary>
        public static float GetRagePct(int phaseTurn) => phaseTurn > 1 ? CardV(ChallengeCards.Rage) * (phaseTurn - 1) : 0f;
        /// <summary>〈開幕の大技〉: ボス戦の各形態の最初のターンの敵の攻撃の上乗せ (%)。</summary>
        public static float GetOpenerPct(int phaseTurn, bool bossFight) => bossFight && phaseTurn == 1 ? CardV(ChallengeCards.Opener) : 0f;
        /// <summary>エスカレーションの上がり幅の上乗せ (%)。 段の倍率の 1 を超えた分に掛ける。</summary>
        public static float GetEscalationRisePct() => CardV(ChallengeCards.Esc);
        /// <summary>ボス戦ではエスカレーションが毎ターン進むか。</summary>
        public static bool BossEscalatesEveryTurn() => ChallengeCards.Held(ChallengeCards.BossEsc) != null;
        /// <summary>大技の倍率の上乗せ。</summary>
        public static float GetHeavyMulBonus() => CardV(ChallengeCards.Heavy);
        /// <summary>〈連撃〉: 2 回目の攻撃の威力 (%)。 0 = 連撃しない。</summary>
        public static float GetTwinStrikePct() => CardV(ChallengeCards.Twin);

        // ── 七つの大罪 (IV 固有・2026-11-10・#93・GAME.md §15-4) ──

        /// <summary>大罪:傲慢 ── ブロックに挿した出目の削り (%)。 シールドの獲得の削りは <see cref="ChallengeCards.SinPrideShieldCutPct"/>。</summary>
        public static float GetSinBlockCutPct() => CardV(ChallengeCards.SinPride);
        /// <summary>大罪:嫉妬 ── 残り HP の割合がこちらのほうが敵より高い間の、 敵の攻撃の上乗せ (%)。</summary>
        public static float GetEnvyPct()
        {
            float v = CardV(ChallengeCards.SinEnvy);
            if (v <= 0f) return 0f;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx == null || ctx.playerMaxHP <= 0 || ctx.enemyMaxHP <= 0) return 0f;
            return ctx.playerCurrentHP * (long)ctx.enemyMaxHP > ctx.enemyCurrentHP * (long)ctx.playerMaxHP ? v : 0f;
        }
        /// <summary>大罪:憤怒 を受けているか (ボスの各形態の最初のターン)。</summary>
        public static bool HasSinWrath => CardV(ChallengeCards.SinWrath) > 0f;
        /// <summary>大罪:怠惰 ── 全部の出目から引く量 (下限 1)。</summary>
        public static int GetSlothDicePenalty() => CardI(ChallengeCards.SinSloth);

        // ── 敵の耐久 ──

        /// <summary>〈厚い皮〉: 敵の最大 HP 倍率 (連戦の全段)。</summary>
        public static float GetEnemyHpMultiplier() => 1f + CardV(ChallengeCards.EnemyHp) / 100f;
        /// <summary>〈鋼の皮膚〉: 敵が各戦闘 1 回だけ致命傷を HP1 で耐えるか。 発動ターンの敵攻撃は行わない。</summary>
        public static bool EnemySurvivesFirstLethal() => ChallengeCards.Held(ChallengeCards.Steel) != null;
        /// <summary>〈鋼の皮膚〉のボスの最大 HP 倍率。 ボスの +20% は〈厚い皮〉と同じ溜まりに足す (掛け算にしない)。 敵の HP 倍率の後に掛かるので比で返す。</summary>
        public static float GetBossHpMultiplier()
        {
            if (ChallengeCards.Held(ChallengeCards.Steel) == null) return 1f;
            float hp = CardV(ChallengeCards.EnemyHp);
            return (100f + hp + ChallengeCards.SteelBossHpPct) / (100f + hp);
        }
        /// <summary>〈再生〉: 敵が毎ターン回復する、 最大 HP に対する割合 (%)。</summary>
        public static float GetEnemyRegenPct() => CardV(ChallengeCards.Regen);
        /// <summary>〈敵のシールド〉: 敵が毎ターン張り直すシールドの、 最大 HP に対する割合 (%)。</summary>
        public static float GetEnemyShieldPct() => CardV(ChallengeCards.EnemyShield);
        /// <summary>敵の防御の上乗せ (連戦の全段)。 札は保留中 (#2)。</summary>
        public static int GetEnemyDefenseBonus() => CardI(ChallengeCards.EnemyDef);

        // ── 手 ──

        /// <summary>〈蝕み〉: ターンの始めに受ける軽減できないダメージの、 自分の最大 HP に対する割合 (%)。</summary>
        public static float GetErodePct() => CardV(ChallengeCards.Erode);
        /// <summary>消耗品の同時所持の上限 (0 = 制限なし)。</summary>
        public static int GetConsumableCarryCap() => CardI(ChallengeCards.Bag);
        /// <summary>1 戦闘で使える消耗品の数 (0 = 制限なし)。</summary>
        public static int GetBattleConsumableCap() => CardI(ChallengeCards.UseCap);
        /// <summary>あらゆる回復に掛かる倍率 (消耗品・前哨基地・休憩・戦闘中・層の入口の全回復)。 回復 −N% ／ バフ「回復 +N%」／ 大罪:暴食。</summary>
        public static float GetHealMultiplier()
            => (1f - CardV(ChallengeCards.Heal) / 100f)
             * (1f + CardV(ChallengeCards.HealUp) / 100f)
             * (1f - CardV(ChallengeCards.SinGluttony) / 100f);

        // ── 経済 ──

        /// <summary>層を移るたびに失う所持金の割合。</summary>
        public static float GetFloorTollRatio() => CardV(ChallengeCards.Money) / 100f;
        /// <summary>店の品物の値段の倍率。</summary>
        public static float GetShopPriceMultiplier() => 1f + CardV(ChallengeCards.Price) / 100f;

        // ── 〈最後の審判〉(刻限: ラン累計の戦闘ターン) ──

        /// <summary>刻限のターン (札の 2 つ目の量)。 0 = 受けていない。</summary>
        public static int GetJudgmentTurnThreshold()
            => ChallengeCards.Held(ChallengeCards.Judge) is ChallengeCards.Card jc ? (int)jc.value2 : 0;

        /// <summary>刻限を越えているか。</summary>
        public static bool IsPastJudgmentDeadline(RunState run)
        {
            int th = GetJudgmentTurnThreshold();
            return th > 0 && run != null && run.totalCombatTurns >= th;
        }
    }
}
