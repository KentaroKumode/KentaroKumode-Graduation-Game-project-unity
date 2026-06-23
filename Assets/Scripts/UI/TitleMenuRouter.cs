using UnityEngine;
using GameLoop;

namespace UI
{
    /// <summary>
    /// タイトルメニューのボタン→ゲーム遷移を結ぶ薄い配線層。
    /// SkillTree/Codex は専用 View 側で自己配線するため、 本ルーターは PlayButton(Run 開始)
    /// と「GameOver/RunClear からタイトル復帰」をハンドルする。
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleMenuRouter : MonoBehaviour
    {
        [Header("ボタン配線")]
        [Tooltip("Run を開始するボタン")]
        public SpriteButton playButton;

        [Header("挙動")]
        [Tooltip("PlayButton を押した時、 ゲーム中なら無視する。 二重ラン防止。")]
        public bool ignoreIfNotTitle = true;

        private bool _wired;

        private void OnEnable()
        {
            if (playButton != null && !_wired)
            {
                playButton.Clicked += OnPlayClicked;
                _wired = true;
            }
        }

        private void OnDisable()
        {
            if (playButton != null && _wired)
            {
                playButton.Clicked -= OnPlayClicked;
                _wired = false;
            }
        }

        private void OnPlayClicked()
        {
            var gm = GameManager.Instance;
            Debug.Log($"[TitleMenuRouter] PlayButton clicked. gm={(gm!=null)} phase={(gm!=null?gm.CurrentPhase.ToString():"<no gm>")}");
            if (gm == null) return;
            if (ignoreIfNotTitle && gm.CurrentPhase != GameManager.GamePhase.Title)
            {
                Debug.LogWarning($"[TitleMenuRouter] ignored (phase={gm.CurrentPhase})");
                return;
            }
            gm.StartNewRun();
        }
    }
}
