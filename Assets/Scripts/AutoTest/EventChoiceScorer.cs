using System.Collections.Generic;
using EventSystem;
using GameLoop;
using MapSystem;

namespace AutoTest
{
    /// <summary>
    /// BOT 用イベント選択肢評価器。
    /// 各選択肢の効果を数値スコアに変換し、現在の RunState（HP/空腹/コイン）で文脈補正する。
    ///
    /// 設計方針:
    ///  - 「100G+カルマ vs なにもなし」は通常 100G を取る（ゴールド価値 > カルマ1の期待コスト）
    ///  - 現在HPが低い、被ダメで致死圏ならHP損失を強く忌避
    ///  - 同様に空腹度・コイン量で文脈補正
    ///  - PriorityItemList の S/A 級は加点
    ///  - Probability は子分岐の期待値（重み平均）を返す
    /// </summary>
    public static class EventChoiceScorer
    {
        // ===== 基本ウェイト =====
        const float W_GOLD       = 0.55f;
        const float W_HP_GAIN    = 1.6f;
        const float W_HP_LOSS    = 1.8f;
        const float W_MAX_HP     = 2.6f;
        const float W_HUNGER     = 0.35f;
        const float W_MATERIAL   = 3.0f;
        const float V_KARMA      = -7.0f;   // カルマ1あたり: 期待される将来コスト
        const float V_TIMED_BUFF = +5.0f;
        const float V_TIMED_DEB  = -6.0f;
        const float V_PERM_DEB   = -28.0f;
        const float V_GAIN_PASS  = +9.0f;   // ランダムパッシブ獲得
        const float V_GAIN_CONS  = +5.0f;
        const float V_GAIN_FLAG  = +30.0f;
        const float V_DISC_FLAG  = -22.0f;
        const float V_DISC_PASS  = -8.0f;
        const float V_ARMOR_DUR  = -3.5f;
        const float V_COMBAT     = -8.0f;
        const float V_ELITE      = -22.0f;

        /// <summary>選択肢を評価。複数効果は加算、Probability は期待値。</summary>
        public static float Score(EventChoice choice, RunState run)
        {
            if (choice?.effects == null || choice.effects.Count == 0) return 0f;
            float total = 0f;
            foreach (var e in choice.effects)
                total += ScoreEffect(e, run);
            return total;
        }

        private static float ScoreEffect(EventEffect e, RunState run)
        {
            if (e == null) return 0f;

            // Probability: 子枝の期待値
            if (e.type == EventEffectType.Probability)
            {
                if (e.branches == null || e.branches.Count == 0) return 0f;
                float exp = 0f, wsum = 0f;
                for (int i = 0; i < e.branches.Count; i++)
                {
                    float w = (e.branchWeights != null && i < e.branchWeights.Count) ? e.branchWeights[i] : 1f;
                    wsum += w;
                    float sub = 0f;
                    foreach (var c in e.branches[i]) sub += ScoreEffect(c, run);
                    exp += sub * w;
                }
                return wsum > 0f ? exp / wsum : 0f;
            }

            int hp     = run != null ? run.playerHP : 30;
            int maxHP  = run != null ? UnityEngine.Mathf.Max(1, run.playerMaxHP) : 30;
            float hpPct = (float)hp / maxHP;
            int coins  = run != null ? run.coins : 0;
            var hungerSys = MapManager.Instance != null ? MapManager.Instance.Hunger : null;
            int hunger = hungerSys != null ? hungerSys.Current : 50;
            int hungerMax = hungerSys != null ? UnityEngine.Mathf.Max(1, hungerSys.Max) : 100;
            float hungerPct = (float)hunger / hungerMax;

            switch (e.type)
            {
                case EventEffectType.None: return 0f;

                case EventEffectType.GoldDelta:
                {
                    // ゴールド：少額・余ってない時は高評価、たんまり持ってる時は減価
                    float w = W_GOLD;
                    if (coins < 8) w *= 1.6f;
                    else if (coins > 40) w *= 0.7f;
                    return e.amount * w;
                }

                case EventEffectType.HpDelta:
                {
                    if (e.amount >= 0)
                    {
                        // 治癒：HP低いほど価値大
                        float w = W_HP_GAIN;
                        if (hpPct < 0.35f) w *= 2.2f;
                        else if (hpPct > 0.85f) w *= 0.4f; // 満タンに近いと無駄
                        // 最大HPを超えた分は無価値
                        int eff = UnityEngine.Mathf.Min(e.amount, UnityEngine.Mathf.Max(0, maxHP - hp));
                        return eff * w;
                    }
                    else
                    {
                        // 被ダメ：HP低いほど忌避強い、致死域なら極端
                        int dmg = -e.amount;
                        float w = W_HP_LOSS;
                        if (hpPct < 0.35f) w *= 2.5f;
                        if (hp - dmg <= 0) w *= 6.0f;       // 直接死
                        else if (hp - dmg <= maxHP * 0.15f) w *= 3.0f; // 残り15%以下で危険
                        return -dmg * w;
                    }
                }

                case EventEffectType.HpFullHeal:
                {
                    int gain = UnityEngine.Mathf.Max(0, maxHP - hp);
                    float w = W_HP_GAIN;
                    if (hpPct < 0.35f) w *= 2.2f;
                    return gain * w;
                }

                case EventEffectType.HpSetTo:
                {
                    int delta = e.amount - hp;
                    if (delta >= 0) return delta * W_HP_GAIN;
                    int dmg = -delta;
                    float w = W_HP_LOSS;
                    if (e.amount <= 0) w *= 8f;
                    return -dmg * w;
                }

                case EventEffectType.MaxHpDelta:
                    return e.amount * W_MAX_HP;

                case EventEffectType.HungerDelta:
                {
                    if (e.amount >= 0)
                    {
                        float w = W_HUNGER;
                        if (hungerPct < 0.25f) w *= 2.0f;
                        return e.amount * w;
                    }
                    else
                    {
                        float w = W_HUNGER;
                        if (hungerPct < 0.30f) w *= 2.5f;
                        return e.amount * w; // 負
                    }
                }

                case EventEffectType.KarmaGain:
                    // カルマ獲得は基本マイナス（永続デバフのトリガ）
                    return (e.amount > 0 ? e.amount : 1) * V_KARMA;

                case EventEffectType.MaterialDelta:
                    return e.amount * W_MATERIAL;

                case EventEffectType.ArmorDurabilityLoss:
                    return V_ARMOR_DUR;

                case EventEffectType.TimedBuff:    return V_TIMED_BUFF;
                case EventEffectType.TimedDebuff:  return V_TIMED_DEB;
                case EventEffectType.PermanentDebuff: return V_PERM_DEB;

                case EventEffectType.GainPassiveItem:
                case EventEffectType.GainSpecificItem:
                {
                    // 具体ID指定があれば優先度で加算
                    if (!string.IsNullOrEmpty(e.param))
                    {
                        if (LearnedPriorityProvider.IsSRank(e.param)) return 35f;
                        if (LearnedPriorityProvider.IsARank(e.param)) return 22f;
                        return 12f;
                    }
                    return V_GAIN_PASS;
                }
                case EventEffectType.GainConsumableItem:
                {
                    if (!string.IsNullOrEmpty(e.param) && LearnedPriorityProvider.IsSRank(e.param)) return 20f;
                    return V_GAIN_CONS;
                }

                case EventEffectType.GainFlag:
                {
                    // 既所持なら冗長
                    if (run?.ownedFlags != null && !string.IsNullOrEmpty(e.param) && run.ownedFlags.Contains(e.param))
                        return V_GAIN_FLAG * 0.3f;
                    return V_GAIN_FLAG;
                }
                case EventEffectType.DiscardFlag:    return V_DISC_FLAG;
                case EventEffectType.DiscardPassiveItem: return V_DISC_PASS;

                case EventEffectType.EnterCombat:
                {
                    if (hpPct < 0.45f) return V_COMBAT * 2.0f;
                    return V_COMBAT;
                }
                case EventEffectType.EnterEliteCombat:
                {
                    if (hpPct < 0.65f) return V_ELITE * 1.6f;
                    return V_ELITE;
                }
                case EventEffectType.RandomEvent:
                    return 0f; // 分散大なので中立

                default:
                    return 0f;
            }
        }

        /// <summary>L1.5 学習リフトの加算重み。 観測 lift1点 ≈ 数値スコア何点ぶんに相当させるか。</summary>
        public const float LearnedLiftWeight = 3.0f;

        /// <summary>
        /// 最善 index を返す。tie-break ランダム。
        /// 探索性 explorationRate (0..1) の確率で次点を選び、両方の選択肢を取り得るようにする。
        /// 学習データ (EventChoiceLearningStats) があれば、 各選択肢に観測リフトを加算する。
        /// </summary>
        public static int PickBestIndex(EventDefinition def, RunState run, System.Random rng, float explorationRate = 0.10f)
        {
            if (def == null || def.choices == null || def.choices.Count == 0) return 0;

            var scores = new List<float>(def.choices.Count);
            string eid = def.id;
            for (int i = 0; i < def.choices.Count; i++)
            {
                float baseScore = Score(def.choices[i], run);
                float lift = EventChoiceLearningStats.GetLearnedLift(eid, i);
                scores.Add(baseScore + LearnedLiftWeight * lift);
            }

            // 最善・次点
            int best = 0; float bestS = scores[0];
            int second = -1; float secondS = float.MinValue;
            for (int i = 1; i < scores.Count; i++)
            {
                if (scores[i] > bestS) { second = best; secondS = bestS; best = i; bestS = scores[i]; }
                else if (scores[i] > secondS) { second = i; secondS = scores[i]; }
            }

            // スコア差が小さい or 探索枠に当たった場合は次点を選ぶ
            if (second >= 0 && rng != null)
            {
                float margin = bestS - secondS;
                bool closeCall = margin < 4.0f; // 僅差なら半々
                double roll = rng.NextDouble();
                if (closeCall && roll < 0.5) return second;
                if (!closeCall && roll < explorationRate) return second;
            }
            return best;
        }
    }
}
