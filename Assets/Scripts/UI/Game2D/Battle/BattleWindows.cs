using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 戦闘の画面から開く窓 (道具・〈有効活用〉の捨て札・持ち物の画面) と、 端子の説明の「対象」の行。
    /// 2026-11-16 に旧い戦闘の HUD (Game2DBattleHud・液晶の外のパネル群) を消した時に、 新しい画面 (<see cref="Game2DBattleLcd"/>) が使う分だけを切り出した。
    /// 中身は旧 HUD のまま (窓は共通の板のモーダル <see cref="Game2DModal"/>・使うのは本体の関数 = BOT と同じ)。
    /// </summary>
    public sealed class BattleWindows
    {
        private readonly Game2DBattleSim sim;
        /// <summary>窓を閉じた・使った時に、 画面に描き直しを頼む (無ければ null)。</summary>
        private readonly System.Action _redraw;
        public BattleWindows(Game2DBattleSim sim, System.Action redraw) { this.sim = sim; _redraw = redraw; }

        /// <summary>持ち物の画面を開く (見るだけ・消耗品の [使う] は出さない ── 戦闘中に使うのは [道具] の窓)。 戦闘の進行・乱数・判断待ちには触らない。</summary>
        public void OpenInventory()
        {
            if (GameHooks.BotDriving || Game2DModal.Active) return;
            if (Game2DNavExtras.OpenInventoryInBattle(GameManager.Instance)) Game2DSfx.Play("ui_click");
        }

        /// <summary>対象を取る端子の「対象: 〈◯◯〉」の行 (対象を取る端子でなければ null)。 解決の後は結果の対象を出す。</summary>
        public string TargetLine(int f)
        {
            string id = sim.board[f];
            // 〈有効活用〉(手札の持ち越しの規則): 捨て札から戻す 1 枚
            if (CombatSystem.TerminalCards.PicksDiscard(id))
            {
                if (sim.ShowingResult) return null;
                var disc = sim.Discard;
                if (disc == null || disc.Count == 0) return "捨て札が空 (戻す端子が無い)";
                if (!sim.IsRecaller(f)) return "出目に挿すと、 捨て札から戻す 1 枚を選べる";
                int r = sim.RecallOf(f);
                return r >= 0 ? "戻す: 〈" + disc[r] + "〉 (押して選び直す)" : null;
            }
            if (!CombatSystem.TerminalCards.PicksTarget(id)) return null;
            int t = -1;
            if (sim.ShowingResult)
            {
                var o = sim.lastOutcome;
                if (o == null || !o.fired.Contains(f)) return null;
                string bid = CombatSystem.TerminalCards.BaseOf(id);
                t = bid == CombatSystem.TerminalIds.Copier ? o.copyTarget : bid == CombatSystem.TerminalIds.BurnOut ? o.burnTarget : o.retainTarget;
            }
            else
            {
                if (!sim.IsPicker(f))
                    return CombatSystem.TerminalCards.BaseOf(id) == CombatSystem.TerminalIds.BurnOut      // 〈火葬の剣〉(2026-11-14): 外す端子が無ければ挿しても不発
                        ? "出目に挿すと、 挿していない手札の端子を外す対象に選べる (外す端子が無いと発動しない)"
                        : "出目に挿すと、 挿していない手札の端子を対象に選べる";
                t = sim.TargetOf(f);
            }
            return t >= 0 && t < sim.board.Count ? "対象: 〈" + sim.board[t] + "〉" : "対象: なし (空けた端子が無い)";
        }

        private Game2DModal _modal;
        // ── 道具 (消耗品・issue #11・gui-needed.md #31) ──
        //   [道具] = 所持品の消耗品の窓。 使えるのはターンの判断 (振り直し・配線) で止まっている間だけ (Game2DBattleSim.CanUseItems)。
        //   使うのは本体の Consumables.Use (BOT と同じ関数)。 使えない品 (HP が満ちている・戦闘の外でだけ使える・死翔で封じられた 等) は
        //   Consumables.BlockReason で暗く出し、 押すと理由を足元に出す。 囮罠は航行の道具なので並べない。
        //   〈シュヴァリエのレイピア〉(パッシブ・消費しない) も同じ窓の先頭で切り替える (Consumables.TryUseRapier・BOT と同じ関数)。

        /// <summary>窓の 1 行: 消耗品 (id・同じ品は 1 行) か、 レイピアの切り替え。</summary>
        private struct ItemRow { public string id; public bool rapier; }

        /// <summary>戦闘の窓に並べる品 (レイピア → 消耗品の持っている順・同じ品は 1 行・囮罠は除く)。</summary>
        private static List<ItemRow> CombatItems()
        {
            var l = new List<ItemRow>(); var run = GameManager.Instance?.Run;
            if (run == null) return l;
            if (run.ownedPassiveItems != null && run.ownedPassiveItems.Contains(ItemIds.ChevalierRapier)) l.Add(new ItemRow { id = ItemIds.ChevalierRapier, rapier = true });
            if (run.ownedConsumables == null) return l;
            var seen = new HashSet<string>();
            foreach (var id in run.ownedConsumables)
                if (seen.Add(id) && ItemIds.ConsFamilyOf(id) != ItemIds.ConsLureFamily) l.Add(new ItemRow { id = id });
            return l;
        }

        /// <summary>今その行を使えるか (使えなければ理由)。 段 (判断の間か) は呼び手が見る。</summary>
        private static string RowBlock(RunState run, ItemRow r)
            => r.rapier ? (Consumables.CanUseRapier(run) ? null : "今は切り替えられない") : Consumables.BlockReason(run, r.id);

        /// <summary>[道具] を明るく出すか: 判断の間で、 使える品が 1 つでもある。</summary>
        public bool AnyUsable()
        {
            if (!sim.CanUseItems) return false;
            var run = GameManager.Instance?.Run; if (run == null) return false;
            foreach (var r in CombatItems()) if (RowBlock(run, r) == null) return true;
            return false;
        }

        // ── 〈有効活用〉の窓 (捨て札から 1 枚・仮の見た目 = 道具の窓と同じ部品) ──
        //   開く: 〈有効活用〉を出目に挿した時 / 挿してある〈有効活用〉を押した (動かさずに放した) 時。 捨て札が空なら開かない。
        //   最初から「今戻る 1 枚」(選んでいなければ最後に捨てた 1 枚) を選んでおき、 その行に「戻す」の印を付ける。
        //   [これを戻す] = 選んだ行に決める / [閉じる] = 今の 1 枚のまま。 決めた値は sim.target[〈有効活用〉の場の位置] = 捨て札の添字。

        public void OpenRecall(int s)
        {
            if (!sim.CanOperate || !sim.IsRecaller(s)) return;
            if (_modal == null) _modal = Object.FindObjectOfType<Game2DModal>();
            if (_modal == null) return;
            _modal.Open(RecallDef(s));
        }

        private ScreenDef RecallDef(int s)
        {
            var disc = sim.Discard ?? new List<string>();
            int cur = sim.RecallOf(s);
            var d = new ScreenDef { id = "battle_recall", jp = true, title = "〈" + sim.board[s] + "〉: 戻す 1 枚", w = 460, cols = 2,
                sub = "捨て札 " + disc.Count + " 枚から選ぶ (次のターンの手札に入る)", sel0 = Mathf.Max(0, cur) };
            for (int i = 0; i < disc.Count; i++)
            {
                var td = CombatSystem.TerminalCards.Get(disc[i]);
                d.Items.Add(new ModalItem { name = disc[i], value = i == cur ? "戻す" : "", desc = td?.description });
            }
            d.buttons.Add(new ModalBtn { label = "これを戻す", action = "pick", primary = true });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "選ばずに閉じると、 最後に捨てた 1 枚が戻る";
            d.onAction = (a, sel) =>
            {
                if (a == "close") { _modal.Close(); return; }
                if (a != "pick") return;
                if (!sim.SetRecall(s, sel)) { _modal.ShowError("今は選べない"); return; }
                _redraw?.Invoke();
                _modal.Close();
            };
            return d;
        }

        private int _itemPage;
        private const int ItemsPerPage = 10;

        /// <summary>消耗品を選んで使う窓 (判断の間だけ)。 使っても窓は閉じずに中身を写し直す (続けて使える・HP とシールドは題の下に出す)。</summary>
        public void OpenItems()
        {
            if (!sim.CanUseItems) { Game2DSfx.Play("ui_click"); return; }
            if (_modal == null) _modal = Object.FindObjectOfType<Game2DModal>();
            var run = GameManager.Instance?.Run;
            if (_modal == null || run == null || CombatItems().Count == 0) { Game2DSfx.Play("ui_click"); return; }
            _itemPage = 0;
            _modal.Open(ItemsDef());
        }

        private ScreenDef ItemsDef()
        {
            var run = GameManager.Instance?.Run;
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            var all = CombatItems();
            int pages = Mathf.Max(1, (all.Count + ItemsPerPage - 1) / ItemsPerPage); _itemPage = Mathf.Clamp(_itemPage, 0, pages - 1);
            int first = _itemPage * ItemsPerPage, cnt = Mathf.Min(ItemsPerPage, all.Count - first);
            int shield = ctx != null ? ctx.consShield : 0;
            var d = new ScreenDef { id = "battle_items", jp = true, title = "道具", w = 460, cols = 2,
                sub = "HP " + sim.playerHp + "/" + sim.playerMax + "  シールド " + shield };
            var rows = new List<ItemRow>();
            for (int i = 0; i < cnt; i++)
            {
                var r = all[first + i]; rows.Add(r);
                var data = ItemSystem.ItemDatabase.Instance?.GetItem(r.id);
                string name = ItemSystem.ItemDatabase.DisplayNameOf(data, r.id);
                string block = run != null ? RowBlock(run, r) : "使えない";
                string value;
                if (r.rapier) value = Consumables.RapierContreOn() ? "構え中" : "";
                else
                {
                    int n = 0; foreach (var c in run.ownedConsumables) if (c == r.id) n++;
                    value = "x" + n;
                }
                string desc = data?.description;
                if (!r.rapier && r.id == ItemIds.OldLetter)      // 1 枚で数回使える (使い切った時だけ所持から減る)
                    desc = (desc ?? "") + "\n(今の 1 枚はあと " + Mathf.Max(1, ItemIds.OldLetterUses - run.oldLetterUsesSpent) + " 回)";
                if (block != null) desc = string.IsNullOrEmpty(desc) ? "(" + block + ")" : desc + "\n(" + block + ")";
                d.Items.Add(new ModalItem { icon = r.id, rarity = data != null ? data.rarity : (ItemSystem.ItemRarity?)null, name = name, value = value, desc = desc, disabled = block != null });
            }
            if (cnt <= 0) d.body = "使える品を持っていない。";
            d.buttons.Add(new ModalBtn { label = "使う", action = "use", primary = true });
            if (pages > 1) d.buttons.Add(new ModalBtn { label = "次の頁 " + (_itemPage + 1) + "/" + pages, action = "page" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "このターンの判断の間に使う (敵の攻撃より先に効く)";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": _modal.Close(); return;
                    case "page": _itemPage = (_itemPage + 1) % pages; break;
                    case "use":
                    {
                        if (sel < 0 || sel >= rows.Count) return;
                        if (!sim.CanUseItems) { _modal.ShowError("今は使えない"); return; }
                        var r = rows[sel]; var rs = GameManager.Instance?.Run;
                        string why = rs != null ? RowBlock(rs, r) : "使えない";
                        if (why != null) { _modal.ShowError(why); return; }
                        bool ok = r.rapier ? sim.UseRapier() : sim.UseItem(r.id);
                        if (!ok) { _modal.ShowError("今は使えない"); return; }
                        Game2DSfx.Play("ui_confirm"); _modal.ShowToast(r.rapier ? (Consumables.RapierContreOn() ? "構えた" : "構えを解いた") : "使った");
                        _redraw?.Invoke();
                        if (CombatItems().Count == 0) { _modal.Close(); return; }
                        break;
                    }
                    default: return;
                }
                _modal.Replace(ItemsDef());
            };
            return d;
        }

    }
}
