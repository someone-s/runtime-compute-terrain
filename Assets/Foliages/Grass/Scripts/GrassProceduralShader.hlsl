#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#ifndef SHADOW_CASTER_PASS
#include "NMGHelpers.hlsl"
#else
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#endif

///////////////////////////////////////////////////////////////////////////////
//                           Mesh Load Functions                             //
///////////////////////////////////////////////////////////////////////////////

ByteAddressBuffer _Vertices;
int _Stride;
int _PositionOffset;
int _NormalOffset;
int _TangentOffset;
int _UVOffset;

float3 LoadPosition(uint index) {
    return asfloat(_Vertices.Load3(index * _Stride + _PositionOffset));
}
float3 LoadNormal(uint index) {
    return asfloat(_Vertices.Load3(index * _Stride + _NormalOffset));
}
float3 LoadTangent(uint index) {
    return asfloat(_Vertices.Load3(index * _Stride + _TangentOffset));
}
float2 LoadUV(uint index) {
    return asfloat(_Vertices.Load2(index * _Stride + _UVOffset));
}

///////////////////////////////////////////////////////////////////////////////
//                              Frag Functions                               //
///////////////////////////////////////////////////////////////////////////////

CBUFFER_START(UnityPerMaterial)
half4 _StartColor;
half4 _EndColor;
CBUFFER_END

struct Varyings {
    float4 positionCS : SV_POSITION;
    #ifndef SHADOW_CASTER_PASS // NOT
    float2 uv : TEXCOORD0;
    float4 positionWS : TEXCOORD1;
    float4 normalWS : TEXCOORD2;
    #endif
};

half4 Fragment(Varyings IN) : SV_TARGET
{
    #ifndef SHADOW_CASTER_PASS // NOT

    InputData inputData = (InputData)0;
    inputData.positionWS = IN.positionWS.xyz;
    inputData.normalWS = IN.normalWS.xyz;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS.xyz);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS.xy);
    
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = lerp(_StartColor, _EndColor, IN.uv.y).rgb;
    surfaceData.specular = 1;
    surfaceData.metallic = 0;
    surfaceData.smoothness = 0.05;
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.emission = 0;
    surfaceData.occlusion = 1;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 1;

    return UniversalFragmentBlinnPhong(inputData, surfaceData);
    #else
    return 0;
    #endif
}

///////////////////////////////////////////////////////////////////////////////
//                              Vert Functions                               //
///////////////////////////////////////////////////////////////////////////////

StructuredBuffer<float4x4> _TransformMatrices;

uint _Jump;
float _JumpScale;

float _WindFrequency;
float _WindAmplitude;

struct Attributes
{
    uint vertexID : SV_VertexID;
    uint instanceID : SV_InstanceID;
};

float randomRange(float2 seed, float min, float max)
{
    float random = frac(sin(dot(seed, float2(12.9898, 78.233)))*43758.5453);
    return lerp(min, max, random);
}

Varyings Vertex(Attributes IN)
{
    Varyings OUT;
    
    float4 positionOS = float4(LoadPosition(IN.vertexID), 1.0);
    positionOS.x *= _JumpScale;
    positionOS.z *= _JumpScale;
    float4x4 objectToWorld = _TransformMatrices[IN.instanceID * _Jump];
    float4 positionWS = mul(objectToWorld, positionOS);

    float xOffset = randomRange(float2(IN.vertexID, IN.instanceID), -1.0, 1.0);
    positionWS.x += sin((_Time.y * _WindFrequency) + xOffset) * (_WindAmplitude * positionOS.y);

    float zOffset = randomRange(float2(IN.instanceID, IN.vertexID), -1.0, 1.0);
    positionWS.z += sin((_Time.y * _WindFrequency) + zOffset) * (_WindAmplitude * positionOS.y);

    OUT.positionCS = mul(UNITY_MATRIX_VP, positionWS);

    #ifndef SHADOW_CASTER_PASS // NOT
    OUT.uv = LoadUV(IN.vertexID);

    float4 normalOS = float4(LoadNormal(IN.vertexID), 0.0);
    float4 normalWS = mul(objectToWorld, normalOS);

    OUT.positionWS = positionWS;
    OUT.normalWS = normalWS;
    #endif
    
    return OUT;
}