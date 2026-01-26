using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// コインの積み上げ管理
    /// </summary>
    public class CoinStackManager : MonoBehaviour
    {
        [Header("整列設定")]
        [SerializeField] private Transform stackStartPoint;
        [SerializeField] private Vector3 stackDirection = Vector3.up;
        [SerializeField] private Vector3 stackGroupDirection = Vector3.right;
        [SerializeField] private float stackSpacing = 0.02f;
        [SerializeField] private float stackGroupSpacing = 0.3f;
        [SerializeField] private int coinsPerStack = 10;
        [SerializeField] private float sortAnimationDuration = 0.1f;
        [SerializeField] private float stackGroupDelay = 0.5f;
        [SerializeField] private bool fastStackingMode = false;
        
        [Header("可変速度設定")]
        [SerializeField] private bool enableVariableSpeed = true; // 60枚等の大量コイン時に高速化
        [SerializeField] private int speedScaleThreshold = 60; // 60枚で最高速度
        [SerializeField] [Range(0.01f, 0.5f)] private float minStackingDelay = 0.01f; // 最高速度時の間隔
        [SerializeField] [Range(0.01f, 0.5f)] private float maxStackingDelay = 0.1f; // 最低速度時の間隔
        
        [System.Serializable]
        public class StackState
        {
            public List<GameObject> coins = new List<GameObject>();
            public Vector3 basePosition;
            public int stackIndex;
            public int maxCoins = 10;
            
            public int CoinCount 
            { 
                get 
                {
                    int count = 0;
                    foreach (var coin in coins)
                    {
                        if (coin != null) count++;
                    }
                    return count;
                }
            }
            
            public bool IsFull => CoinCount >= maxCoins;
            public bool IsEmpty => CoinCount == 0;
        }
        
        private List<StackState> coinStacks = new List<StackState>();
        private int currentStackIndex = 0;
        
        public List<StackState> CoinStacks => coinStacks;
        public int CurrentStackIndex 
        { 
            get => currentStackIndex; 
            set => currentStackIndex = value; 
        }
        public int StackCount => coinStacks.Count;
        public int CurrentStackCoinCount => currentStackIndex < coinStacks.Count ? coinStacks[currentStackIndex].CoinCount : 0;
        public int TotalStackedCoins => GetTotalCoinCount();
        public Vector3 StackDirection => stackDirection;
        public float StackSpacing => stackSpacing;
        public float SortAnimationDuration => sortAnimationDuration;
        public Vector3 StackGroupDirection => stackGroupDirection;
        public float StackGroupSpacing => stackGroupSpacing;
        public int CoinsPerStack => coinsPerStack;
        public float StackGroupDelay => stackGroupDelay;
        
        public void InitializeStack()
        {
            Debug.Log("InitializeStack: Clearing all stacks and creating new one");
            coinStacks.Clear();
            currentStackIndex = 0;
            
            // stackStartPoint が設定されているかチェック
            if (stackStartPoint == null)
            {
                Debug.LogError("StackStartPoint is not assigned! Cannot initialize stacks.");
                return;
            }
            
            CreateNewStack();
            Debug.Log($"InitializeStack complete: Created stack at {coinStacks[0].basePosition}");
        }
        
        public void CreateNewStack()
        {
            if (stackStartPoint == null)
            {
                Debug.LogError("Cannot create stack: stackStartPoint is null!");
                return;
            }
            
            StackState newStack = new StackState
            {
                stackIndex = coinStacks.Count,
                basePosition = stackStartPoint.position + stackGroupDirection * (coinStacks.Count * stackGroupSpacing),
                maxCoins = coinsPerStack
            };
            coinStacks.Add(newStack);
            Debug.Log($"Created new stack #{newStack.stackIndex} at {newStack.basePosition} (total stacks: {coinStacks.Count})");
        }
        
        public void CreateStack() => CreateNewStack();
        
        public void UpdateCurrentIndex()
        {
            for (int i = 0; i < coinStacks.Count; i++)
            {
                if (!coinStacks[i].IsFull)
                {
                    currentStackIndex = i;
                    Debug.Log($"Updated currentStackIndex to {currentStackIndex} (stack has {coinStacks[i].CoinCount}/{coinStacks[i].maxCoins} coins)");
                    return;
                }
            }
            
            currentStackIndex = coinStacks.Count;
            Debug.Log($"All existing stacks are full. Set currentStackIndex to {currentStackIndex} for next new stack");
        }
        
        public void AddCoinToStack(GameObject coin, int stackIndex = -1)
        {
            if (stackIndex < 0)
                stackIndex = currentStackIndex;
            
            // スタックが存在しない場合は作成
            while (stackIndex >= coinStacks.Count)
            {
                CreateNewStack();
            }
            
            StackState stack = coinStacks[stackIndex];
            
            // 現在のスタックが満杯の場合、新しいスタックを作成
            if (stack.IsFull)
            {
                if (stackIndex == currentStackIndex)
                {
                    CreateNewStack();
                    currentStackIndex = coinStacks.Count - 1;
                    stack = coinStacks[currentStackIndex];
                }
                else
                {
                    Debug.LogWarning($"Stack {stackIndex} is full, cannot add coin");
                    return;
                }
            }
            
            stack.coins.Add(coin);
            Debug.Log($"Added coin to stack {stackIndex}, now has {stack.CoinCount}/{stack.maxCoins} coins");
        }
        
        public void RemoveCoinFromStack(GameObject coin)
        {
            for (int i = coinStacks.Count - 1; i >= 0; i--)
            {
                StackState stack = coinStacks[i];
                if (stack.coins.Contains(coin))
                {
                    stack.coins.Remove(coin);
                    if (stack.IsEmpty && coinStacks.Count > 1)
                    {
                        coinStacks.RemoveAt(i);
                    }
                    break;
                }
            }
            UpdateCurrentStackIndex();
        }
        
        public List<GameObject> GetCoinsFromStacks(int amount)
        {
            List<GameObject> result = new List<GameObject>();
            
            for (int stackIndex = coinStacks.Count - 1; stackIndex >= 0 && result.Count < amount; stackIndex--)
            {
                StackState stack = coinStacks[stackIndex];
                
                while (stack.CoinCount > 0 && result.Count < amount)
                {
                    GameObject coin = stack.coins[stack.coins.Count - 1];
                    stack.coins.RemoveAt(stack.coins.Count - 1);
                    result.Add(coin);
                }
                
                if (stack.IsEmpty && coinStacks.Count > 1)
                {
                    coinStacks.RemoveAt(stackIndex);
                }
            }
            
            UpdateCurrentStackIndex();
            return result;
        }
        
        public Vector3 GetStackPosition(int stackIndex, int coinIndexInStack)
        {
            if (stackIndex >= coinStacks.Count)
                return Vector3.zero;
            
            Vector3 basePos = coinStacks[stackIndex].basePosition;
            return basePos + stackDirection * (coinIndexInStack * stackSpacing);
        }
        
        public void ClearAllStacks()
        {
            coinStacks.Clear();
            currentStackIndex = 0;
        }
        
        public void UpdateCurrentStackIndex()
        {
            if (coinStacks.Count == 0)
            {
                CreateNewStack();
                currentStackIndex = 0;
                return;
            }
            
            for (int i = 0; i < coinStacks.Count; i++)
            {
                if (!coinStacks[i].IsFull)
                {
                    currentStackIndex = i;
                    return;
                }
            }
            
            currentStackIndex = coinStacks.Count - 1;
        }
        
        public int GetTotalCoinCount()
        {
            int total = 0;
            foreach (var stack in coinStacks)
            {
                total += stack.CoinCount;
            }
            return total;
        }
        
        public void RemoveCoinsFromStacks(int coinAmount, System.Action<GameObject> onReturnCoin)
        {
            int coinsRemoved = 0;
            
            for (int stackIndex = coinStacks.Count - 1; stackIndex >= 0 && coinsRemoved < coinAmount; stackIndex--)
            {
                var stack = coinStacks[stackIndex];
                
                while (stack.CoinCount > 0 && coinsRemoved < coinAmount)
                {
                    GameObject coinToRemove = stack.coins[stack.coins.Count - 1];
                    stack.coins.RemoveAt(stack.coins.Count - 1);
                    
                    onReturnCoin(coinToRemove);
                    coinsRemoved++;
                }
                
                if (stack.IsEmpty)
                {
                    coinStacks.RemoveAt(stackIndex);
                }
            }
            
            UpdateCurrentStackIndex();
            Debug.Log($"Removed {coinsRemoved} coins from stacks");
        }
        
        public float GetScaledStackingDelay(int totalCoins)
        {
            if (!enableVariableSpeed)
                return maxStackingDelay;
            
            float t = Mathf.Clamp01((float)totalCoins / speedScaleThreshold);
            return Mathf.Lerp(maxStackingDelay, minStackingDelay, t);
        }
        
        /// <summary>
        /// コインを山に積み上げるアニメーション
        /// </summary>
        public IEnumerator AnimateStackSequentially(List<GameObject> coinsForStack, int stackIndex,
            Vector3 stackDirection, float stackSpacing,
            System.Func<GameObject, Vector3, IEnumerator> onAnimateCoin,
            System.Action<GameObject, StackState, int> onAddCoinToStack,
            int activeCoinCount)
        {
            // 山が存在しない場合は作成
            while (coinStacks.Count <= stackIndex)
            {
                CreateNewStack();
            }
            
            StackState targetStack = coinStacks[stackIndex];
            int startingPosition = targetStack.CoinCount;
            
            float currentStackingDelay = GetScaledStackingDelay(activeCoinCount);
            
            for (int coinPos = 0; coinPos < coinsForStack.Count; coinPos++)
            {
                GameObject coin = coinsForStack[coinPos];
                if (coin == null) continue;
                
                int actualPosition = startingPosition + coinPos;
                Vector3 targetPosition = targetStack.basePosition + 
                    stackDirection.normalized * stackSpacing * actualPosition;
                
                onAddCoinToStack(coin, targetStack, actualPosition);
                yield return onAnimateCoin(coin, targetPosition);
                
                yield return new WaitForSeconds(currentStackingDelay);
            }
            
            // スタック間の待機を削除（高速化）
            Debug.Log($"Stack {stackIndex} animation completed (total coins: {targetStack.CoinCount}/{targetStack.maxCoins})");
        }
        
        /// <summary>
        /// 新規コインをスタックに追加（遅延付き並列処理版）
        /// </summary>
        public IEnumerator AnimateNewCoinsToStacks(List<GameObject> coinsToAdd,
            System.Action onCreateNewStack,
            System.Func<GameObject, Vector3, IEnumerator> onAnimateCoin,
            Vector3 stackDirection, float stackSpacing)
        {
            // スタックが一つもない場合は作成
            if (coinStacks.Count == 0)
            {
                onCreateNewStack();
            }
            
            // すべてのコインのアニメーションを遅延付きで並列開始
            List<Coroutine> runningAnimations = new List<Coroutine>();
            float delay = GetScaledStackingDelay(coinsToAdd.Count);
            int index = 0;
            
            foreach (var coin in coinsToAdd)
            {
                if (coin == null) continue;
                
                // 現在のスタックが満杯かチェック
                if (currentStackIndex >= coinStacks.Count || coinStacks[currentStackIndex].IsFull)
                {
                    onCreateNewStack();
                    currentStackIndex = coinStacks.Count - 1;
                }
                
                var targetStack = coinStacks[currentStackIndex];
                int position = targetStack.CoinCount;
                Vector3 targetPosition = targetStack.basePosition + 
                    stackDirection.normalized * stackSpacing * position;
                
                // スタックにコインを追加
                targetStack.coins.Add(coin);
                
                // 遅延付きアニメーションを並列で開始
                float startDelay = delay * index;
                Coroutine animation = StartCoroutine(AnimateCoinWithDelay(coin, targetPosition, startDelay, onAnimateCoin));
                runningAnimations.Add(animation);
                
                index++;
            }
            
            // すべてのアニメーション完了を待機
            foreach (var animation in runningAnimations)
            {
                yield return animation;
            }
        }
        
        /// <summary>
        /// 遅延付きでコインアニメーションを実行
        /// </summary>
        private IEnumerator AnimateCoinWithDelay(GameObject coin, Vector3 targetPosition, float startDelay,
            System.Func<GameObject, Vector3, IEnumerator> onAnimateCoin)
        {
            // 開始遅延
            if (startDelay > 0)
            {
                yield return new WaitForSeconds(startDelay);
            }
            
            // アニメーション実行
            yield return onAnimateCoin(coin, targetPosition);
        }
        
        /// <summary>
        /// コインを指定位置にアニメーション移動（標準版）
        /// </summary>
        public IEnumerator AnimateCoinToPosition(GameObject coin, Vector3 targetPosition,
            float animationDuration,
            System.Action<int> onPlayStackSound,
            System.Func<GameObject, int> onGetCoinPositionInStack)
        {
            Vector3 startPosition = coin.transform.position;
            Quaternion startRotation = coin.transform.rotation;
            
            float randomYAngle = UnityEngine.Random.Range(-5f, 5f);
            Quaternion targetRotation = Quaternion.Euler(0f, randomYAngle, 0f);
            
            Rigidbody rb = coin.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            
            float elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / animationDuration;
                float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
                
                coin.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothProgress);
                coin.transform.rotation = Quaternion.Lerp(startRotation, targetRotation, smoothProgress);
                
                yield return null;
            }
            
            coin.transform.position = targetPosition;
            coin.transform.rotation = targetRotation;
            
            // kinematic状態のためvelocity設定は不要（エラー回避）
            
            int stackPosition = onGetCoinPositionInStack(coin);
            onPlayStackSound(stackPosition);
        }
        
        /// <summary>
        /// コインを指定位置にアニメーション移動（遅延付き）
        /// </summary>
        public IEnumerator AnimateCoinToPositionWithDelay(GameObject coin, Vector3 targetPosition, float delay,
            float animationDuration,
            System.Func<float, float> easeFunction,
            System.Action<int> onPlayStackSound,
            System.Func<GameObject, int> onGetCoinPositionInStack)
        {
            yield return new WaitForSeconds(delay);
            
            Vector3 startPosition = coin.transform.position;
            Quaternion startRotation = coin.transform.rotation;
            Quaternion targetRotation = Quaternion.identity;
            
            Rigidbody rb = coin.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            
            float elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / animationDuration;
                float smoothProgress = easeFunction(progress);
                
                coin.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothProgress);
                coin.transform.rotation = Quaternion.Lerp(startRotation, targetRotation, progress);
                
                yield return null;
            }
            
            coin.transform.position = targetPosition;
            coin.transform.rotation = targetRotation;
            
            // kinematic状態のためvelocity設定は不要（エラー回避）
            
            int stackPosition = onGetCoinPositionInStack(coin);
            onPlayStackSound(stackPosition);
        }
        
        /// <summary>
        // ...existing code...
        
        /// <summary>
        /// 枚数に応じて積み上げ速度を調整
        /// </summary>
        /// <param name="coinAmount">コイン枚数</param>
        /// <returns>調整された積み上げ間隔</returns>
        // ...existing code...
        
        /// <summary>
        /// スタックの内容をデバッグ文字列として取得
        /// </summary>
        /// <param name="stack">対象スタック</param>
        /// <returns>デバッグ文字列</returns>
        public string GetStackContentsDebug(StackState stack)
        {
            var contents = new System.Text.StringBuilder();
            contents.Append($"[{stack.coins.Count} slots: ");
            for (int i = 0; i < stack.coins.Count; i++)
            {
                contents.Append(stack.coins[i] != null ? "O" : "X");
            }
            contents.Append($"] CoinCount={stack.CoinCount}");
            return contents.ToString();
        }
        
        /// <summary>
        /// コインがスタック内の何番目かを取得
        /// </summary>
        /// <param name="coin">対象コイン</param>
        /// <returns>スタック内の位置インデックス (0-9)</returns>
        public int GetCoinPositionInItsStack(GameObject coin)
        {
            // 全スタックを検索
            foreach (var stack in coinStacks)
            {
                // スタック内でコインの位置を検索
                for (int i = 0; i < stack.coins.Count; i++)
                {
                    if (stack.coins[i] == coin)
                    {
                        return i;
                    }
                }
            }
            
            return 0;
        }
        
        /// <summary>
        /// 指定位置にコインをスタックに追加
        /// </summary>
        /// <param name="coin">追加するコイン</param>
        /// <param name="stack">対象スタック</param>
        /// <param name="position">スタック内の位置</param>
        public void AddCoinToStackAtPosition(GameObject coin, StackState stack, int position)
        {
            // 位置がリストの範囲外の場合、nullで埋める
            while (stack.coins.Count <= position)
            {
                stack.coins.Add(null);
            }
            
            // 指定位置にコインを配置
            stack.coins[position] = coin;
            
            Debug.Log($"[STACK DEBUG] Added coin to stack {stack.stackIndex} at position {position}. Stack now: {GetStackContentsDebug(stack)}");
        }
    }
}
