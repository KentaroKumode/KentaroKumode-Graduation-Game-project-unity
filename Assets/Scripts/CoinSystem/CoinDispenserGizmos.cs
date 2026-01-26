using UnityEngine;

namespace CoinSystem
{
#if UNITY_EDITOR
    /// <summary>
    /// CoinDispenser用のエディタ可視化機能
    /// </summary>
    public class CoinDispenserGizmos : MonoBehaviour
    {
        [SerializeField] private CoinDispenser dispenser;
        [SerializeField] private Transform dispenserPoint;
        [SerializeField] private Transform potTarget;
        [SerializeField] private Transform stackStartPoint;
        [SerializeField] private Transform ticketMachinePoint;
        [SerializeField] private float randomForceRange = 1f;
        [SerializeField] private float dispenseForce = 5f;
        [SerializeField] private float minDispenseInterval = 0.02f;
        [SerializeField] private float maxDispenseInterval = 0.2f;
        
        private CoinStackManager stackManager;
        private TicketSystemManager ticketManager;
        
        private void Awake()
        {
            if (dispenser == null)
                dispenser = GetComponent<CoinDispenser>();
            
            stackManager = GetComponent<CoinStackManager>();
            ticketManager = GetComponent<TicketSystemManager>();
        }
        
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.2f);
            
            DrawDispenseVisualization();
            DrawStackVisualization();
            DrawTicketVisualization();
        }
        
        private void OnDrawGizmosSelected()
        {
            DrawDetailedVisualization();
        }
        
        private void DrawDispenseVisualization()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.right, 0.1f);
            
            if (dispenserPoint == null || potTarget == null) 
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, Vector3.one * 0.1f);
                return;
            }
            
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(dispenserPoint.position, 0.1f);
            Gizmos.DrawWireCube(dispenserPoint.position, Vector3.one * 0.05f);
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(potTarget.position, 0.15f);
            
            Vector3 direction = (potTarget.position - dispenserPoint.position).normalized;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(dispenserPoint.position, direction * 1f);
            Gizmos.DrawRay(dispenserPoint.position, direction * 0.5f + Vector3.up * 0.2f);
            Gizmos.DrawRay(dispenserPoint.position, direction * 0.5f - Vector3.up * 0.2f);
            
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Vector3 center = dispenserPoint.position + direction * 0.5f;
            Gizmos.DrawWireSphere(center, randomForceRange);
        }
        
        private void DrawStackVisualization()
        {
            if (stackStartPoint == null || stackManager == null) return;
            
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(stackStartPoint.position, Vector3.one * 0.08f);
            
            for (int i = 0; i < 5; i++)
            {
                Vector3 stackPos = stackStartPoint.position + 
                    stackManager.StackDirection.normalized * 0.3f * i;
                
                Gizmos.color = (i < stackManager.CoinStacks.Count) ? 
                    Color.red : new Color(1f, 0f, 0f, 0.3f);
                Gizmos.DrawWireCube(stackPos, new Vector3(0.3f, 0.02f, 0.3f));
                
                for (int j = 0; j < 10; j++)
                {
                    Vector3 coinPos = stackPos + stackManager.StackDirection.normalized * stackManager.StackSpacing * j;
                    Gizmos.color = new Color(0.8f, 0.4f, 0f, 0.4f);
                    Gizmos.DrawWireSphere(coinPos, 0.02f);
                }
            }
        }
        
        private void DrawTicketVisualization()
        {
            if (ticketMachinePoint == null || ticketManager == null || !ticketManager.EnableTicketSystem) return;
            
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(ticketMachinePoint.position, new Vector3(0.2f, 0.1f, 0.2f));
            
            Gizmos.color = Color.cyan;
            Vector3 ticketDir = Vector3.down;
            Gizmos.DrawRay(ticketMachinePoint.position, ticketDir.normalized * 0.8f);
            
            for (int i = 0; i < 3; i++)
            {
                Vector3 ticketPos = ticketMachinePoint.position + (ticketDir.normalized * 0.1f * i);
                Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
                
                Vector3 ticketSize = new Vector3(0.15f, 0.02f, 0.1f);
                if (Mathf.Abs(ticketDir.normalized.x) > Mathf.Abs(ticketDir.normalized.z))
                {
                    ticketSize = new Vector3(0.1f, 0.02f, 0.15f);
                }
                Gizmos.DrawWireCube(ticketPos, ticketSize);
            }
        }
        
        private void DrawDetailedVisualization()
        {
            if (dispenserPoint != null && potTarget != null)
            {
                Vector3 midPoint = (dispenserPoint.position + potTarget.position) * 0.5f;
                UnityEditor.Handles.Label(midPoint + Vector3.up * 0.3f, 
                    $"排出力: {dispenseForce}\nランダム範囲: {randomForceRange}\n排出間隔: {minDispenseInterval}-{maxDispenseInterval}s");
            }
            
            if (stackStartPoint != null && stackManager != null)
            {
                UnityEditor.Handles.Label(stackStartPoint.position + Vector3.up * 0.5f,
                    $"スタック群間隔: 0.3\nコイン間隔: {stackManager.StackSpacing}\n最大コイン数/山: 10");
            }
            
            if (ticketMachinePoint != null && ticketManager != null && ticketManager.EnableTicketSystem)
            {
                UnityEditor.Handles.Label(ticketMachinePoint.position + Vector3.up * 0.3f,
                    $"チケット間隔: 0.1\n変換閾値: {ticketManager.CoinThresholdLow}-{ticketManager.CoinThresholdHigh}\nコイン/チケット: {ticketManager.CoinsPerTicket}");
            }
            
            if (dispenser != null)
            {
                Vector3 statusPos = transform.position + Vector3.up * 1f;
                string status = $"総コイン数: {dispenser.TotalStackedCoins}\n" +
                               $"アクティブ: {dispenser.ActiveCoinCount}\n" +
                               $"チケット数: {dispenser.ActiveTicketCount}\n" +
                               $"排出中: {dispenser.IsDispensing}\n" +
                               $"整列中: {dispenser.IsSorting}";
                UnityEditor.Handles.Label(statusPos, status);
            }
        }
    }
#endif
}
