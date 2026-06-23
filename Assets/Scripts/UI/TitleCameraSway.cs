using UnityEngine;

namespace UI
{
    /// <summary>
    /// タイトル画面のマウス疑似パララックス（カメラ揺らし版）。
    ///
    /// 世界の中に置かれた“ゲーム機の画面”として SpriteRenderer を 3D 配置し、 **透視カメラを少し揺らす**ことで
    /// 奥行き（モーションパララックス）を出す。 レイヤーを Z で離すほど近景が大きくズレ、 立体感が増す。
    /// 各スプライトはワールドに固定されたまま（カメラの子にしない）＝“カメラに張り付く”感じにならない。
    ///
    /// 使い方:
    ///   1) タイトル用カメラを Projection=Perspective にする。
    ///   2) 背景は遠く(例 z=12)、 タイトル/ボタンは近く(例 z=4) に置き、 重なりは sortingOrder で担保。
    ///   3) このコンポーネントをそのカメラ（または カメラを子に持つリグ）に付ける。
    ///   4) swayPosition を調整（大きいほど視差が強い）。 回転は控えめ推奨（ピクセルのにじみ防止）。
    ///
    /// 注意: localScale は使わない（PPU=32 ピクセル要素のスケール禁止に準拠）。 位置・回転のみ。
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleCameraSway : MonoBehaviour
    {
        [Header("並進の揺らし（ワールド単位・主役）")]
        [Tooltip("マウス端でのカメラ横/縦移動量。 大きいほど視差が強い。")]
        public Vector2 swayPosition = new Vector2(0.6f, 0.35f);

        [Header("回転の揺らし（度・控えめ推奨／0でピクセル最維持）")]
        [Tooltip("マウス端でのカメラの傾き（度）。 横マウス→Y回転、 縦マウス→X回転。 0〜2度程度。")]
        public Vector2 swayRotationDeg = new Vector2(1.2f, 0.8f);

        [Header("挙動")]
        [Range(0.02f, 0.6f)] public float smoothTime = 0.14f;
        [Tooltip("ポーズ中(timeScale=0)でも動かす")]
        public bool useUnscaledTime = true;
        [Tooltip("ウィンドウ外/未入力時は中央へ戻す")]
        public bool recenterWhenNoInput = true;

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private Vector3 _posVel;
        // 回転は SmoothDamp 用にオイラー差分を保持
        private Vector2 _rotCur, _rotVel;

        private void Awake()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
        }

        private void OnDisable()
        {
            transform.localPosition = _basePos;
            transform.localRotation = _baseRot;
            _posVel = Vector3.zero; _rotCur = Vector2.zero; _rotVel = Vector2.zero;
        }

        private void LateUpdate()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 m = ReadMouseNormalized(); // -1〜1（中央=0）

            // 並進: マウス方向へ。 (覗き込む向き＝カメラはマウスと同方向に寄る)
            Vector3 goalPos = _basePos + new Vector3(m.x * swayPosition.x, m.y * swayPosition.y, 0f);
            transform.localPosition = Vector3.SmoothDamp(transform.localPosition, goalPos, ref _posVel, smoothTime, Mathf.Infinity, dt);

            // 回転: 横マウス→Y軸、 縦マウス→X軸（縦は符号反転で自然な見上げ/見下げ）
            Vector2 goalRot = new Vector2(-m.y * swayRotationDeg.y, m.x * swayRotationDeg.x);
            _rotCur.x = Mathf.SmoothDamp(_rotCur.x, goalRot.x, ref _rotVel.x, smoothTime, Mathf.Infinity, dt);
            _rotCur.y = Mathf.SmoothDamp(_rotCur.y, goalRot.y, ref _rotVel.y, smoothTime, Mathf.Infinity, dt);
            transform.localRotation = _baseRot * Quaternion.Euler(_rotCur.x, _rotCur.y, 0f);
        }

        private Vector2 ReadMouseNormalized()
        {
            Vector3 mp = Input.mousePosition;
            if (recenterWhenNoInput &&
                (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height))
                return Vector2.zero;
            float nx = (Screen.width  > 0) ? (mp.x / Screen.width  * 2f - 1f) : 0f;
            float ny = (Screen.height > 0) ? (mp.y / Screen.height * 2f - 1f) : 0f;
            return new Vector2(Mathf.Clamp(nx, -1f, 1f), Mathf.Clamp(ny, -1f, 1f));
        }
    }
}
