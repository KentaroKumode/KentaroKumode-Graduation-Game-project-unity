using UnityEngine;

namespace CoinSystem
{
    /// <summary>
    /// CoinSystemの自動セットアップマネージャー
    /// シーンにCoinSystemの必要コンポーネントを自動配置
    /// </summary>
    [System.Serializable]
    public class CoinSystemSetupManager : MonoBehaviour
    {
        [Header("自動セットアップ")]
        [SerializeField] private bool autoSetupOnStart = true;
        [SerializeField] private GameObject coinPrefab; // Assets/Prefabs/CoinSystem/coin.prefab をアサイン
        
        [Header("セットアップ設定")]
        [SerializeField] private bool createUserInterface = true;
        [SerializeField] private Vector3 dispenserPointOffset = new Vector3(0, 2, 0);
        [SerializeField] private Vector3 potTargetOffset = new Vector3(0, 0, 2);
        [SerializeField] private Vector3 stackStartOffset = new Vector3(0, 0, 3);
        
        void Start()
        {
            Debug.Log("=== CoinSystemSetupManager Started ===");
            
            if (autoSetupOnStart)
            {
                SetupCoinSystem();
            }
        }
        
        private void SetupCoinSystem()
        {
            Debug.Log("Setting up CoinSystem...");
            
            // 1. CoinDispenser のセットアップ
            CoinDispenser coinDispenser = FindObjectOfType<CoinDispenser>();
            if (coinDispenser == null)
            {
                GameObject dispenserObj = new GameObject("CoinDispenser");
                coinDispenser = dispenserObj.AddComponent<CoinDispenser>();
                Debug.Log("Created CoinDispenser GameObject");
                
                // Transform参照の作成と設定
                SetupTransformReferences(coinDispenser);
                
                // コインプレハブの設定
                if (coinPrefab != null)
                {
                    SetCoinPrefab(coinDispenser, coinPrefab);
                }
                else
                {
                    Debug.LogWarning("CoinPrefab not assigned! Please assign coin.prefab to CoinSystemSetupManager.");
                }
            }
            else
            {
                Debug.Log("CoinDispenser already exists");
            }
            
            // 2. ユーザーインターフェースのセットアップ
            if (createUserInterface)
            {
                if (FindObjectOfType<CoinDispenserTest>() == null)
                {
                    GameObject interfaceObj = new GameObject("CoinSystemInterface");
                    CoinDispenserTest interfaceScript = interfaceObj.AddComponent<CoinDispenserTest>();
                    
                    // CoinDispenserの参照を設定
                    SetCoinDispenserReference(interfaceScript, coinDispenser);
                    
                    Debug.Log("Created CoinSystem user interface");
                }
                else
                {
                    Debug.Log("CoinSystem interface already exists");
                }
            }
            
            Debug.Log("CoinSystem setup complete!");
            Debug.Log("Controls: Q = dispense coins, R = reset coins");
        }
        
        private void SetupTransformReferences(CoinDispenser dispenser)
        {
            // DispenserPoint の作成
            GameObject dispenserPoint = new GameObject("DispenserPoint");
            dispenserPoint.transform.position = transform.position + dispenserPointOffset;
            dispenserPoint.transform.SetParent(dispenser.transform);
            
            // PotTarget の作成
            GameObject potTarget = new GameObject("PotTarget");
            potTarget.transform.position = transform.position + potTargetOffset;
            potTarget.transform.SetParent(dispenser.transform);
            
            // StackStartPoint の作成
            GameObject stackStartPoint = new GameObject("StackStartPoint");
            stackStartPoint.transform.position = transform.position + stackStartOffset;
            stackStartPoint.transform.SetParent(dispenser.transform);
            
            // リフレクションを使用してプライベートフィールドを設定
            var dispenserType = typeof(CoinDispenser);
            
            var dispenserPointField = dispenserType.GetField("dispenserPoint", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var potTargetField = dispenserType.GetField("potTarget", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var stackStartPointField = dispenserType.GetField("stackStartPoint", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            dispenserPointField?.SetValue(dispenser, dispenserPoint.transform);
            potTargetField?.SetValue(dispenser, potTarget.transform);
            stackStartPointField?.SetValue(dispenser, stackStartPoint.transform);
            
            Debug.Log("Transform references created and assigned");
        }
        
        private void SetCoinPrefab(CoinDispenser dispenser, GameObject prefab)
        {
            var dispenserType = typeof(CoinDispenser);
            var coinPrefabField = dispenserType.GetField("coinPrefab", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            coinPrefabField?.SetValue(dispenser, prefab);
            Debug.Log("CoinPrefab assigned to CoinDispenser");
        }
        
        private void SetCoinDispenserReference(CoinDispenserTest coinInterface, CoinDispenser dispenser)
        {
            var interfaceType = typeof(CoinDispenserTest);
            var coinDispenserField = interfaceType.GetField("coinDispenser", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            coinDispenserField?.SetValue(coinInterface, dispenser);
            Debug.Log("CoinDispenser reference assigned to CoinSystem interface");
        }
        
        // Inspector からボタンでセットアップ実行
        [ContextMenu("Setup CoinSystem")]
        private void ForceSetupCoinSystem()
        {
            SetupCoinSystem();
        }
        
        void Update()
        {
            return; // キー入力を無効化
            #pragma warning disable CS0162
            // F1キーでCoinSystemをセットアップ
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log("F1 pressed - Setting up CoinSystem");
                SetupCoinSystem();
            }
        }
    }
}