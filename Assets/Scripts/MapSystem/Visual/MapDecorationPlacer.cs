using System.Collections.Generic;
using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップの左右余白に装飾オブジェクトを撒く。
    /// MapManager のフロア生成イベントに同期して再配置する。
    /// </summary>
    public class MapDecorationPlacer : MonoBehaviour
    {
        [System.Serializable]
        public class FloorDecorationSet
        {
            public int floor = 1;
            public GameObject[] prefabs;
        }

        [Header("参照")]
        [SerializeField] private MapVisualizer mapVisualizer;

        [Header("プレハブ（層ごと）")]
        [Tooltip("各フロアの装飾プレハブ候補。floor 番号で引き、見つからなければ fallback を使用")]
        [SerializeField] private FloorDecorationSet[] perFloorPrefabs;
        [Tooltip("該当フロアの定義が無い・プレハブ未指定時のフォールバック。null ならキューブを生成")]
        [SerializeField] private GameObject fallbackPrefab;

        [Header("配置帯（横方向）")]
        [Tooltip("マップ端からのマージン（左右共通）")]
        [SerializeField] private float bandStartOffset = 0.5f;
        [Tooltip("装飾を配置する横幅（左右それぞれの帯の太さ）")]
        [SerializeField] private float bandWidth = 5f;

        [Header("配置帯（縦方向）")]
        [Tooltip("マップ上端への余白（プラスでさらに上まで配置）")]
        [SerializeField] private float verticalPaddingTop = 2f;
        [Tooltip("マップ下端への余白（プラスでさらに下まで配置）")]
        [SerializeField] private float verticalPaddingBottom = 2f;

        [Header("数量・配置")]
        [Tooltip("片側帯あたりの装飾目標数")]
        [SerializeField] private int countPerSide = 18;
        [Tooltip("装飾同士の最小間隔。帯内のサンプリングで衝突回避に使用")]
        [SerializeField] private float minSpacing = 1.4f;
        [Tooltip("1個あたりの最大試行回数（位置抽選で衝突した時のリトライ上限）")]
        [SerializeField] private int maxAttemptsPerItem = 30;

        [Header("バリエーション")]
        [SerializeField] private Vector2 scaleRange = new Vector2(0.85f, 1.15f);

        [Header("挙動")]
        [SerializeField] private bool autoSubscribeMapEvents = true;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private int currentFloor;
        private int currentSeed;
        private bool seedCaptured;

        public IReadOnlyList<GameObject> Spawned => spawned;

        void Awake()
        {
            if (mapVisualizer == null) mapVisualizer = FindObjectOfType<MapVisualizer>();
            TrySubscribe();
        }

        void Start()
        {
            // Awake 順で購読を逃した場合の再試行 + すでに生成済みのマップに対する初期配置
            TrySubscribe();
            var mm = MapSystem.MapManager.Instance;
            if (mm != null && mm.CurrentMap != null)
            {
                StartCoroutine(DelayedPlace(mm.CurrentMap.floor));
            }
        }

        void OnDisable()
        {
            var mm = MapSystem.MapManager.Instance;
            if (mm == null) return;
            mm.OnMapGenerated -= HandleMapGenerated;
        }

        private void TrySubscribe()
        {
            if (!autoSubscribeMapEvents) return;
            var mm = MapSystem.MapManager.Instance;
            if (mm == null) return;
            mm.OnMapGenerated -= HandleMapGenerated;
            mm.OnMapGenerated += HandleMapGenerated;
        }

        private void HandleMapGenerated(MapSystem.FloorMap map)
        {
            // MapVisualizer の BuildMap 直後に呼びたいので 1 フレーム遅延
            StartCoroutine(DelayedPlace(map.floor));
        }

        private System.Collections.IEnumerator DelayedPlace(int floor)
        {
            yield return null;
            PlaceForFloor(floor);
        }

        // ============================================================
        //  外部 API
        // ============================================================

        public void PlaceForFloor(int floor, int? seedOverride = null)
        {
            Clear();
            if (mapVisualizer == null)
            {
                Debug.LogWarning("[MapDecorationPlacer] mapVisualizer 未設定");
                return;
            }
            if (!mapVisualizer.HasMapBounds)
            {
                Debug.LogWarning($"[MapDecorationPlacer] MapBounds 未確定 (floor={floor})。マップ生成完了後に呼ばれているか確認してください。");
                return;
            }

            currentFloor = floor;
            currentSeed = seedOverride ?? Random.Range(0, int.MaxValue);
            seedCaptured = true;

            var preState = Random.state;
            Random.InitState(currentSeed);

            var bounds = mapVisualizer.MapBounds;
            var prefabs = ResolvePrefabsForFloor(floor);

            float yMin = bounds.min.y - verticalPaddingBottom;
            float yMax = bounds.max.y + verticalPaddingTop;

            for (int side = -1; side <= 1; side += 2)
            {
                float xMin, xMax;
                if (side > 0)
                {
                    xMin = bounds.max.x + bandStartOffset;
                    xMax = xMin + bandWidth;
                }
                else
                {
                    xMax = bounds.min.x - bandStartOffset;
                    xMin = xMax - bandWidth;
                }

                var placedPoints = new List<Vector2>(countPerSide);
                for (int i = 0; i < countPerSide; i++)
                {
                    if (TrySamplePoint(xMin, xMax, yMin, yMax, placedPoints, out var p))
                    {
                        placedPoints.Add(p);
                        Spawn(prefabs, p);
                    }
                    // 取れなかった場合は黙ってスキップ（密度上限到達）
                }
            }

            Random.state = preState;
            Debug.Log($"[MapDecorationPlacer] floor={floor} 装飾配置完了 (生成数={spawned.Count}, bounds X[{bounds.min.x:F1},{bounds.max.x:F1}] Y[{bounds.min.y:F1},{bounds.max.y:F1}])");
        }

        public void RestoreForCurrentFloor()
        {
            if (!seedCaptured) return;
            PlaceForFloor(currentFloor, currentSeed);
        }

        public void Clear()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) Destroy(spawned[i]);
            }
            spawned.Clear();
        }

        // ============================================================
        //  内部
        // ============================================================

        private GameObject[] ResolvePrefabsForFloor(int floor)
        {
            if (perFloorPrefabs != null)
            {
                for (int i = 0; i < perFloorPrefabs.Length; i++)
                {
                    var set = perFloorPrefabs[i];
                    if (set != null && set.floor == floor && set.prefabs != null && set.prefabs.Length > 0)
                        return set.prefabs;
                }
            }
            return null;
        }

        private bool TrySamplePoint(float xMin, float xMax, float yMin, float yMax,
            List<Vector2> placed, out Vector2 result)
        {
            float minSqr = minSpacing * minSpacing;
            for (int a = 0; a < maxAttemptsPerItem; a++)
            {
                Vector2 candidate = new Vector2(Random.Range(xMin, xMax), Random.Range(yMin, yMax));
                bool clash = false;
                for (int j = 0; j < placed.Count; j++)
                {
                    if ((placed[j] - candidate).sqrMagnitude < minSqr) { clash = true; break; }
                }
                if (!clash) { result = candidate; return true; }
            }
            result = default;
            return false;
        }

        private void Spawn(GameObject[] prefabs, Vector2 p)
        {
            GameObject prefab = (prefabs != null && prefabs.Length > 0)
                ? prefabs[Random.Range(0, prefabs.Length)]
                : fallbackPrefab;

            GameObject go;
            Transform parent = mapVisualizer.ScrollRoot != null ? mapVisualizer.ScrollRoot : transform;

            if (prefab != null)
            {
                go = Instantiate(prefab, parent);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(parent, false);
                go.name = "MapDecoration_FallbackCube";
            }

            go.transform.localPosition = new Vector3(p.x, p.y, 0f);

            float scale = Random.Range(scaleRange.x, scaleRange.y);
            go.transform.localScale = (prefab != null ? prefab.transform.localScale : Vector3.one) * scale;

            // 回転は一切かけない（プレハブ側で指定されたものをそのまま使う / フォールバックは identity）

            var deco = go.GetComponent<MapDecoration>();
            if (deco == null) deco = go.AddComponent<MapDecoration>();
            deco.originalLocalPosition = go.transform.localPosition;
            deco.originalLocalRotation = go.transform.localRotation;

            spawned.Add(go);
        }
    }
}
