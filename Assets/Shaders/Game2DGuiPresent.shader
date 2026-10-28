// 外枠の GUI (パネル・入れ物・レーダー・バー) を液晶の上に重ねる (2026-10-19)。 正本は docs/design/map-prototype.html の「液晶の合成」の GUI の部分。
// GUI は専用のカメラで 960×540 の RT (_GuiTex・透明な地) に描いてあり、 ここで
//   ① 上が少し明るく下が暗い (LFX.shade)  ② 液晶へ落ちる影: 接地の影 (まわり全体に柔らかく・aoA) + 下へ長く伸びる影 (shadowLen・dy)
// を付けて重ねる。 影は GUI の輪郭 (alpha) から作る = GUI が動けば (入れ物が開く等) 影も一緒に動く。
// RT の alpha は、 UI の通常の合成 (SrcAlpha, OneMinusSrcAlpha) で書かれるので、 かぶり率 = sqrt(alpha)・色は (かぶり率を掛けた色) として読む。
Shader "Game2D/GuiPresent"
{
    Properties
    {
        _GuiTex ("GUI (RT)", 2D) = "black" {}
        _RtSize ("RT の大きさ", Vector) = (960, 540, 0, 0)
        _ShadowA ("下へ伸びる影の濃さ", Range(0, 1)) = 1.0
        _AoA ("接地の影の濃さ", Range(0, 1)) = 0.22
        _ShadowLen ("影の長さ (dot)", Float) = 14
        _ShadowDy ("影のずれ (dot)", Float) = 2
        _ShadeTop ("上の暗さ", Range(0, 1)) = 0.0
        _ShadeBottom ("下の暗さ", Range(0, 1)) = 0.20
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _GuiTex;
            float4 _RtSize;
            float _ShadowA, _AoA, _ShadowLen, _ShadowDy, _ShadeTop, _ShadeBottom;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float Cov(float2 uv) { return sqrt(saturate(tex2D(_GuiTex, uv).a)); }

            // 下へ長く伸びる影: 上にある GUI の輪郭を、 距離が遠いほど薄く
            float Drop(float2 uv, float2 dot1)
            {
                float m = 0;
                for (int i = 0; i <= 14; i++)
                {
                    float f = 1.0 - i / (_ShadowLen + 1.0);
                    m = max(m, Cov(uv + float2(0, (i + _ShadowDy) * dot1.y)) * f);
                }
                return m;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 dot1 = 2.0 / _RtSize.xy;                 // GUI の 1 dot (RT の 2 画素) の uv
                float4 g = tex2D(_GuiTex, i.uv);
                float cov = sqrt(saturate(g.a));

                // 下へ伸びる影 (横に少しぼかす: 3 dot)
                float d = Drop(i.uv, dot1) * 0.4
                        + Drop(i.uv + float2( 1.5 * dot1.x, 0), dot1) * 0.2 + Drop(i.uv + float2(-1.5 * dot1.x, 0), dot1) * 0.2
                        + Drop(i.uv + float2(0,  1.5 * dot1.y), dot1) * 0.1 + Drop(i.uv + float2(0, -1.5 * dot1.y), dot1) * 0.1;
                // 接地の影: まわり全体に柔らかく (半径 3 と 6 dot の輪の平均)
                float ao = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.785398; float2 dir = float2(cos(a), sin(a)) * dot1;
                    ao += Cov(i.uv + dir * 3.0) + Cov(i.uv + dir * 6.0);
                }
                ao /= 16.0;
                float shadow = 1.0 - (1.0 - saturate(ao) * _AoA) * (1.0 - saturate(d) * _ShadowA);

                // GUI の色: 上が少し明るく下が暗い (色はかぶり率を掛けてある = 色 × 黒の係数でそのまま暗くできる)
                float t = 1.0 - i.uv.y;                           // 上 = 0 / 下 = 1
                float3 rgb = g.rgb * (1.0 - lerp(_ShadeTop, _ShadeBottom, t));

                // 合成: 色は GUI だけ (影は黒 = 色に足さず alpha だけ)
                float a = cov + shadow * (1.0 - cov);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}
