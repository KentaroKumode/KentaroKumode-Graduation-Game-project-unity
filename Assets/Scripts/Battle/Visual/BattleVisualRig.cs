using UnityEngine;

namespace Battle.Visual
{
    /// <summary>
    /// バトル演出用の舞台(リグ)。
    /// LCD コンテンツ・カメラの視界内に「プレイヤー駒・敵駒・鍔迫り合いの中心」を配置する。
    ///
    /// 自/敵 = 木彫りの駒(プレースホルダ=円柱)。同一 RT に同居。
    /// 攻撃エフェクトは <see cref="BattleVisualDirector"/> が registry から取り出してこのリグの座標で生成する。
    /// </summary>
    [DisallowMultipleComponent]
    public class BattleVisualRig : MonoBehaviour
    {
        [Header("配線")]
        [Tooltip("LCD コンテンツ用カメラ。 未指定時は LcdScreen から自動取得。")]
        public Camera contentCamera;
        [Tooltip("駒/FX のレイヤ名。 空文字なら contentCamera GO のレイヤを使う。")]
        public string contentLayerName = "";
        [Tooltip("true=contentCamera の子として配置(カメラに追従)。 false=ワールド座標固定で置く(透視カメラでげ鍔進り合いを見せるとき推奨)。")]
        public bool parentToCamera = false;

        [Header("レイアウト(world)")]
        [Tooltip("舞台中心のワールド位置(parentToCamera=false 時)。 parentToCamera=true 時は camera からのローカル相対。")]
        public Vector3 stagePosition = new Vector3(1000f, -5f, 0f);
        [Tooltip("プレイヤー駒/敵駒の中心からの左右距離")]
        public float sideOffset = 2.4f;
        [Tooltip("鍔迫り合いの中心高さ(駒の中心より上)")]
        public float clashYOffset = 0.6f;

        [Header("照明")]
        [Tooltip("駒を照らす専用ディレクショナルライトを内部生成する")]
        public bool buildStageLight = true;
        [Tooltip("ステージ照明の色")]
        public Color stageLightColor = new Color(1f, 0.95f, 0.85f);
        [Tooltip("ステージ照明の強度")]
        public float stageLightIntensity = 1.6f;
        [Tooltip("ステージ照明の俯角(度)。 駒を正面斜め上から照らす。")]
        public float stageLightPitch = 35f;
        [Tooltip("ステージ照明の左右オフセット(度)")]
        public float stageLightYaw = 20f;
        [Tooltip("補助の半球光(ambient フィル)。 影を浮かせる用。")]
        public Color stageAmbientColor = new Color(0.35f, 0.35f, 0.45f);
        [Tooltip("半球光の強度")]
        public float stageAmbientIntensity = 0.8f;

        [Header("駒")]
        [Tooltip("プレイヤー駒の prefab/FBX (未指定なら円柱プレースホルダ)")]
        public GameObject playerPiecePrefab;
        [Tooltip("敵駒の prefab/FBX (未指定なら円柱プレースホルダ)")]
        public GameObject enemyPiecePrefab;
        [Tooltip("駒のスケール倍率")]
        public float pieceScale = 1.0f;
        [Tooltip("プレイヤー駒の追加オフセット(pivot 起点 ローカル)")]
        public Vector3 playerPieceOffset = Vector3.zero;
        [Tooltip("プレイヤー駒の追加回転(オイラー角)")]
        public Vector3 playerPieceEuler = new Vector3(0f, 90f, 0f);
        [Tooltip("敵駒の追加オフセット(pivot 起点 ローカル)")]
        public Vector3 enemyPieceOffset = Vector3.zero;
        [Tooltip("敵駒の追加回転(オイラー角)")]
        public Vector3 enemyPieceEuler = new Vector3(0f, -90f, 0f);
        [Tooltip("プレイヤー駒に乗算する色(プレハブのマテリアル色を保ちたいなら白)")]
        public Color playerPieceColor = Color.white;
        [Tooltip("敵駒に乗算する色")]
        public Color enemyPieceColor = Color.white;
        [Tooltip("プレースホルダ円柱の高さ(prefab 未指定時のみ使用)")]
        public float pieceHeight = 0.9f;
        [Tooltip("プレースホルダ円柱の半径(prefab 未指定時のみ使用)")]
        public float pieceRadius = 0.35f;

        [Header("床(Plane)")]
        [Tooltip("駒の足元に床 Plane を生成する")]
        public bool buildGroundPlane = true;
        [Tooltip("床の色")]
        public Color groundColor = new Color(0.22f, 0.18f, 0.14f);
        [Tooltip("床のサイズ(world units)。 Plane primitive はネイティブ 10×10 なのでこの値÷10 で scale する。")]
        public Vector2 groundSize = new Vector2(12f, 8f);
        [Tooltip("床の Y オフセット(pivot からの相対)。 駒の足元に来るよう微調整。")]
        public float groundY = 0f;

        public Transform PlayerPivot { get; private set; }
        public Transform EnemyPivot { get; private set; }
        public Transform ClashCenter { get; private set; }
        public Camera ContentCamera => contentCamera;
        public int ContentLayer { get; private set; }

        private GameObject _root;
        private Transform _playerPiece;
        private Transform _enemyPiece;

        private void Awake()
        {
            EnsureContentCamera();
            ContentLayer = ResolveContentLayer();
            BuildStage();
            SetVisible(false);
        }

        private void EnsureContentCamera()
        {
            if (contentCamera != null) return;
            var lcd = FindObjectOfType<UI.Lcd.LcdScreen>();
            if (lcd != null) contentCamera = lcd.contentCamera;
        }

        private int ResolveContentLayer()
        {
            if (!string.IsNullOrEmpty(contentLayerName))
            {
                int layer = LayerMask.NameToLayer(contentLayerName);
                if (layer >= 0) return layer;
            }
            return contentCamera != null ? contentCamera.gameObject.layer : 0;
        }

        private void BuildStage()
        {
            _root = new GameObject("BattleStageRoot");
            if (parentToCamera && contentCamera != null)
            {
                _root.transform.SetParent(contentCamera.transform, false);
                _root.transform.localPosition = stagePosition;
                _root.transform.localRotation = Quaternion.identity;
            }
            else
            {
                // rig の active 状態を継承させるため、 transform は rig 自身の子にしつつワールド座標で固定する。
                _root.transform.SetParent(transform, false);
                _root.transform.position = stagePosition;
                _root.transform.rotation = Quaternion.identity;
            }
            _root.layer = ContentLayer;

            PlayerPivot = new GameObject("PlayerPivot").transform;
            PlayerPivot.SetParent(_root.transform, false);
            PlayerPivot.localPosition = new Vector3(-sideOffset, 0f, 0f);

            EnemyPivot = new GameObject("EnemyPivot").transform;
            EnemyPivot.SetParent(_root.transform, false);
            EnemyPivot.localPosition = new Vector3(sideOffset, 0f, 0f);
            EnemyPivot.localRotation = Quaternion.identity;

            ClashCenter = new GameObject("ClashCenter").transform;
            ClashCenter.SetParent(_root.transform, false);
            ClashCenter.localPosition = new Vector3(0f, clashYOffset, 0f);

            _playerPiece = BuildPiece("PlayerPiece", playerPiecePrefab, playerPieceColor, PlayerPivot, playerPieceOffset, playerPieceEuler);
            _enemyPiece  = BuildPiece("EnemyPiece",  enemyPiecePrefab,  enemyPieceColor,  EnemyPivot,  enemyPieceOffset,  enemyPieceEuler);

            if (buildGroundPlane) BuildGround();
            if (buildStageLight) BuildStageLight();
        }

        private void BuildGround()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "GroundPlane";
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = new Vector3(0f, groundY, 0f);
            go.transform.localRotation = Quaternion.identity;
            // Unity の Plane primitive はネイティブ 10×10 units。 1/10 で目標サイズに。
            go.transform.localScale = new Vector3(groundSize.x * 0.1f, 1f, groundSize.y * 0.1f);
            SetLayerRecursively(go, ContentLayer);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.color = groundColor;
                mat.SetFloat("_Glossiness", 0.05f);
                rend.sharedMaterial = mat;
            }
        }

        private void BuildStageLight()
        {
            // メインのキーライト: 駒を斜め前上から照らす。
            var keyGo = new GameObject("StageKeyLight");
            keyGo.transform.SetParent(_root.transform, false);
            keyGo.transform.localRotation = Quaternion.Euler(stageLightPitch, stageLightYaw, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = stageLightColor;
            key.intensity = stageLightIntensity;
            key.shadows = LightShadows.Soft;
            key.cullingMask = 1 << ContentLayer;

            // 半球フィル: 影が真っ黒にならないように下面を持ち上げる。
            var fillGo = new GameObject("StageFillLight");
            fillGo.transform.SetParent(_root.transform, false);
            fillGo.transform.localRotation = Quaternion.Euler(-30f, -stageLightYaw, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = stageAmbientColor;
            fill.intensity = stageAmbientIntensity;
            fill.shadows = LightShadows.None;
            fill.cullingMask = 1 << ContentLayer;
        }

        private Transform BuildPiece(string name, GameObject prefab, Color tint, Transform parent, Vector3 offset, Vector3 euler)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, parent);
                go.transform.localScale *= pieceScale;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                go.transform.SetParent(parent, false);
                float yScale = Mathf.Max(0.01f, pieceHeight * 0.5f);
                float xz = Mathf.Max(0.01f, pieceRadius * 2f);
                go.transform.localScale = new Vector3(xz, yScale, xz);
                offset += new Vector3(0f, pieceHeight * 0.5f, 0f);
            }

            go.name = name;
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.Euler(euler);
            SetLayerRecursively(go, ContentLayer);

            // 色乗算: マテリアルがあれば tint を適用 (プレハブのマテリアルが破壊されないよう sharedMaterial をコピー)。
            if (tint != Color.white)
            {
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mat = new Material(rend.sharedMaterial);
                    if (mat.HasProperty("_Color")) mat.color *= tint;
                    rend.sharedMaterial = mat;
                }
            }
            return go.transform;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
        }

        /// <summary>戦闘外ではリグを非表示にする。</summary>
        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        /// <summary>駒を初期姿勢に戻す。</summary>
        public void ResetPieces()
        {
            if (PlayerPivot != null)
            {
                PlayerPivot.localPosition = new Vector3(-sideOffset, 0f, 0f);
                PlayerPivot.localRotation = Quaternion.identity;
            }
            if (EnemyPivot != null)
            {
                EnemyPivot.localPosition = new Vector3(sideOffset, 0f, 0f);
                EnemyPivot.localRotation = Quaternion.identity;
            }
        }

        public void ApplyPieceColors(Color player, Color enemy)
        {
            playerPieceColor = player;
            enemyPieceColor  = enemy;
            ApplyColor(_playerPiece, player);
            ApplyColor(_enemyPiece, enemy);
        }

        private static void ApplyColor(Transform piece, Color color)
        {
            if (piece == null) return;
            foreach (var rend in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_Color"))
                    rend.sharedMaterial.color = color;
            }
        }
    }
}
