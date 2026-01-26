using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// コインとチケット間の変換処理を管理
    /// </summary>
    public class CoinTicketConversionManager : MonoBehaviour
    {
        [Header("変換設定")]
        [SerializeField] private int coinsPerTicket = 10;
        [SerializeField] private int coinThresholdHigh = 60;
        [SerializeField] private int coinThresholdLow = 50;
        
        // 依存コンポーネント
        private CoinStackManager stackManager;
        private TicketSystemManager ticketManager;
        private CoinPoolManager poolManager;
        
        private void Awake()
        {
            stackManager = GetComponent<CoinStackManager>();
            ticketManager = GetComponent<TicketSystemManager>();
            poolManager = GetComponent<CoinPoolManager>();
        }
        
        /// <summary>
        /// 整列前の自動チケット変換チェック
        /// </summary>
        public IEnumerator CheckAndConvertToTickets(
            System.Func<int, IEnumerator> onConvertNewCoins,
            System.Func<int, IEnumerator> onCollectFromStacks)
        {
            int stackedCoins = stackManager.TotalStackedCoins;
            int activeCoins = poolManager.ActiveCoins.Count;
            int totalCoins = stackedCoins + activeCoins;
            
            // 60枚以下なら変換不要
            if (totalCoins <= coinThresholdHigh)
            {
                yield break;
            }
            
            // 超過分をチケット化（10枚単位で切り上げ）
            int excessCoins = totalCoins - coinThresholdHigh;
            int ticketsToCreate = (excessCoins + coinsPerTicket - 1) / coinsPerTicket;
            int coinsToConvert = ticketsToCreate * coinsPerTicket;
            
            int coinsAfterConversion = totalCoins - coinsToConvert;
            Debug.Log($"Converting {coinsToConvert} coins to {ticketsToCreate} tickets. Before: {totalCoins}, After: {coinsAfterConversion}");
            
            // 新規排出分から優先的に変換
            int coinsFromNew = Math.Min(coinsToConvert, activeCoins);
            int coinsFromStacked = coinsToConvert - coinsFromNew;
            
            if (coinsFromNew > 0)
            {
                yield return onConvertNewCoins(coinsFromNew);
            }
            
            if (coinsFromStacked > 0)
            {
                yield return onCollectFromStacks(coinsFromStacked);
            }
        }
        
        /// <summary>
        /// コイン不足時の自動変換チェック
        /// </summary>
        public void CheckAndConvertFromTickets(
            System.Func<int, IEnumerator> onConvertTicketsToCoins)
        {
            int currentCoins = stackManager.TotalStackedCoins;
            
            // コインが0枚でチケットがある場合のみ変換
            if (currentCoins == 0 && ticketManager.ActiveTickets.Count > 0)
            {
                int ticketsToConvert = Math.Min(1, ticketManager.ActiveTickets.Count);
                StartCoroutine(onConvertTicketsToCoins(ticketsToConvert));
            }
        }
        
        /// <summary>
        /// 新規排出コインをチケットに変換
        /// </summary>
        public IEnumerator ConvertNewCoinsToTicket(int coinsToConvert, Transform dispenserPoint,
            System.Func<GameObject, Vector3, float, IEnumerator> onAnimateCoinToDispenser,
            System.Func<int, IEnumerator> onCreateTickets)
        {
            Debug.Log($"Converting {coinsToConvert} newly dispensed coins to tickets");
            
            List<GameObject> coinsToRemove = new List<GameObject>();
            
            // activeCoinsから取得
            for (int i = 0; i < coinsToConvert && i < poolManager.ActiveCoins.Count; i++)
            {
                coinsToRemove.Add(poolManager.ActiveCoins[i]);
            }
            
            // リストから削除
            foreach (var coin in coinsToRemove)
            {
                poolManager.ActiveCoins.Remove(coin);
            }
            
            Debug.Log($"Removed {coinsToRemove.Count} coins from active list");
            
            // 排出口へ吸い込みアニメーション
            Vector3 sideOffset = UnityEngine.Random.value > 0.5f ? 
                dispenserPoint.right * 0.15f : -dispenserPoint.right * 0.15f;
            
            for (int i = 0; i < coinsToRemove.Count; i++)
            {
                GameObject coin = coinsToRemove[i];
                Vector3 targetPosition = dispenserPoint.position + sideOffset;
                float delay = i * 0.05f;
                
                yield return onAnimateCoinToDispenser(coin, targetPosition, delay);
            }
            
            yield return new WaitForSeconds(0.3f);
            
            // チケット生成
            int ticketsToCreate = coinsToConvert / coinsPerTicket;
            if (ticketsToCreate > 0)
            {
                yield return onCreateTickets(ticketsToCreate);
            }
        }
        
        /// <summary>
        /// 山からコインを回収してチケットに変換
        /// </summary>
        public IEnumerator CollectCoinsForTicket(int coinsToCollect, Transform dispenserPoint,
            System.Func<GameObject, Vector3, float, IEnumerator> onAnimateCoinToDispenser,
            System.Func<int, IEnumerator> onCreateTickets,
            System.Action onUpdateStackIndex)
        {
            Debug.Log($"Collecting {coinsToCollect} coins from stacks for ticket conversion");
            
            List<GameObject> collectedCoins = new List<GameObject>();
            int coinsCollected = 0;
            
            // 後ろの山から回収
            for (int stackIndex = stackManager.CoinStacks.Count - 1; stackIndex >= 0 && coinsCollected < coinsToCollect; stackIndex--)
            {
                var stack = stackManager.CoinStacks[stackIndex];
                
                while (stack.CoinCount > 0 && coinsCollected < coinsToCollect)
                {
                    GameObject coin = stack.coins[stack.coins.Count - 1];
                    stack.coins.RemoveAt(stack.coins.Count - 1);
                    collectedCoins.Add(coin);
                    coinsCollected++;
                }
                
                // 空の山を削除
                if (stack.CoinCount == 0 && stackManager.CoinStacks.Count > 1)
                {
                    stackManager.CoinStacks.RemoveAt(stackIndex);
                }
            }
            
            onUpdateStackIndex();
            
            // 吸い込みアニメーション
            Vector3 sideOffset = UnityEngine.Random.value > 0.5f ? 
                dispenserPoint.right * 0.15f : -dispenserPoint.right * 0.15f;
            
            for (int i = 0; i < collectedCoins.Count; i++)
            {
                GameObject coin = collectedCoins[i];
                Vector3 targetPosition = dispenserPoint.position + sideOffset;
                float delay = i * 0.05f;
                
                yield return onAnimateCoinToDispenser(coin, targetPosition, delay);
            }
            
            yield return new WaitForSeconds(0.3f);
            
            // チケット生成
            int ticketsToCreate = coinsCollected / coinsPerTicket;
            if (ticketsToCreate > 0)
            {
                yield return onCreateTickets(ticketsToCreate);
            }
        }
        
        /// <summary>
        /// チケットをコインに変換
        /// </summary>
        public IEnumerator ConvertTicketsToCoins(int ticketAmount, bool autoSort,
            System.Func<GameObject, Vector3, float, IEnumerator> onAnimateTicketToDispenser,
            System.Func<int, bool, IEnumerator> onDispenseCoins,
            Transform dispenserPoint)
        {
            for (int i = 0; i < ticketAmount && ticketManager.ActiveTickets.Count > 0; i++)
            {
                // チケット除去と吸い込み
                GameObject ticket = ticketManager.ActiveTickets[ticketManager.ActiveTickets.Count - 1];
                ticketManager.ActiveTickets.RemoveAt(ticketManager.ActiveTickets.Count - 1);
                
                Vector3 targetPosition = dispenserPoint.position;
                yield return onAnimateTicketToDispenser(ticket, targetPosition, 0f);
                
                yield return new WaitForSeconds(0.3f);
                
                // コイン排出
                yield return onDispenseCoins(coinsPerTicket, autoSort);
            }
        }
        
        /// <summary>
        /// コインをディスペンサーに吸い込むアニメーション
        /// </summary>
        public IEnumerator AnimateCoinToDispenser(GameObject coin, Vector3 targetPosition, float delay,
            System.Action onPlaySound)
        {
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }
            
            onPlaySound();
            
            Vector3 startPosition = coin.transform.position;
            float duration = 0.5f;
            float elapsed = 0f;
            
            Rigidbody rb = coin.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
            }
            
            Vector3 midPoint = (startPosition + targetPosition) / 2f + Vector3.up * 0.3f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
                
                Vector3 m1 = Vector3.Lerp(startPosition, midPoint, smoothProgress);
                Vector3 m2 = Vector3.Lerp(midPoint, targetPosition, smoothProgress);
                coin.transform.position = Vector3.Lerp(m1, m2, smoothProgress);
                
                coin.transform.Rotate(Vector3.up, 360f * Time.deltaTime * 2f);
                
                yield return null;
            }
            
            coin.transform.position = targetPosition;
        }
    }
}
