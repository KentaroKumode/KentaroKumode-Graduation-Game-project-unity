// 液晶のブルーム用の下処理 (Game2DLcdGhost が毎フレーム呼ぶ)。 Quad のシェーダ (Game2D/LcdScreen) が、 できた小さな絵をなめらかに引き伸ばして重ねる。
//   Pass 0: しきい値を超えた明るさだけを取り出し、 4×4 の塊ごとに「平均 (面の光)」と「最大 (細い線・点の光)」にまとめて縮小する
//   Pass 1: 横のガウスぼかし   Pass 2: 縦のガウスぼかし   Pass 3: 2×2 を平均して半分に縮小
// 元の絵は 1 画素ずつの点なので、 点を直接サンプルして足すと、 にじみの輪郭が階段状にカクカクする。 縮小してぼかした絵を、 双一次補間で拡大して使えば、 輪郭が丸くなめらかになる。
Shader "Hidden/Game2DBloom"
{
    Properties { _MainTex ("src", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Thr, _Pow;
        float2 _Dir;                       // ぼかす向き (テクセル単位)
        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
        ENDCG

        // 0: 明るい所だけ取り出して 4×4 → 1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float invR = 1.0 / max(1.0 - _Thr, 0.001);
                float3 mean = 0; float mx = 0;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        float2 off = (float2(x, y) - 1.5) * _MainTex_TexelSize.xy;
                        float3 c = tex2Dlod(_MainTex, float4(i.uv + off, 0, 0)).rgb;
                        float3 e = pow(saturate((c - _Thr) * invR), _Pow);
                        mean += e;
                        mx = max(mx, dot(e, float3(0.299, 0.587, 0.114)));
                    }
                return fixed4(mean / 16.0, mx);
            }
            ENDCG
        }

        // 1, 2: ガウスぼかし (9 タップ・双一次の補間で 5 回の読み出し)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = _Dir * _MainTex_TexelSize.xy;
                float4 c = tex2D(_MainTex, i.uv) * 0.2270270270;
                c += tex2D(_MainTex, i.uv + d * 1.3846153846) * 0.3162162162;
                c += tex2D(_MainTex, i.uv - d * 1.3846153846) * 0.3162162162;
                c += tex2D(_MainTex, i.uv + d * 3.2307692308) * 0.0702702703;
                c += tex2D(_MainTex, i.uv - d * 3.2307692308) * 0.0702702703;
                return c;
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = _Dir * _MainTex_TexelSize.xy;
                float4 c = tex2D(_MainTex, i.uv) * 0.2270270270;
                c += tex2D(_MainTex, i.uv + d * 1.3846153846) * 0.3162162162;
                c += tex2D(_MainTex, i.uv - d * 1.3846153846) * 0.3162162162;
                c += tex2D(_MainTex, i.uv + d * 3.2307692308) * 0.0702702703;
                c += tex2D(_MainTex, i.uv - d * 3.2307692308) * 0.0702702703;
                return c;
            }
            ENDCG
        }

        // 3: 半分に縮小 (双一次)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}
