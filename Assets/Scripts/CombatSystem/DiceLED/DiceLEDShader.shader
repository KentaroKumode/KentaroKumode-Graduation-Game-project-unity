// =============================================================================
// DiceLED Shader
// =============================================================================
// 目的: LED表面の自己発光を軽量に再現する Unlit + Emission シェーダー
//
// パフォーマンス対策:
//   - GPU Instancing 対応 → 同メッシュLED を 1 ドローコールにバッチ
//   - MaterialPropertyBlock で _EmissionColor を個別制御
//     → マテリアルインスタンス不要 = メモリ・ドローコール節約
//   - ライティング計算なし（Unlit）→ 頂点/フラグメント負荷最小
//
// セットアップ:
//   1. マテリアルを 1 つだけ作成（シェーダー = CombatSystem/DiceLED）
//   2. Inspector で "Enable GPU Instancing" を ON
//   3. 全90個の LED MeshRenderer にこのマテリアルを割り当て
//   4. C# 側は MaterialPropertyBlock で _EmissionColor を制御
// =============================================================================

Shader "CombatSystem/DiceLED"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Color ("Main Color", Color) = (1, 1, 1, 1)
        _BaseColor ("Base Color (消灯時)", Color) = (0.05, 0.05, 0.05, 1)
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            // GPU Instancing バッファ
            // MaterialPropertyBlock から per-instance で上書き可能
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // アルベド（テクスチャ × メインカラー）
                float4 albedo = tex2D(_MainTex, i.uv) * _Color;

                float4 baseCol = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float4 emission = UNITY_ACCESS_INSTANCED_PROP(Props, _EmissionColor);

                // アルベドで表面色を決め、BaseColor + Emission を乗算
                // 消灯時: albedo × baseCol（暗いテクスチャ）
                // 点灯時: albedo × (baseCol + emission)（テクスチャを保ったまま発光）
                float4 col = albedo * (baseCol + emission);
                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Color"
}
