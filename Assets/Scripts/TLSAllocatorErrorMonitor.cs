using UnityEngine;

/// <summary>
/// TLS Allocatorエラーの発生頻度を監視
/// </summary>
public class TLSAllocatorErrorMonitor : MonoBehaviour
{
    private int errorCount = 0;
    private float startTime;
    private float lastErrorTime;
    
    [Header("監視設定")]
    [SerializeField] private bool enableMonitoring = true;
    
    void Start()
    {
        startTime = Time.realtimeSinceStartup;
        
        if (enableMonitoring)
        {
            Application.logMessageReceived += HandleLog;
            Debug.Log("[TLSMonitor] Error monitoring started");
        }
    }
    
    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logString.Contains("TLS Allocator") && logString.Contains("unfreed allocations"))
        {
            errorCount++;
            float currentTime = Time.realtimeSinceStartup;
            float timeSinceStart = currentTime - startTime;
            float timeSinceLastError = currentTime - lastErrorTime;
            
            Debug.LogWarning($"[TLSMonitor] TLS Allocator エラー検出 #{errorCount}\n" +
                           $"起動からの時間: {timeSinceStart:F1}秒\n" +
                           $"前回からの間隔: {timeSinceLastError:F3}秒\n" +
                           $"発生頻度: {errorCount / timeSinceStart:F2}回/秒");
            
            lastErrorTime = currentTime;
            
            // 高頻度警告
            if (errorCount > 100 && timeSinceStart < 60)
            {
                Debug.LogError($"[TLSMonitor] 🚨 高頻度エラー検出！ {errorCount}回 in {timeSinceStart:F1}秒\n" +
                              "メモリリークが深刻です。緊急対応が必要！");
            }
        }
    }
    

    
    string GetSeverityLevel()
    {
        float runtime = Time.realtimeSinceStartup - startTime;
        float frequency = errorCount / Mathf.Max(runtime, 1f);
        
        if (errorCount == 0) return "✅ 問題なし";
        if (frequency < 0.01f) return "🟢 低頻度 - 監視継続";
        if (frequency < 0.1f) return "🟡 中頻度 - 注意が必要";
        if (frequency < 1.0f) return "🟠 高頻度 - 対策推奨";
        return "🔴 極高頻度 - 緊急対応！";
    }
    
    void OnDestroy()
    {
        if (enableMonitoring)
        {
            Application.logMessageReceived -= HandleLog;
            
            float runtime = Time.realtimeSinceStartup - startTime;
            Debug.Log($"[TLSMonitor] 監視終了\n" +
                     $"総実行時間: {runtime:F1}秒\n" +
                     $"総エラー数: {errorCount}回\n" +
                     $"平均頻度: {(errorCount / Mathf.Max(runtime, 1f)):F3}回/秒");
        }
    }
}
