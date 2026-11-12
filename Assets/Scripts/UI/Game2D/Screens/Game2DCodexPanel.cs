using System.Collections.Generic;
using Codex;
using CombatSystem;
using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 記録 (codex) の一覧と読む画面 (#24・<b>仮置き</b>・2026-11-11)。 タイトルの「GALLERY」 (<see cref="UI.Codex.CodexView"/>) から開く。
    /// 分類: 読み物 (<see cref="WorldVignettes"/>) / 層 (<see cref="LayerTitles"/>) / 敵 (enemies.json) / 品 (items.json)。 文面は各正本をそのまま出す (ここでは書かない)。
    /// 未解禁は「？？？」の伏せ字。 読み物は解禁の手がかり (<see cref="VignetteUnlockState.Hint"/>) だけ添える。 層・敵・品は着いた・戦った・手に入れた時に開く (<see cref="CodexRecord"/>)。
    /// <para>操作: 行を選んで「読む」(本文を頁で送る) / 1〜4 で分類 / ← → で頁 / Esc で戻る・閉じる。</para>
    /// </summary>
    public class Game2DCodexPanel : MonoBehaviour
    {
        public Game2DModal modal;

        private enum Cat { Story, Floor, Enemy, Item }
        private static readonly string[] CatName = { "読み物", "層", "敵", "品" };
        private const int PerPage = 16, ReadLines = 12, W = 472;
        private const string Hidden = "？？？";

        private struct Entry { public string name, desc, body, hint, tag; public bool open; }

        private static Game2DCodexPanel _inst;
        private ScreenDef _def;
        private Cat _cat; private int _page; private bool _reading; private int _readIdx, _readPage;
        private bool _lockedInput, _prevLock;
        private List<Entry> _entries;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            var m = FindObjectOfType<Game2DModal>();
            if (m != null && m.GetComponent<Game2DCodexPanel>() == null) m.gameObject.AddComponent<Game2DCodexPanel>().modal = m;
        }

        private void Awake() { _inst = this; }
        private void OnDestroy() { if (_inst == this) _inst = null; }

        private bool IsShown => modal != null && _def != null && modal.IsOpen && modal.Current == _def;

        /// <summary>窓を開く。 開けなければ false (Game2D の窓が無い・ほかの窓が出ている)。</summary>
        public static bool Open()
        {
            if (_inst == null) { Attach(); if (_inst == null) _inst = FindObjectOfType<Game2DCodexPanel>(); }
            if (_inst == null || _inst.modal == null || _inst.modal.IsOpen) return false;
            _inst.OpenImpl();
            return true;
        }

        private void OpenImpl()
        {
            _cat = Cat.Story; _page = 0; _reading = false;
            _prevLock = SpriteButton.InputLocked; SpriteButton.InputLocked = true; _lockedInput = true;      // 後ろのタイトルのボタンを押さない
            _entries = Entries(_cat);
            _def = Build(); modal.Open(_def);
        }

        private void Close()
        {
            if (IsShown) modal.Close();
            _def = null;
            if (_lockedInput) { SpriteButton.InputLocked = _prevLock; _lockedInput = false; }
        }

        private void Update()
        {
            if (modal == null || _def == null) return;
            if (!IsShown) { _def = null; if (_lockedInput) { SpriteButton.InputLocked = _prevLock; _lockedInput = false; } return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { if (_reading) { _reading = false; Rebuild(); } else Close(); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || (_reading && Input.GetKeyDown(KeyCode.UpArrow))) { Turn(-1); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow) || (_reading && Input.GetKeyDown(KeyCode.DownArrow))) { Turn(1); return; }
            if (!_reading)
                for (int i = 0; i < CatName.Length; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { SetCat((Cat)i); return; }
        }

        private void SetCat(Cat c) { _cat = c; _page = 0; _entries = Entries(c); Game2DSfx.Play("ui_tab"); Rebuild(); }

        private void Turn(int dir)
        {
            if (_reading) _readPage += dir;
            else _page += dir;
            Game2DSfx.Play("ui_click");
            Rebuild();
        }

        private void Rebuild() { _def = Build(); modal.Replace(_def); }

        // ── 項目 ──

        private static List<Entry> Entries(Cat c)
        {
            var l = new List<Entry>();
            switch (c)
            {
                case Cat.Story:
                    foreach (var v in WorldVignettes.All)
                    {
                        bool open = VignetteUnlockState.IsUnlocked(v);
                        l.Add(new Entry { open = open, name = open ? v.title : Hidden, body = v.body, desc = open ? FirstLine(v.body) : VignetteUnlockState.Hint(v), hint = VignetteUnlockState.Hint(v) });
                    }
                    break;
                case Cat.Floor:
                    for (int depth = 1; depth <= 8; depth++)
                    {
                        var e = LayerTitles.ForFloor(depth); bool open = CodexRecord.FloorReached(depth);
                        l.Add(new Entry { open = open, name = open ? e.title : Hidden, desc = open ? e.subtitle : "まだ着いていない。", body = e.title + "\n\n" + e.subtitle, hint = "まだ着いていない。" });
                    }
                    break;
                case Cat.Enemy:
                {
                    var all = EnemyDatabase.GetByFloorRange(int.MinValue, int.MaxValue);
                    var sorted = new List<EnemyData>(all);
                    // 層の順 (商人など層の外の敵は最後)。 同じ層の中は enemies.json の並び (OrderBy は安定)
                    sorted = new List<EnemyData>(System.Linq.Enumerable.OrderBy(sorted, x => x.floor >= 99 ? 1000 : x.floor));
                    foreach (var en in sorted)
                    {
                        bool open = CodexRecord.EnemyMet(en.id);
                        string nm = string.IsNullOrEmpty(en.displayName) ? en.id : en.displayName;
                        string tag = BossIds.IsBoss(en.id) ? "ボス" : en.elite ? "エリート" : null;
                        l.Add(new Entry { open = open, name = open ? nm : Hidden, tag = open ? tag : null, desc = open ? en.description : "まだ戦っていない。", body = EnemyBody(en), hint = "まだ戦っていない。" });
                    }
                    break;
                }
                case Cat.Item:
                {
                    var db = ItemSystem.ItemDatabase.Instance;
                    if (db == null) break;
                    var items = db.GetAllItems();
                    foreach (var kind in new[] { ItemSystem.ItemCategory.Weapon, ItemSystem.ItemCategory.Passive, ItemSystem.ItemCategory.Consumable })
                        foreach (var it in items)
                        {
                            if (it == null || it.category != kind) continue;
                            bool open = CodexRecord.ItemOwned(it.id);
                            if (!open && ItemIds.Entrusted.Contains(it.id)) continue;   // 預かり物 (博士の時計) は手に入れるまで「？？？」の枠も出さない (超低確率のイベントの種明かしになる)
                            string nm = string.IsNullOrEmpty(it.displayName) ? it.id : it.displayName;
                            string tag = kind == ItemSystem.ItemCategory.Weapon ? "武器" : kind == ItemSystem.ItemCategory.Consumable ? "消耗品" : null;
                            string body = nm + "\n\n" + it.description + (string.IsNullOrEmpty(it.flavorText) ? "" : "\n\n" + it.flavorText);
                            l.Add(new Entry { open = open, name = open ? nm : Hidden, tag = open ? tag : null, desc = open ? it.description : "まだ手に入れていない。", body = body, hint = "まだ手に入れていない。" });
                        }
                    break;
                }
            }
            return l;
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n'); return i < 0 ? s : s.Substring(0, i);
        }

        private static string EnemyBody(EnemyData e)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(string.IsNullOrEmpty(e.displayName) ? e.id : e.displayName).Append("\n\n");
            if (!string.IsNullOrEmpty(e.description)) sb.Append(e.description).Append("\n\n");
            sb.Append("HP ").Append(e.maxHP).Append("  防御 ").Append(e.defense).Append("  貫通 ").Append(e.armorPen)
              .Append("  攻撃 ").Append(e.UsesAttackRoll ? e.attackRollMin + "〜" + e.attackRollMax : e.EffectiveRollCount + " 個 × 1〜" + e.EffectiveRollMax);
            if (e.passiveSkills != null)
                foreach (var ps in e.passiveSkills)
                {
                    if (ps == null || string.IsNullOrEmpty(ps.skillName)) continue;
                    sb.Append("\n\n〈").Append(ps.skillName).Append("〉 ").Append(ps.description);
                    if (!string.IsNullOrEmpty(ps.flavorText)) sb.Append('\n').Append(ps.flavorText);
                }
            return sb.ToString();
        }

        // ── 画面 ──

        private ScreenDef Build() => _reading ? BuildReader() : BuildList();

        private ScreenDef BuildList()
        {
            if (_entries == null) _entries = Entries(_cat);
            int n = _entries.Count, pages = Mathf.Max(1, (n + PerPage - 1) / PerPage);
            _page = ((_page % pages) + pages) % pages;
            int opened = 0; foreach (var e in _entries) if (e.open) opened++;
            var d = new ScreenDef
            {
                id = "codex_panel", jp = true, w = W, cols = 2, rowH = 15,
                title = "記録 ― " + CatName[(int)_cat],
                sub = "開いた " + opened + "/" + n + "  頁 " + (_page + 1) + "/" + pages,
            };
            int first = _page * PerPage;
            for (int i = first; i < Mathf.Min(n, first + PerPage); i++)
            {
                var e = _entries[i];
                d.Items.Add(new ModalItem { name = e.name, value = e.tag, desc = string.IsNullOrEmpty(e.desc) ? " " : e.desc, disabled = !e.open });
            }
            if (n == 0) d.body = "項目が無い。";
            d.buttons.Add(new ModalBtn { label = "読む", action = "read", primary = true });
            d.buttons.Add(new ModalBtn { label = "前の頁", action = "prev" });
            d.buttons.Add(new ModalBtn { label = "次の頁", action = "next" });
            d.buttons.Add(new ModalBtn { label = "分類: " + CatName[((int)_cat + 1) % CatName.Length] + "へ", action = "cat" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            d.footer = "1 読み物 / 2 層 / 3 敵 / 4 品   ← → で頁   Esc で閉じる";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": Close(); return;
                    case "prev": Turn(-1); return;
                    case "next": Turn(1); return;
                    case "cat": SetCat((Cat)(((int)_cat + 1) % CatName.Length)); return;
                    case "read":
                    {
                        int idx = first + sel;
                        if (sel < 0 || idx >= n) return;
                        if (!_entries[idx].open) { modal.ShowToast(_entries[idx].hint); return; }
                        _reading = true; _readIdx = idx; _readPage = 0; Game2DSfx.Play("ui_confirm");
                        Rebuild();
                        return;
                    }
                }
            };
            return d;
        }

        private ScreenDef BuildReader()
        {
            var e = _entries[Mathf.Clamp(_readIdx, 0, _entries.Count - 1)];
            var lines = Game2DJpFont.Lines(e.body ?? "", W - 2 * 16 - 4);
            int pages = Mathf.Max(1, (lines.Count + ReadLines - 1) / ReadLines);
            _readPage = Mathf.Clamp(_readPage, 0, pages - 1);
            int first = _readPage * ReadLines;
            var sb = new System.Text.StringBuilder();
            for (int i = first; i < Mathf.Min(lines.Count, first + ReadLines); i++) { if (i > first) sb.Append('\n'); sb.Append(lines[i]); }
            var d = new ScreenDef
            {
                id = "codex_reader", jp = true, w = W, h = 266, cols = 1, bodyLines = ReadLines,
                title = e.name, sub = CatName[(int)_cat] + "  頁 " + (_readPage + 1) + "/" + pages,
                body = sb.Length > 0 ? sb.ToString() : " ",
            };
            d.buttons.Add(new ModalBtn { label = "前へ", action = "prev" });
            d.buttons.Add(new ModalBtn { label = "次へ", action = "next", primary = true });
            d.buttons.Add(new ModalBtn { label = "一覧へ", action = "back" });
            d.footer = "← → か ↑ ↓ で頁   Esc で一覧へ";
            d.onAction = (a, _) =>
            {
                switch (a)
                {
                    case "prev": Turn(-1); return;
                    case "next": Turn(1); return;
                    case "back": _reading = false; Rebuild(); return;
                }
            };
            return d;
        }
    }
}
