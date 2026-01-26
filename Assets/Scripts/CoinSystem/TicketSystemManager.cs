using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace CoinSystem
{
    /// <summary>
    /// チケットシステム管理
    /// </summary>
    public class TicketSystemManager : MonoBehaviour
    {
        [Header("チケット設定")]
        [SerializeField] private bool enableTicketSystem = true;
        [SerializeField] private GameObject ticketPrefab;
        [SerializeField] private int coinsPerTicket = 10;
        [SerializeField] private int coinThresholdHigh = 60;
        [SerializeField] private int coinThresholdLow = 50;
        [SerializeField] private Transform ticketMachinePoint;
        [SerializeField] private Vector3 ticketDirection = Vector3.down;
        [SerializeField] private float ticketSpacing = 0.1f;
        [SerializeField] private float ticketDispenseDistance = 0.05f;
        [SerializeField] [Range(0.5f, 5.0f)] private float ticketEmergenceDuration = 1.5f;
        [SerializeField] [Range(0f, 0.5f)] private float ticketRandomVelocity = 0.1f;
        [SerializeField] [Range(0f, 10.0f)] private float ticketCoinSoundDelay = 0.0f;
        [SerializeField] [Range(0.5f, 2.0f)] private float ticketCoinSoundPitch = 1.0f;
        [SerializeField] [Range(0.1f, 2.0f)] private float ticketSoundDelay = 0.2f;
        [SerializeField] [Range(0.5f, 3.0f)] private float ticketDispenseInterval = 1.0f;
        
        private List<GameObject> activeTickets = new List<GameObject>();
        private int ticketCount = 0;
        
        public bool EnableTicketSystem => enableTicketSystem;
        public int CoinsPerTicket => coinsPerTicket;
        public int CoinThresholdHigh => coinThresholdHigh;
        public int CoinThresholdLow => coinThresholdLow;
        public int ActiveTicketCount => activeTickets.Count;
        public List<GameObject> ActiveTickets => activeTickets;
        public float TicketEmergenceDuration => ticketEmergenceDuration;
        public float TicketRandomVelocity => ticketRandomVelocity;
        public float TicketCoinSoundDelay => ticketCoinSoundDelay;
        public float TicketCoinSoundPitch => ticketCoinSoundPitch;
        public float TicketSoundDelay => ticketSoundDelay;
        public float TicketDispenseInterval => ticketDispenseInterval;
        public float TicketSpacing => ticketSpacing;
        public Vector3 TicketDirection => ticketDirection;
        
        /// <summary>
        /// チケットを作成・排出（音付き）
        /// </summary>
        public IEnumerator CreateAndDispenseTickets(int ticketAmount, System.Action playTicketSound)
        {
            if (ticketPrefab == null || ticketMachinePoint == null)
            {
                Debug.LogError("TicketPrefab or ticketMachinePoint not available!");
                yield break;
            }
            
            Debug.Log($"Creating and dispensing {ticketAmount} tickets");
            
            // AudioManagerを取得してコイン音も再生できるように
            var audioManager = GetComponent<CoinAudioManager>();
            
            for (int i = 0; i < ticketAmount; i++)
            {
                Debug.Log($"Starting ticket {i + 1} printing process");
                
                // チケット印刷音を再生
                if (playTicketSound != null)
                {
                    Debug.Log($"Playing ticket sound for ticket {i + 1}");
                    playTicketSound.Invoke();
                }
                else
                {
                    Debug.LogWarning("playTicketSound callback is null!");
                }
                
                // チケット印刷後のコイン音を並列で処理（排出をブロックしないように）
                if (audioManager != null && ticketCoinSoundDelay >= 0)
                {
                    StartCoroutine(PlayDelayedCoinSound(audioManager, ticketCoinSoundDelay, ticketCoinSoundPitch, i + 1));
                }
                
                // 音声間隔を待つ（チケット印刷音のみ）
                if (ticketSoundDelay > 0)
                {
                    Debug.Log($"Waiting {ticketSoundDelay}s for ticket sound delay");
                    yield return new WaitForSeconds(ticketSoundDelay);
                }
                
                yield return StartCoroutine(PrintSingleTicket(i));
                
                // 次のチケットまで間隔を置く（速度制限）
                Debug.Log($"Waiting {ticketDispenseInterval}s before next ticket");
                yield return new WaitForSeconds(ticketDispenseInterval);
            }
            
            Debug.Log($"Completed creating {ticketAmount} tickets");
        }
        
        /// <summary>
        /// 遅延付きでコイン音を再生（並列処理用）
        /// </summary>
        private IEnumerator PlayDelayedCoinSound(CoinAudioManager audioManager, float delay, float pitch, int ticketNumber)
        {
            if (delay > 0)
            {
                Debug.Log($"Waiting {delay}s before playing coin sound for ticket {ticketNumber}");
                yield return new WaitForSeconds(delay);
            }
            
            Debug.Log($"Playing ticket coin sound with pitch {pitch} for ticket {ticketNumber}");
            audioManager.PlayRandomCoinSound(pitch);
        }
        
        /// <summary>
        /// 1枚のチケットを印刷・排出
        /// </summary>
        private IEnumerator PrintSingleTicket(int ticketIndex)
        {
            GameObject ticket = CreateTicket();
            if (ticket == null)
            {
                Debug.LogError("Failed to create ticket");
                yield break;
            }
            
            Debug.Log($"Ticket created: {ticket.name} at {ticketMachinePoint.position}");
            
            // 初期位置をチケット機の少し手前に設定（隠す）
            Vector3 hiddenPosition = ticketMachinePoint.position - (ticketDirection.normalized * 0.01f);
            ticket.transform.position = hiddenPosition;
            ticket.transform.rotation = ticketMachinePoint.rotation;
            ticket.SetActive(true);
            
            // チケット印刷中はコインとの衝突を回避
            Rigidbody rb = ticket.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
            }
            
            // コライダーも無効化
            Collider col = ticket.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }
            
            // チケット出現アニメーション
            Vector3 originalScale = ticket.transform.localScale;
            Vector3 completePosition = ticketMachinePoint.position + (ticketDirection.normalized * 0.2f);
            
            yield return StartCoroutine(AnimateTicketEmergence(ticket, hiddenPosition, completePosition, originalScale, originalScale, ticketEmergenceDuration));
            
            // アニメ終了後、物理挙動を有効化
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.detectCollisions = true;
                
                // ランダム誤差
                Vector3 randomVel = new Vector3(
                    UnityEngine.Random.Range(-ticketRandomVelocity, ticketRandomVelocity),
                    0f,
                    UnityEngine.Random.Range(-ticketRandomVelocity, ticketRandomVelocity)
                );
                rb.velocity = randomVel;
            }
            
            if (col != null)
            {
                col.enabled = true;
            }
        }
        
        public GameObject CreateTicket()
        {
            if (ticketPrefab == null)
            {
                Debug.LogError("TicketPrefab is not assigned!");
                return null;
            }
            
            GameObject ticket = Instantiate(ticketPrefab);
            activeTickets.Add(ticket);
            ticketCount++;
            
            return ticket;
        }
        
        public void RemoveTicket(GameObject ticket)
        {
            if (activeTickets.Contains(ticket))
            {
                activeTickets.Remove(ticket);
                ticketCount--;
            }
        }
        
        public Vector3 GetTicketPosition(int ticketIndex)
        {
            if (ticketMachinePoint == null)
            {
                Debug.LogError("ticketMachinePoint is not assigned in TicketSystemManager");
                return Vector3.zero;
            }
            return ticketMachinePoint.position + ticketDirection.normalized * (ticketIndex * ticketSpacing);
        }
        
        public Vector3 GetTicketHiddenPosition()
        {
            if (ticketMachinePoint == null)
            {
                Debug.LogError("ticketMachinePoint is not assigned in TicketSystemManager");
                return Vector3.zero;
            }
            return ticketMachinePoint.position - (ticketDirection.normalized * 0.01f);
        }
        
        public Vector3 GetTicketEmergencePosition()
        {
            if (ticketMachinePoint == null)
            {
                Debug.LogError("ticketMachinePoint is not assigned in TicketSystemManager");
                return Vector3.zero;
            }
            return ticketMachinePoint.position + (ticketDirection.normalized * ticketDispenseDistance);
        }
        
        public Quaternion GetTicketRotation()
        {
            if (ticketMachinePoint == null)
            {
                Debug.LogError("ticketMachinePoint is not assigned in TicketSystemManager");
                return Quaternion.identity;
            }
            return ticketMachinePoint.rotation;
        }
        
        public bool ShouldConvertToTicket(int currentCoins, int incomingCoins)
        {
            if (!enableTicketSystem)
                return false;
            
            int totalCoins = currentCoins + incomingCoins;
            return totalCoins > coinThresholdHigh;
        }
        
        public int CalculateTicketsNeeded(int excessCoins)
        {
            return (excessCoins + coinsPerTicket - 1) / coinsPerTicket;
        }
        
        public int CalculateCoinsToConvert(int excessCoins)
        {
            int ticketsNeeded = CalculateTicketsNeeded(excessCoins);
            return ticketsNeeded * coinsPerTicket;
        }
        
        public void ClearAllTickets()
        {
            foreach (var ticket in activeTickets)
            {
                if (ticket != null)
                    Destroy(ticket);
            }
            activeTickets.Clear();
            ticketCount = 0;
        }
        
        public void SetTicketPrefab(GameObject prefab)
        {
            ticketPrefab = prefab;
        }
        
        /// <summary>
        /// チケットの出現アニメーション
        /// </summary>
        public IEnumerator AnimateTicketEmergence(GameObject ticket, Vector3 startPos, Vector3 endPos, 
            Vector3 startScale, Vector3 endScale, float duration)
        {
            float elapsed = 0f;
            
            while (elapsed < duration && ticket != null)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                
                ticket.transform.position = Vector3.Lerp(startPos, endPos, progress);
                ticket.transform.localScale = Vector3.Lerp(startScale, endScale, progress);
                
                yield return null;
            }
            
            if (ticket != null)
            {
                ticket.transform.position = endPos;
                ticket.transform.localScale = endScale;
            }
        }
        
        /// <summary>
        /// チケットを指定位置にアニメーション移動（物理演算オフ）
        /// </summary>
        public IEnumerator AnimateTicketToPositionKinematic(GameObject ticket, Vector3 targetPosition, float duration = 0.8f)
        {
            Vector3 startPosition = ticket.transform.position;
            float elapsed = 0f;
            
            Rigidbody rb = ticket.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
            }
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
                ticket.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothProgress);
                
                yield return null;
            }
            
            ticket.transform.position = targetPosition;
        }
        
        /// <summary>
        /// チケットをディスペンサーに吸い込むアニメーション
        /// </summary>
        public IEnumerator AnimateTicketToDispenser(GameObject ticket, Vector3 targetPosition, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            if (ticket == null) yield break;
            
            Vector3 startPosition = ticket.transform.position;
            float duration = 0.5f;
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                if (ticket == null) yield break;
                
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                // ベジェ曲線で滑らかに移動
                Vector3 midPoint = Vector3.Lerp(startPosition, targetPosition, 0.5f) + Vector3.up * 0.3f;
                Vector3 m1 = Vector3.Lerp(startPosition, midPoint, t);
                Vector3 m2 = Vector3.Lerp(midPoint, targetPosition, t);
                ticket.transform.position = Vector3.Lerp(m1, m2, t);
                
                // 回転アニメーション
                ticket.transform.Rotate(Vector3.up, 360f * Time.deltaTime * 2f);
                
                yield return null;
            }
            
            // 最後に削除
            if (ticket != null)
            {
                Destroy(ticket);
            }
        }
        
        /// <summary>
        /// チケットを整列させる
        /// </summary>
        public IEnumerator SortTickets(Transform ticketMachinePoint, float ticketSpacing, Vector3 ticketDirection,
            System.Func<GameObject, Vector3, IEnumerator> onAnimateTicket)
        {
            Debug.Log("Starting ticket sorting");
            
            yield return new WaitForSeconds(3f);
            
            List<GameObject> validTickets = new List<GameObject>();
            for (int i = 0; i < activeTickets.Count; i++)
            {
                if (activeTickets[i] != null)
                {
                    validTickets.Add(activeTickets[i]);
                }
            }
            
            for (int i = 0; i < validTickets.Count; i++)
            {
                GameObject ticket = validTickets[i];
                if (ticket == null) continue;
                
                Vector3 basePosition = ticketMachinePoint.position + 
                    (ticketDirection.normalized * ticketSpacing * i);
                Vector3 minorOffset = new Vector3(0, 0, 0);
                Vector3 sortedPosition = basePosition + minorOffset;
                
                yield return onAnimateTicket(ticket, sortedPosition);
                
                yield return new WaitForSeconds(0.1f);
            }
            
            ticketCount = validTickets.Count;
            Debug.Log($"Sorted {validTickets.Count} tickets in sheet formation");
        }
        
        /// <summary>
        /// 指定枚数のチケットを消費（支払い用）
        /// </summary>
        public IEnumerator ConsumeTickets(int amount, Transform dispenserPoint, System.Func<GameObject, IEnumerator> animateToDispenser)
        {
            List<GameObject> ticketsToConsume = new List<GameObject>();
            
            // 後ろのチケットから順に収集してマネージャーから削除
            for (int i = 0; i < amount && activeTickets.Count > 0; i++)
            {
                GameObject ticket = activeTickets[activeTickets.Count - 1];
                ticketsToConsume.Add(ticket);
                RemoveTicket(ticket);
            }
            
            // 排出口の左右にランダムにオフセット
            Vector3 sideOffset = UnityEngine.Random.value > 0.5f ? 
                dispenserPoint.right * 0.15f : -dispenserPoint.right * 0.15f;
            
            // 各チケットを排出口へアニメーション
            for (int i = 0; i < ticketsToConsume.Count; i++)
            {
                GameObject ticket = ticketsToConsume[i];
                Vector3 targetPosition = dispenserPoint.position + sideOffset;
                float delay = i * 0.1f;
                yield return animateToDispenser(ticket);
            }
            
            // アニメーション完了まで待機
            float totalTime = ticketsToConsume.Count * 0.1f + 0.5f;
            yield return new WaitForSeconds(totalTime);
            
            // チケットオブジェクトを削除
            foreach (var ticket in ticketsToConsume)
            {
                if (ticket != null)
                    Destroy(ticket);
            }
            
            Debug.Log($"Consumed {ticketsToConsume.Count} tickets for payment");
        }
    }
}
