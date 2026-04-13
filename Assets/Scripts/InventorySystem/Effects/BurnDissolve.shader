Shader "Custom/BurnDissolve"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _NoiseTex ("Dissolve Noise", 2D) = "white" {}
        _DissolveAmount ("Dissolve Amount", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width", Range(0, 0.2)) = 0.05
        _EdgeColor1 ("Edge Color Hot", Color) = (1, 1, 1, 1)
        _EdgeColor2 ("Edge Color Mid", Color) = (1, 1, 1, 1)
        _EdgeColor3 ("Edge Color Cool", Color) = (0, 0, 0, 1)
        _EmissionStrength ("Emission Strength", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NoiseTex;
            fixed4 _Color;
            float _DissolveAmount;
            float _EdgeWidth;
            fixed4 _EdgeColor1;
            fixed4 _EdgeColor2;
            fixed4 _EdgeColor3;
            float _EmissionStrength;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ベースカラー
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;

                // ノイズ値
                float noise = tex2D(_NoiseTex, i.uv).r;

                // ディゾルブ判定
                float dissolveThreshold = _DissolveAmount;

                // ノイズがしきい値以下なら完全に消す
                if (noise < dissolveThreshold - _EdgeWidth)
                {
                    discard;
                }

                // エッジ（燃え境界）の計算
                float edgeFactor = 1.0 - saturate((noise - dissolveThreshold + _EdgeWidth) / _EdgeWidth);

                // 3段グラデーション
                // edgeFactor 0=内側(高温) → 0.3=中間 → 1=外側(黒)
                fixed4 edgeColor;
                float emissionFade;
                if (edgeFactor < 0.3)
                {
                    edgeColor = lerp(_EdgeColor1, _EdgeColor2, edgeFactor / 0.3);
                    emissionFade = 1.0;
                }
                else
                {
                    edgeColor = lerp(_EdgeColor2, _EdgeColor3, (edgeFactor - 0.3) / 0.7);
                    emissionFade = 1.0 - (edgeFactor - 0.3) / 0.7;
                }

                // エッジ判定
                float isEdge = step(dissolveThreshold - _EdgeWidth, noise) * step(noise, dissolveThreshold);

                // 非エッジ: 元テクスチャ色 / エッジ: エッジカラーのみ（Unlit: ライト影響なし）
                fixed3 finalColor = lerp(c.rgb, edgeColor.rgb * _EmissionStrength * emissionFade, isEdge);
                float finalAlpha = lerp(c.a, 1.0 - edgeFactor * 0.6, isEdge);

                return fixed4(finalColor, finalAlpha);
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
