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

        public static void Install()
        {
            GameHooks.ChooseStartingPassive = ChooseByBuyScore;
            GameHooks.WeaponEvBonus = LearnedWeaponBonus;
            GameHooks.MeasureRunPower = InventoryPower.Compute;
            GameHooks.OnCritRateResolved = CritRateCensus.Note;
            GameHooks.OnShopTierOffered = FamilyTierStats.NoteOffer;
            GameHooks.OnShopUpgradeOffered = FamilyTierStats.NoteUpgradeOffered;
            GameHooks.OnShopUpgradeBought = FamilyTierStats.NoteUpgradeBought;
            BossTuning.Install();
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
