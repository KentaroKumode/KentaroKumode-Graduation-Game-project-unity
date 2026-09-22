using UnityEngine;
using GameLoop;

namespace UI
{
    /// <summary>
    /// タイトルメニューのボタン→ゲーム遷移を結ぶ薄い配線層。
    /// SkillTree/Codex は専用 View 側で自己配線するため、 本ルーターは PlayButton(Run 開始)
    /// と「GameOver/RunClear からタイトル復帰」をハンドルする。
    ///
    /// 2026-07-07: classSelect が配線されている場合、 PLAY → 職業選択 (Papers, Please 風) →
    /// 受理スタンプ → StartNewRun の順に挟む。 未配線なら従来通り即ラン開始。
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleMenuRouter : MonoBehaviour
    {
        [Header("ボタン配線")]
        [Tooltip("Run を開始するボタン")]
        public SpriteButton playButton;

        [Header("職業選択 (任意)")]
        [Tooltip("配線すると PLAY → 職業選択 → ラン開始 になる。 null なら即ラン開始。")]
        public ClassSelect.ClassSelectView classSelect;

        [Header("挙動")]
        [Tooltip("PlayButton を押した時、 ゲーム中なら無視する。 二重ラン防止。")]
        public bool ignoreIfNotTitle = true;

        private bool _classSelectOpen;

        private void OnEnable() => Rewire();

        /// <summary>参照を代入し直した後に呼ぶと購読を張り直す (AddComponent → フィールド代入の順でも配線が生きる)。
        /// 解除→購読の順で行うため二重購読しない。 冪等。</summary>
        public void Rewire()
        {
            if (playButton != null)
            {
                playButton.Clicked -= OnPlayClicked;
                playButton.Clicked += OnPlayClicked;
            }
            if (classSelect != null)
            {
                classSelect.Decided -= OnClassDecided;
                classSelect.Decided += OnClassDecided;
            }
        }

        private void OnDisable()
        {
            if (playButton != null)
                playButton.Clicked -= OnPlayClicked;
            if (classSelect != null)
                classSelect.Decided -= OnClassDecided;
            _classSelectOpen = false;
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

            // 職業選択が配線されていれば、 ラン開始前に提示 (マップ侵入は受理スタンプ後)
            if (classSelect != null)
            {
                if (_classSelectOpen) return; // 表示中の再クリックは無視
                _classSelectOpen = true;
                Debug.Log($"[TitleMenuRouter] 職業選択を表示 (view={classSelect.name}, rigCam={(classSelect.rigCamera != null ? classSelect.rigCamera.name : "<null>")})");
                classSelect.Show();
                return;
            }

            Debug.LogWarning("[TitleMenuRouter] classSelect 未配線 → 職業選択をスキップして直接ラン開始");
            gm.StartNewRun();
        }

        /// <summary>職業選択の受理スタンプ確定 → UI を畳んでラン開始 (SelectedClass は View 側で設定済み)。</summary>
        private void OnClassDecided(ClassType cls)
        {
            if (!_classSelectOpen) return;
            _classSelectOpen = false;
            classSelect.Hide();

            var gm = GameManager.Instance;
            if (gm == null) return;
            Debug.Log($"[TitleMenuRouter] 職業 {cls} で ラン開始");
            gm.StartNewRun();
        }
    }
}
