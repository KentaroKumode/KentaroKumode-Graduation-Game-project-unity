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
    ///   <item>黄→赤のエッジグロー（Emission）</item>
    ///   <item>火の粉パーティクル自動生成</item>
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
        [SerializeField] private Color edgeColorInner = new Color(1f, 0.9f, 0.3f, 1f);  // 黄
        [SerializeField] private Color edgeColorOuter = new Color(1f, 0.15f, 0f, 1f);   // 赤

        [Header("パーティクル")]
        [SerializeField] private bool autoCreateParticles = true;
        [SerializeField] private int sparkCount = 30;
        [SerializeField] private Color sparkColor = new Color(1f, 0.6f, 0.1f, 1f);

        // =================================================================
        //  内部
        // =================================================================

        private Material[] burnMaterials;
        private Shader burnShader;
        private Texture2D noiseTexture;
        private ParticleSystem sparkParticles;
        private System.Action onComplete;
        private bool isBurning = false;

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
            noiseTexture = GenerateNoiseTexture(256);

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
                    burnMat.SetColor("_EdgeColor1", edgeColorInner);
                    burnMat.SetColor("_EdgeColor2", edgeColorOuter);

                    replaced[i] = burnMat;
                    matList.Add(burnMat);
                }

                rend.materials = replaced;
            }

            burnMaterials = matList.ToArray();

            // パーティクル生成
            if (autoCreateParticles)
                CreateSparkParticles();

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

                yield return null;
            }

            // 完全消滅
            foreach (var mat in burnMaterials)
            {
                if (mat != null)
                    mat.SetFloat("_DissolveAmount", 1.1f);
            }

            // パーティクルの残りを待つ
            if (sparkParticles != null)
            {
                sparkParticles.Stop();
                yield return new WaitForSeconds(0.5f);
            }

            isBurning = false;
            onComplete?.Invoke();

            Debug.Log("[ItemBurnEffect] 🔥 Burn complete");
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
        private static Texture2D GenerateNoiseTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            // 複数オクターブのPerlinNoise合成
            float scale1 = 6f;
            float scale2 = 12f;
            float scale3 = 24f;
            float offsetX = Random.Range(0f, 100f);
            float offsetY = Random.Range(0f, 100f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (float)x / size;
                    float ny = (float)y / size;

                    float n1 = Mathf.PerlinNoise(nx * scale1 + offsetX, ny * scale1 + offsetY);
                    float n2 = Mathf.PerlinNoise(nx * scale2 + offsetX, ny * scale2 + offsetY) * 0.5f;
                    float n3 = Mathf.PerlinNoise(nx * scale3 + offsetX, ny * scale3 + offsetY) * 0.25f;

                    float noise = (n1 + n2 + n3) / 1.75f; // 正規化
                    noise = Mathf.Clamp01(noise);

                    // 「下から上に燃え上がる」バイアス: 下部ほど早く消えるよう、Y座標でバイアス
                    float yBias = 1f - ((float)y / size) * 0.4f;
                    noise *= yBias;

                    tex.SetPixel(x, y, new Color(noise, noise, noise, 1f));
                }
            }

            tex.Apply();
            tex.name = "BurnNoise";
            return tex;
        }

        // =================================================================
        //  火の粉パーティクル
        // =================================================================

        private void CreateSparkParticles()
        {
            var psObj = new GameObject("BurnSparks");
            psObj.transform.SetParent(transform, false);
            psObj.transform.localPosition = Vector3.zero;

            sparkParticles = psObj.AddComponent<ParticleSystem>();
            
            // AddComponent直後に自動再生されるため、停止してからプロパティを設定
            sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            
            var main = sparkParticles.main;
            main.playOnAwake = false;
            main.duration = burnDuration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = sparkColor;
            main.gravityModifier = -0.3f; // 上に昇る
            main.maxParticles = sparkCount * 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = sparkParticles.emission;
            emission.rateOverTime = sparkCount / burnDuration;

            var shape = sparkParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // バウンズに合わせる
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combined.Encapsulate(renderers[i].bounds);
                shape.scale = combined.size;
                psObj.transform.position = combined.center;
            }
            else
            {
                shape.scale = Vector3.one * 0.5f;
            }

            var col = sparkParticles.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(sparkColor, 0f),
                    new GradientColorKey(new Color(1f, 0.3f, 0f), 0.5f),
                    new GradientColorKey(Color.black, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = grad;

            var sizeOverLife = sparkParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            // PreviewCardレイヤーに設定
            int previewLayer = LayerMask.NameToLayer("PreviewCard");
            if (previewLayer >= 0)
                psObj.layer = previewLayer;

            var renderer = sparkParticles.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
            renderer.material.SetColor("_Color", sparkColor);

            sparkParticles.Play();
        }

        // =================================================================
        //  ヘルパー
        // =================================================================

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
        }
    }
}
