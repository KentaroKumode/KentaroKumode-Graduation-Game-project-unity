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
        private int _lastCoinsGained, _lastCoins = -1, _lastHp = -1;
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
            if (ph != _shown) { _shown = ph; _dirty = true; if (ph != GameManager.GamePhase.EventEncounter) _eventResult = null; if (ph == GameManager.GamePhase.GateRitual) System.Array.Clear(_gateOffer, 0, 3); }
            // 画面の外で所持金・HP が変わった時も描き直す (買える品の明暗・副題の数字)
            int coins = gm.Run != null ? gm.Run.coins : 0, hp = gm.Run != null ? gm.Run.playerHP : 0;
            if (coins != _lastCoins || hp != _lastHp) { _lastCoins = coins; _lastHp = hp; if (_open) _dirty = true; }
            if (!_dirty) return;
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
            switch (ph)
            {
                case GameManager.GamePhase.ShopVisit: return Shop(gm);
                case GameManager.GamePhase.EventEncounter: return Event(gm);
                case GameManager.GamePhase.Reward: return Reward(gm);
                case GameManager.GamePhase.RestStop:
                {
                    var d = New("休憩", "HP " + run.playerHP + "/" + run.playerMaxHP);
                    d.Items.Add(new ModalItem { name = "休む", desc = "HP を回復する。" });
                    d.Items.Add(new ModalItem { name = "鍛える", desc = "素材を使って武器を 1 段強化する (強化できなければダイス強化・それも無理なら休む)。", value = "素材 " + run.weaponMaterials });
                    d.buttons.Add(B("決める", "do", true));
                    d.onAction = (a, sel) => { if (a != "do") return; if (sel == 1) gm.RestUpgrade(); else gm.RestHeal(); Refresh(); };
                    return d;
                }
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
                    var d = New("層を抜けた", run.currentFloor + " 層"); d.body = "次の層へ降りる。";
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

        // ── ショップ ──

        private ScreenDef Shop(GameManager gm)
        {
            var run = gm.Run; var sm = ShopManager.Instance; var inv = sm != null ? sm.Current : null;
            var d = New("ショップ", "所持金 " + run.coins + "G", 460); d.cols = 2;
            if (inv != null)
            {
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var s = inv.slots[i];
                    int price = s.kind == ShopSlotKind.WeaponMaterial ? inv.CurrentMaterialPrice : s.price;
                    bool sold = s.kind != ShopSlotKind.WeaponMaterial && s.sold;
                    d.Items.Add(new ModalItem
                    {
                        name = SlotName(s), desc = SlotDesc(s),
                        value = sold ? "売切" : price + "G",
                        disabled = sold || price > run.coins,
                    });
                }
            }
            d.buttons.Add(B("買う", "buy", true));
            if (inv != null) d.buttons.Add(B("振り直す " + inv.CurrentRerollPrice + "G", "reroll"));
            d.buttons.Add(B("出る", "leave"));
            d.footer = "品を選んで「買う」";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "buy":
                    {
                        int before = run.coins; gm.ShopBuy(sel);
                        if (run.coins == before && sel >= 0 && inv != null && sel < inv.slots.Count && !inv.slots[sel].sold) modal.ShowToast("買えない (所持金が足りない・売り切れ)");
                        else Game2DSfx.Play("ui_confirm");
                        break;
                    }
                    case "reroll": if (!gm.ShopReroll()) modal.ShowToast("振り直せない (所持金が足りない)"); break;
                    case "leave": gm.ExitShop(); break;
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
            if (ev != null) foreach (var c in ev.choices) d.Items.Add(new ModalItem { name = c.text });
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
            "最大 HP −25%。 拒むと〈不完全な修復〉を背負う。",
            "遺物を 2 個焚く。 拒むと〈不完全な起動〉を背負う。",
            "物資 −40。 拒むと〈不完全な転移〉を背負う。",
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
