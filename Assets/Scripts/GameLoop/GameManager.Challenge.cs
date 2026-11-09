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
        /// <summary>層の入口の全回復を、 人が札を選び終えるまで預けている。</summary>
        private bool _entryHealPending;

        /// <summary>層の入口の全回復。 挑戦カードのランでは「回復 −N%」が掛かる (旧の配点では掛けない ── 今まで通り)。</summary>
        private void ApplyEntryHeal()
        {
            _entryHealPending = false;
            if (Run == null || Run.playerHP >= Run.playerMaxHP) return;
            float mul = Run.challengeCardMode ? MetaDebuffApplicator.GetHealMultiplier() : 1f;
            int before = Run.playerHP;
            Run.playerHP = Mathf.Min(Run.playerMaxHP, before + Mathf.RoundToInt((Run.playerMaxHP - before) * mul));
            Log($"層突入: HP 回復 {before} → {Run.playerHP}" + (mul < 1f ? $" (回復 ×{mul:0.##})" : ""));
        }

        /// <summary>ランの始め: 整備で選んだ難易度でカードのランにする。 整備の配点 (旧 §15-2) が入っている時は配点を使う (BOT の測定)。</summary>
        private void BeginChallengeCards()
        {
            PendingChallengeTiers.Clear(); _challengeOffer = null; _entryHealPending = false;
            var st = MetaProgressManager.Instance?.State;
            int d = st != null ? Mathf.Clamp(st.challengeDifficulty, 0, ChallengeCards.MaxDifficulty) : 0;
            Run.challengeCardMode = st == null || d > 0 || st.Challenge.IsEmpty;
            Run.challengeDifficulty = Run.challengeCardMode ? d : 0;
            Run.InvalidateCardChallenge();
            if (Run.challengeCardMode && d > 0) Log($"挑戦: 難易度 {d} ({ChallengeCards.ScheduleText(d)})");
        }

        /// <summary>層に入った時: この場所で引く回数だけ配る。 <paramref name="layer"/> は見せる層 (1..5 = currentFloor)・深さではない。</summary>
        private void DealChallengeForLayer(int layer)
        {
            if (Run == null || !Run.challengeCardMode) return;
            var forced = GameHooks.ForcedChallengeCards?.Invoke(layer);   // 測定: 引き方の代わりに決めたカードを受ける
            if (forced != null)
            {
                foreach (var c in forced)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    // "R:N" = この層で Tier N を本番と同じように 2 枚配って選ぶ (測定: Tier ごとの正味・2026-11-10)
                    if (c.StartsWith("R:") && int.TryParse(c.Substring(2), out int rt))
                    {
                        var offer = ChallengeCards.Deal(rt, Run.challengePicks);
                        if (offer.Count == 0) continue;
                        int pick = GameHooks.ChooseChallengeCard?.Invoke(offer) ?? 0;
                        TakeChallengeCard(offer[Mathf.Clamp(pick, 0, offer.Count - 1)]);
                        continue;
                    }
                    TakeChallengeCard(c);
                }
                return;
            }
            if (GameHooks.ForcedChallengeCards != null || Run.challengeDifficulty <= 0) return;
            int slot = ChallengeCards.SlotOfLayer(layer);
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
            if (offer.Count > 0) TakeChallengeCard(offer[Mathf.Clamp(pick, 0, offer.Count - 1)]);
            if (PendingChallengeTiers.Count == 0 && _entryHealPending) ApplyEntryHeal();
        }

        private void TakeChallengeCard(string card)
        {
            Run.challengePicks.Add(card);
            Run.InvalidateCardChallenge();
            ChallengeCards.Split(card, out var deb, out var buf);
            Log($"挑戦カード: − {ChallengeCards.Text(deb)} ／ + {ChallengeCards.Text(buf)}");
            // 七つの大罪 (IV・2026-11-10): 受けた時に効く 2 枚
            if (deb != null && deb.kind == ChallengeCards.SinGreed)
            {
                Log($"大罪:強欲: 所持金 {Run.coins}G と消耗品 {Run.ownedConsumables?.Count ?? 0} 個を失った");
                Run.coins = 0;
                Run.ownedConsumables?.Clear();
                // 最も高価なパッシブを失う (進行用の品と武器は除く)
                var db = ItemSystem.ItemDatabase.Instance;
                for (int k = 0; k < ChallengeCards.SinGreedPassives && db != null && Run.ownedPassiveItems != null; k++)
                {
                    int best = -1, bestPrice = -1;
                    for (int i = 0; i < Run.ownedPassiveItems.Count; i++)
                    {
                        string id = Run.ownedPassiveItems[i];
                        if (string.IsNullOrEmpty(id) || ItemIds.ProgressionItems.Contains(id)) continue;
                        var it = db.GetItem(id);
                        if (it == null || it.category == ItemSystem.ItemCategory.Weapon) continue;
                        if (it.basePrice > bestPrice) { bestPrice = it.basePrice; best = i; }
                    }
                    if (best < 0) break;
                    Log($"大罪:強欲: {Run.ownedPassiveItems[best]} を失った");
                    Run.ownedPassiveItems.RemoveAt(best);
                }
                ItemSystem.PassiveSkills.RunPassiveSync.RefreshFromRun(Run);
            }
            if (deb != null && deb.kind == ChallengeCards.SinLust)
            {
                var cands = new List<string>();
                if (Run.ownedPassiveItems != null)
                    foreach (var id in Run.ownedPassiveItems)
                        if (!string.IsNullOrEmpty(id) && !ItemIds.ProgressionItems.Contains(id) && !Run.sinSealedPassives.Contains(id) && !cands.Contains(id)) cands.Add(id);
                for (int k = 0; k < (int)deb.value && cands.Count > 0; k++)
                {
                    int at = GameRng.Range(0, cands.Count, "challenge.lust", k);
                    Run.sinSealedPassives.Add(cands[at]); cands.RemoveAt(at);
                }
                ItemSystem.PassiveSkills.RunPassiveSync.RefreshFromRun(Run);
                Log("大罪:色欲: 働かなくなった品 = " + string.Join("・", Run.sinSealedPassives));
            }
            if (deb != null && deb.kind == ChallengeCards.SinGluttony)
            {
                int cut = Mathf.Max(1, Mathf.RoundToInt(Run.playerMaxHP * ChallengeCards.SinGluttonyMaxHpPct / 100f));
                Run.playerMaxHP = Mathf.Max(1, Run.playerMaxHP - cut);
                Run.playerHP = Mathf.Min(Run.playerHP, Run.playerMaxHP);
                Log($"大罪:暴食: 最大 HP −{cut} ({Run.playerHP}/{Run.playerMaxHP})");
            }
            if (buf != null && buf.kind == ChallengeCards.MaxHp)
            {
                // 最大 HP +N%: 受けた時に上げ、 上がった分だけ今の HP も増やす
                int add = Mathf.Max(1, Mathf.RoundToInt(Run.playerMaxHP * buf.value / 100f));
                Run.playerMaxHP += add; Run.playerHP += add;
                Log($"挑戦カード: 最大 HP +{add} ({Run.playerHP}/{Run.playerMaxHP})");
            }
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
