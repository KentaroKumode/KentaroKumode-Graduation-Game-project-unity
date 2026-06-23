using System;
using UnityEngine;

namespace GameLoop
{
    /// <summary>
    /// フェーズごとにシーンを「X 座標ゾーン」で分離する。 各フェーズに対応する X 位置に
    /// <see cref="targetCamera"/> をテレポートさせ、 そこに置いた専用コンテンツだけを写す。
    ///
    /// 配置例:
    ///   X=1000: Title (ortho ContentCamera 担当 — 本 Router の対象外)
    ///   X=1500: Map
    ///   X=2000: Combat (battle visual)
    ///   X=2500: Shop
    ///
    /// 各ゾーン内のコンテンツは scene 上で実際にその X に置く。
    /// 本 Router はカメラ位置だけを動かす。 表示/非表示の制御は <see cref="PhaseDirector"/> 側。
    /// </summary>
    [DisallowMultipleComponent]
    public class PhaseSceneRouter : MonoBehaviour
    {
        [Serializable]
        public class Zone
        {
            public string label;
            public GameManager.GamePhase[] phases;
            [Tooltip("true=position 全要素を上書き / false=X 座標のみ反映")]
            public bool overrideFullPosition = true;
            public Vector3 cameraPosition = new Vector3(1000f, 0f, -22f);
            [Tooltip("オイラー角でカメラ回転を指定。 useRotation=false なら無視。")]
            public bool useRotation = true;
            public Vector3 cameraEuler = new Vector3(15f, 0f, 180f);
            [Tooltip("透視カメラの FOV を上書きする(0 で変更しない)")]
            public float fieldOfView = 0f;
        }

        [Tooltip("移動させるカメラ(通常はゲームプレイ用透視カメラ)")]
        public Camera targetCamera;
        [Tooltip("各フェーズに対応するカメラ配置")]
        public Zone[] zones;

        private bool _wired;

        private void OnEnable() { Subscribe(); Apply(); }
        private void Start()    { Subscribe(); Apply(); }
        private void OnDisable()
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.OnPhaseChanged -= OnPhaseChanged;
            _wired = false;
        }

        private void Subscribe()
        {
            if (_wired) return;
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.OnPhaseChanged += OnPhaseChanged;
            _wired = true;
        }

        private void OnPhaseChanged(GameManager.GamePhase _) => Apply();

        private void Apply()
        {
            if (targetCamera == null) return;
            var gm = GameManager.Instance;
            if (gm == null) return;
            var phase = gm.CurrentPhase;
            if (zones == null) return;
            foreach (var z in zones)
            {
                if (z?.phases == null) continue;
                foreach (var p in z.phases)
                {
                    if (p != phase) continue;
                    if (z.overrideFullPosition)
                    {
                        targetCamera.transform.position = z.cameraPosition;
                    }
                    else
                    {
                        var pos = targetCamera.transform.position;
                        pos.x = z.cameraPosition.x;
                        targetCamera.transform.position = pos;
                    }
                    if (z.useRotation)
                        targetCamera.transform.rotation = Quaternion.Euler(z.cameraEuler);
                    if (z.fieldOfView > 0f)
                        targetCamera.fieldOfView = z.fieldOfView;
                    return;
                }
            }
        }
    }
}
