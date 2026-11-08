using System.Collections.Generic;
using UnityEngine;
using MetaProgression;

namespace GameLoop
{
    /// <summary>
    /// 挑戦カード (docs/GAME.md §23-25) のラン中の流れ。 層 d (1..5) に入るたび、 難易度の引き方 (<see cref="ChallengeCards.Schedule"/>) で
    /// その場所に割り当てた回数だけ 2 枚から 1 枚を選ぶ。 BOT は <see cref="GameHooks.ChooseChallengeCard"/> (無ければ先頭) でその場で選び、
    /// 人が遊んでいる時は航行の画面の窓まで預ける (<see cref="PendingChallengeTiers"/>)。
    /// </summary>
    public partial class GameManager
    {
        /// <summary>選ぶのを預かっている引き (Tier の並び・先頭から)。 人が遊んでいる時だけ。</summary>
        public readonly List<int> PendingChallengeTiers = new List<int>();
        private List<string> _challengeOffer;

        /// <summary>ランの始め: 整備で選んだ難易度でカードのランにする。 整備の配点 (旧 §15-2) が入っている時は配点を使う (BOT の測定)。</summary>
        private void BeginChallengeCards()
        {
            PendingChallengeTiers.Clear(); _challengeOffer = null;
            var st = MetaProgressManager.Instance?.State;
            int d = st != null ? Mathf.Clamp(st.challengeDifficulty, 0, ChallengeCards.MaxDifficulty) : 0;
            Run.challengeCardMode = st == null || d > 0 || st.Challenge.IsEmpty;
            Run.challengeDifficulty = Run.challengeCardMode ? d : 0;
            Run.InvalidateCardChallenge();
            if (Run.challengeCardMode && d > 0) Log($"挑戦: 難易度 {d} ({ChallengeCards.ScheduleText(d)})");
        }

        /// <summary>層に入った時: この場所で引く回数だけ配る。</summary>
        private void DealChallengeForDepth(int depth)
        {
            if (Run == null || !Run.challengeCardMode || Run.challengeDifficulty <= 0) return;
            int slot = ChallengeCards.SlotOfDepth(depth);
            if (slot < 0) return;
            bool human = GameHooks.ChooseChallengeCard == null && !GameHooks.BotDriving && !Application.isBatchMode;
            foreach (var e in ChallengeCards.Schedule(Run.challengeDifficulty))
            {
                if (e.slot != slot) continue;
                if (human) { PendingChallengeTiers.Add(e.tier); continue; }
                var offer = ChallengeCards.Deal(e.tier, Run.challengePicks);
                if (offer.Count == 0) continue;
                int pick = GameHooks.ChooseChallengeCard?.Invoke(offer) ?? 0;
                TakeChallengeCard(offer[Mathf.Clamp(pick, 0, offer.Count - 1)]);
            }
        }

        /// <summary>預かっている引きの、 いま見せる 2 枚 ("デバフの id|バフの id")。 初めて聞かれた時に配る (前に選んだカードを見て重なりを避ける)。</summary>
        public IReadOnlyList<string> ChallengeOffer
        {
            get
            {
                if (Run == null || PendingChallengeTiers.Count == 0) return null;
                return _challengeOffer ??= ChallengeCards.Deal(PendingChallengeTiers[0], Run.challengePicks);
            }
        }

        /// <summary>預かっている引きの先頭から 1 枚を選んで受ける。</summary>
        public void ResolveChallengePick(int pick)
        {
            var offer = ChallengeOffer;
            if (offer == null) return;
            PendingChallengeTiers.RemoveAt(0); _challengeOffer = null;
            if (offer.Count == 0) return;
            TakeChallengeCard(offer[Mathf.Clamp(pick, 0, offer.Count - 1)]);
        }

        private void TakeChallengeCard(string card)
        {
            Run.challengePicks.Add(card);
            Run.InvalidateCardChallenge();
            ChallengeCards.Split(card, out var deb, out var buf);
            Log($"挑戦カード: − {ChallengeCards.Text(deb)} ／ + {ChallengeCards.Text(buf)}");
            // 〈凶運〉はランの始めに役を封じる効果なので、 途中で受けたら受けた時に封じる
            if (Run.sealedRoleMask == 0 && MetaDebuffApplicator.C.Has(ChallengeT4.凶運))
                Run.sealedRoleMask = MetaDebuffApplicator.RollSealedRoles(Run);
        }

        /// <summary>5 層 (通常の終点) のボスを倒した時: 難易度を記録する。</summary>
        private void RecordChallengeClear()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm?.State;
            if (st == null || Run == null || !Run.challengeCardMode || Run.challengeDifficulty <= st.challengeBest) return;
            st.challengeBest = Run.challengeDifficulty;
            mpm.Save();
            Log($"挑戦: 難易度 {Run.challengeDifficulty} を踏破 (記録を更新)");
        }
    }
}
