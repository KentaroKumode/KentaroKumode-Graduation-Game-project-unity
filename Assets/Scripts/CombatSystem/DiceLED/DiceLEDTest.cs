using UnityEngine;

namespace CombatSystem.DiceLED
{
    /// <summary>
    /// DiceLED システムのランタイムテスト用コントローラー。
    /// CombatManager 無しでダイスロールの LED 演出をテストできる。
    /// 
    /// <para><b>操作:</b></para>
    /// <list type="bullet">
    ///   <item>Space: ダイスロール（ローリング→確定→結果表示）</item>
    ///   <item>1～9 キー: 全サイコロをその出目で即時表示（パターン確認用）</item>
    ///   <item>0 キー: 全消灯</item>
    ///   <item>R: リセット（ログもクリア）</item>
    ///   <item>C: プレイヤー色切り替え</item>
    ///   <item>↑/↓: プレイヤーダイス数 増減</item>
    ///   <item>←/→: 敵ダイス数 増減</item>
    /// </list>
    /// </summary>
    public class DiceLEDTest : MonoBehaviour
    {
        [Header("テスト設定")]
        [Tooltip("テスト用のプレイヤーダイス数")]
        [SerializeField, Range(1, 5)] private int testPlayerDiceCount = 2;

        [Tooltip("テスト用の敵ダイス数")]
        [SerializeField, Range(1, 5)] private int testEnemyDiceCount = 2;

        [Tooltip("テスト用のプレイヤーダイス最大値")]
        [SerializeField, Range(1, 9)] private int testPlayerDiceMax = 6;

        [Tooltip("テスト用の敵ダイス最大値")]
        [SerializeField, Range(1, 9)] private int testEnemyDiceMax = 6;

        private DiceLEDManager manager;
        private int colorIndex;
        private int rollCount;

        // --- 最新ロール結果 ---
        private int[] lastPlayerValues;
        private int[] lastEnemyValues;
        private int lastPlayerTotal;
        private int lastEnemyTotal;
        private string lastResultText = "";
        private bool lastPlayerMax;
        private bool lastEnemyMax;

        private readonly Color[] testColors = new Color[]
        {
            new Color(0.2f, 0.8f, 1f),   // 水色
            new Color(0.2f, 1f, 0.3f),    // 緑
            new Color(1f, 0.9f, 0.2f),    // 黄色
            new Color(1f, 0.4f, 0.8f),    // ピンク
        };

        void Start()
        {
            manager = DiceLEDManager.Instance;
            if (manager == null)
            {
                Debug.LogError("[DiceLEDTest] DiceLEDManager が見つかりません");
                enabled = false;
                return;
            }

            manager.OnRollingComplete += OnRollComplete;
            manager.OnAllMax += OnAllMax;
        }

        void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnRollingComplete -= OnRollComplete;
                manager.OnAllMax -= OnAllMax;
            }
        }

        void Update()
        {
            if (manager == null) return;

            // ----- Space: ダイスロール -----
            if (Input.GetKeyDown(KeyCode.Space) && !manager.IsRolling)
            {
                PlayTestRolling();
            }

            // ----- 数字キー: パターン確認 -----
            for (int num = 0; num <= 9; num++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + num)
                    || Input.GetKeyDown(KeyCode.Keypad0 + num))
                {
                    ShowAllValue(num);
                    return;
                }
            }

            // ----- M: 全最大値テスト -----
            if (Input.GetKeyDown(KeyCode.M) && !manager.IsRolling)
            {
                PlayMaxRolling();
            }

            // ----- R: リセット -----
            if (Input.GetKeyDown(KeyCode.R))
            {
                manager.TurnOffAll();
                rollCount = 0;
                lastResultText = "";
                lastPlayerValues = null;
                lastEnemyValues = null;
                lastPlayerMax = false;
                lastEnemyMax = false;
                Debug.Log("[DiceLEDTest] リセット");
            }

            // ----- C: 色切り替え -----
            if (Input.GetKeyDown(KeyCode.C))
            {
                colorIndex = (colorIndex + 1) % testColors.Length;
                manager.SetPlayerColor(testColors[colorIndex]);
            }

            // ----- 矢印キー: ダイス数調整 -----
            if (Input.GetKeyDown(KeyCode.UpArrow))
                testPlayerDiceCount = Mathf.Min(testPlayerDiceCount + 1, 5);
            if (Input.GetKeyDown(KeyCode.DownArrow))
                testPlayerDiceCount = Mathf.Max(testPlayerDiceCount - 1, 1);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                testEnemyDiceCount = Mathf.Min(testEnemyDiceCount + 1, 5);
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                testEnemyDiceCount = Mathf.Max(testEnemyDiceCount - 1, 1);
        }

        // =================================================================
        //  ダイスロール
        // =================================================================

        /// <summary>ランダム出目でローリング → 確定</summary>
        private void PlayTestRolling()
        {
            lastPlayerValues = new int[testPlayerDiceCount];
            lastEnemyValues  = new int[testEnemyDiceCount];

            lastPlayerTotal = 0;
            lastEnemyTotal  = 0;

            for (int i = 0; i < lastPlayerValues.Length; i++)
            {
                lastPlayerValues[i] = Random.Range(1, testPlayerDiceMax + 1);
                lastPlayerTotal += lastPlayerValues[i];
            }
            for (int i = 0; i < lastEnemyValues.Length; i++)
            {
                lastEnemyValues[i] = Random.Range(1, testEnemyDiceMax + 1);
                lastEnemyTotal += lastEnemyValues[i];
            }

            rollCount++;
            lastResultText = "Rolling...";

            manager.SetActiveDiceCount(testPlayerDiceCount, testEnemyDiceCount);
            manager.PlayRollingAnimation(
                lastPlayerValues, lastEnemyValues,
                testPlayerDiceMax, testEnemyDiceMax);

            string pStr = string.Join("+", lastPlayerValues);
            string eStr = string.Join("+", lastEnemyValues);
            Debug.Log($"[DiceLEDTest] Roll #{rollCount}: P[{pStr}]={lastPlayerTotal}  E[{eStr}]={lastEnemyTotal}");
        }

        /// <summary>ローリング完了コールバック → 結果テキスト更新</summary>
        private void OnRollComplete()
        {
            string maxTag = "";
            if (lastPlayerMax) maxTag += " [P:MAX!]";
            if (lastEnemyMax)  maxTag += " [E:MAX!]";

            if (lastPlayerTotal > lastEnemyTotal)
                lastResultText = $"Player WIN!  差: {lastPlayerTotal - lastEnemyTotal}{maxTag}";
            else if (lastPlayerTotal < lastEnemyTotal)
                lastResultText = $"Enemy WIN!  差: {lastEnemyTotal - lastPlayerTotal}{maxTag}";
            else
                lastResultText = $"DRAW!{maxTag}";
        }

        /// <summary>全最大値イベントコールバック</summary>
        private void OnAllMax(bool isPlayer)
        {
            if (isPlayer)
                lastPlayerMax = true;
            else
                lastEnemyMax = true;

            Debug.Log($"[DiceLEDTest] ALL MAX! ({(isPlayer ? "Player" : "Enemy")})");
        }

        /// <summary>全ダイスを最大値にしてローリング（演出確認用）</summary>
        private void PlayMaxRolling()
        {
            lastPlayerValues = new int[testPlayerDiceCount];
            lastEnemyValues  = new int[testEnemyDiceCount];
            lastPlayerTotal = 0;
            lastEnemyTotal  = 0;
            lastPlayerMax = false;
            lastEnemyMax  = false;

            for (int i = 0; i < lastPlayerValues.Length; i++)
            {
                lastPlayerValues[i] = testPlayerDiceMax;
                lastPlayerTotal += testPlayerDiceMax;
            }
            for (int i = 0; i < lastEnemyValues.Length; i++)
            {
                lastEnemyValues[i] = Random.Range(1, testEnemyDiceMax + 1);
                lastEnemyTotal += lastEnemyValues[i];
            }

            rollCount++;
            lastResultText = "Rolling... (MAX TEST)";

            manager.SetActiveDiceCount(testPlayerDiceCount, testEnemyDiceCount);
            manager.PlayRollingAnimation(
                lastPlayerValues, lastEnemyValues,
                testPlayerDiceMax, testEnemyDiceMax);

            Debug.Log($"[DiceLEDTest] MAX TEST Roll #{rollCount}");
        }

        /// <summary>全サイコロを指定値で即時表示（パターン確認）</summary>
        private void ShowAllValue(int value)
        {
            int[] pValues = new int[testPlayerDiceCount];
            int[] eValues = new int[testEnemyDiceCount];
            for (int i = 0; i < pValues.Length; i++) pValues[i] = value;
            for (int i = 0; i < eValues.Length; i++) eValues[i] = value;

            manager.SetActiveDiceCount(testPlayerDiceCount, testEnemyDiceCount);
            manager.ShowResultImmediate(pValues, eValues);
            lastResultText = $"パターン確認: {value}";
        }

        // =================================================================
        //  OnGUI デバッグ表示
        // =================================================================

        private GUIStyle headerStyle;
        private GUIStyle bodyStyle;
        private GUIStyle resultStyle;

        void OnGUI()
        {
            if (manager == null) return;

            // スタイル初期化
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 16,
                    fontStyle = FontStyle.Bold
                };
                bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 13
                };
                resultStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
            }

            int x = 10, y = 10, w = 400, h = 22;

            // タイトル
            GUI.Label(new Rect(x, y, w, h + 4),
                "<color=white>Dice LED Test</color>", headerStyle);
            y += 28;

            // ステータス
            string rollingStr = manager.IsRolling
                ? "<color=yellow>ROLLING...</color>"
                : "<color=lime>READY</color>";
            GUI.Label(new Rect(x, y, w, h), rollingStr, bodyStyle);
            y += h;

            // ダイス構成
            GUI.Label(new Rect(x, y, w, h),
                $"<color=cyan>Player:</color> {testPlayerDiceCount}d{testPlayerDiceMax}   " +
                $"<color=#FF6644>Enemy:</color> {testEnemyDiceCount}d{testEnemyDiceMax}",
                bodyStyle);
            y += h;

            // ロール回数
            GUI.Label(new Rect(x, y, w, h),
                $"Roll Count: {rollCount}", bodyStyle);
            y += h + 4;

            // 最新結果
            if (lastPlayerValues != null && lastPlayerValues.Length > 0)
            {
                string pDice = string.Join(" + ", lastPlayerValues);
                string eDice = string.Join(" + ", lastEnemyValues);

                GUI.Label(new Rect(x, y, w, h),
                    $"<color=cyan>P: [{pDice}] = {lastPlayerTotal}</color>", bodyStyle);
                y += h;
                GUI.Label(new Rect(x, y, w, h),
                    $"<color=#FF6644>E: [{eDice}] = {lastEnemyTotal}</color>", bodyStyle);
                y += h + 2;
            }

            // 勝敗結果
            if (!string.IsNullOrEmpty(lastResultText))
            {
                GUI.Label(new Rect(x, y, w, h + 4),
                    $"<color=white>{lastResultText}</color>", resultStyle);
                y += h + 8;
            }

            // 操作説明
            y += 4;
            GUI.Label(new Rect(x, y, w, h),
                "<color=#AAAAAA>Space=Roll  M=MAXテスト  0-9=パターン  R=Reset  C=色</color>",
                bodyStyle);
            y += h;
            GUI.Label(new Rect(x, y, w, h),
                "<color=#AAAAAA>↑↓=Pダイス数  ←→=Eダイス数</color>",
                bodyStyle);
        }
    }
}
