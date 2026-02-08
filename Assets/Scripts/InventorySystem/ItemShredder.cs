using UnityEngine;
using CoinSystem;

namespace InventorySystem
{
    /// <summary>
    /// アイテムシュレッダー — D&amp;Dでアイテムを破棄してGOLD獲得
    /// 
    /// <para><b>機能:</b></para>
    /// <list type="bullet">
    ///   <item>アイテムをD&amp;Dでシュレッダーにドロップして破棄</item>
    ///   <item>レアリティに応じたGOLD獲得（1〜5 GOLD）</item>
    ///   <item>CoinSystem連携でコイン排出</item>
    ///   <item>破棄演出（シュレッダーアニメーション）</item>
    /// </list>
    /// 
    /// <para><b>GOLD獲得テーブル:</b></para>
    /// <list type="table">
    ///   <item>BRONZE  → 1 GOLD</item>
    ///   <item>SILVER  → 2 GOLD</item>
    ///   <item>GOLD    → 3 GOLD</item>
    ///   <item>LEGENDARY → 4 GOLD</item>
    ///   <item>MYTHIC  → 5 GOLD</item>
    /// </list>
    /// </summary>
    public class ItemShredder : MonoBehaviour
    {
        [Header("シュレッダー設定")]
        [SerializeField] private Collider shredderCollider;           // シュレッダーのコライダー（ドロップ判定）
        [SerializeField] private Transform shredderEntryPoint;        // シュレッダー投入口の位置

        [Header("CoinSystem連携")]
        [SerializeField] private CoinSystemController coinSystemController;

        [Header("演出")]
        [SerializeField] private ParticleSystem shredParticles;       // シュレッダー演出パーティクル
        [SerializeField] private AudioSource shredSound;              // シュレッダー効果音

        [Header("デバッグ")]
        [SerializeField] private bool showDebugLog = true;

        // イベント
        /// <summary>アイテムがシュレッダーされた時（アイテム名, 獲得GOLD）</summary>
        public event System.Action<string, int> OnItemShredded;

        void Awake()
        {
            // CoinSystemController自動検索
            if (coinSystemController == null)
            {
                coinSystemController = FindObjectOfType<CoinSystemController>();
                if (coinSystemController != null && showDebugLog)
                    Debug.Log("[ItemShredder] CoinSystemController auto-detected");
            }
        }

        // =================================================================
        //  公開 API
        // =================================================================

        /// <summary>
        /// アイテムをシュレッダーに投入
        /// </summary>
        /// <param name="item">破棄するアイテム</param>
        /// <returns>獲得GOLD数</returns>
        public int ShredItem(CompleteItemData item)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemShredder] ⚠️ Cannot shred null item");
                return 0;
            }

            // レアリティに応じたGOLD計算
            int goldReward = GetGoldReward(item.rarity);

            // コイン排出
            if (coinSystemController != null && goldReward > 0)
            {
                coinSystemController.DispenseCoins(goldReward);
                if (showDebugLog)
                    Debug.Log($"[ItemShredder] 💰 Dispensed {goldReward} GOLD for {item.displayName}");
            }
            else if (coinSystemController == null)
            {
                Debug.LogWarning("[ItemShredder] ⚠️ CoinSystemController not found");
            }

            // 演出再生
            PlayShredEffect();

            // 効果音再生
            PlayShredSound();

            // イベント発火
            OnItemShredded?.Invoke(item.displayName, goldReward);

            // ログ出力
            string rarityIcon = GetRarityIcon(item.rarity);
            Debug.Log($"[ItemShredder] 🔪 SHREDDED: {rarityIcon} {item.displayName} ({item.rarity}) → {goldReward} GOLD");

            return goldReward;
        }

        /// <summary>
        /// 指定位置がシュレッダー内かチェック
        /// </summary>
        public bool IsOverShredder(Vector3 worldPosition)
        {
            if (shredderCollider == null) return false;
            return shredderCollider.bounds.Contains(worldPosition);
        }

        /// <summary>
        /// レイキャストでシュレッダーにヒットしたかチェック
        /// </summary>
        public bool IsShredderHit(RaycastHit hit)
        {
            if (shredderCollider == null) return false;
            return hit.collider == shredderCollider;
        }

        /// <summary>
        /// Rayを使ってシュレッダーにヒットするかチェック
        /// </summary>
        public bool IsShredderHit(Ray ray, float maxDistance = 100f)
        {
            if (shredderCollider == null) return false;
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance);
            foreach (var h in hits)
            {
                if (h.collider == shredderCollider)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// シュレッダーの投入口座標を取得
        /// </summary>
        public Vector3 GetEntryPoint()
        {
            if (shredderEntryPoint != null)
                return shredderEntryPoint.position;
            return transform.position;
        }

        // =================================================================
        //  GOLD計算
        // =================================================================

        /// <summary>
        /// レアリティに応じたGOLD獲得量
        /// </summary>
        public static int GetGoldReward(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.BRONZE    => 1,
                ItemRarity.SILVER    => 2,
                ItemRarity.GOLD      => 3,
                ItemRarity.LEGENDARY => 4,
                ItemRarity.MYTHIC    => 5,
                _ => 1
            };
        }

        /// <summary>
        /// レアリティ別アイコン取得
        /// </summary>
        private static string GetRarityIcon(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.BRONZE    => "🥉",
                ItemRarity.SILVER    => "🥈",
                ItemRarity.GOLD      => "🥇",
                ItemRarity.LEGENDARY => "⭐",
                ItemRarity.MYTHIC    => "💎",
                _ => "❓"
            };
        }

        // =================================================================
        //  演出
        // =================================================================

        private void PlayShredEffect()
        {
            if (shredParticles != null)
            {
                shredParticles.Play();
            }
        }

        private void PlayShredSound()
        {
            if (shredSound != null && shredSound.clip != null)
            {
                shredSound.Play();
            }
            else
            {
                // フォールバック: InventorySoundManagerの破棄音を使用
                InventorySoundManager.Instance?.PlayItemDiscard();
            }
        }

        // =================================================================
        //  Gizmo
        // =================================================================
        
        void OnDrawGizmosSelected()
        {
            // シュレッダーエリアの可視化
            if (shredderCollider != null)
            {
                Gizmos.color = new Color(1f, 0.3f, 0f, 0.3f); // オレンジ半透明
                Gizmos.DrawCube(shredderCollider.bounds.center, shredderCollider.bounds.size);
                
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(shredderCollider.bounds.center, shredderCollider.bounds.size);
            }

            // 投入口の可視化
            if (shredderEntryPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(shredderEntryPoint.position, 0.1f);
            }
        }

        // =================================================================
        //  デバッグ
        // =================================================================

        [ContextMenu("Debug: Test Shred (BRONZE)")]
        private void DebugTestShredBronze()
        {
            Debug.Log($"[ItemShredder] Test BRONZE → {GetGoldReward(ItemRarity.BRONZE)} GOLD");
        }

        [ContextMenu("Debug: Test Shred (MYTHIC)")]
        private void DebugTestShredMythic()
        {
            Debug.Log($"[ItemShredder] Test MYTHIC → {GetGoldReward(ItemRarity.MYTHIC)} GOLD");
        }
    }
}
