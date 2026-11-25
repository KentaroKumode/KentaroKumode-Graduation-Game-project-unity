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

        private void OnDestroy() { Unsubscribe(); GameManager.EventKeysByScreen = false; }

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
            // 確率分岐は引いた分岐の文 (分けて書いていなければ全分岐で同じ文・2026-11-12)
            string after = choice?.PostFlavorFor(result != null ? result.pickedBranch : -1);
            if (!string.IsNullOrEmpty(after)) sb.Append(after);
            _eventResult = sb.ToString().TrimEnd('\n');
            _evEpoch++;
            _dirty = true;
        }

        private void Update()
        {
            if (modal == null) return;
            var gm = GameManager.Instance;
            if (gm == null || GameHooks.BotDriving || Application.isBatchMode) { GameManager.EventKeysByScreen = false; CloseIfOpen(); return; }
            Subscribe();
            var ph = gm.CurrentPhase;
            // イベントの画面が出ている間は、 キーも画面が受ける (頁送り・2026-11-13)
            bool evKeys = ph == GameManager.GamePhase.EventEncounter && _evPlan != null && Game2DEventScreen.Showing && !modal.IsOpen;
            GameManager.EventKeysByScreen = evKeys;
            if (evKeys && !Game2DLoadingScreen.Blocking) EventKeys(gm);
            if (ph != _shown) { _shown = ph; _dirty = true; _shopSell = false; _shopSupply = false; _robArmed = false; _sellPage = 0; _shopTerm = false; _shopAxis = false; _termPick = TermPick.None; _termDeckPage = 0; if (ph != GameManager.GamePhase.EventEncounter) _eventResult = null; if (ph == GameManager.GamePhase.GateRitual) System.Array.Clear(_gateOffer, 0, 3); }
            // 画面の外で所持金・HP が変わった時も描き直す (買える品の明暗・副題の数字)
            int coins = gm.Run != null ? gm.Run.coins : 0, hp = gm.Run != null ? gm.Run.playerHP : 0;
            if (coins != _lastCoins || hp != _lastHp) { _lastCoins = coins; _lastHp = hp; if (_open || Game2DEventScreen.Showing) _dirty = true; }
            int pend = gm.PendingStartingPassiveChoices.Count + gm.PendingEventPassiveOffers.Count * 100000000 + gm.PendingEventDiceOffers.Count * 10000000 + gm.PendingEventTerminalRemovals * 1000 + gm.PendingChallengeTiers.Count * 100 + (gm.Run != null ? gm.Run.pendingWeaponIds.Count * 10000 + gm.Run.pendingTerminalOffer.Count * 1000000 : 0);
            if (pend != _lastPending) { _lastPending = pend; _dirty = true; }
            // イベントが替わった (同じフェーズのまま・QA の口 QaBattle.ShowEvent で続けて出した時など) も描き直す
            var evNow = ph == GameManager.GamePhase.EventEncounter && EventEncounter.Instance != null ? EventEncounter.Instance.Current : null;
            if (evNow != _evSeen || (evNow != null && gm.EventChoiceResolved != _evSeenResolved)) { _evSeen = evNow; _evSeenResolved = gm.EventChoiceResolved; _dirty = true; }
            // 作り直しの依頼 (RequestRebuild・QA の口が同じイベント・同じフェーズのまま層や所持金を替えて出し直した時): 最初の頁から作り直す
            if (_rebuildSeen != _rebuildSerial) { _rebuildSeen = _rebuildSerial; _evKey = null; _evSelFor = null; _dirty = true; }
            // エンディングの画面 (#328) の間は窓を出さない。 同じフレームで先に開いていたら閉じて、 読み終わってから作り直す
            if (Game2DEndingScreen.Blocking) { if (_open || Game2DEventScreen.Showing) { CloseIfOpen(); _dirty = true; } return; }
            if (_endingSerial != Game2DEndingScreen.ClosedSerial) { _endingSerial = Game2DEndingScreen.ClosedSerial; _dirty = true; }      // 読み終わった: 今のフェーズの窓 (踏破) を必ず作り直す
            if (!_dirty) return;
            // ロード画面の間は出さない (終わってから出す・_dirty は残す)。 開いている窓も閉じる ──
            //   「層を抜けた」の窓は押した直後にロード画面が始まるので、 閉じないとロード画面の上に残り続けていた (2026-11-11)
            if (Game2DLoadingScreen.Blocking) { CloseIfOpen(); return; }
            _dirty = false;
            _evBuilt = false;
            var def = Build(gm, ph);
            // イベントの画面は液晶の中身として描く (Game2DEventScreen・2026-11-16): 窓は閉じて、 液晶の板に出す
            if (_evBuilt)
            {
                if (_open) { modal.Close(); _open = false; }
                if (!Game2DEventScreen.Show(_evView)) Debug.LogWarning("[Game2DPhaseScreens] イベントの画面を液晶に出せない (地図の板が無い)");
                return;
            }
            Game2DEventScreen.Hide();
            if (def == null) { CloseIfOpen(); return; }
            if (_open) modal.Replace(def); else modal.Open(def);
            _open = true;
        }

        private void CloseIfOpen() { if (_open) { modal.Close(); _open = false; } Game2DEventScreen.Hide(); }

        /// <summary>今の場面の画面を、 最初の頁から作り直させる (同じフェーズ・同じイベントのまま中身を替えた時・QA の口 QaBattle.ShowEvent が呼ぶ)。</summary>
        public static void RequestRebuild() => _rebuildSerial++;
        private static int _rebuildSerial; private int _rebuildSeen;
        private int _endingSerial;

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
            if (ph == GameManager.GamePhase.MapNavigation && gm.PendingEventPassiveOffers.Count > 0) return EventPassivePick(gm);
            if (ph == GameManager.GamePhase.MapNavigation && gm.PendingEventDiceOffers.Count > 0) return EventDicePick(gm);
            // イベントの「山札から端子を抜く」(2026-11-21): 休憩で抜く時と同じ山札の一覧
            if (ph == GameManager.GamePhase.MapNavigation && gm.PendingEventTerminalRemovals > 0) { _termPick = TermPick.EventRemove; return TerminalDeckList(gm); }
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
                    // 2026-11-20 (system-overhaul-spec §4-2): いちばん低いレア度の秘宝から差し出す 1 つを選び、 受け取る秘宝は地図へ戻ってから 2 択で選ぶ
                    var give = gm.ExchangeGiveCandidates(); bool can = give.Count > 0;
                    var d = New("交換", null, can ? 420 : 360);
                    d.body = can ? "差し出す秘宝を 1 つ選ぶ。 上のレア度の秘宝 " + GameManager.ExchangeChoices + " つから 1 つ受け取る。" : "渡せる秘宝が無い。";
                    foreach (int i in give)
                    {
                        string id = run.ownedPassiveItems[i]; var data = ItemDatabase.Instance?.GetItem(id);
                        d.Items.Add(new ModalItem { name = ItemDatabase.DisplayNameOf(data, id), desc = data?.description });
                    }
                    if (can) d.buttons.Add(B("差し出す", "swap", true));
                    d.buttons.Add(B("通り過ぎる", "skip", !can));
                    d.onAction = (a, sel) => { if (a == "swap") gm.DoExchangeTile(Mathf.Clamp(sel, 0, give.Count - 1)); else gm.SkipExchangeTile(); Refresh(); };
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
                    d.buttons.Add(B("タイトルへ", "ok", true));
                    d.onAction = (a, _) => { gm.ReturnToTitle(); Refresh(); };
                    return d;
                }
                default: return null;      // 地図・戦闘・戦闘結果・タイトルは別の画面
            }
        }

        /// <summary>品の提示の行 (報酬・品を選ぶ・開幕の秘宝)。 呪物の賽の呪いの付いた品 (2026-11-25) は名前を赤にし、 右に「呪い」、 説明の頭に代価を出す。</summary>
        private static ModalItem OfferRow(string id)
        {
            var data = ItemDatabase.Instance?.GetItem(id);
            var it = new ModalItem { name = ItemDatabase.DisplayNameOf(data, id), desc = data?.description };
            if (CombatSystem.DiePassives.IsCursedOffer(GameManager.Instance?.Run, id))
            {
                it.value = CombatSystem.DiePassives.CurseMark;
                it.tone = Game2DKit.Red;
                it.desc = CombatSystem.DiePassives.CurseNote + (string.IsNullOrEmpty(it.desc) ? "" : "\n" + it.desc);
            }
            return it;
        }

        // ── 開幕の秘宝 (開幕パッシブ) の選択 (〈宝物庫〉r10: 何本目かで 2〜3 択) ──

        private ScreenDef StartingPick(GameManager gm)
        {
            var cands = gm.PendingStartingPassiveChoices[0];
            var d = New("開幕の秘宝", "残り " + gm.PendingStartingPassiveChoices.Count, 420);
            d.body = "このランに持っていく秘宝を 1 つ選ぶ。";
            foreach (var id in cands) d.Items.Add(OfferRow(id));
            d.buttons.Add(B("選ぶ", "pick", true));
            d.buttons.Add(SkipButton(cands));
            d.onAction = (a, sel) => { if (a == "pick" || a == "skip") { gm.ResolveStartingPassive(a == "skip" ? -1 : Mathf.Clamp(sel, 0, cands.Count - 1)); Game2DSfx.Play("ui_confirm"); } Refresh(); };
            return d;
        }

        // ── イベントの「パッシブアイテムをNつから選ぶ」(2026-11-12)。 開幕パッシブの選択と同じ形 ──

        private ScreenDef EventPassivePick(GameManager gm)
        {
            var cands = gm.PendingEventPassiveOffers[0];
            var d = New("品を選ぶ", gm.PendingEventPassiveOffers.Count > 1 ? "残り " + gm.PendingEventPassiveOffers.Count : null, 420);
            d.body = cands.Count > 1 ? "差し出された品から 1 つ選ぶ。 見送ればゴールドになる。" : "受け取るか、 見送ってゴールドにする。";
            foreach (var id in cands) d.Items.Add(OfferRow(id));
            d.buttons.Add(B(cands.Count > 1 ? "選ぶ" : "受け取る", "pick", true));
            d.buttons.Add(SkipButton(cands));
            d.onAction = (a, sel) => { if (a == "pick" || a == "skip") { gm.ResolveEventPassiveOffer(a == "skip" ? -1 : Mathf.Clamp(sel, 0, cands.Count - 1)); Game2DSfx.Play("ui_confirm"); } Refresh(); };
            return d;
        }

        /// <summary>秘宝の提示の「見送る（+NG）」(見送り換金・full-item-rework-spec §1-3)。 額は提示の中で一番高いレア度の額 (ItemSystem.AxisPool.SkipGoldFor)。</summary>
        private static ModalBtn SkipButton(IEnumerable<string> cands) => B("見送る（+" + ItemSystem.AxisPool.SkipGoldFor(cands) + "G）", "skip");

        // ── イベントの「賽を提示して交換」(2026-11-20)。 品の選択と同じ形・面を見せて、 替えるか「やめる」 ──

        private ScreenDef EventDicePick(GameManager gm)
        {
            var cands = gm.PendingEventDiceOffers[0];
            var d = New("賽を替える", "今の賽: " + EquippedDie.Label(EquippedDie.IdOf(gm.Run)) + "（面 " + string.Join(" ", EquippedDie.FacesOf(EquippedDie.IdOf(gm.Run))) + "）", 420);
            d.body = "差し出された賽と、 今の賽を入れ替えられる。 前の賽は手元に残らない。";
            foreach (var id in cands)
            {
                var data = ItemDatabase.Instance?.GetItem(id);
                d.Items.Add(new ModalItem { name = EquippedDie.Label(id), desc = "面 " + string.Join(" ", EquippedDie.FacesOf(id)) + (string.IsNullOrEmpty(data?.description) ? "" : "。 " + data.description) });
            }
            d.buttons.Add(B("替える", "pick", true));
            d.buttons.Add(B("やめる", "keep"));
            d.onAction = (a, sel) => { gm.ResolveEventDiceOffer(a == "pick" ? Mathf.Clamp(sel, 0, cands.Count - 1) : -1); Game2DSfx.Play("ui_confirm"); Refresh(); };
            return d;
        }

        // ── 挑戦カード (docs/GAME.md §23-25): 層の入口で 2 枚から 1 枚。 選んだカードのデバフとバフを両方受ける ──

        private ScreenDef ChallengePick(GameManager gm)
        {
            var offer = gm.ChallengeOffer; int tier = gm.PendingChallengeTiers[0];
            bool sin = tier >= MetaProgression.ChallengeCards.SinTier;
            var d = New(sin ? "大罪をひとつ背負う" : "試練をひとつ選ぶ", (sin ? "七つの大罪" : "Tier " + Game2DModal.ChallengeRomanOf(tier)) + (gm.PendingChallengeTiers.Count > 1 ? "  残り " + gm.PendingChallengeTiers.Count : ""), 460);
            d.body = sin ? "裏の恩恵は無い。 5 層の間、 背負った罪が効き続ける。" : "選んだ札のデバフとバフを、 両方受ける。";
            d.bodyLines = 1;
            d.rowH = Game2DModal.ChallengeRowH;
            if (offer != null)
                foreach (var card in offer)
                {
                    MetaProgression.ChallengeCards.Split(card, out var deb, out var buf);
                    d.Items.Add(SinOrCardItem(tier, deb, buf));
                }
            d.buttons.Add(B("受ける", "pick", true));
            d.onAction = (a, sel) => { if (a == "pick") { gm.ResolveChallengePick(Mathf.Clamp(sel, 0, d.Items.Count - 1)); Game2DSfx.Play("ui_confirm"); } Refresh(); };
            return d;
        }

        /// <summary>挑戦カードの行 (大罪は裏なし・2 段目はフレーバーの一節)。 持ち物のステータスの頁も使う。</summary>
        internal static ModalItem SinOrCardItem(int tier, MetaProgression.ChallengeCards.Card deb, MetaProgression.ChallengeCards.Card buf)
        {
            if (buf == null && deb != null && MetaProgression.ChallengeCards.IsSin(deb.kind))
                return new ModalItem { challengeTier = 4, deb = MetaProgression.ChallengeCards.Text(deb), buf = null, sub = FirstLine(MetaProgression.ChallengeCards.Flavor(deb)) };
            return new ModalItem { challengeTier = tier, deb = MetaProgression.ChallengeCards.Text(deb), buf = MetaProgression.ChallengeCards.Text(buf) };
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf((char)10);
            return (nl >= 0 ? s.Substring(0, nl) : s).Trim();
        }

        // ── 休憩 ──
        //   休む / 多めに休む (ターンが進む) / 鍛える / 山札から抜く / 端子を強化する (2026-11-20)。 どれも 1 回の休憩で 1 つだけ (面を外すは 2026-11-18 に削除・ADR-0015)。

        private ScreenDef Rest(GameManager gm)
        {
            var run = gm.Run;
            Game2DTutorial.Request(TutorialTopics.Equip);      // 手引き「装備と強化」: 初めて休憩に入った (武器の強化・面を外す)
            if (_termPick == TermPick.Rest || _termPick == TermPick.RestUpgrade) return TerminalDeckList(gm);
            var d = New("休憩", "HP " + run.playerHP + "/" + run.playerMaxHP + "  素材 " + run.weaponMaterials);
            int more = Mathf.RoundToInt(MapSystem.FreeMove.FreeMapParams.RestMoreRatio * 100);
            d.Items.Add(new ModalItem { name = "休む", desc = "HP を回復する。 ターンは進まない。" });
            d.Items.Add(new ModalItem { name = "多めに休む", value = "+" + MapSystem.FreeMove.FreeMapParams.RestMoreTurns + " ターン", desc = "最大 HP の " + more + "% を回復する。 代わりにターンが進む (追跡者が動く)。" });
            d.Items.Add(new ModalItem { name = "鍛える", value = "素材 " + run.weaponMaterials, desc = "素材を使って武器を 1 段強化する (強化できなければ休む)。" });
            // 端子の山札 (ADR-0014): 回復の代わりに 1 枚抜く (無料)。 山札が手札の枚数を下回る抜き方はできない
            bool canTerm = gm.CanRemoveTerminal; int deckN = run.terminalDeck != null ? run.terminalDeck.Count : 0;
            d.Items.Add(new ModalItem { name = "山札から抜く", value = canTerm ? "山札 " + deckN + " 枚" : "抜けない", disabled = !canTerm,
                desc = canTerm ? "回復の代わりに、 山札からカードを 1 枚抜く (この先ずっと)。 抜くカードを次に選ぶ。" : "山札が下限の枚数 (" + CombatSystem.TerminalCards.MinDeckSize + ") を下回る抜き方はできない。" });
            // 端子を強化する (2026-11-20): 素材を払って、 山札の端子 1 枚を強化した端子 (＋) に差し替える。 値段は強化するたびに上がる
            int upCost = GameManager.TerminalUpgradeCost(run); bool anyUp = GameManager.HasUpgradableTerminal(run), canUp = anyUp && run.weaponMaterials >= upCost;
            d.Items.Add(new ModalItem { name = "カードを強化する", value = "素材 " + upCost, disabled = !canUp,
                desc = !anyUp ? "山札に強化できるカードが無い。"
                     : canUp ? "素材 " + upCost + " を使って、 山札のカードを 1 枚強化する (この先ずっと・強化するたびに素材が 2 増える)。 強化するカードを次に選ぶ。"
                             : "素材が足りない (" + upCost + " 要る)。" });
            d.buttons.Add(B("決める", "do", true));
            d.onAction = (a, sel) =>
            {
                if (a != "do") return;
                switch (sel)
                {
                    case 1: gm.RestHealMore(); break;
                    case 2: gm.RestUpgrade(); break;
                    case 3: if (canTerm) { _termPick = TermPick.Rest; _termDeckPage = 0; } break;
                    case 4: if (canUp) { _termPick = TermPick.RestUpgrade; _termDeckPage = 0; } break;
                    default: gm.RestHeal(); break;
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

        private struct SellEntry { public ShopManager.SellSource src; public int index; public string name, desc, icon, price; public ItemRarity? rarity; }

        private static List<SellEntry> SellList(RunState run)
        {
            var l = new List<SellEntry>();
            if (run.ownedPassiveItems != null)
                for (int i = 0; i < run.ownedPassiveItems.Count; i++)
                    if (run.ownedPassiveItems[i] != run.equippedWeaponId      // 装備中の武器は売らせない (武器もパッシブの一覧に入っている)
                        && ItemIds.CanSell(run.ownedPassiveItems[i]))   // 手放せない品 (確信の 3 品・預かり物) は売れない (ShopManager.TrySell と同じ判定)
                        l.Add(Entry(ShopManager.SellSource.Passive, i, run.ownedPassiveItems[i]));
            if (run.ownedConsumables != null)
                for (int i = 0; i < run.ownedConsumables.Count; i++) l.Add(Entry(ShopManager.SellSource.Consumable, i, run.ownedConsumables[i]));
            if (run.weaponMaterials > 0)
                l.Add(new SellEntry { src = ShopManager.SellSource.WeaponMaterial, index = 0, name = "強化素材 (所持 " + run.weaponMaterials + ")", desc = "1 つ売る。", icon = "", price = ShopManager.WeaponMaterialSellPrice + "G" });
            return l;
        }

        private static SellEntry Entry(ShopManager.SellSource src, int i, string id)
        {
            var data = ItemDatabase.Instance?.GetItem(id);
            // 売値は ShopManager.SellRange (売却・BOT と同じ口)。 売値 0G の品 (items.json の noSellValue) は「0G」と出て、 売れば 0G が入る
            return new SellEntry { src = src, index = i, name = ItemDatabase.DisplayNameOf(data, id), desc = data?.description, icon = data != null ? data.id : id, price = ShopManager.SellPriceLabel(id), rarity = data != null ? data.rarity : (ItemRarity?)null };
        }

        private ScreenDef Shop(GameManager gm)
        {
            Game2DTutorial.Request(TutorialTopics.Shop);      // 手引き「ショップ」: 初めてショップに入った
            if (_termPick != TermPick.None) return TerminalDeckList(gm);
            if (_shopTerm && !_shopSell) return ShopTerminals(gm);
            if (_shopAxis && !_shopSell) return ShopAxisReroll(gm);
            return _shopSell ? ShopSell(gm) : ShopBuy(gm);
        }

        // ── 軸指定リロール (2026-11-20・item-overhaul-spec §2-3) ──
        //   軸を 1 つ選んで振り直す。 値段は通常のリロール + ShopManager.AxisRerollSurcharge (回数は通常と共有)。 横の数は今の偏重 (軸の秘宝の数)。
        private bool _shopAxis;

        private ScreenDef ShopAxisReroll(GameManager gm)
        {
            var run = gm.Run; var sm = ShopManager.Instance; var inv = sm != null ? sm.Current : null;
            int price = sm != null ? sm.AxisRerollPrice : 0;
            var d = New("ショップ ― 軸で振り直す", "所持金 " + run.coins + "G", 460);
            d.body = "出現の枠 " + ItemSystem.AxisPool.SlotsLabel(run) + " の軸か金を選ぶ。 売れていない秘宝の前の " + ShopManager.AxisRerollGuaranteed + " 枠はその軸の品になり、 残りの枠とカードも出やすくなる。"; d.bodyLines = 2;
            var axList = new List<string>(run.lockedAxes) { ItemSystem.PassiveSkills.AxisRules.Gold };
            var axes = axList.ToArray();
            foreach (var ax in axes)
            {
                string word = ItemSystem.PassiveSkills.AxisRules.WordOf(ax);
                d.Items.Add(new ModalItem
                {
                    name = ax, value = (string.IsNullOrEmpty(word) ? "秘宝 " : word + " ") + ItemSystem.PassiveSkills.AxisRules.Count(run, ax),
                    desc = ax + "の秘宝" + (ax == ItemSystem.PassiveSkills.AxisRules.Gold ? "" : "とカード") + "が出やすくなる (秘宝の " + ShopManager.AxisRerollGuaranteed + " 枠は確定)。 値段は振り直し " + (inv != null ? inv.CurrentRerollPrice : 0) + "G + " + ShopManager.AxisRerollSurchargeFor(run) + "G。",
                    disabled = inv == null || price > run.coins,
                });
            }
            d.buttons.Add(B("振り直す " + price + "G", "axisroll", true));
            d.buttons.Add(B("棚へ", "back"));
            d.footer = "軸を選んで「振り直す」";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "axisroll":
                        if (sel < 0 || sel >= axes.Length) modal.ShowError("軸を選ぶ");
                        else if (gm.ShopReroll(axes[sel])) { Game2DSfx.Play("ui_confirm"); _shopAxis = false; }
                        else modal.ShowError("振り直せない (所持金が足りない)");
                        break;
                    case "back": _shopAxis = false; break;
                }
                Refresh();
            };
            return d;
        }

        // ── 店の端子 (ADR-0014): 端子の棚 (山札に加える) と、 山札から 1 枚抜くサービス (店で抜くたびに値段が上がる) ──

        private bool _shopTerm;

        private ScreenDef ShopTerminals(GameManager gm)
        {
            var run = gm.Run; int deckN = run.terminalDeck != null ? run.terminalDeck.Count : 0;
            var d = New("ショップ ― カード", "所持金 " + run.coins + "G  山札 " + deckN + " 枚", 460);
            var shelf = gm.TerminalShop;
            foreach (var s in shelf)
            {
                var t = CombatSystem.TerminalCards.Get(s.id);
                d.Items.Add(new ModalItem
                {
                    name = s.id, tone = t != null ? Game2DTerminalLook.KindColor(t.kind) : (Color32?)null,
                    value = s.sold ? "売切" : s.price + "G",
                    desc = t != null ? Game2DTerminalLook.Marks(t, run) + "\n" + t.description : null,
                    disabled = s.sold || s.price > run.coins,
                });
            }
            int removeRow = d.Items.Count, price = gm.TerminalRemovePrice; bool canRemove = gm.CanRemoveTerminal;
            d.Items.Add(new ModalItem
            {
                name = "山札から 1 枚抜く", value = canRemove ? price + "G" : "抜けない",
                desc = canRemove ? "山札からカードを 1 枚抜く (この先ずっと)。 抜くたびに値段が上がる。 抜くカードを次に選ぶ。"
                                 : "山札が下限の枚数 (" + CombatSystem.TerminalCards.MinDeckSize + ") を下回る抜き方はできない。",
                disabled = !canRemove || price > run.coins,
            });
            d.buttons.Add(B("決める", "do", true));
            d.buttons.Add(B("山札を見る", "deck"));
            d.buttons.Add(B("棚へ", "back"));
            d.buttons.Add(B("出る", "leave"));
            d.footer = "カードを選んで「決める」で山札に加える";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "do":
                        if (sel == removeRow)
                        {
                            if (canRemove && price <= run.coins) { _termPick = TermPick.Shop; _termDeckPage = 0; }
                            else modal.ShowError(canRemove ? "所持金が足りない" : "これ以上は抜けない");
                        }
                        else if (sel >= 0 && sel < shelf.Count)
                        {
                            if (gm.ShopBuyTerminal(sel)) Game2DSfx.Play("ui_confirm"); else modal.ShowError("買えない (所持金が足りない・売り切れ)");
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
            if (inv != null && !_shopSupply) d.buttons.Add(B("振り直す " + sm.RerollPrice + "G", "reroll"));
            if (inv != null && !_shopSupply) d.buttons.Add(B("軸で振り直す " + sm.AxisRerollPrice + "G", "axis"));
            d.buttons.Add(B(_shopSupply ? "棚へ" : "補給 (物資)", "supply"));
            d.buttons.Add(B("カード", "term"));
            d.buttons.Add(B("売る", "sellmode"));
            if (robOk) d.buttons.Add(B(_robArmed ? "本当に強盗" : "強盗", "rob"));
            d.buttons.Add(B("出る", "leave"));
            d.footer = _robArmed ? "強盗: 商人と戦う。 勝てば棚の品を奪う。 以降の店は値段 ×" + ShopManager.RobberySurcharge.ToString("0.#") + "。 もう一度押すと実行"
                     : _weaponArmed >= 0 ? "武器の乗り換え: 「" + WeaponLabel(run.equippedWeaponId) + "」を下取りに出す。 もう一度「買う」で買う" : "品を選んで「買う」　出現 " + ItemSystem.AxisPool.SlotsLabel(run) + (ItemSystem.AxisPool.IsLocked(run) ? " ロック" : "");
            d.onAction = (a, sel) =>
            {
                if (a != "rob") _robArmed = false;
                int armed = _weaponArmed; if (a != "buy") _weaponArmed = -1;
                switch (a)
                {
                    case "buy":
                    {
                        if (sel == packRow) { if (sm != null && sm.TryBuyProvisionPack(run)) Game2DSfx.Play("ui_confirm"); else modal.ShowError(run.provision >= run.provisionCap ? "物資は満タン" : "買えない (所持金が足りない・品切れ)"); break; }
                        if (sel == lureRow) { if (sm != null && sm.TryBuyLure(run)) Game2DSfx.Play("ui_confirm"); else modal.ShowError("買えない (所持金が足りない・持ちきれない)"); break; }
                        if (_shopSupply) break;
                        // 武器は 1 本だけ: 別の武器を持っている時は、 1 度目の「買う」で入れ替えの警告を出し、 2 度目で買う
                        if (inv != null && sel >= 0 && sel < inv.slots.Count && inv.slots[sel].kind == ShopSlotKind.Weapon && !inv.slots[sel].sold
                            && HoldsOtherWeapon(run, inv.slots[sel].itemId) && armed != sel)
                        { _weaponArmed = sel; modal.ShowToast("「" + WeaponLabel(run.equippedWeaponId) + "」を下取りに出して乗り換える"); break; }
                        _weaponArmed = -1;
                        int before = run.coins; gm.ShopBuy(sel);
                        if (run.coins == before && sel >= 0 && inv != null && sel < inv.slots.Count && !inv.slots[sel].sold) modal.ShowError("買えない (所持金が足りない・売り切れ)");
                        else Game2DSfx.Play("ui_confirm");
                        break;
                    }
                    case "supply": _shopSupply = !_shopSupply; break;
                    case "term": _shopTerm = true; _shopSupply = false; break;
                    case "reroll": if (!gm.ShopReroll()) modal.ShowError("振り直せない (所持金が足りない)"); break;
                    case "axis": _shopAxis = true; break;
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
            return ItemDatabase.DisplayNameOf(id);
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
            bool fromEvent = gm.TerminalOfferFromEvent;      // イベントの「端子をNつから選ぶ」(2026-11-21): 題を変え、 戦闘の不安定化の行は出さない
            var d = New(fromEvent ? "カードを選ぶ" : "カードの報酬", "山札 " + (run.terminalDeck != null ? run.terminalDeck.Count : 0) + " 枚", 460);
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
            // 不安定化 (2026-11-13): 戦闘に勝った後の最初の窓はこれ (通常・エリート・番人・使い魔・イベントの戦闘)。 この戦闘で溜まった分を出す。
            //   本文の 2 行目に足す。 候補が多くて (〈剥ぎ取り〉の上乗せ) 窓の高さ (ModalJpMetrics.MaxH) を超えるなら、 題の右の副題へ回す
            if (!fromEvent) AddInstabilityLine(gm, d, offer.Count);
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
        /// <summary>山札の一覧を何のために開いているか。 View = 見るだけ (店の端子の頁から) / Shop = 店で抜く端子を選ぶ / Rest = 休憩で抜く端子を選ぶ /
        /// RestUpgrade = 休憩で強化する端子を選ぶ (強化できる端子だけを並べ、 説明は強化後の文) / EventRemove = イベントの「山札から端子を抜く」で抜く端子を選ぶ (地図で)。
        /// None は報酬の窓の「山札を見る」(_termDeckView) の時。</summary>
        private enum TermPick { None, View, Shop, Rest, RestUpgrade, EventRemove }
        private TermPick _termPick;

        private ScreenDef TerminalDeckList(GameManager gm)
        {
            var run = gm.Run; var deck = run.terminalDeck ?? new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (var id in deck) { counts.TryGetValue(id, out int c); counts[id] = c + 1; }
            var names = new List<string>();
            bool upPick = _termPick == TermPick.RestUpgrade;
            foreach (var t in CombatSystem.TerminalCards.All) if (counts.ContainsKey(t.id)) names.Add(t.id);
            foreach (var id in counts.Keys) if (!names.Contains(id)) names.Add(id);      // 定義に無い名前 (古いセーブなど) も落とさない
            if (upPick) names.RemoveAll(id => !CombatSystem.TerminalCards.CanUpgrade(id));      // 強化: 強化できる端子だけ
            int pages = Mathf.Max(1, (names.Count + TermDeckPerPage - 1) / TermDeckPerPage); _termDeckPage = Mathf.Clamp(_termDeckPage, 0, pages - 1);
            var d = New("山札", "計 " + deck.Count + " 枚・" + names.Count + " 種", 460); d.cols = 2;
            int first = _termDeckPage * TermDeckPerPage, cnt = Mathf.Min(TermDeckPerPage, names.Count - first);
            for (int i = 0; i < cnt; i++)
            {
                string id = names[first + i]; var t = CombatSystem.TerminalCards.Get(id);
                var tu = upPick ? CombatSystem.TerminalCards.Get(CombatSystem.TerminalIds.Upgraded(id)) : null;
                d.Items.Add(new ModalItem
                {
                    name = id, value = "×" + counts[id], tone = t != null ? Game2DTerminalLook.KindColor(t.kind) : (Color32?)null,
                    desc = tu != null ? "強化すると: " + Game2DTerminalLook.Describe(tu, run) : t != null ? Game2DTerminalLook.Describe(t, run) : null,
                });
            }
            if (names.Count == 0) d.body = "山札は空。";
            bool evRemove = _termPick == TermPick.EventRemove;
            bool pick = _termPick == TermPick.Shop || _termPick == TermPick.Rest || evRemove || upPick;
            if (upPick)
            {
                d.title = "強化するカードを選ぶ ― 素材 " + GameManager.TerminalUpgradeCost(run);
                d.buttons.Add(B("強化する", "upgrade", true));
            }
            else if (pick)
            {
                d.title = _termPick == TermPick.Shop ? "抜くカードを選ぶ ― " + gm.TerminalRemovePrice + "G" : evRemove ? "抜くカードを選ぶ" : "抜くカードを選ぶ ― 休憩";
                d.buttons.Add(B("抜く", "remove", true));
            }
            if (pages > 1) d.buttons.Add(B("次の頁 " + (_termDeckPage + 1) + "/" + pages, "page"));
            d.buttons.Add(B(evRemove ? "抜かない" : "戻る", "back", !pick));
            d.footer = upPick ? "強化したカードは名前に「＋」が付く。 同じ名前が 2 枚あれば 1 枚だけ強化する"
                     : pick ? "抜いたカードは、 このランの山札から消える" : "名前の色は種類 (赤 = 攻撃・青 = 防御・金 = 充電・緑 = 補助)";
            d.onAction = (a, sel) =>
            {
                if (a == "page") _termDeckPage = (_termDeckPage + 1) % pages;
                else if (a == "back") { if (evRemove) gm.ResolveEventTerminalRemoval(-1); _termDeckView = false; _termPick = TermPick.None; }
                else if (a == "upgrade" && upPick && sel >= 0 && sel < cnt)
                {
                    int at = deck.IndexOf(names[first + sel]);
                    if (gm.RestUpgradeTerminal(at)) { Game2DSfx.Play("ui_confirm"); _termPick = TermPick.None; }
                    else modal.ShowError("強化できない (素材が足りない・強化できないカード)");
                }
                else if (a == "remove" && pick && sel >= 0 && sel < cnt)
                {
                    int at = deck.IndexOf(names[first + sel]);
                    bool ok = _termPick == TermPick.Shop ? gm.ShopRemoveTerminal(at) : evRemove ? gm.ResolveEventTerminalRemoval(at) : gm.RestRemoveTerminal(at);
                    if (ok) { Game2DSfx.Play("ui_confirm"); _termPick = TermPick.None; }
                    else modal.ShowError("抜けない (所持金が足りない・山札が手札の枚数を下回る・最後の攻撃のカード)");
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
            for (int i = 0; i < cnt; i++) { var e = list[first + i]; d.Items.Add(new ModalItem { name = e.name, desc = e.desc, icon = e.icon, rarity = e.rarity, value = (e.src == ShopManager.SellSource.Consumable ? "消耗品 " : e.src == ShopManager.SellSource.WeaponMaterial ? "素材 " : "") + e.price }); }
            if (list.Count == 0) d.body = "売れる物を持っていない。";
            d.buttons.Add(B("売る", "sell", true));
            if (pages > 1) d.buttons.Add(B("次の頁 " + (_sellPage + 1) + "/" + pages, "page"));
            d.buttons.Add(B("買う画面へ", "buymode"));
            d.footer = "売る物を選んで「売る」";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "sell":
                    {
                        if (sel < 0 || sel >= cnt) break;
                        var e = list[first + sel]; int before = run.coins;
                        // 売れたかは持ち物の数で見る (売値 0G の品は売っても所持金が増えない)
                        int held = (run.ownedPassiveItems?.Count ?? 0) + (run.ownedConsumables?.Count ?? 0) + run.weaponMaterials;
                        // GameManager の売却は「売る対象の種類」を切り替えてから番号で売る (キー操作と同じ口)
                        if (!gm.ShopSellMode) gm.ToggleShopSellMode();
                        for (int k = 0; k < 3 && gm.ShopSellSource != e.src; k++) gm.CycleSellSource();      // 種類は 3 つ (パッシブ・消耗品・素材) を回る
                        if (gm.ShopSellSource != e.src) { modal.ShowError("売れない"); break; }
                        gm.ShopSell(e.index);
                        if ((run.ownedPassiveItems?.Count ?? 0) + (run.ownedConsumables?.Count ?? 0) + run.weaponMaterials < held) { Game2DSfx.Play("ui_confirm"); modal.ShowToast(e.name + " を売った +" + (run.coins - before) + "G"); }
                        else modal.ShowError("売れない");
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
            }
            if (string.IsNullOrEmpty(s.itemId)) return "(空)";
            return ItemDatabase.DisplayNameOf(s.itemId);
        }

        /// <summary>行の左のアイコン: 品は id (Resources/Icons/Items)、 素材は枠だけ ("")。</summary>
        /// <summary>行の枠のレアリティ (2026-11-08)。 素材・品の無い枠は null (従来の灰色の枠)。</summary>
        private static ItemRarity? SlotRarity(ShopSlot s)
        {
            if (s.kind == ShopSlotKind.WeaponMaterial || string.IsNullOrEmpty(s.itemId)) return null;
            var data = ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null ? data.rarity : (ItemRarity?)null;
        }

        private static string SlotIcon(ShopSlot s)
        {
            if (s.kind == ShopSlotKind.WeaponMaterial || string.IsNullOrEmpty(s.itemId)) return "";
            var data = ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null ? data.id : s.itemId;
        }

        private static string SlotDesc(ShopSlot s)
        {
            switch (s.kind)
            {
                case ShopSlotKind.WeaponMaterial: return "武器を強化する素材 (休憩で使う)。 在庫に限りは無い。";
                case ShopSlotKind.Dice: return ItemCards.Die(s.itemId).description + "\n買うと、 今の賽 (" + EquippedDie.Label(EquippedDie.IdOf(GameManager.Instance?.Run)) + ") と入れ替わる。";
            }
            var data = string.IsNullOrEmpty(s.itemId) ? null : ItemDatabase.Instance?.GetItem(s.itemId);
            return data != null ? data.description : null;
        }

        // ── イベント ──

        // 頁送り (2026-11-13・EventPaging): 導入は「／」の段落ごとに 1 頁、 長い段落と結果の文は行数で割る。 選択肢は最後の頁だけ。
        //   頁の位置は画面だけが持つ (進行・セーブには入れない)。 導入 / 結果 / 別のイベントに替わったら最初の頁へ戻る。
        //   _evEpoch は選んだ回数 (OnEventResolved) ── ランダムイベントの振り直しで同じイベントに戻っても最初の頁から出す。
        // 2026-11-16: 窓 1 枚から全画面の配置 (背景の絵・左下に本文の板・右に選択肢の列) へ (EventScreenView・EventLayout・作者が見本で承認)。
        //   絵は窓 (GUI の層) でなく液晶の中身として写す (Game2DEventScreen・液晶の効果を戦闘・結末とそろえる)。 Event() は窓の中身を返さず (null)、 _evBuilt を立てる。
        //   選択肢の箱を押すと選ぶだけ・確定は操作の行の [選ぶ] か数字のキー (今までの「行を選んでから [選ぶ]」と同じ・誤って即決しない)。
        private int _evPage, _evEpoch; private string _evKey; private EventPaging.Plan _evPlan; private bool _evResolved; private bool _evBuilt;
        private readonly EventScreenView _evView = new EventScreenView(); private string _evSelFor; private EventDefinition _evSeen; private bool _evSeenResolved;

        private ScreenDef Event(GameManager gm)
        {
            var ev = EventEncounter.Instance != null ? EventEncounter.Instance.Current : null;
            var run = gm.Run;
            bool resolved = gm.EventChoiceResolved;
            string text = resolved ? (string.IsNullOrEmpty(_eventResult) ? "……" : _eventResult) : ev?.flavor;
            var plan = resolved ? EventPaging.Result(text, Game2DJpFont.Advance, Game2DJpFont.LineH) : EventPaging.Intro(text, Game2DJpFont.Advance, Game2DJpFont.LineH);      // 行間・字間の設定は実行時に読む
            string key = (resolved ? "r:" : "i:") + Game2DJpFont.LineH + ":" + Game2DJpFont.CharGap + ":" + _evEpoch + ":" + (ev != null ? ev.name : "") + ":" + text;
            bool fresh = key != _evKey;
            if (fresh) { _evKey = key; _evPage = 0; }
            _evPage = plan.Clamp(_evPage); _evPlan = plan; _evResolved = resolved; _evBuilt = true;
            bool last = plan.IsLast(_evPage);

            var v = _evView;
            v.backdrop = EventScreenView.BackdropName(ev);
            v.title = ev != null ? ev.name : "イベント";
            v.bar = EventBar(gm); v.pageMark = plan.Mark(_evPage); v.lines = plan.Lines(_evPage);
            v.buttons.Clear(); v.choices = null;
            if (_evPage > 0) v.buttons.Add(new EventScreenView.Btn { label = "前へ", action = "prev" });
            if (!last) { v.buttons.Add(new EventScreenView.Btn { label = "次へ", action = "page", primary = true }); v.hint = "Space 次へ"; }
            else if (resolved || ev == null || ev.choices.Count == 0) { v.buttons.Add(new EventScreenView.Btn { label = "次へ", action = "next", primary = true }); v.hint = "Space 次へ"; }
            else
            {
                // 選択肢 (最後の頁だけ): 名前・見せる代償の札・選べないか・捕捉中の見込み・箱の下の説明
                v.choices = new List<EventScreenView.Choice>();
                int first = -1;
                for (int i = 0; i < ev.choices.Count; i++)
                {
                    var c = ev.choices[i];
                    bool ok = EventEncounter.IsChoiceSelectable(ev, i, run);
                    if (ok && first < 0) first = i;
                    v.choices.Add(new EventScreenView.Choice
                    {
                        name = c.text, chips = c.CostChips(), disabled = !ok,
                        warnTurns = EventEncounter.WouldBeCaught(c.TurnCost),
                        note = EventEncounter.DescribeNote(ev, i, run),
                    });
                }
                if (_evSelFor != key) { _evSelFor = key; v.ResetSelection(Mathf.Max(0, first)); }      // 選択肢が初めて出た時に、 最初の選べる箱を選んでおく
                v.buttons.Add(new EventScreenView.Btn { label = "選ぶ", action = "pick", primary = true });
                v.hint = ev.choices.Count > 1 ? "1〜" + Mathf.Min(9, ev.choices.Count) + " で選ぶ" : "1 で選ぶ";
            }
            if (fresh && v.choices == null) v.ResetSelection(0);
            v.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "prev": EventPageStep(-1, false); return;      // 押した音は画面 (EventScreenView) が鳴らす
                    case "page": EventPageStep(+1, false); return;
                    case "next": gm.ConfirmEventEncounter(); break;
                    case "pick": if (ev != null && sel >= 0 && sel < ev.choices.Count) gm.ResolveEventChoice(sel); break;
                }
                Refresh();
            };
            return null;      // 窓は使わない (Update が _evBuilt を見て液晶の板に出す)
        }

        /// <summary>イベントの画面の上の帯の値 (2026-11-16): HP・物資・ゴールド・素材・ターン・捕捉中 (地図の帯 Game2DNavExtras と同じ所から読む)。
        /// 航行マップの無い層では、 ターンと捕捉中を出さない (詰めて並べる)。 結果の頁では選んだ効果の後の値: HP・物資・ゴールド・素材は選んだ時に適用済み、
        /// ターンは地図へ戻る時に進むので、 まだ進めていない分 (EventEncounter.PendingTurns) を足して見せる。 捕捉中の見込みは今の値 (ターンが進む前)。</summary>
        private static List<EventLayout.BarItem> EventBar(GameManager gm)
        {
            var run = gm.Run; var list = new List<EventLayout.BarItem>();
            if (run == null) return list;
            list.Add(new EventLayout.BarItem { label = "HP", value = run.playerHP + "/" + run.playerMaxHP });
            list.Add(new EventLayout.BarItem { label = "物資", value = run.provision.ToString() });
            list.Add(new EventLayout.BarItem { value = run.coins + "G" });
            list.Add(new EventLayout.BarItem { label = "素材", value = run.weaponMaterials.ToString() });
            var sim = gm.FreeMap;
            if (sim != null)
            {
                int pending = EventEncounter.Instance != null ? EventEncounter.Instance.PendingTurns : 0;
                list.Add(new EventLayout.BarItem { label = "ターン", value = (sim.Turn + pending).ToString() });
            }
            var eta = EventEncounter.CatchEtaNow();
            if (eta.Valid) list.Add(new EventLayout.BarItem { label = "捕捉中", value = "あと" + eta.turns, warn = true });
            else if (sim != null && sim.FoeAlive && sim.FoeMode == MapSystem.FreeMove.FoeMode.Chase) list.Add(new EventLayout.BarItem { value = "捕捉中", warn = true });
            return list;
        }

        /// <summary>頁を 1 つ送る・戻す (画面の中だけ)。</summary>
        private void EventPageStep(int dir, bool sound)
        {
            if (_evPlan == null) return;
            int p = _evPlan.Clamp(_evPage + dir);
            if (p == _evPage) return;
            _evPage = p; if (sound) Game2DSfx.Play("ui_click"); Refresh();
        }

        /// <summary>イベントの画面のキー (手引きの板と同じ操作): Space / Enter / → で次の頁 (結果の最後の頁では地図へ戻る)・← / Backspace で前の頁・
        /// 1〜9 は最後の頁 (選択肢が出ている頁) でだけ選ぶ。 この間 GameManager のイベントのキー (<see cref="GameManager.EventKeysByScreen"/>) は止める
        /// ── 止めないと Space が結果の頁を飛ばして地図へ戻し、 数字が見えていない選択肢を選んでいた。</summary>
        private void EventKeys(GameManager gm)
        {
            if (_evPlan == null || Game2DTutorial.Blocking) return;
            bool space = Input.GetKeyDown(KeyCode.Space);
            bool next = space || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.RightArrow);
            bool prev = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Backspace);
            if (space) GameManager.ScreenUsedSpaceFrame = Time.frameCount;     // 同じ Space が地図 (待つ) へ流れないように
            if (prev) { EventPageStep(-1, true); return; }
            bool last = _evPlan.IsLast(_evPage);
            if (next)
            {
                if (!last) EventPageStep(+1, true);
                else if (_evResolved) { Game2DSfx.Play("ui_confirm"); gm.ConfirmEventEncounter(); Refresh(); }
                return;          // 選択肢の頁の Space は何もしない (読み飛ばしの連打で選ばないように)
            }
            if (!last || _evResolved) return;
            var ev = EventEncounter.Instance != null ? EventEncounter.Instance.Current : null;
            if (ev == null) return;
            for (int i = 0; i < Mathf.Min(ev.choices.Count, 9); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { Game2DSfx.Play("ui_confirm"); gm.ResolveEventChoice(i); Refresh(); return; }      // ボタン「選ぶ」(窓が ui_confirm を鳴らす) と同じ音 (#321)
        }

        /// <summary>報酬の窓に「不安定 +N（累計 M）」(この戦闘で溜まった分・<see cref="GameManager.BattleInstabilityLine"/>) を足す。 溜まっていなければ何もしない。
        /// 本文の行を 1 つ増やしても窓の高さ (<see cref="ModalJpMetrics.MaxH"/>) に収まるなら本文の最後の行へ、 収まらなければ副題の後ろへ。</summary>
        private static void AddInstabilityLine(GameManager gm, ScreenDef d, int rows)
        {
            string inst = gm.BattleInstabilityLine();
            if (inst == null) return;
            bool detail = false; foreach (var it in d.Items) if (!string.IsNullOrEmpty(it.desc)) { detail = true; break; }
            int lines = (string.IsNullOrEmpty(d.body) ? 0 : Game2DJpFont.Lines(d.body, ModalJpMetrics.BodyW(d.w)).Count) + 1;
            if (ModalJpMetrics.HeightUncapped(Game2DJpFont.LineH, true, lines, rows, d.rowH, detail) <= ModalJpMetrics.MaxH)
            {
                d.body = string.IsNullOrEmpty(d.body) ? inst : d.body + "\n" + inst;
                d.bodyLines = Mathf.Max(d.bodyLines, lines);
            }
            else d.sub = string.IsNullOrEmpty(d.sub) ? inst : d.sub + "・" + inst;
        }

        // ── 報酬 ──

        private ScreenDef Reward(GameManager gm)
        {
            var d = New("報酬", _lastCoinsGained > 0 ? "+" + _lastCoinsGained + "G" : null);
            if (gm.HasPendingRewardChoice)
            {
                var offer = gm.CurrentRewardChoice;
                bool relicPair = ItemSystem.AxisPool.SkipGoldFor(offer) > 0;
                string pickLine = offer.Count >= 3 ? "3 つから 1 つを受け取る。" : offer.Count == 2 ? "どちらか 1 つを受け取る。" : "受け取るか選ぶ。";
                d.body = relicPair ? pickLine + " 見送ればゴールドになる。" : pickLine;
                foreach (var id in offer) d.Items.Add(OfferRow(id));
                AddInstabilityLine(gm, d, d.Items.Count);   // 不安定化: 択から選ぶ報酬の窓にも (2026-11-13)
                d.buttons.Add(B("受け取る", "pick", true));
                if (relicPair) d.buttons.Add(SkipButton(offer));
                int n = offer.Count;
                d.onAction = (act, sel) => { if (act == "pick") gm.ResolveRewardChoice(Mathf.Clamp(sel, 0, n - 1)); else if (act == "skip") gm.ResolveRewardChoice(-1); Refresh(); };
                return d;
            }
            d.body = _lastCoinsGained > 0 ? _lastCoinsGained + "G を手に入れた。" : "戦利品を手に入れた。";
            // 時限デバフ〈徒労〉/ 時限バフ〈開眼〉が掛かった戦闘 (2026-11-13): 何も得られなかったのが〈徒労〉のせいだと分かる 1 行 / 2 倍になった 1 行
            string boon = gm.BattleBoonLine;
            //   〈徒労〉の別の形 (LayerLong・2026-11-14) はゴールドが出るので、 〈開眼〉と同じく後ろに足す。 既定の形 (UntilKing) と旧い形 (Stacks) は何も得られないので置き換える
            bool toilNothing = gm.LastBattleBoon == EventSystem.TimedEffects.BattleBoon.Toil && GameHooks.ToilMode != EventSystem.TimedEffects.ToilMode.LayerLong;
            if (boon != null) d.body = toilNothing ? boon + "。" : d.body + "\n" + boon + "。";
            AddInstabilityLine(gm, d, 0);      // 不安定化 (2026-11-13): この戦闘で溜まった分 (エスカレーション・勝ち・報酬の秘宝)
            d.buttons.Add(B("次へ", "ok", true));
            d.onAction = (act, _) => { _lastCoinsGained = 0; gm.ConfirmReward(); Refresh(); };
            return d;
        }

        // ── 〈門〉 ──

        private static readonly string[] GateName = { "血を捧げる", "秘宝を焚く", "光に身を任せる" };
        private static readonly string[] GateDesc =
        {
            // 数は GameManager.Gate の定数から組み立てる (写しを持たない・#131)。
            "最大 HP −" + Mathf.RoundToInt(GameLoop.GameManager.GateBloodMaxHpPct * 100f) + "%。 拒むと〈不完全な修復〉を背負う。",
            "秘宝を " + GameLoop.GameManager.GateRelicDemand + " 個焚く。 拒むと〈不完全な起動〉を背負う。",
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
