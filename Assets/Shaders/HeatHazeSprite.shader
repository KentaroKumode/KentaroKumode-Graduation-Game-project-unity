// 背景スプライト専用の「陽炎(ヒートヘイズ)」シェーダ（Built-in RP / SpriteRenderer 用）。
// このマテリアルを貼ったスプライトだけが揺れる（タイトル等の別スプライトには影響しない）。
// 既定では「上ほど強く・下ほど弱い」（_HazeHeight で上端からの及ぶ範囲を指定）。
Shader "Sprites/HeatHaze"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _HazeStrength ("陽炎強さ (UV揺らぎ量)", Range(0, 0.03)) = 0.005
        _HazeSpeed ("陽炎速度", Float) = 2.0
        _HazeFreq ("陽炎周波数 (縦の波数)", Float) = 38
        _HazeHeight ("陽炎の及ぶ範囲 (0..1・上端から)", Range(0.05, 1)) = 0.55
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
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _HazeStrength;
            float _HazeSpeed;
            float _HazeFreq;
            float _HazeHeight;

            v2f vert (appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                // 上端(uv.y=1)ほど強い陽炎。 _HazeHeight ぶんだけ上から効かせる。 横にサイン揺らし(2波合成)。
                float mask = saturate((uv.y - (1.0 - _HazeHeight)) / max(1e-4, _HazeHeight));
                float w = sin(uv.y * _HazeFreq + _Time.y * _HazeSpeed)
                        + 0.5 * sin(uv.y * _HazeFreq * 2.3 + _Time.y * _HazeSpeed * 1.7);
                uv.x += w * _HazeStrength * mask;

                fixed4 c = tex2D(_MainTex, uv) * IN.color;
                c.rgb *= c.a; // premultiplied（Blend One OneMinusSrcAlpha）
                return c;
            }
            ENDCG
        }
    }
}
