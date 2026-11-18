// 液晶のブルーム用の下処理 (Game2DLcdGhost が毎フレーム呼ぶ)。 Quad のシェーダ (Game2D/LcdScreen) が、 できた小さな絵をなめらかに引き伸ばして重ねる。
//   Pass 0: しきい値を超えた明るさだけを取り出し、 4×4 の塊ごとに「平均 (面の光)」と「最大 (細い線・点の光)」にまとめて縮小する
//   Pass 1: 横のガウスぼかし   Pass 2: 縦のガウスぼかし   Pass 3: 2×2 を平均して半分に縮小   Pass 4: GUI の RT を重ねる (2026-11-16)
//   Pass 5: 周りの明るさの下地 (輝度の 4×4 平均)   Pass 6: 周りの明るさを効かせた取り出し (2026-11-17・試し・Game2DLcdGhost.BloomMode)
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
        // Pass 4 (2026-11-16): GUI の RT を、 残像・ブルーム・自動露出の元の絵 (中身の写し) の上に重ねる (Game2DLcdGhost)。
        //   GUI の RT はかぶり率 = sqrt(alpha)・色はかぶり率を掛けた色 (Game2D/LcdScreen の GuiOver と同じ読み方)
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { float4 g = tex2D(_MainTex, i.uv); return float4(g.rgb, sqrt(saturate(g.a))); }
            ENDCG
        }
        // Pass 5 (2026-11-17・周りの明るさの試し): 周りの明るさの下地。 4×4 の塊の輝度の平均 (しきい値なし)。 これをぼかして縮めた物を「周り」とする (Game2DLcdGhost)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float l = 0;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        float2 off = (float2(x, y) - 1.5) * _MainTex_TexelSize.xy;
                        l += dot(tex2Dlod(_MainTex, float4(i.uv + off, 0, 0)).rgb, float3(0.299, 0.587, 0.114));
                    }
                l /= 16.0;
                return fixed4(l, l, l, 1);
            }
            ENDCG
        }

        // Pass 6 (2026-11-17・周りの明るさの試し): Pass 0 と同じ取り出しに、 周りの明るさ S (_SurTex・ぼかした輝度) を効かせる。 _RelMode で形を選ぶ
        //   1 = 抑える形 (作者の狙い「周りが明るい所ではブルームを抑える」): e = 今の取り出し × (1 − smoothstep(_SupLo, _SupHi, S))。
        //       周りが _SupLo より暗ければ今のまま (暗い夜・洞窟の光り方は変わらない)、 _SupHi より明るければ消える。 乗算なので、 光る形と色は今と同じで強さだけが下がる
        //   2 = 周りより明るい分だけ光る形: しきい値を周りに合わせて上げる T = _RelThr + _RelK × S (e = (c − T) / (1 − _RelThr))。
        //       比 c / S にしなかったのは、 暗い所で S が 0 に近いと比が跳ね、 黒い地の上の暗い物まで光るため。 差なら S → 0 で今のしきい値の形に戻る
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _SurTex;
            float _RelMode, _SupLo, _SupHi, _RelK, _RelThr;
            fixed4 frag (v2f i) : SV_Target
            {
                float S = tex2Dlod(_SurTex, float4(i.uv, 0, 0)).r;
                float thr = _RelMode > 1.5 ? _RelThr + _RelK * S : _Thr;
                float invR = 1.0 / max(1.0 - (_RelMode > 1.5 ? _RelThr : _Thr), 0.001);
                float sup = _RelMode > 1.5 ? 1.0 : 1.0 - smoothstep(_SupLo, max(_SupHi, _SupLo + 1e-4), S);
                float3 mean = 0; float mx = 0;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        float2 off = (float2(x, y) - 1.5) * _MainTex_TexelSize.xy;
                        float3 c = tex2Dlod(_MainTex, float4(i.uv + off, 0, 0)).rgb;
                        float3 e = pow(saturate((c - thr) * invR), _Pow);
                        mean += e;
                        mx = max(mx, dot(e, float3(0.299, 0.587, 0.114)));
                    }
                return fixed4(mean / 16.0, mx) * sup;
            }
            ENDCG
        }
    }
}
