// VFX RT を完全透明 (RGBA = 0,0,0,0) でクリアする専用シェーダー。
// Graphics.Blit(null, rt, mat) で RT 全体に (0,0,0,0) を強制描画する。
//
// 用途: BiRP の Camera.Clear が α を 0 に書かないバグへの対処。
// このシェーダーは Frag で固定値 0 を返すため、 ハード/ドライバの clear 動作に依存しない。
Shader "Hidden/Battle/ClearTransparent"
{
    Properties
    {
        _MainTex ("_", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 frag(v2f_img i) : SV_Target
            {
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
