Shader "Custom/ProceduralShader"
{
    Properties
    {
        [MainColor] _StartColor("StartColor", Color) = (1,1,1,1)
        [MainColor] _EndColor("EndColor", Color) = (1,1,1,1)

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
            
            CBUFFER_START(UnityPerMaterial)
            half4 _StartColor;
            half4 _EndColor;
            CBUFFER_END

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

            StructuredBuffer<float4x4> TransformMatrices;
            ByteAddressBuffer Vertices;
            int stride;
            int positionOffset;
            int normalOffset;
            int uvOffset;

            uint jump;

            float3 LoadPosition(uint index) {
                return asfloat(Vertices.Load3(index * stride + positionOffset));
            }
            float3 LoadNormal(uint index) {
                return asfloat(Vertices.Load3(index * stride + normalOffset));
            }
            float2 LoadUV(uint index) {
                return asfloat(Vertices.Load2(index * stride + uvOffset));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                float4 positionOS = float4(LoadPosition(IN.vertexID), 1.0);
                float t = 1 + (jump - 1) * 0.5;
                float4x4 scale = float4x4(
                    t,   0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, t,   0.0,
                    0.0, 0.0, 0.0, 1.0 
                );
                positionOS = mul(scale, positionOS);
                float4x4 objectToWorld = TransformMatrices[IN.instanceID * jump];

                float4 positionWS = mul(objectToWorld, positionOS);
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
                half4 color = lerp(_StartColor, _EndColor, IN.uv.y);
                //return tex2D(_MainTex, i.uv);
                //return lightAmount * half4(0.243, 0.360, 0.196, 0.0);
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
            
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
            };

            StructuredBuffer<float4x4> TransformMatrices;
            ByteAddressBuffer Vertices;
            int stride;
            int positionOffset;
            int normalOffset;

            uint jump;

            float3 LoadPosition(uint index) {
                return asfloat(Vertices.Load3(index * stride + positionOffset));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                float4 positionOS = float4(LoadPosition(IN.vertexID), 1.0);
                float4x4 objectToWorld = TransformMatrices[IN.instanceID * jump];

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
