using UnityEngine;

namespace GameLoop
{
    /// <summary><b>燈火: ゴールドが足りない分を物資で払う。</b> 2026-09-13 新設 /
    /// <b>2026-09-14 に r10 限定から段効果へ降ろした (案C)</b> ── 段が上がるほど深く払える。
    /// <b>レート 1G = 物資 2</b> (2026-09-13: 1:1 から改定。 1:1 では r10−r9 +6.99pt で
    /// 目標 +3pt を大きく超えていた)。
    ///
    /// <para><b>なぜこれか。</b> 燈火トラックの段効果は物資上限 +15/段 で、 r10 なら上限 250。
    /// ところが物資は<b>回復源が無い</b>ので、 上限を上げても「減り始める位置が高い」だけで、
    /// 余った物資はランの終わりに<b>そのまま捨てられていた</b>。 支払いに使えるなら、
    /// 段効果 (上限) がそのまま購買力になる ── <b>トラックの段効果と極点が噛み合う</b>。
    /// 防御 r10 (自分のトラックが生まない盾を持ち越す) が噛み合わずゼロと区別できなかったのの逆。</para>
    ///
    /// <para><b>規則と方策を分ける。</b> ここが決めるのは「どこまでが合法か」だけ。
    /// <see cref="RuleFloorNow"/> より下へは<b>規則として</b>払えない ── 物資 0 は発狂＝ラン終了なので、
    /// 買い物で自殺できる規則にはしない。 「いくら払うのが賢いか」は方策の側 (BOT) が
    /// <see cref="PolicyFloorNow(RunState)"/> でもっと保守的に決める。 規則と方策を同じ数字にすると、
    /// 規則を緩めた瞬間に方策まで無謀になる。</para></summary>
    public static class ProvisionPayment
    {
        /// <summary>規則としての下限。 ここを割る支払いはできない。
        ///
        /// <para><b>2026-09-14: 段効果になった (案C)。</b> 燈火の段が上がるほど深く払える
        /// (r1 で 45 ＝ 悲観帯まで、 段ごとに −3、 r10 で 20 ＝ 絶望帯の手前)。
        /// 旧構成は r10 の極点だけが 20 固定で、 <b>段効果 (物資上限) と極点が噛み合っていなかった</b>
        /// ── 上限を上げても物資に収入が無いので、 タンクが空のまま大きくなるだけだった
        /// (実測 r9 で 6 層突入時 物資 170 未使用 / Bal比 −11.12pt で全アーム最下位)。
        /// 正本は <see cref="MetaProgression.MetaPanel.LanternProvisionSpendFloor"/>。</para></summary>
        public static int RuleFloorNow()
            => MetaProgression.MetaBuffApplicator.GetProvisionSpendFloor();

        /// <summary>方策としての下限。 <b>規則と同じ 80。</b>
        ///
        /// <para><b>物資を「残高」ではなく「帯」で値付けする (2026-09-14)。</b>
        /// 旧実装は規則の床 +10 という固定値で、 <b>床より上の物資を全部タダ金と見ていた</b>。
        /// その結果 案C 初版で BOT が物資を床 (45〜20) まで使い切り、
        /// Balanced のクリア率が 24.60% → 12.29% へ崩れた。 使い道を作ると必ず使い切る。</para>
        ///
        /// <para>物資の価値は残量ではなく<b>どの帯へ落ちるか</b>で決まる:
        /// 75 以下で疲労 (15% で最終ダメ半減)、 <b>45 以下で悲観 ＋ 上限が 45 へ恒久ロック</b>、
        /// 20 以下で絶望 (開幕パッシブ無効)。 <b>規則はそこまで禁じない</b> ──
        /// 人間が「ここで押し切る」と決めた手を規則で塞がない。 賢さの判断は方策の仕事。</para></summary>
        public static int PolicyFloorNow(RunState run)
        {
            int rule = RuleFloorNow();
            if (rule == int.MaxValue) return int.MaxValue;   // 未解禁
            // **規則より高い水位でしか使わない。** 規則は絶望帯の手前まで許すが、
            //   BOT は素の上限 (100) を超えた分だけを資源として扱う。
            return Mathf.Max(rule, MetaProgression.MetaPanel.ProvisionSpendThreshold);
        }

        /// <summary>[計装] 物資で払った回数と総額。 バッチ後に読む。</summary>
        public static long PaymentCount;
        public static long ProvisionSpentTotal;
        public static void ResetStats() { PaymentCount = 0; ProvisionSpentTotal = 0; }

        /// <summary>物資払いが使えるか。 <b>r10 限定</b> (段効果へ降ろす案は撤回・MetaPanel 参照)。</summary>
        public static bool IsUnlocked()
            => MetaProgression.MetaBuffApplicator.GetProvisionSpendFloor() < int.MaxValue;

        /// <summary>1G を買うのに要る物資。</summary>
        public const int ProvisionPerGold = 20;   // 2026-09-28: 物資 ×10

        /// <summary>支払いに回せる物資。 未解禁なら 0。</summary>
        public static int SpendableProvision(RunState run, int floor)
        {
            if (run == null || !IsUnlocked()) return 0;
            return Mathf.Max(0, run.provision - floor);
        }

        /// <summary>使える物資を「何 G 分か」へ換算する (切り捨て)。</summary>
        private static int ProvisionAsGold(RunState run, int floor)
            => SpendableProvision(run, floor) / ProvisionPerGold;

        /// <summary>規則上いくらまで払えるか (ゴールド + 物資の換算分)。</summary>
        public static int Affordable(RunState run)
            => run == null ? 0 : run.coins + ProvisionAsGold(run, RuleFloorNow());

        /// <summary>BOT が「買ってよい」と見なす上限。 規則より保守的。</summary>
        public static int PolicyAffordable(RunState run)
            => run == null ? 0 : run.coins + ProvisionAsGold(run, PolicyFloorNow(run));

        /// <summary><paramref name="price"/> を払う。 ゴールドを先に使い、 不足分だけ物資で埋める。
        /// <b>払えないなら何も動かさず false</b> ── 部分的に引いて失敗すると、
        /// 「買えなかったのにゴールドだけ減った」という一番読めない壊れ方になる。</summary>
        public static bool TryPay(RunState run, int price, string label)
        {
            if (run == null || price < 0) return false;
            if (price == 0) return true;

            if (run.coins >= price)
            {
                run.coins -= price;
                run.coinsSpent += price;
                return true;
            }

            int shortfall = price - run.coins;
            int provisionCost = shortfall * ProvisionPerGold;
            if (provisionCost > SpendableProvision(run, RuleFloorNow())) return false;

            int paidGold = run.coins;
            run.coins = 0;
            run.coinsSpent += paidGold;
            run.provision -= provisionCost;
            PaymentCount++;
            ProvisionSpentTotal += provisionCost;
            Debug.Log($"[燈火r10] 物資で支払い: {label} {price}G のうち {paidGold}G"
                    + $" + 物資 {provisionCost} ({shortfall}G 分) (残 物資 {run.provision}/{run.provisionCap})");
            return true;
        }
    }
}
