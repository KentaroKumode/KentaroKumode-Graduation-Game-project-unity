// 中心から外向きに伝播する水面波紋シェーダー (メタデバフ ノード用)。
// 脈動周期に合わせて 1 波が中央から外へ広がる。 明滅 (色/alpha 変化) なし、
// UV ディスプレースメントのみで「揺らぎ」 を表現する。
//
// 使い方:
//   - SkillTreeView がこのシェーダーを使った Material を SpriteRenderer に割当て、
//     毎フレーム MaterialPropertyBlock で _RipplePhase / _RippleStrength を更新する。
//   - スプライトはアトラス化しない前提 (UV (0,0)〜(1,1) の全域)。
Shader "Custom/MetaDebuffRipple"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        // 波紋の現在の前線位置 (0=中心, 1=外周)。 C# 側で時間に同期して 0→1 を循環させる。
        _RipplePhase ("Ripple Phase", Range(0,1)) = 0.0
        // UV ディスプレースメント振幅 (Lv に応じて大きく)。 0=変形なし。
        _RippleStrength ("Ripple Strength", Range(0, 0.20)) = 0.05
        // 波の山谷の細かさ (空間周波数)。 高いと細かい同心円が見える。
        _RippleFreq ("Ripple Frequency", Float) = 8.0
        // 前線のガウシアン帯幅 (波が及ぶ範囲)。
        _RippleBand ("Ripple Band Width", Range(0.05, 0.5)) = 0.20
        // パーフェクトピクセル: 1=テクセル中央スナップ (px 整数刻みでズレる) / 0=連続 (滑らかにアンチエイリアス)
        [Toggle] _PerfectPixel ("Perfect Pixel Snap", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // x=1/w, y=1/h, z=w, w=h
            float4 _Color;
            float _RipplePhase;
            float _RippleStrength;
            float _RippleFreq;
            float _RippleBand;
            float _PerfectPixel;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ===== すべて入力テクスチャの「ドット (= 1 texel)」 単位で計算する =====
                // 連続値 (UV) で sin/r/band を計算すると、 隣接ドットで displacement が滑らかに変化し、
                // 結果として「dot 単位の波」 ではなく「滲んだ波」 が出る。
                // 解決: 入力テクスチャの dot 数を基準に、 中心からの距離も波の前線も displacement も
                // 整数 dot で扱う。 1 dot 動くか、 動かないかの離散的な見え方になる。

                float2 texSize = _MainTex_TexelSize.zw;       // 入力テクスチャの dot 数 (w, h)
                float halfDiagDot = length(texSize * 0.5);    // 中心から角までの dot 数 (= 最大半径)

                if (_PerfectPixel > 0.5)
                {
                    // 現在のフラグメントが属する「入力 dot」 の位置 (px 整数座標)
                    float2 pxCoord = floor(i.uv * texSize);                 // 0..texSize-1
                    float2 centerPx = texSize * 0.5;
                    float2 dPx = (pxCoord + 0.5) - centerPx;                // 中心からの距離 (dot)
                    float rPx = length(dPx);
                    float2 dirPx = (rPx > 1e-4) ? dPx / rPx : float2(0, 0);

                    // 波の前線位置を dot 単位の整数に変換
                    float phaseDot = floor(_RipplePhase * halfDiagDot + 0.5);
                    // 帯幅 (gaussian sigma) も dot 単位、 1未満は 1 でクランプ
                    float bandDot = max(1.0, _RippleBand * halfDiagDot);
                    // 波長 (空間周期) も dot 単位。 _RippleFreq=8 なら sprite 全域に 4 周期程度。
                    float wavelengthDot = max(1.0, halfDiagDot * 2.0 / max(_RippleFreq, 0.001));
                    // 最大変位 (dot)。 _RippleStrength は UV 単位だったので texSize 倍で dot 換算
                    float dispMaxDot = _RippleStrength * texSize.x;

                    // 前線付近だけ波が立つ
                    float band = exp(-pow((rPx - phaseDot) / bandDot, 2));
                    // 波の正弦 (dot 単位の周期)
                    float wave = sin((rPx - phaseDot) * 6.2831853 / wavelengthDot);
                    // 変位 (dot, 整数化)
                    float dispDot = round(wave * dispMaxDot * band);

                    // サンプル位置 = 元 dot の中心 + 整数 dot 変位
                    float2 sampleDot = pxCoord + 0.5 + dirPx * dispDot;
                    // 再度 dot 整数に丸めて (誤差吸収)、 中央 UV へ
                    sampleDot = floor(sampleDot) + 0.5;
                    float2 sampleUv = sampleDot / texSize;

                    fixed4 col = tex2Dlod(_MainTex, float4(sampleUv, 0, 0)) * i.color;
                    return col;
                }
                else
                {
                    // 連続変位モード (滑らか・アンチエイリアス可)
                    float2 d = i.uv - float2(0.5, 0.5);
                    float r = length(d);
                    float2 dir = (r > 1e-4) ? d / r : float2(0, 0);
                    float band = exp(-pow((r - _RipplePhase) / max(_RippleBand, 1e-3), 2));
                    float disp = sin((r - _RipplePhase) * _RippleFreq * 6.2831853) * _RippleStrength * band;
                    float2 newUv = i.uv + dir * disp;
                    fixed4 col = tex2Dlod(_MainTex, float4(newUv, 0, 0)) * i.color;
                    return col;
                }
            }
            ENDCG
        }
    }
}
