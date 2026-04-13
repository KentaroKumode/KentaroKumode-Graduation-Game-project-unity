Shader "Hidden/CameraFilter"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // ブルーム
        _BloomTex ("Bloom Texture", 2D) = "black" {}
        _BloomThreshold ("Bloom Threshold", Float) = 1.0
        _BloomIntensity ("Bloom Intensity", Float) = 1.0
        _BloomSoftKnee ("Bloom Soft Knee", Float) = 0.5
        _BlurDirection ("Blur Direction", Vector) = (1, 0, 0, 0)
        
        // ビネット
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.3
        _VignetteSmoothness ("Vignette Smoothness", Range(0.01, 1)) = 0.4
        _VignetteColor ("Vignette Color", Color) = (0, 0, 0, 1)
        
        // フィルムグレイン
        _GrainIntensity ("Grain Intensity", Range(0, 0.5)) = 0.05
        _GrainSize ("Grain Size", Range(0.5, 5)) = 1.5
        
        // カラーグレーディング
        _Brightness ("Brightness", Range(-0.5, 0.5)) = 0
        _Contrast ("Contrast", Range(0.5, 2)) = 1
        _Saturation ("Saturation", Range(0, 2)) = 1
        _Temperature ("Temperature", Range(-1, 1)) = 0
        _Tint ("Tint Color", Color) = (1, 1, 1, 1)
        _Gamma ("Gamma", Range(0.5, 2)) = 1
    }

    // ─── 共通設定 ───
    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float2 uv : TEXCOORD0;
        float4 vertex : SV_POSITION;
    };

    sampler2D _MainTex;
    float4 _MainTex_TexelSize; // (1/w, 1/h, w, h)

    v2f vert (appdata v)
    {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        return o;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        // ═══════════════════════════════════════════════════
        // Pass 0: フィルター合成（ビネット＋グレイン＋カラグレ＋ブルーム合成）
        // ═══════════════════════════════════════════════════
        Pass
        {
            Name "COMPOSITE"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite

            sampler2D _BloomTex;
            float _BloomIntensity;

            // ビネット
            float _VignetteIntensity;
            float _VignetteSmoothness;
            float4 _VignetteColor;
            
            // フィルムグレイン
            float _GrainIntensity;
            float _GrainSize;
            
            // カラーグレーディング
            float _Brightness;
            float _Contrast;
            float _Saturation;
            float _Temperature;
            float4 _Tint;
            float _Gamma;

            // ハッシュベースノイズ
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            fixed4 fragComposite (v2f i) : SV_Target
            {
                float3 rgb = tex2D(_MainTex, i.uv).rgb;

                // === ブルーム合成 ===
                float3 bloom = tex2D(_BloomTex, i.uv).rgb;
                rgb += bloom * _BloomIntensity;

                // === カラーグレーディング ===
                rgb += _Brightness;
                rgb = (rgb - 0.5) * _Contrast + 0.5;

                float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                rgb = lerp(float3(luma, luma, luma), rgb, _Saturation);

                rgb.r += _Temperature * 0.1;
                rgb.g += _Temperature * 0.03;
                rgb.b -= _Temperature * 0.1;

                rgb *= _Tint.rgb;
                rgb = pow(max(rgb, 0.0001), 1.0 / _Gamma);

                // === フィルムグレイン ===
                if (_GrainIntensity > 0)
                {
                    float2 grainUV = i.uv * _ScreenParams.xy / _GrainSize;
                    float noise = hash(grainUV + frac(_Time.y * 7.31)) * 2.0 - 1.0;
                    rgb += noise * _GrainIntensity;
                }

                // === ビネット ===
                if (_VignetteIntensity > 0)
                {
                    float2 d = abs(i.uv - 0.5) * 2.0;
                    float vignette = dot(d, d);
                    vignette = smoothstep(1.0 - _VignetteSmoothness, 1.0, vignette * _VignetteIntensity * 2.0);
                    rgb = lerp(rgb, _VignetteColor.rgb, vignette);
                }

                return fixed4(saturate(rgb), 1.0);
            }
            ENDCG
        }

        // ═══════════════════════════════════════════════════
        // Pass 1: 輝度抽出（ブルーム用 — HDR閾値フィルタ）
        // ═══════════════════════════════════════════════════
        Pass
        {
            Name "BLOOM_EXTRACT"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragExtract

            float _BloomThreshold;
            float _BloomSoftKnee;

            half4 fragExtract (v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv);
                half brightness = max(col.r, max(col.g, col.b));

                // ソフトニー: 閾値付近を滑らかに減衰
                half knee = _BloomThreshold * _BloomSoftKnee;
                half soft = brightness - _BloomThreshold + knee;
                soft = clamp(soft, 0.0, 2.0 * knee);
                soft = soft * soft / (4.0 * knee + 0.00001);

                half contribution = max(soft, brightness - _BloomThreshold);
                contribution /= max(brightness, 0.00001);

                return col * contribution;
            }
            ENDCG
        }

        // ═══════════════════════════════════════════════════
        // Pass 2: ガウシアンブラー（方向指定― H/V兼用）
        // ═══════════════════════════════════════════════════
        Pass
        {
            Name "BLOOM_BLUR"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur

            float4 _BlurDirection; // (1,0,0,0)=水平, (0,1,0,0)=垂直

            // 9-tap ガウシアン: ウェイト [0.227027, 0.194594, 0.121621, 0.054054, 0.016216]
            static const float weights[5] = {
                0.227027, 0.194594, 0.121621, 0.054054, 0.016216
            };

            half4 fragBlur (v2f i) : SV_Target
            {
                float2 texelSize = _MainTex_TexelSize.xy * _BlurDirection.xy;
                half4 result = tex2D(_MainTex, i.uv) * weights[0];

                [unroll]
                for (int j = 1; j < 5; j++)
                {
                    float2 offset = texelSize * j;
                    result += tex2D(_MainTex, i.uv + offset) * weights[j];
                    result += tex2D(_MainTex, i.uv - offset) * weights[j];
                }

                return result;
            }
            ENDCG
        }

        // ═══════════════════════════════════════════════════
        // Pass 3: ダウンサンプル（4tapバイリニア平均）
        // ═══════════════════════════════════════════════════
        Pass
        {
            Name "BLOOM_DOWNSAMPLE"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown

            half4 fragDown (v2f i) : SV_Target
            {
                float2 ts = _MainTex_TexelSize.xy * 0.5;
                half4 s = 0;
                s += tex2D(_MainTex, i.uv + float2(-ts.x, -ts.y));
                s += tex2D(_MainTex, i.uv + float2( ts.x, -ts.y));
                s += tex2D(_MainTex, i.uv + float2(-ts.x,  ts.y));
                s += tex2D(_MainTex, i.uv + float2( ts.x,  ts.y));
                return s * 0.25;
            }
            ENDCG
        }

        // ═══════════════════════════════════════════════════
        // Pass 4: アップサンプル＋加算合成
        // ═══════════════════════════════════════════════════
        Pass
        {
            Name "BLOOM_UPSAMPLE"
            Blend One One // 加算ブレンド
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp

            half4 fragUp (v2f i) : SV_Target
            {
                // バイリニア補間でそのまま拡大（GPUが補間）
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
