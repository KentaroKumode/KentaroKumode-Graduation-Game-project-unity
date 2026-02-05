using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// BackGroundPlaneプレハブのファクトリー
    /// プログラムから背景プレーンプレハブを生成する際に使用
    /// </summary>
    public static class BackGroundPlaneFactory
    {
        /// <summary>
        /// 標準的な背景プレーンプレハブを作成
        /// </summary>
        public static GameObject CreateBackgroundPlanePrefab()
        {
            // ベースオブジェクト作成
            GameObject prefab = new GameObject("BackgroundPlanePrefab");
            
            // BackGroundPlaneコンポーネント追加
            BackGroundPlane backgroundPlane = prefab.AddComponent<BackGroundPlane>();
            
            // SpriteRendererは自動で追加されるが、レイヤー設定
            SpriteRenderer spriteRenderer = prefab.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = -10; // 背景として後ろに表示
            }
            
            return prefab;
        }
        
        /// <summary>
        /// カスタム設定での背景プレーンプレハブを作成
        /// </summary>
        public static GameObject CreateBackgroundPlanePrefab(
            Color backgroundColor, 
            Vector3 scale, 
            int sortingOrder = -10)
        {
            GameObject prefab = CreateBackgroundPlanePrefab();
            BackGroundPlane backgroundPlane = prefab.GetComponent<BackGroundPlane>();
            
            // カスタム設定を適用
            backgroundPlane.SetBackgroundColor(backgroundColor);
            backgroundPlane.SetSize(scale);
            
            SpriteRenderer spriteRenderer = prefab.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = sortingOrder;
            }
            
            return prefab;
        }
        
        /// <summary>
        /// シンプルな半透明背景プレーンを作成
        /// </summary>
        public static GameObject CreateSimpleBackgroundPlane()
        {
            return CreateBackgroundPlanePrefab(
                new Color(0.1f, 0.1f, 0.1f, 0.8f),  // 暗いグレーの半透明
                new Vector3(2f, 2f, 1f),             // 2x2サイズ
                -10                                  // 背景レイヤー
            );
        }
        
        /// <summary>
        /// アイテムプレビュー用の背景プレーンを作成
        /// </summary>
        public static GameObject CreateItemPreviewBackground()
        {
            return CreateBackgroundPlanePrefab(
                new Color(0.0f, 0.0f, 0.0f, 0.7f),  // 黒の半透明
                new Vector3(1.5f, 1.5f, 1f),        // やや小さめサイズ
                -5                                   // アイテムより後ろ
            );
        }
    }
}