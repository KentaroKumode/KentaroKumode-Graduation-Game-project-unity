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
        // ===== 値段表 (2026-11-11・issue #17・docs/GAME.md §17 の交換レート) =====
        //  **点の単位はゴールド**。 品の値段は店の値段 (層の物価 ShopManager.ProgressPriceMultiplier が掛かる) から、
        //  HP は回復薬の店頭価格 ÷ 回復量から、 物資は「店の 2G」と「不足を HP 5 で払う」の高い方から決める。
        //  旧: 手置きの点 (パッシブ 9・消耗品 5・ゴールド 0.55/旧単位) ── パッシブがゴールド 16 ほどにしか見えていなかった。
        /// <summary>パッシブ 1 個の店頭価格 (物価 ×1 のとき)。 レア度の抽選重み (RarityWeightedPicker) で平均した
        /// 基準価格 8.8 × 抽選の幅の平均 1.125 × 品物の価格の倍率 2 (ShopManager.ItemPriceScale)。</summary>
        const float PRICE_PASSIVE = 19.8f;
        /// <summary>消耗品 1 個の店頭価格 (物価 ×1)。 基準価格の重み平均 5.9 × 1.125 × 2。</summary>
        const float PRICE_CONSUMABLE = 13.3f;
        /// <summary>HP 1 の値段 (物価 ×1)。 回復薬 (最大 HP の 40%・基準価格 5・SILVER) の店頭価格 14.1 ÷ 最大 HP 85 の 40%。</summary>
        const float PRICE_HP = 0.41f;
        /// <summary>強化素材 1。 店の値段 (15 × 物価) ではない ── 1 ランに 56 入って使い道は 16 (武器の 5 段の合計) で、
        /// 終了時に 43 余っている (2026-11-11 の runs.jsonl)。 限界の価値はほぼ無い。</summary>
        const float PRICE_MATERIAL = 2.0f;
        /// <summary>物資 1 の下限 (店の値段・FreeMapParams.ProvisionPrice)。 上限を超える分は 0。</summary>
        const float PRICE_PROVISION_MIN = 2.0f;
        /// <summary>HP を失う時の上乗せ (回復薬で買い戻す手間と、 次の戦闘の危険)。</summary>
        const float HP_LOSS_PREMIUM = 1.2f;
        /// <summary>この物資以下なら、 得る価値・失う痛みを 2 倍に見る (残り 3 歩)。</summary>
        const int   PROVISION_LOW = 3;

        /// <summary>今の層の物価 (店の値段の進行倍率・本体の式をそのまま読む)。</summary>
        static float Prog(RunState run) => ItemSystem.Shop.ShopManager.ProgressPriceMultiplier(run);
        static float HpPrice(RunState run) => PRICE_HP * Prog(run);
        static float ProvisionPrice(RunState run)
            => UnityEngine.Mathf.Max(PRICE_PROVISION_MIN, MapSystem.FreeMove.FreeMapParams.HpPerSupply * HpPrice(run));
        static float PassivePrice(RunState run) => PRICE_PASSIVE * Prog(run);
        static float ConsumablePrice(RunState run) => PRICE_CONSUMABLE * Prog(run);

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
        /// <summary>時限バフ / デバフ: 消耗品の半分 (多くは次の 1 戦闘だけ効く)。</summary>
        const float TIMED_VS_CONSUMABLE = 0.5f;
        const float V_GAIN_FLAG  = +30.0f;
        const float V_DISC_FLAG  = -22.0f;
        const float V_ARMOR_DUR  = -3.5f;
        const float V_COMBAT     = -8.0f;
        const float V_ELITE      = -22.0f;

        // ===== 生存拒否権 =====
        //  致死はどんな報酬でも覆せない値。 二撃で死ぬは「パッシブ 2 個ぶん」(層の物価で 44〜158G) ── 品 1 つでは覆らない。
        const float V_LETHAL_VETO = -400f;   // 選んだ直後の一撃で死ぬ
        static float FragilePenalty(RunState run) => -2f * PassivePrice(run);   // 二撃で死ぬ
        /// <summary>選べない選択肢 (払えないゴールド)。 致死の拒否権より下に置く。</summary>
        public const float V_UNAVAILABLE = -10000f;

        /// <summary>〈観測所の写し〉の評価。 上乗せ (パッシブ×1+素材+8+金+60+全回復) は
        /// パッシブ 1 個 + 雑多 を足して 2026-11-05 の点で 18 前後だったが、
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
            // 航行の効果はターンより先に適用される (GameManager.PassEventTurns)。 撒く・遠ざけるが付いた選択肢のターンは、 その後の見込みで値段を付ける
            _turnShaken = choice.Has(EventEffectType.ShakeOffPursuer);
            _turnPushed = 0;
            foreach (var e in choice.effects) if (e != null && e.type == EventEffectType.PushPursuer) _turnPushed += e.amount;
            float total = 0f;
            foreach (var e in choice.effects)
                total += ScoreEffect(e, run);
            _turnShaken = false; _turnPushed = 0;

            // 危険度は「このフロアの雑魚 1 発」を尺度にする (進路選択・回復目標と同じ尺度)。
            int hit = AutoRunner.EstimateFloorMaxHit(run);
            if (hit > 0 && run != null)
            {
                int hpAfter = PredictWorstHp(choice, run);
                if (hpAfter <= hit)          total += V_LETHAL_VETO;
                else if (hpAfter <= hit * 2) total += FragilePenalty(run);
            }
            return total;
        }

        /// <summary>選んだ直後の HP (最悪分岐) が「この層の雑魚 2 発」以下になるか (二撃で死ぬ・致死)。</summary>
        public static bool EndangersLife(EventChoice choice, RunState run)
        {
            if (choice?.effects == null || run == null) return false;
            int hit = AutoRunner.EstimateFloorMaxHit(run);
            return hit > 0 && PredictWorstHp(choice, run) <= hit * 2;
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
                    float w = 1f;   // 点の単位はゴールド (2026-11-11 からイベントの文面も今の大きさ)
                    if (coins < 8) w *= 1.6f;
                    else if (coins > 40) w *= 0.7f;
                    return e.amount * w;
                }

                case EventEffectType.HpDelta:
                {
                    if (e.amount >= 0)
                    {
                        // 治癒：HP低いほど価値大
                        float w = HpPrice(run);
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
                        float w = HpPrice(run) * HP_LOSS_PREMIUM;
                        if (hpPct < 0.35f) w *= 2.5f;
                        if (hp - dmg <= 0) w *= 6.0f;       // 直接死
                        else if (hp - dmg <= maxHP * 0.15f) w *= 3.0f; // 残り15%以下で危険
                        return -dmg * w;
                    }
                }

                case EventEffectType.HpFullHeal:
                {
                    int gain = UnityEngine.Mathf.Max(0, maxHP - hp);
                    float w = HpPrice(run);
                    if (hpPct < 0.35f) w *= 2.2f;
                    return gain * w;
                }

                case EventEffectType.HpSetTo:
                {
                    int delta = e.amount - hp;
                    if (delta >= 0) return delta * HpPrice(run);
                    int dmg = -delta;
                    float w = HpPrice(run) * HP_LOSS_PREMIUM;
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
                    float w = HpPrice(run) * HP_LOSS_PREMIUM;
                    if (hpPct < 0.35f) w *= 2.5f;   // 低HPで半分はほぼ立て直せない
                    return -dmg * w;
                }

                case EventEffectType.MaxHpDelta:
                    return e.amount * 3f * HpPrice(run);   // 最大 HP 1 ≒ 層ごとの全回復で 3 回ほど効く HP

                // 物資±N (今の単位: 1 歩 = 1・2026-11-11)。 2026-11-11 までは ProvisionDelta を読んでおらず (0 点)、
                //   旧表記の HungerDelta だけを旧単位の重みで読んでいた ── データ側はすべて「物資±N」なので、 物資は評価に入っていなかった。
                case EventEffectType.ProvisionDelta:
                case EventEffectType.HungerDelta:
                {
                    bool low = provisionVal <= PROVISION_LOW;
                    if (e.amount >= 0)
                    {
                        int eff = UnityEngine.Mathf.Min(e.amount, UnityEngine.Mathf.Max(0, provisionCap - provisionVal));   // 上限を超える分は捨てられる
                        return eff * ProvisionPrice(run) * (low ? 2f : 1f);
                    }
                    int need = -e.amount;
                    int paid = UnityEngine.Mathf.Min(need, UnityEngine.Mathf.Max(0, provisionVal));
                    int shortHp = (need - paid) * MapSystem.FreeMove.FreeMapParams.HpPerSupply;   // 足りない分は HP で払う
                    return -paid * ProvisionPrice(run) * (provisionVal - need <= PROVISION_LOW ? 2f : 1f) - shortHp * HpPrice(run) * HP_LOSS_PREMIUM;
                }

                case EventEffectType.TurnsPass:
                    return ScoreTurns(e.amount);

                case EventEffectType.MaterialDelta:
                    return e.amount * PRICE_MATERIAL;

                case EventEffectType.ArmorDurabilityLoss:
                    return V_ARMOR_DUR;

                case EventEffectType.TimedBuff:    return  TIMED_VS_CONSUMABLE * ConsumablePrice(run);
                case EventEffectType.TimedDebuff:  return -TIMED_VS_CONSUMABLE * ConsumablePrice(run);
                case EventEffectType.PermanentDebuff: return -PassivePrice(run);

                case EventEffectType.GainPassiveItem:
                case EventEffectType.GainSpecificItem:
                {
                    // 具体ID指定があれば優先度で加算
                    if (!string.IsNullOrEmpty(e.param))
                    {
                        if (LearnedPriorityProvider.IsSRank(e.param)) return 1.7f * PassivePrice(run);
                        if (LearnedPriorityProvider.IsARank(e.param)) return 1.3f * PassivePrice(run);
                        return 1.1f * PassivePrice(run);   // 名前付き (ユニーク) は店に並ばない
                    }
                    return PassivePrice(run);
                }
                case EventEffectType.GainConsumableItem:
                {
                    if (!string.IsNullOrEmpty(e.param) && LearnedPriorityProvider.IsSRank(e.param)) return 1.5f * ConsumablePrice(run);
                    return ConsumablePrice(run);
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
                case EventEffectType.DiscardPassiveItem: return -PassivePrice(run);

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

                // ── 2026-11-12: 報酬を多様にする汎用の動詞 (docs/GAME.md §17 の値段表の行と同じ値) ──
                case EventEffectType.RevealMap:        return ScoreReveal(e.amount, false, run);
                case EventEffectType.RevealEncounters: return ScoreReveal(e.amount, true, run);
                case EventEffectType.RevealSupplies:   return ScoreRevealSupplies(run);
                case EventEffectType.ShakeOffPursuer:  return ScoreShakeOff(run);
                case EventEffectType.PushPursuer:      return ScorePush(e.amount);
                case EventEffectType.PullPursuer:      return ScorePull(e.amount, run);
                case EventEffectType.Shortcut:         return ScoreShortcut(e.amount, run);
                case EventEffectType.ShiftFront:       return ScoreFront(e.amount, run);

                case EventEffectType.OfferPassives:
                    // N つから選べる分の上乗せ: 3 つなら ×1.3 (BOT は開幕の品と同じ買いの点で選ぶ)
                    return PassivePrice(run) * (1f + V_OFFER_PER_EXTRA * UnityEngine.Mathf.Max(0, e.amount - 1));
                case EventEffectType.TradePassive:
                    return GameManager.CanTradePassive(run) ? V_TRADE * PassivePrice(run) : 0f;
                case EventEffectType.ForgeWeapon:
                    // 素材は別に払う (WeaponUpgradeCost × PRICE_MATERIAL を差し引く)。 鍛えられないなら選べない (IsAvailable)
                {
                    int cost = GameManager.WeaponUpgradeCost(run);
                    if (cost == int.MaxValue) return 0f;
                    return V_FORGE * PassivePrice(run) - cost * PRICE_MATERIAL;
                }
                case EventEffectType.NextShopDiscount:
                    // 次の店で使う金 ≒ 今の所持金 + 次の店までの収入 (戦闘 1〜2 回)
                    return UnityEngine.Mathf.Clamp(e.amount, 0, 100) / 100f * (coins + V_SHOP_INCOME_AHEAD);
                case EventEffectType.NextRestBonus:
                    // 最大 HP の N% の HP。 次の休憩で回復を選ぶとは限らず、 満ちていれば捨てる分があるので割り引く
                    return e.amount / 100f * maxHP * HpPrice(run) * V_REST_BONUS_USE;

                default:
                    return 0f;
            }
        }

        // ===== 2026-11-12: 報酬を多様にする汎用の動詞の値段 (docs/GAME.md §17 の値段表) =====
        /// <summary>地図を明かす: 新しく種類が分かる点 1 つ = 物資 0.1 (12 点ほど明かして「1 歩ぶんの寄り道を減らす」)。</summary>
        const float V_REVEAL_POINT = 0.1f;
        /// <summary>戦闘を見抜く: まだ完全公開でない点 1 つ = 物資 0.05 (戦闘名は種類より使い道が狭い)。</summary>
        const float V_SCOUT_POINT = 0.05f;
        /// <summary>補給の場所が分かる: まだ分かっていない店・休憩 1 つ = 1 歩 (物資 1)。</summary>
        const float V_SUPPLY_POINT = 1.0f;
        /// <summary>追跡者を撒く (補足中): 捕まる見込みの 6 割を消す。 補足されていなくても近い時は少し。</summary>
        const float V_SHAKE_CHASED = 0.6f;
        const float V_SHAKE_NEAR = 2.0f;
        /// <summary>追跡者を遠ざける: 補足中は 1 歩 = 補足中の 1 ターンぶん (V_TURN_CHASED)、 巡回中は 1 歩 0.3。</summary>
        const float V_PUSH_PATROL = 0.3f;
        /// <summary>近道: 1 歩 = 物資 1 の 6 割 (飛ばした点の報酬も逃すので満額ではない)。 補足中は距離も稼ぐ (1 歩 +2)。</summary>
        const float V_SHORTCUT_STEP = 0.6f;
        const float V_SHORTCUT_CHASED = 2.0f;
        /// <summary>前線 1 段 (= 5 ターンぶんの前線の進み) = 物資 0.5。 今いる所に前線が届いていれば倍。</summary>
        const float V_FRONT_STEP = 0.5f;
        /// <summary>N つから選ぶ: 1 つ増えるごとにパッシブの値段の +15%。</summary>
        const float V_OFFER_PER_EXTRA = 0.15f;
        /// <summary>取り替え: 段の低い品を上の段の品にする差 ≒ パッシブ 0.4 個。</summary>
        const float V_TRADE = 0.4f;
        /// <summary>武器の 1 段 = パッシブ 0.7 個 (休憩・前哨基地の強化の機会を 1 回増やす)。</summary>
        const float V_FORGE = 0.7f;
        /// <summary>次の店までに入る金の見込み (戦闘 1〜2 回)。</summary>
        const float V_SHOP_INCOME_AHEAD = 15f;
        /// <summary>次の休憩の回復の上乗せのうち、 実際に効く割合。</summary>
        const float V_REST_BONUS_USE = 0.6f;

        /// <summary>この選択肢に「追跡者を撒く」があるか / 「遠ざける」の合計 (ターンの値段をその後の見込みで付けるため)。 Score の間だけ立つ。</summary>
        [System.ThreadStatic] static bool _turnShaken;
        [System.ThreadStatic] static int _turnPushed;

        static MapSystem.FreeMove.FreeMapSim Sim => GameManager.Instance != null ? GameManager.Instance.FreeMap : null;

        private static float ScoreReveal(int range, bool scouting, RunState run)
        {
            var sim = Sim;
            if (sim == null || range <= 0) return 0f;
            var d = sim.HopsFrom(sim.Me);
            int fresh = 0;
            for (int i = 0; i < d.Length; i++)
                if (d[i] <= range && (scouting ? !sim.Scouted[i] : !sim.Known[i])) fresh++;
            return fresh * (scouting ? V_SCOUT_POINT : V_REVEAL_POINT) * ProvisionPrice(run);
        }

        private static float ScoreRevealSupplies(RunState run)
        {
            var sim = Sim; var map = MapManager.Instance?.CurrentMap;
            if (sim == null || map == null) return 0f;
            int n = 0;
            foreach (var node in map.GetAllNodes())
                if (node != null && node.index >= 0 && node.index < sim.Known.Length && !node.visited && !sim.Known[node.index]
                    && (node.type == TileType.Shop || node.type == TileType.Rest)) n++;
            return n * V_SUPPLY_POINT * ProvisionPrice(run);
        }

        private static float ScoreShakeOff(RunState run)
        {
            var sim = Sim;
            if (sim == null || !sim.FoeAlive) return 0f;
            if (sim.Eta().Valid)
            {
                float hpPct = run != null ? (float)run.playerHP / UnityEngine.Mathf.Max(1, run.playerMaxHP) : 1f;
                return -V_CAUGHT_BODY * V_SHAKE_CHASED * (hpPct < 0.65f ? 1.8f : 1f);
            }
            return sim.Stone().level >= 1 ? V_SHAKE_NEAR : 0f;
        }

        private static float ScorePush(int steps)
        {
            var sim = Sim;
            if (sim == null || !sim.FoeAlive || steps <= 0) return 0f;
            return sim.Eta().Valid ? -V_TURN_CHASED * steps : V_PUSH_PATROL * steps;
        }

        private static float ScorePull(int steps, RunState run)
        {
            var sim = Sim;
            if (sim == null || !sim.FoeAlive || steps <= 0) return 0f;
            if (sim.Eta().Valid) return V_TURN_CHASED * steps;
            int d = sim.HopsFrom(sim.Me)[sim.FoeNode];
            int after = d == int.MaxValue ? int.MaxValue : UnityEngine.Mathf.Max(1, d - steps);
            // 補足される距離まで来るなら、 捕まる見込みの半分 (補足されても逃げ切れることがある)
            if (after <= sim.DetectHops) return 0.5f * V_CAUGHT_BODY;
            return V_TURN_FOE_PATROL * steps;
        }

        private static float ScoreShortcut(int steps, RunState run)
        {
            var sim = Sim;
            if (sim == null || steps <= 0) return 0f;
            int toGoal = sim.HopsFrom(sim.Layout.goal)[sim.Me];
            if (toGoal == int.MaxValue) return 0f;
            int n = UnityEngine.Mathf.Min(steps, UnityEngine.Mathf.Max(0, toGoal - 1));   // 終点の手前で止まる
            float v = n * V_SHORTCUT_STEP * ProvisionPrice(run);
            if (sim.Eta().Valid) v += n * V_SHORTCUT_CHASED;
            return v;
        }

        private static float ScoreFront(int steps, RunState run)
        {
            var sim = Sim;
            if (sim == null || steps == 0 || sim.Weather == MapSystem.FreeMove.WeatherKind.None) return 0f;
            if (steps < 0 && double.IsNegativeInfinity(sim.Front)) return 0f;   // まだ届いていない前線は戻せない
            float per = V_FRONT_STEP * ProvisionPrice(run) * (sim.InFront(sim.Me) ? 2f : 1f);
            return -steps * per;   // 遠ざける (負) が得、 近づける (正) が損
        }

        /// <summary>その場で N ターンを使う値段 (2026-11-11・issue #17)。 本体と同じ見込み (<see cref="EventEncounter.CatchEtaNow"/>・
        /// 補足中に「留まったら何ターンで捕まるか」) を読む。 **捕まるなら戦闘 1 回ぶん**、 捕まらないなら 1 ターンごとの小さな値段。</summary>
        private static float ScoreTurns(int turns)
        {
            if (turns <= 0) return 0f;
            var sim = GameManager.Instance != null ? GameManager.Instance.FreeMap : null;
            if (sim == null) return 0f;   // 航行マップの外では時間は何も動かさない
            var eta = EventEncounter.CatchEtaNow();
            // 同じ選択肢で撒く・遠ざけるなら、 ターンはその後に過ぎる (2026-11-12)
            if (_turnShaken) eta = MapSystem.FreeMove.CatchEta.None;
            else if (eta.Valid && _turnPushed > 0) eta.turns += _turnPushed;
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
