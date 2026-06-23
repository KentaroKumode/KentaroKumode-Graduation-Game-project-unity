// VFX 専用ドット化合成シェーダー。
// メインカメラ出力 (_MainTex) の上に、 低解像度 VFX RT (_VfxTex) を α 閾値で1bit化＋色量子化して上乗せ。
//
// 使い方: Graphics.Blit(src=mainCameraSrc, dst, this) で 1 パス合成。
// _VfxTex は Point filter 設定済み、 サンプルが低解像度のままチャンキーに引き伸ばされる。
Shader "Hidden/Battle/VFXComposite"
{
    Properties
    {
        _MainTex   ("Main (camera src)", 2D) = "white" {}
        _VfxTex    ("VFX RT (additive)", 2D) = "black" {}
        _InkTex    ("Ink RT (alpha-blend, premult)", 2D) = "black" {}
        _HasInk    ("Has Ink RT (0/1)", Float) = 0
        _CompositeMode ("Composite Mode (0=Additive 1=AlphaBlend 2=Auto)", Float) = 0
        _AutoSplit     ("Auto: brightness split point", Float) = 0.18
        _AutoSplitWidth("Auto: brightness split smoothing", Float) = 0.18
        _GlowStrength  ("Glow Strength (additive layer)", Float) = 0.6
        _GlowRadius    ("Glow Radius (RT texels)", Float) = 1.5
        _GlowThreshold ("Glow source threshold", Float) = 0.05
        _EdgeMode  ("Edge Mode (0=Soft 1=Smooth 2=Hard)", Float) = 0
        _Threshold ("Visibility Threshold", Float) = 0.15
        _SmoothWidth ("Smooth Transition Width", Float) = 0.1
        _DoQuantize ("Quantize Color (0/1)", Float) = 0
        _Levels    ("Color Levels per Channel", Float) = 8
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _VfxTex;
            sampler2D _InkTex;
            float _HasInk;
            float _CompositeMode; // 0=Additive 1=AlphaBlend 2=Auto
            float _EdgeMode;      // 0=Soft 1=Smooth 2=Hard
            float _Threshold;
            float _SmoothWidth;
            float _DoQuantize;
            float _Levels;
            float _AutoSplit;
            float _AutoSplitWidth;
            float _GlowStrength;
            float _GlowRadius;
            float _GlowThreshold;
            float4 _VfxTex_TexelSize;

            // 加算層のグロー (9-tap box blur + 閾値カット)。 RT は低解像度なので軽い。
            fixed3 SampleGlow(float2 uv)
            {
                float2 tx = _VfxTex_TexelSize.xy * _GlowRadius;
                fixed3 sum = (fixed3)0;
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        fixed3 s = tex2D(_VfxTex, uv + float2(x, y) * tx).rgb;
                        // 閾値以下はカット (暗い背景がうっすら光らないように)
                        float b = max(max(s.r, s.g), s.b);
                        s *= step(_GlowThreshold, b);
                        sum += s;
                    }
                }
                return sum * (1.0 / 9.0);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 m = tex2D(_MainTex, i.uv);
                fixed4 v = tex2D(_VfxTex, i.uv);

                // 加算層のグローを先に計算 (近傍からの寄与を拾うので、 中央が空でも光が漏れる)
                bool glowEnabled = (_GlowStrength > 0.001);
                fixed3 glow = glowEnabled ? SampleGlow(i.uv) * _GlowStrength : (fixed3)0;

                // 視認性メトリクス。
                //   Additive モードでは α を完全無視 (= 輝度のみ)。 α 蓄積による黒ハロを回避。
                //   AlphaBlend / Auto モードでは α と輝度の最大値 (黒い alpha-blend 粒子も拾う)。
                float bright = max(max(v.r, v.g), v.b);
                bool isAdditive  = (_CompositeMode < 0.5);
                bool isAuto      = (_CompositeMode > 1.5);
                float vis = isAdditive ? bright : max(v.a, bright);

                // 早期切り捨て: 中央 VFX が無い場合は m + glow のみ (グローのハロは残す)
                if (vis < 0.001)
                {
                    fixed3 baseRgb = saturate(m.rgb + glow);
                    if (_HasInk > 0.5)
                    {
                        fixed4 ink = tex2D(_InkTex, i.uv);
                        if (ink.a > 0.001)
                            baseRgb = saturate(ink.rgb + baseRgb * (1.0 - ink.a));
                    }
                    return fixed4(baseRgb, m.a);
                }

                // gate = エッジ処理 (0..1)
                float gate = 1.0;
                if (_EdgeMode > 1.5) // Hard
                {
                    gate = step(_Threshold, vis);
                }
                else if (_EdgeMode > 0.5) // Smooth
                {
                    float w = max(0.001, _SmoothWidth);
                    gate = smoothstep(_Threshold - w, _Threshold + w, vis);
                }
                if (gate < 0.001)
                {
                    fixed3 baseRgb = saturate(m.rgb + glow);
                    if (_HasInk > 0.5)
                    {
                        fixed4 ink = tex2D(_InkTex, i.uv);
                        if (ink.a > 0.001)
                            baseRgb = saturate(ink.rgb + baseRgb * (1.0 - ink.a));
                    }
                    return fixed4(baseRgb, m.a);
                }

                // カラー量子化
                if (_DoQuantize > 0.5)
                {
                    float lv = max(2.0, _Levels);
                    v.rgb = floor(v.rgb * lv) / lv;
                }

                fixed3 outRgb;
                if (isAdditive)
                {
                    // 加算合成: m + v.rgb (α 無視 → 黒ハロ無し、 墨斬りは消える)
                    outRgb = saturate(m.rgb + v.rgb * gate);
                }
                else if (!isAuto)
                {
                    // 純 AlphaBlend: v.rgb + m * (1-v.a) (墨斬り得意、 加算系に黒ハロ)
                    float effA = v.a * gate;
                    outRgb = saturate(v.rgb * gate + m.rgb * (1.0 - effA));
                }
                else
                {
                    // ===== Auto: 輝度で additive / alpha-blend を per-pixel ブレンド =====
                    //   bright が高い   → additive 寄り (黒ハロ回避)
                    //   bright が低い   → alpha-blend 寄り (黒い斬撃が背景を削る)
                    //   その間は smoothstep でなめらか
                    float addMix = smoothstep(
                        _AutoSplit - _AutoSplitWidth,
                        _AutoSplit + _AutoSplitWidth,
                        bright);

                    fixed3 addOut = saturate(m.rgb + v.rgb * gate);
                    // alpha-blend 側は alpha が「黒の濃さ」を表すと仮定して背景を減衰
                    float effA = v.a * gate;
                    fixed3 blendOut = saturate(v.rgb * gate + m.rgb * (1.0 - effA));

                    outRgb = lerp(blendOut, addOut, addMix);
                }

                // 加算層のグローを乗せる (Ink 合成の前)
                outRgb = saturate(outRgb + glow);

                // ===== Ink RT 合成 (premultiplied alpha blend で additive レイヤーの上に重ねる) =====
                // Ink RT は素のアルファブレンド particle を直接焼いた RT。
                // Blend SrcAlpha OneMinusSrcAlpha の結果なので rgb は既に premultiplied 相当。
                if (_HasInk > 0.5)
                {
                    fixed4 ink = tex2D(_InkTex, i.uv);
                    if (ink.a > 0.001)
                    {
                        outRgb = saturate(ink.rgb + outRgb * (1.0 - ink.a));
                    }
                }

                return fixed4(outRgb, m.a);
            }
            ENDCG
        }
    }
}
