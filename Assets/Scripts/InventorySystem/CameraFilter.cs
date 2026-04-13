using UnityEngine;

/// <summary>
/// カメラにアタッチしてブルーム＋ビネット＋フィルムグレイン＋カラーグレーディングを適用する
/// Built-in RP専用（OnRenderImage使用）
/// カメラのHDRを有効にするとダイスLED等のEmissionがブルームで光る
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class CameraFilter : MonoBehaviour
{
    // ─── ブルーム ───
    [Header("ブルーム")]
    [Tooltip("ブルームの有効/無効")]
    public bool bloomEnabled = true;

    [Tooltip("輝度閾値（これを超えるピクセルが光る）")]
    [Range(0.1f, 5f)]
    public float bloomThreshold = 0.9f;

    [Tooltip("閾値付近の滑らかさ")]
    [Range(0f, 1f)]
    public float bloomSoftKnee = 0.5f;

    [Tooltip("ブルームの強さ")]
    [Range(0f, 10f)]
    public float bloomIntensity = 1.5f;

    [Tooltip("ブラー繰り返し回数（多い＝広い光）")]
    [Range(1, 8)]
    public int bloomIterations = 4;

    // ─── ビネット ───
    [Header("ビネット")]
    [Tooltip("ビネットの強さ（0で無効）")]
    [Range(0f, 1f)]
    public float vignetteIntensity = 0.25f;

    [Tooltip("ビネットのぼかし範囲")]
    [Range(0.01f, 1f)]
    public float vignetteSmoothness = 0.4f;

    [Tooltip("ビネットの色")]
    public Color vignetteColor = Color.black;

    // ─── フィルムグレイン ───
    [Header("フィルムグレイン")]
    [Tooltip("グレインの強さ（0で無効）")]
    [Range(0f, 0.5f)]
    public float grainIntensity = 0.04f;

    [Tooltip("グレインの粒サイズ")]
    [Range(0.5f, 5f)]
    public float grainSize = 1.5f;

    // ─── カラーグレーディング ───
    [Header("カラーグレーディング")]
    [Tooltip("明るさ調整")]
    [Range(-0.5f, 0.5f)]
    public float brightness = 0f;

    [Tooltip("コントラスト")]
    [Range(0.5f, 2f)]
    public float contrast = 1.05f;

    [Tooltip("彩度")]
    [Range(0f, 2f)]
    public float saturation = 1.1f;

    [Tooltip("色温度（正=暖色、負=寒色）")]
    [Range(-1f, 1f)]
    public float temperature = 0.05f;

    [Tooltip("ティントカラー（乗算）")]
    public Color tintColor = Color.white;

    [Tooltip("ガンマ補正")]
    [Range(0.5f, 2f)]
    public float gamma = 1f;

    // ─── 内部 ───
    private Material filterMaterial;
    private Shader filterShader;

    // シェーダーパスインデックス
    private const int PASS_COMPOSITE    = 0;
    private const int PASS_EXTRACT      = 1;
    private const int PASS_BLUR         = 2;
    private const int PASS_DOWNSAMPLE   = 3;
    private const int PASS_UPSAMPLE     = 4;

    // シェーダープロパティID（キャッシュ）
    private static readonly int _BloomTex = Shader.PropertyToID("_BloomTex");
    private static readonly int _BloomThreshold = Shader.PropertyToID("_BloomThreshold");
    private static readonly int _BloomSoftKnee = Shader.PropertyToID("_BloomSoftKnee");
    private static readonly int _BloomIntensity = Shader.PropertyToID("_BloomIntensity");
    private static readonly int _BlurDirection = Shader.PropertyToID("_BlurDirection");
    private static readonly int _VignetteIntensity = Shader.PropertyToID("_VignetteIntensity");
    private static readonly int _VignetteSmoothness = Shader.PropertyToID("_VignetteSmoothness");
    private static readonly int _VignetteColor = Shader.PropertyToID("_VignetteColor");
    private static readonly int _GrainIntensity = Shader.PropertyToID("_GrainIntensity");
    private static readonly int _GrainSize = Shader.PropertyToID("_GrainSize");
    private static readonly int _Brightness = Shader.PropertyToID("_Brightness");
    private static readonly int _Contrast = Shader.PropertyToID("_Contrast");
    private static readonly int _Saturation = Shader.PropertyToID("_Saturation");
    private static readonly int _Temperature = Shader.PropertyToID("_Temperature");
    private static readonly int _Tint = Shader.PropertyToID("_Tint");
    private static readonly int _Gamma = Shader.PropertyToID("_Gamma");

    private void OnEnable()
    {
        EnsureMaterial();

        // HDRが無効ならブルームが効かないので警告
        var cam = GetComponent<Camera>();
        if (cam != null && !cam.allowHDR && bloomEnabled)
        {
            Debug.LogWarning("[CameraFilter] カメラのHDRが無効です。ブルームを効かせるにはHDRを有効にしてください");
        }
    }

    private void OnDisable()
    {
        if (filterMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(filterMaterial);
            else
                DestroyImmediate(filterMaterial);
            filterMaterial = null;
        }
    }

    private bool EnsureMaterial()
    {
        if (filterMaterial != null) return true;

        filterShader = Shader.Find("Hidden/CameraFilter");
        if (filterShader == null)
        {
            Debug.LogError("[CameraFilter] シェーダー 'Hidden/CameraFilter' が見つかりません");
            enabled = false;
            return false;
        }

        if (!filterShader.isSupported)
        {
            Debug.LogError("[CameraFilter] シェーダーがこのプラットフォームで非対応です");
            enabled = false;
            return false;
        }

        filterMaterial = new Material(filterShader);
        filterMaterial.hideFlags = HideFlags.HideAndDontSave;
        return true;
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!EnsureMaterial())
        {
            Graphics.Blit(source, destination);
            return;
        }

        // --- ブルーム処理 ---
        RenderTexture bloomRT = null;

        if (bloomEnabled && bloomIntensity > 0f)
        {
            int w = source.width / 2;
            int h = source.height / 2;
            RenderTextureFormat fmt = source.format;

            // 1) 輝度抽出 + ダウンサンプル
            filterMaterial.SetFloat(_BloomThreshold, bloomThreshold);
            filterMaterial.SetFloat(_BloomSoftKnee, bloomSoftKnee);

            RenderTexture current = RenderTexture.GetTemporary(w, h, 0, fmt);
            current.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, current, filterMaterial, PASS_EXTRACT);

            // ダウンサンプルのピラミッドを保持（アップサンプル時に使う）
            int iterations = Mathf.Min(bloomIterations, 8);
            RenderTexture[] pyramid = new RenderTexture[iterations];
            pyramid[0] = current;

            // 2) ダウンサンプルチェーン
            for (int i = 1; i < iterations; i++)
            {
                w = Mathf.Max(w / 2, 1);
                h = Mathf.Max(h / 2, 1);
                RenderTexture down = RenderTexture.GetTemporary(w, h, 0, fmt);
                down.filterMode = FilterMode.Bilinear;
                Graphics.Blit(pyramid[i - 1], down, filterMaterial, PASS_DOWNSAMPLE);
                pyramid[i] = down;
            }

            // 3) 最下段にガウシアンブラー（H + V）
            current = pyramid[iterations - 1];
            {
                RenderTexture blurTemp = RenderTexture.GetTemporary(current.width, current.height, 0, fmt);
                blurTemp.filterMode = FilterMode.Bilinear;

                // 水平ブラー
                filterMaterial.SetVector(_BlurDirection, new Vector4(1, 0, 0, 0));
                Graphics.Blit(current, blurTemp, filterMaterial, PASS_BLUR);

                // 垂直ブラー
                filterMaterial.SetVector(_BlurDirection, new Vector4(0, 1, 0, 0));
                Graphics.Blit(blurTemp, current, filterMaterial, PASS_BLUR);

                RenderTexture.ReleaseTemporary(blurTemp);
            }

            // 4) アップサンプルチェーン（加算合成で戻す）
            for (int i = iterations - 2; i >= 0; i--)
            {
                RenderTexture low = current;
                RenderTexture high = pyramid[i];

                // low を high サイズにアップサンプル＋加算
                // まず high に直接加算ブレンド
                Graphics.Blit(low, high, filterMaterial, PASS_UPSAMPLE);

                // 各段にも H+V ブラーを軽くかける
                {
                    RenderTexture blurTemp = RenderTexture.GetTemporary(high.width, high.height, 0, fmt);
                    blurTemp.filterMode = FilterMode.Bilinear;

                    filterMaterial.SetVector(_BlurDirection, new Vector4(1, 0, 0, 0));
                    Graphics.Blit(high, blurTemp, filterMaterial, PASS_BLUR);

                    filterMaterial.SetVector(_BlurDirection, new Vector4(0, 1, 0, 0));
                    Graphics.Blit(blurTemp, high, filterMaterial, PASS_BLUR);

                    RenderTexture.ReleaseTemporary(blurTemp);
                }

                // low はもう不要（pyramid[0]以外は解放、0は最後に使うので残す）
                if (i < iterations - 2)
                    RenderTexture.ReleaseTemporary(low);

                current = high;
            }

            bloomRT = current; // pyramid[0] — 最終ブルームテクスチャ

            // ダウンサンプル途中のRTを解放（pyramid[0]以外）
            for (int i = 1; i < iterations; i++)
            {
                if (pyramid[i] != bloomRT)
                    RenderTexture.ReleaseTemporary(pyramid[i]);
            }
        }

        // --- フィルター合成パス ---
        filterMaterial.SetFloat(_VignetteIntensity, vignetteIntensity);
        filterMaterial.SetFloat(_VignetteSmoothness, vignetteSmoothness);
        filterMaterial.SetColor(_VignetteColor, vignetteColor);

        filterMaterial.SetFloat(_GrainIntensity, grainIntensity);
        filterMaterial.SetFloat(_GrainSize, grainSize);

        filterMaterial.SetFloat(_Brightness, brightness);
        filterMaterial.SetFloat(_Contrast, contrast);
        filterMaterial.SetFloat(_Saturation, saturation);
        filterMaterial.SetFloat(_Temperature, temperature);
        filterMaterial.SetColor(_Tint, tintColor);
        filterMaterial.SetFloat(_Gamma, gamma);

        if (bloomRT != null)
        {
            filterMaterial.SetTexture(_BloomTex, bloomRT);
            filterMaterial.SetFloat(_BloomIntensity, bloomIntensity);
        }
        else
        {
            filterMaterial.SetFloat(_BloomIntensity, 0f);
        }

        Graphics.Blit(source, destination, filterMaterial, PASS_COMPOSITE);

        // ブルームRT解放
        if (bloomRT != null)
            RenderTexture.ReleaseTemporary(bloomRT);
    }
}
