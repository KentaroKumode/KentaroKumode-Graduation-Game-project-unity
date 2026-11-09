using System.Collections.Generic;
using MetaProgression;
using MetaProgression.Relics;

namespace UI.Game2D
{
    /// <summary>
    /// 遺物の窓 (<b>仮置き</b>・2026-11-10・docs/GAME.md §15-5)。 整備パネル (<see cref="Game2DMetaPanel"/>) の「遺物」から開く。
    /// 所持品の一覧から、 部位ごとに持ち込む 3 個を選ぶ・強化する (素材 1)・分解する (素材 +1)・★2 以下を一括で分解する・指名するセットを選ぶ。
    /// ゲームの処理は <see cref="MetaProgressState"/> と <see cref="RelicRoller"/> の口だけ。
    /// </summary>
    public static class Game2DRelicPanel
    {
        private const int PerPage = 8;
        private static Game2DModal _modal;
        private static int _page;
        private static readonly System.Random _rng = new System.Random();

        public static void Show(Game2DModal modal)
        {
            _modal = modal; _page = 0;
            _modal.Replace(Build());
        }

        private static string SetBonusLine(MetaProgressState st)
        {
            var cnt = new int[RelicCatalog.SetCount];
            foreach (var r in st.EquippedRelics()) cnt[r.set]++;
            var parts = new List<string>();
            for (int s = 0; s < cnt.Length; s++)
            {
                if (cnt[s] >= 2) parts.Add(RelicCatalog.SetNames[s] + "2: " + RelicCatalog.Set2Desc[s]);
                if (cnt[s] >= 3) parts.Add(RelicCatalog.SetNames[s] + "3: " + RelicCatalog.Set3Desc[s]);
            }
            return parts.Count > 0 ? string.Join(" / ", parts) : "セット効果なし";
        }

        private static ScreenDef Build()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm.State;
            var owned = st.ownedRelics ?? new List<Relic>();
            int pages = System.Math.Max(1, (owned.Count + PerPage - 1) / PerPage);
            if (_page >= pages) _page = pages - 1;
            string target = st.relicTargetSet >= 0 && st.relicTargetSet < RelicCatalog.SetCount ? RelicCatalog.SetNames[st.relicTargetSet] : "なし";
            var def = new ScreenDef
            {
                id = "relic_panel", jp = true, w = 472, cols = 1, rowH = 15,
                title = "遺物",
                sub = "所持 " + owned.Count + "  素材 " + st.relicMaterial + "  指名 " + target + "  (" + (_page + 1) + "/" + pages + ")",
                note = SetBonusLine(st),
            };
            int start = _page * PerPage;
            for (int i = start; i < System.Math.Min(owned.Count, start + PerPage); i++)
            {
                var r = owned[i];
                bool eq = st.equippedRelics != null && System.Array.IndexOf(st.equippedRelics, i) >= 0;
                string subs = "";
                for (int k = 0; k < r.SubCount; k++) subs += (k == 0 ? "" : "・") + RelicCatalog.Describe((RelicStat)r.subStats[k], r.subSteps[k]);
                def.Items.Add(new ModalItem
                {
                    name = (eq ? "[持] " : "") + r.Name,
                    value = "★" + r.stars + (r.enhance > 0 ? " +" + r.enhance : ""),
                    desc = RelicCatalog.Describe(r.MainStat, r.MainStep) + (subs.Length > 0 ? " / " + subs : ""),
                    tone = eq ? Game2DKit.Amber : (UnityEngine.Color32?)null,
                });
            }
            if (owned.Count == 0)
                def.Items.Add(new ModalItem { name = "遺物なし", desc = "ランを終えると遺物が手に入る (3 層以下 1 個 / 4 層 2 個 / 5 層到達 3 個)。", disabled = true });

            def.buttons.Add(new ModalBtn { label = "持ち込む", action = "equip", primary = true });
            def.buttons.Add(new ModalBtn { label = "強化 (素材1)", action = "enhance" });
            def.buttons.Add(new ModalBtn { label = "分解", action = "dismantle" });
            def.buttons.Add(new ModalBtn { label = "★2以下を分解", action = "bulk" });
            def.buttons.Add(new ModalBtn { label = "指名を変える", action = "target" });
            if (pages > 1) def.buttons.Add(new ModalBtn { label = "次の頁", action = "page" });
            def.buttons.Add(new ModalBtn { label = "整備へ", action = "back" });
            def.footer = "[持] = 持ち込み中 (部位ごとに 1 個)。 指名したセットはランの終わりに半分の確率で出る";
            def.onAction = (a, sel) =>
            {
                int idx = sel >= 0 ? start + sel : -1;
                bool valid = idx >= 0 && idx < owned.Count;
                switch (a)
                {
                    case "close": Game2DMetaPanel.Close(); return;
                    case "back": Game2DMetaPanel.Show(); return;
                    case "page": _page = (_page + 1) % pages; break;
                    case "equip":
                        if (!valid) { _modal.ShowToast("遺物を選ぶ"); break; }
                        st.ToggleEquip(idx); mpm.Save(); Game2DSfx.Play("ui_confirm");
                        break;
                    case "enhance":
                        if (!valid) { _modal.ShowToast("遺物を選ぶ"); break; }
                        if (!owned[idx].CanEnhance) { _modal.ShowToast("これ以上は強化できない"); break; }
                        if (st.relicMaterial <= 0) { _modal.ShowToast("素材が足りない (遺物を分解すると 1 つ)"); break; }
                        if (RelicRoller.Enhance(owned[idx], _rng))
                        {
                            st.relicMaterial--;
                            MetaProgression.Achievements.AchievementService.NoteRelicAcquired(owned[idx]);
                            mpm.Save(); Game2DSfx.Play("ui_confirm");
                        }
                        break;
                    case "dismantle":
                        if (!valid) { _modal.ShowToast("遺物を選ぶ"); break; }
                        st.DismantleRelic(idx); mpm.Save(); _modal.ShowToast("分解した (素材 +1)");
                        break;
                    case "bulk":
                    {
                        int n = st.DismantleUpToStars(2); mpm.Save();
                        _modal.ShowToast(n > 0 ? "★2 以下を " + n + " 個分解した (持ち込み中は残す)" : "分解できる★2 以下の遺物は無い");
                        break;
                    }
                    case "target":
                        st.relicTargetSet = st.relicTargetSet + 1 >= RelicCatalog.SetCount ? -1 : st.relicTargetSet + 1;
                        mpm.Save(); Game2DSfx.Play("ui_click");
                        break;
                }
                _modal.Replace(Build());
            };
            return def;
        }
    }
}
