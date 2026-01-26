using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// コインシステム統合コントローラー
    /// 各種マネージャーを統括し、システム全体の動作を制御
    /// </summary>
    public class CoinSystemController : MonoBehaviour
    {
        [Header("各種コンポーネント")]
        private CoinPoolManager poolManager;
        private CoinAudioManager audioManager;
        private CoinStackManager stackManager;
        private CoinPhysicsManager physicsManager;
        private CoinDispenser dispenserManager;
        private TicketSystemManager ticketManager;
        private PaymentManager paymentManager;
        private CoinTicketConversionManager conversionManager;
        private TiledPixelDisplay pixelDisplay;
        
        [Header("スタック設定")]
        [SerializeField] private Transform stackStartPoint; // スタック開始位置
        
        [Header("チケットシステム関連")]
        [SerializeField] private Transform ticketMachinePoint;
        [SerializeField] private Vector3 ticketDirection = Vector3.down;
        
        [Header("ディスプレイシステム")]
        [SerializeField] private bool enableDisplay = false;
        
        // 状態フラグ
        private bool isSorting = false;
        private bool isConsuming = false;
        private int lastDisplayedValue = -1; // 最後に表示した値（変更検出用）
        private float lastRandomUpdateTime = 0f; // 最後のランダム更新時刻
        private const float RANDOM_UPDATE_INTERVAL = 0.02f; // ランダム表示の更新間隔（秒）
        
        // イベント
        public event Action<int> OnDispenseComplete;
        public event Action<int> OnSortComplete;
        
        public bool IsDispensing => dispenserManager?.IsDispensing ?? false;
        public bool IsSorting => isSorting;
        public bool IsConsuming => isConsuming;
        
        private void Awake()
        {
            Debug.Log("[CoinSystemController] Initializing...");
        }
        
        private void Start()
        {
            // 各種コンポーネントを自動取得（Start()で行うことでCoinSystemManagerの初期化を待つ）
            poolManager = GetComponent<CoinPoolManager>();
            audioManager = GetComponent<CoinAudioManager>();
            stackManager = GetComponent<CoinStackManager>();
            physicsManager = GetComponent<CoinPhysicsManager>();
            dispenserManager = GetComponent<CoinDispenser>();
            ticketManager = GetComponent<TicketSystemManager>();
            paymentManager = GetComponent<PaymentManager>();
            conversionManager = GetComponent<CoinTicketConversionManager>();
            // pixelDisplayはCoinSystemManagerから設定されるため、ここでは取得しない
            
            ValidateComponents();
            InitializeEventSubscriptions();
            InitializeStackManager();
            InitializeDisplay();
            
            Debug.Log("[CoinSystemController] Initialization complete");
        }
        
        private void ValidateComponents()
        {
            if (poolManager == null)
                Debug.LogError("CoinPoolManager not found! Make sure it's attached to the same GameObject.");
            if (audioManager == null)
                Debug.LogError("CoinAudioManager not found! Make sure it's attached to the same GameObject.");
            if (stackManager == null)
                Debug.LogError("CoinStackManager not found! Make sure it's attached to the same GameObject.");
            if (physicsManager == null)
                Debug.LogError("CoinPhysicsManager not found! Make sure it's attached to the same GameObject.");
            if (dispenserManager == null)
                Debug.LogError("CoinDispenser not found! Make sure it's attached to the same GameObject.");
            if (ticketManager == null)
                Debug.LogError("TicketSystemManager not found! Make sure it's attached to the same GameObject.");
            if (paymentManager == null)
                Debug.LogError("PaymentManager not found! Make sure it's attached to the same GameObject.");
            if (conversionManager == null)
                Debug.LogError("CoinTicketConversionManager not found! Make sure it's attached to the same GameObject.");
            if (enableDisplay && pixelDisplay == null)
                Debug.LogWarning("TiledPixelDisplay not found but display is enabled. Add the component or disable display.");
        }
        
        private void InitializeEventSubscriptions()
        {
            // DispenserManagerのイベントを購読
            if (dispenserManager != null)
            {
                dispenserManager.OnDispenseComplete += OnDispenserComplete;
            }
        }
        
        private void InitializeStackManager()
        {
            // StackManagerの初期化
            if (stackManager != null && stackStartPoint != null)
            {
                // stackStartPointをStackManagerに設定
                var field = typeof(CoinStackManager).GetField("stackStartPoint", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                    field.SetValue(stackManager, stackStartPoint);
                    
                stackManager.InitializeStack();
            }
            
            // coinPrefab検証
            if (poolManager != null && poolManager.CoinPrefab == null)
            {
                Debug.Log("[CoinSystemController] coinPrefab not yet assigned. Waiting for CoinSystemManager or manual assignment.");
            }
            else if (poolManager != null)
            {
                Debug.Log($"[CoinSystemController] coinPrefab assigned: {poolManager.CoinPrefab.name}");
            }
        }
        
        /// <summary>
        /// ディスプレイシステムの初期化
        /// </summary>
        private void InitializeDisplay()
        {
            if (enableDisplay && pixelDisplay != null)
            {
                if (!pixelDisplay.IsInitialized)
                {
                    pixelDisplay.Initialize();
                }
                
                // イベント購読
                pixelDisplay.OnDisplayUpdate += OnDisplayUpdated;
                pixelDisplay.OnDisplayClear += OnDisplayCleared;
                
                // 初期表示
                UpdateDisplay(0);
                Debug.Log("[CoinSystemController] Display initialized");
            }
        }
        
        void Update()
        {
            // ディスプレイ自動更新
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                if (IsDispensing || isSorting)
                {
                    // 排出中または積み上げ中はランダム数字で演出（時間ベース）
                    if (Time.time - lastRandomUpdateTime >= RANDOM_UPDATE_INTERVAL)
                    {
                        int randomValue = UnityEngine.Random.Range(0, 999);
                        pixelDisplay.DisplayNumber(randomValue);
                        lastRandomUpdateTime = Time.time;
                    }
                }
                else if (!isConsuming)
                {
                    // 通常時はポット内のコイン総数を表示（値が変更されたときのみ）
                    int currentValue = GetTotalCoinCount();
                    
                    if (currentValue != lastDisplayedValue)
                    {
                        pixelDisplay.DisplayNumber(currentValue);
                        lastDisplayedValue = currentValue;
                    }
                }
            }
        }
        
        /// <summary>
        /// コインを指定枚数だけ排出する公開API - DispenserManagerに委譲
        /// </summary>
        /// <param name="amount">排出する枚数(コイン枚数)</param>
        public void DispenseCoins(int amount)
        {
            dispenserManager?.DispenseCoins(amount);
        }
        
        /// <summary>
        /// 排出完了時のコールバック
        /// </summary>
        private void OnDispenserComplete(int amount)
        {
            OnDispenseComplete?.Invoke(amount);
            
            // ディスプレイ更新
            UpdateDisplayWithCurrentCoins();
            
            // 自動ソート開始
            StartCoroutine(DelayedSort());
        }
        
        private IEnumerator DelayedSort()
        {
            // 即座にソート開始（遅延なし）
            yield return StartCoroutine(SortCoinsCoroutine());
        }
        
        private IEnumerator SortCoinsCoroutine()
        {
            if (isSorting) 
            {
                Debug.LogWarning("SortCoinsCoroutine already running, skipping...");
                yield break;
            }
            
            yield return StartCoroutine(SortCoinsInternal());
        }
        
        /// <summary>
        /// 公開用のコイン整列メソッド
        /// </summary>
        public IEnumerator SortCoinsInternal()
        {
            if (isSorting) 
            {
            Debug.LogWarning("SortCoinsInternal already running, skipping...");
            yield break;
        }
        
        isSorting = true;
        Debug.Log("=== Starting SortCoinsInternal ===");
        
        // コンポーネントの有効性をチェック
        if (poolManager == null)
        {
            Debug.LogError("PoolManager is null! Cannot proceed with sorting.");
            isSorting = false;
            yield break;
        }
        
        if (stackManager == null)
        {
            Debug.LogError("StackManager is null! Cannot proceed with sorting.");
            isSorting = false;
            yield break;
        }
        
        Debug.Log($"Active coins count: {poolManager.ActiveCoins.Count}");
        
        // コインの姿勢が完全に静止したかをチェック
        Debug.Log("Waiting for coins to settle (0.3 seconds)...");
        yield return new WaitForSeconds(0.3f);
        
        // スタック判定（ソートシステム）
        Debug.Log("Starting AnimateCoinsToStacks...");
        yield return StartCoroutine(AnimateCoinsToStacks());
        Debug.Log("AnimateCoinsToStacks completed successfully");
        
        OnSortComplete?.Invoke(poolManager.ActiveCoins.Count);
        Debug.Log("=== SortCoinsInternal completed successfully ===");
        
        // チケット有効時：コイン総数がticketThresholdHighを超えている、または低すぎる場合に自動変換
        if (ticketManager != null && ticketManager.EnableTicketSystem)
        {
            Debug.Log("Checking ticket conversion...");
            CheckAndConvertFromTickets();
        }
        
        isSorting = false;
        Debug.Log("=== SortCoinsInternal finished, isSorting set to false ===");
    }
        
    private IEnumerator AnimateCoinsToStacks()
    {
        Debug.Log("=== Starting AnimateCoinsToStacks ===");
        
        if (poolManager == null)
        {
            Debug.LogError("PoolManager is null in AnimateCoinsToStacks!");
            yield break;
        }
        
        if (poolManager.ActiveCoins.Count == 0)
        {
            Debug.LogWarning("No active coins to animate");
            yield break;
        }
        
        if (stackManager == null)
        {
            Debug.LogError("StackManager is null in AnimateCoinsToStacks!");
            yield break;
        }

        // スタック初期化を確実に実行
        if (stackManager.StackCount == 0)
        {
            Debug.Log("No stacks found, initializing stack system");
            stackManager.InitializeStack();
            Debug.Log("Stack initialization completed");
        }

        // スタック配置前の既存スタック状態をログ出力
        Debug.Log($"[STACK DEBUG] Existing stacks: {stackManager.CoinStacks.Count}");
        for (int i = 0; i < stackManager.CoinStacks.Count; i++)
        {
            var stack = stackManager.CoinStacks[i];
            Debug.Log($"[STACK DEBUG] Stack {i}: {stack.CoinCount}/{stack.maxCoins} coins at {stack.basePosition}");
        }

        List<GameObject> coinsToSort = new List<GameObject>(poolManager.ActiveCoins);
        Debug.Log($"Starting to sort {coinsToSort.Count} coins into stacks");

        // 全てのコインを順次スタックにアニメーション
        Debug.Log("Calling stackManager.AnimateNewCoinsToStacks...");
        yield return StartCoroutine(stackManager.AnimateNewCoinsToStacks(
            coinsToSort,
            () => stackManager.CreateNewStack(),
            (coin, targetPos) => stackManager.AnimateCoinToPosition(
                coin, targetPos, stackManager.SortAnimationDuration,
                (stackPos) => audioManager?.PlayStackSound(stackPos, stackManager.CoinsPerStack),
                (coinObj) => stackManager.GetCoinPositionInItsStack(coinObj)
            ),
            stackManager.StackDirection,
            stackManager.StackSpacing
        ));
        Debug.Log("stackManager.AnimateNewCoinsToStacks completed");

        Debug.Log($"Stack animation completed. Final stacks: {stackManager.StackCount}");
        
        // 積み上げ完了したコインをActiveCoinsから削除
        if (poolManager != null)
        {
            poolManager.ActiveCoins.Clear();
            Debug.Log($"AnimateCoinsToStacks completed. Cleared ActiveCoins");
        }
        
        Debug.Log("=== AnimateCoinsToStacks finished ===");
    }
        
    /// <summary>
    /// 現在スタック中のコインのスタック状態のチェック機能（コルーチン実行：コイン静止判定を実施）
    /// </summary>
    private void UpdateCurrentStackIndex()
    {
        stackManager?.UpdateCurrentIndex();
    }
        
    /// <summary>
        /// コインが高閾値を超えている場合、チケットへの変換をチェック - ConversionManagerに依存
        /// </summary>
        private IEnumerator CheckCoinsBeforeSorting()
        {
            if (conversionManager == null) yield break;
            
            yield return conversionManager.CheckAndConvertToTickets(
                ConvertNewCoinsToTicket,
                CollectCoinsForTicket
            );
        }
        
        /// <summary>
        /// 新規排出されたコインをチケットに変換 - ConversionManagerに依存
        /// </summary>
        private IEnumerator ConvertNewCoinsToTicket(int coinsToConvert)
        {
            if (conversionManager == null) yield break;
            
            yield return conversionManager.ConvertNewCoinsToTicket(
                coinsToConvert,
                dispenserManager.DispenserPoint,
                AnimateCoinToDispenser,
                CreateTicketsCoroutine
            );
        }
        
        /// <summary>
        /// チケット変換のためにコインを収集 - ConversionManagerに依存
        /// </summary>
        private IEnumerator CollectCoinsForTicket(int coinsToCollect)
        {
            if (conversionManager == null) yield break;
            
            yield return conversionManager.CollectCoinsForTicket(
                coinsToCollect,
                dispenserManager.DispenserPoint,
                AnimateCoinToDispenser,
                CreateTicketsCoroutine,
                UpdateCurrentStackIndex
            );
        }
        
        /// <summary>
        /// コインを排出機位置へアニメーション - ConversionManagerに依存
        /// </summary>
        private IEnumerator AnimateCoinToDispenser(GameObject coin, Vector3 targetPosition, float delay = 0f)
        {
            yield return conversionManager.AnimateCoinToDispenser(coin, targetPosition, delay, PlayRandomCoinSound);
        }
        
        /// <summary>
        /// スタックからコインを削除 - StackManagerに依存
        /// </summary>
        private void RemoveCoinsFromStacks(int coinAmount)
        {
            stackManager?.RemoveCoinsFromStacks(coinAmount, coin => poolManager?.ReturnCoinToPool(coin));
        }
        
        /// <summary>
        /// チケットを生成・排出 - TicketManagerに依存
        /// </summary>
        private IEnumerator CreateTicketsCoroutine(int ticketAmount)
        {
            if (ticketManager == null)
            {
                Debug.LogError("TicketManager not available!");
                yield break;
            }
            
            yield return ticketManager.CreateAndDispenseTickets(ticketAmount, () => audioManager?.PlayTicketSound());
        }
        
        /// <summary>
        /// チケットを整列 - TicketSystemManagerに委譲
        /// </summary>
        private IEnumerator SortTicketsCoroutine()
        {
            if (ticketManager == null) yield break;
            
            yield return ticketManager.SortTickets(ticketMachinePoint, ticketManager.TicketSpacing, ticketManager.TicketDirection,
                (ticket, pos) => ticketManager.AnimateTicketToPositionKinematic(ticket, pos));
        }
        
        /// <summary>
        /// 決済処理：指定枚数のコインを消費（PaymentManagerに依存）
        /// </summary>
        public IEnumerator ConsumeCoins(int amount)
        {
            if (paymentManager == null)
            {
                Debug.LogError("PaymentManager not available!");
                yield break;
            }
            
            // 支払い開始 - ディスプレイでランダム表示開始
            isConsuming = true;
            Debug.Log("[CoinSystemController] Payment started - Random display animation activated");
            
            // PaymentManagerに決済処理を委譲
            yield return paymentManager.ProcessPayment(
                amount,
                dispenserManager.DispenserPoint,
                (coinAmount) => ConsumeCoinsFromStacks(coinAmount, dispenserManager.DispenserPoint),
                ConsumeTickets,
                dispenserManager.DispenseChangeCoins,
                ConvertTicketsToCoinsCoroutine,
                () => SortCoinsCoroutine()
            );
            
            // 支払い完了 - 正しい枚数を表示
            isConsuming = false;
            UpdateDisplayWithCurrentCoins();
            Debug.Log("[CoinSystemController] Payment completed - Display updated with actual coin count");
        }
        
        /// <summary>
        /// スタック内のコインの消費処理
        /// </summary>
        private IEnumerator ConsumeCoinsFromStacks(int coinAmount, Transform targetPosition)
        {
            // StackManagerに委譲
            RemoveCoinsFromStacks(coinAmount);
            yield break;
        }
        
        /// <summary>
        /// チケット消費処理 - TicketSystemManagerに委譲
        /// </summary>
        private IEnumerator ConsumeTickets(int amount)
        {
            if (ticketManager == null)
            {
                Debug.LogError("TicketManager not available!");
                yield break;
            }
            
            yield return ticketManager.ConsumeTickets(amount, dispenserManager.DispenserPoint, 
                ticket => AnimateTicketToDispenser(ticket, dispenserManager.DispenserPoint.position, 0f));
        }
        
        /// <summary>
        /// チケットを排出口へアニメーション - TicketSystemManagerに委譲
        /// </summary>
        private IEnumerator AnimateTicketToDispenser(GameObject ticket, Vector3 targetPosition, float delay)
        {
            yield return ticketManager.AnimateTicketToDispenser(ticket, targetPosition, delay);
        }
        
        /// <summary>
        /// コインが0枚の場合、チケットからコインへの変換をチェック
        /// </summary>
        private void CheckAndConvertFromTickets()
        {
            int currentCoins = GetTotalCoinCount();
            
            // コインが0枚でチケットがある場合、1枚変換
            if (currentCoins == 0 && ticketManager.ActiveTickets.Count > 0)
            {
                int ticketsToConvert = Math.Min(1, ticketManager.ActiveTickets.Count);
                StartCoroutine(ConvertTicketsToCoinsCoroutine(ticketsToConvert));
            }
        }
        
        /// <summary>
        /// チケットをコインに変換 - ConversionManagerに依存
        /// </summary>
        private IEnumerator ConvertTicketsToCoinsCoroutine(int ticketAmount, bool autoSort = true)
        {
            if (conversionManager == null) yield break;
            
            yield return conversionManager.ConvertTicketsToCoins(
                ticketAmount,
                autoSort,
                AnimateTicketToDispenser,
                dispenserManager.DispenseChangeCoins,
                dispenserManager.DispenserPoint
            );
        }
        
        /// <summary>
        /// スタックにコインを追加
        /// </summary>
        private IEnumerator AddCoinsToStacksCoroutine(int coinAmount)
        {
            List<GameObject> newCoins = new List<GameObject>();
            
            // 指定枚数のコインを生成
            for (int i = 0; i < coinAmount; i++)
            {
                GameObject coin = GetCoinFromPool();
                if (coin == null) break;
                
                // 排出口の上に配置（高さをズラして重ならないように）
                coin.transform.position = dispenserManager.DispenserPoint.position + Vector3.up * i * 0.1f;
                coin.SetActive(true);
                
                newCoins.Add(coin);
                poolManager.ActiveCoins.Add(coin);
            }
            
            // 新しいコインをスタックに配置
            yield return StartCoroutine(AnimateNewCoinsToStacks(newCoins));
        }
        
        /// <summary>
        /// 新しいコインをスタックに配置 - StackManagerに依存
        /// </summary>
        private IEnumerator AnimateNewCoinsToStacks(List<GameObject> coinsToAdd)
        {
            if (stackManager == null)
            {
                Debug.LogError("StackManager not available!");
                yield break;
            }
            
            yield return stackManager.AnimateNewCoinsToStacks(coinsToAdd,
                () => CreateNewStack(),
                (coin, pos) => AnimateCoinToPosition(coin, pos),
                stackManager.StackDirection, stackManager.StackSpacing);
        }
        
        /// <summary>
        /// 1つのスタック分のコインを順次配置 - StackManagerに依存
        /// </summary>
        private IEnumerator AnimateStackSequentially(List<GameObject> coinsForStack, int stackIndex)
        {
            if (stackManager == null)
            {
                Debug.LogError("StackManager not available!");
                yield break;
            }
            
            yield return stackManager.AnimateStackSequentially(coinsForStack, stackIndex,
                stackManager.StackDirection, stackManager.StackSpacing,
                (coin, pos) => AnimateCoinToPosition(coin, pos),
                AddCoinToStackAtPosition,
                poolManager.ActiveCoins.Count);
        }
        
        private void CreateNewStack()
        {
            if (stackManager == null)
            {
                Debug.LogError("StackManager not available!");
                return;
            }
            
            int stackIndex = stackManager.CoinStacks.Count;
            Vector3 basePosition = stackStartPoint.position + 
                stackManager.StackGroupDirection.normalized * stackManager.StackGroupSpacing * stackIndex;
            
            CoinStackManager.StackState newStack = new CoinStackManager.StackState
            {
                basePosition = basePosition,
                stackIndex = stackIndex,
                maxCoins = stackManager.CoinsPerStack
            };
            
            stackManager.CoinStacks.Add(newStack);
            Debug.Log($"Created new stack {stackIndex} at position {basePosition} with max coins: {stackManager.CoinsPerStack}");
        }
        
        /// <summary>
        /// コインを指定位置へアニメーション - StackManagerに依存
        /// </summary>
        private IEnumerator AnimateCoinToPosition(GameObject coin, Vector3 targetPosition)
        {
            if (stackManager == null)
            {
                Debug.LogError("StackManager not available!");
                yield break;
            }
            
            yield return stackManager.AnimateCoinToPosition(coin, targetPosition, stackManager.SortAnimationDuration,
                (coinIndex) => audioManager?.PlayStackSound(coinIndex, 10), GetCoinPositionInItsStack);
        }
        
        /// <summary>
        /// 遅延付きでコインを指定位置へアニメーション - StackManagerに依存
        /// </summary>
        private IEnumerator AnimateCoinToPositionWithDelay(GameObject coin, Vector3 targetPosition, float delay)
        {
            if (stackManager == null)
            {
                Debug.LogError("StackManager not available!");
                yield break;
            }
            
            yield return stackManager.AnimateCoinToPositionWithDelay(coin, targetPosition, delay, stackManager.SortAnimationDuration,
                null, (coinIndex) => audioManager?.PlayStackSound(coinIndex, 10), GetCoinPositionInItsStack);
        }
        
        /// <summary>
        /// コインがスタック内の何番目かを取得 - StackManagerに委譲
        /// </summary>
        private int GetCoinPositionInItsStack(GameObject coin)
        {
            return stackManager?.GetCoinPositionInItsStack(coin) ?? 0;
        }
        
        /// <summary>
        /// スタックの内容をデバッグ文字列として取得 - StackManagerに委譲
        /// </summary>
        private string GetStackContentsDebug(CoinStackManager.StackState stack)
        {
            return stackManager?.GetStackContentsDebug(stack) ?? "StackManager not available";
        }
        
        /// <summary>
        /// 指定位置にコインをスタックに追加 - StackManagerに委譲
        /// </summary>
        private void AddCoinToStackAtPosition(GameObject coin, CoinStackManager.StackState stack, int position)
        {
            stackManager?.AddCoinToStackAtPosition(coin, stack, position);
        }
        
        // ヘルパーメソッド
        private int GetTotalCoinCount()
        {
            int coinCount = stackManager?.TotalStackedCoins ?? 0;
            int ticketCount = ticketManager?.ActiveTicketCount ?? 0;
            int coinsPerTicket = ticketManager?.CoinsPerTicket ?? 10;
            
            // チケットを1枚10コイン换算で追加
            return coinCount + (ticketCount * coinsPerTicket);
        }
        
        private void PlayTicketSound()
        {
            audioManager?.PlayTicketSound();
        }
        
        private void PlayRandomCoinSound()
        {
            audioManager?.PlayRandomCoinSound();
        }
        
        private GameObject GetCoinFromPool()
        {
            return poolManager?.GetCoinFromPool();
        }
        
        private void ReturnCoinToPool(GameObject coin)
        {
            poolManager?.ReturnCoinToPool(coin);
        }
        
        #region Display Control Methods
        /// <summary>
        /// ディスプレイに数値を表示
        /// </summary>
        /// <param name="value">表示する値</param>
        public void UpdateDisplay(int value)
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                pixelDisplay.DisplayNumber(value);
            }
        }
        
        /// <summary>
        /// ディスプレイにテキストを表示
        /// </summary>
        /// <param name="text">表示するテキスト</param>
        public void UpdateDisplayText(string text)
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                pixelDisplay.DisplayText(text);
            }
        }
        
        /// <summary>
        /// ディスプレイをクリア
        /// </summary>
        public void ClearDisplay()
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                pixelDisplay.ClearDisplay();
            }
        }
        
        /// <summary>
        /// 現在のコイン総数をディスプレイに表示
        /// </summary>
        public void DisplayTotalCoins()
        {
            if (enableDisplay)
            {
                int totalCoins = GetTotalCoinCount();
                UpdateDisplay(totalCoins);
            }
        }
        
        /// <summary>
        /// 現在のコイン枚数でディスプレイを更新（内部用）
        /// </summary>
        private void UpdateDisplayWithCurrentCoins()
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                int totalCoins = GetTotalCoinCount();
                pixelDisplay.DisplayNumber(totalCoins);
            }
        }
        
        /// <summary>
        /// ディスプレイの背景色を変更
        /// </summary>
        /// <param name="newColor">新しい背景色</param>
        public void SetDisplayColor(Color newColor)
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                pixelDisplay.SetDisplayColor(newColor);
            }
        }
        
        /// <summary>
        /// ディスプレイの文字色を変更
        /// </summary>
        /// <param name="newColor">新しい文字色</param>
        public void SetDisplayTextColor(Color newColor)
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                pixelDisplay.SetTextColor(newColor);
            }
        }
        
        /// <summary>
        /// ディスプレイの発光強度を変更
        /// </summary>
        /// <param name="intensity">発光強度</param>
        public void SetDisplayEmissiveIntensity(float intensity)
        {
            if (enableDisplay && pixelDisplay != null && pixelDisplay.IsInitialized)
            {
                pixelDisplay.SetEmissiveIntensity(intensity);
            }
        }
        
        /// <summary>
        /// ディスプレイ更新時のコールバック
        /// </summary>
        private void OnDisplayUpdated(string text)
        {
            Debug.Log($"[CoinSystemController] Display updated: {text}");
        }
        
        /// <summary>
        /// ディスプレイクリア時のコールバック
        /// </summary>
        private void OnDisplayCleared()
        {
            Debug.Log("[CoinSystemController] Display cleared");
        }
        #endregion
        
        /// <summary>
        /// コイン破棄時のクリーンアップ処理
        /// </summary>
        private void OnDestroy()
        {
            // イベント購読解除
            if (dispenserManager != null)
            {
                dispenserManager.OnDispenseComplete -= OnDispenserComplete;
            }
            
            if (pixelDisplay != null)
            {
                pixelDisplay.OnDisplayUpdate -= OnDisplayUpdated;
                pixelDisplay.OnDisplayClear -= OnDisplayCleared;
            }
            
            StopAllCoroutines();
            
            OnDispenseComplete = null;
            OnSortComplete = null;
            
            ReturnAllCoinsToPool();
        }
        
        /// <summary>
        /// 指定範囲のコインメッセージデータに送信
        /// </summary>
        public void ReturnAllCoinsToPool()
        {
            Debug.Log("=== COIN CLEANUP INITIATED ===");
            
            // 実行中のコルーチンをすべて停止
            StopAllCoroutines();
            
            // フラグをリセット
            isSorting = false;
            isConsuming = false;
            
            // プールマネージャーに全コインを返却
            poolManager?.ReturnAllCoinsToPool();
            stackManager?.ClearAllStacks();
            ticketManager?.ClearAllTickets();
            
            Debug.Log("=== COIN CLEANUP COMPLETE ===");
        }
    }
}