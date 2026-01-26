using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using InventorySystem;

namespace InventorySystemEditor
{
    /// <summary>
    /// InventorySystemのUI構造を自動生成するエディタツール
    /// メニュー: Tools > Inventory System > Setup UI
    /// </summary>
    public class InventoryUISetup : EditorWindow
    {
        [MenuItem("Tools/Inventory System/Setup UI")]
        static void SetupInventoryUI()
        {
            if (EditorUtility.DisplayDialog("Inventory UI Setup",
                "インベントリUIを自動生成します。\n既存のInventoryUIがある場合は削除されます。",
                "実行", "キャンセル"))
            {
                CreateInventoryUI();
            }
        }

        static void CreateInventoryUI()
        {
            // 既存のInventoryUIを削除
            GameObject existing = GameObject.Find("InventoryUI");
            if (existing != null)
            {
                DestroyImmediate(existing);
            }

            // Canvas作成
            GameObject canvasObj = new GameObject("InventoryUI");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            
            canvasObj.AddComponent<GraphicRaycaster>();

            // EventSystem作成（存在しない場合）
            if (GameObject.Find("EventSystem") == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // 背景パネル
            CreateBackgroundPanel(canvasObj.transform);

            // フィルターパネル
            CreateFilterPanel(canvasObj.transform);

            // グリッドスクロールビュー
            CreateGridScrollView(canvasObj.transform);

            // 警告ダイアログ
            CreateWarningDialog(canvasObj.transform);

            // 詳細ビューパネル
            CreateDetailViewPanel(canvasObj.transform);

            // ツールチップパネル
            CreateTooltipPanel(canvasObj.transform);

            Debug.Log("[InventoryUISetup] UI生成完了！");
            Selection.activeGameObject = canvasObj;
        }

        static GameObject CreateBackgroundPanel(Transform parent)
        {
            GameObject panel = new GameObject("BackgroundPanel");
            panel.transform.SetParent(parent, false);
            
            Image image = panel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.5f);
            
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            
            panel.SetActive(false);
            
            return panel;
        }

        static GameObject CreateFilterPanel(Transform parent)
        {
            GameObject panel = new GameObject("FilterPanel");
            panel.transform.SetParent(parent, false);
            
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, -20);
            rect.sizeDelta = new Vector2(800, 50);
            
            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            FilterPanel filterScript = panel.AddComponent<FilterPanel>();

            // トグル作成
            string[] categories = { "Weapon", "Armor", "Passive", "Material", "Consumable", "Quest" };
            Toggle[] toggles = new Toggle[6];

            for (int i = 0; i < categories.Length; i++)
            {
                GameObject toggleObj = CreateToggle(panel.transform, categories[i]);
                toggles[i] = toggleObj.GetComponent<Toggle>();
            }

            // FilterPanelに参照設定（Reflection使用）
            var weaponField = typeof(FilterPanel).GetField("weaponToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var armorField = typeof(FilterPanel).GetField("armorToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var passiveField = typeof(FilterPanel).GetField("passiveToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var materialField = typeof(FilterPanel).GetField("materialToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var consumableField = typeof(FilterPanel).GetField("consumableToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var questField = typeof(FilterPanel).GetField("questToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            weaponField?.SetValue(filterScript, toggles[0]);
            armorField?.SetValue(filterScript, toggles[1]);
            passiveField?.SetValue(filterScript, toggles[2]);
            materialField?.SetValue(filterScript, toggles[3]);
            consumableField?.SetValue(filterScript, toggles[4]);
            questField?.SetValue(filterScript, toggles[5]);

            return panel;
        }

        static GameObject CreateToggle(Transform parent, string label)
        {
            GameObject toggleObj = new GameObject(label + "Toggle");
            toggleObj.transform.SetParent(parent, false);
            
            Toggle toggle = toggleObj.AddComponent<Toggle>();
            
            RectTransform toggleRect = toggleObj.GetComponent<RectTransform>();
            toggleRect.sizeDelta = new Vector2(120, 40);

            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(toggleObj.transform, false);
            Image bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            // Checkmark
            GameObject check = new GameObject("Checkmark");
            check.transform.SetParent(bg.transform, false);
            Image checkImage = check.AddComponent<Image>();
            checkImage.color = new Color(0.3f, 0.8f, 0.3f, 1f);
            RectTransform checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.sizeDelta = new Vector2(-10, -10);

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(toggleObj.transform, false);
            Text text = labelObj.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            RectTransform textRect = labelObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;

            return toggleObj;
        }

        static GameObject CreateGridScrollView(Transform parent)
        {
            GameObject scrollView = new GameObject("GridScrollView");
            scrollView.transform.SetParent(parent, false);
            
            RectTransform rect = scrollView.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = new Vector2(20, 20);
            rect.offsetMax = new Vector2(-20, -100);

            ScrollRect scroll = scrollView.AddComponent<ScrollRect>();
            
            // Viewport
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollView.transform, false);
            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Mask>();
            viewport.AddComponent<Image>();

            // Content
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 500);

            GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(80, 80);
            grid.spacing = new Vector2(5, 5);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;

            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;

            return scrollView;
        }

        static GameObject CreateWarningDialog(Transform parent)
        {
            GameObject dialog = new GameObject("WarningDialog");
            dialog.transform.SetParent(parent, false);
            
            RectTransform rect = dialog.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400, 200);
            
            Image bg = dialog.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            WarningDialog warningScript = dialog.AddComponent<WarningDialog>();

            // Title
            GameObject title = CreateText(dialog.transform, "TitleText", "警告", 24, TextAnchor.UpperCenter);
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.anchoredPosition = new Vector2(0, -20);
            titleRect.sizeDelta = new Vector2(-20, 40);

            // Message
            GameObject message = CreateText(dialog.transform, "MessageText", "メッセージ", 16, TextAnchor.MiddleCenter);
            RectTransform messageRect = message.GetComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0, 0.3f);
            messageRect.anchorMax = new Vector2(1, 0.7f);
            messageRect.sizeDelta = new Vector2(-40, 0);

            // Yes Button
            GameObject yesBtn = CreateButton(dialog.transform, "YesButton", "はい");
            RectTransform yesBtnRect = yesBtn.GetComponent<RectTransform>();
            yesBtnRect.anchorMin = new Vector2(0.1f, 0.1f);
            yesBtnRect.anchorMax = new Vector2(0.4f, 0.25f);
            yesBtnRect.sizeDelta = Vector2.zero;

            // No Button
            GameObject noBtn = CreateButton(dialog.transform, "NoButton", "いいえ");
            RectTransform noBtnRect = noBtn.GetComponent<RectTransform>();
            noBtnRect.anchorMin = new Vector2(0.6f, 0.1f);
            noBtnRect.anchorMax = new Vector2(0.9f, 0.25f);
            noBtnRect.sizeDelta = Vector2.zero;

            // Reflection設定
            var dialogField = typeof(WarningDialog).GetField("dialogPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var titleField = typeof(WarningDialog).GetField("titleText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var messageField = typeof(WarningDialog).GetField("messageText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var yesField = typeof(WarningDialog).GetField("yesButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var noField = typeof(WarningDialog).GetField("noButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            dialogField?.SetValue(warningScript, dialog);
            titleField?.SetValue(warningScript, title.GetComponent<Text>());
            messageField?.SetValue(warningScript, message.GetComponent<Text>());
            yesField?.SetValue(warningScript, yesBtn.GetComponent<Button>());
            noField?.SetValue(warningScript, noBtn.GetComponent<Button>());

            dialog.SetActive(false);

            return dialog;
        }

        static GameObject CreateDetailViewPanel(Transform parent)
        {
            GameObject panel = new GameObject("DetailViewPanel");
            panel.transform.SetParent(parent, false);
            
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            
            panel.SetActive(false);
            
            return panel;
        }

        static GameObject CreateTooltipPanel(Transform parent)
        {
            GameObject panel = new GameObject("TooltipPanel");
            panel.transform.SetParent(parent, false);
            
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200, 150);
            
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            
            panel.SetActive(false);
            
            return panel;
        }

        static GameObject CreateText(Transform parent, string name, string content, int fontSize, TextAnchor alignment)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);
            
            Text text = textObj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            
            return textObj;
        }

        static GameObject CreateButton(Transform parent, string name, string label)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            
            Button btn = btnObj.AddComponent<Button>();
            Image btnImage = btnObj.AddComponent<Image>();
            btnImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            Text text = textObj.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            return btnObj;
        }
    }
}
