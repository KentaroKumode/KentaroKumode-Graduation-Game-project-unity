#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace CombatSystem.DiceLED
{
    /// <summary>
    /// DiceLED 自動セットアップ エディタウィンドウ。
    /// 
    /// メニュー: Tools → DiceLED Auto Setup
    /// 
    /// ワンクリックで以下を自動実行:
    ///   1. シーン内から DICE_1～DICE_10 を検索
    ///   2. 各サイコロに SingleDiceLED コンポーネントを自動追加
    ///   3. 各サイコロの子 Renderer を座標から自動マッピング（LED名不問）
    ///   4. DiceLEDManager に playerDice / enemyDice を自動登録
    ///   5. DiceLEDManager が無ければ自動作成
    /// 
    /// LED のマッピングはローカル座標で自動判定:
    ///   Z 昇順（小 Z = 上段）、同行内で X 降順（大 X = 左列）
    /// </summary>
    public class DiceLEDAutoSetup : EditorWindow
    {
        // =================================================================
        //  定数
        // =================================================================

        private const string DICE_PREFIX = "DICE_";

        // =================================================================
        //  ウィンドウ状態
        // =================================================================

        private Vector2 scrollPos;
        private string logText = "";
        private List<DiceInfo> foundDice = new List<DiceInfo>();
        private DiceLEDManager foundManager;

        private struct DiceInfo
        {
            public GameObject go;
            public int number;
            public int rendererCount;
            public bool hasSingleDiceLED;
        }

        // =================================================================
        //  メニュー
        // =================================================================

        [MenuItem("Tools/DiceLED Auto Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<DiceLEDAutoSetup>("DiceLED Setup");
            window.minSize = new Vector2(420, 500);
        }

        // =================================================================
        //  GUI
        // =================================================================

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("DiceLED 自動セットアップ",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シーン内の DICE_1～DICE_10 を検索し、\n" +
                "子 Renderer のローカル座標で LED を自動マッピングします。\n\n" +
                "必要な命名:\n" +
                "  サイコロ親: DICE_1 ～ DICE_10\n" +
                "  LED 子: 任意の名前（座標で自動判定）\n\n" +
                "  DICE_1～5 = プレイヤー / DICE_6～10 = 敵\n\n" +
                "座標判定: Z小=上段, X大=左列",
                MessageType.Info);

            EditorGUILayout.Space(6);

            // ----- スキャンボタン -----
            if (GUILayout.Button("1. シーンをスキャン", GUILayout.Height(32)))
            {
                ScanScene();
            }

            // ----- スキャン結果表示 -----
            if (foundDice.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("検出結果", EditorStyles.boldLabel);

                EditorGUILayout.BeginVertical("box");
                foreach (var d in foundDice)
                {
                    string status = d.hasSingleDiceLED ? "✓" : "＋追加予定";
                    string rendStatus = d.rendererCount >= 9
                        ? $"Renderer: {d.rendererCount} ✓"
                        : $"Renderer: {d.rendererCount} ⚠ (9個必要)";

                    string side = d.number <= 5 ? "[Player]" : "[Enemy] ";
                    EditorGUILayout.LabelField(
                        $"  {side} {d.go.name}   {rendStatus}   Component: {status}");
                }
                EditorGUILayout.EndVertical();

                string managerStatus = foundManager != null
                    ? $"✓ {foundManager.gameObject.name}"
                    : "なし（自動作成します）";
                EditorGUILayout.LabelField($"DiceLEDManager: {managerStatus}");
            }

            EditorGUILayout.Space(8);

            // ----- 実行ボタン -----
            GUI.enabled = foundDice.Count > 0;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button("2. 自動セットアップ実行", GUILayout.Height(40)))
            {
                ExecuteAutoSetup();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // ----- ログ表示 -----
            if (!string.IsNullOrEmpty(logText))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("実行ログ", EditorStyles.boldLabel);
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos,
                    GUILayout.Height(200));
                EditorGUILayout.TextArea(logText, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        // =================================================================
        //  スキャン
        // =================================================================

        private void ScanScene()
        {
            foundDice.Clear();
            logText = "";

            // シーン内の全 GameObject を走査
            var allObjects = FindObjectsOfType<Transform>(true);
            foreach (var t in allObjects)
            {
                if (!t.gameObject.name.StartsWith(DICE_PREFIX,
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int num = ExtractNumber(t.gameObject.name, DICE_PREFIX);
                if (num < 1 || num > 10) continue;

                // 子 Renderer の数をカウント（自分自身は除外）
                int rendererCount = 0;
                var childRenderers = t.GetComponentsInChildren<Renderer>(true);
                foreach (var r in childRenderers)
                {
                    if (r.gameObject != t.gameObject)
                        rendererCount++;
                }

                foundDice.Add(new DiceInfo
                {
                    go = t.gameObject,
                    number = num,
                    hasSingleDiceLED = t.GetComponent<SingleDiceLED>() != null,
                    rendererCount = rendererCount
                });
            }

            // 番号順ソート
            foundDice = foundDice.OrderBy(d => d.number).ToList();

            // DiceLEDManager検索
            foundManager = FindObjectOfType<DiceLEDManager>();

            Repaint();
        }

        // =================================================================
        //  セットアップ実行
        // =================================================================

        private void ExecuteAutoSetup()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"=== DiceLED Auto Setup 開始 ===");
            log.AppendLine($"検出サイコロ: {foundDice.Count} 個");
            log.AppendLine();

            Undo.SetCurrentGroupName("DiceLED Auto Setup");
            int undoGroup = Undo.GetCurrentGroup();

            // ----- 1. 各サイコロに SingleDiceLED を追加 & LED 割り当て -----
            var playerSlots = new SingleDiceLED[5];
            var enemySlots  = new SingleDiceLED[5];

            foreach (var dice in foundDice)
            {
                log.AppendLine($"--- {dice.go.name} (#{dice.number}) ---");

                // SingleDiceLED コンポーネント確保
                var singleDice = dice.go.GetComponent<SingleDiceLED>();
                if (singleDice == null)
                {
                    singleDice = Undo.AddComponent<SingleDiceLED>(dice.go);
                    log.AppendLine($"  SingleDiceLED コンポーネント追加");
                }

                // 子 Renderer を取得（自分自身は除外）
                var childRenderers = new List<Renderer>();
                var allR = dice.go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in allR)
                {
                    if (r.gameObject != dice.go)
                        childRenderers.Add(r);
                }

                if (childRenderers.Count < 9)
                {
                    log.AppendLine($"  ⚠ 子 Renderer が {childRenderers.Count} 個（9個必要）");
                }

                // 座標ソート: Z昇順×X降順 で 3×3 グリッドにマッピング
                var sorted = SingleDiceLED.SortRenderersByPosition(childRenderers);

                // SerializedObject 経由で ledRenderers に書き込み
                var so = new SerializedObject(singleDice);
                var prop = so.FindProperty("ledRenderers");

                prop.arraySize = 9;
                for (int i = 0; i < 9 && i < sorted.Count; i++)
                {
                    prop.GetArrayElementAtIndex(i).objectReferenceValue =
                        sorted[i];
                    var lp = sorted[i].transform.localPosition;
                    log.AppendLine($"  [{i}] {sorted[i].gameObject.name}" +
                                   $"  (X={lp.x:F3}, Z={lp.z:F3})");
                }
                so.ApplyModifiedProperties();

                // Player / Enemy 振り分け
                if (dice.number >= 1 && dice.number <= 5)
                    playerSlots[dice.number - 1] = singleDice;
                else if (dice.number >= 6 && dice.number <= 10)
                    enemySlots[dice.number - 6] = singleDice;

                log.AppendLine();
            }

            // ----- 2. DiceLEDManager にサイコロを登録 -----
            if (foundManager == null)
            {
                var go = new GameObject("[DiceLEDManager]");
                Undo.RegisterCreatedObjectUndo(go, "Create DiceLEDManager");
                foundManager = go.AddComponent<DiceLEDManager>();
                log.AppendLine("DiceLEDManager を新規作成しました");
            }

            var mgrSO = new SerializedObject(foundManager);

            // playerDice
            var playerProp = mgrSO.FindProperty("playerDice");
            playerProp.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                playerProp.GetArrayElementAtIndex(i).objectReferenceValue =
                    playerSlots[i];
            }

            // enemyDice
            var enemyProp = mgrSO.FindProperty("enemyDice");
            enemyProp.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                enemyProp.GetArrayElementAtIndex(i).objectReferenceValue =
                    enemySlots[i];
            }

            mgrSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(foundManager);

            int pCount = playerSlots.Count(s => s != null);
            int eCount = enemySlots.Count(s => s != null);
            log.AppendLine($"DiceLEDManager 割り当て:");
            log.AppendLine($"  Player: {pCount} 個");
            log.AppendLine($"  Enemy:  {eCount} 個");

            // Undo グループを閉じる
            Undo.CollapseUndoOperations(undoGroup);

            log.AppendLine();
            log.AppendLine($"=== 完了（Ctrl+Z で元に戻せます）===");

            logText = log.ToString();
            Debug.Log(logText);

            // 再スキャン
            ScanScene();
        }

        // =================================================================
        //  ユーティリティ
        // =================================================================

        private static int ExtractNumber(string name, string prefix)
        {
            if (name.Length <= prefix.Length) return -1;
            string numPart = name.Substring(prefix.Length);
            // "LED_0_mesh" のように接尾辞がある場合も対応
            string digits = "";
            foreach (char c in numPart)
            {
                if (char.IsDigit(c))
                    digits += c;
                else
                    break;
            }
            return int.TryParse(digits, out int n) ? n : -1;
        }
    }
}
#endif
