// 液晶（LCD）サーフェス用 Unlit シェーダ（Built-in RP）。
// LcdScreen が流し込む RenderTexture(_MainTex) を等倍ドットのまま表示し、 横走査線を重畳する。
// ピクセルは Point サンプル前提（RT 側 FilterMode=Point）でくっきり維持。
// ※陽炎(ヒートヘイズ)は「背景スプライト専用マテリアル(Sprites/HeatHaze)」側で行う（タイトルに掛けないため）。
Shader "Lcd/Scanline"
{
    Properties
    {
        _MainTex ("LCD (RenderTexture)", 2D) = "black" {}
        _Tint ("色味 (LCDの色温度)", Color) = (1,1,1,1)
        _Brightness ("明度補正 (走査線の暗化を相殺)", Range(0.5, 2.0)) = 1.12
        _ScanlinePixels ("走査線の間隔 (px・1暗線あたりの画素数)", Float) = 3
        _NativeHeight ("ネイティブ縦解像度 (px・間隔→本数換算用)", Float) = 556
        _ScanlineStrength ("走査線の濃さ", Range(0, 1)) = 0.35
        _ScanlineSharp ("走査線の鋭さ", Range(0.2, 4.0)) = 1.4
        _ScanlineScroll ("走査線の縦スクロール速度 (0=静止)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _Brightness;
            float _ScanlinePixels;
            float _NativeHeight;
            float _ScanlineStrength;
            float _ScanlineSharp;
            float _ScanlineScroll;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // 横走査線: 「間隔(px)」から本数を換算（本数 = 縦解像度 / 間隔）して uv.y を刻み、 行境界を暗く。
                float count = _NativeHeight / max(1.0, _ScanlinePixels);
                float phase = i.uv.y * count + _Time.y * _ScanlineScroll;
                float tri = abs(frac(phase) - 0.5) * 2.0;      // 行境界=1 / 行中心=0
                float lineDark = pow(tri, _ScanlineSharp);     // 鋭さで線幅調整
                float darken = 1.0 - _ScanlineStrength * lineDark;

                col.rgb *= darken * _Brightness;
                col.rgb *= _Tint.rgb;
                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
