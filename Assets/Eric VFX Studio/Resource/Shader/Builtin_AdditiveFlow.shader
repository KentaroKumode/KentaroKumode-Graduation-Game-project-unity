Shader "Eric/BuiltIn_AdditiveFlow"
{
    Properties
    {
        [MainTexture] _MainTex("Main Texture", 2D) = "white" {}
        [HDR] _TintColor("Tint Color", Color) = (1, 1, 1, 1)
        _FlowSpeed("MainTex Flow Speed", Vector) = (0, 0, 0, 0)
        _Brightness("Brightness Boost", Float) = 1.0
        
        [Header(Soft Particles)]
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticlesEnabled("Enable Soft Particles", Float) = 0
        _SoftParticlesFadeDistance("Fade Distance", Range(0.01, 10.0)) = 1.0

        [Header(UV Distortion)]
        _DistortionMap("Distortion Map", 2D) = "white" {}
        _DistortionPower("Distortion Power", Range(0, 1)) = 0
        _DistortionSpeed("Distortion Speed", Vector) = (0, 0, 0, 0)

        [Header(Fresnel Rim Light)]
        [Toggle(_FRESNEL_ON)] _UseFresnel("Enable Fresnel", Float) = 0
        [Enum(Normal Based, 0, UV Based, 1)] _FresnelType("Fresnel Type", Float) = 0
        [HDR] _FresnelColor("Fresnel Color", Color) = (1, 1, 0, 1)
        _FresnelIntensity("Fresnel Intensity", Float) = 5.0
        _FresnelWidth("Fresnel Width", Range(0.1, 15.0)) = 5.0
        [Toggle(_FRESNEL_MASK_ON)] _FresnelMasksTexture("Use Fresnel as Texture Mask", Float) = 0

        [Header(Dissolve)]
        [Toggle(_USEPARTICLEALPHADISSOLVE_ON)] _UseAlphaDissolve("Use Alpha as Dissolve", Float) = 0
        _DissolveTex("Dissolve Texture", 2D) = "white" {}
        _DissolveAmount("Dissolve Amount", Range(0, 1)) = 0.0
        _VertexAlphaRef("Alpha Ref", Range(0.01, 1.0)) = 1.0
        [HDR] _DissolveEdgeColor("Dissolve Edge Color", Color) = (1, 0, 0, 1)
        _DissolveEdgeWidth("Dissolve Edge Width", Range(0, 0.2)) = 0.05

        [Header(Overlay)]
        _OverlayTex("Overlay Texture", 2D) = "white" {}
        [HDR] _OverlayColor("Overlay Color", Color) = (1, 1, 1, 1)
        _OverlayFlowSpeed("Overlay Flow Speed", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Cull Off ZWrite Off Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles
            #pragma shader_feature _SOFTPARTICLES_ON
            #pragma shader_feature _FRESNEL_ON
            #pragma shader_feature _FRESNEL_MASK_ON
            #pragma shader_feature _USEPARTICLEALPHADISSOLVE_ON

            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 projPos : TEXCOORD1; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD3; float3 viewDirWS : TEXCOORD4; float4 color : COLOR; };

            sampler2D _MainTex, _DissolveTex, _DistortionMap, _OverlayTex, _CameraDepthTexture;
            float4 _MainTex_ST, _DissolveTex_ST, _DistortionMap_ST, _OverlayTex_ST;
            half4 _TintColor, _FlowSpeed, _DistortionSpeed, _FresnelColor, _DissolveEdgeColor, _OverlayColor, _OverlayFlowSpeed;
            half _Brightness, _FresnelWidth, _FresnelIntensity, _DistortionPower, _DissolveAmount, _VertexAlphaRef, _DissolveEdgeWidth, _FresnelType, _SoftParticlesFadeDistance;

            v2f vert(appdata v) {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                o.uv = v.uv;
                o.color = v.color;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                o.viewDirWS = _WorldSpaceCameraPos.xyz - worldPos;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                float softFade = 1.0;
                #ifdef _SOFTPARTICLES_ON
                    float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos));
                    float sceneZ, partZ;
                    if (unity_OrthoParams.w == 0) { 
                        sceneZ = LinearEyeDepth(rawDepth);
                        partZ = i.projPos.z;
                    } else { 
                        #if UNITY_REVERSED_Z
                            sceneZ = _ProjectionParams.y + (1.0 - rawDepth) * (_ProjectionParams.z - _ProjectionParams.y);
                        #else
                            sceneZ = _ProjectionParams.y + rawDepth * (_ProjectionParams.z - _ProjectionParams.y);
                        #endif
                        partZ = i.projPos.z;
                    }
                    softFade = saturate((sceneZ - partZ) / max(0.01, _SoftParticlesFadeDistance));
                #endif

                half2 uvOff = (tex2D(_DistortionMap, i.uv * _DistortionMap_ST.xy + _DistortionMap_ST.zw + _DistortionSpeed.xy * _Time.y).rg * 2.0 - 1.0) * _DistortionPower;
                half4 tex = tex2D(_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw + _FlowSpeed.xy * _Time.y + uvOff);
                half4 over = tex2D(_OverlayTex, i.uv * _OverlayTex_ST.xy + _OverlayTex_ST.zw + _OverlayFlowSpeed.xy * _Time.y + uvOff) * _OverlayColor;

                half dAmt = _DissolveAmount;
                #ifdef _USEPARTICLEALPHADISSOLVE_ON
                    dAmt = saturate(_DissolveAmount + (1.0 - saturate(i.color.a / max(0.001, _VertexAlphaRef))));
                #endif
                half dVal = tex2D(_DissolveTex, i.uv * _DissolveTex_ST.xy + _DissolveTex_ST.zw).r;
                half dMask = step(dAmt, dVal) * tex.a;
                half eMask = saturate(step(dAmt - _DissolveEdgeWidth, dVal) - step(dAmt, dVal)) * tex.a * saturate(dAmt * 100.0);

                half3 rgb = tex.rgb * _TintColor.rgb * i.color.rgb * over.rgb * _Brightness;
                half alpha = tex.a * _TintColor.a * i.color.a * over.a * softFade;

                #ifdef _FRESNEL_ON
                    half fPower = max(0.1, 15.1 - _FresnelWidth);
                    half fEff = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), normalize(i.viewDirWS)))), fPower);
                    #ifdef _FRESNEL_MASK_ON
                        rgb *= fEff; alpha *= fEff;
                    #endif
                    rgb += _FresnelColor.rgb * fEff * _FresnelIntensity;
                #endif

                half3 finalRGB = (rgb * dMask + _DissolveEdgeColor.rgb * eMask) * alpha;
                return half4(finalRGB, 1.0);
            }
            ENDCG
        }
    }
}