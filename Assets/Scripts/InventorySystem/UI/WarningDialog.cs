using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InventorySystem
{
    /// <summary>
    /// 汎用警告ダイアログ
    /// </summary>
    public class WarningDialog : MonoBehaviour
    {
        [Header("UI要素")]
        [SerializeField] private GameObject dialogPanel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;
        [SerializeField] private TextMeshProUGUI yesButtonText;
        [SerializeField] private TextMeshProUGUI noButtonText;
        
        private System.Action onYesCallback;
        private System.Action onNoCallback;
        
        void Start()
        {
            if (dialogPanel != null)
            {
                dialogPanel.SetActive(false);
            }
            
            if (yesButton != null)
            {
                yesButton.onClick.AddListener(OnYesClicked);
            }
            
            if (noButton != null)
            {
                noButton.onClick.AddListener(OnNoClicked);
            }
        }
        
        /// <summary>
        /// ダイアログを表示
        /// </summary>
        public void ShowDialog(string title, string message, System.Action onYes, System.Action onNo = null,
                               string yesText = "はい", string noText = "いいえ")
        {
            if (dialogPanel != null)
            {
                dialogPanel.SetActive(true);
            }
            
            // テキスト設定
            if (titleText != null)
                titleText.text = title;
            
            if (messageText != null)
                messageText.text = message;
            
            if (yesButtonText != null)
                yesButtonText.text = yesText;
            
            if (noButtonText != null)
                noButtonText.text = noText;
            
            // コールバック設定
            onYesCallback = onYes;
            onNoCallback = onNo;
            
            Debug.Log($"[WarningDialog] Showing: {title}");
        }
        
        /// <summary>
        /// ダイアログを非表示
        /// </summary>
        public void HideDialog()
        {
            if (dialogPanel != null)
            {
                dialogPanel.SetActive(false);
            }
            
            onYesCallback = null;
            onNoCallback = null;
        }
        
        /// <summary>
        /// 「はい」ボタンクリック
        /// </summary>
        private void OnYesClicked()
        {
            InventorySoundManager.Instance?.PlayUIClick();
            onYesCallback?.Invoke();
            HideDialog();
        }
        
        /// <summary>
        /// 「いいえ」ボタンクリック
        /// </summary>
        private void OnNoClicked()
        {
            InventorySoundManager.Instance?.PlayUIClick();
            onNoCallback?.Invoke();
            HideDialog();
        }
        
        /// <summary>
        /// 装備確認ダイアログ
        /// </summary>
        public void ShowEquipConfirmation(CompleteItemData item, System.Action onConfirm)
        {
            ShowDialog(
                "装備確認",
                $"{item.displayName}を装備しますか？",
                onConfirm,
                null,
                "装備する",
                "キャンセル"
            );
        }
        
        /// <summary>
        /// 使用確認ダイアログ
        /// </summary>
        public void ShowUseConfirmation(CompleteItemData item, System.Action onConfirm)
        {
            ShowDialog(
                "使用確認",
                $"{item.displayName}を使用しますか？",
                onConfirm,
                null,
                "使用する",
                "キャンセル"
            );
        }
        
        /// <summary>
        /// 破棄確認ダイアログ
        /// </summary>
        public void ShowDiscardConfirmation(CompleteItemData item, System.Action onConfirm)
        {
            ShowDialog(
                "破棄確認",
                $"{item.displayName}を破棄しますか？\nこのアイテムは失われます。",
                onConfirm,
                null,
                "破棄する",
                "キャンセル"
            );
        }
        
        /// <summary>
        /// アイテム喪失警告
        /// </summary>
        public void ShowItemLossWarning(System.Action onConfirm, System.Action onCancel)
        {
            ShowDialog(
                "警告",
                "このまま閉じるとアイテムが失われます！\nよろしいですか？",
                onConfirm,
                onCancel,
                "はい",
                "いいえ"
            );
        }
        
        /// <summary>
        /// メモリリーク防止：イベントリスナーの解除
        /// </summary>
        void OnDestroy()
        {
            // イベントリスナーを解除してメモリリークを防止
            if (yesButton != null)
            {
                yesButton.onClick.RemoveAllListeners();
            }
            
            if (noButton != null)
            {
                noButton.onClick.RemoveAllListeners();
            }
            
            // コールバックもクリア
            onYesCallback = null;
            onNoCallback = null;
        }
    }
}
