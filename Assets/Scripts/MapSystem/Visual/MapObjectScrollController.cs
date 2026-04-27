using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップオブジェクトを Y 方向にスクロールする。
    /// 入力はマウスホイールのみ。慣性で滑り、停止時に最寄り row へスナップする。
    /// MapVisualizer と同じオブジェクトにアタッチして利用する。
    /// </summary>
    [RequireComponent(typeof(MapVisualizer))]
    public class MapObjectScrollController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private MapVisualizer mapVisualizer;

        [Header("表示窓(ワールド単位)")]
        [Tooltip("ゲーム機風画面枠の高さ（ワールド単位）。小さいほどスクロール範囲が広がる")]
        [SerializeField] private float viewportHeight = 4f;
        [Tooltip("マップ端を超えてスクロールできる余裕（ワールド単位）。大きいほど自由にスクロール可能")]
        [SerializeField] private float overScrollMargin = 4f;

        [Header("ホイール入力")]
        [Tooltip("ホイール1ノッチで加える速度")]
        [SerializeField] private float wheelImpulse = 12f;
        [Tooltip("速度の上限（ワールド単位/秒）")]
        [SerializeField] private float maxVelocity = 24f;
        [SerializeField] private bool invertWheel = false;

        [Header("慣性")]
        [Tooltip("速度の減衰係数 (大きいほど早く止まる)")]
        [SerializeField] private float damping = 6f;
        [Tooltip("この速度以下になったらスナップ判定に入る")]
        [SerializeField] private float snapVelocityThreshold = 0.5f;

        [Header("行スナップ")]
        [Tooltip("スナップ目標へ近づく速さ")]
        [SerializeField] private float snapLerpSpeed = 14f;
        [Tooltip("この距離以下になったらスナップ完了")]
        [SerializeField] private float snapCompleteEpsilon = 0.01f;

        [Header("自動追従")]
        [SerializeField] private bool followCurrentNode = true;
        [SerializeField] private float followLerpSpeed = 12f;

        private float minOffsetY;
        private float maxOffsetY;
        private bool boundsReady;

        // スクロール状態機械
        private enum ScrollState { Idle, Inertia, Snapping, Following }
        private ScrollState state = ScrollState.Idle;

        private float currentOffsetY;
        private float velocityY;
        private float snapTargetY;

        void Reset()
        {
            mapVisualizer = GetComponent<MapVisualizer>();
        }

        void Awake()
        {
            if (mapVisualizer == null)
                mapVisualizer = GetComponent<MapVisualizer>();
        }

        void OnEnable()
        {
            TrySubscribe();
        }

        void Start()
        {
            // Awake/OnEnable 順序の race で購読を逃した場合に備えて再試行（冪等）。
            TrySubscribe();

            var mm = MapManager.Instance;
            if (mm != null && mm.CurrentMap != null)
            {
                RebuildScrollBounds();
                if (followCurrentNode && mm.CurrentNode != null)
                    BeginFollow(mm.CurrentNode.id, immediate: true);
            }
        }

        void OnDisable()
        {
            var mm = MapManager.Instance;
            if (mm == null) return;
            mm.OnMapGenerated -= OnMapGenerated;
            mm.OnNodeEntered -= OnNodeEntered;
        }

        private void TrySubscribe()
        {
            var mm = MapManager.Instance;
            if (mm == null) return;
            mm.OnMapGenerated -= OnMapGenerated;
            mm.OnNodeEntered -= OnNodeEntered;
            mm.OnMapGenerated += OnMapGenerated;
            mm.OnNodeEntered += OnNodeEntered;
        }

        void Update()
        {
            if (mapVisualizer == null || mapVisualizer.ScrollRoot == null)
                return;

            // 初回起動時、OnMapGenerated イベントが MapVisualizer の BuildMap より
            // 先に呼ばれているとここで bounds が未確立になる。Update で自動回復させる。
            if (!boundsReady)
            {
                if (mapVisualizer.HasMapBounds)
                {
                    RebuildScrollBounds();
                    var mm = MapManager.Instance;
                    if (followCurrentNode && mm != null && mm.CurrentNode != null)
                        BeginFollow(mm.CurrentNode.id, immediate: true);
                }
                if (!boundsReady) return;
            }

            HandleWheelInput();
            TickStateMachine(Time.deltaTime);
            ApplyOffset();
        }

        // ================================================================
        //  入力
        // ================================================================

        private void HandleWheelInput()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) return;

            float sign = invertWheel ? 1f : -1f;
            velocityY = Mathf.Clamp(velocityY + wheel * wheelImpulse * sign, -maxVelocity, maxVelocity);
            state = ScrollState.Inertia;
        }

        // ================================================================
        //  状態遷移
        // ================================================================

        private void TickStateMachine(float dt)
        {
            switch (state)
            {
                case ScrollState.Inertia:
                    TickInertia(dt);
                    break;
                case ScrollState.Snapping:
                    TickSnapping(dt);
                    break;
                case ScrollState.Following:
                    TickFollowing(dt);
                    break;
                case ScrollState.Idle:
                default:
                    break;
            }
        }

        private void TickInertia(float dt)
        {
            currentOffsetY += velocityY * dt;

            // 範囲外に出たら速度を吸収して即停止 (バウンス無し)
            if (currentOffsetY < minOffsetY)
            {
                currentOffsetY = minOffsetY;
                velocityY = 0f;
            }
            else if (currentOffsetY > maxOffsetY)
            {
                currentOffsetY = maxOffsetY;
                velocityY = 0f;
            }

            // 指数減衰
            velocityY = Mathf.Lerp(velocityY, 0f, 1f - Mathf.Exp(-damping * dt));

            if (Mathf.Abs(velocityY) <= snapVelocityThreshold)
                EnterSnap();
        }

        private void EnterSnap()
        {
            snapTargetY = ComputeNearestRowOffset(currentOffsetY);
            velocityY = 0f;
            state = ScrollState.Snapping;
        }

        private void TickSnapping(float dt)
        {
            currentOffsetY = Mathf.Lerp(currentOffsetY, snapTargetY, 1f - Mathf.Exp(-snapLerpSpeed * dt));
            if (Mathf.Abs(currentOffsetY - snapTargetY) <= snapCompleteEpsilon)
            {
                currentOffsetY = snapTargetY;
                state = ScrollState.Idle;
            }
        }

        private void TickFollowing(float dt)
        {
            currentOffsetY = Mathf.Lerp(currentOffsetY, snapTargetY, 1f - Mathf.Exp(-followLerpSpeed * dt));
            if (Mathf.Abs(currentOffsetY - snapTargetY) <= snapCompleteEpsilon)
            {
                currentOffsetY = snapTargetY;
                state = ScrollState.Idle;
            }
        }

        // ================================================================
        //  スナップ計算
        // ================================================================

        /// <summary>
        /// 現在のオフセットから、最も近い「行を中央に置けるオフセット」を計算する。
        /// ScrollRoot の localPosition.y は -nodeWorldY と対応する仕組み。
        /// </summary>
        private float ComputeNearestRowOffset(float offsetY)
        {
            float rowSpacing = (mapVisualizer != null) ? mapVisualizer.RowSpacing : 1f;
            if (rowSpacing <= 0f) return Mathf.Clamp(offsetY, minOffsetY, maxOffsetY);

            // offset = -nodeY なので、最寄り行のY = round(-offset / rowSpacing) * rowSpacing
            float nearestRowY = Mathf.Round(-offsetY / rowSpacing) * rowSpacing;
            float candidate = -nearestRowY;
            return Mathf.Clamp(candidate, minOffsetY, maxOffsetY);
        }

        // ================================================================
        //  イベント
        // ================================================================

        private void OnMapGenerated(FloorMap _)
        {
            RebuildScrollBounds();

            var mm = MapManager.Instance;
            if (followCurrentNode && mm != null && mm.CurrentNode != null)
                BeginFollow(mm.CurrentNode.id, immediate: true);
        }

        private void OnNodeEntered(MapNode node)
        {
            if (!followCurrentNode || node == null) return;
            BeginFollow(node.id, immediate: false);
        }

        private void BeginFollow(string nodeId, bool immediate)
        {
            if (!boundsReady) return;
            if (!mapVisualizer.TryGetNodeMapLocalY(nodeId, out float nodeY)) return;

            snapTargetY = Mathf.Clamp(-nodeY, minOffsetY, maxOffsetY);
            velocityY = 0f;

            if (immediate)
            {
                currentOffsetY = snapTargetY;
                state = ScrollState.Idle;
            }
            else
            {
                state = ScrollState.Following;
            }
        }

        // ================================================================
        //  範囲計算
        // ================================================================

        private void RebuildScrollBounds()
        {
            if (mapVisualizer == null || !mapVisualizer.HasMapBounds || mapVisualizer.ScrollRoot == null)
            {
                boundsReady = false;
                return;
            }

            var bounds = mapVisualizer.MapBounds;
            float half = viewportHeight * 0.5f;

            // overScrollMargin の分だけマップ端を超えて移動可能にする
            float centerMin = bounds.min.y + half - overScrollMargin;
            float centerMax = bounds.max.y - half + overScrollMargin;
            if (centerMax < centerMin)
            {
                float mid = (bounds.min.y + bounds.max.y) * 0.5f;
                centerMin = centerMax = mid;
            }

            // ScrollRoot を逆方向へ動かしてカメラを動かしたかのように見せる。
            minOffsetY = -centerMax;
            maxOffsetY = -centerMin;

            currentOffsetY = Mathf.Clamp(maxOffsetY, minOffsetY, maxOffsetY);
            snapTargetY = currentOffsetY;
            velocityY = 0f;
            state = ScrollState.Idle;
            ApplyOffset();
            boundsReady = true;
        }

        private void ApplyOffset()
        {
            if (mapVisualizer == null || mapVisualizer.ScrollRoot == null) return;
            var p = mapVisualizer.ScrollRoot.localPosition;
            p.y = currentOffsetY;
            mapVisualizer.ScrollRoot.localPosition = p;
        }
    }
}
