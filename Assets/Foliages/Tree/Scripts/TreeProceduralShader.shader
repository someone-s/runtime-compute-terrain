Shader "Custom/TreeProceduralShader"
{
    Properties
    {
        _ColorTexture("Color Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags {
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "UniversalMaterialType" = "SimpleLit"
        }
        Pass {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            TEXTURE2D(_ColorTexture); SAMPLER(sampler_ColorTexture);

            CBUFFER_START(UnityPerMaterial)
            float4 _ColorTexture_ST;
            CBUFFER_END


            //float _WindFrequency;
            //float _WindAmplitude;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 lightAmount : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            StructuredBuffer<float4x4> _TransformMatrices;
            ByteAddressBuffer _Vertices;
            int _Stride;
            int _PositionOffset;
            int _NormalOffset;
            int _UVOffset;

            uint _Jump;
            float _JumpScale;

            float3 LoadPosition(uint index) {
                return asfloat(_Vertices.Load3(index * _Stride + _PositionOffset));
            }
            float3 LoadNormal(uint index) {
                return asfloat(_Vertices.Load3(index * _Stride + _NormalOffset));
            }
            float2 LoadUV(uint index) {
                return asfloat(_Vertices.Load2(index * _Stride + _UVOffset));
            }

            // float randomRange(float2 seed, float min, float max)
            // {
            //     float random = frac(sin(dot(seed, float2(12.9898, 78.233)))*43758.5453);
            //     return lerp(min, max, random);
            // }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                float4 positionOS = float4(LoadPosition(IN.vertexID), 1.0);
                // positionOS.x *= _JumpScale;
                // positionOS.z *= _JumpScale;
                float4x4 objectToWorld = _TransformMatrices[IN.instanceID * _Jump];
                float4 positionWS = mul(objectToWorld, positionOS);

                // float xOffset = randomRange(float2(IN.vertexID, IN.instanceID), -1.0, 1.0);
                // positionWS.x += sin((_Time.y * _WindFrequency) + xOffset) * (_WindAmplitude * positionOS.y);

                // float zOffset = randomRange(float2(IN.instanceID, IN.vertexID), -1.0, 1.0);
                // positionWS.z += sin((_Time.y * _WindFrequency) + zOffset) * (_WindAmplitude * positionOS.y);

                OUT.positionCS = mul(UNITY_MATRIX_VP, positionWS);

                OUT.shadowCoord = TransformWorldToShadowCoord(positionWS.xyz);

                OUT.uv = LoadUV(IN.vertexID);

                Light light = GetMainLight();

                float4 normalOS = float4(LoadNormal(IN.vertexID), 0.0);
                float4 normalWS = mul(objectToWorld, normalOS);
                OUT.lightAmount = LightingLambert(light.color, light.direction, normalWS.xyz);

                
                return OUT;
            }

            half4 frag(Varyings IN) : SV_TARGET
            {

                half shadowAmount = lerp(0.2, 1.0, MainLightRealtimeShadow(IN.shadowCoord));
                half4 lightAmount = lerp(half4(0.5, 0.5, 0.5, 1.0), half4(1.0, 1.0, 1.0, 1.0), half4(IN.lightAmount, 1.0));
	            half4 color = SAMPLE_TEXTURE2D(_ColorTexture, sampler_ColorTexture, TRANSFORM_TEX(IN.uv, _ColorTexture));
                return shadowAmount * lightAmount * color;
            }


            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
            };

            StructuredBuffer<float4x4> _TransformMatrices;
            ByteAddressBuffer _Vertices;
            int _Stride;
            int _PositionOffset;
            int _NormalOffset;

            uint _Jump;
            float _JumpScale;

            float3 LoadPosition(uint index) {
                return asfloat(_Vertices.Load3(index * _Stride + _PositionOffset));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                float4 positionOS = float4(LoadPosition(IN.vertexID), 1.0);
                // positionOS.x *= _JumpScale;
                // positionOS.z *= _JumpScale;
                float4x4 objectToWorld = _TransformMatrices[IN.instanceID * _Jump];

                float4 positionWS = mul(objectToWorld, positionOS);
                OUT.positionCS = mul(UNITY_MATRIX_VP, positionWS);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

    }
}
