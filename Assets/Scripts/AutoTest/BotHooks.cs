using System.Collections.Generic;
using GameLoop;

namespace AutoTest
{
    /// <summary>
    /// 本体の差し込み口 (<see cref="GameHooks"/> / <see cref="CombatSystem.BossParams.Overrides"/>) へ BOT を繋ぐ。
    /// AutoRunner の生成時に <see cref="Install"/>、 破棄時に <see cref="Uninstall"/>。
    /// </summary>
    public static class BotHooks
    {
        /// <summary>学習済み準パワーを攻撃力スケールへ換算する係数 (自動装備の武器評価 <c>Loadout.WeaponEV</c> に足す)。
        ///
        /// <para><b>大きく取る。</b> 素の attackPower(1〜8) / 会心率(5〜40%) は合計 13 点ぶんの
        /// 幅があるのに対し、 武器間の準パワー差は 1.3 程度しかない。 係数が小さいと
        /// <b>生のステータスが学習値を 5:1 で押し潰す</b> ── そして生の数値は
        /// <b>防御とパッシブの価値を一切表現できない</b> (盾は攻撃1〜4/会心1〜2 で必ず負ける)。
        /// 学習値は実測された寄与そのものなので、 代理指標に劣後させる理由がない。</para>
        ///
        /// <para>0 にすると旧来の「ステータスだけ」に戻る。 <b>暫定値・要測定</b>。</para></summary>
        private const float LearnedWeaponWeight = 6.0f;

        /// <summary>[2026-11-10] 軸に寄せる方策 (AutoRunner.buyAxisFocus)。 空 = 無効。 開幕の品の選択と挑戦カードで、 この軸を優先する。</summary>
        public static string AxisFocus = "";
        /// <summary>軸に寄せる加点 (序列の段の数・× StepPerTier)。</summary>
        public static int AxisFocusBonus = 1;

        private static bool IsFocusItem(string id)
            => !string.IsNullOrEmpty(AxisFocus) && !string.IsNullOrEmpty(id)
            && ItemSystem.ItemDatabase.Instance?.GetItem(id)?.axis == AxisFocus;

        public static void Install()
        {
            GameHooks.BotDriving = true;
            GameHooks.ChooseStartingPassive = ChooseByBuyScore;
            GameHooks.WeaponEvBonus = LearnedWeaponBonus;
            GameHooks.MeasureRunPower = InventoryPower.Compute;
            GameHooks.OnCritRateResolved = CritRateCensus.Note;
            BossTuning.Install();
        }

        /// <summary>挑戦カードの 2 枚から、 バフが自分の軸 (品の数が一番多い軸) に合う方を取る (2026-11-09)。
        /// 軸の札は「その軸の品の数」、 汎用の札は 1 で比べ、 同点は先頭。 デバフは見ない (方策は最小限・比べるための基準)。</summary>
        public static int ChooseChallengeByAxis(System.Collections.Generic.IReadOnlyList<string> offer)
        {
            var run = GameManager.Instance?.Run;
            int best = 0; float bestScore = float.MinValue;
            for (int i = 0; i < offer.Count; i++)
            {
                MetaProgression.ChallengeCards.Split(offer[i], out _, out var buf);
                string axis = buf == null ? null : buf.kind switch
                {
                    MetaProgression.ChallengeCards.Poison => ItemSystem.PassiveSkills.AxisRules.Poison,
                    MetaProgression.ChallengeCards.Bleed => ItemSystem.PassiveSkills.AxisRules.Bleed,
                    MetaProgression.ChallengeCards.Heat => ItemSystem.PassiveSkills.AxisRules.Heat,
                    MetaProgression.ChallengeCards.Charge => ItemSystem.PassiveSkills.AxisRules.Charge,
                    MetaProgression.ChallengeCards.Shield => ItemSystem.PassiveSkills.AxisRules.Shield,
                    MetaProgression.ChallengeCards.Crit => ItemSystem.PassiveSkills.AxisRules.Crit,
                    _ => null,
                };
                float score = axis != null ? ItemSystem.PassiveSkills.AxisRules.Count(run, axis) : 1f;
                if (axis != null && !string.IsNullOrEmpty(AxisFocus) && axis == AxisFocus) score += 1000f;   // [2026-11-10] 軸に寄せる方策
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        public static void Uninstall()
        {
            GameHooks.Clear();
            BossTuning.Uninstall();
        }

        /// <summary>学習した購入スコアが最大の提示を選ぶ。 同点は先の方 (旧実装と同じ)。</summary>
        private static int ChooseByBuyScore(IReadOnlyList<string> offers)
        {
            int best = 0; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < offers.Count; i++)
            {
                float sc = LearnedPriorityProvider.BuyScore(offers[i]);
                if (IsFocusItem(offers[i])) sc += AxisFocusBonus * LearnedPriorityProvider.StepPerTier;   // [2026-11-10] 軸に寄せる方策
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            return best;
        }

        /// <summary>家系パッシブの価値。 素のステータスには現れないので学習値から引く。
        ///
        /// <para><b>家系の終端 (T4) の学習値で代表させる。</b> T1 の盾を拾うことは
        /// 「盾の家系に乗る」ことであり、 判断すべきはチェーン全体の価値だから ──
        /// 同家系なら上位段へ自動で乗る段ルールと同じ理屈である。
        /// T1〜T3 は <c>ExcludedFromLift</c> で学習値を持たない (チェーン下流の効果が
        /// lift に流れ込むため) ので、 終端を見ないと<b>家系パッシブが評価に一切入らない</b>。</para>
        ///
        /// <para>**未学習なら 0** ── 学習ゼロのバッチでは素の EV だけで比較する
        /// (<c>PowerScore</c> は未学習に <c>Unlearned</c> を返すので、 そのまま足すと
        /// 全武器に同じ下駄を履かせることになり比較の役に立たない)。</para></summary>
        private static float LearnedWeaponBonus(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            string fam = ItemSystem.ItemDatabase.Instance?.GetItem(id)?.family;
            string key = !string.IsNullOrEmpty(fam) ? fam + "_t4" : id;   // 家系の終端で代表
            if (!LearnedPriorityProvider.HasLearned(key)) return 0f;
            return LearnedPriorityProvider.PowerScore(key) * LearnedWeaponWeight;
        }
    }
}
