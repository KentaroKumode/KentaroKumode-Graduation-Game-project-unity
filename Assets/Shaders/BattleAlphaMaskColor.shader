// 黒透過スプライトをアルファマスクとして使い、 _Color をそのまま色として描く。
// 真っ黒 (rgb=0,a=1) の素材を任意の色に染められる。
// 用途: 武器エフェクト・FX
Shader "Battle/AlphaMaskColor"
{
    Properties
    {
        _MainTex ("Sprite (alpha mask)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission boost", Range(0,8)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Emission;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed a = tex2D(_MainTex, i.uv).a;
                return fixed4(_Color.rgb * _Emission, a * _Color.a);
            }
            ENDCG
        }
    }
}
