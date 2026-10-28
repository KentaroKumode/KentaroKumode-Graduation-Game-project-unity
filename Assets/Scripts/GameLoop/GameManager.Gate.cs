using System;
using System.Collections.Generic;
using UnityEngine;
using Codex;
using CombatSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.PassiveSkills;
using ItemSystem.Shop;
using MapSystem;

namespace GameLoop
{
    /// <summary>GameManager: 〈門〉の 3 工程と、 撃破後の一択 (裂け目)。</summary>
    public partial class GameManager
    {
        /// <summary>門マス到達時に呼ばれ、GateRitual フェーズへ遷移する。</summary>
        private void BeginGateRitual()
        {
            gateBloodResolved = false;
            gateRelicResolved = false;
            gateTransferResolved = false;
            RunStats.GateReached++;
            RunStats.GateProvisionBefore += Run.provision;   // [計装] 取り置き方策の検算用 (支払い前)
            SetPhase(GamePhase.GateRitual);
            Log("地獄の果てに、大きな円盤が壁へ立てかけられている。横の文が、不思議と読める ── これは門だ。");
        }

        /// <summary>①外縁の再構成で支払う最大HPの割合。 <b>現在HPではなく最大HPを削る。</b>
        /// 門の先に休憩も前哨基地の回復も無いので、 現在HPを払う形にすると
        /// 「削られた状態で入る」だけになり、 回復手段の有無で価値が乱高下する。
        /// 最大HPを削れば<b>どう回復しても取り返せない</b>ので、 代償として一定になる。</summary>
        public const float GateBloodMaxHpPct = 0.25f;

        /// <summary>①「門の外縁を再構成するために、生物の血が必要らしい」
        /// — 捧げる = 最大HP −25%。 拒否/不能なら〈不完全な修復〉。</summary>
        public void OfferGateBlood(bool accept)
        {
            if (CurrentPhase != GamePhase.GateRitual) return;

            int demand = Mathf.Max(1, Mathf.CeilToInt(Run.playerMaxHP * GateBloodMaxHpPct));
            // 最大HP を割り切ったら死ぬので、 払っても 1 以上残ることを条件にする。
            bool canPay = accept && !Run.lastStandActive && Run.playerMaxHP - demand >= 1;
            if (canPay)
            {
                int beforeMax = Run.playerMaxHP;
                Run.playerMaxHP -= demand;
                int lost = Mathf.Max(0, Run.playerHP - Run.playerMaxHP);
                Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                if (lost > 0) MetaProgression.Achievements.AchievementService.NoteExternalHpDamage(lost);
                RunStats.GateBloodPaid++; RunStats.GateMaxHpPaid += demand;
                Log($"血を捧げた。外縁は修復され、門が動き始めた (最大HP {beforeMax}→{Run.playerMaxHP})");
            }
            else
            {
                Run.AddFlaw(GateFlaw.IncompleteRepair);
                Log(Run.lastStandActive
                    ? "ラストスタンド中は血を流せない。〈不完全な修復〉のまま門が動き始めた。"
                    : "今後を加味して血を流すことを拒んだ。不完全ではあるが、門が動き始めた。〈不完全な修復〉");
            }
        }

        /// <summary>②で焚かれる遺物の数。</summary>
        public const int GateRelicDemand = 2;

        /// <summary>②「門を起動するために、いくつかの遺物が必要らしい」
        /// — 捧げる = <b>ランダムに</b>パッシブ 2 個を失う。 拒否/不足なら〈不完全な起動〉。
        ///
        /// <para><b>選べない。</b> 旧〈遺品の儀〉は「合計 30G 以上を満たす最小ペア」＝
        /// 一番安い 2 個を自動で出しており、 実質「端数を捨てるだけ」の無痛の支払いだった。
        /// ランダムにすると<b>主力が焼ける可能性</b>が入り、 代償として意味を持つ。
        /// 抽選は専用キーの <see cref="GameRng"/> ── 同一シードのペア比較を壊さないため、
        /// <b>払う/払わないに関わらず同じ回数だけ引く</b>ようにはしない
        /// (この判断はランの最後の 1 マスで、 以降に乱数を消費する分岐が無いので影響が閉じる)。</para></summary>
        public void OfferGateRelics(bool accept)
        {
            if (CurrentPhase != GamePhase.GateRitual) return;

            if (!accept || Run.lastStandActive)
            {
                Run.AddFlaw(GateFlaw.IncompleteIgnition);
                Log(Run.lastStandActive
                    ? "ラストスタンド中は遺物を手放せない。〈不完全な起動〉"
                    : "起動に見合う遺物など無いと判断した。間に合わせの材料で起動したが、明らかに不完全だ。〈不完全な起動〉");
                return;
            }

            // 候補: ownedPassiveItems のうち、進行用の品 (ItemIds.ProgressionItems: チェーン/初期装備ほか) を除いた実体。
            var candidates = new List<int>();
            var owned = Run.ownedPassiveItems;
            if (owned != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    string id = owned[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ItemIds.ProgressionItems.Contains(id)) continue;
                    candidates.Add(i);
                }
            }

            if (candidates.Count < GateRelicDemand)
            {
                Run.AddFlaw(GateFlaw.IncompleteIgnition);
                Log($"捧げられる遺物が {candidates.Count} 個しかない。〈不完全な起動〉");
                return;
            }

            // ランダムに GateRelicDemand 個。 高 index から除去する (並列配列の添字崩れ回避)。
            var picked = new List<int>(GateRelicDemand);
            for (int n = 0; n < GateRelicDemand; n++)
            {
                int at = GameRng.Range(0, candidates.Count, "gate.relic", n);
                picked.Add(candidates[at]);
                candidates.RemoveAt(at);
            }
            picked.Sort((a, b) => b.CompareTo(a));

            var names = new List<string>(picked.Count);
            foreach (int idx in picked)
            {
                if (idx >= 0 && owned != null && idx < owned.Count) names.Add(owned[idx]);
                ItemSystem.Helpers.PassiveAddHelper.RemoveAt(Run, idx);
            }
            RunStats.GateRelicsPaid++; RunStats.GateRelicsBurned += names.Count;
            Log($"遺物を門に捧げた。〈{string.Join("〉と〈", names)}〉は光を放った後、門の中で塵になって消えた。");
        }

        /// <summary>③で支払う物資。</summary>
        public const int GateProvisionDemand = 400;   // 2026-09-28: 物資 ×10

        /// <summary>③「門が転移を開始した時、あなたの意識が薄れゆく…」
        /// — 光に身を任せる = 物資 −40。 抗う/不足なら〈不完全な転移〉。</summary>
        public void OfferGateTransfer(bool accept)
        {
            if (CurrentPhase != GamePhase.GateRitual) return;

            bool canPay = accept && !Run.lastStandActive && Run.provision >= GateProvisionDemand;
            if (canPay)
            {
                int before = Run.provision;
                ProvisionSystem.Reduce(Run, GateProvisionDemand);
                RunStats.GateTransferPaid++;
                Log($"光に身を任せ、転移の行く末に思いを馳せた (物資 {before}→{Run.provision})");
            }
            else
            {
                Run.AddFlaw(GateFlaw.IncompleteTransfer);
                Log(Run.lastStandActive
                    ? "ラストスタンド中は身を任せられない。〈不完全な転移〉"
                    : "薄れゆく意識に頑強に抵抗した。転移は不完全に終わったが、意識を失うことは避けた。〈不完全な転移〉");
            }
        }

        // ================================================================
        //  ショップマス
        // ================================================================

        /// <summary>ショップ売却モード（true=売却、false=購入）</summary>
        public bool ShopSellMode { get; private set; }

        /// <summary>ショップ売却モードでの売却対象種別</summary>
        public ShopManager.SellSource ShopSellSource { get; private set; } = ShopManager.SellSource.Passive;

        /// <summary>ヴェスカ撃破後の一択を確定してランを閉じる。
        ///
        /// <para><paramref name="leaveOpen"/> = false → <b>裂け目を閉じる</b> (TRUE END〈帳尻〉)。
        /// 自分と遺物の山とヴェスカを対価に帳尻を合わせる。 遺物経済は破綻し、
        /// 主人公は行方不明になる。 数年後に七層の調査で見つかる手記から、
        /// この盤上遊戯の原作『大穴』が書かれる ── <b>額縁が閉じるのはこちら側だけ</b>。</para>
        ///
        /// <para><paramref name="leaveOpen"/> = true → <b>閉じない</b> (GOOD END〈見送り〉)。
        /// 世界は残るが本は書かれない。 額縁に接続しないことがこのエンドの代償にあたる。</para>
        ///
        /// <para>正本: <see cref="Endings"/> / docs/GAME.md §20。</para></summary>
        public void ChooseRiftFate(bool leaveOpen)
        {
            if (CurrentPhase != GamePhase.RiftChoice) return;
            Run.riftLeftOpen = leaveOpen;
            MetaProgression.Achievements.AchievementService.CompleteRun(Run);
            Run.EndRun();
            SetPhase(GamePhase.RunClear);
            OnRunCleared?.Invoke(Run);
            Log(leaveOpen ? "=== 完全クリア === 裂け目を閉じなかった"
                          : "=== 完全クリア === 裂け目を閉じた");
            var endingFull = Endings.Resolve(Run);
            Log(endingFull.Display);
            VignetteUnlockState.OnRunClear(Run, endingFull.id, VignetteUnlockState.IsMaxDifficultyRun(Run));
        }

        /// <summary>門の 3 工程をすべて決めて MapNavigation へ戻る。
        /// <b>門マスは 7 層の終端</b>なので、 戻った先で
        /// <see cref="IsBosslessFloorCleared"/> が成立し、 そのまま 8 層へ転移する。</summary>
        public void CompleteGateRitual()
        {
            if (CurrentPhase != GamePhase.GateRitual) return;
            RunStats.GateProvisionAfter += Run.provision;    // [計装] 支払い後。 45 (悲観・上限恒久ロック) を割っていないか
            Log(Run.gateFlaws == GateFlaw.None
                ? "門は完全に起動した。"
                : $"門が起動した ── 不完全なまま: {Run.gateFlaws}");
            SetPhase(GamePhase.MapNavigation);
        }
    }
}
