// 夜タイトル用オーロラ：画面上端に向かってフェードする緑/紫の波がうねりながら流れる。
// 手続き的生成（テクスチャ不要）＋Additive ブレンドで星空に被せても色が濁らない。
//
// 用途: SpriteRenderer or MeshRenderer に貼る。 UV は (0,0)=左下／(1,1)=右上 想定。
// 規約: ピクセルアート要素は localScale 拡大禁止だが、 本シェーダは手続き生成のため Quad 拡大OK。
Shader "Sprites/Aurora"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ColorA ("バンドA色（緑系）", Color) = (0.30, 1.00, 0.65, 1)
        _ColorB ("バンドB色（紫系）", Color) = (0.55, 0.40, 1.00, 1)
        _Speed ("スクロール速度", Float) = 0.06
        _Freq ("横の波数", Float) = 3.0
        _Amplitude ("縦のうねり振幅(0..1)", Range(0, 0.5)) = 0.18
        _Thickness ("バンドの厚み(0.05..0.4)", Range(0.05, 0.4)) = 0.18
        _Intensity ("全体強度", Range(0, 2)) = 1.0
        _FadeTopY ("上端フェード開始(0..1)", Range(0, 1)) = 0.05
        _FadeBottomY ("下端フェード終了(0..1)", Range(0, 1)) = 0.55
        _PixelGridX ("ピクセル格子X(0=スナップ無)", Float) = 0
        _PixelGridY ("ピクセル格子Y(0=スナップ無)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One One // Additive

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f       { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _ColorA, _ColorB;
            float _Speed, _Freq, _Amplitude, _Thickness, _Intensity, _FadeTopY, _FadeBottomY;
            float _PixelGridX, _PixelGridY;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.color = IN.color;
                OUT.texcoord = IN.texcoord;
                return OUT;
            }

            // 細いバンド = ガウシアン的に中心ライン y0 から離れるほど暗くなる
            float band(float y, float y0, float thickness)
            {
                float d = abs(y - y0) / max(thickness, 0.0001);
                return exp(-d * d);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                // ピクセルパーフェクト: UV を ドット格子に量子化（LCD ドット単位でステアステップ）
                if (_PixelGridX > 0.5) uv.x = (floor(uv.x * _PixelGridX) + 0.5) / _PixelGridX;
                if (_PixelGridY > 0.5) uv.y = (floor(uv.y * _PixelGridY) + 0.5) / _PixelGridY;
                // 時間スクロールも格子に乗せる（細かく動かないので1Hz程度の更新でOK）
                float t = _Time.y * _Speed;
                if (_PixelGridX > 0.5) t = floor(t * _PixelGridX) / _PixelGridX;

                // 2つのバンドが異なる位相/速度でうねる
                float yA = 0.5 + sin(uv.x * _Freq * 6.2831853 + t * 4.0) * _Amplitude
                              + sin(uv.x * _Freq * 11.0 - t * 2.3) * _Amplitude * 0.5;
                float yB = 0.55 + sin(uv.x * _Freq * 4.7 - t * 3.1 + 1.7) * _Amplitude * 0.9
                               + cos(uv.x * _Freq * 8.3 + t * 1.6) * _Amplitude * 0.6;

                float ba = band(uv.y, yA, _Thickness);
                float bb = band(uv.y, yB, _Thickness * 0.85);

                // 縦フェード: 上端ほど薄く、 下端ほど無し（上から滲ませる）
                float vertFade = saturate(1.0 - smoothstep(_FadeTopY, _FadeBottomY, 1.0 - uv.y));

                fixed3 rgb = (_ColorA.rgb * ba + _ColorB.rgb * bb) * vertFade * _Intensity;
                return fixed4(rgb, 1.0); // Additive なので alpha は無視される
            }
            ENDCG
        }
    }
}
