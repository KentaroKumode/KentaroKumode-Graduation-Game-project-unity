// ロード画面の板 (液晶を写すカメラの前・地図の板の手前) 用 (2026-11-01)。 入り・出のトランジションを、 液晶のドット (480×270) 単位の網目で行う。
//   _Fade = ロード画面が見えている割合 (0..1)。 4×4 の Bayer 行列でドットごとに「見せる / 見せない」を決める (半透明は使わない)。
//   見せないドット: _UsePrev = 1 なら写し取った直前の画面 (_PrevTex) を出す (入り) / 0 なら描かずに捨てて、 後ろの本物の地図を見せる (出)。
// Resources に置くのは、 ビルドに必ず含めるため (Shader.Find で引く)。
Shader "Game2D/LoadingBlend"
{
    Properties
    {
        _MainTex ("ロード画面 (480×270)", 2D) = "black" {}
        _PrevTex ("直前の画面 (液晶の RT を写し取ったもの)", 2D) = "black" {}
        _Fade ("ロード画面が見えている割合", Range(0, 1)) = 1
        _UsePrev ("見せないドットに直前の画面を出すか (1 = 出す / 0 = 捨てる)", Float) = 0
        _Dots ("ドットの数", Vector) = (480, 270, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _PrevTex;
            float _Fade, _UsePrev;
            float4 _Dots;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            // 4×4 Bayer 行列 (0..15) を、 ドットの座標から引く
            float Bayer(float2 d)
            {
                float2 m = fmod(floor(d), 4.0);
                float a = m.x, b = m.y;                           // 2 ビットずつを交互に組む (x と y の排他的論理和・y)
                float x0 = fmod(a, 2.0), x1 = floor(a / 2.0), y0 = fmod(b, 2.0), y1 = floor(b / 2.0);
                float bx0 = fmod(x0 + y0, 2.0), bx1 = fmod(x1 + y1, 2.0);
                return bx0 * 8.0 + y0 * 4.0 + bx1 * 2.0 + y1;    // 標準の 4×4 Bayer と同じ並び
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = i.uv * _Dots.xy;
                float th = (Bayer(d) + 0.5) / 16.0;
                if (_Fade >= th) return tex2D(_MainTex, i.uv);
                if (_UsePrev > 0.5) return tex2D(_PrevTex, i.uv);
                clip(-1);
                return 0;
            }
            ENDCG
        }
    }
}
