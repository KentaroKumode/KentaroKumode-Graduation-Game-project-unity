using UnityEngine;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// フォントの互換性チェッカー
    /// カスタムフォントが別のコンピューターで正しく表示されるかチェック
    /// </summary>
    public class FontDistributionChecker : MonoBehaviour
    {
        [Header("フォント設定確認")]
        [SerializeField] private TMP_FontAsset[] usedFontAssets;
        [SerializeField] private bool showDetailedReport = true;
        
        [Header("フォールバック設定")]
        [SerializeField] private TMP_FontAsset fallbackFont; // Google Fontsなど安全なフォント
        [SerializeField] private bool autoSetupFallback = true;
        
        void Start()
        {
            if (Application.isEditor)
            {
                CheckFontDistributionSafety();
                if (autoSetupFallback)
                {
                    SetupFontFallbacks();
                }
            }
        }
        
        /// <summary>
        /// フォントの互換性をチェック
        /// </summary>
        [ContextMenu("Check Font Distribution Safety")]
        public void CheckFontDistributionSafety()
        {
            Debug.Log("=== フォントの互換性チェック開始 ===");
            
            // 使用中のフォントを自動検�E
            if (usedFontAssets == null || usedFontAssets.Length == 0)
            {
                AutoDetectUsedFonts();
            }
            
            foreach (var fontAsset in usedFontAssets)
            {
                if (fontAsset == null) continue;
                
                CheckSingleFont(fontAsset);
            }
            
            Debug.Log("=== チェック完了 ===");
            Debug.Log("推奨: Google Fonts、Adobe Fonts等のオープンライセンス、Unity標準フォントの使用");
        }
        
        /// <summary>
        /// 使用中のフォントを自動検出
        /// </summary>
        private void AutoDetectUsedFonts()
        {
            var allTMPTexts = FindObjectsOfType<TextMeshProUGUI>();
            var all3DTMPTexts = FindObjectsOfType<TextMeshPro>();
            
            System.Collections.Generic.HashSet<TMP_FontAsset> fontSet = 
                new System.Collections.Generic.HashSet<TMP_FontAsset>();
            
            foreach (var tmp in allTMPTexts)
            {
                if (tmp.font != null) fontSet.Add(tmp.font);
            }
            
            foreach (var tmp in all3DTMPTexts)
            {
                if (tmp.font != null) fontSet.Add(tmp.font);
            }
            
            usedFontAssets = new TMP_FontAsset[fontSet.Count];
            fontSet.CopyTo(usedFontAssets);
            
            Debug.Log($"自動検出: {usedFontAssets.Length}個のフォントが見つかりました");
        }
        
        /// <summary>
        /// 個別フォントをチェック
        /// </summary>
        private void CheckSingleFont(TMP_FontAsset fontAsset)
        {
            string fontName = fontAsset.name;
            string sourceFontFile = fontAsset.sourceFontFile != null ? fontAsset.sourceFontFile.name : "Unknown";
            
            Debug.Log($"--- フォント {fontName} ---");
            Debug.Log($"ソースフォント: {sourceFontFile}");
            
            // 安全なフォントの判定
            bool isSafe = IsSafeFont(fontName, sourceFontFile);
            
            if (isSafe)
            {
                Debug.Log($"✓ {fontName} は配布可能です");
            }
            else
            {
                Debug.LogWarning($"⚠️ {fontName} はライセンス確認が必要です");
                Debug.LogWarning("商用利用・再配布可能か確認してください");
            }
            
            if (showDetailedReport)
            {
                ShowDetailedFontInfo(fontAsset);
            }
        }
        
        /// <summary>
        /// フォントが配布可能かチェック
        /// </summary>
        private bool IsSafeFont(string fontName, string sourceFontFile)
        {
            // Unity標準フォント
            string[] unityStandardFonts = { "LegacyRuntime", "Arial" };
            
            // Google Fonts（一般的なもの）
            string[] googleFonts = { 
                "Roboto", "Open Sans", "Lato", "Montserrat", "Oswald", 
                "Source Sans Pro", "Raleway", "PT Sans", "Ubuntu", "Nunito"
            };
            
            // SIL Open Font License フォント
            string[] silFonts = { 
                "Noto Sans", "Noto Serif", "Source Code Pro", "Fira Sans", 
                "Liberation Sans", "DejaVu Sans"
            };
            
            string lowerFontName = fontName.ToLower();
            string lowerSourceName = sourceFontFile.ToLower();
            
            // Unity標準フォントチェック
            foreach (string standardFont in unityStandardFonts)
            {
                if (lowerFontName.Contains(standardFont.ToLower()) || 
                    lowerSourceName.Contains(standardFont.ToLower()))
                {
                    return true;
                }
            }
            
            // Google Fontsチェック
            foreach (string googleFont in googleFonts)
            {
                if (lowerFontName.Contains(googleFont.ToLower().Replace(" ", "")) || 
                    lowerSourceName.Contains(googleFont.ToLower().Replace(" ", "")))
                {
                    return true;
                }
            }
            
            // SILフォントチェック
            foreach (string silFont in silFonts)
            {
                if (lowerFontName.Contains(silFont.ToLower().Replace(" ", "")) || 
                    lowerSourceName.Contains(silFont.ToLower().Replace(" ", "")))
                {
                    return true;
                }
            }
            
            return false; // 不明なフォント、要確認
        }
        
        /// <summary>
        /// フォント詳細情報を表示
        /// </summary>
        private void ShowDetailedFontInfo(TMP_FontAsset fontAsset)
        {
            Debug.Log($"フォント詳細情報:");
            Debug.Log($"  - Atlas Count: {(fontAsset.atlasTextures?.Length ?? 0)}");
            Debug.Log($"  - Character Count: {fontAsset.characterTable?.Count ?? 0}");
            Debug.Log($"  - Face Info: {fontAsset.faceInfo.familyName}");
            Debug.Log($"  - Point Size: {fontAsset.faceInfo.pointSize}");
        }
        
        /// <summary>
        /// フォントフォールバックを設定
        /// </summary>
        private void SetupFontFallbacks()
        {
            if (fallbackFont == null)
            {
                // デフォルトフォールバックを検索
                fallbackFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (fallbackFont == null)
                {
                    Debug.LogWarning("フォールバック用フォントが見つかりません");
                    return;
                }
            }
            
            Debug.Log("フォールバックフォントを設定中...");
            
            // 全てのTextMeshProにフォールバックを設定
            var allTMPTexts = FindObjectsOfType<TextMeshProUGUI>();
            var all3DTMPTexts = FindObjectsOfType<TextMeshPro>();
            
            foreach (var tmp in allTMPTexts)
            {
                SetupFallbackForComponent(tmp);
            }
            
            foreach (var tmp in all3DTMPTexts)
            {
                SetupFallbackForComponent(tmp);
            }
            
            Debug.Log($"フォールバックフォント設定完了: {fallbackFont.name}");
        }
        
        /// <summary>
        /// 個別コンポーネントにフォールバック設定
        /// </summary>
        private void SetupFallbackForComponent(TMP_Text tmpComponent)
        {
            if (tmpComponent.font != null && tmpComponent.font != fallbackFont)
            {
                // フォールバックリストに追加（重複チェック付き）
                var fallbackList = tmpComponent.font.fallbackFontAssetTable;
                if (fallbackList != null && !fallbackList.Contains(fallbackFont))
                {
                    fallbackList.Add(fallbackFont);
                }
            }
        }
        
        /// <summary>
        /// 推奨フォント情報を表示
        /// </summary>
        [ContextMenu("Show Recommended Fonts")]
        public void ShowRecommendedFonts()
        {
            Debug.Log("=== 配布可能な推奨フォント ===");
            Debug.Log("Google Fonts (OFL): https://fonts.google.com/");
            Debug.Log("  - Roboto, Open Sans, Lato, Montserrat など");
            Debug.Log("SIL Open Font License:");
            Debug.Log("  - Noto Sans/Serif, Source Code Pro, Fira Sans など");
            Debug.Log("Unity 標準:");
            Debug.Log("  - Liberation Sans SDF (TextMeshPro標準)");
            Debug.Log("");
            Debug.Log("⚠️ 避けるべき: Windows標準フォント（Meiryo、MS Gothic等）");
            Debug.Log("⚠️ 確認必要: Adobe Fonts、商用フォント");
        }
    }
}
