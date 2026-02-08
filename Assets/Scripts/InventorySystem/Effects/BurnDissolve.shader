Shader "Custom/BurnDissolve"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _NoiseTex ("Dissolve Noise", 2D) = "white" {}
        _DissolveAmount ("Dissolve Amount", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width", Range(0, 0.2)) = 0.05
        _EdgeColor1 ("Edge Color Inner (Yellow)", Color) = (1, 0.9, 0.3, 1)
        _EdgeColor2 ("Edge Color Outer (Red)", Color) = (1, 0.2, 0, 1)
        _EmissionStrength ("Emission Strength", Range(0, 8)) = 3
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 200
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _NoiseTex;
        fixed4 _Color;
        float _DissolveAmount;
        float _EdgeWidth;
        fixed4 _EdgeColor1;
        fixed4 _EdgeColor2;
        float _EmissionStrength;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // ベースカラー
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            
            // ノイズ値を取得
            float noise = tex2D(_NoiseTex, IN.uv_MainTex).r;
            
            // ディゾルブ判定
            float dissolveThreshold = _DissolveAmount;
            
            // ノイズがしきい値以下なら完全に消す
            if (noise < dissolveThreshold - _EdgeWidth)
            {
                discard;
            }
            
            // エッジ（燃え境界）の計算
            float edgeFactor = 1.0 - saturate((noise - dissolveThreshold + _EdgeWidth) / _EdgeWidth);
            
            // エッジのグラデーション（内側=黄色、外側=赤）
            fixed4 edgeColor = lerp(_EdgeColor1, _EdgeColor2, edgeFactor);
            
            // エッジ部分はEmissionで光らせる
            float isEdge = step(dissolveThreshold - _EdgeWidth, noise) * step(noise, dissolveThreshold);
            
            o.Albedo = lerp(c.rgb, edgeColor.rgb, isEdge);
            o.Emission = edgeColor.rgb * isEdge * _EmissionStrength;
            o.Alpha = lerp(c.a, edgeColor.a * (1.0 - edgeFactor * 0.5), isEdge);
            o.Metallic = 0;
            o.Smoothness = 0;
        }
        ENDCG
    }

    FallBack "Transparent/Diffuse"
}
