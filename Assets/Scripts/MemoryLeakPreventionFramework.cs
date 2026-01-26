using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// TLS Allocator エラー根本解決のための包括的メモリリーク対策フレームワーク
/// </summary>
public class MemoryLeakPreventionFramework : MonoBehaviour
{
    [Header("監視設定")]
    [SerializeField] private bool enableMemoryMonitoring = true;
    [SerializeField] private float monitoringInterval = 5.0f;
    
    [Header("デバッグ")]
    [SerializeField] private bool logMemoryStats = false;
    
    // 静的インスタンス管理
    private static MemoryLeakPreventionFramework instance;
    private static readonly Dictionary<System.Type, int> staticInstanceCounts = new Dictionary<System.Type, int>();
    
    // コルーチン追跡
    private static readonly HashSet<MonoBehaviour> activeCoroutineOwners = new HashSet<MonoBehaviour>();
    
    // メモリ統計
    private long lastAllocatedMemory = 0;
    private long currentAllocatedMemory = 0;
    private int memoryLeakWarnings = 0;
    
    public static MemoryLeakPreventionFramework Instance => instance;
    
    void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning($"[MemoryLeakPrevention] 重複インスタンス検出: {gameObject.name}");
            Destroy(gameObject);
            return;
        }
        
        instance = this;
        DontDestroyOnLoad(gameObject);
        
        Debug.Log("[MemoryLeakPrevention] Framework initialized");
    }
    
    void Start()
    {
        if (enableMemoryMonitoring)
        {
            StartCoroutine(MemoryMonitoringCoroutine());
        }
        
        RegisterCoroutineOwner(this);
    }
    
    /// <summary>
    /// 静的インスタンス登録（Singleton監視）
    /// </summary>
    public static void RegisterStaticInstance(System.Type type, MonoBehaviour instance)
    {
        if (!staticInstanceCounts.ContainsKey(type))
        {
            staticInstanceCounts[type] = 0;
        }
        
        staticInstanceCounts[type]++;
        
        if (staticInstanceCounts[type] > 1)
        {
            Debug.LogWarning($"[MemoryLeakPrevention] 静的インスタンス重複警告: {type.Name} (Count: {staticInstanceCounts[type]})");
        }
    }
    
    /// <summary>
    /// 静的インスタンス解除
    /// </summary>
    public static void UnregisterStaticInstance(System.Type type)
    {
        if (staticInstanceCounts.ContainsKey(type))
        {
            staticInstanceCounts[type]--;
            if (staticInstanceCounts[type] <= 0)
            {
                staticInstanceCounts.Remove(type);
            }
        }
    }
    
    /// <summary>
    /// コルーチン実行オブジェクト登録
    /// </summary>
    public static void RegisterCoroutineOwner(MonoBehaviour owner)
    {
        if (owner != null && !activeCoroutineOwners.Contains(owner))
        {
            activeCoroutineOwners.Add(owner);
        }
    }
    
    /// <summary>
    /// コルーチン実行オブジェクト解除
    /// </summary>
    public static void UnregisterCoroutineOwner(MonoBehaviour owner)
    {
        if (owner != null && activeCoroutineOwners.Contains(owner))
        {
            activeCoroutineOwners.Remove(owner);
        }
    }
    
    /// <summary>
    /// メモリ監視コルーチン
    /// </summary>
    private IEnumerator MemoryMonitoringCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(monitoringInterval);
            
            lastAllocatedMemory = currentAllocatedMemory;
            currentAllocatedMemory = System.GC.GetTotalMemory(false);
            
            long memoryDelta = currentAllocatedMemory - lastAllocatedMemory;
            
            if (logMemoryStats)
            {
                Debug.Log($"[MemoryMonitoring] Current: {currentAllocatedMemory / 1024 / 1024}MB, Delta: {memoryDelta / 1024}KB");
            }
            
            // メモリリーク警告
            if (memoryDelta > 1024 * 1024) // 1MB以上の増加
            {
                memoryLeakWarnings++;
                Debug.LogWarning($"[MemoryLeakPrevention] メモリ大幅増加検出: +{memoryDelta / 1024}KB (警告回数: {memoryLeakWarnings})");
                
                if (memoryLeakWarnings >= 3)
                {
                    PerformEmergencyCleanup();
                    memoryLeakWarnings = 0;
                }
            }
        }
    }
    
    /// <summary>
    /// 緊急クリーンアップ実行
    /// </summary>
    private void PerformEmergencyCleanup()
    {
        Debug.LogWarning("[MemoryLeakPrevention] 緊急クリーンアップ実行中...");
        
        // 孤立したコルーチンオーナーをクリーンアップ
        var toRemove = new List<MonoBehaviour>();
        foreach (var owner in activeCoroutineOwners)
        {
            if (owner == null || owner.gameObject == null)
            {
                toRemove.Add(owner);
            }
        }
        
        foreach (var owner in toRemove)
        {
            activeCoroutineOwners.Remove(owner);
        }
        
        // 強制ガベージコレクション
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        
        Debug.LogWarning($"[MemoryLeakPrevention] クリーンアップ完了 - 孤立オブジェクト: {toRemove.Count}個");
    }
    
    /// <summary>
    /// リスナー安全解除ヘルパー
    /// </summary>
    public static void SafeRemoveAllListeners(UnityEngine.UI.Button button)
    {
        if (button != null && button.onClick != null)
        {
            button.onClick.RemoveAllListeners();
        }
    }
    
    public static void SafeRemoveAllListeners(UnityEngine.UI.Toggle toggle)
    {
        if (toggle != null && toggle.onValueChanged != null)
        {
            toggle.onValueChanged.RemoveAllListeners();
        }
    }
    
    /// <summary>
    /// コルーチン安全停止ヘルパー
    /// </summary>
    public static void SafeStopCoroutine(MonoBehaviour owner, Coroutine coroutine)
    {
        if (owner != null && coroutine != null)
        {
            owner.StopCoroutine(coroutine);
        }
    }
    
    void OnDestroy()
    {
        UnregisterCoroutineOwner(this);
        
        if (instance == this)
        {
            instance = null;
        }
    }
    
    /// <summary>
    /// 現在のメモリ統計を取得
    /// </summary>
    public void LogCurrentStats()
    {
        Debug.Log($"[MemoryStats] " +
                  $"Current Memory: {currentAllocatedMemory / 1024 / 1024}MB, " +
                  $"Active Coroutine Owners: {activeCoroutineOwners.Count}, " +
                  $"Static Instances: {staticInstanceCounts.Count}, " +
                  $"Leak Warnings: {memoryLeakWarnings}");
    }
}