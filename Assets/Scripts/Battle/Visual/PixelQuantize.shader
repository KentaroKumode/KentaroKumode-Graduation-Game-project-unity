// カラー量子化シェーダー: 入力テクスチャの RGB を _Levels 段階にスナップ。
// 例: _Levels=4 で各チャンネル 0/0.33/0.66/1.0 の 4段階 → 4^3=64色のパレット風になる。
// PixelPostFilter から動的に Blit され、 ピクセル化済みの描画にパレット感を加える。
Shader "Hidden/Battle/PixelQuantize"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Levels  ("Color Levels per Channel", Float) = 8
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
            float _Levels;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float lv = max(2.0, _Levels);
                // (floor + 0.5) / lv で各チャンネルを lv 段階に丸める
                c.rgb = floor(c.rgb * lv) / lv;
                return c;
            }
            ENDCG
        }
    }
}
