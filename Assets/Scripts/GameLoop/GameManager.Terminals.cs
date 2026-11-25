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
            if (Run == null || RunSpec.NoRewards) return;   // ラン指定の「報酬なし」(チュートリアル)
            if (boss && Run.currentFloor > TerminalBossRewardLastFloor) return;
            // 0 層の試練の木箱 (2026-11-20・docs/system-overhaul-spec.md §4-4) は 3 択 (エリートと同じ数)
            bool farm = Run.Depth == MapSystem.FreeMove.FloorPlan.FarmFloor;
            // 〈戦利の目録〉: どの戦闘の報酬も 1 枚多い (通常 2 → 3・エリートと 0 層 3 → 4・ボス 3 → 4)
            int n = (boss ? TerminalChoicesBoss : elite || farm ? TerminalChoicesElite : TerminalChoicesNormal) + Run.terminalExtraChoices
                  + (Run.OwnsPassive(ItemIds.MacroAppraisal) ? 1 : 0);
            Run.terminalExtraChoices = 0;
            var offer = RollTerminalReward(Run, n, boss);
            if (offer.Count == 0) return;
            // イベントの「端子をNつから選ぶ」を人が選ぶ前に戦闘になった (「…, 戦闘に突入」) なら、 その提示は戦闘の報酬の後へ回す (消さない)
            if (Run.pendingTerminalOffer.Count > 0) PendingEventTerminalOffers.Insert(0, Run.pendingTerminalOffer);
            Run.pendingTerminalOffer = offer;
            TerminalOfferFromEvent = false;
            TerminalDiag.NoteOffer(boss ? TerminalDiag.SceneBoss : elite ? TerminalDiag.SceneElite : TerminalDiag.SceneNormal, offer);   // [計装 #106]
            if (GameHooks.ChooseTerminalReward == null && Loadout.HumanPlaying) return;      // 人: 画面が出す
            ResolveTerminalReward(GameHooks.ChooseTerminalReward?.Invoke(offer) ?? 0);
        }

        /// <summary>提示された端子から index 番目を山札に加える。 index &lt; 0 = 受け取らない。</summary>
        public void ResolveTerminalReward(int index)
        {
            if (Run == null || Run.pendingTerminalOffer.Count == 0) return;
            TerminalDiag.NoteResolve(Run.pendingTerminalOffer, index);   // [計装 #106]
            if (index >= 0 && index < Run.pendingTerminalOffer.Count)
            {
                string id = Run.pendingTerminalOffer[index];
                Run.terminalDeck.Add(id);
                MetaProgression.Achievements.AchievementService.NoteTerminalDeck(Run);   // 実績〈紙の城〉
                Log($"端子を山札に加えた: {id} (山札 {Run.terminalDeck.Count} 枚)");
            }
            Run.pendingTerminalOffer.Clear();
            TerminalOfferFromEvent = false;
            // 預かっているイベントの提示があれば、 次はそれを出す
            if (PendingEventTerminalOffers.Count > 0)
            {
                Run.pendingTerminalOffer = PendingEventTerminalOffers[0];
                PendingEventTerminalOffers.RemoveAt(0);
                TerminalOfferFromEvent = true;
            }
        }

        // =====================================================================
        //  イベントの「端子をNつから選ぶ」「山札から端子を抜く」(2026-11-21・docs/system-overhaul-spec.md §2-2)
        // =====================================================================

        /// <summary>今の提示 (<see cref="RunState.pendingTerminalOffer"/>) がイベントから来たか (画面の題と、 戦闘の不安定化の行を出さないため)。</summary>
        public bool TerminalOfferFromEvent { get; private set; }

        /// <summary>人が選ぶ前に別の提示が出ていたイベントの提示 (先頭から順に <see cref="RunState.pendingTerminalOffer"/> へ移す)。</summary>
        public readonly List<List<string>> PendingEventTerminalOffers = new List<List<string>>();

        /// <summary>人が地図へ戻ってから抜く端子を選ぶ、 イベントの「山札から端子を抜く」の数。</summary>
        public int PendingEventTerminalRemovals { get; private set; }

        /// <summary>イベントの提示の候補を n 枚引く。 rare = レアから / それ以外 = 基本でもレアでもない端子から (戦闘の報酬の通常 / 層ボスと同じ絞り方・軸の寄せ方も同じ)。
        /// 乱数は専用のキー "event.terminal" (戦闘の報酬の "terminal.reward" の列を動かさない)。</summary>
        public static List<string> RollEventTerminals(RunState run, int n, bool rare)
            => RollTerminals(run, n, d => d.rare == rare, 1f, "event.terminal");

        /// <summary>イベントの端子の提示を受ける。 BOT (<see cref="GameHooks.ChooseTerminalReward"/>・戦闘の報酬と同じ点・負 = 受け取らない) はこの場で決め、
        /// 加えた端子の名前 (加えなければ "") を返す。 人は戦闘の報酬と同じ画面へ預けて null を返す。</summary>
        public string OfferEventTerminals(List<string> offer)
        {
            if (Run == null || offer == null || offer.Count == 0) return "";
            if (GameHooks.ChooseTerminalReward == null && Loadout.HumanPlaying)
            {
                if (Run.pendingTerminalOffer.Count > 0) PendingEventTerminalOffers.Add(offer);
                else { Run.pendingTerminalOffer = offer; TerminalOfferFromEvent = true; }
                return null;
            }
            int pick = GameHooks.ChooseTerminalReward?.Invoke(offer) ?? 0;
            if (pick < 0 || pick >= offer.Count) { Log("イベント: 端子は山札に加えなかった"); return ""; }
            Run.terminalDeck.Add(offer[pick]);
            MetaProgression.Achievements.AchievementService.NoteTerminalDeck(Run);   // 実績〈紙の城〉
            Log($"イベント: 端子を山札に加えた: {offer[pick]} (山札 {Run.terminalDeck.Count} 枚)");
            return offer[pick];
        }

        /// <summary>イベントの「山札から端子を抜く」を受ける。 BOT (<see cref="GameHooks.ChooseTerminalRemoval"/>・負 = 抜かない・無ければ抜かない) はこの場で抜き、
        /// 抜いた端子の名前 (抜かなければ "") を返す。 人は地図へ戻った時の画面へ預けて null を返す。 抜けない (山札が下限) なら "" (呼ぶ側が先に確かめる)。</summary>
        public string OfferEventTerminalRemoval()
        {
            if (!CanRemoveTerminal) return "";
            if (GameHooks.ChooseTerminalRemoval == null && Loadout.HumanPlaying) { PendingEventTerminalRemovals++; return null; }
            int at = GameHooks.ChooseTerminalRemoval?.Invoke(Run.terminalDeck) ?? -1;
            return RemoveTerminalForEvent(at) ?? "";
        }

        /// <summary>預かっている「山札から端子を抜く」を 1 つ閉じる (地図の画面から呼ぶ)。 deckIndex が負 = 抜かない。 抜けない添字なら false (預かりは残る)。</summary>
        public bool ResolveEventTerminalRemoval(int deckIndex)
        {
            if (Run == null || PendingEventTerminalRemovals <= 0) return false;
            if (deckIndex >= 0 && RemoveTerminalForEvent(deckIndex) == null) return false;
            if (deckIndex < 0) Log("イベント: 山札から端子は抜かなかった");
            PendingEventTerminalRemovals--;
            return true;
        }

        /// <summary>イベントで山札の deckIndex 枚目を抜く (下限は休憩・店と同じ)。 抜いた端子の名前・抜けなければ null。
        /// 〈研ぎ代の領収書〉(店・休憩で抜くたび) には数えない。</summary>
        private string RemoveTerminalForEvent(int deckIndex)
        {
            if (!CanRemoveTerminalAt(Run, deckIndex)) return null;
            string id = Run.terminalDeck[deckIndex];
            Run.terminalDeck.RemoveAt(deckIndex);
            Log($"イベント: 山札から抜いた: {id} (山札 {Run.terminalDeck.Count} 枚)");
            return id;
        }

        /// <summary>イベントの預かり (提示・抜く) を空にする (ランの始め)。</summary>
        private void ClearEventTerminalPending() { PendingEventTerminalOffers.Clear(); PendingEventTerminalRemovals = 0; TerminalOfferFromEvent = false; }

        /// <summary>報酬の候補を重ならないように n 個引く。 boss = レアから / それ以外 = 基本でもレアでもない端子から。
        /// 軸の端子は、 その軸の品を持っているほど出やすい (持っていなければ出にくい)。</summary>
        public static List<string> RollTerminalReward(RunState run, int n, bool boss)
            => RollTerminals(run, n, d => d.rare == boss, 1f, "terminal.reward");

        /// <summary>重み付きで重ならないように n 枚引く (基本と強化した端子は出さない ── 強化は休憩でだけ得る・2026-11-20)。 軸の端子は偏重で寄せる。
        /// 出現の枠が 4 つ埋まった後は、 枠に無い軸の端子は出さない (<see cref="ItemSystem.AxisPool.AxisOpen"/>)。
        /// rareWeight = レアの重みに掛ける (報酬は rare で絞るので 1)。 focusAxis = 店の軸指定リロール (その軸の端子の重み ×<see cref="ItemSystem.Shop.ShopManager.AxisRerollWeightMul"/>・null = なし)。</summary>
        private static List<string> RollTerminals(RunState run, int n, System.Func<TerminalDef, bool> filter, float rareWeight, string rngKey, string focusAxis = null)
        {
            var pool = new List<TerminalDef>(); var w = new List<float>();
            foreach (var d in TerminalCards.All)
            {
                if (d.basic || d.upgraded || !filter(d)) continue;
                if (!ItemSystem.AxisPool.AxisOpen(run, d.axis)) continue;   // 出現の枠 (4 枠ロック後はロックした軸の端子だけ)
                float wt = 1f;
                if (d.axis != null)
                {
                    int depth = ItemSystem.PassiveSkills.AxisRules.Count(run, d.axis);
                    wt = depth <= 0 ? 0.3f : 1f + 0.5f * depth;
                }
                if (d.rare) wt *= rareWeight;
                if (focusAxis != null && d.axis == focusAxis) wt *= ItemSystem.Shop.ShopManager.AxisRerollWeightMul;
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
            var shelf = new List<string>(); foreach (var s in TerminalShop) shelf.Add(s.id);
            TerminalDiag.NoteShopShelf(shelf);   // [計装 #106]
        }

        /// <summary>軸指定リロール (2026-11-20・<see cref="ShopReroll"/>): 端子の棚の売れていない枠を、 focusAxis の端子を出やすくして引き直す
        /// (棚に残る売れた端子とは重ねない・乱数は棚と同じキー "terminal.shop")。 値段の付け方は <see cref="RollTerminalShop"/> と同じ。</summary>
        private void RerollTerminalShop(string focusAxis)
        {
            if (Run == null || TerminalShop.Count == 0) return;
            var sold = new HashSet<string>(); int open = 0;
            foreach (var s in TerminalShop) { if (s.sold) sold.Add(s.id); else open++; }
            if (open == 0) return;
            float mul = ItemSystem.Shop.ShopManager.Instance?.Current?.priceMultiplier ?? 1f;
            var ids = RollTerminals(Run, open, d => !sold.Contains(d.id), TerminalShopRareWeight, "terminal.shop", focusAxis);
            int k = 0;
            foreach (var s in TerminalShop)
            {
                if (s.sold || k >= ids.Count) continue;
                var d = TerminalCards.Get(ids[k++]);
                s.id = d.id;
                s.price = Mathf.Max(1, Mathf.CeilToInt((d.rare ? TerminalShopPriceRare : d.disposable ? TerminalShopPriceDisposable : TerminalShopPriceCommon) * mul));
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
            MetaProgression.Achievements.AchievementService.NoteTerminalDeck(Run);   // 実績〈紙の城〉
            MetaProgression.Achievements.AchievementService.NoteShopPurchase(Run.coins, countsAsItem: false, surcharged: false);   // 端子は品ではない (清貧に数えない)
            TerminalDiag.NoteShopBuy(s.id);   // [計装 #106]
            Log($"端子を買った: {s.id} (-{s.price}G・山札 {Run.terminalDeck.Count} 枚)");
            return true;
        }

        /// <summary>山札から抜ける (抜いても手札の枚数を下回らない)。</summary>
        public bool CanRemoveTerminal => CanRemoveTerminalOf(Run);

        /// <summary>同上 (ランを渡す版・イベントの「山札から端子を抜く」が選べるかの判定も読む)。</summary>
        public static bool CanRemoveTerminalOf(RunState run) => run?.terminalDeck != null && run.terminalDeck.Count > TerminalCards.MinDeckSize;

        /// <summary>山札の deckIndex 枚目を抜けるか (下限 <see cref="CanRemoveTerminalOf"/> に加えて、 最後の攻撃の端子は抜けない ── <see cref="TerminalCards.KeepsLastAttack"/>・2026-11-21)。
        /// 店・休憩・イベントで抜く所と、 戦闘で使ってランの山札から抜ける端子 (CombatManager.RemoveUsedFromRunDeck) が通す。</summary>
        public static bool CanRemoveTerminalAt(RunState run, int deckIndex)
        {
            if (!CanRemoveTerminalOf(run) || deckIndex < 0 || deckIndex >= run.terminalDeck.Count) return false;
            var rest = new List<string>(run.terminalDeck); rest.RemoveAt(deckIndex);
            return !TerminalCards.KeepsLastAttack(run.terminalDeck[deckIndex], rest);
        }

        /// <summary>店で山札から 1 枚抜く値段 (店で抜くたびに上がる)。 整備パネル〈圧縮の心得〉ならランで最初の 1 回は 0 (2 回目からは今までどおり 2 回目の値段)。</summary>
        public int TerminalRemovePrice
            => Run != null && Run.terminalShopRemovals == 0 && MetaProgression.MetaBuffApplicator.IsFirstShopRemovalFree() ? 0
             : TerminalRemoveBasePrice + TerminalRemoveStep * (Run != null ? Run.terminalShopRemovals : 0);

        /// <summary>店のサービス: 山札の deckIndex 枚目を抜く (払えない・抜けないなら false)。</summary>
        public bool ShopRemoveTerminal(int deckIndex)
        {
            if (CurrentPhase != GamePhase.ShopVisit || !CanRemoveTerminalAt(Run, deckIndex)) return false;
            int price = TerminalRemovePrice;
            if (Run.coins < price || !ProvisionPayment.TryPay(Run, price, "端子を抜く")) return false;
            string id = Run.terminalDeck[deckIndex];
            Run.terminalDeck.RemoveAt(deckIndex);
            Run.terminalShopRemovals++;
            NoteReceiptRemoval();
            TerminalDiag.NoteRemoved(id, TerminalDiag.RemovedShop);   // [計装 #106]
            Log($"山札から抜いた (店): {id} (-{price}G・店で {Run.terminalShopRemovals} 回目・山札 {Run.terminalDeck.Count} 枚)");
            return true;
        }

        /// <summary>〈研ぎ代の領収書〉: 店・休憩で抜くたび、 ランの間ずっと攻撃 +2 (持っている間に抜いた回数・効果は GoldEffects.cs の MacroReceipt)。</summary>
        private void NoteReceiptRemoval()
        {
            if (!Run.OwnsPassive(ItemIds.MacroReceipt)) return;
            Run.receiptRemovals++;
            Log($"〈{ItemIds.MacroReceipt}〉: 攻撃 +{ItemSystem.PassiveSkills.Effects.MacroReceipt.PerRemoval * Run.receiptRemovals}");
        }

        /// <summary>休憩: 回復の代わりに、 山札の deckIndex 枚目を抜く (無料・休憩の行動を使う)。 抜けなければ false (休憩は続く)。</summary>
        public bool RestRemoveTerminal(int deckIndex)
        {
            if (CurrentPhase != GamePhase.RestStop || !CanRemoveTerminalAt(Run, deckIndex)) return false;
            string id = Run.terminalDeck[deckIndex];
            Run.terminalDeck.RemoveAt(deckIndex);
            TerminalDiag.NoteRemoved(id, TerminalDiag.RemovedRest);   // [計装 #106]
            NoteReceiptRemoval();
            Log($"山札から抜いた (休憩): {id} (山札 {Run.terminalDeck.Count} 枚)");
            ReturnToMapOrClearFloor();
            return true;
        }
    }
}
