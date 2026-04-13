using System.Collections;
using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム燃え尽き演出エフェクト
    /// 
    /// <para><b>機能:</b></para>
    /// <list type="bullet">
    ///   <item>ディゾルブノイズで燃え広がる演出</item>
    ///   <item>エッジグロー（Emission）— インスペクターで色調整可能</item>
    ///   <item>完了コールバック</item>
    /// </list>
    /// 
    /// <para><b>使い方:</b></para>
    /// <code>
    /// ItemBurnEffect.Play(targetObj, 1.5f, () => Destroy(targetObj));
    /// </code>
    /// </summary>
    public class ItemBurnEffect : MonoBehaviour
    {
        // =================================================================
        //  パラメータ
        // =================================================================

        [Header("タイミング")]
        [SerializeField] private float burnDuration = 1.5f;
        [SerializeField] private AnimationCurve burnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("シェーダー設定")]
        [SerializeField] private float edgeWidth = 0.06f;
        [SerializeField] private Color edgeColorHot = new Color(1f, 1f, 1f, 1f);      // 白（高温）
        [SerializeField] private Color edgeColorMid = new Color(1f, 1f, 1f, 1f);      // 白（中間）
        [SerializeField] private Color edgeColorCool = new Color(0f, 0f, 0f, 1f);       // 黒（燃焼終了）
        [Tooltip("エッジのEmission強度（0=光らない、大=強く光る）")]
        [SerializeField, Range(0f, 8f)] private float emissionStrength = 1f;

        [Header("ディゾルブ溶け方")]
        [Tooltip("ノイズの粗さ（小=細かい溶け方、大=大きな塁だらで溶ける）")]
        [SerializeField, Range(1f, 30f)] private float noiseScale = 6f;
        [Tooltip("ノイズの細かさ（小=スムーズ、大=ギザギザ）")]
        [SerializeField, Range(0f, 1f)] private float noiseDetail = 0.5f;
        [Tooltip("下から上へ燃え上がるバイアス（0=均一、1=強い下→上）")]
        [SerializeField, Range(0f, 1f)] private float burnDirectionBias = 0.4f;
        [Tooltip("ノイズテクスチャ解像度")]
        [SerializeField] private int noiseResolution = 20;

        [Header("燃えカスパーティクル")]
        [Tooltip("燃えカスパーティクルを有効にする")]
        [SerializeField] private bool ashEnabled = true;
        [Tooltip("最大同時パーティクル数")]
        [SerializeField] private int ashMaxParticles = 80;
        [Tooltip("最大放出レート（個/秒）")]
        [SerializeField] private float ashEmissionRate = 40f;
        [Tooltip("パーティクルの大きさ")]
        [SerializeField] private float ashSize = 0.015f;
        [Tooltip("パーティクルの寿命（秒）")]
        [SerializeField] private float ashLifetime = 1.5f;
        [Tooltip("落下の重力（カメラ下方向）")]
        [SerializeField] private float ashGravity = 0.8f;
        [Tooltip("横方向の広がり")]
        [SerializeField] private float ashSpread = 0.3f;
        [Tooltip("trueならテクスチャから色を抽出、falseならashColorを使用")]
        [SerializeField] private bool ashUseTextureColor = true;
        [Tooltip("カスタム燃えカス色（ashUseTextureColor=false時に使用）")]
        [SerializeField] private Color ashColor = new Color(0.25f, 0.18f, 0.12f, 1f);

        // =================================================================
        //  内部
        // =================================================================

        private Material[] burnMaterials;
        private Shader burnShader;
        private Texture2D noiseTexture;
        private System.Action onComplete;
        private bool isBurning = false;
        private ParticleSystem ashParticleSystem;
        private GameObject ashParticleObj;
        private Transform cameraTransform;

        /// <summary>演出中かどうか</summary>
        public bool IsBurning => isBurning;

        // =================================================================
        //  静的ファクトリー
        // =================================================================

        /// <summary>
        /// 対象GameObjectに燃え尽き演出を適用して再生
        /// </summary>
        /// <param name="target">燃え尽きるオブジェクト</param>
        /// <param name="duration">演出時間（秒）</param>
        /// <param name="onComplete">完了時コールバック</param>
        /// <returns>演出コンポーネント</returns>
        public static ItemBurnEffect Play(GameObject target, float duration = 1.5f, System.Action onComplete = null)
        {
            if (target == null) return null;

            var effect = target.AddComponent<ItemBurnEffect>();
            effect.burnDuration = duration;
            effect.onComplete = onComplete;
            effect.StartBurn();
            return effect;
        }

        /// <summary>
        /// 対象GameObjectに燃え尽き演出を適用して再生（全パラメータ指定）
        /// </summary>
        public static ItemBurnEffect Play(GameObject target, float duration, Color flameColor, Color midColor, Color charColor, float edgeWidth, float emissionStrength, float noiseScale, float noiseDetail, float directionBias, bool ashEnabled, int ashMaxParticles, float ashEmissionRate, float ashSize, float ashLifetime, float ashGravity, float ashSpread, bool ashUseTextureColor, Color ashColor, Transform cameraTransform, System.Action onComplete = null)
        {
            if (target == null) return null;

            var effect = target.AddComponent<ItemBurnEffect>();
            effect.burnDuration = duration;
            effect.edgeColorHot = flameColor;
            effect.edgeColorMid = midColor;
            effect.edgeColorCool = charColor;
            effect.edgeWidth = edgeWidth;
            effect.emissionStrength = emissionStrength;
            effect.noiseScale = noiseScale;
            effect.noiseDetail = noiseDetail;
            effect.burnDirectionBias = directionBias;
            effect.ashEnabled = ashEnabled;
            effect.ashMaxParticles = ashMaxParticles;
            effect.ashEmissionRate = ashEmissionRate;
            effect.ashSize = ashSize;
            effect.ashLifetime = ashLifetime;
            effect.ashGravity = ashGravity;
            effect.ashSpread = ashSpread;
            effect.ashUseTextureColor = ashUseTextureColor;
            effect.ashColor = ashColor;
            effect.cameraTransform = cameraTransform;
            effect.onComplete = onComplete;
            effect.StartBurn();
            return effect;
        }

        /// <summary>
        /// 対象GameObjectに燃え尽き演出を適用して再生（全パラメータ指定、燃えカスデフォルト）
        /// </summary>
        public static ItemBurnEffect Play(GameObject target, float duration, Color flameColor, Color charColor, float edgeWidth, float noiseScale, float noiseDetail, float directionBias, System.Action onComplete = null)
        {
            if (target == null) return null;

            var effect = target.AddComponent<ItemBurnEffect>();
            effect.burnDuration = duration;
            effect.edgeColorHot = flameColor;
            effect.edgeColorCool = charColor;
            effect.edgeWidth = edgeWidth;
            effect.noiseScale = noiseScale;
            effect.noiseDetail = noiseDetail;
            effect.burnDirectionBias = directionBias;
            effect.onComplete = onComplete;
            effect.StartBurn();
            return effect;
        }

        /// <summary>
        /// 対象GameObjectに燃え尽き演出を適用して再生（色指定付き）
        /// </summary>
        /// <param name="target">燃え尽きるオブジェクト</param>
        /// <param name="duration">演出時間（秒）</param>
        /// <param name="flameColor">炎色（内側エッジ）</param>
        /// <param name="charColor">コゲ色（外側エッジ）</param>
        /// <param name="onComplete">完了時コールバック</param>
        /// <returns>演出コンポーネント</returns>
        public static ItemBurnEffect Play(GameObject target, float duration, Color flameColor, Color charColor, System.Action onComplete = null)
        {
            if (target == null) return null;

            var effect = target.AddComponent<ItemBurnEffect>();
            effect.burnDuration = duration;
            effect.edgeColorHot = flameColor;
            effect.edgeColorCool = charColor;
            effect.onComplete = onComplete;
            effect.StartBurn();
            return effect;
        }

        // =================================================================
        //  演出開始
        // =================================================================

        /// <summary>燃え尽き演出を開始</summary>
        public void StartBurn()
        {
            if (isBurning) return;
            isBurning = true;

            // シェーダー検索
            burnShader = Shader.Find("Custom/BurnDissolve");
            if (burnShader == null)
            {
                Debug.LogWarning("[ItemBurnEffect] BurnDissolve shader not found — fallback to fade");
                StartCoroutine(FallbackFadeCoroutine());
                return;
            }

            // ノイズテクスチャ生成
            noiseTexture = GenerateNoiseTexture(noiseResolution, noiseScale, noiseDetail, burnDirectionBias);

            // 全レンダラーのマテリアルをBurnDissolveに差し替え
            var renderers = GetComponentsInChildren<Renderer>();
            burnMaterials = new Material[0];
            var matList = new System.Collections.Generic.List<Material>();

            foreach (var rend in renderers)
            {
                Material[] originals = rend.materials;
                Material[] replaced = new Material[originals.Length];

                for (int i = 0; i < originals.Length; i++)
                {
                    var burnMat = new Material(burnShader);

                    // 元テクスチャをコピー
                    if (originals[i].HasProperty("_MainTex"))
                        burnMat.SetTexture("_MainTex", originals[i].GetTexture("_MainTex"));
                    if (originals[i].HasProperty("_Color"))
                        burnMat.SetColor("_Color", originals[i].GetColor("_Color"));

                    burnMat.SetTexture("_NoiseTex", noiseTexture);
                    burnMat.SetFloat("_DissolveAmount", 0f);
                    burnMat.SetFloat("_EdgeWidth", edgeWidth);
                    burnMat.SetColor("_EdgeColor1", edgeColorHot);
                    burnMat.SetColor("_EdgeColor2", edgeColorMid);
                    burnMat.SetColor("_EdgeColor3", edgeColorCool);
                    burnMat.SetFloat("_EmissionStrength", emissionStrength);

                    replaced[i] = burnMat;
                    matList.Add(burnMat);
                }

                rend.materials = replaced;
            }

            burnMaterials = matList.ToArray();

            // --- 燃えカスパーティクル初期化 ---
            if (ashEnabled)
            {
                // burnMaterials（既にコピー済み）からテクスチャを取得
                // ※ rend.materials を再アクセスするとUnityが新しいMaterialインスタンスを
                //    生成し、burnMaterialsの参照が無効化されてディゾルブが壊れる
                Texture mainTex = null;
                foreach (var mat in burnMaterials)
                {
                    if (mat != null && mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                    {
                        mainTex = mat.GetTexture("_MainTex");
                        break;
                    }
                }

                Color[] ashColors;
                if (ashUseTextureColor)
                {
                    ashColors = ExtractColorsFromTexture(mainTex);
                }
                else
                {
                    // カスタム色から明暗バリエーションを生成
                    ashColors = new Color[]
                    {
                        ashColor,
                        ashColor * 0.7f,
                        ashColor * 1.2f,
                        ashColor * 0.4f,
                        Color.Lerp(ashColor, Color.black, 0.5f),
                        Color.Lerp(ashColor, new Color(0.3f, 0.2f, 0.1f), 0.3f),
                        ashColor * 0.85f,
                        Color.Lerp(ashColor, Color.gray, 0.2f)
                    };
                    for (int i = 0; i < ashColors.Length; i++)
                    {
                        ashColors[i].a = 1f;
                    }
                }

                // モデルの境界を計算
                Bounds bounds = new Bounds(transform.position, Vector3.zero);
                foreach (var rend in renderers)
                {
                    bounds.Encapsulate(rend.bounds);
                }

                ashParticleSystem = CreateAshParticleSystem(ashColors, bounds);
            }

            StartCoroutine(BurnCoroutine());
        }

        // =================================================================
        //  コルーチン
        // =================================================================

        private IEnumerator BurnCoroutine()
        {
            float elapsed = 0f;

            while (elapsed < burnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / burnDuration);
                float dissolve = burnCurve.Evaluate(t);

                // 全マテリアルのディゾルブ値を更新
                foreach (var mat in burnMaterials)
                {
                    if (mat != null)
                        mat.SetFloat("_DissolveAmount", dissolve);
                }

                // 燃えカスパーティクル: ディゾルブ値に完全同期
                if (ashParticleSystem != null)
                {
                    var emission = ashParticleSystem.emission;
                    // dissolve値からsin曲線で自然な放出: 0→ピーク(0.5)→0 をディゾルブと完全一致
                    float emitCurve = (dissolve > 0.001f)
                        ? Mathf.Sin(Mathf.Clamp01(dissolve) * Mathf.PI)
                        : 0f;
                    emission.rateOverTime = ashEmissionRate * emitCurve;

                    // パーティクル位置をモデル中心に追従
                    if (ashParticleObj != null)
                        ashParticleObj.transform.position = transform.position;
                }

                yield return null;
            }

            // 完全消滅
            foreach (var mat in burnMaterials)
            {
                if (mat != null)
                    mat.SetFloat("_DissolveAmount", 1.1f);
            }

            // パーティクル停止（残りは自然消滅）
            if (ashParticleSystem != null)
            {
                var emission = ashParticleSystem.emission;
                emission.rateOverTime = 0f;
                ashParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // バーン完了→即座にコールバック
            isBurning = false;
            onComplete?.Invoke();

            // パーティクルの残りが消えるまで待ってから破棄
            if (ashParticleObj != null)
                StartCoroutine(CleanupAshParticles());

            Debug.Log("[ItemBurnEffect] 🔥 Burn complete");
        }

        /// <summary>残存パーティクルが消えてからオブジェクトを破棄</summary>
        private IEnumerator CleanupAshParticles()
        {
            yield return new WaitForSeconds(ashLifetime + 0.5f);
            if (ashParticleObj != null)
                Destroy(ashParticleObj);
        }

        /// <summary>シェーダーが無い場合のフォールバック（フェードアウト）</summary>
        private IEnumerator FallbackFadeCoroutine()
        {
            float elapsed = 0f;
            var renderers = GetComponentsInChildren<Renderer>();

            // マテリアルを半透明対応に切替
            foreach (var rend in renderers)
            {
                foreach (var mat in rend.materials)
                {
                    SetMaterialTransparent(mat);
                }
            }

            while (elapsed < burnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / burnDuration);

                // スケール縮小 + 色を赤→黒
                float scale = Mathf.Lerp(1f, 0f, t);
                transform.localScale *= (1f - Time.deltaTime * 2f);

                foreach (var rend in renderers)
                {
                    foreach (var mat in rend.materials)
                    {
                        Color c = Color.Lerp(Color.red, Color.black, t);
                        c.a = 1f - t;
                        if (mat.HasProperty("_Color"))
                            mat.SetColor("_Color", c);
                    }
                }

                yield return null;
            }

            isBurning = false;
            onComplete?.Invoke();
        }

        // =================================================================
        //  ノイズテクスチャ生成
        // =================================================================

        /// <summary>PerlinNoiseベースのディゾルブノイズを生成</summary>
        private static Texture2D GenerateNoiseTexture(int size, float baseScale, float detail, float directionBias)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            // 複数オクターブのPerlinNoise合成
            float scale1 = baseScale;
            float scale2 = baseScale * 2f;
            float scale3 = baseScale * 4f;
            float detailWeight2 = detail;
            float detailWeight3 = detail * 0.5f;
            float totalWeight = 1f + detailWeight2 + detailWeight3;
            float offsetX = Random.Range(0f, 100f);
            float offsetY = Random.Range(0f, 100f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (float)x / size;
                    float ny = (float)y / size;

                    float n1 = Mathf.PerlinNoise(nx * scale1 + offsetX, ny * scale1 + offsetY);
                    float n2 = Mathf.PerlinNoise(nx * scale2 + offsetX, ny * scale2 + offsetY) * detailWeight2;
                    float n3 = Mathf.PerlinNoise(nx * scale3 + offsetX, ny * scale3 + offsetY) * detailWeight3;

                    float noise = (n1 + n2 + n3) / totalWeight; // 正規化
                    noise = Mathf.Clamp01(noise);

                    // 方向バイアス: 下部ほど早く消える
                    float yBias = 1f - ((float)y / size) * directionBias;
                    noise *= yBias;

                    tex.SetPixel(x, y, new Color(noise, noise, noise, 1f));
                }
            }

            tex.Apply();
            tex.name = "BurnNoise";
            return tex;
        }

        // =================================================================
        //  燃えカスパーティクル
        // =================================================================

        /// <summary>テクスチャから代表色を抽出（読み取り不可テクスチャ対応）</summary>
        private Color[] ExtractColorsFromTexture(Texture mainTex, int sampleCount = 16)
        {
            if (mainTex == null)
            {
                // テクスチャなし→デフォルトの灰色
                return new Color[]
                {
                    new Color(0.3f, 0.25f, 0.2f),
                    new Color(0.15f, 0.1f, 0.08f),
                    new Color(0.5f, 0.4f, 0.3f),
                    new Color(0.08f, 0.05f, 0.03f)
                };
            }

            // RenderTextureを使ってGPUから読み取り（isReadableフラグ不要）
            RenderTexture rt = RenderTexture.GetTemporary(mainTex.width, mainTex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(mainTex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(mainTex.width, mainTex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, mainTex.width, mainTex.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // ランダムサンプリング
            Color[] colors = new Color[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int px = Random.Range(0, readable.width);
                int py = Random.Range(0, readable.height);
                Color c = readable.GetPixel(px, py);
                // 灰化: 彩度を落として暗くする（燃えカスっぽく）
                float gray = c.grayscale;
                c = Color.Lerp(c, new Color(gray, gray, gray), 0.5f); // 彩度50%ダウン
                c *= 0.6f; // 暗く
                c.a = 1f;
                colors[i] = c;
            }

            Destroy(readable);
            return colors;
        }

        /// <summary>燃えカスパーティクルシステムを生成</summary>
        private ParticleSystem CreateAshParticleSystem(Color[] ashColors, Bounds modelBounds)
        {
            ashParticleObj = new GameObject("BurnAshParticles");
            ashParticleObj.transform.position = modelBounds.center;
            // ワールド空間で動作させるため親に付けない

            var ps = ashParticleObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // ----- Main -----
            var main = ps.main;
            main.duration = burnDuration + 1f;
            main.loop = false;
            main.startLifetime = ashLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(ashSize * 0.5f, ashSize);
            main.maxParticles = ashMaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f; // ワールド重力無効（カメラ相対落下はForceOverLifetimeで実現）
            main.startRotation = 0f; // ドット風: 回転なしで四角を保つ

            // ----- 色: テクスチャから抽出した色のグラデーション -----
            var grad = new Gradient();
            int keyCount = Mathf.Min(ashColors.Length, 8); // GradientKeyは最大8
            GradientColorKey[] colorKeys = new GradientColorKey[keyCount];
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
            for (int i = 0; i < keyCount; i++)
            {
                colorKeys[i] = new GradientColorKey(ashColors[i], (float)i / (keyCount - 1));
            }
            alphaKeys[0] = new GradientAlphaKey(1f, 0f);
            alphaKeys[1] = new GradientAlphaKey(1f, 1f);
            grad.SetKeys(colorKeys, alphaKeys);
            main.startColor = new ParticleSystem.MinMaxGradient(grad);

            // ----- Emission: 最初はゼロ、BurnCoroutineから制御 -----
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            // ----- Shape: モデルの範囲に合わせたBox -----
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = modelBounds.size * 0.8f;
            shape.position = Vector3.zero;

            // ----- Force over Lifetime: カメラ下方向への落下 -----
            var force = ps.forceOverLifetime;
            force.enabled = true;
            // カメラの-up方向を「下」として重力をかける
            Vector3 camDown = (cameraTransform != null)
                ? -cameraTransform.up
                : Vector3.down;
            float gravityForce = ashGravity * 9.81f; // Physics.gravityスケール
            force.x = new ParticleSystem.MinMaxCurve(camDown.x * gravityForce);
            force.y = new ParticleSystem.MinMaxCurve(camDown.y * gravityForce);
            force.z = new ParticleSystem.MinMaxCurve(camDown.z * gravityForce);

            // ----- Velocity over Lifetime: 横揺れ（カメラローカル軸基準） -----
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            // カメラのright/forward方向にspreadで揺れ
            Vector3 camRight = (cameraTransform != null) ? cameraTransform.right : Vector3.right;
            Vector3 camFwd = (cameraTransform != null) ? cameraTransform.forward : Vector3.forward;
            // ワールド軸に分解して設定
            float spreadRight = ashSpread;
            float spreadFwd = ashSpread * 0.5f;
            float vxMin = -spreadRight * camRight.x - spreadFwd * camFwd.x;
            float vxMax =  spreadRight * camRight.x + spreadFwd * camFwd.x;
            float vyMin = -spreadRight * camRight.y - spreadFwd * camFwd.y;
            float vyMax =  spreadRight * camRight.y + spreadFwd * camFwd.y;
            float vzMin = -spreadRight * camRight.z - spreadFwd * camFwd.z;
            float vzMax =  spreadRight * camRight.z + spreadFwd * camFwd.z;
            // Min/Maxが逆転していたらswap
            if (vxMin > vxMax) { float tmp = vxMin; vxMin = vxMax; vxMax = tmp; }
            if (vyMin > vyMax) { float tmp = vyMin; vyMin = vyMax; vyMax = tmp; }
            if (vzMin > vzMax) { float tmp = vzMin; vzMin = vzMax; vzMax = tmp; }
            vel.x = new ParticleSystem.MinMaxCurve(vxMin, vxMax);
            vel.y = new ParticleSystem.MinMaxCurve(vyMin, vyMax);
            vel.z = new ParticleSystem.MinMaxCurve(vzMin, vzMax);

            // ----- Size over Lifetime: 縮小して消える -----
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 0.6f),
                new Keyframe(1f, 0f)
            );
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // ----- Color over Lifetime: 後半で暗くフェードアウト -----
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var colorGrad = new Gradient();
            colorGrad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.4f, 0.3f, 0.2f), 0.6f),
                    new GradientColorKey(new Color(0.1f, 0.08f, 0.05f), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = colorGrad;

            // ----- Rotation over Lifetime: ドット風なので回転なし -----
            var rot = ps.rotationOverLifetime;
            rot.enabled = false;

            // ----- Renderer: 四角ドット用テクスチャ + マテリアル -----
            var renderer = ashParticleObj.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var particleMat = new Material(Shader.Find("Particles/Standard Unlit"));
            if (particleMat != null)
            {
                particleMat.SetFloat("_Mode", 0);
                particleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                particleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                particleMat.renderQueue = 3100;
                // 四角ドットテクスチャ生成
                particleMat.SetTexture("_MainTex", GenerateSquareDotTexture(8));
            }
            renderer.material = particleMat;

            ps.Play();
            return ps;
        }

        // =================================================================
        //  ヘルパー
        // =================================================================

        /// <summary>四角ドット用テクスチャ（中央に白い四角、周囲は透明）</summary>
        private static Texture2D GenerateSquareDotTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, // ドット感を保つ
                wrapMode = TextureWrapMode.Clamp
            };

            // 全体を白に（1ピクセル余白なしの四角）
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, Color.white);
                }
            }
            tex.Apply();
            tex.name = "SquareDot";
            return tex;
        }

        private void SetMaterialTransparent(Material mat)
        {
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }

        void OnDestroy()
        {
            // マテリアルクリーンアップ
            if (burnMaterials != null)
            {
                foreach (var mat in burnMaterials)
                {
                    if (mat != null)
                        Destroy(mat);
                }
            }

            if (noiseTexture != null)
                Destroy(noiseTexture);

            // パーティクルクリーンアップ
            if (ashParticleObj != null)
                Destroy(ashParticleObj);
        }
    }
}
