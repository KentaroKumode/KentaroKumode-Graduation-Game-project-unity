using GameLoop;
using MapSystem;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 補給拠点の画面 (#15・<b>仮置き</b>・2026-11-11)。
    /// <para>現状 (2026-11-11 に確認): 補給拠点 (<see cref="TileType.SupplyDepot"/>) は 5 層の一本道の 2 つ目の点にだけ置かれ、 踏んでも
    /// <c>GameManager.ActivateTile</c> の既定の分岐で航行へ戻るだけ ── 画面も補給の処理も無かった (TileType の注記「生成・効果は未実装」)。
    /// 2026-11-11: 着いた時に今の手持ちを見せ、 <b>物資を安く買える</b> (1 つ <c>ProvisionSystem.DepotPrice</c> G・数の限りなし)。</para>
    /// <para>出るのは人が遊んでいる時に補給拠点へ着いた瞬間だけ (BOT の間・ロード画面の間・ほかの窓が出ている間は出さない)。 「出発」か Esc で閉じる。
    /// 本番の絵は別 issue (#101)。</para>
    /// </summary>
    public class Game2DDepotPanel : MonoBehaviour
    {
        public Game2DModal modal;
        private ScreenDef _def;
        private MapNode _lastNode;
        private bool _pending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            var m = FindObjectOfType<Game2DModal>();
            if (m != null && m.GetComponent<Game2DDepotPanel>() == null) m.gameObject.AddComponent<Game2DDepotPanel>().modal = m;
        }

        private bool IsShown => modal != null && _def != null && modal.IsOpen && modal.Current == _def;

        private void Update()
        {
            if (modal == null) return;
            if (_def != null && !IsShown) _def = null;
            var gm = GameManager.Instance; var mm = MapManager.Instance;
            if (gm == null || mm == null || Application.isBatchMode || GameHooks.BotDriving) { if (IsShown) Close(); _pending = false; return; }
            var node = mm.CurrentNode;
            if (node != _lastNode) { _lastNode = node; _pending = node != null && node.type == TileType.SupplyDepot && gm.CurrentPhase == GameManager.GamePhase.MapNavigation; }
            if (IsShown)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || gm.CurrentPhase != GameManager.GamePhase.MapNavigation) Close();
                return;
            }
            if (!_pending || gm.CurrentPhase != GameManager.GamePhase.MapNavigation) return;
            if (modal.IsOpen || Game2DModal.Active || Game2DLoadingScreen.Blocking) return;      // ほかの窓が先 (閉じたら出す)
            _pending = false;
            _def = Build(gm.Run); modal.Open(_def);
        }

        private void Close() { if (IsShown) modal.Close(); _def = null; }

        private static int CountLures(RunState run)
        {
            int n = 0;
            if (run?.ownedConsumables != null) foreach (var c in run.ownedConsumables) if (c == ItemIds.Lure) n++;
            return n;
        }

        private ScreenDef Build(RunState run)
        {
            var d = new ScreenDef { id = "depot_panel", jp = true, w = 400, cols = 1, rowH = 15 };
            d.title = "補給拠点"; d.sub = run != null ? run.currentFloor + " 層" : null;
            d.body = "物資を安く買える拠点。 1 つ " + ProvisionSystem.DepotPrice + "G (数の限りは無い)。\n所持金 " + (run != null ? run.coins : 0) + "G";
            if (run != null)
            {
                int flareCost = Mathf.Max(1, MapSystem.FreeMove.FreeMapParams.FlareSupply);
                d.Items.Add(new ModalItem { name = "物資", value = run.provision + "/" + run.provisionCap, desc = "移動・照明・戦闘の消耗で減る。 層の始めの前哨基地と、 ショップの「物資の包」で増える。" });
                d.Items.Add(new ModalItem { name = "照明", value = "あと " + run.provision / flareCost + " 回", desc = "照明は物資で払う (1 回 物資 " + flareCost + ")。 残りの回数は今の物資から数えた値。" });
                d.Items.Add(new ModalItem { name = "囮罠", value = CountLures(run) + " 個", desc = "航行で置くと、 追跡者を罠の方へ引きつける。 ショップで買える。" });
                d.Items.Add(new ModalItem { name = "HP", value = run.playerHP + "/" + run.playerMaxHP, desc = "休憩・前哨基地・消耗品で回復する。" });
            }
            bool canBuy = run != null && run.coins >= ProvisionSystem.DepotPrice && run.provision < run.provisionCap;
            if (canBuy) d.buttons.Add(new ModalBtn { label = "物資を買う " + ProvisionSystem.DepotPrice + "G", action = "buy", primary = true });
            d.buttons.Add(new ModalBtn { label = "出発", action = "close", primary = !canBuy });
            d.footer = "行を選ぶと説明が出る / Esc で閉じる";
            d.onAction = (a, _) =>
            {
                if (a == "close") { Close(); return; }
                if (a == "buy" && ProvisionSystem.TryBuyAtDepot(GameManager.Instance?.Run)) { _def = Build(GameManager.Instance.Run); modal.Open(_def); }   // 買ったら数字を描き直す
            };
            return d;
        }
    }
}
