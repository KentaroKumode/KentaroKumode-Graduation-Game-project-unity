using System.Collections;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoinSystem
{
    /// <summary>
    /// コイン排出システムのユーザーインターフェイス
    /// Qキー: コイン排出, Rキー: リセット
    /// </summary>
    public class CoinDispenserTest : MonoBehaviour
    {
        [Header("コイン設定")]
        [SerializeField] private CoinDispenser coinDispenser;
        
        [Header("払い出し設定")]
        [SerializeField] private int dispensingAmount = 5; // インスペクターで変更可能
        [SerializeField] private int minDispenseAmount = 1;
        [SerializeField] private int maxDispenseAmount = 100; // 100枚までテスト可能
        
        [Header("支払い設定")]
        [SerializeField] private int paymentAmount = 3; // 支払い枚数
        [SerializeField] private int minPaymentAmount = 1;
        [SerializeField] private int maxPaymentAmount = 50; // 50枚まで支払い可能
        
        [Header("パフォーマンス監視")]
        [SerializeField] private bool showPerformanceInfo = true;
        private float deltaTime = 0.0f;
    
        // OnGUI最適化用キャッシュ
        private GUIStyle cachedBoldStyle;
        private GUIStyle cachedFpsStyle;
        
        [SerializeField] private KeyCode dispenseKey = KeyCode.Space;
        [SerializeField] private KeyCode resetKey = KeyCode.R;
        
        private void Start()
        {
            // GUIスタイルキャッシュ初期化
            InitializeGUIStyles();
            
            if (coinDispenser == null)
            {
                coinDispenser = FindObjectOfType<CoinDispenser>();
            }
            
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser not found! Please assign it in the inspector or ensure a CoinDispenser exists in the scene.");
                return;
            }
            
            // イベントの購読
            coinDispenser.OnDispenseComplete += OnDispenseComplete;
            coinDispenser.OnSortComplete += OnSortComplete;
            
            Debug.Log($"CoinDispenser Interface ready. Controls: {dispenseKey}=dispense, {resetKey}=reset");
        }
        
        private void Update()
        {
            return; // キー入力を無効化
            #pragma warning disable CS0162
            // FPS計算
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
            
            // Qキー: コイン排出（ハードコード）
            if (Input.GetKeyDown(KeyCode.Q))
            {
                Debug.Log("Q key pressed - Dispensing coins!");
                DispenseCoins();
            }
            
            // dispenseKeyによる排出（Inspector設定）
            if (Input.GetKeyDown(dispenseKey) && dispenseKey != KeyCode.Q)
            {
                Debug.Log($"Key {dispenseKey} pressed!");
                DispenseCoins();
            }
            
            // Rキー: リセット（ハードコード）
            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("R key pressed - Resetting!");
                ResetCoins();
            }
            
            // resetKeyによるリセット（Inspector設定）
            if (Input.GetKeyDown(resetKey) && resetKey != KeyCode.R)
            {
                Debug.Log($"Key {resetKey} pressed!");
                ResetCoins();
            }
            
            // 強制リセット機能 (Fキー)
            if (Input.GetKeyDown(KeyCode.F))
            {
                Debug.Log("F key pressed - Force resetting CoinDispenser state!");
                ForceResetDispenserState();
            }
            
            // 100枚テスト (Tキー)
            if (Input.GetKeyDown(KeyCode.T))
            {
                Debug.Log("T key pressed - Testing 100 coins performance!");
                Test100Coins();
            }
            
            // チケット排出テスト (Yキー)
            if (Input.GetKeyDown(KeyCode.Y))
            {
                Debug.Log("Y key pressed - Testing ticket dispensing!");
                TestTicketDispensing();
            }
            
            // ディスプレイテスト (Dキー)
            if (Input.GetKeyDown(KeyCode.D))
            {
                Debug.Log("D key pressed - Testing display!");
                TestDisplay();
            }
            
            // ディスプレイ数値増減テスト (矢印キー)
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                TestDisplayIncrement();
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                TestDisplayDecrement();
            }
            
            // 極限テスト (高性能PC用)
            if (Input.GetKeyDown(KeyCode.X))
            {
                Debug.Log("X key pressed - EXTREME STRESS TEST!");
                StartCoroutine(ExtremeStressTest());
            }
            
            // メモリリーク手動チェック (Mキー)

        }
        
        /// <summary>
        /// GUIスタイルの初期化
        /// </summary>
        private void InitializeGUIStyles()
        {
            if (cachedBoldStyle == null)
            {
                cachedBoldStyle = new GUIStyle();
                cachedBoldStyle.fontStyle = FontStyle.Bold;
                cachedBoldStyle.normal.textColor = Color.white;
            }
            
            if (cachedFpsStyle == null)
            {
                cachedFpsStyle = new GUIStyle();
                cachedFpsStyle.normal.textColor = Color.green;
            }
        }
        
        private void DispenseCoins()
        {
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser is not assigned!");
                return;
            }
            
            // coinPrefabが未設定の場合、CoinSystemManagerの初期化を待つ
            if (coinDispenser.CoinPrefab == null)
            {
                Debug.LogWarning("CoinDispenser is not fully initialized yet. Trying to find CoinSystemManager...");
                CoinSystemManager manager = FindObjectOfType<CoinSystemManager>();
                if (manager != null)
                {
                    Debug.Log("CoinSystemManager found. It should initialize CoinDispenser shortly.");
                }
                else
                {
                    Debug.LogError("CoinSystemManager not found! Please ensure CoinSystemManager exists in the scene and is enabled.");
                }
                return;
            }
            
            Debug.Log($"CoinDispenser Status - IsDispensing: {coinDispenser.IsDispensing}, IsSorting: {coinDispenser.IsSorting}");
            
            if (coinDispenser.IsDispensing || coinDispenser.IsSorting)
            {
                Debug.LogWarning("CoinDispenser is currently busy. Please wait.");
                Debug.LogWarning("Press F to force reset CoinDispenser state if stuck.");
                return;
            }
            
            Debug.Log($"Dispensing {dispensingAmount} coins...");
            coinDispenser.DispenseCoins(dispensingAmount);
        }
        
        private void PayCoins()
        {
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser is not assigned!");
                return;
            }
            
            Debug.Log($"Paying {paymentAmount} coins...");
            StartCoroutine(coinDispenser.ConsumeCoins(paymentAmount));
        }
        
        private void ResetCoins()
        {
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser is not assigned!");
                return;
            }
            
            // coinPrefabが未設定の場合、警告を出す
            if (coinDispenser.CoinPrefab == null)
            {
                Debug.LogWarning("CoinDispenser is not fully initialized yet. Reset operation may not work properly.");
                Debug.LogWarning("Please ensure CoinSystemManager exists and is enabled in the scene.");
            }
            
            Debug.Log("Resetting all coins...");
            coinDispenser.ReturnAllCoinsToPool();
        }
        
        private void Test100Coins()
        {
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser is not assigned!");
                return;
            }
            
            if (coinDispenser.IsDispensing || coinDispenser.IsSorting)
            {
                Debug.LogWarning("CoinDispenser is busy. Reset first.");
                return;
            }
            
            float startTime = Time.realtimeSinceStartup;
            Debug.Log($"=== Starting 100 coins performance test at {startTime:F2}s ===");
            Debug.Log($"Pre-test FPS: {1.0f / deltaTime:F1}");
            
            // 一時的に払い出し枚数を100に設定
            int originalAmount = dispensingAmount;
            dispensingAmount = 100;
            
            DispenseCoins();
            
            // 元の値に戻す
            dispensingAmount = originalAmount;
            
            Debug.Log($"100 coins dispense initiated. Monitor performance in GUI.");
            
            // メモリリーク対策: 大量排出後は少し待ってからGC呼び出し
            StartCoroutine(PostTest100CoinsCleanup());
        }
        
        private System.Collections.IEnumerator PostTest100CoinsCleanup()
        {
            yield return new WaitForSeconds(5f); // 積み上げ完了を待つ
            Debug.Log("Performing cleanup after 100 coins test");
            System.GC.Collect();
            yield return null;
        }
        
        private void TestTicketDispensing()
        {
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser is not assigned!");
                return;
            }
            
            if (coinDispenser.IsDispensing || coinDispenser.IsSorting)
            {
                Debug.LogWarning("CoinDispenser is busy. Reset first with F key.");
                return;
            }
            
            Debug.Log("=== Testing direct ticket dispensing ===");
            Debug.Log("Dispensing 2 tickets directly for testing");
            
            // 2枚のチケットを直接排出
            StartCoroutine(TestTicketDispensingCoroutine());
        }
        
        private System.Collections.IEnumerator TestTicketDispensingCoroutine()
        {
            // TicketSystemManagerを取得してCreateAndDispenseTicketsを呼び出し
            var ticketManager = coinDispenser.GetComponent<TicketSystemManager>();
            var audioManager = coinDispenser.GetComponent<CoinAudioManager>();
            
            if (ticketManager != null)
            {
                Debug.Log("Testing ticket dispensing through TicketSystemManager");
                yield return StartCoroutine(ticketManager.CreateAndDispenseTickets(2, () => audioManager?.PlayTicketSound()));
                Debug.Log("Ticket dispensing test completed");
            }
            else
            {
                Debug.LogError("Could not find TicketSystemManager component");
            }
        }

        /// <summary>
        /// 極限ストレステスト - 高性能PCでも負荷を感じられるレベル
        /// </summary>
        private IEnumerator ExtremeStressTest()
        {
            Debug.Log("=== EXTREME STRESS TEST INITIATED ===");
            Debug.Log("WARNING: This will spawn multiple waves of 100 coins!");
            
            if (coinDispenser == null || coinDispenser.IsDispensing || coinDispenser.IsSorting)
            {
                Debug.LogWarning("Cannot start extreme test - dispenser busy or null");
                yield break;
            }
            
            float initialFPS = 1.0f / deltaTime;
            Debug.Log($"Initial FPS: {initialFPS:F1}");
            
            // 3波の100枚排出で300枚同時存在を目指す
            for (int wave = 0; wave < 3; wave++)
            {
                Debug.Log($"Stress Test Wave {wave + 1}/3");
                
                int originalAmount = dispensingAmount;
                dispensingAmount = 100;
                
                DispenseCoins();
                dispensingAmount = originalAmount;
                
                // 次の波まで5秒待機（積み上げが終わる前に次を開始）
                yield return new WaitForSeconds(5f);
            }
            
            Debug.Log("EXTREME STRESS TEST COMPLETE - Monitor performance!");
            Debug.Log($"Target: 300+ active coins with physics simulation");
        }
        
        private void ForceResetDispenserState()
        {
            if (coinDispenser == null)
            {
                Debug.LogError("CoinDispenser is not assigned!");
                return;
            }
            
            Debug.Log("=== Force Resetting CoinDispenser State ===");
            
            // コルーチンを停止
            StopAllCoroutines();
            
            // リフレクションでフラグを直接リセット
            var dispenserType = typeof(CoinDispenser);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            dispenserType.GetField("isDispensing", flags)?.SetValue(coinDispenser, false);
            dispenserType.GetField("isSorting", flags)?.SetValue(coinDispenser, false);
            
            Debug.Log($"State after reset - IsDispensing: {coinDispenser.IsDispensing}, IsSorting: {coinDispenser.IsSorting}");
            
            // コインもリセット
            coinDispenser.ReturnAllCoinsToPool();
            
            Debug.Log("Force reset complete!");
        }
        
        private void OnDispenseComplete(int amount)
        {
            Debug.Log($"Dispense complete! {amount} coins dispensed.");
        }
        
        private void OnSortComplete(int amount)
        {
            Debug.Log($"Sort complete! {amount} coins sorted into stacks.");
        }
        
        private void OnDestroy()
        {
            // イベントの購読解除
            if (coinDispenser != null)
            {
                coinDispenser.OnDispenseComplete -= OnDispenseComplete;
                coinDispenser.OnSortComplete -= OnSortComplete;
            }
        }
        
        private void OnGUI()
        {
            // 完全に無効化（TLS Allocatorエラー防止）
            return;
        }

        #region Display Test Methods
        private int testDisplayValue = 0;

        /// <summary>
        /// ディスプレイのテスト - ランダム数値を表示
        /// </summary>
        private void TestDisplay()
        {
            CoinSystemController controller = FindObjectOfType<CoinSystemController>();
            if (controller == null)
            {
                Debug.LogWarning("CoinSystemController not found!");
                return;
            }
            
            testDisplayValue = Random.Range(0, 999);
            controller.UpdateDisplay(testDisplayValue);
            Debug.Log($"Display test: Showing {testDisplayValue}");
        }
        
        /// <summary>
        /// ディスプレイ数値を増加
        /// </summary>
        private void TestDisplayIncrement()
        {
            CoinSystemController controller = FindObjectOfType<CoinSystemController>();
            if (controller == null)
            {
                Debug.LogWarning("CoinSystemController not found!");
                return;
            }
            
            testDisplayValue = Mathf.Min(999, testDisplayValue + 1);
            controller.UpdateDisplay(testDisplayValue);
            Debug.Log($"Display increment: {testDisplayValue}");
        }
        
        /// <summary>
        /// ディスプレイ数値を減少
        /// </summary>
        private void TestDisplayDecrement()
        {
            CoinSystemController controller = FindObjectOfType<CoinSystemController>();
            if (controller == null)
            {
                Debug.LogWarning("CoinSystemController not found!");
                return;
            }
            
            testDisplayValue = Mathf.Max(0, testDisplayValue - 1);
            controller.UpdateDisplay(testDisplayValue);
            Debug.Log($"Display decrement: {testDisplayValue}");
        }
        #endregion
    }
}