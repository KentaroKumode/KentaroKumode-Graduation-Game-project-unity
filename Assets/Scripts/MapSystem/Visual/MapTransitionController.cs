using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップ⇄ショップ等の特殊フェーズ遷移時の演出。
    /// 巻取りはやめ、装飾オブジェクトを前哨基地側から順に空中へ持ち上げて消す方式に変更。
    ///
    /// RollUp:
    ///   1) MapDecorationPlacer.Spawned を取得
    ///   2) Y 座標で並び替え（liftFromLowerY=true なら低い方から / false なら高い方から）
    ///   3) 一定間隔ずらしで各装飾を上空（local +Y / world に応じてマップ平面の法線方向）へリフト
    ///   4) 全装飾消滅後にマップ可視を OFF（背景・ノード・ライン）
    ///
    /// Unroll:
    ///   1) マップ可視 ON
    ///   2) 装飾を再配置（同じシード）
    /// </summary>
    public class MapTransitionController : MonoBehaviour
    {
        public static MapTransitionController Instance { get; private set; }

        [Header("参照")]
        [SerializeField] private MapVisualizer mapVisualizer;
        [SerializeField] private MapDecorationPlacer decorationPlacer;

        [Header("装飾リフトアニメ")]
        [Tooltip("RollUp 呼び出しから装飾リフトが始まるまでの遅延")]
        [SerializeField] private float decorationStartDelay = 0f;
        [Tooltip("各装飾が持ち上がるのにかける時間")]
        [SerializeField] private float perItemLiftDuration = 0.45f;
        [Tooltip("隣の装飾との発動間隔（小さいほどほぼ同時、大きいほど波のように順次）")]
        [SerializeField] private float perItemDelay = 0.04f;
        [Tooltip("リフト距離（ワールド単位）。十分大きい値でカメラ外まで運ぶ")]
        [SerializeField] private float liftDistanceWorld = 50f;
        [Tooltip("リフト方向（ワールド空間）。デフォルトはワールド上方向")]
        [SerializeField] private Vector3 liftDirectionWorld = Vector3.up;
        [Tooltip("低 Y 側から順に持ち上げる（前哨基地が下端の場合 true）")]
        [SerializeField] private bool liftFromLowerY = true;
        [Tooltip("リフト中の横揺れ振幅（ふわっと感を出す）")]
        [SerializeField] private float lateralWobble = 0.15f;

        [Header("マップスライドアウト")]
        [Tooltip("マップ本体（背景・ノード・ライン）が滑り落ちる方向（ワールド空間）。デフォルトは前哨基地＝カメラ手前方向")]
        [SerializeField] private Vector3 mapSlideDirectionWorld = new Vector3(0f, 0f, -1f);
        [Tooltip("スライド距離（ワールド単位）")]
        [SerializeField] private float mapSlideDistance = 8f;
        [Tooltip("スライド + フェードにかける時間")]
        [SerializeField] private float mapSlideDuration = 0.7f;
        [Tooltip("装飾リフト開始からマップスライドを開始するまでの遅延")]
        [SerializeField] private float mapSlideStartDelay = 0.1f;
        [Tooltip("フェードを有効化（Material の _Color / _BaseColor のアルファをゼロまで下げる）")]
        [SerializeField] private bool enableMapFade = true;

        [Header("マップ可視オブジェクト")]
        [Tooltip("追加で非表示にしたい要素。ScrollRoot 配下の非装飾要素は自動で扱われる")]
        [SerializeField] private GameObject[] hideOnRollUp;

        private bool isAnimating;
        private Coroutine current;
        private readonly List<GameObject> autoHidden = new List<GameObject>();

        // スライド/フェードでいじった transform と renderer を後で戻すためのスナップショット
        private struct SlideSnap
        {
            public Transform t;
            public Vector3 originalLocalPos;
        }
        private struct FadeSnap
        {
            public Renderer r;
            public string prop;
            public Color originalColor;
        }
        private readonly List<SlideSnap> slideSnaps = new List<SlideSnap>();
        private readonly List<FadeSnap> fadeSnaps = new List<FadeSnap>();

        public bool IsAnimating => isAnimating;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (mapVisualizer == null) mapVisualizer = FindObjectOfType<MapVisualizer>();
            if (decorationPlacer == null) decorationPlacer = FindObjectOfType<MapDecorationPlacer>();
        }

        public void RollUp(System.Action onComplete = null)
        {
            if (current != null) StopCoroutine(current);
            current = StartCoroutine(RollUpRoutine(onComplete));
        }

        public void Unroll(System.Action onComplete = null)
        {
            if (current != null) StopCoroutine(current);
            current = StartCoroutine(UnrollRoutine(onComplete));
        }

        private IEnumerator RollUpRoutine(System.Action onComplete)
        {
            isAnimating = true;

            // 装飾リフトを順次起動
            var items = CollectDecorationsSorted();
            float totalDelay = decorationStartDelay;
            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                var d = items[i];
                if (d == null) continue;
                StartCoroutine(LiftAndDestroy(d, totalDelay));
                totalDelay += perItemDelay;
            }

            // マップ本体のスライド+フェードを並行起動
            StartCoroutine(SlideOutMapAfterDelay(mapSlideStartDelay));

            float decoEnd = totalDelay + perItemLiftDuration;
            float mapEnd = mapSlideStartDelay + mapSlideDuration;
            float wait = Mathf.Max(decoEnd, mapEnd) + 0.05f;
            yield return new WaitForSeconds(wait);

            isAnimating = false;
            current = null;
            onComplete?.Invoke();
        }

        private IEnumerator UnrollRoutine(System.Action onComplete)
        {
            isAnimating = true;

            // スライド・フェードを戻して再表示
            RestoreMapSlideAndFade();
            ShowAutoHidden();

            // 装飾を再配置（同じシードで戻す）
            if (decorationPlacer != null) decorationPlacer.RestoreForCurrentFloor();

            yield return null;

            isAnimating = false;
            current = null;
            onComplete?.Invoke();
        }

        // ============================================================
        //  マップスライド+フェード
        // ============================================================

        private IEnumerator SlideOutMapAfterDelay(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            slideSnaps.Clear();
            fadeSnaps.Clear();

            if (mapVisualizer == null || mapVisualizer.ScrollRoot == null) yield break;
            var root = mapVisualizer.ScrollRoot;

            // 対象: ScrollRoot 直下の非装飾子要素
            var targets = new List<Transform>();
            foreach (Transform child in root)
            {
                if (child == null) continue;
                if (child.GetComponent<MapDecoration>() != null) continue;
                if (!child.gameObject.activeSelf) continue;
                targets.Add(child);
            }
            if (targets.Count == 0) yield break;

            // 各 Transform: 元位置を保持
            foreach (var t in targets)
                slideSnaps.Add(new SlideSnap { t = t, originalLocalPos = t.localPosition });

            // 各 Renderer: 色プロパティを MaterialPropertyBlock 経由で操作
            var mpb = new MaterialPropertyBlock();
            var renderers = new List<Renderer>();
            foreach (var t in targets) renderers.AddRange(t.GetComponentsInChildren<Renderer>(true));

            if (enableMapFade)
            {
                foreach (var r in renderers)
                {
                    if (r == null || r.sharedMaterial == null) continue;
                    string prop = r.sharedMaterial.HasProperty("_Color") ? "_Color"
                                : r.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor"
                                : null;
                    if (prop == null) continue;
                    Color c = r.sharedMaterial.GetColor(prop);
                    fadeSnaps.Add(new FadeSnap { r = r, prop = prop, originalColor = c });
                }
            }

            // ワールド方向 → ScrollRoot ローカル方向
            Vector3 dirLocal = root.InverseTransformDirection(mapSlideDirectionWorld.normalized);

            float t0 = 0f;
            while (t0 < mapSlideDuration)
            {
                t0 += Time.deltaTime;
                float p = Mathf.Clamp01(t0 / mapSlideDuration);
                float ease = SmoothStep(p);

                Vector3 offset = dirLocal * mapSlideDistance * ease;
                for (int i = 0; i < slideSnaps.Count; i++)
                {
                    var s = slideSnaps[i];
                    if (s.t == null) continue;
                    s.t.localPosition = s.originalLocalPos + offset;
                }

                if (enableMapFade)
                {
                    float alpha = 1f - ease;
                    for (int i = 0; i < fadeSnaps.Count; i++)
                    {
                        var f = fadeSnaps[i];
                        if (f.r == null) continue;
                        f.r.GetPropertyBlock(mpb);
                        Color c = f.originalColor;
                        c.a = f.originalColor.a * alpha;
                        mpb.SetColor(f.prop, c);
                        f.r.SetPropertyBlock(mpb);
                    }
                }

                yield return null;
            }

            // 完全消滅後に SetActive(false) して、戻すためのリストに記録
            foreach (var t in targets)
            {
                if (t == null) continue;
                t.gameObject.SetActive(false);
                autoHidden.Add(t.gameObject);
            }

            if (hideOnRollUp != null)
                for (int i = 0; i < hideOnRollUp.Length; i++)
                    if (hideOnRollUp[i] != null) hideOnRollUp[i].SetActive(false);
        }

        private void RestoreMapSlideAndFade()
        {
            // 位置復帰
            for (int i = 0; i < slideSnaps.Count; i++)
            {
                var s = slideSnaps[i];
                if (s.t != null) s.t.localPosition = s.originalLocalPos;
            }
            slideSnaps.Clear();

            // フェード復帰: MPB をクリアすればマテリアル本来の色に戻る
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < fadeSnaps.Count; i++)
            {
                var f = fadeSnaps[i];
                if (f.r == null) continue;
                f.r.GetPropertyBlock(mpb);
                mpb.Clear();
                f.r.SetPropertyBlock(mpb);
            }
            fadeSnaps.Clear();
        }

        private void ShowAutoHidden()
        {
            for (int i = 0; i < autoHidden.Count; i++)
                if (autoHidden[i] != null) autoHidden[i].SetActive(true);
            autoHidden.Clear();

            if (hideOnRollUp != null)
                for (int i = 0; i < hideOnRollUp.Length; i++)
                    if (hideOnRollUp[i] != null) hideOnRollUp[i].SetActive(true);
        }

        // ============================================================
        //  内部
        // ============================================================

        private List<MapDecoration> CollectDecorationsSorted()
        {
            var list = new List<MapDecoration>();
            if (decorationPlacer == null) return list;

            foreach (var go in decorationPlacer.Spawned)
            {
                if (go == null) continue;
                var d = go.GetComponent<MapDecoration>();
                if (d != null) list.Add(d);
            }

            list.Sort((a, b) =>
            {
                float ay = a.originalLocalPosition.y;
                float by = b.originalLocalPosition.y;
                int cmp = ay.CompareTo(by);
                return liftFromLowerY ? cmp : -cmp;
            });
            return list;
        }

        private IEnumerator LiftAndDestroy(MapDecoration d, float startDelay)
        {
            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);
            if (d == null) yield break;

            // ワールド方向 → 各装飾の親空間（ScrollRoot ローカル）へ変換して移動
            Transform parent = d.transform.parent;
            Vector3 dirLocal = parent != null
                ? parent.InverseTransformDirection(liftDirectionWorld.normalized)
                : liftDirectionWorld.normalized;

            Vector3 startPos = d.originalLocalPosition;
            Vector3 endPos = startPos + dirLocal * liftDistanceWorld;

            float t = 0f;
            // ふわっと感: 開始は緩やか・後半加速の easeIn
            while (t < perItemLiftDuration)
            {
                if (d == null) yield break;
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / perItemLiftDuration);
                float ease = p * p; // easeIn
                Vector3 pos = Vector3.Lerp(startPos, endPos, ease);
                if (lateralWobble > 0f)
                {
                    float wob = Mathf.Sin(p * Mathf.PI * 2f) * lateralWobble * (1f - p);
                    pos.x += wob;
                }
                d.transform.localPosition = pos;
                yield return null;
            }

            if (d != null) Destroy(d.gameObject);
        }

        private static float SmoothStep(float t) => t * t * (3f - 2f * t);
    }
}
