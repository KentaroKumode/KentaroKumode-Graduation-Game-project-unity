using System.Collections.Generic;
using CombatSystem;
using UnityEngine;

namespace GameLoop
{
    /// <summary>GameManager: 端子の山札 (ADR-0014) の報酬。 戦闘後は 2 択・エリートは 3 択・層ボスはレア 3 択。</summary>
    public partial class GameManager
    {
        public const int TerminalChoicesNormal = 2, TerminalChoicesElite = 3, TerminalChoicesBoss = 3;
        /// <summary>層ボスの報酬が出る最後の層 (見せる層の番号)。 5 層はヴェスカの連戦で、 後に層が無いので出さない。</summary>
        public const int TerminalBossRewardLastFloor = 4;

        /// <summary>戦闘に勝った後、 端子の報酬を提示する。 人は画面で選ぶ (<see cref="RunState.pendingTerminalOffer"/>)。
        /// BOT は <see cref="GameHooks.ChooseTerminalReward"/> でその場で選ぶ (無ければ先頭)。</summary>
        private void OfferTerminalReward(bool elite, bool boss)
        {
            if (Run == null) return;
            if (boss && Run.currentFloor > TerminalBossRewardLastFloor) return;
            int n = (boss ? TerminalChoicesBoss : elite ? TerminalChoicesElite : TerminalChoicesNormal) + Run.terminalExtraChoices;
            Run.terminalExtraChoices = 0;
            var offer = RollTerminalReward(Run, n, boss);
            if (offer.Count == 0) return;
            Run.pendingTerminalOffer = offer;
            if (GameHooks.ChooseTerminalReward == null && Loadout.HumanPlaying) return;      // 人: 画面が出す
            ResolveTerminalReward(GameHooks.ChooseTerminalReward?.Invoke(offer) ?? 0);
        }

        /// <summary>提示された端子から index 番目を山札に加える。 index &lt; 0 = 受け取らない。</summary>
        public void ResolveTerminalReward(int index)
        {
            if (Run == null || Run.pendingTerminalOffer.Count == 0) return;
            if (index >= 0 && index < Run.pendingTerminalOffer.Count)
            {
                string id = Run.pendingTerminalOffer[index];
                Run.terminalDeck.Add(id);
                Log($"端子を山札に加えた: {id} (山札 {Run.terminalDeck.Count} 枚)");
            }
            Run.pendingTerminalOffer.Clear();
        }

        /// <summary>報酬の候補を重ならないように n 個引く。 boss = レアから / それ以外 = 基本でもレアでもない端子から。
        /// 軸の端子は、 その軸の品を持っているほど出やすい (持っていなければ出にくい)。</summary>
        /// <para>誓約で封じられた軸の端子は候補に出さない (受け取っても資源が増えない)。</para>
        public static List<string> RollTerminalReward(RunState run, int n, bool boss)
            => RollTerminals(run, n, d => d.rare == boss, 1f, "terminal.reward");

        /// <summary>重み付きで重ならないように n 枚引く (基本は出さない・誓約で封じられた軸は出さない)。 軸の端子は偏重で寄せる。
        /// rareWeight = レアの重みに掛ける (報酬は rare で絞るので 1)。</summary>
        private static List<string> RollTerminals(RunState run, int n, System.Func<TerminalDef, bool> filter, float rareWeight, string rngKey)
        {
            var sealedAxes = ItemSystem.PassiveSkills.Effects.OathSeals.SealedAxes(run);
            var pool = new List<TerminalDef>(); var w = new List<float>();
            foreach (var d in TerminalCards.All)
            {
                if (d.basic || !filter(d)) continue;
                if (d.axis != null && sealedAxes.Contains(d.axis)) continue;      // 誓約の封じ (§4: 封じた軸の端子は報酬・店に出さない)
                float wt = 1f;
                if (d.axis != null)
                {
                    int depth = ItemSystem.PassiveSkills.AxisRules.Count(run, d.axis);
                    wt = depth <= 0 ? 0.3f : 1f + 0.5f * depth;
                }
                if (d.rare) wt *= rareWeight;
                pool.Add(d); w.Add(wt);
            }
            var res = new List<string>();
            while (res.Count < n && pool.Count > 0)
            {
                float total = 0f; for (int i = 0; i < w.Count; i++) total += w[i];
                float r = GameRng.Value(rngKey) * total; int k = 0;
                for (; k < w.Count - 1; k++) { r -= w[k]; if (r < 0f) break; }
                res.Add(pool[k].id); pool.RemoveAt(k); w.RemoveAt(k);
            }
            return res;
        }

        // =====================================================================
        //  店と休憩: 山札に足す・抜く (2026-11-11・数値はすべて仮)
        // =====================================================================
        //   店: 端子の棚 (TerminalShopSlots 枚・レアも混ざり、 値段が高い) と、 「山札から 1 枚抜く」サービス (店で使うたびに値段が上がる通算価格)。
        //   休憩: 回復の代わりに「山札から 1 枚抜く」(無料・休憩の行動を使う)。
        //   どちらも山札が下限 (TerminalCards.MinDeckSize) を下回る抜き方はできない。 人の画面も BOT も、 下の同じ関数を呼ぶ。

        /// <summary>店の端子の棚の枚数 / 値段の素 (店の価格倍率 = 進行・強盗の割増・商人の鈴 を掛ける)。</summary>
        public const int TerminalShopSlots = 3, TerminalShopPriceCommon = 16, TerminalShopPriceDisposable = 12, TerminalShopPriceRare = 36;
        /// <summary>棚にレアが出る重み (コモン 1 に対して)。</summary>
        public const float TerminalShopRareWeight = 0.25f;
        /// <summary>店で抜く値段 = 素 + 段 × これまでに店で抜いた回数 (上限なし・店の価格倍率は掛けない ── 在庫操作の値段なので一律)。</summary>
        public const int TerminalRemoveBasePrice = 15, TerminalRemoveStep = 10;

        /// <summary>店の端子の棚 1 枠。</summary>
        public sealed class TerminalShopSlot { public string id; public int price; public bool sold; }

        /// <summary>今の店の端子の棚 (店に入るたびに作り直す)。 店の外では空。</summary>
        public readonly List<TerminalShopSlot> TerminalShop = new List<TerminalShopSlot>();

        /// <summary>店に入った時: 端子の棚を作る (乱数は専用のキー "terminal.shop")。</summary>
        private void RollTerminalShop()
        {
            TerminalShop.Clear();
            if (Run == null) return;
            float mul = ItemSystem.Shop.ShopManager.Instance?.Current?.priceMultiplier ?? 1f;
            foreach (var id in RollTerminals(Run, TerminalShopSlots, d => true, TerminalShopRareWeight, "terminal.shop"))
            {
                var d = TerminalCards.Get(id);
                int basePrice = d.rare ? TerminalShopPriceRare : d.disposable ? TerminalShopPriceDisposable : TerminalShopPriceCommon;
                TerminalShop.Add(new TerminalShopSlot { id = id, price = Mathf.Max(1, Mathf.CeilToInt(basePrice * mul)) });
            }
        }

        /// <summary>店の端子の棚の i 枠目を買って山札に加える。</summary>
        public bool ShopBuyTerminal(int i)
        {
            if (CurrentPhase != GamePhase.ShopVisit || Run == null || i < 0 || i >= TerminalShop.Count) return false;
            var s = TerminalShop[i];
            if (s.sold || Run.coins < s.price) return false;
            if (!ProvisionPayment.TryPay(Run, s.price, "端子を買う")) return false;
            s.sold = true;
            Run.terminalDeck.Add(s.id);
            Log($"端子を買った: {s.id} (-{s.price}G・山札 {Run.terminalDeck.Count} 枚)");
            return true;
        }

        /// <summary>山札から抜ける (抜いても手札の枚数を下回らない)。</summary>
        public bool CanRemoveTerminal => Run?.terminalDeck != null && Run.terminalDeck.Count > TerminalCards.MinDeckSize;

        /// <summary>店で山札から 1 枚抜く値段 (店で抜くたびに上がる)。</summary>
        public int TerminalRemovePrice => TerminalRemoveBasePrice + TerminalRemoveStep * (Run != null ? Run.terminalShopRemovals : 0);

        /// <summary>店のサービス: 山札の deckIndex 枚目を抜く (払えない・抜けないなら false)。</summary>
        public bool ShopRemoveTerminal(int deckIndex)
        {
            if (CurrentPhase != GamePhase.ShopVisit || !CanRemoveTerminal || deckIndex < 0 || deckIndex >= Run.terminalDeck.Count) return false;
            int price = TerminalRemovePrice;
            if (Run.coins < price || !ProvisionPayment.TryPay(Run, price, "端子を抜く")) return false;
            string id = Run.terminalDeck[deckIndex];
            Run.terminalDeck.RemoveAt(deckIndex);
            Run.terminalShopRemovals++;
            Log($"山札から抜いた (店): {id} (-{price}G・店で {Run.terminalShopRemovals} 回目・山札 {Run.terminalDeck.Count} 枚)");
            return true;
        }

        /// <summary>休憩: 回復の代わりに、 山札の deckIndex 枚目を抜く (無料・休憩の行動を使う)。 抜けなければ false (休憩は続く)。</summary>
        public bool RestRemoveTerminal(int deckIndex)
        {
            if (CurrentPhase != GamePhase.RestStop || !CanRemoveTerminal || deckIndex < 0 || deckIndex >= Run.terminalDeck.Count) return false;
            string id = Run.terminalDeck[deckIndex];
            Run.terminalDeck.RemoveAt(deckIndex);
            Log($"山札から抜いた (休憩): {id} (山札 {Run.terminalDeck.Count} 枚)");
            ReturnToMapOrClearFloor();
            return true;
        }
    }
}
