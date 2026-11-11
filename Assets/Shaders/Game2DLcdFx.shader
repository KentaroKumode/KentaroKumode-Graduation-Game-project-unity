// 2D 画面の仕上げ (2026-10-19)。 液晶 (地図) と外枠の GUI の上に 1 枚かぶせる (Overlay Canvas の RawImage 用・Built-in RP)。
// 2026-11-02 に効果を絞って作り直した: ビネット (中心から離れるほど地の色へ寄せる) と、 戻した寒色の乗算。
//   (画面のフチ・黒の持ち上げ・ガラスの映り込みは外した。 git の履歴に残る)
// 走査線・湾曲・色収差・横線ノイズは、 地図だけに掛かるので液晶の Quad 側 (Game2DLcdScreen.shader) にある。
Shader "Game2D/LcdFx"
{
    Properties
    {
        [HideInInspector] _MainTex ("RawImage が書き込むだけ (使わない)", 2D) = "white" {}
        _Ground ("地の色 (ビネット)", Color) = (0.043, 0.039, 0.035, 1)
        _Vignette ("ビネットの濃さ (四隅での地の色の混ざり具合)", Range(0, 1)) = 0.45
        _Cool ("寒色の乗算 (航行のクリーム色の線を白へ寄せる・赤を落とす)", Color) = (0.90, 0.93, 1.0, 1)
        _Bright ("明るさ (設定の画面が入れる・1 = そのまま・2 まで)", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always

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
            float _Vignette;

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            fixed4 frag (v2f i) : SV_Target
            {
                // 中心から離れるほど地の色へ。 横を少し縮めた楕円 (横長の画面で四隅がいちばん暗くなる)
                float2 c = (i.uv - 0.5) * 2.0;
                float r = length(c * float2(0.92, 1.0));
                float a = _Vignette * smoothstep(0.55, 1.45, r);
                return fixed4(_Ground.rgb, a);
            }
            ENDCG
        }

        // 寒色の乗算 (2026-11-02 に戻した): 航行の線・ノードは生成り (黄みの白) で描いてあるので、 赤を少し落として白へ寄せる。 外すとクリーム色に見える
        //   明るさ (設定・2026-11-11) も同じ乗算に載せる。 1 より明るくもできるよう 2 倍の乗算 (結果 = 下地 × 出力 × 2) にして、 出力は半分で返す
        Pass
        {
            Blend DstColor SrcColor
            CGPROGRAM
            #pragma vertex vert2
            #pragma fragment frag2
            #include "UnityCG.cginc"
            fixed4 _Cool;
            float _Bright;
            float4 vert2 (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag2 () : SV_Target { return fixed4(saturate(_Cool.rgb * _Bright * 0.5), 1); }
            ENDCG
        }
    }
}
