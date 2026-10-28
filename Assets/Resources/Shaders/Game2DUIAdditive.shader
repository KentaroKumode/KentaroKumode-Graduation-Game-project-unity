// UI 用の加算合成 (2026-10-27)。 レーダーのブルーム (光のにじみ) を、 背景の上へ「足す」ために使う。
// 半透明の色で上書きすると、 下の液晶の地のディザの粒を覆って潰す。 足し算なら、 地の粒はそのまま透けて残る。
// 出力は RGB だけを足す (アルファは足さない)。 Resources に置くのは、 ビルドに必ず含めるため。
Shader "Game2D/UIAdditive"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target { fixed4 t = tex2D(_MainTex, i.uv); return fixed4(t.rgb * i.color.rgb, 0); }
            ENDCG
        }
    }
}
