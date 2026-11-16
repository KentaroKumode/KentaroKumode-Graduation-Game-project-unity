// 2D 画面の液晶 (地図・タイトルなどの「画面の中身」) を映す Quad 用 (2026-10-19)。
// 2026-11-02 に効果を絞って作り直した: 走査線 (横) / 画面の湾曲 / 色収差 / 発光のにじみ (ブルーム) + 自動露出 / ノイズ (横線ノイズ + フィルムの粒)。 もう 1 つのビネットは外枠まで覆うので Game2D/LcdFx 側。
//   (画素の格子・サブピクセル・ドットの芯・バックライトのにじみ・ちらつきは、 この作り直しで外した。 git の履歴に残る。 ブルームと自動露出は同日に戻した)
//   湾曲 → 色収差 → 残像 (起動の瞬間用) → 横線ノイズ → 走査線 → ブルーム → 露出 → 粒。 走査線と横線ノイズは湾曲した後の座標で引くので、 画面と一緒に曲がる。
// 2026-11-16 (作者の決定「外の部品はもう無い」): GUI (計器盤・帯・窓・手引き) の RT (Game2DGuiRt・960×540・中身の RT と同じ格子) も、 ここで湾曲した後の同じ座標で読んで
//   中身に重ね、 そのあと効果 (色収差・残像・横線ノイズ・走査線・ブルーム・露出・粒) を全部掛ける。 GUI はグローバルの _Game2DGuiTex (_Game2DGuiOn = 1 の時だけ)。
//   残像・ブルーム・自動露出の元の絵 (_PrevTex など・Game2DLcdGhost) にも GUI を重ねてある。 ボスの大技の揺れ・暗転・赤・白も GUI へここで掛ける (旧: 提示の板 Game2D/GuiPresent)。
Shader "Game2D/LcdScreen"
{
    Properties
    {
        _MainTex ("LCD (RenderTexture)", 2D) = "black" {}
        _PrevTex ("前のフレーム (残像・Game2DLcdGhost が渡す)", 2D) = "black" {}
        _Ghost ("残像の濃さ (液晶の応答の遅れ・点灯の瞬間は Game2DBoot が上げる)", Range(0, 1)) = 0.38
        _Curve ("湾曲 (0 = 平ら)", Range(0, 0.2)) = 0.03
        _Back ("画面の外の色", Color) = (0.027, 0.02, 0.008, 1)
        _CurveFit ("湾曲の外形の合わせ方 (1 = 縁の中央を絵の端に合わせる・絵の外形が樽型・既定 2026-11-16 作者の決定 / 0 = 旧い合わせ方: 四隅を絵の端に合わせ見える範囲は長方形)", Range(0, 1)) = 1
        _Power ("電源 (Game2DBoot が入れる・0 = 消灯 / 1 = 点灯済み)", Range(0, 1)) = 1
        _Distort ("画面の歪み (Game2DBoot が点灯の瞬間だけ入れる・0 = なし)", Range(0, 1)) = 0
        _Scan ("走査線の濃さ (横線の暗い帯の暗さ・0 = なし)", Range(0, 1)) = 0.4
        _ScanRows ("中身の縦のドット数 (走査線はこの 1 ドット行ごとに 明・暗 を交互に並べる = 暗線の太さが絵の 1 ドットと同じ)", Float) = 270
        _Chroma ("色収差 (中心は合い、 縁ほど赤と青が外へずれる)", Range(0, 0.02)) = 0.003
        _BloomTex ("ブルーム (1/4・Game2DLcdGhost が作る)", 2D) = "black" {}
        _BloomTexB ("ブルーム (1/8)", 2D) = "black" {}
        _Bloom ("発光のにじみ (明るい画素のまわりへ光が漏れる)", Range(0, 1)) = 0.35
        _BloomThr ("にじみ始める明るさ", Range(0, 1)) = 0.22
        _Exposure ("自動露出 (Game2DLcdGhost が毎フレーム入れる・1 = 変えない)", Float) = 1
        _BloomFar ("広いにじみ (1/8) の混ぜ具合 (小さいほど光の届く範囲が狭い。 近くの強さは変えない)", Range(0, 1)) = 0.12
        _BloomSoft ("ぼかしの広がり (大きいほど柔らかく広い・Game2DLcdGhost が読む)", Range(0.5, 4)) = 1.1
        _BloomTint ("にじみの色味 (元の絵の色を、 この色へ寄せる)", Color) = (0.72, 0.88, 1.0, 1)
        _BloomTintAmt ("にじみの色味へ寄せる割合", Range(0, 1)) = 0.85
        _Halo ("縁の光 (細い線・小さい点のまわりの淡い輪)", Range(0, 1)) = 0.25
        _BloomPow ("明るいほど強く掛かる度合い (1 = 比例・2 以上 = 明るい所だけ強く)", Range(1, 4)) = 1
        _Grain ("フィルムの粒 (見た目の明るさへ足す幅・中間の明るさで ±この値・0 = なし)", Range(0, 0.3)) = 0.035
        _GrainCells ("粒の格子 (横・縦にいくつ並ぶか。 中身のドット数 480×270 なら 1 ドット = 1 粒)", Vector) = (480, 270, 0, 0)
        _GrainFps ("粒が入れ替わる速さ (毎秒の回数)", Range(1, 60)) = 24
        _Ink ("横線ノイズの色", Color) = (0.886, 0.945, 1, 1)
        _NoiseTex ("横線ノイズ (Game2DLcdNoise が毎フレーム描く・R8)", 2D) = "black" {}
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
            float _Chroma, _Distort, _Power, _Ghost, _Curve, _CurveFit, _Scan, _ScanRows, _Grain, _GrainFps, _Bloom, _BloomThr, _Halo, _BloomTintAmt, _BloomFar, _Exposure;
            fixed4 _Back, _Ink, _BloomTint;
            float4 _GrainCells;
            // GUI (2026-11-16・Game2DGuiRt がグローバルで入れる)
            sampler2D _Game2DGuiTex;
            float _Game2DGuiOn, _Game2DGuiFxOn, _Game2DGuiFxDark, _Game2DGuiFxRed;
            // 走査線の試しの形 (2026-11-16・作者「走査線で文章が読みづらい」・QaBattle.ScanMode が入れるグローバル)。 0 = 今のまま (既定)
            //   1 = 縞を dot の行の半分の細かさにして、 どの dot の行も同じ明るさ (下半分が暗い) にする / 2 = 明るい所 (字・明るい線) ほど走査線を弱める /
            //   3 = GUI (計器盤・帯・窓・手引き) の覆っている所だけ走査線を弱める
            float _Game2DScanMode;
            float4 _Game2DGuiFxWhite, _Game2DGuiFxShake;

            // 中身の色 lcd の上に、 GUI の RT の uv の所を重ねる。 GUI の RT は UI の通常の合成 (SrcAlpha, OneMinusSrcAlpha) で透明な地へ書かれているので、
            // かぶり率 = sqrt(alpha)・色はかぶり率を掛けた色 (旧 Game2D/GuiPresent と同じ読み方)。 ボスの大技は GUI 全体に同じ強さで掛ける (中身と同じに)。
            float3 GuiOver(float3 lcd, float2 uv)
            {
                if (_Game2DGuiOn < 0.5) return lcd;
                if (_Game2DGuiFxOn > 0.5) uv += float2(-_Game2DGuiFxShake.x / 480.0, _Game2DGuiFxShake.y / 270.0);      // 揺れ (dot)。 中身の揺れは絵を (dx, dy) ずらす = ここでは (−dx, +dy) から読む
                float4 g = tex2D(_Game2DGuiTex, uv);
                float cov = sqrt(saturate(g.a));
                float3 rgb = g.rgb;
                if (_Game2DGuiFxOn > 0.5)
                {
                    // 暗幕 → 赤 (王) → 白 (発光は赤の上)。 暗幕の倍率が負 (シュヴァリエの真っ黒の一拍) は 0 = 黒
                    rgb *= max(_Game2DGuiFxDark, 0.0);
                    rgb = float3(min(cov, rgb.r + 26.0 / 255.0 * _Game2DGuiFxRed * cov), rgb.g * (1.0 - 0.45 * _Game2DGuiFxRed), rgb.b * (1.0 - 0.6 * _Game2DGuiFxRed));
                    rgb = lerp(rgb, _Game2DGuiFxWhite.rgb * cov, saturate(_Game2DGuiFxWhite.a));
                }
                return lcd * (1.0 - cov) + rgb;
            }

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 粒用の乱数 (隣り合う画素どうしで相関しにくい)
            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 電源オン: システムの起動なので演出は付けない。 一瞬の暗転のあとに、 バックライトの明るさがなめらかに単調に上がるだけ。 p = 0 (消灯) .. 1 (点灯済み)
            float3 PowerOn(float3 col, float p)
            {
                if (p >= 1.0) return col;
                float b = smoothstep(0.1, 1.0, p);
                return col * b * b;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ── 画面の湾曲 (樽型): 元 = 中心 × (1 + k·r²) / (1 + k·(2 − f))。 c と r² は縦横とも −1〜1 に正規化した座標。
                //   f = _CurveFit = 1 (既定・2026-11-16 作者の決定): 分母 1 + k。 縁の中央 (左右は c = (±1, 0)・上下は c = (0, ±1)・どちらも r² = 1) がちょうど絵の端に合い、 四隅は絵の外 (_Back) を見る
                //     = 絵の外形が樽型になり、 中身の曲がりとそろう。 絵は 1 画素も画面の外へ出ない。 式は縦横で同じ (r² が正規化した座標なので、 左右と上下の縁の中央が同時に合う)
                //   f = 0 (旧い合わせ方・見比べ用 QaBattle.LcdCurveFit(0)): 分母 1 + 2k。 四隅 (r² = 2) がちょうど絵の端に合い、 見える範囲は長方形のまま (縁の中央の近くの絵は画面の外へ押し出される)
                //   値は Game2DSettingsApply.CurveFit が実行時の材質へ入れる (アセットの材質が古い値を持っていても既定になるように)
                //   マウスの当たり (画面の位置 → 液晶の dot) は同じ式を Game2DPointer.LcdDot が持つ
                float2 c = i.uv * 2.0 - 1.0;
                float r2 = dot(c, c);
                float2 s = c * (1.0 + _Curve * r2) / (1.0 + _Curve * (2.0 - _CurveFit));
                float2 suv = s * 0.5 + 0.5;
                if (suv.x < 0 || suv.y < 0 || suv.x > 1 || suv.y > 1) return fixed4(_Back.rgb, 1);
                // 絵の外形の縁 (f > 0 の時だけ): 画面の 1 画素ぶんで _Back へ寄せ、 樽型の縁が画面の画素でギザギザにならないようにする (f = 0 は今までどおり何もしない)
                float edgeM = min(min(suv.x, 1.0 - suv.x), min(suv.y, 1.0 - suv.y));
                float edgeA = _CurveFit > 0.001 && _Curve > 0.0001 ? saturate(edgeM / max(fwidth(edgeM), 1e-5)) : 1.0;      // 湾曲 0 (設定で平ら) なら外形は画面の縁そのもの・寄せない
                float2 panelUv = suv;      // 走査線は液晶の板そのもの。 点灯の瞬間の歪み (絵のずれ) には付いていかない

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

                // ── 色収差: 中心から外向きに、 赤は外へ・青は内へずれる (レンズの色ずれ)。 中心にも少し残し、 縁へ行くほど強くなる。 緑は動かさない
                float2 ca = c * _Chroma * (0.25 + 0.75 * r2);
                //   GUI も同じ座標で重ねてから色をずらす (計器や窓の字も中身と同じに色がずれる)
                fixed3 cur = fixed3(GuiOver(tex2D(_MainTex, suv + ca).rgb, suv + ca).r, GuiOver(tex2D(_MainTex, suv).rgb, suv).g, GuiOver(tex2D(_MainTex, suv - ca).rgb, suv - ca).b);

                // 残像 (点灯の瞬間の液晶の応答の遅れ): 前のフレームを lighten で薄く重ねる
                fixed3 prev = tex2Dlod(_PrevTex, float4(suv, 0, 0)).rgb;
                fixed3 col = cur + _Ghost * (max(cur, prev) - cur);

                // ── 横線ノイズ (ゆがめる前の絵に乗せる = 湾曲と一緒に曲がる)。 線の予定は Game2DLcdNoise が毎フレーム 1 枚の濃淡に描く
                col = lerp(col, _Ink.rgb, tex2D(_NoiseTex, suv).r);

                // ── 走査線 (横): 絵の 1 ドット行ごとに 明・暗 を交互に並べる (暗線の太さ = 絵の 1 ドット)。 湾曲した後の座標で数えるので、 絵の行と一緒に曲がる。
                //   行の境目だけ、 画面の 1 画素の幅でなめらかにする (ドットが画面の 1 画素に近い小さい窓では、 そこから先は線を弱めてちらつきを防ぐ)
                int scanMode = (int)round(_Game2DScanMode);
                float v = panelUv.y * _ScanRows * (scanMode == 1 ? 2.0 : 1.0);      // 絵のドット行の番号 (小数)。 形 1 は 1 dot の行を明・暗の 2 本に割る
                float fw = max(fwidth(v), 1e-5);
                float rowDark = fmod(floor(v), 2.0);                               // この行が暗 (1) か 明 (0) か
                float e = min(frac(v), 1.0 - frac(v));                              // 行の境目までの距離
                float dark = lerp(rowDark, 1.0 - rowDark, 0.5 * saturate(1.0 - e / (0.5 * fw)));
                float scan = _Scan * saturate((1.0 - fw) / 0.5);
                if (scanMode == 2) scan *= 1.0 - 0.75 * smoothstep(0.30, 0.80, dot(col, float3(0.299, 0.587, 0.114)));      // 明るい所ほど弱める (字の画が縞で切れない)
                if (scanMode == 3 && _Game2DGuiOn > 0.5) scan *= 1.0 - 0.85 * sqrt(saturate(tex2D(_Game2DGuiTex, panelUv).a));      // GUI の覆っている所だけ弱める
                col *= (1.0 - scan * dark) / (1.0 - scan * 0.5);                    // 暗い行は半分 = 平均の暗さ (濃さ / 2) を割って、 画面全体の明るさは変えない

                // ── 発光のにじみ (ブルーム): 明るい画素のまわりへ光が漏れる。 走査線の暗い帯にも回り込む = 画素が自ら光っている見え方なので、 走査線の後に足す。
                //   下処理 (Game2DLcdGhost + Hidden/Game2DBloom) が、 しきい値を超えた明るさだけを縮小・ぼかした絵 (_BloomTex = 1/4・_BloomTexB = 1/8) を用意する。
                //   それを双一次補間で引き伸ばして重ねるので、 輪郭が階段状にならず、 丸くなめらかに広がる。
                //   rgb = 面の光 (広い明るい面ほど強い)  a = 縁の光 (細い線・小さい点のまわりにも出る)。 足さずに大きい方を取る (眩しくならない)。
                {
                    float4 g = tex2D(_BloomTex, suv) * 0.6 + tex2D(_BloomTexB, suv) * _BloomFar;
                    float3 glowRaw = max(g.rgb * _Bloom * 2.0, g.aaa * _Halo) * (1.0 - _BloomThr) * (_Exposure * _Exposure);      // 眩しい絵では露出が下がり、 にじみは 2 乗で弱まる
                    // 光の色: 元の絵の色 (航行の線は生成りで黄みがある) をそのまま漏らすと黄色く見えるので、 明るさだけ取って冷たい白へ寄せる
                    float gl = dot(glowRaw, float3(0.299, 0.587, 0.114));
                    col += lerp(glowRaw, gl * _BloomTint.rgb / dot(_BloomTint.rgb, float3(0.299, 0.587, 0.114)), _BloomTintAmt);
                }
                col *= _Exposure;                                   // 自動露出: 明るすぎる絵は全体を少し下げる

                // ── フィルムの粒: 出力の画面 (湾曲した後) に、 毎フレームではなく _GrainFps で入れ替わる粒を乗せる。 平均は変えない (明るい粒と暗い粒が半々)。
                //   中間の明るさでいちばん目立ち、 真っ黒と真っ白では弱い (フィルムの粒と同じ)。
                //   粒は絵のドットの格子に揃えて切る (1 粒 = 絵の 1 ドット・湾曲した後の座標なので絵と一緒に曲がる)。 画面の画素に揃えて切ると、 絵のドットより細かい砂になる
                float2 gc = floor(panelUv * _GrainCells.xy);
                float gn = hash12(gc + floor(_Time.y * _GrainFps) * float2(37.17, 91.31)) - 0.5;
                //   足すのは見た目の明るさ (ガンマ空間) で。 このプロジェクトはリニア色空間なので、 そのまま足すと暗い所ほど粒が極端に強調される (黒が砂嵐になる)
                #ifdef UNITY_COLORSPACE_GAMMA
                float3 gcol = col;
                #else
                float3 gcol = LinearToGammaSpace(max(col, 0.0));
                #endif
                float lum = saturate(dot(gcol, float3(0.299, 0.587, 0.114)));
                gcol += gn * 2.0 * _Grain * (0.15 + 0.85 * 4.0 * lum * (1.0 - lum));
                #ifdef UNITY_COLORSPACE_GAMMA
                col = gcol;
                #else
                col = GammaToLinearSpace(max(gcol, 0.0));
                #endif

                col = PowerOn(col, _Power);                   // 起動演出 (電源オン)
                col = lerp(_Back.rgb, col, edgeA);            // 絵の外形の縁 (f > 0 の時だけ・上の edgeA)
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
