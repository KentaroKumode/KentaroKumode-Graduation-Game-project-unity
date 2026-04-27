using System.Collections.Generic;
using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップのビジュアル表現を生成・管理する。
    /// ノード位置にジッターを加え、パスラインにベジェ揺れを入れて手書きボードゲーム感を出す。
    /// MapManager.OnMapGenerated / OnNodeEntered / OnMysteryResolved を購読する。
    /// </summary>
    public class MapVisualizer : MonoBehaviour
    {
        [Header("レイアウト")]
        [SerializeField] private float laneSpacing = 3.0f;
        [SerializeField] private float rowSpacing = 3.0f;
        [SerializeField] private float positionJitter = 0.5f;

        [Header("ライン (ドット配置 — pixel perfect)")]
        [Tooltip("ベジェ計算用のサンプル点数（多いほど滑らかな曲線）")]
        [SerializeField] private int curveResolution = 32;
        [SerializeField] private float curveSway = 0.4f;
        [Tooltip("ノードの端から線を始めるためのインセット（≒ノード半径）")]
        [SerializeField] private float nodeEdgeInset = 0.5f;
        [Tooltip("各ドットの一辺ピクセル数（正方形・軸アライン）")]
        [SerializeField] private int pathDotSizePx = 2;
        [Tooltip("ダッシュ1本のピクセル長さ。size と同じ値ならドット1個、大きいほど連続ドットで線分風に")]
        [SerializeField] private int pathDashLengthPx = 2;
        [Tooltip("ダッシュ間の空白ピクセル数")]
        [SerializeField] private int pathGapLengthPx = 6;
        [Tooltip("PPU (ノード/ドット解像度の基準。通常32)")]
        [SerializeField] private int pixelsPerUnit = 32;

        [Header("色")]
        [SerializeField] private Color lineDefault = new Color(0.5f, 0.45f, 0.35f, 0.6f);
        [SerializeField] private Color lineVisited = new Color(0.9f, 0.8f, 0.5f, 1f);
        [SerializeField] private Color lineReachable = new Color(1f, 1f, 1f, 0.9f);

        [Header("ノードPrefab")]
        [SerializeField] private GameObject defaultNodePrefab;
        [SerializeField] private float nodeScale = 1.0f;

        [Header("マップ背景")]
        [Tooltip("マップの下に敷くスプライト。フロア共通。null なら背景なし")]
        [SerializeField] private Sprite mapBackgroundSprite;
        [Tooltip("ノード配置領域の外側にどれだけ広げるか (ワールド単位)")]
        [SerializeField] private float backgroundPadding = 1.0f;
        [Tooltip("背景の sortingOrder。ノード/ドットより小さく")]
        [SerializeField] private int backgroundSortingOrder = -10;
        [Tooltip("ピクセル境界に揃えるか (PPU=32 なら推奨ON)")]
        [SerializeField] private bool snapBackgroundToPixel = true;

        [Header("ピクセルパーフェクト")]
        [SerializeField] private TileIconAtlas tileIconAtlas;
        [SerializeField] private bool enablePixelCameraRender = false;
        [SerializeField] private PixelPerfectMapCamera pixelCamera;

        // === 内部 ===
        private Transform scrollRoot;
        private Transform nodeContainer;
        private Transform lineContainer;
        private Transform backgroundContainer;

        private Dictionary<string, MapNodeVisual> nodeVisuals = new Dictionary<string, MapNodeVisual>();
        private List<MapLineVisual> lineVisuals = new List<MapLineVisual>();
        private Dictionary<string, Vector3> worldPositions = new Dictionary<string, Vector3>();

        private FloorMap currentMap;
        private int layoutSeed;
        private Bounds mapBounds;

        public Transform ScrollRoot => scrollRoot;
        public Bounds MapBounds => mapBounds;
        public bool HasMapBounds => worldPositions.Count > 0;
        public float RowSpacing => rowSpacing;

        void Awake()
        {
            TrySubscribe();
        }

        void Start()
        {
            // Awake 順序で購読を逃した可能性があるため、Start でも再試行する。
            // Subscribe は重複登録しない実装になっているので冪等。
            TrySubscribe();

            // すでにマップが生成済みなら即時描画（イベントを逃したケース対策）
            var mm = MapManager.Instance;
            if (mm != null && mm.CurrentMap != null)
                OnMapGenerated(mm.CurrentMap);
        }

        void OnDestroy()
        {
            var mm = MapManager.Instance;
            if (mm == null) return;
            mm.OnMapGenerated -= OnMapGenerated;
            mm.OnNodeEntered -= OnNodeEntered;
            mm.OnMysteryResolved -= OnMysteryResolved;
        }

        /// <summary>MapManager のイベントに登録する（重複登録しない）。</summary>
        private void TrySubscribe()
        {
            var mm = MapManager.Instance;
            if (mm == null) return;
            mm.OnMapGenerated -= OnMapGenerated;
            mm.OnNodeEntered -= OnNodeEntered;
            mm.OnMysteryResolved -= OnMysteryResolved;
            mm.OnMapGenerated += OnMapGenerated;
            mm.OnNodeEntered += OnNodeEntered;
            mm.OnMysteryResolved += OnMysteryResolved;
        }

        // ================================================================
        //  イベントハンドラ
        // ================================================================

        private void OnMapGenerated(FloorMap map)
        {
            ClearVisuals();
            currentMap = map;
            layoutSeed = Random.Range(0, 99999);
            BuildMap(map);
            RefreshVisualStates();
        }

        private void OnNodeEntered(MapNode node)
        {
            RefreshVisualStates();
        }

        private void OnMysteryResolved(MapNode node, TileType resolved)
        {
            if (nodeVisuals.TryGetValue(node.id, out var visual))
                visual.SetTileType(resolved);
        }

        // ================================================================
        //  マップ構築
        // ================================================================

        private void BuildMap(FloorMap map)
        {
            EnsureContainers();
            ComputeWorldPositions(map);
            SnapPositionsToPixelGrid();
            CacheMapBounds();
            SpawnBackground();
            SpawnNodes(map);
            SpawnLines(map);
            FitPixelCamera();
        }

        private void ComputeWorldPositions(FloorMap map)
        {
            worldPositions.Clear();
            float centerLane = (map.laneCount - 1) / 2f;
            var rng = new System.Random(layoutSeed);

            foreach (var node in map.GetAllNodes())
            {
                float x, y;

                if (node.lane == -1)
                {
                    // 収束ノード（前哨基地/ボス）— 中央、ジッター小さめ
                    x = centerLane * laneSpacing;
                    // Y軸を反転: 行0(前哨基地) を下、行N(ボス) を上に表示
                    y = -node.row * rowSpacing;
                    x += Jitter(rng, positionJitter * 0.3f);
                    y += Jitter(rng, positionJitter * 0.3f);
                }
                else
                {
                    x = node.lane * laneSpacing;
                    y = -node.row * rowSpacing;
                    x += Jitter(rng, positionJitter);
                    y += Jitter(rng, positionJitter);
                }

                worldPositions[node.id] = new Vector3(x, y, 0f);
            }
        }

        private void SpawnNodes(FloorMap map)
        {
            foreach (var node in map.GetAllNodes())
            {
                if (!worldPositions.TryGetValue(node.id, out var pos)) continue;

                GameObject go = defaultNodePrefab != null
                    ? Instantiate(defaultNodePrefab, nodeContainer)
                    : CreateFallbackNode();

                // 親 (MapTestRoot 等) を寝かせ回転している場合でも、ノードが
                // 親のローカル座標系に正しく配置されるよう localPosition で設定する。
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.identity;

                go.name = $"Node_{node.id}";
                go.transform.localScale = Vector3.one * nodeScale;

                var visual = go.GetComponent<MapNodeVisual>();
                if (visual == null) visual = go.AddComponent<MapNodeVisual>();

                visual.Initialize(node, tileIconAtlas);
                nodeVisuals[node.id] = visual;
            }
        }

        private void SpawnLines(FloorMap map)
        {
            var drawnPairs = new HashSet<string>();

            foreach (var node in map.GetAllNodes())
            {
                if (!worldPositions.TryGetValue(node.id, out var fromPos)) continue;

                foreach (var connId in node.connections)
                {
                    // 双方向の重複防止
                    string pairKey = string.Compare(node.id, connId) < 0
                        ? $"{node.id}>{connId}" : $"{connId}>{node.id}";
                    if (drawnPairs.Contains(pairKey)) continue;
                    drawnPairs.Add(pairKey);

                    if (!worldPositions.TryGetValue(connId, out var toPos)) continue;

                    var target = map.GetNode(connId);
                    bool isLateral = (target != null && target.row == node.row);

                    var pathGo = new GameObject($"Path_{node.id}_{connId}");
                    pathGo.transform.SetParent(lineContainer, false);

                    // ベジェ曲線サンプリング
                    var rng = new System.Random(pairKey.GetHashCode() ^ layoutSeed);
                    var points = GenerateBezierPath(fromPos, toPos, isLateral, rng);

                    // 曲線に沿ってドットを等弧長間隔で配置
                    var dots = SpawnPathDots(pathGo.transform, points);

                    var lineVisual = pathGo.AddComponent<MapLineVisual>();
                    lineVisual.InitializeDots(node.id, connId, dots, isLateral);
                    lineVisuals.Add(lineVisual);
                }
            }
        }

        // ================================================================
        //  状態更新
        // ================================================================

        private void RefreshVisualStates()
        {
            var mm = MapManager.Instance;
            if (mm == null || currentMap == null) return;

            var currentNode = mm.CurrentNode;
            var reachableIds = new HashSet<string>();
            if (currentNode != null)
            {
                foreach (var r in currentMap.GetReachableFrom(currentNode.id))
                    reachableIds.Add(r.id);
            }

            // ノード状態更新
            foreach (var kvp in nodeVisuals)
            {
                var node = currentMap.GetNode(kvp.Key);
                if (node == null) continue;

                var state = NodeVisualState.Default;
                if (node == currentNode)
                    state = NodeVisualState.Current;
                else if (reachableIds.Contains(node.id))
                    state = NodeVisualState.Reachable;
                else if (node.visited)
                    state = NodeVisualState.Visited;

                kvp.Value.SetState(state);
            }

            // ライン状態更新
            foreach (var line in lineVisuals)
            {
                var fromNode = currentMap.GetNode(line.FromId);
                var toNode = currentMap.GetNode(line.ToId);
                if (fromNode == null || toNode == null) continue;

                Color color = lineDefault;
                if (fromNode.visited && toNode.visited)
                    color = lineVisited;
                else if ((fromNode == currentNode && reachableIds.Contains(toNode.id)) ||
                         (toNode == currentNode && reachableIds.Contains(fromNode.id)))
                    color = lineReachable;

                line.SetColor(color);
            }
        }

        // ================================================================
        //  ベジェ曲線生成
        // ================================================================

        /// <summary>
        /// 2点間のベジェ曲線を生成。
        /// 通常接続: 三次ベジェのS字カーブ（変曲点を1つ持つ三次関数グラフのような形）。
        /// 横接続: 単純な弧（下に膨らむ）。
        /// </summary>
        private Vector3[] GenerateBezierPath(Vector3 from, Vector3 to, bool isLateral, System.Random rng)
        {
            var points = new Vector3[curveResolution];

            // ノードの中心ではなく端から線を始めるため、両端をインセット
            Vector3 fullDir = to - from;
            float fullLen = fullDir.magnitude;
            float inset = Mathf.Min(nodeEdgeInset, fullLen * 0.45f); // 線が消えないようにクランプ
            if (fullLen > 1e-4f)
            {
                Vector3 unit = fullDir / fullLen;
                from = from + unit * inset;
                to = to - unit * inset;
            }

            Vector3 direction = to - from;

            if (isLateral)
            {
                // 横接続: 中点を下にふくらませた2次ベジェ
                Vector3 mid = (from + to) * 0.5f;
                float sway = -Mathf.Abs(curveSway) * (0.5f + (float)rng.NextDouble() * 0.5f);
                Vector3 control = mid + Vector3.down * sway;

                for (int i = 0; i < curveResolution; i++)
                {
                    float t = i / (float)(curveResolution - 1);
                    float u = 1f - t;
                    points[i] = u * u * from + 2f * u * t * control + t * t * to;
                }
                return points;
            }

            // 通常接続: 三次ベジェのS字カーブ
            // 制御点P1を1/3地点で正側、P2を2/3地点で負側に振る → 変曲点を持つ
            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f).normalized;
            // 線ごとにS字の向き(±)をランダムにし、振幅もランダム化
            float sign = (rng.NextDouble() < 0.5) ? 1f : -1f;
            float amp = curveSway * (0.7f + (float)rng.NextDouble() * 0.3f); // 0.7〜1.0倍

            Vector3 p1 = from + direction * (1f / 3f) + perpendicular * (sign * amp);
            Vector3 p2 = from + direction * (2f / 3f) + perpendicular * (-sign * amp);

            for (int i = 0; i < curveResolution; i++)
            {
                float t = i / (float)(curveResolution - 1);
                float u = 1f - t;
                // 三次ベジェ: B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
                points[i] = u * u * u * from
                          + 3f * u * u * t * p1
                          + 3f * u * t * t * p2
                          + t * t * t * to;
            }

            return points;
        }

        // ================================================================
        //  ユーティリティ
        // ================================================================

        // ================================================================
        //  ドット配置 (パス描画)
        // ================================================================

        /// <summary>
        /// ベジェ曲線に沿って、軸アラインの正方形ドットでダッシュ列を作る。
        /// 回転を一切使わないため、どの方向のパスでも完全にピクセル化(アンチエイリアスなし)を保つ。
        /// 「長さ」は連続ドットの個数で表現する。
        /// </summary>
        private List<SpriteRenderer> SpawnPathDots(Transform parent, Vector3[] points)
        {
            var result = new List<SpriteRenderer>();
            if (points == null || points.Length < 2) return result;

            // 累積弧長を計算
            var cumLen = new float[points.Length];
            cumLen[0] = 0f;
            for (int i = 1; i < points.Length; i++)
                cumLen[i] = cumLen[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            float totalLen = cumLen[points.Length - 1];
            if (totalLen < 1e-4f) return result;

            float pixelUnit = 1f / Mathf.Max(1, pixelsPerUnit);
            int dotSize = Mathf.Max(1, pathDotSizePx);
            int dashLen = Mathf.Max(dotSize, pathDashLengthPx);
            int gapLen = Mathf.Max(0, pathGapLengthPx);

            // ダッシュ内ではドット中心を size px ごとに配置 (正方形が隣接して連続線になる)
            float stepUnit = dotSize * pixelUnit;
            int dotsPerDash = Mathf.Max(1, dashLen / dotSize);
            float dashUnit = dotsPerDash * stepUnit;
            float gapUnit = gapLen * pixelUnit;
            float periodUnit = dashUnit + gapUnit;
            if (periodUnit < 1e-6f) periodUnit = stepUnit;

            float dotWorldSize = dotSize * pixelUnit;

            int idx = 0;
            for (float arc = 0f; arc <= totalLen; arc += stepUnit)
            {
                float posInPeriod = arc % periodUnit;
                // ダッシュ部分 (0 .. dashUnit) の時のみドットを配置
                if (posInPeriod >= dashUnit) continue;

                Vector3 pos = SampleByArcLength(points, cumLen, arc);
                pos = SnapToPixel(pos, pixelUnit);

                var dotGo = new GameObject($"Dot_{idx:000}");
                dotGo.transform.SetParent(parent, false);
                dotGo.transform.localPosition = pos;
                dotGo.transform.localRotation = Quaternion.identity;
                // 軸アライン正方形 (回転なし)
                dotGo.transform.localScale = new Vector3(dotWorldSize, dotWorldSize, 1f);

                var dsr = dotGo.AddComponent<SpriteRenderer>();
                dsr.sprite = GetOrCreatePathDotSprite();
                dsr.color = lineDefault;
                dsr.sortingOrder = -1;

                result.Add(dsr);
                idx++;
            }

            return result;
        }

        /// <summary>累積弧長配列から指定弧長位置を線形補間で取得</summary>
        private static Vector3 SampleByArcLength(Vector3[] points, float[] cumLen, float target)
        {
            int n = points.Length;
            if (target <= 0f) return points[0];
            if (target >= cumLen[n - 1]) return points[n - 1];

            for (int i = 1; i < n; i++)
            {
                if (cumLen[i] >= target)
                {
                    float seg = cumLen[i] - cumLen[i - 1];
                    if (seg < 1e-6f) return points[i - 1];
                    float u = (target - cumLen[i - 1]) / seg;
                    return Vector3.Lerp(points[i - 1], points[i], u);
                }
            }
            return points[n - 1];
        }

        private static Vector3 SnapToPixel(Vector3 v, float pixelUnit)
        {
            return new Vector3(
                Mathf.Round(v.x / pixelUnit) * pixelUnit,
                Mathf.Round(v.y / pixelUnit) * pixelUnit,
                Mathf.Round(v.z / pixelUnit) * pixelUnit);
        }

        // 1×1 px の白スプライトを共有
        private static Sprite pathDotSprite;
        private Sprite GetOrCreatePathDotSprite()
        {
            if (pathDotSprite != null) return pathDotSprite;
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // PPU=1 で1unit=1px相当。実サイズは transform.localScale で制御する。
            pathDotSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            pathDotSprite.name = "PathDotSprite";
            return pathDotSprite;
        }

        private float Jitter(System.Random rng, float range)
        {
            return (float)(rng.NextDouble() * 2.0 - 1.0) * range;
        }

        /// <summary>全ノード位置をピクセルグリッドにスナップ</summary>
        private void SnapPositionsToPixelGrid()
        {
            if (pixelCamera == null) return;
            var keys = new List<string>(worldPositions.Keys);
            foreach (var key in keys)
                worldPositions[key] = pixelCamera.SnapToPixel(worldPositions[key]);
        }

        /// <summary>マップ範囲を計算してピクセルカメラをフィット</summary>
        private void FitPixelCamera()
        {
            if (!enablePixelCameraRender || pixelCamera == null || worldPositions.Count == 0) return;
            pixelCamera.SetupForMap(mapBounds);
        }

        public bool TryGetNodeMapLocalY(string nodeId, out float y)
        {
            if (worldPositions.TryGetValue(nodeId, out var pos))
            {
                y = pos.y;
                return true;
            }

            y = 0f;
            return false;
        }

        private void EnsureContainers()
        {
            if (scrollRoot == null)
            {
                var root = new GameObject("MapScrollRoot");
                root.transform.SetParent(transform, false);
                scrollRoot = root.transform;
            }

            // 背景は最初に作成して最背面にする
            if (backgroundContainer == null)
            {
                var go = new GameObject("BackgroundContainer");
                go.transform.SetParent(scrollRoot, false);
                backgroundContainer = go.transform;
            }

            if (nodeContainer == null)
            {
                var go = new GameObject("NodeContainer");
                go.transform.SetParent(scrollRoot, false);
                nodeContainer = go.transform;
            }

            if (lineContainer == null)
            {
                var go = new GameObject("LineContainer");
                go.transform.SetParent(scrollRoot, false);
                lineContainer = go.transform;
            }
        }

        /// <summary>マップ背景スプライトを SerializeField から自動生成。</summary>
        private void SpawnBackground()
        {
            if (mapBackgroundSprite == null) return;
            if (backgroundContainer == null) return;

            // 背景の中心とサイズ (worldPositions と同じローカル空間で計算)
            Vector3 center = mapBounds.center;
            float w = mapBounds.size.x + backgroundPadding * 2f;
            float h = mapBounds.size.y + backgroundPadding * 2f;

            if (snapBackgroundToPixel)
            {
                float pixelUnit = 1f / Mathf.Max(1, pixelsPerUnit);
                center = SnapToPixel(center, pixelUnit);
                w = Mathf.Round(w / pixelUnit) * pixelUnit;
                h = Mathf.Round(h / pixelUnit) * pixelUnit;
            }

            var bgGo = new GameObject("MapBackground");
            bgGo.transform.SetParent(backgroundContainer, false);
            // ノードより少し奥 (寝かせ後の世界 -Y 方向 = 地中側)
            bgGo.transform.localPosition = new Vector3(center.x, center.y, 0.01f);
            bgGo.transform.localRotation = Quaternion.identity;

            var sr = bgGo.AddComponent<SpriteRenderer>();
            sr.sprite = mapBackgroundSprite;
            sr.flipY = true; // 親の寝かせ回転 (X=-90) に合わせる
            sr.sortingOrder = backgroundSortingOrder;
            sr.drawMode = SpriteDrawMode.Sliced; // size をワールド単位で指定可能に
            sr.size = new Vector2(w, h);

            if (sr.sprite.texture != null)
                sr.sprite.texture.filterMode = FilterMode.Point;
        }

        /// <summary>戦闘・イベント中などにマップ全体を非表示にする/再表示する。</summary>
        public void SetMapVisible(bool visible)
        {
            if (scrollRoot != null) scrollRoot.gameObject.SetActive(visible);
        }

        private void CacheMapBounds()
        {
            if (worldPositions.Count == 0)
            {
                mapBounds = default;
                return;
            }

            var enumerator = worldPositions.Values.GetEnumerator();
            enumerator.MoveNext();
            mapBounds = new Bounds(enumerator.Current, Vector3.zero);
            while (enumerator.MoveNext())
                mapBounds.Encapsulate(enumerator.Current);
        }

        private void ClearVisuals()
        {
            if (scrollRoot != null) Destroy(scrollRoot.gameObject);
            scrollRoot = null;
            nodeContainer = null;
            lineContainer = null;
            backgroundContainer = null;
            nodeVisuals.Clear();
            lineVisuals.Clear();
            worldPositions.Clear();
            mapBounds = default;
        }

        /// <summary>Prefab未設定時のフォールバックノード（MeshRendererを持たない空 GO）</summary>
        private GameObject CreateFallbackNode()
        {
            // PrimitiveType.Quad は MeshRenderer を持つため、
            // SpriteRenderer と競合する。空の GO にする。
            // localPosition は呼び出し側で設定する（親の回転に追従させるため）。
            var go = new GameObject("FallbackNode");
            go.transform.SetParent(nodeContainer, false);
            return go;
        }
    }
}
