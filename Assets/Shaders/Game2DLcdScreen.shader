// 2D 画面の液晶 (地図・タイトルなどの「画面の中身」) を映す Quad 用 (2026-10-19)。 正本は docs/design/map-prototype.html の
// 液晶の合成 (lcdComposite / LFX / NOISE / CRT)。 旧 Lcd/Scanline (走査線) は置き換えた。
//   残像 (lighten) → 横線ノイズ → 樽型の湾曲 → 画素の格子 → バックライトのにじみ。
// 地図 (この Quad) にだけ掛かる。 外枠の GUI (Canvas) には掛けない (HTML も GUI は別の層)。
// 画素の格子で暗くなる分は、 先に明るくして打ち消す (HTML の brightness 補正と同じ)。
Shader "Game2D/LcdScreen"
{
    Properties
    {
        _MainTex ("LCD (RenderTexture)", 2D) = "black" {}
        _PrevTex ("前のフレーム (残像)", 2D) = "black" {}
        _Ghost ("残像の濃さ", Range(0, 1)) = 0.38
        _Curve ("湾曲 (0 = 平ら)", Range(0, 0.2)) = 0.03
        _Back ("画面の外の色", Color) = (0.027, 0.02, 0.008, 1)
        _BloomTex ("ブルーム (1/4・Game2DLcdGhost が作る)", 2D) = "black" {}
        _BloomTexB ("ブルーム (1/8)", 2D) = "black" {}
        _Power ("電源 (Game2DBoot が入れる・0 = 消灯 / 1 = 点灯済み)", Range(0, 1)) = 1
        _Distort ("画面の歪み (Game2DBoot が点灯の瞬間だけ入れる・0 = なし)", Range(0, 1)) = 0
        _Chroma ("色のにじみ (縁ほど赤と青が左右にずれる)", Range(0, 0.01)) = 0.0018
        _Flicker ("画面のちらつき (明るさの揺れの大きさ)", Range(0, 0.05)) = 0.012
        _Exposure ("自動露出 (Game2DLcdGhost が毎フレーム入れる・1 = 変えない)", Float) = 1
        _Grid ("画素の格子の濃さ", Range(0, 1)) = 0.22
        _Comp ("格子で暗くなる分の補正の倍率 (1 = 平均の明るさを保つ理論値)", Range(0.5, 2)) = 1
        _Bleed ("バックライトのにじみ", Range(0, 0.2)) = 0.03
        _Bloom ("発光のにじみ (明るい画素のまわりへ光が漏れる)", Range(0, 1)) = 0.35
        _BloomThr ("にじみ始める明るさ", Range(0, 1)) = 0.22
        _BloomTint ("にじみの色味 (元の絵の色を、 この色へ寄せる)", Color) = (0.72, 0.88, 1.0, 1)
        _BloomTintAmt ("にじみの色味へ寄せる割合", Range(0, 1)) = 0.85
        _Halo ("縁の光 (細い線・小さい点のまわりの淡い輪)", Range(0, 1)) = 0.25
        _BloomPow ("明るいほど強く掛かる度合い (1 = 比例・2 以上 = 明るい所だけ強く)", Range(1, 4)) = 1
        _BleedColor ("にじみの色", Color) = (0.549, 0.667, 0.863, 1)
        _Ink ("横線の色", Color) = (0.886, 0.945, 1, 1)
        _NoiseTex ("横線ノイズ (Game2DLcdNoise が毎フレーム描く・R8)", 2D) = "black" {}
        _RtSize ("RT の大きさ", Vector) = (960, 540, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex, _PrevTex, _NoiseTex, _BloomTex, _BloomTexB;
            float _Chroma, _Flicker, _Distort, _Power, _Exposure, _Ghost, _Curve, _Grid, _Comp, _Bleed, _Bloom, _BloomThr, _BloomPow, _Halo, _BloomTintAmt;
            fixed4 _Back, _BleedColor, _Ink, _BloomTint;
            float4 _RtSize;

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 電源オン: システムの起動なので演出は付けない。 一瞬の暗転のあとに、 バックライトの明るさがなめらかに単調に上がるだけ。 p = 0 (消灯) .. 1 (点灯済み)
            float3 PowerOn(float3 col, float2 uv, float p)
            {
                if (p >= 1.0) return col;
                float b = smoothstep(0.1, 1.0, p);
                return col * b * b;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 樽型の湾曲: 元 = 中心 × (1 + k·r²) / (1 + 2k)。 HTML は (1 + k) で四隅が欠けるが、 ここは 2k で四隅を端に合わせて、 画面の外を見せない
                float2 c = i.uv * 2.0 - 1.0;
                float r2 = dot(c, c);
                float2 s = c * (1.0 + _Curve * r2) / (1.0 + 2.0 * _Curve);      // 四隅がちょうど絵の端に合う (縁の中央は少し内側を見る) = 外が見えず、 黒い角が出ない
                float2 suv = s * 0.5 + 0.5;
                if (suv.x < 0 || suv.y < 0 || suv.x > 1 || suv.y > 1) return fixed4(_Back.rgb, 1);

                // 点灯の瞬間の歪み: 行ごとの横ずれ (走査のちぎれ)・ブロックごとの大きな横ずれ・縦の揺れ。 強さ _Distort は点灯直後に最大から急に減る
                if (_Distort > 0.001)
                {
                    float tk = floor(_Time.y * 24.0);
                    float rowH = hash21(float2(floor(suv.y * 120.0), tk));
                    float blkH = hash21(float2(floor(suv.y * 7.0), tk + 17.0));
                    float off = (rowH - 0.5) * 0.10 * step(0.45, rowH)
                              + (blkH - 0.5) * 0.45 * step(0.55, blkH);
                    suv.x = saturate(suv.x + off * _Distort);
                    suv.y = saturate(suv.y + sin(suv.y * 40.0 + _Time.y * 60.0) * 0.012 * _Distort);
                }

                // 色のにじみ (RGB のずれ): 中心は合い、 縁へ行くほど赤と青が左右へずれる (古いブラウン管のレンズ)
                float2 ca = float2(c.x * _Chroma, 0);
                fixed3 cur = fixed3(tex2D(_MainTex, suv + ca).r, tex2D(_MainTex, suv).g, tex2D(_MainTex, suv - ca).b);

                // 残像 (液晶の応答の遅れ): 前のフレームを lighten で薄く重ねる
                fixed3 prev = tex2Dlod(_PrevTex, float4(suv, 0, 0)).rgb;
                fixed3 col = cur + _Ghost * (max(cur, prev) - cur);

                // 画素の格子で暗くなる分を先に明るくして打ち消す (暗くなる画素は 3/4 × 濃さ)
                col *= _Comp / (1.0 - 0.75 * _Grid);

                // 横線ノイズ (ゆがめる前の絵に乗せる = 湾曲と一緒に曲がる)。 線の予定は Game2DLcdNoise が毎フレーム 1 枚の濃淡に描く
                col = lerp(col, _Ink.rgb, tex2D(_NoiseTex, suv).r);

                // 画素の格子: 画面の画素の偶奇で 1 画素の隙間 (2 画素周期)
                float2 sp = floor(i.pos.xy);
                float gap = max(fmod(sp.x, 2.0), fmod(sp.y, 2.0));
                col *= 1.0 - gap * _Grid;

                // 発光のにじみ: 明るい画素のまわりへ光が漏れる (格子の隙間にも回り込む = 画素が自ら光っている見え方)。
                //   下処理 (Game2DLcdGhost + Hidden/Game2DBloom) が、 しきい値を超えた明るさだけを縮小・ぼかした絵 (_BloomTex = 1/4・_BloomTexB = 1/8) を用意する。
                //   それを双一次補間で引き伸ばして重ねるので、 輪郭が階段状にならず、 丸くなめらかに広がる。
                //   rgb = 面の光 (広い明るい面ほど強い)  a = 縁の光 (細い線・小さい点のまわりにも出る)。 足さずに大きい方を取る (眩しくならない)。
                {
                    float4 g1 = tex2D(_BloomTex, suv), g2 = tex2D(_BloomTexB, suv);
                    float4 g = g1 * 0.6 + g2 * 0.4;
                    float3 glowRaw = max(g.rgb * _Bloom * 2.0, g.aaa * _Halo) * (1.0 - _BloomThr) * (_Exposure * _Exposure);
                    // 光の色: 元の絵の色 (航行の線は生成りで黄みがある) をそのまま漏らすと黄色く見えるので、 明るさだけ取って冷たい白へ寄せる
                    float gl = dot(glowRaw, float3(0.299, 0.587, 0.114));
                    col += lerp(glowRaw, gl * _BloomTint.rgb / dot(_BloomTint.rgb, float3(0.299, 0.587, 0.114)), _BloomTintAmt);
                }

                // バックライトのにじみ: 左下と右上の角の近くが少しだけ明るい
                float2 a = i.uv * float2(_RtSize.x / _RtSize.y, 1.0);
                float rad = 0.7;
                float g1 = saturate(1.0 - length(a - float2(0, 0)) / rad);
                float g2 = saturate(1.0 - length(a - float2(_RtSize.x / _RtSize.y, 1.0)) / rad);
                col += _BleedColor.rgb * _Bleed * (g1 + g2);

                col *= 1.0 + _Flicker * (0.6 * sin(_Time.y * 43.0) + 0.4 * sin(_Time.y * 9.3));        // 画面のちらつき (ごく弱く)
                col *= _Exposure;                                   // 自動露出: 明るすぎる絵は全体を少し下げる
                col = PowerOn(col, i.uv, _Power);                   // 起動演出 (電源オン)
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
