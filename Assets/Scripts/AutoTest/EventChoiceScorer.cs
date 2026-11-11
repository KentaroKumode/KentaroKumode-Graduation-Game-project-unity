using System.Collections.Generic;
using EventSystem;
using GameLoop;
using MapSystem;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// BOT 用イベント選択肢評価器。
    /// 各選択肢の効果を数値スコアに変換し、現在の RunState（HP/物資/コイン）と追跡の状況（ターンを使う選択肢）で文脈補正する。
    ///
    /// 設計方針:
    ///  - 「100G+物資損 vs なにもなし」は通常 100G を取る（ゴールド価値 > 物資コスト）
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
        /// <summary>物資 1 の値打ち (2026-11-11・今の単位 = 1 歩)。 店の値段は 2G (= 1.1 点) だが、 店の数は層に 1 つで
        /// 足りない 1 は HP 5 (= 9 点) で払う ── その間に置く。 上限を超える分は 0。</summary>
        const float W_PROVISION  = 3.0f;
        /// <summary>この物資以下なら、 得る価値・失う痛みを 2 倍に見る (残り 3 歩)。</summary>
        const int   PROVISION_LOW = 3;

        // ===== ターンを使う選択肢 (2026-11-11・issue #17) =====
        /// <summary>補足されていない時の 1 ターンの値段。 巡回中の追跡者がいる層は、 その場に留まると通りかかられる見込みがある。</summary>
        const float V_TURN_FOE_PATROL = -1.0f;
        /// <summary>追跡者のいない層 (または倒した後) の 1 ターン。 天候の前線が進むだけ。</summary>
        const float V_TURN_NO_FOE     = -0.3f;
        /// <summary>補足されているが、 使うターンの間には捕まらない時の 1 ターン (距離が詰まる)。</summary>
        const float V_TURN_CHASED     = -3.0f;
        /// <summary>使うターンの間に捕まる: 追跡者 (本体・猟犬) との戦闘。 勝てば物資 +6 とアイテムが入るので、 エリートの戦闘より少し軽く見る。</summary>
        const float V_CAUGHT_BODY     = -18.0f;
        /// <summary>使い魔に捕まる: その層の通常の敵 1 体 (物資は得ない)。</summary>
        const float V_CAUGHT_FAMILIAR = -8.0f;
        const float W_MATERIAL   = 3.0f;
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

        // ===== 生存拒否権 =====
        //  加点の最大は V_GAIN_FLAG(+30) なので、 この値ならどんな報酬でも覆せない。
        const float V_LETHAL_VETO = -400f;   // 選んだ直後の一撃で死ぬ
        const float V_FRAGILE     = -60f;    // 二撃で死ぬ
        /// <summary>選べない選択肢 (払えないゴールド)。 致死の拒否権より下に置く。</summary>
        public const float V_UNAVAILABLE = -10000f;

        /// <summary>〈観測所の写し〉の評価。 上乗せ (パッシブ×1+素材+8+金+60+全回復) は
        /// パッシブ 1 個 (V_GAIN_PASS=9) + 雑多 を足して 18 前後だが、
        /// **次に博士へ会える保証が無い** (出現率 1/50)。 大きく割り引いて
        /// 「他の択より少し弱いが、選ばれることはある」帯に置く。</summary>
        const float V_OBSERVATORY_COPY = +7.0f;

        /// <summary>選択肢を評価。複数効果は加算、Probability は期待値。
        ///
        /// **加算の上に生存の拒否権を重ねる。** 効果ごとの線形加点だけだと、
        /// 「HPが1になる ＋ フラグ獲得(+30) ＋ パッシブ獲得(+9)」のような選択肢が
        /// 正のスコアになり、 BOT が自殺的な取引を選び続ける。 実際に「災厄の予兆」で
        /// これが起き、 **全ランの 29.4% がその経路で死亡していた** (2026-08-05)。
        /// 報酬の大小に関係なく、 選んだ結果が致死圏なら選ばせない。</summary>
        public static float Score(EventChoice choice, RunState run)
        {
            if (choice?.effects == null || choice.effects.Count == 0) return 0f;
            // 払えないゴールドの選択肢は選べない (本体も弾く)。 どの加点でも覆らない値にする。
            if (!choice.IsAvailable(run)) return V_UNAVAILABLE;
            float total = 0f;
            foreach (var e in choice.effects)
                total += ScoreEffect(e, run);

            // 危険度は「このフロアの雑魚 1 発」を尺度にする (進路選択・回復目標と同じ尺度)。
            int hit = AutoRunner.EstimateFloorMaxHit(run);
            if (hit > 0 && run != null)
            {
                int hpAfter = PredictWorstHp(choice, run);
                if (hpAfter <= hit)          total += V_LETHAL_VETO;
                else if (hpAfter <= hit * 2) total += V_FRAGILE;
            }
            return total;
        }

        /// <summary>この選択肢を取った直後の HP を、 **最悪分岐**で見積もる。
        /// Probability は期待値ではなく最悪枝を取る ── 生存判定は平均ではなく下振れで決まるため。</summary>
        private static int PredictWorstHp(EventChoice choice, RunState run)
        {
            int hp = run.playerHP;
            int maxHp = UnityEngine.Mathf.Max(1, run.playerMaxHP);
            int prov = run.provision;
            foreach (var e in choice.effects) ApplyWorst(e, ref hp, ref maxHp, ref prov);
            return UnityEngine.Mathf.Clamp(hp, 0, maxHp);
        }

        private static void ApplyWorst(EventEffect e, ref int hp, ref int maxHp, ref int prov)
        {
            if (e == null) return;
            switch (e.type)
            {
                case EventEffectType.ProvisionDelta:
                case EventEffectType.HungerDelta:
                    if (e.amount >= 0) { prov += e.amount; break; }
                    {
                        // 足りない分は HP で払う (本体: EventEffectExecutor・HP は 1 で止まる)
                        int need = -e.amount, paid = UnityEngine.Mathf.Min(need, UnityEngine.Mathf.Max(0, prov));
                        prov -= paid;
                        if (need > paid) hp = UnityEngine.Mathf.Max(1, hp - (need - paid) * MapSystem.FreeMove.FreeMapParams.HpPerSupply);
                    }
                    break;
                case EventEffectType.HpDelta:    hp = UnityEngine.Mathf.Clamp(hp + e.amount, 0, maxHp); break;
                case EventEffectType.HpFullHeal: hp = maxHp; break;
                case EventEffectType.HpSetTo:    hp = UnityEngine.Mathf.Clamp(e.amount, 0, maxHp); break;
                case EventEffectType.HpHalve:    hp = UnityEngine.Mathf.Max(1, (hp + 1) / 2); break;
                case EventEffectType.MaxHpDelta:
                    maxHp = UnityEngine.Mathf.Max(1, maxHp + e.amount);
                    hp = UnityEngine.Mathf.Min(hp, maxHp);
                    break;
                case EventEffectType.Probability:
                {
                    if (e.branches == null || e.branches.Count == 0) return;
                    int worst = int.MaxValue, worstMax = maxHp, worstProv = prov;
                    foreach (var br in e.branches)
                    {
                        int h = hp, m = maxHp, p = prov;
                        foreach (var c in br) ApplyWorst(c, ref h, ref m, ref p);
                        if (h < worst) { worst = h; worstMax = m; worstProv = p; }
                    }
                    if (worst != int.MaxValue) { hp = worst; maxHp = worstMax; prov = worstProv; }
                    break;
                }
            }
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
            // 物資: 移動の燃料 (1 歩 = 1)。 低いほど得る価値・失う痛みが大きい。
            int provisionVal = run != null ? run.provision : GameLoop.ProvisionRules.Max;
            int provisionCap = run != null ? UnityEngine.Mathf.Max(1, run.provisionCap) : GameLoop.ProvisionRules.Max;

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
                    // **「HP が 1 になる」も致死域として扱う。** 旧実装は amount<=0 しか
                    // 増幅せず、 HP1 は通常の被ダメと同じ重みだったため、 アイテム獲得の
                    // 加点に負けて BOT が選び続けていた (2026-08-04)。
                    if (e.amount <= 0) w *= 8f;
                    else if (e.amount <= UnityEngine.Mathf.Max(1, maxHP * 0.15f)) w *= 6f;
                    return -dmg * w;
                }

                case EventEffectType.HpHalve:
                {
                    int dmg = hp - UnityEngine.Mathf.Max(1, (hp + 1) / 2);
                    float w = W_HP_LOSS;
                    if (hpPct < 0.35f) w *= 2.5f;   // 低HPで半分はほぼ立て直せない
                    return -dmg * w;
                }

                case EventEffectType.MaxHpDelta:
                    return e.amount * W_MAX_HP;

                // 物資±N (今の単位: 1 歩 = 1・2026-11-11)。 2026-11-11 までは ProvisionDelta を読んでおらず (0 点)、
                //   旧表記の HungerDelta だけを旧単位の重みで読んでいた ── データ側はすべて「物資±N」なので、 物資は評価に入っていなかった。
                case EventEffectType.ProvisionDelta:
                case EventEffectType.HungerDelta:
                {
                    bool low = provisionVal <= PROVISION_LOW;
                    if (e.amount >= 0)
                    {
                        int eff = UnityEngine.Mathf.Min(e.amount, UnityEngine.Mathf.Max(0, provisionCap - provisionVal));   // 上限を超える分は捨てられる
                        return eff * W_PROVISION * (low ? 2f : 1f);
                    }
                    int need = -e.amount;
                    int paid = UnityEngine.Mathf.Min(need, UnityEngine.Mathf.Max(0, provisionVal));
                    int shortHp = (need - paid) * MapSystem.FreeMove.FreeMapParams.HpPerSupply;   // 足りない分は HP で払う
                    return -paid * W_PROVISION * (provisionVal - need <= PROVISION_LOW ? 2f : 1f) - shortHp * W_HP_LOSS;
                }

                case EventEffectType.TurnsPass:
                    return ScoreTurns(e.amount);

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

                case EventEffectType.ObservatoryTakeCopy:
                {
                    // **NPC の帯同と同じ穴。** 即時効果が (素材+6 / 物資+8) しか無いので、
                    //   写し自体を 0 点にすると 3 バッチ連続で BOT が一度も取らなかった
                    //   (写し使用 0)。 価値は「次に博士へ会ったときの上乗せ」にある。
                    //
                    //   上乗せは パッシブ×1 + 素材+8 + ゴールド+60 + 全回復。
                    //   ただし **次の遭遇があるとは限らない** (出現率 1/50) ので大きく割り引く。
                    //   既に持っているなら重複は無意味。
                    if (GameLoop.ObservatoryState.CopyHeld) return 0f;
                    return V_OBSERVATORY_COPY;
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

                case EventEffectType.CircusHandover:
                    // [廃止] 旅団契約システムを 2026-08-11 に削除したので、 引き渡す契約が無い。
                    return 0f;

                default:
                    return 0f;
            }
        }

        /// <summary>その場で N ターンを使う値段 (2026-11-11・issue #17)。 本体と同じ見込み (<see cref="EventEncounter.CatchEtaNow"/>・
        /// 補足中に「留まったら何ターンで捕まるか」) を読む。 **捕まるなら戦闘 1 回ぶん**、 捕まらないなら 1 ターンごとの小さな値段。</summary>
        private static float ScoreTurns(int turns)
        {
            if (turns <= 0) return 0f;
            var sim = GameManager.Instance != null ? GameManager.Instance.FreeMap : null;
            if (sim == null) return 0f;   // 航行マップの外では時間は何も動かさない
            var eta = EventEncounter.CatchEtaNow();
            if (eta.Valid)
            {
                if (eta.turns <= turns)
                {
                    var run = GameManager.Instance.Run;
                    float hpPct = run != null ? (float)run.playerHP / UnityEngine.Mathf.Max(1, run.playerMaxHP) : 1f;
                    float caught = eta.who == 2 ? V_CAUGHT_FAMILIAR : V_CAUGHT_BODY;
                    return caught * (hpPct < 0.65f ? 1.8f : 1f) + V_TURN_CHASED * (eta.turns - 1);
                }
                return V_TURN_CHASED * turns;
            }
            return (sim.FoeAlive ? V_TURN_FOE_PATROL : V_TURN_NO_FOE) * turns;
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
            if (second >= 0 && rng != null && secondS > V_UNAVAILABLE * 0.5f)   // 選べない選択肢は探索でも選ばない
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
