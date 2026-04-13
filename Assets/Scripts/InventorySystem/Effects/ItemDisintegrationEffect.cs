using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InventorySystem
{
    /// <summary>
    /// アイテム粉砕演出エフェクト
    /// 
    /// <para><b>機能:</b></para>
    /// <list type="bullet">
    ///   <item>震え → ディゾルブ(粉になって消える) の2段階演出</item>
    ///   <item>粉パーティクルはオブジェクトのテクスチャ色を抽出して使用</item>
    ///   <item>完了コールバック</item>
    /// </list>
    /// 
    /// <para><b>使い方:</b></para>
    /// <code>
    /// ItemDisintegrationEffect.Play(targetObj, () => Destroy(targetObj));
    /// </code>
    /// </summary>
    public class ItemDisintegrationEffect : MonoBehaviour
    {
        // =================================================================
        //  パラメータ
        // =================================================================

        [Header("振動")]
        [SerializeField] private float shakeDuration = 0.5f;
        [SerializeField] private float shakeIntensity = 0.02f;
        [SerializeField] private float shakeSpeed = 40f;

        [Header("ディゾルブ")]
        [SerializeField] private float dissolveDuration = 0.15f;
        [SerializeField] private AnimationCurve dissolveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private float edgeWidth = 0.08f;
        [SerializeField] private Color edgeColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        [SerializeField] private float emissionStrength = 0.5f;

        [Header("粉パーティクル")]
        [SerializeField] private int dustMaxParticles = 120;
        [SerializeField] private float dustEmissionRate = 80f;
        [SerializeField] private float dustSize = 0.008f;
        [SerializeField] private float dustLifetime = 1.2f;
        [SerializeField] private float dustGravity = 0.5f;
        [SerializeField] private float dustSpread = 0.15f;
        [SerializeField] private bool dustUseTextureColor = true;
        [SerializeField] private Color dustColor = new Color(0.6f, 0.6f, 0.55f, 1f);

        // =================================================================
        //  内部
        // =================================================================

        private Material[] dissolveMaterials;
        private Shader dissolveShader;
        private Texture2D noiseTexture;
        private System.Action onComplete;
        private bool isPlaying = false;
        private ParticleSystem dustParticleSystem;
        private GameObject dustParticleObj;
        private Transform cameraTransform;

        public bool IsPlaying => isPlaying;

        // =================================================================
        //  静的ファクトリー
        // =================================================================

        /// <summary>
        /// 対象GameObjectに粉砕演出を適用して再生（全パラメータ指定）
        /// </summary>
        public static ItemDisintegrationEffect Play(
            GameObject target,
            float shakeDuration, float shakeIntensity, float shakeSpeed,
            float dissolveDuration, float edgeWidth, Color edgeColor, float emissionStrength,
            int dustMaxParticles, float dustEmissionRate, float dustSize,
            float dustLifetime, float dustGravity, float dustSpread,
            bool dustUseTextureColor, Color dustColor,
            Transform cameraTransform,
            System.Action onComplete = null)
        {
            if (target == null) return null;

            var effect = target.AddComponent<ItemDisintegrationEffect>();
            effect.shakeDuration = shakeDuration;
            effect.shakeIntensity = shakeIntensity;
            effect.shakeSpeed = shakeSpeed;
            effect.dissolveDuration = dissolveDuration;
            effect.edgeWidth = edgeWidth;
            effect.edgeColor = edgeColor;
            effect.emissionStrength = emissionStrength;
            effect.dustMaxParticles = dustMaxParticles;
            effect.dustEmissionRate = dustEmissionRate;
            effect.dustSize = dustSize;
            effect.dustLifetime = dustLifetime;
            effect.dustGravity = dustGravity;
            effect.dustSpread = dustSpread;
            effect.dustUseTextureColor = dustUseTextureColor;
            effect.dustColor = dustColor;
            effect.cameraTransform = cameraTransform;
            effect.onComplete = onComplete;
            effect.StartEffect();
            return effect;
        }

        /// <summary>
        /// シンプル版
        /// </summary>
        public static ItemDisintegrationEffect Play(GameObject target, Transform cameraTransform, System.Action onComplete = null)
        {
            if (target == null) return null;

            var effect = target.AddComponent<ItemDisintegrationEffect>();
            effect.cameraTransform = cameraTransform;
            effect.onComplete = onComplete;
            effect.StartEffect();
            return effect;
        }

        // =================================================================
        //  演出開始
        // =================================================================

        private void StartEffect()
        {
            if (isPlaying) return;
            isPlaying = true;

            dissolveShader = Shader.Find("Custom/BurnDissolve");
            if (dissolveShader == null)
            {
                Debug.LogWarning("[ItemDisintegrationEffect] BurnDissolve shader not found");
                onComplete?.Invoke();
                Destroy(this);
                return;
            }

            StartCoroutine(DisintegrationCoroutine());
        }

        // =================================================================
        //  メインコルーチン
        // =================================================================

        private IEnumerator DisintegrationCoroutine()
        {
            Vector3 originalPos = transform.localPosition;

            // ===== フェーズ1: 振動 =====
            float shakeElapsed = 0f;
            while (shakeElapsed < shakeDuration)
            {
                shakeElapsed += Time.deltaTime;
                float progress = shakeElapsed / shakeDuration;

                // 振動は後半に向かって強くなる
                float currentIntensity = shakeIntensity * progress;
                float offsetX = Mathf.Sin(shakeElapsed * shakeSpeed) * currentIntensity;
                float offsetY = Mathf.Sin(shakeElapsed * shakeSpeed * 1.3f) * currentIntensity * 0.6f;

                transform.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);
                yield return null;
            }
            transform.localPosition = originalPos;

            // ===== フェーズ2: ディゾルブ準備 =====
            noiseTexture = GenerateNoiseTexture(64, 6f, 0.5f, 0f);
            SetupDissolveMaterials();
            CreateDustParticleSystem();

            // ===== フェーズ3: ディゾルブ + 粉パーティクル =====
            // 一瞬ディゾルブの場合はバースト放出
            if (dustParticleSystem != null)
            {
                var emissionModule = dustParticleSystem.emission;
                emissionModule.rateOverTime = dustEmissionRate;
                
                // バースト: ディゾルブ開始時に大量放出
                emissionModule.SetBursts(new ParticleSystem.Burst[] {
                    new ParticleSystem.Burst(0f, (short)(dustMaxParticles * 0.7f))
                });
            }

            float dissolveElapsed = 0f;
            while (dissolveElapsed < dissolveDuration)
            {
                dissolveElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(dissolveElapsed / dissolveDuration);
                float dissolve = dissolveCurve.Evaluate(t);

                // シェーダーのディゾルブ値を更新
                foreach (var mat in dissolveMaterials)
                {
                    if (mat != null)
                        mat.SetFloat("_DissolveAmount", dissolve);
                }

                // パーティクル放出量をディゾルブに同期 (sin波)
                if (dustParticleSystem != null)
                {
                    var emissionModule = dustParticleSystem.emission;
                    float emitRate = dustEmissionRate * Mathf.Sin(Mathf.PI * dissolve);
                    emissionModule.rateOverTime = emitRate;

                    // パーティクル位置をオブジェクトに追従
                    dustParticleObj.transform.position = GetBoundsCenter();
                }

                yield return null;
            }

            // ディゾルブ完了 — パーティクル放出停止
            if (dustParticleSystem != null)
            {
                var emissionModule = dustParticleSystem.emission;
                emissionModule.rateOverTime = 0f;
            }

            // パーティクルを独立させてからオブジェクトを非表示にする
            CleanupDustParticles();

            // オブジェクトを即座に非表示
            gameObject.SetActive(false);
            
            Debug.Log($"[ItemDisintegrationEffect] 粉砕演出完了、コールバック呼び出し");
            
            // コールバック（インベントリ削除等）
            isPlaying = false;
            onComplete?.Invoke();
        }

        // =================================================================
        //  マテリアル設定
        // =================================================================

        private void SetupDissolveMaterials()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var matList = new List<Material>();

            foreach (var rend in renderers)
            {
                Material[] originals = rend.materials;
                Material[] replaced = new Material[originals.Length];

                for (int i = 0; i < originals.Length; i++)
                {
                    var dissolveMat = new Material(dissolveShader);

                    if (originals[i].HasProperty("_MainTex"))
                        dissolveMat.SetTexture("_MainTex", originals[i].GetTexture("_MainTex"));
                    if (originals[i].HasProperty("_Color"))
                        dissolveMat.SetColor("_Color", originals[i].GetColor("_Color"));

                    dissolveMat.SetTexture("_NoiseTex", noiseTexture);
                    dissolveMat.SetFloat("_DissolveAmount", 0f);
                    dissolveMat.SetFloat("_EdgeWidth", edgeWidth);
                    dissolveMat.SetColor("_EdgeColor1", edgeColor);
                    dissolveMat.SetColor("_EdgeColor2", edgeColor);
                    dissolveMat.SetColor("_EdgeColor3", new Color(0f, 0f, 0f, 1f));
                    dissolveMat.SetFloat("_EmissionStrength", emissionStrength);

                    replaced[i] = dissolveMat;
                    matList.Add(dissolveMat);
                }
                rend.materials = replaced;
            }
            dissolveMaterials = matList.ToArray();
        }

        // =================================================================
        //  粉パーティクル
        // =================================================================

        private void CreateDustParticleSystem()
        {
            // テクスチャ色を抽出
            Color[] particleColors = null;
            if (dustUseTextureColor)
            {
                particleColors = ExtractColorsFromTexture();
            }

            Bounds bounds = GetObjectBounds();
            float boundsSize = bounds.size.magnitude;

            dustParticleObj = new GameObject("DustParticles");
            dustParticleObj.transform.position = bounds.center;
            dustParticleSystem = dustParticleObj.AddComponent<ParticleSystem>();

            var main = dustParticleSystem.main;
            main.maxParticles = dustMaxParticles;
            main.startLifetime = dustLifetime;
            main.startSize = dustSize;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f; // ForceOverLifetimeで制御

            // カスタム色 or テクスチャ色
            if (particleColors != null && particleColors.Length > 0)
            {
                var gradient = new Gradient();
                int colorCount = Mathf.Min(particleColors.Length, 8);
                GradientColorKey[] colorKeys = new GradientColorKey[colorCount];
                GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
                for (int i = 0; i < colorCount; i++)
                {
                    colorKeys[i] = new GradientColorKey(particleColors[i], (float)i / (colorCount - 1));
                }
                alphaKeys[0] = new GradientAlphaKey(1f, 0f);
                alphaKeys[1] = new GradientAlphaKey(1f, 1f);
                gradient.SetKeys(colorKeys, alphaKeys);
                main.startColor = new ParticleSystem.MinMaxGradient(gradient);
            }
            else
            {
                main.startColor = dustColor;
            }

            // 放出形状 — オブジェクトの形に合わせた薄いボックス
            var shape = dustParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = bounds.size;

            // 放出レート
            var emission = dustParticleSystem.emission;
            emission.rateOverTime = 0f; // コルーチンで制御

            // カメラ下方向への落下 (ForceOverLifetime)
            var force = dustParticleSystem.forceOverLifetime;
            force.enabled = true;
            if (cameraTransform != null)
            {
                Vector3 camDown = -cameraTransform.up * dustGravity;
                force.x = camDown.x;
                force.y = camDown.y;
                force.z = camDown.z;
            }
            else
            {
                force.y = -dustGravity;
            }

            // 横方向への広がり
            var velocity = dustParticleSystem.velocityOverLifetime;
            velocity.enabled = true;
            float spread = dustSpread;
            velocity.x = new ParticleSystem.MinMaxCurve(-spread, spread);
            velocity.y = new ParticleSystem.MinMaxCurve(-spread * 0.3f, spread * 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(-spread, spread);

            // フェードアウト
            var colorOverLifetime = dustParticleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var alphaGradient = new Gradient();
            alphaGradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = alphaGradient;

            // サイズ縮小
            var sizeOverLifetime = dustParticleSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            // レンダラー設定
            var renderer = dustParticleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
            renderer.material.SetTexture("_MainTex", GenerateCircleDotTexture());
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        // =================================================================
        //  テクスチャ色抽出
        // =================================================================

        private Color[] ExtractColorsFromTexture()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var rend in renderers)
            {
                foreach (var mat in dissolveMaterials)
                {
                    if (mat != null && mat.HasProperty("_MainTex"))
                    {
                        Texture tex = mat.GetTexture("_MainTex");
                        if (tex != null && tex is Texture2D tex2D)
                        {
                            return SampleColorsFromTexture(tex2D, 16);
                        }
                        else if (tex != null)
                        {
                            // RenderTextureからの読み取り
                            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0);
                            Graphics.Blit(tex, rt);
                            RenderTexture prev = RenderTexture.active;
                            RenderTexture.active = rt;

                            Texture2D readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                            readable.Apply();

                            RenderTexture.active = prev;
                            RenderTexture.ReleaseTemporary(rt);

                            Color[] result = SampleColorsFromTexture(readable, 16);
                            Destroy(readable);
                            return result;
                        }
                    }
                }
            }
            return null;
        }

        private Color[] SampleColorsFromTexture(Texture2D tex, int sampleCount)
        {
            Color[] colors = new Color[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int x = Random.Range(0, tex.width);
                int y = Random.Range(0, tex.height);
                Color c = tex.GetPixel(x, y);
                // 少し彩度を下げて粉っぽくする
                float gray = c.grayscale;
                c = Color.Lerp(c, new Color(gray, gray, gray), 0.4f);
                c *= 0.7f; // 少し暗く
                c.a = 1f;
                colors[i] = c;
            }
            return colors;
        }

        // =================================================================
        //  ユーティリティ
        // =================================================================

        private Vector3 GetBoundsCenter()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return transform.position;

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);
            return combined.center;
        }

        private Bounds GetObjectBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.1f);

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);
            return combined;
        }

        private Texture2D GenerateCircleDotTexture()
        {
            int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float center = size / 2f;
            float radius = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(1f - (dist / radius));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return tex;
        }

        private Texture2D GenerateNoiseTexture(int resolution, float scale, float detail, float directionBias)
        {
            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            float offsetX = Random.Range(0f, 1000f);
            float offsetY = Random.Range(0f, 1000f);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float nx = (float)x / resolution * scale + offsetX;
                    float ny = (float)y / resolution * scale + offsetY;
                    float value = Mathf.PerlinNoise(nx, ny);

                    if (detail > 0)
                    {
                        value = value * (1f - detail) + Mathf.PerlinNoise(nx * 3f, ny * 3f) * detail;
                    }

                    tex.SetPixel(x, y, new Color(value, value, value, 1f));
                }
            }
            tex.Apply();
            return tex;
        }

        private void CleanupDustParticles()
        {
            if (dustParticleObj != null)
            {
                // パーティクルが完全に消えるまで待ってから破棄
                float delay = dustLifetime + 0.5f;
                Destroy(dustParticleObj, delay);
                dustParticleObj = null;
                dustParticleSystem = null;
            }
        }

        private void OnDestroy()
        {
            // パーティクルは独立オブジェクトなので遅延破棄
            CleanupDustParticles();
            if (noiseTexture != null)
            {
                Destroy(noiseTexture);
            }
            if (dissolveMaterials != null)
            {
                foreach (var mat in dissolveMaterials)
                {
                    if (mat != null) Destroy(mat);
                }
            }
        }
    }
}
