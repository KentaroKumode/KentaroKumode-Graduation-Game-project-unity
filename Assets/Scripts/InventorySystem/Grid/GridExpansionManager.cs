using UnityEngine;
using System.Collections;

namespace InventorySystem
{
    /// <summary>
    /// グリッド拡張とアンロック演出を管理
    /// </summary>
    public class GridExpansionManager : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private GridManager gridManager;
        
        [Header("演出設定")]
        [SerializeField] private GameObject lockPrefab;          // 鎖・錠前プレハブ
        [SerializeField] private GameObject explosionEffect;     // 爆発エフェクト
        [SerializeField] private float shakeDuration = InventoryConstants.UNLOCK_SHAKE_DURATION;
        [SerializeField] private float shakeIntensity = 0.1f;
        [SerializeField] private float flashDuration = InventoryConstants.UNLOCK_FLASH_DURATION;
        
        [Header("音")]
        [SerializeField] private AudioClip shakeSound;
        [SerializeField] private AudioClip explosionSound;
        
        private AudioSource audioSource;
        
        void Start()
        {
            if (gridManager == null)
            {
                gridManager = FindObjectOfType<GridManager>();
            }
            
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        
        /// <summary>
        /// 拡張演出を実行
        /// </summary>
        public void ExecuteExpansion(int newRowCount)
        {
            if (gridManager == null)
            {
                Debug.LogError("[GridExpansionManager] GridManager is null!");
                return;
            }
            
            StartCoroutine(ExpansionCoroutine(newRowCount));
        }
        
        /// <summary>
        /// 拡張演出コルーチン
        /// </summary>
        private IEnumerator ExpansionCoroutine(int newRowCount)
        {
            int rowToUnlock = newRowCount - 1;
            
            // 対象行のセルを取得
            GridCell[] cellsToUnlock = new GridCell[InventoryConstants.GRID_WIDTH];
            for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
            {
                cellsToUnlock[x] = gridManager.GetCell(x, rowToUnlock);
            }
            
            // Phase 1: 鎖が揺れる
            yield return StartCoroutine(ShakeLocksPhase(cellsToUnlock));
            
            // Phase 2: 少し待機
            yield return new WaitForSeconds(0.2f);
            
            // Phase 3: 白フラッシュで吹き飛ぶ
            yield return StartCoroutine(ExplosionPhase(cellsToUnlock));
            
            // 実際にアンロック
            gridManager.UnlockRow(newRowCount);
            
            Debug.Log($"[GridExpansionManager] Expansion animation complete for row {rowToUnlock}");
        }
        
        /// <summary>
        /// 鎖が揺れるフェーズ
        /// </summary>
        private IEnumerator ShakeLocksPhase(GridCell[] cells)
        {
            // 揺れる音
            if (shakeSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(shakeSound);
            }
            
            float elapsed = 0f;
            Vector3[] originalPositions = new Vector3[cells.Length];
            
            // 元の位置を記録
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null)
                {
                    originalPositions[i] = cells[i].transform.position;
                }
            }
            
            // 揺らす
            while (elapsed < shakeDuration)
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] != null)
                    {
                        Vector3 offset = new Vector3(
                            Random.Range(-shakeIntensity, shakeIntensity),
                            0,
                            Random.Range(-shakeIntensity, shakeIntensity)
                        );
                        cells[i].transform.position = originalPositions[i] + offset;
                    }
                }
                
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            // 位置を戻す
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null)
                {
                    cells[i].transform.position = originalPositions[i];
                }
            }
        }
        
        /// <summary>
        /// 爆発フェーズ
        /// </summary>
        private IEnumerator ExplosionPhase(GridCell[] cells)
        {
            // 爆発音
            if (explosionSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(explosionSound);
            }
            
            // 白フラッシュ
            foreach (var cell in cells)
            {
                if (cell != null)
                {
                    // エフェクト生成
                    if (explosionEffect != null)
                    {
                        Instantiate(explosionEffect, cell.transform.position, Quaternion.identity);
                    }
                    
                    // TODO: 白フラッシュシェーダー適用
                }
            }
            
            yield return new WaitForSeconds(flashDuration);
        }
    }
}
