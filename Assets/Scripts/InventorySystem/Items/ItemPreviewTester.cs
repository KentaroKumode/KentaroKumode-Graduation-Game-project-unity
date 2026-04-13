using UnityEngine;
using InventorySystem;

namespace InventorySystem
{
    /// <summary>
    /// アイテムプレビューテストシステム
    /// スペースキーでランダムアイテムを生成してプレビュー表示
    /// </summary>
    public class ItemPreviewTester : MonoBehaviour
    {
        [Header("設定")]
        [SerializeField] private ItemLibrary itemLibrary;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private ItemPreview3DTextDisplay textDisplay;
        [SerializeField] private bool enableAutoDestroy = true;
        [SerializeField] private float autoDestroyTime = 5f;
        
        [Header("表示設定")]
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;
        [SerializeField] private Vector3 rotationSpeed = new Vector3(0, 45, 0);
        [SerializeField] private bool enableRotation = true;
        
        private GameObject currentPreviewObject;
        private ItemDataV2 currentItem;
        
        void Start()
        {
            if (itemLibrary == null)
            {
                Debug.LogError("ItemLibraryが設定されていません！");
                return;
            }
            
            if (spawnPoint == null)
                spawnPoint = transform;
                
            Debug.Log($"ItemPreviewTester initialized. Library has {itemLibrary.Count} items.");
        }
        
        void Update()
        {
            // スペースキーでランダムアイテム生成
            if (Input.GetKeyDown(KeyCode.Space))
            {
                GenerateRandomItem();
            }
            
            // R キーでリセット
            if (Input.GetKeyDown(KeyCode.R))
            {
                ClearCurrentPreview();
            }
            
            // 現在のオブジェクトを回転
            if (enableRotation && currentPreviewObject != null)
            {
                currentPreviewObject.transform.Rotate(rotationSpeed * Time.deltaTime);
            }
        }
        
        /// <summary>
        /// ランダムなアイテムを生成してプレビュー
        /// </summary>
        public void GenerateRandomItem()
        {
            if (itemLibrary == null || itemLibrary.Count == 0)
            {
                Debug.LogWarning("アイテムライブラリが空です！");
                return;
            }
            
            ClearCurrentPreview();
            currentItem = itemLibrary.GetRandomItem();
            ShowCurrentItem();
        }
        
        /// <summary>
        /// 指定レアリティのランダムアイテムを生成
        /// </summary>
        public void GenerateItemByRarity(ItemRarity rarity)
        {
            if (itemLibrary == null)
            {
                Debug.LogWarning("アイテムライブラリが設定されていません。");
                return;
            }
            
            ClearCurrentPreview();
            currentItem = itemLibrary.GetRandomItemByRarity(rarity);
            ShowCurrentItem();
        }
        
        /// <summary>
        /// 現在のアイテムをプレビュー表示（共通処理）
        /// </summary>
        private void ShowCurrentItem()
        {
            if (currentItem == null)
            {
                Debug.LogWarning("アイテムの取得に失敗しました。");
                return;
            }
            
            if (currentItem.fbxModel != null)
            {
                Vector3 spawnPos = spawnPoint.position + spawnOffset;
                currentPreviewObject = Instantiate(currentItem.fbxModel, spawnPos, spawnPoint.rotation);
                ApplyRarityEffects(currentPreviewObject, currentItem.rarity);
            }
            else
            {
                Debug.LogWarning($"アイテム '{currentItem.displayName}' にFBXモデルが設定されていません。");
            }
            
            DisplayItemInfo(currentItem);
            
            if (enableAutoDestroy && currentPreviewObject != null)
            {
                Destroy(currentPreviewObject, autoDestroyTime);
            }
            
            LogItemInfo(currentItem);
        }
        
        /// <summary>
        /// 現在のプレビューをクリア
        /// </summary>
        public void ClearCurrentPreview()
        {
            if (currentPreviewObject != null)
            {
                DestroyImmediate(currentPreviewObject);
                currentPreviewObject = null;
            }
            
            if (textDisplay != null)
            {
                textDisplay.Hide();
            }
            
            currentItem = null;
        }
        
        /// <summary>
        /// アイテム情報をテキスト表示
        /// </summary>
        private void DisplayItemInfo(ItemDataV2 item)
        {
            if (textDisplay == null) return;
            
            var completeItem = CompleteItemData.FromItemDataV2(item);
            textDisplay.ShowItemInfo(completeItem);
        }
        
        /// <summary>
        /// レアリティに基づく視覚効果を適用
        /// </summary>
        private void ApplyRarityEffects(GameObject obj, ItemRarity rarity)
        {
            if (obj == null) return;
            
            // レアリティカラーを取得
            Color rarityColor = RarityColorUtility.GetRarityColor(rarity);
            
            // レンダラーの色を変更
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.materials)
                {
                    if (material.HasProperty("_Color"))
                    {
                        material.color = Color.Lerp(material.color, rarityColor, 0.3f);
                    }
                    else if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", Color.Lerp(material.GetColor("_BaseColor"), rarityColor, 0.3f));
                    }
                }
            }
            
            // 高レアリティには追加効果
            if (rarity >= ItemRarity.LEGENDARY)
            {
                AddGlowEffect(obj, rarityColor);
            }
        }
        
        /// <summary>
        /// グロー効果を追加
        /// </summary>
        private void AddGlowEffect(GameObject obj, Color glowColor)
        {
            // 簡単なスケールアニメーション
            var scaler = obj.AddComponent<SimpleScaler>();
            scaler.Initialize(glowColor);
        }
        
        /// <summary>
        /// アイテム情報をログ出力
        /// </summary>
        private void LogItemInfo(ItemDataV2 item)
        {
            Debug.Log("=== アイテム情報 ===");
            Debug.Log($"表示名: {item.displayName}");
            Debug.Log($"内部名: {item.internalName}");
            Debug.Log($"カテゴリ: {item.category}");
            Debug.Log($"レアリティ: {item.rarity}");
            Debug.Log($"サイズ: {item.size.x}x{item.size.y}");
            Debug.Log($"購入価格: {item.buyPrice}G");
            Debug.Log($"売却価格: {item.sellPrice}G");
            Debug.Log($"説明: {item.description}");
            
            if (item.IsWeapon)
            {
                Debug.Log($"武器ダイス: {item.weaponDice}");
                Debug.Log($"武器パッシブ数: {item.weaponPassives.Count}");
            }
            
            if (item.IsPassive)
            {
                Debug.Log($"パッシブ効果数: {item.passiveEffects.Count}");
            }
            
            if (item.IsQuest)
            {
                Debug.Log($"フレーバーテキスト: {item.flavorText}");
                Debug.Log($"スキル名: {item.skillName}");
            }
        }
        
        void OnGUI()
        {
            return; // テストUI一時無効化
            // デバッグ情報表示
            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label("=== アイテムプレビューテスト ===");
            GUILayout.Label("Space: ランダムアイテム生成");
            GUILayout.Label("R: リセット");
            
            if (itemLibrary != null)
            {
                GUILayout.Label($"ライブラリアイテム数: {itemLibrary.Count}");
            }
            
            if (currentItem != null)
            {
                GUILayout.Label($"現在: {currentItem.displayName}");
                GUILayout.Label($"レアリティ: {currentItem.rarity}");
            }
            
            GUILayout.EndArea();
        }
    }
    
    /// <summary>
    /// 簡単なスケールアニメーション
    /// </summary>
    public class SimpleScaler : MonoBehaviour
    {
        private Vector3 originalScale;
        private Color glowColor;
        private float time = 0f;
        
        public void Initialize(Color color)
        {
            originalScale = transform.localScale;
            glowColor = color;
        }
        
        void Update()
        {
            time += Time.deltaTime;
            float scale = 1f + Mathf.Sin(time * 2f) * 0.1f;
            transform.localScale = originalScale * scale;
        }
    }
}