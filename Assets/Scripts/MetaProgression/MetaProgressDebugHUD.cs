using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// メタ進行のデバッグ表示・操作 UI。IMGUI ベース。
    /// 本格 UI (整備パネル) 実装までの繋ぎ。
    ///
    /// 旧58段トラック表示から、ポイント購入 + 16トラック配点表示へ刷新。
    /// キー: M でトグル。
    /// </summary>
    public class MetaProgressDebugHUD : MonoBehaviour
    {
        [SerializeField] private bool startVisible = false;
        [SerializeField] private KeyCode toggleKey = KeyCode.M;
        [SerializeField] private int fontSize = 13;

        private bool visible;
        private Vector2 panelScroll;
        private Vector2 debuffScroll;

        void Awake() { visible = startVisible; }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;
            var mgr = MetaProgressManager.Instance;
            if (mgr?.State == null) return;

            var s = mgr.State;
            float w = 480f, h = Mathf.Min(680f, Screen.height - 40f);
            float x = Screen.width - w - 10f, y = 10f;

            var box = new GUIStyle(GUI.skin.box) { fontSize = fontSize, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8,8,8,8) };
            GUILayout.BeginArea(new Rect(x, y, w, h), "", box);

            int assigned = s.TotalAssignedPoints();
            int unassigned = s.UnassignedPoints();

            GUILayout.Label($"<b>[Meta v6] トークン: {s.tokens}    ポイント {s.panelPointsPurchased}/{MetaPanel.MaxPoints}  (割振 {assigned} / 未 {unassigned})</b>",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize + 2, richText = true });

            // ポイント購入
            int cost = mgr.NextPointCost;
            bool canBuy = mgr.CanPurchasePoint();
            GUI.enabled = canBuy;
            if (GUILayout.Button(mgr.IsPointCapReached ? "ポイント上限到達" : $"ポイント +1 購入  (-{cost})", GUILayout.Height(26)))
                mgr.TryPurchasePoint();
            GUI.enabled = true;

            GUILayout.Space(4);

            // トラック一覧
            GUILayout.Label("--- 整備パネル (▲配点 / ▼払戻) ---");
            panelScroll = GUILayout.BeginScrollView(panelScroll, GUILayout.Height(360f));
            foreach (MetaPanelKind k in System.Enum.GetValues(typeof(MetaPanelKind)))
            {
                int r   = s.GetRank(k);
                int max = k.MaxRank();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{k,-13} {r}/{max}", GUILayout.Width(140));
                // 目盛りの視覚化
                var bar = new System.Text.StringBuilder();
                for (int i = 0; i < max; i++) bar.Append(i < r ? "●" : "○");
                GUILayout.Label(bar.ToString(), GUILayout.Width(180));
                GUI.enabled = unassigned > 0 && r < max;
                if (GUILayout.Button("▲", GUILayout.Width(28))) { mgr.TryAssignRank(k); }
                GUI.enabled = r > 0;
                if (GUILayout.Button("▼", GUILayout.Width(28))) { mgr.TryRefundRank(k); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // 集計 (Applicator 由来)
            GUILayout.Label(
                $"HP+{MetaPanel.ShellHp(s.GetRank(MetaPanelKind.Shell))} " +
                $"開幕Gold+{MetaPanel.VaultGold(s.GetRank(MetaPanelKind.Vault))} " +
                $"ボスGold+{MetaBuffApplicator.GetBossGoldBonus()} " +
                $"道中P率+{MetaBuffApplicator.GetPlunderPassiveDropPct():F0}% " +
                $"素材+{MetaPanel.SupplyStartMaterial(s.GetRank(MetaPanelKind.Supply))} " +
                $"与ダメ+{MetaBuffApplicator.GetOutgoingDamagePct()}% " +
                $"被ダメ-{MetaBuffApplicator.GetGuardDamageReductionPct() * 100f:F0}% " +
                $"希望-{MetaBuffApplicator.GetHopeLossReduction()} " +
                $"会心率+{MetaBuffApplicator.GetCritRatePctBonus() * 100f:F1}%",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true });

            string unlocks = "";
            if (MetaBuffApplicator.IsTreasureChestGoldUnlocked())    unlocks += " 宝箱G";
            if (MetaBuffApplicator.IsShopRobberyUnlocked())          unlocks += " 強盗";
            if (MetaBuffApplicator.IsStartingPassiveItemUnlocked())  unlocks += " 開幕P";
            GUILayout.Label($"解禁:{(unlocks.Length == 0 ? " なし" : unlocks)}  特売{MetaBuffApplicator.GetSaleItemCount()}枠 "
                + $"({MetaBuffApplicator.GetSaleDiscountMinPct()}〜{MetaBuffApplicator.GetSaleDiscountMaxPct()}%)",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true });

            GUILayout.Space(6);

            // 挑戦デバフ (docs/GAME.md §15-2)。 ボタンを押すたび存在する Tier を順送りする。
            GUILayout.Label($"--- 挑戦デバフ  {s.ChallengeScore}pt [{s.Challenge.BandName}] ---");
            debuffScroll = GUILayout.BeginScrollView(debuffScroll, GUILayout.Height(180f));
            for (int i = 0; i < ChallengeCatalog.Axes.Count; i++)
            {
                var d = ChallengeCatalog.Axes[i];
                int cur = s.Challenge.Tier(d.axis);
                if (GUILayout.Button($"{d.displayName}  {(cur == 0 ? "—" : "T" + cur)}"))
                    mgr.SetChallengeTier(d.axis, NextTier(d, cur));
            }
            GUILayout.Space(4);
            for (int i = 0; i < ChallengeCatalog.T4s.Count; i++)
            {
                var d = ChallengeCatalog.T4s[i];
                bool on = s.Challenge.Has(d.t4);
                bool tg = GUILayout.Toggle(on, $"【T4】{d.displayName} ({d.points}pt)");
                if (tg != on) mgr.SetChallengeT4(d.t4, tg);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("挑戦 全解除")) mgr.ClearChallenge();
            if (GUILayout.Button("挑戦 100pt")) mgr.SetChallengeMax(true);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1000T")) mgr.AddTokens(1000);
            if (GUILayout.Button("リスペック")) mgr.RespecAllRanks();
            if (GUILayout.Button("MAX (test)")) mgr.MaxAllForTesting();
            if (GUILayout.Button("全リセット")) mgr.ResetAll();
            if (GUILayout.Button("閉じる (M)")) visible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
        /// <summary>存在する Tier を順送りする (未選択 → 最小 → … → 最大 → 未選択)。</summary>
        private static int NextTier(AxisDef d, int cur)
        {
            if (cur == 0) return d.availableTiers[0];
            for (int i = 0; i < d.availableTiers.Length; i++)
                if (d.availableTiers[i] == cur)
                    return (i + 1 < d.availableTiers.Length) ? d.availableTiers[i + 1] : 0;
            return 0;
        }

    }
}
