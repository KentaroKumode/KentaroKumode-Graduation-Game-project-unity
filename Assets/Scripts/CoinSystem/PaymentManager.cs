using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// コインとチケットを使った支払い処理を管理
    /// </summary>
    public class PaymentManager : MonoBehaviour
    {
        [Header("支払い設定")]
        [SerializeField] private int coinsPerTicket = 10;
        [SerializeField] private int coinThresholdHigh = 60;
        [SerializeField] private int coinThresholdLow = 50;
        
        // 依存コンポーネント
        private CoinStackManager stackManager;
        private TicketSystemManager ticketManager;
        private CoinPoolManager poolManager;
        
        // 支払い状態
        private bool isProcessingPayment = false;
        
        public bool IsProcessingPayment => isProcessingPayment;
        
        private void Awake()
        {
            stackManager = GetComponent<CoinStackManager>();
            ticketManager = GetComponent<TicketSystemManager>();
            poolManager = GetComponent<CoinPoolManager>();
        }
        
        /// <summary>
        /// 支払い処理のメインロジック
        /// </summary>
        public IEnumerator ProcessPayment(int amount, Transform dispenserPoint, 
            System.Func<int, IEnumerator> onConsumeCoinsFromStacks,
            System.Func<int, IEnumerator> onConsumeTickets,
            System.Func<int, bool, IEnumerator> onDispenseChangeCoins,
            System.Func<int, bool, IEnumerator> onConvertTicketsToCoins,
            System.Func<IEnumerator> onSortCoins)
        {
            if (isProcessingPayment)
            {
                Debug.LogWarning("Already processing payment. Please wait.");
                yield break;
            }
            
            isProcessingPayment = true;
            
            try
            {
                Debug.Log($"Processing payment: {amount} coins");
                
                int currentCoins = stackManager.TotalStackedCoins;
                int currentTickets = ticketManager.ActiveTickets.Count;
                
                // 10枚超の支払い：チケット優先
                if (amount > 10)
                {
                    yield return ProcessLargePayment(amount, currentTickets, 
                        onConsumeTickets, onConsumeCoinsFromStacks);
                }
                // 10枚以下の支払い：条件判定
                else
                {
                    yield return ProcessSmallPayment(amount, currentCoins, currentTickets,
                        onConsumeCoinsFromStacks, onConsumeTickets, onDispenseChangeCoins);
                }
                
                // 自動補充：コインが少なければチケットから変換
                if (stackManager.TotalStackedCoins < coinThresholdLow && ticketManager.ActiveTickets.Count > 0)
                {
                    Debug.Log($"Coins below threshold ({coinThresholdLow}), converting 1 ticket");
                    yield return onConvertTicketsToCoins(1, false);
                }
                
                // 整列処理
                if (poolManager.ActiveCoins.Count > 0)
                {
                    Debug.Log($"Sorting {poolManager.ActiveCoins.Count} coins after payment");
                    yield return new WaitForSeconds(0.5f);
                    yield return onSortCoins();
                }
            }
            finally
            {
                isProcessingPayment = false;
                Debug.Log("Payment processing completed");
            }
        }
        
        /// <summary>
        /// 大量支払い処理（10枚超）
        /// </summary>
        private IEnumerator ProcessLargePayment(int amount, int currentTickets,
            System.Func<int, IEnumerator> onConsumeTickets,
            System.Func<int, IEnumerator> onConsumeCoinsFromStacks)
        {
            Debug.Log($"Large payment ({amount} coins): Ticket priority mode");
            
            // チケットから消費
            int ticketsNeeded = amount / coinsPerTicket;
            int ticketsToConsume = Math.Min(ticketsNeeded, currentTickets);
            
            if (ticketsToConsume > 0)
            {
                Debug.Log($"Consuming {ticketsToConsume} tickets");
                yield return onConsumeTickets(ticketsToConsume);
            }
            
            // 残りはコインから
            int remaining = amount - (ticketsToConsume * coinsPerTicket);
            if (remaining > 0)
            {
                Debug.Log($"Consuming remaining {remaining} coins from stacks");
                yield return onConsumeCoinsFromStacks(remaining);
            }
        }
        
        /// <summary>
        /// 小額支払い処理（10枚以下）
        /// </summary>
        private IEnumerator ProcessSmallPayment(int amount, int currentCoins, int currentTickets,
            System.Func<int, IEnumerator> onConsumeCoinsFromStacks,
            System.Func<int, IEnumerator> onConsumeTickets,
            System.Func<int, bool, IEnumerator> onDispenseChangeCoins)
        {
            Debug.Log($"Small payment ({amount} coins): Conditional mode");
            
            // チケットが0枚なら山のコインを使う
            if (currentTickets == 0)
            {
                Debug.Log("No tickets available: Using stacked coins");
                yield return onConsumeCoinsFromStacks(amount);
            }
            else
            {
                // チケットを崩した場合のおつり計算
                int change = coinsPerTicket - amount;
                int coinsAfterChange = currentCoins + change;
                
                // おつりを積んで60枚以下ならチケット使用
                if (coinsAfterChange <= coinThresholdHigh)
                {
                    Debug.Log($"Using ticket with {change} coins change (total: {coinsAfterChange} <= {coinThresholdHigh})");
                    yield return onConsumeTickets(1);
                    
                    // おつり排出
                    if (change > 0)
                    {
                        Debug.Log($"Dispensing {change} coins as change");
                        yield return onDispenseChangeCoins(change, false);
                    }
                }
                else
                {
                    Debug.Log($"Change would exceed limit ({coinsAfterChange} > {coinThresholdHigh}): Using stacked coins");
                    yield return onConsumeCoinsFromStacks(amount);
                }
            }
        }
    }
}
