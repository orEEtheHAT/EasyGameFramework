Shader "EasyGameFramework/Interaction/Focus Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.1, 0.8, 1, 1)
        _OutlineWidth ("World Width", Range(0.001, 0.1)) = 0.015
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+50" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "FocusOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZTest LEqual
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(_OutlineColor.rgb, 1); }
            ENDHLSL
        }
    }
}
