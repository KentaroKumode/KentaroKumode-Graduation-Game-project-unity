using System.Collections.Generic;
using EventSystem;
using GameLoop;
using ItemSystem;
using ItemSystem.Shop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 地図・戦闘の外の場面 (ショップ・イベント・報酬・休憩・秘宝・罠・交換・層の終わり・〈門〉・エンディング・ゲームオーバー) を、
    /// <b>実データで</b> <see cref="Game2DModal"/> に出す (<b>仮置き</b>・2026-10-31)。 文字は日本語のドット字。
    /// ボタンは GameManager の既存の操作 (キー入力の口と同じ関数) を呼ぶだけで、 ゲームの処理は持たない。
    /// 戦闘と戦闘結果は 2D の戦闘画面 (<see cref="Battle.Game2DBattleSim"/>) が受け持つ。 BOT がランを動かしている間は何もしない。
    /// </summary>
    public class Game2DPhaseScreens : MonoBehaviour
    {
        public Game2DModal modal;

        private GameManager.GamePhase _shown = GameManager.GamePhase.Title;
        private bool _open, _dirty;
        private int _lastCoinsGained, _lastCoins = -1, _lastHp = -1, _lastPending = -1;
        private string _eventResult;
        private readonly bool[] _gateOffer = new bool[3];
        private bool _subscribed;

        /// <summary>シーンに無ければ付ける (シーンは git 管理外なので、 builder を回していない環境でも動くように)。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            var m = FindObjectOfType<Game2DModal>();
            if (m != null && m.GetComponent<Game2DPhaseScreens>() == null) m.gameObject.AddComponent<Game2DPhaseScreens>().modal = m;
        }

        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) return;
            var gm = GameManager.Instance; if (gm == null) return;
            gm.OnRewardGranted += OnRewardGranted;
            if (EventEncounter.Instance != null) EventEncounter.Instance.OnEventResolved += OnEventResolved;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var gm = GameManager.Instance; if (gm != null) gm.OnRewardGranted -= OnRewardGranted;
            if (EventEncounter.Instance != null) EventEncounter.Instance.OnEventResolved -= OnEventResolved;
            _subscribed = false;
        }

        private void OnRewardGranted(int coins) { _lastCoinsGained = coins; _dirty = true; }

        private void OnEventResolved(EventChoice choice, EventEffectExecutor.ExecutionResult result)
        {
            var sb = new System.Text.StringBuilder();
            if (result?.log != null) foreach (var line in result.log) sb.Append("・").Append(line).Append('\n');
            if (!string.IsNullOrEmpty(choice?.postFlavor)) sb.Append(choice.postFlavor);
            _eventResult = sb.ToString().TrimEnd('\n');
            _dirty = true;
        }

        private void Update()
        {
            if (modal == null) return;
            var gm = GameManager.Instance;
            if (gm == null || GameHooks.BotDriving || Application.isBatchMode) { CloseIfOpen(); return; }
            Subscribe();
            var ph = gm.CurrentPhase;
            if (ph != _shown) { _shown = ph; _dirty = true; _shopSell = false; _shopSupply = false; _robArmed = false; _sellPage = 0; _restFaces = false; _shopTerm = false; _termPick = TermPick.None; _termDeckPage = 0; if (ph != GameManager.GamePhase.EventEncounter) _eventResult = null; if (ph == GameManager.GamePhase.GateRitual) System.Array.Clear(_gateOffer, 0, 3); }
            // 画面の外で所持金・HP が変わった時も描き直す (買える品の明暗・副題の数字)
            int coins = gm.Run != null ? gm.Run.coins : 0, hp = gm.Run != null ? gm.Run.playerHP : 0;
            if (coins != _lastCoins || hp != _lastHp) { _lastCoins = coins; _lastHp = hp; if (_open) _dirty = true; }
            int pend = gm.PendingStartingPassiveChoices.Count + gm.PendingChallengeTiers.Count * 100 + (gm.Run != null ? gm.Run.pendingWeaponIds.Count * 10000 + gm.Run.pendingTerminalOffer.Count * 1000000 : 0);
            if (pend != _lastPending) { _lastPending = pend; _dirty = true; }
            if (!_dirty) return;
            // ロード画面の間は出さない (終わってから出す・_dirty は残す)。 開いている窓も閉じる ──
            //   「層を抜けた」の窓は押した直後にロード画面が始まるので、 閉じないとロード画面の上に残り続けていた (2026-11-11)
            if (Game2DLoadingScreen.Blocking) { CloseIfOpen(); return; }
            _dirty = false;
            var def = Build(gm, ph);
            if (def == null) { CloseIfOpen(); return; }
            if (_open) modal.Replace(def); else modal.Open(def);
            _open = true;
        }

        private void CloseIfOpen() { if (_open) { modal.Close(); _open = false; } }

        /// <summary>操作の後に作り直す (フェーズが変わらない操作 ── 買った・振り直した など)。</summary>
        private void Refresh() { _dirty = true; }

        private static ScreenDef New(string title, string sub = null, int w = 360)
            => new ScreenDef { id = "phase", jp = true, title = title, sub = sub, w = w };

        private static ModalBtn B(string label, string action, bool primary = false) => new ModalBtn { label = label, action = action, primary = primary };

        private ScreenDef Build(GameManager gm, GameManager.GamePhase ph)
        {
            var run = gm.Run;
            if (run != null && run.pendingWeaponIds.Count > 0 && ph != GameManager.GamePhase.Combat && ph != GameManager.GamePhase.BattleResult) return WeaponSwap(gm);
            if (run != null && run.pendingTerminalOffer.Count > 0 && ph != GameManager.GamePhase.Combat && ph != GameManager.GamePhase.BattleResult)
                return TerminalReward(gm);      // 山札 (ADR-0014) の報酬
            _termDeckView = false;
            if (ph == GameManager.GamePhase.MapNavigation && gm.PendingStartingPassiveChoices.Count > 0) return StartingPick(gm);
            if (ph == GameManager.GamePhase.MapNavigation && gm.PendingChallengeTiers.Count > 0) return ChallengePick(gm);
            switch (ph)
            {
                case GameManager.GamePhase.ShopVisit: return Shop(gm);
                case GameManager.GamePhase.EventEncounter: return Event(gm);
                case GameManager.GamePhase.Reward: return Reward(gm);
                case GameManager.GamePhase.RestStop: return Rest(gm);
                case GameManager.GamePhase.TreasureOpen:
                {
                    var d = New("秘宝"); d.body = string.IsNullOrEmpty(gm.LastTreasureSummary) ? "宝箱を開けた。" : gm.LastTreasureSummary;
                    d.buttons.Add(B("受け取る", "ok", true));
                    d.onAction = (a, _) => { gm.ConfirmTileEvent(); Refresh(); };
                    return d;
                }
                case GameManager.GamePhase.TrapTriggered:
                {
                    var d = New("罠"); d.body = "罠を踏んだ。";
                    d.buttons.Add(B("次へ", "ok", true));
                    d.onAction = (a, _) => { gm.ConfirmTileEvent(); Refresh(); };
                    return d;
                }
                case GameManager.GamePhase.ExchangeTile:
                {
                    var d = New("交換"); bool can = gm.CanExchangeTile;
                    d.body = can ? "いちばん段の低いパッシブを 1 つ渡し、 上の段のものを 1 つ受け取る。" : "渡せるパッシブが無い。";
                    if (can) d.buttons.Add(B("交換する", "swap", true));
                    d.buttons.Add(B("通り過ぎる", "skip", !can));
                    d.onAction = (a, _) => { if (a == "swap") gm.DoExchangeTile(); else gm.SkipExchangeTile(); Refresh(); };
                    return d;
                }
                case GameManager.GamePhase.FloorClear:
                {
                    // ボスを倒した直後の場面があれば、 それを先に読ませる (6 層・灰燼の王 → 椅子)
                    string after = Endings.AftermathOf(run);
                    var d = New("層を抜けた", run.currentFloor + " 層"); d.body = after != null ? after + "\n\n次の層へ降りる。" : "次の層へ降りる。";
                    if (after != null) d.bodyLines = 9;
                    d.buttons.Add(B("次の層へ", "ok", true));
                    d.onAction = (a, _) => { gm.ConfirmFloorClear(); Refresh(); };
                    return d;
                }
                case GameManager.GamePhase.GateRitual: return Gate(gm);
                case GameManager.GamePhase.RiftChoice:
                {
                    var d = New("裂け目の前", null, 420); d.body = "ヴェスカは倒れた。 裂け目の前に立っている。";
                    d.Items.Add(new ModalItem { name = "裂け目を閉じる", desc = "自分と遺物の山を対価に、 こちら側と向こう側の帳尻を合わせる。" });
                    d.Items.Add(new ModalItem { name = "裂け目を閉じない", desc = "大穴の広がる速さを見るに、 亡びは孫の孫より遠い。" });
                    d.buttons.Add(B("決める", "do", true));
                    d.onAction = (a, sel) => { if (a == "do") gm.ChooseRiftFate(sel == 1); Refresh(); };
                    return d;
                }
                case GameManager.GamePhase.RunClear:
                case GameManager.GamePhase.GameOver:
                {
                    bool clear = ph == GameManager.GamePhase.RunClear;
                    var d = New(clear ? "踏破" : "力尽きた");
                    d.body = run != null ? "到達 " + run.currentFloor + " 層 ／ 戦闘 " + run.totalBattles + " ／ 勝利 " + run.totalWins + " ／ 所持金 " + run.coins + "G" : "";
                    // 入手した遺物 (§15-5・2026-11-10): ランの終わりに落ちた分を見せる (所持品へは自動で入っている)
                    foreach (var rel in MetaProgression.MetaBuffApplicator.LastRunRelics)
                        d.Items.Add(new ModalItem { name = "入手: " + rel.Name, value = "★" + rel.stars,
                            desc = MetaProgression.Relics.RelicCatalog.Describe(rel.MainStat, rel.MainStep) + (rel.SubCount > 0 ? " ほか サブ " + rel.SubCount + " 個" : "") });
                    d.buttons.Add(B("タイトルへ", "ok", true));
                    d.onAction = (a, _) => { gm.ReturnToTitle(); Refresh(); };
                    return d;
                }
                default: return null;      // 地図・戦闘・戦闘結果・タイトルは別の画面
            }
        }

        // ── 開幕パッシブの選択 (〈宝物庫〉r10: 何本目かで 2〜3 択) ──

        private ScreenDef StartingPick(GameManager gm)
        {
            var cands = gm.PendingStartingPassiveChoices[0];
            var d = New("開幕のパッシブ", "残り " + gm.PendingStartingPassiveChoices.Count, 420);
            d.body = "このランに持っていくパッシブを 1 つ選ぶ。";
            foreach (var id in cands)
            {
                var data = ItemDatabase.Instance?.GetItem(id);
                d.Items.Add(new ModalItem { name = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : id, desc = data?.description });
            }
            d.buttons.Add(B("選ぶ", "pick", true));
            d.onAction = (a, sel) => { if (a == "pick") { gm.ResolveStartingPassive(Mathf.Clamp(sel, 0, cands.Count - 1)); Game2DSfx.Play("ui_confirm"); } Refresh(); };
            return d;
        }

        // ── 挑戦カード (docs/GAME.md §23-25): 層の入口で 2 枚から 1 枚。 選んだカードのデバフとバフを両方受ける ──

        private ScreenDef ChallengePick(GameManager gm)
        {
            var offer = gm.ChallengeOffer; int tier = gm.PendingChallengeTiers[0];
            var d = New("試練をひとつ選ぶ", "Tier " + Game2DModal.ChallengeRomanOf(tier) + (gm.PendingChallengeTiers.Count > 1 ? "  残り " + gm.PendingChallengeTiers.Count : ""), 460);
            d.body = "選んだ札のデバフとバフを、 両方受ける。";
            d.bodyLines = 1;
            d.rowH = Game2DModal.ChallengeRowH;
            if (offer != null)
                foreach (var card in offer)
                {
                    MetaProgression.ChallengeCards.Split(card, out var deb, out var buf);
                    d.Items.Add(new ModalItem { challengeTier = tier, deb = MetaProgression.ChallengeCards.Text(deb), buf = MetaProgression.ChallengeCards.Text(buf) });
                }
            d.buttons.Add(B("受ける", "pick", true));
            d.onAction = (a, sel) => { if (a == "pick") { gm.ResolveChallengePick(Mathf.Clamp(sel, 0, d.Items.Count - 1)); Game2DSfx.Play("ui_confirm"); } Refresh(); };
            return d;
        }

        // ── 休憩 ──
        //   休む / 多めに休む (ターンが進む) / 鍛える / 面を外す (どの面かを選ぶ画面へ)。 どれも 1 回の休憩で 1 つだけ。

        private bool _restFaces;

        private ScreenDef Rest(GameManager gm)
        {
            var run = gm.Run;
            Game2DTutorial.Request(TutorialTopics.Equip);      // 手引き「装備と強化」: 初めて休憩に入った (武器の強化・面を外す)
            if (_restFaces) return RestFaces(gm);
            if (_termPick == TermPick.Rest) return TerminalDeckList(gm);
            var d = New("休憩", "HP " + run.playerHP + "/" + run.playerMaxHP + "  素材 " + run.weaponMaterials);
            int more = Mathf.RoundToInt(MapSystem.FreeMove.FreeMapParams.RestMoreRatio * 100);
            d.Items.Add(new ModalItem { name = "休む", desc = "HP を回復する。 ターンは進まない。" });
            d.Items.Add(new ModalItem { name = "多めに休む", value = "+" + MapSystem.FreeMove.FreeMapParams.RestMoreTurns + " ターン", desc = "最大 HP の " + more + "% を回復する。 代わりにターンが進む (追跡者が動く)。" });
            d.Items.Add(new ModalItem { name = "鍛える", value = "素材 " + run.weaponMaterials, desc = "素材を使って武器を 1 段強化する (強化できなければ休む)。" });
            bool canRemove = gm.TryGetFaceRemovalCost(out int g, out int m);
            d.Items.Add(new ModalItem { name = "面を外す", value = canRemove ? g + "G 素材" + m : "外せない", disabled = !canRemove,
                desc = canRemove ? "ダイスの面を 1 枚外す (出目の合計と、 その目の役を失う)。 外す面を次に選ぶ。" : "面はこれ以上外せない。" });
            // 端子の山札 (ADR-0014): 回復の代わりに 1 枚抜く (無料)。 山札が手札の枚数を下回る抜き方はできない
            bool canTerm = gm.CanRemoveTerminal; int deckN = run.terminalDeck != null ? run.terminalDeck.Count : 0;
            d.Items.Add(new ModalItem { name = "山札から抜く", value = canTerm ? "山札 " + deckN + " 枚" : "抜けない", disabled = !canTerm,
                desc = canTerm ? "回復の代わりに、 山札から端子を 1 枚抜く (この先ずっと)。 抜く端子を次に選ぶ。" : "山札が下限の枚数 (" + CombatSystem.TerminalCards.MinDeckSize + ") を下回る抜き方はできない。" });
            d.buttons.Add(B("決める", "do", true));
            d.onAction = (a, sel) =>
            {
                if (a != "do") return;
                switch (sel)
                {
                    case 1: gm.RestHealMore(); break;
                    case 2: gm.RestUpgrade(); break;
                    case 3: if (canRemove) _restFaces = true; break;
                    case 4: if (canTerm) { _termPick = TermPick.Rest; _termDeckPage = 0; } break;
                    default: gm.RestHeal(); break;
                }
                Refresh();
            };
            return d;
        }

        private ScreenDef RestFaces(GameManager gm)
        {
            var run = gm.Run; gm.TryGetFaceRemovalCost(out int g, out int m);
            var d = New("面を外す", g + "G・素材 " + m + "  (所持 " + run.coins + "G・素材 " + run.weaponMaterials + ")", 420); d.cols = 2;
            var faces = new List<int>();
            if (run.diceFaceParts != null)
                foreach (var p in run.diceFaceParts)
                {
                    faces.Add(p.face);
                    d.Items.Add(new ModalItem { name = DiceFaceParts.Label(p), desc = DiceFaceParts.Describe(p.tier) });
                }
            bool afford = run.coins >= g && run.weaponMaterials >= m;
            d.buttons.Add(B("外す", "remove", true));
            d.buttons.Add(B("戻る", "back"));
            d.footer = afford ? "外す面を選ぶ" : "払えない (ゴールドか素材が足りない)";
            d.onAction = (a, sel) =>
            {
                if (a == "back") _restFaces = false;
                else if (a == "remove" && sel >= 0 && sel < faces.Count)
                {
                    if (gm.RestRemoveFace(faces[sel])) { _restFaces = false; Game2DSfx.Play("ui_confirm"); }
                    else modal.ShowToast("外せない (ゴールドか素材が足りない)");
                }
                Refresh();
            };
            return d;
        }

        // ── ショップ ──
        //   買う画面: 棚の品 + どの店にもある売り物 (物資の包・囮罠)。 売る画面: 所持品 (パッシブ・消耗品・強化素材) を頁で送る。
        //   強盗は 2 度押しで確定 (商人との戦闘になり、 以降の店の値段が上がる・1 ラン 1 回・整備パネルで解禁)。

        private bool _shopSell, _shopSupply, _robArmed;
        private int _sellPage;
        private const int SellPerPage = 10;

        private struct SellEntry { public ShopManager.SellSource src; public int index; public string name, desc, icon; public ItemRarity? rarity; }

        private static List<SellEntry> SellList(RunState run)
        {
            var l = new List<SellEntry>();
            if (run.ownedPassiveItems != null)
                for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                    if (run.ownedPassiveItems[i] != run.equippedWeaponId)      // 装備中の武器は売らせない (武器もパッシブの一覧に入っている)
                        l.Add(Entry(ShopManager.SellSource.Passive, i, run.ownedPassiveItems[i]));
            if (run.ownedConsumables != null)
                for (int i = 0; i < run.ownedConsumables.Count; i++) l.Add(Entry(ShopManager.SellSource.Consumable, i, run.ownedConsumables[i]));
            if (run.weaponMaterials > 0)
                l.Add(new SellEntry { src = ShopManager.SellSource.WeaponMaterial, index = 0, name = "強化素材 (所持 " + run.weaponMaterials + ")", desc = "1 つ売る。", icon = "" });
            return l;
        }

        private static SellEntry Entry(ShopManager.SellSource src, int i, string id)
        {
            var data = ItemDatabase.Instance?.GetItem(id);
            return new SellEntry { src = src, index = i, name = data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : id, desc = data?.description, icon = data != null ? data.id : id, rarity = data != null ? data.rarity : (ItemRarity?)null };
        }

        private ScreenDef Shop(GameManager gm)
        {
            Game2DTutorial.Request(TutorialTopics.Shop);      // 手引き「ショップ」: 初めてショップに入った
            if (_termPick != TermPick.None) return TerminalDeckList(gm);
            if (_shopTerm && !_shopSell) return ShopTerminals(gm);
            return _shopSell ? ShopSell(gm) : ShopBuy(gm);
        }

        // ── 店の端子 (ADR-0014): 端子の棚 (山札に加える) と、 山札から 1 枚抜くサービス (店で抜くたびに値段が上がる) ──

        private bool _shopTerm;

        private ScreenDef ShopTerminals(GameManager gm)
        {
            var run = gm.Run; int deckN = run.terminalDeck != null ? run.terminalDeck.Count : 0;
            var d = New("ショップ ― 端子", "所持金 " + run.coins + "G  山札 " + deckN + " 枚", 460);
            var shelf = gm.TerminalShop;
            foreach (var s in shelf)
            {
                var t = CombatSystem.TerminalCards.Get(s.id);
                bool sealedAx = CombatSystem.TerminalCards.AxisSealedByRun(t, run);
                d.Items.Add(new ModalItem
                {
                    name = s.id, tone = t != null ? Game2DTerminalLook.KindColor(t.kind) : (Color32?)null,
                    value = s.sold ? "売切" : s.price + "G",
                    desc = t != null ? Game2DTerminalLook.Marks(t, run) + "\n" + t.description + (sealedAx ? "\n誓約で封じられている" : "") : null,
                    disabled = s.sold || s.price > run.coins,
                });
            }
            int removeRow = d.Items.Count, price = gm.TerminalRemovePrice; bool canRemove = gm.CanRemoveTerminal;
            d.Items.Add(new ModalItem
            {
                name = "山札から 1 枚抜く", value = canRemove ? price + "G" : "抜けない",
                desc = canRemove ? "山札から端子を 1 枚抜く (この先ずっと)。 抜くたびに値段が上がる。 抜く端子を次に選ぶ。"
                                 : "山札が下限の枚数 (" + CombatSystem.TerminalCards.MinDeckSize + ") を下回る抜き方はできない。",
                disabled = !canRemove || price > run.coins,
            });
            d.buttons.Add(B("決める", "do", true));
            d.buttons.Add(B("山札を見る", "deck"));
            d.buttons.Add(B("棚へ", "back"));
            d.buttons.Add(B("出る", "leave"));
            d.footer = "端子を選んで「決める」で山札に加える";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "do":
                        if (sel == removeRow)
                        {
                            if (canRemove && price <= run.coins) { _termPick = TermPick.Shop; _termDeckPage = 0; }
                            else modal.ShowToast(canRemove ? "所持金が足りない" : "これ以上は抜けない");
                        }
                        else if (sel >= 0 && sel < shelf.Count)
                        {
                            if (gm.ShopBuyTerminal(sel)) Game2DSfx.Play("ui_confirm"); else modal.ShowToast("買えない (所持金が足りない・売り切れ)");
                        }
                        break;
                    case "deck": _termPick = TermPick.View; _termDeckPage = 0; break;
                    case "back": _shopTerm = false; break;
                    case "leave": _shopTerm = false; gm.ExitShop(); break;
                }
                Refresh();
            };
            return d;
        }

        private ScreenDef ShopBuy(GameManager gm)
        {
            var run = gm.Run; var sm = ShopManager.Instance; var inv = sm != null ? sm.Current : null;
            var d = New(_shopSupply ? "ショップ ― 補給" : "ショップ", "所持金 " + run.coins + "G  物資 " + run.provision + "/" + run.provisionCap + "  武器 " + WeaponLabel(run.equippedWeaponId), 460); d.cols = 2;
            int n = 0;
            if (inv != null && !_shopSupply)
            {
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var s = inv.slots[i];
                    int price = s.kind == ShopSlotKind.WeaponMaterial ? inv.CurrentMaterialPrice : s.price;
                    int tradeIn = sm.WeaponTradeIn(i, run); price = Mathf.Max(0, price - tradeIn);      // 武器の乗り換えは差額
                    bool sold = s.kind != ShopSlotKind.WeaponMaterial && s.sold;
                    d.Items.Add(new ModalItem
                    {
                        name = SlotName(s), desc = tradeIn > 0 ? SlotDesc(s) + "\n※ 武器は 1 本だけ。 装備中の「" + WeaponLabel(run.equippedWeaponId) + "」を " + tradeIn + "G で下取りに出し、 差額で乗り換える" : SlotDesc(s),
                        icon = SlotIcon(s), rarity = SlotRarity(s),
                        value = sold ? "売切" : tradeIn > 0 ? "差額 " + price + "G" : price + "G",
                        disabled = sold || price > run.coins,
                    });
                }
                n = inv.slots.Count;
            }
            // どの店にもある売り物 (棚とは別の頁。 棚は 12 品で窓が埋まる)
            int packs = sm != null ? sm.ProvisionPacksLeft : 0, packPrice = ShopManager.ProvisionPackPrice, lurePrice = MapSystem.FreeMove.FreeMapParams.ShopLurePrice;
            if (_shopSupply) d.Items.Add(new ModalItem
            {
                name = "物資の包 (残り " + packs + ")", value = packPrice + "G", icon = "",
                desc = "物資 +" + ShopManager.ProvisionPackAmount + "。 物資を補える売り物はこれだけ。 1 つの店で " + ShopManager.ProvisionPackStock + " 包まで。",
                disabled = packs <= 0 || run.coins < packPrice || run.provision >= run.provisionCap,
            });
            if (_shopSupply) d.Items.Add(new ModalItem { name = ItemDatabase.DisplayNameOf(ItemIds.Lure), value = lurePrice + "G", icon = "", desc = "航行で置くと、 追跡者を罠の方へ引きつける。 在庫に限りは無い。", disabled = run.coins < lurePrice });
            int packRow = _shopSupply ? 0 : -1, lureRow = _shopSupply ? 1 : -1;

            bool robOk = sm != null && !run.shopRobberyDone && MetaProgression.MetaBuffApplicator.IsShopRobberyUnlocked();
            d.buttons.Add(B("買う", "buy", true));
            if (inv != null && !_shopSupply) d.buttons.Add(B("振り直す " + inv.CurrentRerollPrice + "G", "reroll"));
            d.buttons.Add(B(_shopSupply ? "棚へ" : "補給 (物資)", "supply"));
            d.buttons.Add(B("端子", "term"));
            d.buttons.Add(B("売る", "sellmode"));
            if (robOk) d.buttons.Add(B(_robArmed ? "本当に強盗" : "強盗", "rob"));
            d.buttons.Add(B("出る", "leave"));
            d.footer = _robArmed ? "強盗: 商人と戦う。 勝てば棚の品を奪う。 以降の店は値段 ×" + ShopManager.RobberySurcharge.ToString("0.#") + "。 もう一度押すと実行"
                     : _weaponArmed >= 0 ? "武器の乗り換え: 「" + WeaponLabel(run.equippedWeaponId) + "」を下取りに出す。 もう一度「買う」で買う" : "品を選んで「買う」";
            d.onAction = (a, sel) =>
            {
                if (a != "rob") _robArmed = false;
                int armed = _weaponArmed; if (a != "buy") _weaponArmed = -1;
                switch (a)
                {
                    case "buy":
                    {
                        if (sel == packRow) { if (sm != null && sm.TryBuyProvisionPack(run)) Game2DSfx.Play("ui_confirm"); else modal.ShowToast(run.provision >= run.provisionCap ? "物資は満タン" : "買えない (所持金が足りない・品切れ)"); break; }
                        if (sel == lureRow) { if (sm != null && sm.TryBuyLure(run)) Game2DSfx.Play("ui_confirm"); else modal.ShowToast("買えない (所持金が足りない・持ちきれない)"); break; }
                        if (_shopSupply) break;
                        // 武器は 1 本だけ: 別の武器を持っている時は、 1 度目の「買う」で入れ替えの警告を出し、 2 度目で買う
                        if (inv != null && sel >= 0 && sel < inv.slots.Count && inv.slots[sel].kind == ShopSlotKind.Weapon && !inv.slots[sel].sold
                            && HoldsOtherWeapon(run, inv.slots[sel].itemId) && armed != sel)
                        { _weaponArmed = sel; modal.ShowToast("「" + WeaponLabel(run.equippedWeaponId) + "」を下取りに出して乗り換える"); break; }
                        _weaponArmed = -1;
                        int before = run.coins; gm.ShopBuy(sel);
                        if (run.coins == before && sel >= 0 && inv != null && sel < inv.slots.Count && !inv.slots[sel].sold) modal.ShowToast("買えない (所持金が足りない・売り切れ)");
                        else Game2DSfx.Play("ui_confirm");
                        break;
                    }
                    case "supply": _shopSupply = !_shopSupply; break;
                    case "term": _shopTerm = true; _shopSupply = false; break;
                    case "reroll": if (!gm.ShopReroll()) modal.ShowToast("振り直せない (所持金が足りない)"); break;
                    case "sellmode": _shopSell = true; _sellPage = 0; if (!gm.ShopSellMode) gm.ToggleShopSellMode(); break;
                    case "rob": if (!_robArmed) _robArmed = true; else { _robArmed = false; gm.ShopRobbery(); } break;
                    case "leave": gm.ExitShop(); break;
                }
                Refresh();
            };
            return d;
        }

        private int _weaponArmed = -1;      // 入れ替えの警告を出した武器の行 (もう一度「買う」で買う)

        private static bool HoldsOtherWeapon(RunState run, string id) => run != null && !string.IsNullOrEmpty(run.equippedWeaponId) && run.equippedWeaponId != id;

        private static string WeaponLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "なし";
            var w = ItemDatabase.Instance?.GetItem(id);
            return w != null && !string.IsNullOrEmpty(w.displayName) ? w.displayName : id;
        }

        private static string WeaponLine(RunState run, string id, bool equipped)
        {
            var w = string.IsNullOrEmpty(id) ? null : ItemDatabase.Instance?.GetItem(id);
            if (w == null) return id;
            string plus = equipped && run.weaponPlus > 0 ? " +" + run.weaponPlus : "";
            return "攻撃 " + w.attackPower + plus + "  会心 " + w.critRatePct + "%\n" + w.description;
        }

        // ── 端子の山札 (ADR-0014) の報酬 ──
        //   戦闘後 2 択・エリート 3 択・層ボスはレア 3 択 (候補は GameManager が入れる)。 1 枚を山札に加えるか、 受け取らない。
        //   「山札を見る」で今の山札 (名前ごとの枚数) を頁で送って読める。

        private bool _termDeckView;
        private int _termDeckPage;
        private const int TermDeckPerPage = 12;

        private ScreenDef TerminalReward(GameManager gm)
        {
            if (_termDeckView) return TerminalDeckList(gm);
            Game2DTutorial.Request(TutorialTopics.GrowDeck);      // 手引き「端子を増やす」: 初めて戦闘に勝って端子の報酬が出た (issue #86)
            var run = gm.Run; var offer = run.pendingTerminalOffer;
            var d = New("端子の報酬", "山札 " + (run.terminalDeck != null ? run.terminalDeck.Count : 0) + " 枚", 460);
            d.body = "1 枚を山札に加える。 加えなくてもよい。"; d.bodyLines = 1;
            foreach (var id in offer)
            {
                var t = CombatSystem.TerminalCards.Get(id);
                d.Items.Add(new ModalItem
                {
                    name = id, tone = t != null ? Game2DTerminalLook.KindColor(t.kind) : (Color32?)null,
                    value = t != null ? Game2DTerminalLook.Marks(t, run) : null,
                    desc = t != null ? t.description : null,
                });
            }
            d.buttons.Add(B("山札に加える", "pick", true));
            d.buttons.Add(B("受け取らない", "skip"));
            d.buttons.Add(B("山札を見る", "deck"));
            d.footer = "受け取らないと、 候補はすべて流れる";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "pick": gm.ResolveTerminalReward(Mathf.Clamp(sel, 0, offer.Count - 1)); Game2DSfx.Play("ui_confirm"); break;
                    case "skip": gm.ResolveTerminalReward(-1); Game2DSfx.Play("ui_confirm"); break;
                    case "deck": _termDeckView = true; _termDeckPage = 0; break;
                }
                Refresh();
            };
            return d;
        }

        /// <summary>今の山札の中身 (名前ごとの枚数)。 並びは定義の順 (基本 → コモン → レア → 軸 → 使い捨て)。</summary>
        /// <summary>山札の一覧を何のために開いているか。 View = 見るだけ (店の端子の頁から) / Shop = 店で抜く端子を選ぶ / Rest = 休憩で抜く端子を選ぶ。
        /// None は報酬の窓の「山札を見る」(_termDeckView) の時。</summary>
        private enum TermPick { None, View, Shop, Rest }
        private TermPick _termPick;

        private ScreenDef TerminalDeckList(GameManager gm)
        {
            var run = gm.Run; var deck = run.terminalDeck ?? new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (var id in deck) { counts.TryGetValue(id, out int c); counts[id] = c + 1; }
            var names = new List<string>();
            foreach (var t in CombatSystem.TerminalCards.All) if (counts.ContainsKey(t.id)) names.Add(t.id);
            foreach (var id in counts.Keys) if (!names.Contains(id)) names.Add(id);      // 定義に無い名前 (古いセーブなど) も落とさない
            int pages = Mathf.Max(1, (names.Count + TermDeckPerPage - 1) / TermDeckPerPage); _termDeckPage = Mathf.Clamp(_termDeckPage, 0, pages - 1);
            var d = New("山札", "計 " + deck.Count + " 枚・" + names.Count + " 種", 460); d.cols = 2;
            int first = _termDeckPage * TermDeckPerPage, cnt = Mathf.Min(TermDeckPerPage, names.Count - first);
            for (int i = 0; i < cnt; i++)
            {
                string id = names[first + i]; var t = CombatSystem.TerminalCards.Get(id);
                d.Items.Add(new ModalItem
                {
                    name = id, value = "×" + counts[id], tone = t != null ? Game2DTerminalLook.KindColor(t.kind) : (Color32?)null,
                    desc = t != null ? Game2DTerminalLook.Describe(t, run) + (CombatSystem.TerminalCards.AxisSealedByRun(t, run) ? "\n誓約で封じられている" : "") : null,
                });
            }
            if (names.Count == 0) d.body = "山札は空。";
            bool pick = _termPick == TermPick.Shop || _termPick == TermPick.Rest;
            if (pick)
            {
                d.title = _termPick == TermPick.Shop ? "抜く端子を選ぶ ― " + gm.TerminalRemovePrice + "G" : "抜く端子を選ぶ ― 休憩";
                d.buttons.Add(B("抜く", "remove", true));
            }
            if (pages > 1) d.buttons.Add(B("次の頁 " + (_termDeckPage + 1) + "/" + pages, "page"));
            d.buttons.Add(B("戻る", "back", !pick));
            d.footer = pick ? "抜いた端子は、 このランの山札から消える" : "名前の色は種類 (赤 = 攻撃・青 = 防御・金 = 充電・緑 = 補助)";
            d.onAction = (a, sel) =>
            {
                if (a == "page") _termDeckPage = (_termDeckPage + 1) % pages;
                else if (a == "back") { _termDeckView = false; _termPick = TermPick.None; }
                else if (a == "remove" && pick && sel >= 0 && sel < cnt)
                {
                    int at = deck.IndexOf(names[first + sel]);
                    bool ok = _termPick == TermPick.Shop ? gm.ShopRemoveTerminal(at) : gm.RestRemoveTerminal(at);
                    if (ok) { Game2DSfx.Play("ui_confirm"); _termPick = TermPick.None; }
                    else modal.ShowToast("抜けない (所持金が足りない・山札が手札の枚数を下回る)");
                }
                Refresh();
            };
            return d;
        }

        /// <summary>武器の入れ替え (武器は 1 本だけ・2026-11-11)。 2 本目の武器を手に入れた時に、 どの場面でも先に聞く。</summary>
        private ScreenDef WeaponSwap(GameManager gm)
        {
            // (手引き「装備と強化」は休憩でだけ出す ── 作者の指示・2026-11-12。 武器を拾った時には出さない)
            var run = gm.Run; string cur = run.equippedWeaponId, next = run.pendingWeaponIds[0];
            var d = New("武器の入れ替え", "武器は 1 本しか持てない");
            d.Items.Add(new ModalItem { name = "装備中: " + WeaponLabel(cur), desc = WeaponLine(run, cur, true), icon = cur });
            d.Items.Add(new ModalItem { name = "手に入れた: " + WeaponLabel(next), desc = WeaponLine(run, next, false), icon = next });
            d.buttons.Add(B("入れ替える", "swap", true));
            d.buttons.Add(B("今のままにする", "keep"));
            d.footer = "入れ替えると「" + WeaponLabel(cur) + "」は手放す。 今のままにすると「" + WeaponLabel(next) + "」は置いていく";
            d.onAction = (a, _) => { Loadout.ResolvePendingWeapon(run, a == "swap"); Game2DSfx.Play("ui_confirm"); Refresh(); };
            return d;
        }

        private ScreenDef ShopSell(GameManager gm)
        {
            var run = gm.Run; var list = SellList(run);
            int pages = Mathf.Max(1, (list.Count + SellPerPage - 1) / SellPerPage); _sellPage = Mathf.Clamp(_sellPage, 0, pages - 1);
            var d = New("ショップ ― 売る", "所持金 " + run.coins + "G", 460); d.cols = 2;
            int first = _sellPage * SellPerPage, cnt = Mathf.Min(SellPerPage, list.Count - first);
            for (int i = 0; i < cnt; i++) { var e = list[first + i]; d.Items.Add(new ModalItem { name = e.name, desc = e.desc, icon = e.icon, rarity = e.rarity, value = e.src == ShopManager.SellSource.Consumable ? "消耗品" : e.src == ShopManager.SellSource.WeaponMaterial ? "素材" : null }); }
            if (list.Count == 0) d.body = "売れる物を持っていない。";
            d.buttons.Add(B("売る", "sell", true));
            if (pages > 1) d.buttons.Add(B("次の頁 " + (_sellPage + 1) + "/" + pages, "page"));
            d.buttons.Add(B("買う画面へ", "buymode"));
            d.footer = "売る物を選んで「売る」 (値はレア度で決まる)";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "sell":
                    {
                        if (sel < 0 || sel >= cnt) break;
                        var e = list[first + sel]; int before = run.coins;
                        // GameManager の売却は「売る対象の種類」を切り替えてから番号で売る (キー操作と同じ口)
                        if (!gm.ShopSellMode) gm.ToggleShopSellMode();
                        for (int k = 0; k < 3 && gm.ShopSellSource != e.src; k++) gm.CycleSellSource();      // 種類は 3 つ (パッシブ・消耗品・素材) を回る
                        if (gm.ShopSellSource != e.src) { modal.ShowToast("売れない"); break; }
                        gm.ShopSell(e.index);
                        if (run.coins > before) { Game2DSfx.Play("ui_confirm"); modal.ShowToast(e.name + " を売った +" + (run.coins - before) + "G"); }
                        else modal.ShowToast("売れない");
                        break;
                    }
                    case "page": _sellPage = (_sellPage + 1) % pages; break;
                    case "buymode": _shopSell = false; if (gm.ShopSellMode) gm.ToggleShopSellMode(); break;
                }
                Refresh();
            };
            return d;
        }

        private static string SlotName(ShopSlot s)
        {
            switch (s.kind)
            {
                case ShopSlotKind.WeaponMaterial: return "強化素材";
                case ShopSlotKind.FacePart: return "出目パーツ " + DiceFaceParts.Label(s.facePart);
            }
            if (string.IsNullOrEmpty(s.itemId)) return "(空)";
            var data = ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : s.itemId;
        }

        /// <summary>行の左のアイコン: 品は id (Resources/Icons/Items)、 素材・出目パーツは枠だけ ("")。</summary>
        /// <summary>行の枠のレアリティ (2026-11-08)。 素材・出目パーツ・品の無い枠は null (従来の灰色の枠)。</summary>
        private static ItemRarity? SlotRarity(ShopSlot s)
        {
            if (s.kind == ShopSlotKind.WeaponMaterial || s.kind == ShopSlotKind.FacePart || string.IsNullOrEmpty(s.itemId)) return null;
            var data = ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null ? data.rarity : (ItemRarity?)null;
        }

        private static string SlotIcon(ShopSlot s)
        {
            if (s.kind == ShopSlotKind.WeaponMaterial || s.kind == ShopSlotKind.FacePart || string.IsNullOrEmpty(s.itemId)) return "";
            var data = ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null ? data.id : s.itemId;
        }

        private static string SlotDesc(ShopSlot s)
        {
            switch (s.kind)
            {
                case ShopSlotKind.WeaponMaterial: return "武器を強化する素材 (休憩で使う)。 在庫に限りは無い。";
                case ShopSlotKind.FacePart: return DiceFaceParts.Describe(s.facePart.tier);
            }
            var data = string.IsNullOrEmpty(s.itemId) ? null : ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null ? data.description : null;
        }

        // ── イベント ──

        private ScreenDef Event(GameManager gm)
        {
            var ev = EventEncounter.Instance != null ? EventEncounter.Instance.Current : null;
            var d = New(ev != null ? ev.name : "イベント", null, 420);
            if (gm.EventChoiceResolved)
            {
                d.body = string.IsNullOrEmpty(_eventResult) ? "……" : _eventResult; d.bodyLines = 8;
                d.buttons.Add(B("次へ", "next", true));
                d.onAction = (a, _) => { gm.ConfirmEventEncounter(); Refresh(); };
                return d;
            }
            d.body = ev != null ? ev.flavor : null;
            // 選択肢の下に代償 (使うターン・補足中の捕まるまで・足りないゴールド) を出す (2026-11-11・issue #17)
            if (ev != null)
                for (int i = 0; i < ev.choices.Count; i++)
                {
                    var c = ev.choices[i];
                    d.Items.Add(new ModalItem { name = c.text, desc = EventEncounter.DescribeCost(c, gm.Run),
                                                disabled = !EventEncounter.IsChoiceSelectable(ev, i, gm.Run) });
                }
            d.buttons.Add(B("選ぶ", "pick", true));
            d.onAction = (a, sel) => { if (a == "pick" && ev != null && sel >= 0 && sel < ev.choices.Count) gm.ResolveEventChoice(sel); Refresh(); };
            return d;
        }

        // ── 報酬 ──

        private ScreenDef Reward(GameManager gm)
        {
            var d = New("報酬", _lastCoinsGained > 0 ? "+" + _lastCoinsGained + "G" : null);
            if (gm.HasPendingRewardChoice)
            {
                var (a, b) = gm.CurrentRewardChoice;
                d.body = "どちらか 1 つを受け取る。";
                foreach (var id in new[] { a, b })
                {
                    var data = ItemDatabase.Instance?.GetItem(id);
                    d.Items.Add(new ModalItem { name = data != null ? data.displayName : id, desc = data?.description });
                }
                d.buttons.Add(B("受け取る", "pick", true));
                d.onAction = (act, sel) => { if (act == "pick") gm.ResolveRewardChoice(Mathf.Clamp(sel, 0, 1)); Refresh(); };
                return d;
            }
            d.body = _lastCoinsGained > 0 ? _lastCoinsGained + "G を手に入れた。" : "戦利品を手に入れた。";
            d.buttons.Add(B("次へ", "ok", true));
            d.onAction = (act, _) => { _lastCoinsGained = 0; gm.ConfirmReward(); Refresh(); };
            return d;
        }

        // ── 〈門〉 ──

        private static readonly string[] GateName = { "血を捧げる", "遺物を焚く", "光に身を任せる" };
        private static readonly string[] GateDesc =
        {
            // 数は GameManager.Gate の定数から組み立てる (写しを持たない・#131)。
            "最大 HP −" + Mathf.RoundToInt(GameLoop.GameManager.GateBloodMaxHpPct * 100f) + "%。 拒むと〈不完全な修復〉を背負う。",
            "遺物を " + GameLoop.GameManager.GateRelicDemand + " 個焚く。 拒むと〈不完全な起動〉を背負う。",
            "物資 −" + GameLoop.GameManager.GateProvisionDemand + "。 拒むと〈不完全な転移〉を背負う。",
        };

        private ScreenDef Gate(GameManager gm)
        {
            var d = New("〈門〉", null, 420); d.body = "捧げるものを選んで起動する。 拒んだ工程のぶん、 〈不完全な〜〉を背負う。";
            for (int i = 0; i < 3; i++) d.Items.Add(new ModalItem { name = GateName[i], desc = GateDesc[i], value = _gateOffer[i] ? "捧げる" : "拒む" });
            d.buttons.Add(B("切り替える", "toggle"));
            d.buttons.Add(B("起動", "go", true));
            d.onAction = (a, sel) =>
            {
                if (a == "toggle" && sel >= 0 && sel < 3) _gateOffer[sel] = !_gateOffer[sel];
                else if (a == "go")
                {
                    gm.OfferGateBlood(_gateOffer[0]); gm.OfferGateRelics(_gateOffer[1]); gm.OfferGateTransfer(_gateOffer[2]);
                    gm.CompleteGateRitual();
                }
                Refresh();
            };
            return d;
        }
    }
}
