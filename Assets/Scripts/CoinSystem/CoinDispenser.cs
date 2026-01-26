using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// コイン排出専用マネージャー
    /// </summary>
    public class CoinDispenser : MonoBehaviour
    {
        public int CurrentStackCoinCount => stackManager != null ? stackManager.CurrentStackCoinCount : 0;
        public int StackCount => stackManager != null ? stackManager.StackCount : 0;
        public GameObject CoinPrefab => poolManager != null ? poolManager.CoinPrefab : null;
        public int ActiveCoinCount => poolManager != null ? poolManager.ActiveCoinCount : 0;
        public int TotalStackedCoins => stackManager != null ? stackManager.TotalStackedCoins : 0;
        public int ActiveTicketCount => ticketManager != null ? ticketManager.ActiveTicketCount : 0;
        
        /// <summary>
        /// すべてのコインをプールに返却
        /// </summary>
        public void ReturnAllCoinsToPool()
        {
            poolManager?.ReturnAllCoinsToPool();
        }
        
        /// <summary>
        /// コイン消費処理をCoinSystemControllerに委譲するラッパー
        /// </summary>
        public System.Collections.IEnumerator ConsumeCoins(int amount)
        {
            var controller = GetComponent<CoinSystemController>();
            if (controller != null)
            {
                yield return controller.ConsumeCoins(amount);
            }
            else
            {
                Debug.LogError("CoinSystemController not found on the same GameObject as CoinDispenser.");
            }
        }
        // 整列状態フラグ
        private bool isSorting = false;
        [Header("排出設定")]
        [SerializeField] private Transform dispenserPoint; // 排出口
        [SerializeField] private Transform potTarget; // ポット(壺への誘導)
        [SerializeField] private float dispenseForce = 5f; // 排出力
        [SerializeField] private Vector3 forceDirection = Vector3.right; // 排出方向
        [SerializeField] private float randomForceRange = 1f; // ランダム誤差
        
        [Header("可変速度設定")]
        [SerializeField] private bool enableVariableSpeed = false; // 可変速度モードの有効/無効
        [SerializeField] private int speedScaleThreshold = 100; // この枚数で最大速度になる
        [SerializeField] [Range(0.01f, 1.0f)] private float minDispenseInterval = 0.05f; // 最高速度時の排出間隔
        [SerializeField] [Range(0.01f, 1.0f)] private float maxDispenseInterval = 0.2f; // 最低速度時の排出間隔
        
        // 依存コンポーネント
        private CoinPoolManager poolManager;
        private CoinAudioManager audioManager;
        private CoinStackManager stackManager;
        private CoinPhysicsManager physicsManager;
        private TicketSystemManager ticketManager;
        
        // 状態フラグ
        private bool isDispensing = false;
        
        // イベント
        public event Action<int> OnDispenseComplete;
        public event Action<int> OnSortComplete;
        
        // WaitForSecondsのキャッシュによりアロケーションを削減
        private WaitForSeconds[] cachedWaits;
        private const int maxCachedWaits = 20;
        
        public bool IsDispensing => isDispensing;
        public bool IsSorting => isSorting;
        public Transform DispenserPoint => dispenserPoint;
        public Transform PotTarget => potTarget;
        
        private void Awake()
        {
            InitializeCachedWaits();
        }
        
        private void Start()
        {
            // 依存コンポーネントを取得
            poolManager = GetComponent<CoinPoolManager>();
            audioManager = GetComponent<CoinAudioManager>();
            stackManager = GetComponent<CoinStackManager>();
            physicsManager = GetComponent<CoinPhysicsManager>();
            ticketManager = GetComponent<TicketSystemManager>();
            
            ValidateComponents();
        }
        
        private void InitializeCachedWaits()
        {
            cachedWaits = new WaitForSeconds[maxCachedWaits];
            for (int i = 0; i < maxCachedWaits; i++)
            {
                float waitTime = 0.01f + (i * 0.05f); // 0.01から0.96秒まで
                cachedWaits[i] = new WaitForSeconds(waitTime);
            }
        }
        
        private WaitForSeconds GetCachedWaitForSeconds(float seconds)
        {
            // 初期化済みチェック
            if (cachedWaits == null || cachedWaits.Length == 0)
            {
                Debug.LogWarning("[CoinDispenserManager] WaitForSeconds cache not initialized! Initializing now...");
                InitializeCachedWaits();
            }
            
            // 0.01-0.96秒の範囲でキャッシュを使用
            int index = Mathf.Clamp(Mathf.RoundToInt((seconds - 0.01f) / 0.05f), 0, maxCachedWaits - 1);
            return cachedWaits[index];
        }
        
        private void ValidateComponents()
        {
            if (dispenserPoint == null)
            {
                Debug.LogWarning("Dispenser Point is not assigned. Using this transform as dispenser point.");
                dispenserPoint = transform;
            }
            
            if (potTarget == null)
            {
                Debug.LogWarning("Pot Target is not assigned. Creating default target.");
                GameObject defaultTarget = new GameObject("DefaultPotTarget");
                defaultTarget.transform.SetParent(transform);
                defaultTarget.transform.localPosition = Vector3.forward * 2f;
                potTarget = defaultTarget.transform;
            }
        }
        
        /// <summary>
        /// コインを指定枚数だけ排出する公開API
        /// </summary>
        /// <param name="amount">排出する枚数(コイン枚数)</param>
        public void DispenseCoins(int amount)
        {
            if (poolManager == null || poolManager.CoinPrefab == null)
            {
                Debug.LogError("[CoinDispenserManager] Cannot dispense: coinPrefab is not assigned!");
                return;
            }
            
            if (isDispensing)
            {
                Debug.LogWarning("Already dispensing coins. Wait for completion.");
                return;
            }
            
            if (amount <= 0)
            {
                Debug.LogWarning("Amount must be greater than 0.");
                return;
            }
            
            StartCoroutine(DispenseCoinsCoroutine(amount));
        }
        
        private IEnumerator DispenseCoinsCoroutine(int amount)
        {
            isDispensing = true;
            
            try
            {
                // 排出口の開始効果音を再生
                audioManager?.PlayDispensingSound();
                
                // 枚数に応じて排出間隔を計算
                float currentDispenseInterval;
                if (enableVariableSpeed && stackManager != null)
                {
                    // CoinDispenserの設定を使用して可変速度を計算
                    float t = Mathf.Clamp01((float)amount / speedScaleThreshold);
                    currentDispenseInterval = Mathf.Lerp(maxDispenseInterval, minDispenseInterval, t);
                    Debug.Log($"Variable speed enabled: {amount} coins, interval: {currentDispenseInterval}s (range: {maxDispenseInterval}s - {minDispenseInterval}s)");
                }
                else
                {
                    // 固定速度で最大間隔を使用
                    currentDispenseInterval = maxDispenseInterval;
                    Debug.Log($"Fixed speed: using {maxDispenseInterval}s interval");
                }
                
                // チケットシステム有効時：高閾値を超える分をチケットに変換、0枚以下にならないようチケットで排出
                int coinsToDispense = amount;
                int ticketsToDispense = 0;
                
                if (ticketManager != null && ticketManager.EnableTicketSystem)
                {
                    int currentStackedCoins = stackManager?.TotalStackedCoins ?? 0;
                    int totalAfterDispense = currentStackedCoins + amount;
                    
                    if (totalAfterDispense > ticketManager.CoinThresholdHigh)
                    {
                        // 60枚以上になる分を変換
                        int excessCoins = totalAfterDispense - ticketManager.CoinThresholdHigh;
                        // 10枚単位切り上げでチケット枚数を計算
                        ticketsToDispense = (excessCoins + ticketManager.CoinsPerTicket - 1) / ticketManager.CoinsPerTicket;
                        int coinsInTickets = ticketsToDispense * ticketManager.CoinsPerTicket;
                        
                        // チケットで排出する分を差し引く
                        coinsToDispense = amount - coinsInTickets;
                        
                        Debug.Log($"排出計画: 現在{currentStackedCoins}枚 + 排出予定{amount}枚 = {totalAfterDispense}枚");
                        Debug.Log($"チケット{ticketsToDispense}枚({coinsInTickets}コイン分) + コイン{coinsToDispense}枚を排出");
                    }
                }
                
                // チケットとコインを並列で排出
                List<Coroutine> dispensingCoroutines = new List<Coroutine>();
                
                // チケット排出を開始（並列処理）
                if (ticketsToDispense > 0)
                {
                    Debug.Log($"コイン枚数高閾値超過：チケット{ticketsToDispense}枚とコイン{coinsToDispense}枚を並列排出");
                    var ticketCoroutine = StartCoroutine(ticketManager.CreateAndDispenseTickets(ticketsToDispense, () => audioManager?.PlayTicketSound()));
                    dispensingCoroutines.Add(ticketCoroutine);
                }
                
                // コイン排出を開始（並列処理）
                if (coinsToDispense > 0)
                {
                    var coinCoroutine = StartCoroutine(DispenseCoinsOnly(coinsToDispense, currentDispenseInterval));
                    dispensingCoroutines.Add(coinCoroutine);
                }
                
                // チケット排出完了を待機
                if (ticketsToDispense > 0 && dispensingCoroutines.Count > 0)
                {
                    yield return dispensingCoroutines[0]; // チケットコルーチン待機
                }
                
                // コイン排出完了を待機
                if (coinsToDispense > 0)
                {
                    int lastIndex = dispensingCoroutines.Count - 1;
                    if (lastIndex >= 0)
                    {
                        yield return dispensingCoroutines[lastIndex]; // コインコルーチン待機
                    }
                }
                
                Debug.Log("コイン排出完了、物理モーションの完了を待機中...");
                
                // コインの物理的な落下・移動が完全に完了するまで待機
                yield return new WaitForSeconds(1.0f);
                
                Debug.Log("コインの物理モーション完了、積み上げ処理へ移行");
                
                // OnDispenseCompleteイベントを積み上げ開始直前に発火（isDispensingをfalseにする直前）
                OnDispenseComplete?.Invoke(amount);
                
                // パラメータ計算: 最大高さ判定時に作成される力の変数
                if (amount > 50)
                {
                    System.GC.Collect();
                    yield return null;
                }
            }
            finally
            {
                isDispensing = false;
                Debug.Log("DispenseCoinsCoroutine completed, isDispensing set to false");
            }
        }
        
        /// <summary>
        /// お釣りとしてコインを排出
        /// </summary>
        public IEnumerator DispenseChangeCoins(int amount, bool autoSort = true)
        {
            Debug.Log($"Dispensing {amount} change coins");
            
            for (int i = 0; i < amount; i++)
            {
                GameObject coin = poolManager?.GetCoinFromPool();
                if (coin == null) break;
                poolManager.ActiveCoins.Add(coin);
                
                coin.transform.position = dispenserPoint.position + Vector3.up * 0.1f * i;
                coin.transform.rotation = dispenserPoint.rotation;
                coin.SetActive(true);
                
                // 物理計算を委譲
                physicsManager?.ApplyDispensePhysics(coin, dispenserPoint, potTarget);
                
                audioManager?.PlayRandomCoinSound();
                yield return GetCachedWaitForSeconds(0.05f);
            }
            
            if (autoSort)
            {
                yield return new WaitForSeconds(0.1f);
                // ソート処理は統合マネージャーに委譲（循環参照回避のためイベント使用）
                OnDispenseComplete?.Invoke(amount);
            }
        }
        
        /// <summary>
        /// 設定更新メソッド
        /// </summary>
        public void UpdateSettings(Transform newDispenserPoint, Transform newPotTarget, float newDispenseForce, float newRandomForceRange)
        {
            dispenserPoint = newDispenserPoint;
            potTarget = newPotTarget;
            dispenseForce = newDispenseForce;
            randomForceRange = newRandomForceRange;
            
            // 物理マネージャーにも設定を反映
            physicsManager?.UpdatePhysicsSettings(newDispenseForce, newRandomForceRange);
        }
        
        /// <summary>
        /// 現在の設定を取得
        /// </summary>
        public (float dispenseForce, float randomForceRange, float minInterval, float maxInterval) GetSettings()
        {
            return (dispenseForce, randomForceRange, minDispenseInterval, maxDispenseInterval);
        }        
        /// <summary>
        /// コイン専用排出処理（並列処理用）
        /// </summary>
        private IEnumerator DispenseCoinsOnly(int coinsToDispense, float currentDispenseInterval)
        {
            Debug.Log($"Starting parallel coin dispensing: {coinsToDispense} coins");
            
            for (int i = 0; i < coinsToDispense; i++)
            {
                GameObject coin = poolManager?.GetCoinFromPool();
                if (coin == null) break;
                
                // 排出音（開始位置での設定）
                coin.transform.position = dispenserPoint.position;
                coin.transform.rotation = dispenserPoint.rotation;
                coin.SetActive(true);
                
                // 物理計算をCoinPhysicsManagerに委譲
                physicsManager?.ApplyDispensePhysics(coin, dispenserPoint, potTarget);
                
                // ActiveCoinsに追加（プールマネージャーが管理）
                poolManager.ActiveCoins.Add(coin);
                
                // コイン排出音再生（何度も音が重ならないように音量を弱める再生）
                audioManager?.PlayDispensingSound();
                audioManager?.PlayRandomCoinSound();
                
                // パラメータ計算: 20枚以上では速い速度を計算し段階的に遅くする
                if (i > 0 && i % 20 == 0)
                {
                    yield return null;
                }
                
                yield return GetCachedWaitForSeconds(currentDispenseInterval);
            }
            
            Debug.Log($"Completed parallel coin dispensing: {coinsToDispense} coins");
        }
        

    }
}