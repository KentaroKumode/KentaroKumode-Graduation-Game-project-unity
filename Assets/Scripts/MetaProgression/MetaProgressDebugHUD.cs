using UnityEngine;

namespace MetaProgression
{
    /// <summary>
    /// メタ進行のデバッグ表示・操作 UI。IMGUI ベース。
    /// 本格 UI 実装までの繋ぎ。
    /// </summary>
    public class MetaProgressDebugHUD : MonoBehaviour
    {
        [SerializeField] private bool startVisible = false;
        [SerializeField] private KeyCode toggleKey = KeyCode.M;
        [SerializeField] private int fontSize = 13;

        private bool visible;
        private Vector2 buffScroll;
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
            float w = 460f, h = Mathf.Min(620f, Screen.height - 40f);
            float x = Screen.width - w - 10f, y = 10f;

            var box = new GUIStyle(GUI.skin.box) { fontSize = fontSize, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8,8,8,8) };
            GUILayout.BeginArea(new Rect(x, y, w, h), "", box);

            GUILayout.Label($"<b>[Meta] トークン: {s.tokens}    Lv {s.currentLevel}/{MetaBuffTrack.TotalSteps}</b>",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize + 2, richText = true });

            GUILayout.Label($"集計: HP+{s.hpBonus} / Gold+{s.goldBonus} / Dice+{s.diceTotalBonus} / DmgRed-{s.damageReduce} / Hunger-{s.hungerReduce} / Mat+{s.startMaterial} / WinG+{s.combatGoldBonus} / FloorHeal+{s.floorClearHeal}",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true });
            string bossExtra = s.bossExtraRareUnlocked ? "レア" : (s.bossExtraNormalUnlocked ? "ノーマル" : "なし");
            string extras = (s.divineProtectUnlocked ? " 神加護" : "")
                          + (s.startingPassiveItemUnlocked ? " 開幕P" : "")
                          + (s.treasureChestGoldUnlocked ? " 宝箱G" : "");
            GUILayout.Label($"Refund Lv{s.refundLevel} ({MetaBuffApplicator.GetRefundChance() * 100f:0}%) / Crit Lv{s.critLevel} / BossExtra: {bossExtra}{extras}",
                new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true });

            GUILayout.Space(6);

            // 次の購入
            if (mgr.IsTrackComplete)
            {
                GUILayout.Label("=== 全段階開放済み ===");
            }
            else
            {
                var next = MetaBuffTrack.Get(mgr.NextLevel);
                string label = next != null ? next.DisplayLabel : "-";
                string major = (next != null && next.isMajor) ? " 【大】" : "";
                GUILayout.Label($"次: Lv{mgr.NextLevel} {label}{major}  コスト {mgr.NextCost}");
                GUI.enabled = mgr.CanPurchase();
                if (GUILayout.Button($"購入する  (-{mgr.NextCost})", GUILayout.Height(28)))
                    mgr.TryPurchase();
                GUI.enabled = true;
            }

            GUILayout.Space(6);

            // バフトラック一覧（折りたたみ風）
            GUILayout.Label("--- バフトラック (大スキルのみ表示) ---");
            buffScroll = GUILayout.BeginScrollView(buffScroll, GUILayout.Height(150f));
            for (int lv = 1; lv <= MetaBuffTrack.TotalSteps; lv++)
            {
                var step = MetaBuffTrack.Get(lv);
                if (step == null || !step.isMajor) continue;
                string mark = lv <= s.currentLevel ? "✓" : (lv == mgr.NextLevel ? "→" : " ");
                GUILayout.Label($"{mark} Lv{lv}: {step.DisplayLabel} (cost {MetaBuffTrack.CalcCost(lv)})");
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);

            // デバフトグル
            GUILayout.Label("--- デバフトグル ---");
            debuffScroll = GUILayout.BeginScrollView(debuffScroll, GUILayout.Height(180f));
            foreach (MetaDebuffLevel d in System.Enum.GetValues(typeof(MetaDebuffLevel)))
            {
                bool on = s.HasDebuff(d);
                bool toggled = GUILayout.Toggle(on, $"Lv{(int)d}: {d.ToString().Substring(d.ToString().IndexOf('_') + 1)}");
                if (toggled != on) mgr.ToggleDebuff(d, toggled);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1000T (debug)")) mgr.AddTokens(1000);
            if (GUILayout.Button("リセット")) mgr.ResetAll();
            if (GUILayout.Button("閉じる (M)")) visible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
