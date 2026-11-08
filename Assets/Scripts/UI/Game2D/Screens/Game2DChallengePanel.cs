using MetaProgression;

namespace UI.Game2D
{
    /// <summary>
    /// 挑戦の難易度を選ぶ窓 (2026-11-08・docs/GAME.md §23-25)。 整備パネル (<see cref="Game2DMetaPanel"/>) の「挑戦」から開く。
    /// 一本の目盛り (0..10 = ラン中にカードを引く回数) と、 その難易度で「どの層の移り目で何 Tier を引くか」の並びを出す。
    /// カードの中身はここでは見せない (ラン中に選ぶ時の楽しみ)。 選べるのは 5 層を踏破した最高の難易度 +1 まで。
    /// <para>旧: 軸ごとに点数のボタンを押す配点の窓 (§15-2) は、 この窓に置き換えた。 配点そのもの (<see cref="ChallengeLoadout"/>) は BOT の測定が使うので残る。</para>
    /// </summary>
    public static class Game2DChallengePanel
    {
        private static Game2DModal _modal;

        /// <summary>整備パネルから挑戦の頁へ切り替える (同じ窓の中身を差し替える)。</summary>
        public static void Show(Game2DModal modal)
        {
            _modal = modal;
            _modal.Replace(Build());
        }

        private static int MaxSelectable(MetaProgressState st) => System.Math.Min(ChallengeCards.MaxDifficulty, st.challengeBest + 1);

        private static ScreenDef Build()
        {
            var mpm = MetaProgressManager.Instance; var st = mpm.State;
            int d = System.Math.Max(0, System.Math.Min(st.challengeDifficulty, MaxSelectable(st)));
            var def = new ScreenDef
            {
                id = "challenge_panel", jp = true, w = 420, cols = 1, sliders = true, sliderNameW = 64,
                title = "挑戦",
                sub = "記録 " + st.challengeBest,
            };
            def.Items.Add(new ModalItem
            {
                name = "難易度", count = d, max = ChallengeCards.MaxDifficulty,
                effect = d == 0 ? "カードを引かない" : "カードを " + d + " 回引く",
                desc = "層の移り目でカードを引き、 2 枚から 1 枚を選ぶ。 選べるのは記録 +1 (" + MaxSelectable(st) + ") まで。",
            });
            // どの層の移り目で何 Tier を引くか (重い Tier ほど熱く暗い色)
            var sch = ChallengeCards.Schedule(d);
            for (int s = 0; s < ChallengeCards.Slots; s++)
            {
                string tiers = ""; int top = 0;
                foreach (var e in sch) if (e.slot == s) { tiers += (tiers.Length > 0 ? " " : "") + ChallengeCards.Roman(e.tier); if (e.tier > top) top = e.tier; }
                def.Items.Add(new ModalItem
                {
                    name = ChallengeCards.SlotNames[s], value = tiers.Length > 0 ? tiers : "―",
                    tone = top > 0 ? Game2DModal.ChallengeTierColor(top) : Game2DKit.Faint,
                    desc = top > 0 ? "この移り目で " + tiers.Split(' ').Length + " 回引く。" : "この移り目では引かない。",
                });
            }
            def.buttons.Add(new ModalBtn { label = "整備へ", action = "back", primary = true });
            def.buttons.Add(new ModalBtn { label = "閉じる", action = "close" });
            def.footer = "目盛りを押す・引いて難易度を選ぶ";
            def.onAction = (act, sel) =>
            {
                switch (act)
                {
                    case "close": Game2DMetaPanel.Close(); return;
                    case "back": Game2DMetaPanel.Show(); return;
                    case "slide":
                    {
                        if (sel != 0) break;
                        int goal = System.Math.Max(0, System.Math.Min(Game2DModal.SlideTarget, ChallengeCards.MaxDifficulty));
                        int cap = MaxSelectable(st);
                        if (goal > cap) { _modal.ShowToast("難易度 " + goal + " は、 " + (goal - 1) + " を踏破すると選べる"); goal = cap; }
                        if (goal != st.challengeDifficulty)
                        {
                            st.challengeDifficulty = goal;
                            mpm.ClearChallenge();   // 旧の配点が残っているとカードのランにならない (BeginChallengeCards)
                            mpm.Save();
                            Game2DSfx.Play("ui_click");
                        }
                        break;
                    }
                }
                _modal.Replace(Build());
            };
            return def;
        }
    }
}
