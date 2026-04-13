// TMPro用 完全Unlitシェーダー
// - ライティング完全無視
// - 標準アルファブレンド（premultiply なし）
// - 頂点カラーがそのまま表示される

Shader "Custom/TMP_SDF_Unlit" {

Properties {
    _FaceColor      ("Face Color", Color) = (1,1,1,1)
    _FaceDilate     ("Face Dilate", Range(-1,1)) = 0

    _OutlineColor   ("Outline Color", Color) = (0,0,0,1)
    _OutlineWidth   ("Outline Thickness", Range(0,1)) = 0
    _OutlineSoftness("Outline Softness", Range(0,1)) = 0

    _UnderlayColor  ("Border Color", Color) = (0,0,0,.5)
    _UnderlayOffsetX("Border OffsetX", Range(-1,1)) = 0
    _UnderlayOffsetY("Border OffsetY", Range(-1,1)) = 0
    _UnderlayDilate ("Border Dilate", Range(-1,1)) = 0
    _UnderlaySoftness("Border Softness", Range(0,1)) = 0

    _WeightNormal   ("Weight Normal", float) = 0
    _WeightBold     ("Weight Bold", float) = .5

    _ShaderFlags    ("Flags", float) = 0
    _ScaleRatioA    ("Scale RatioA", float) = 1
    _ScaleRatioB    ("Scale RatioB", float) = 1
    _ScaleRatioC    ("Scale RatioC", float) = 1

    _MainTex        ("Font Atlas", 2D) = "white" {}
    _TextureWidth   ("Texture Width", float) = 512
    _TextureHeight  ("Texture Height", float) = 512
    _GradientScale  ("Gradient Scale", float) = 5
    _ScaleX         ("Scale X", float) = 1
    _ScaleY         ("Scale Y", float) = 1
    _PerspectiveFilter("Perspective Correction", Range(0, 1)) = 0.875
    _Sharpness      ("Sharpness", Range(-1,1)) = 0

    _VertexOffsetX  ("Vertex OffsetX", float) = 0
    _VertexOffsetY  ("Vertex OffsetY", float) = 0

    _ClipRect       ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
    _MaskSoftnessX  ("Mask SoftnessX", float) = 0
    _MaskSoftnessY  ("Mask SoftnessY", float) = 0

    _StencilComp    ("Stencil Comparison", Float) = 8
    _Stencil        ("Stencil ID", Float) = 0
    _StencilOp      ("Stencil Operation", Float) = 0
    _StencilWriteMask("Stencil Write Mask", Float) = 255
    _StencilReadMask("Stencil Read Mask", Float) = 255

    _CullMode       ("Cull Mode", Float) = 0
    _ColorMask      ("Color Mask", Float) = 15
}

SubShader {
    Tags
    {
        "Queue"="Transparent"
        "IgnoreProjector"="True"
        "RenderType"="Transparent"
    }

    Stencil
    {
        Ref [_Stencil]
        Comp [_StencilComp]
        Pass [_StencilOp]
        ReadMask [_StencilReadMask]
        WriteMask [_StencilWriteMask]
    }

    Cull [_CullMode]
    ZWrite Off
    Lighting Off
    Fog { Mode Off }
    ZTest [unity_GUIZTestMode]
    Blend SrcAlpha OneMinusSrcAlpha
    ColorMask [_ColorMask]

    Pass {
        CGPROGRAM
        #pragma vertex VertShader
        #pragma fragment PixShader

        #pragma multi_compile __ UNITY_UI_CLIP_RECT
        #pragma multi_compile __ UNITY_UI_ALPHACLIP

        #include "UnityCG.cginc"
        #include "UnityUI.cginc"

        // --- プロパティ宣言 ---
        uniform sampler2D _MainTex;
        uniform float     _TextureWidth;
        uniform float     _TextureHeight;
        uniform float     _GradientScale;
        uniform float     _ScaleX;
        uniform float     _ScaleY;
        uniform float     _PerspectiveFilter;
        uniform float     _Sharpness;
        uniform float     _WeightNormal;
        uniform float     _WeightBold;
        uniform float     _ScaleRatioA;
        uniform float     _FaceDilate;
        uniform float     _OutlineWidth;
        uniform float     _OutlineSoftness;
        uniform fixed4    _FaceColor;
        uniform fixed4    _OutlineColor;
        uniform float     _VertexOffsetX;
        uniform float     _VertexOffsetY;
        uniform float4    _ClipRect;
        uniform float     _MaskSoftnessX;
        uniform float     _MaskSoftnessY;

        struct vertex_t {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 vertex    : POSITION;
            float3 normal    : NORMAL;
            fixed4 color     : COLOR;
            float2 texcoord0 : TEXCOORD0;
            float2 texcoord1 : TEXCOORD1;
        };

        struct pixel_t {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
            float4 vertex    : SV_POSITION;
            fixed4 faceColor : COLOR;
            float2 uv        : TEXCOORD0;
            half4  param     : TEXCOORD1;
            half4  mask      : TEXCOORD2;
        };

        pixel_t VertShader(vertex_t input)
        {
            pixel_t output;
            UNITY_INITIALIZE_OUTPUT(pixel_t, output);
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float bold = step(input.texcoord1.y, 0);

            float4 vert = input.vertex;
            vert.x += _VertexOffsetX;
            vert.y += _VertexOffsetY;
            float4 vPosition = UnityObjectToClipPos(vert);

            float2 pixelSize = vPosition.w;
            pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

            float scale = rsqrt(dot(pixelSize, pixelSize));
            scale *= abs(input.texcoord1.y) * _GradientScale * (_Sharpness + 1);
            if (UNITY_MATRIX_P[3][3] == 0)
                scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale,
                    abs(dot(UnityObjectToWorldNormal(input.normal.xyz), normalize(WorldSpaceViewDir(vert)))));

            float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
            weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;

            scale /= 1 + (_OutlineSoftness * _ScaleRatioA * scale);
            float bias = (0.5 - weight) * scale - 0.5;

            // 頂点カラー × _FaceColor（premultiply しない）
            fixed4 faceColor = fixed4(input.color.rgb, input.color.a) * _FaceColor;

            float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);

            output.vertex    = vPosition;
            output.faceColor = faceColor;
            output.uv        = input.texcoord0;
            output.param     = half4(scale, bias, 0, 0);
            output.mask      = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw,
                                     0.25 / (0.25 * half2(_MaskSoftnessX, _MaskSoftnessY) + pixelSize.xy));

            return output;
        }

        fixed4 PixShader(pixel_t input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            // SDF値を取得
            half d = tex2D(_MainTex, input.uv).a * input.param.x;
            // SDFマスク（グリフ内部 = 1、外側 = 0）
            half mask = saturate(d - input.param.y);

            // 最終色 = 頂点カラー×_FaceColor そのまま、alphaにマスク適用
            fixed4 c = input.faceColor;
            c.a *= mask;

            #if UNITY_UI_CLIP_RECT
            half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
            c.a *= m.x * m.y;
            #endif

            #if UNITY_UI_ALPHACLIP
            clip(c.a - 0.001);
            #endif

            return c;
        }
        ENDCG
    }
}

Fallback "TextMeshPro/Mobile/Distance Field"
}
