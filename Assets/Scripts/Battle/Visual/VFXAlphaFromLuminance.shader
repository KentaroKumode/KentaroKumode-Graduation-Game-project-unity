// VFXカメラの replacement shader。
// パーティクル素材の α が "見た目の不透明度" を表していない問題への対処:
//   Eric VFX 等の additive 素材は「透明に見える領域」も α=1 RGB=0 で記録されており、
//   通常のシェーダーで描画すると RT の α が真四角に焼き付き、 合成時に背景を黒く塗りつぶす。
//
// このシェーダーはどの particle shader も置き換え、 フラグメントで
//   rgb     = tex * 頂点カラー の RGB を α で premultiply
//   α (out) = max(rgb)  (= 輝度)
// として書き出す。 これにより RT の α は実際の "見た目の明るさ" を表すようになる。
//
// 結果:
//   - additive 素材の空白部 (RGB=0, α=1): premult後 rgb=0 → 輝度=0 → α=0 で透明
//   - additive 素材の明部:                premult後 rgb=明 → α=明 → 合成で正しく加算
//   - 黒い alpha-blend (Ink Slash 等):    premult後 rgb=0 → α=0 で見えなくなる ← トレードオフ
//
// 適用: VFXPixelCompositor.cs から Camera.SetReplacementShader(this, "RenderType") で適用。
Shader "Hidden/Battle/VFXAlphaFromLuminance"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float4 color  : COLOR;
        float2 uv     : TEXCOORD0;
    };
    struct v2f
    {
        float4 pos   : SV_POSITION;
        fixed4 color : COLOR;
        float2 uv    : TEXCOORD0;
    };

    sampler2D _MainTex;
    float4 _MainTex_ST;

    v2f vert(appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.color = v.color;
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        return o;
    }

    fixed4 frag(v2f i) : SV_Target
    {
        fixed4 tex = tex2D(_MainTex, i.uv);
        fixed4 c   = tex * i.color;
        // α で premultiply (additive 素材で α=1 RGB=0 の空白部は 0 になる)
        fixed3 rgb = c.rgb * c.a;
        fixed lum  = max(max(rgb.r, rgb.g), rgb.b);
        return fixed4(rgb, lum);
    }
    ENDCG

    // Transparent (大半のパーティクル)
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One One   // 加算で RGB と α (=輝度) を蓄積

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    // TransparentCutout
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    // Opaque (たまに混じる)
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
