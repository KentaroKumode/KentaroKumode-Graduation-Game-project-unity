// 背景スプライト用の「コントラスト調整」シェーダ（Built-in RP / SpriteRenderer 用）。
// MoonlightPulse からの MaterialPropertyBlock で _Contrast を脈動させて、 暗部はより暗く・明部はより明るく動かす。
// 純粋な明度倍ではなく、 ピクセル毎に (rgb − 0.5) * contrast + 0.5 を適用。
Shader "Sprites/Contrast"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Contrast ("コントラスト (1=変化なし)", Range(0.5, 2.0)) = 1.0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f       { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Contrast;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.color = IN.color * _Color;
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                // premultiplied alpha (Blend One OneMinusSrcAlpha) を維持するため、
                // 一旦 straight にしてからコントラスト → 再び premultiply。
                fixed a = c.a;
                fixed3 rgb = a > 0.0001 ? c.rgb / a : c.rgb;
                rgb = (rgb - 0.5) * _Contrast + 0.5;
                rgb = saturate(rgb);
                return fixed4(rgb * a, a);
            }
            ENDCG
        }
    }
}
