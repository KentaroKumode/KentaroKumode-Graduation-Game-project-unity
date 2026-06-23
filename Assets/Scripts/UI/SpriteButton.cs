using UnityEngine;
using UnityEngine.Events;

namespace UI
{
    /// <summary>
    /// SpriteRenderer 製のボタン（ワールド配置・3D/透視カメラ対応）。
    /// クリックでイベント発火、 **押下中だけ明度を変える**（ホバー演出なし。 localScale は使わない＝PPU=32 スケール禁止に準拠）。
    ///
    /// 前提: 同 GameObject に Collider2D（BoxCollider2D 等）と SpriteRenderer。
    /// 透視カメラでも `ScreenPointToRay`＋`Physics2D.GetRayIntersection` でワールドのスプライトを正しく拾う。
    ///
    /// 使い方: タイトルの PLAY / スキルツリーのスプライトに付け、 BoxCollider2D を絵に合わせる。 onClick に遷移処理を配線。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SpriteButton : MonoBehaviour
    {
        [Header("対象")]
        [Tooltip("明度を変える SpriteRenderer（未指定なら自身/子から取得）")]
        public SpriteRenderer targetRenderer;
        [Tooltip("レイキャストに使うカメラ（未指定なら Camera.main）")]
        public Camera cam;

        [Header("押下中の明度変化")]
        [Tooltip("押下中の明度シフト。 正=白へ寄せて明るく / 負=黒へ寄せて暗く。 ホバーでは変化しない。")]
        [Range(-1f, 1f)] public float pressBrightness = 0.30f;
        [Tooltip("色変化の追従の速さ（0=即時）")]
        [Range(0f, 0.3f)] public float colorSmooth = 0.04f;

        [Header("クリック")]
        public UnityEvent onClick = new UnityEvent();
        /// <summary>コードから購読する用（onClick と併用可）。</summary>
        public event System.Action Clicked;

        /// <summary>現在ポインタが乗っているか（selfRaycast / 外部供給どちらでも）。</summary>
        public bool PointerOver { get; private set; }
        /// <summary>ホバー状態が変化したとき発火（両端カーソル表示など外部購読用）。引数: (自身, 乗ったか)。</summary>
        public event System.Action<SpriteButton, bool> PointerOverChanged;

        [Tooltip("ポーズ中(timeScale=0)でも反応する")]
        public bool useUnscaledTime = true;
        [Tooltip("対象レイヤー（既定=全て）")]
        public LayerMask raycastMask = ~0;

        [Header("入力経路")]
        [Tooltip("true=自前でマウスレイキャスト（画面に直接描く通常用途）。 false=外部(LcdPointer)が SetPointerOver で駆動（液晶内ボタン）。")]
        public bool selfRaycast = true;

        private Collider2D _col;
        private Color _baseColor;
        private bool _pressedOnThis; // このボタン上で押し下げ、 まだ離していない（押下中）
        private bool _externalOver;  // selfRaycast=false のとき LcdPointer から供給されるホバー状態

        /// <summary>外部(LcdPointer)からホバー状態を供給（selfRaycast=false 用）。</summary>
        public void SetPointerOver(bool over) => _externalOver = over;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<SpriteRenderer>();
            if (targetRenderer != null) _baseColor = targetRenderer.color;
        }

        private void OnDisable()
        {
            _pressedOnThis = false;
            if (targetRenderer != null) targetRenderer.color = _baseColor;
        }

        private void Update()
        {
            if (cam == null) cam = Camera.main;
            bool over = selfRaycast ? IsPointerOver() : _externalOver;

            // ホバー状態の変化を通知（両端カーソル等の外部購読用）
            if (over != PointerOver) { PointerOver = over; if (PointerOverChanged != null) PointerOverChanged(this, over); }

            // 押下/離し（ホバーでは何もしない＝明度は押下中のみ変化）
            if (over && Input.GetMouseButtonDown(0)) _pressedOnThis = true;
            if (Input.GetMouseButtonUp(0))
            {
                if (_pressedOnThis && over) Fire();
                _pressedOnThis = false;
            }
            // ボタン外へドラッグで出たら押下状態を解除（明度も戻る）
            if (_pressedOnThis && !over && !Input.GetMouseButton(0)) _pressedOnThis = false;

            ApplyColor(over);
        }

        private bool IsPointerOver()
        {
            if (cam == null || _col == null) return false;
            Vector3 mp = Input.mousePosition;
            if (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height) return false;
            Ray ray = cam.ScreenPointToRay(mp);
            // 透視/正射いずれでも、 ワールドの 2D コライダーをレイ交差で拾う。
            var hit = Physics2D.GetRayIntersection(ray, Mathf.Infinity, raycastMask);
            return hit.collider != null && (hit.collider == _col || hit.collider.transform.IsChildOf(transform));
        }

        private void ApplyColor(bool over)
        {
            if (targetRenderer == null) return;
            Color goal = _baseColor;
            if (_pressedOnThis && over) // 押下中のみ明度変化
            {
                goal = (pressBrightness >= 0f)
                    ? Color.Lerp(_baseColor, Color.white, pressBrightness)   // 明るく
                    : Color.Lerp(_baseColor, Color.black, -pressBrightness); // 暗く
            }

            if (colorSmooth <= 0f)
            {
                targetRenderer.color = goal;
            }
            else
            {
                float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float t = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, colorSmooth));
                targetRenderer.color = Color.Lerp(targetRenderer.color, goal, t);
            }
        }

        private void Fire()
        {
            onClick?.Invoke();
            Clicked?.Invoke();
        }
    }
}
