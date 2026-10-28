// 2D 画面の仕上げ (2026-10-19)。 液晶 (地図) と外枠の GUI の上に 1 枚かぶせる (Overlay Canvas の RawImage 用・Built-in RP)。
// 正本は docs/design/map-prototype.html の LFX。
//   ビネット / 画面のフチ (縁から地の色へ寄せる) / 寒色の乗算 / 黒の持ち上げ
// 画素の格子・横線ノイズ・残像・湾曲・にじみは、 地図だけに掛かるので液晶の Quad 側 (Game2DLcdScreen.shader) にある。
Shader "Game2D/LcdFx"
{
    Properties
    {
        [HideInInspector] _MainTex ("RawImage が書き込むだけ (使わない)", 2D) = "white" {}
        _Ground ("地の色 (フチ・ビネット)", Color) = (0.043, 0.039, 0.035, 1)
        _Vignette ("ビネットの濃さ", Range(0, 1)) = 0.30
        _EdgeW ("フチの幅 (GUI dot)", Float) = 14
        _EdgeA ("フチの濃さ", Range(0, 1)) = 0.70
        _Cool ("寒色の乗算 (白の青み・赤を落とす)", Color) = (0.90, 0.96, 1.0, 1)
        _Lift ("黒の持ち上げ (青緑の加算)", Color) = (0.012, 0.026, 0.045, 1)
        _Reflect ("ガラスの映り込み (斜めの光の帯)", Range(0, 0.2)) = 0.05
        _RtSize ("RT の大きさ", Vector) = (960, 540, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always

        // 1: ビネット・フチ
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Ground;
            float _Vignette, _EdgeW, _EdgeA;
            float4 _RtSize;

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 dots = uv * (_RtSize.xy * 0.5);             // GUI dot (RT の 2 画素)

                // ビネット: 中心から離れるほど地の色へ
                float2 c = (uv - 0.5) * 2.0;
                float r = length(c * float2(0.92, 1.0));
                float vig = _Vignette * smoothstep(0.55, 1.45, r);

                // 画面のフチ: 縁から _EdgeW dot の帯を、 縁に向かって地の色へゆっくり寄せる
                float d = min(min(dots.x, _RtSize.x * 0.5 - dots.x), min(dots.y, _RtSize.y * 0.5 - dots.y));
                float t = saturate(1.0 - d / _EdgeW);
                float edge = _EdgeA * t * t;

                float a = 1.0 - (1.0 - vig) * (1.0 - edge);
                return fixed4(_Ground.rgb, a);
            }
            ENDCG
        }

        // 2: 寒色の乗算 (白を青白く・赤と黄を落とす)
        Pass
        {
            Blend DstColor Zero
            CGPROGRAM
            #pragma vertex vert2
            #pragma fragment frag2
            #include "UnityCG.cginc"
            fixed4 _Cool;
            float4 vert2 (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag2 () : SV_Target { return fixed4(_Cool.rgb, 1); }
            ENDCG
        }

        // 4: ガラスの映り込み: 左上から斜めに走る淡い光の帯と、 左上の隅のぼんやりした反射。 加算 (上ほど強く、 下は弱い)
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag4
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            float _Reflect;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag4 (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = uv.x + (1.0 - uv.y) * 0.75;                              // 左上 = 0 → 右下へ
                float band = exp(-pow((t - 0.62) / 0.13, 2.0)) * 0.55 + exp(-pow((t - 0.93) / 0.045, 2.0)) * 0.35;
                float top = lerp(0.35, 1.0, uv.y);                                    // 上ほど強い
                float corner = exp(-dot(uv - float2(0.04, 0.96), uv - float2(0.04, 0.96)) / 0.07) * 0.5;
                float v = (band * top + corner) * _Reflect;
                return fixed4(float3(0.86, 0.94, 1.0) * v, 0);
            }
            ENDCG
        }

        // 3: 黒の持ち上げ (真っ黒でなく、 青緑がかった黒)
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert2
            #pragma fragment frag3
            #include "UnityCG.cginc"
            fixed4 _Lift;
            float4 vert2 (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag3 () : SV_Target { return fixed4(_Lift.rgb, 0); }
            ENDCG
        }
    }
}
