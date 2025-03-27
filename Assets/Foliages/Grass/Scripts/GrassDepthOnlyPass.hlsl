#ifndef GRASS_DEPTH_ONLY_PASS_INCLUDED
#define GRASS_DEPTH_ONLY_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    uint vertexID : SV_VertexID;
    uint instanceID : SV_InstanceID;
};

struct Varyings
{
    #if defined(_ALPHATEST_ON)
        float2 uv       : TEXCOORD0;
    #endif
    float4 positionCS   : SV_POSITION;
};

Varyings DepthOnlyVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    float3 positionOS = LoadPosition(input.vertexID);
    float4x4 objectToWorld = _TransformMatrices[input.instanceID * _Jump];
    float3 positionWS = mul(objectToWorld, float4(positionOS, 1)).xyz;

    float xOffset = randomRange(float2(input.vertexID, input.instanceID), -1.0, 1.0);
    positionWS.x += sin((_Time.y * _WindFrequency) + xOffset) * (_WindAmplitude * positionOS.y);

    float zOffset = randomRange(float2(input.instanceID, input.vertexID), -1.0, 1.0);
    positionWS.z += sin((_Time.y * _WindFrequency) + zOffset) * (_WindAmplitude * positionOS.y);

    float4 positionCS = TransformWorldToHClip(positionWS);

    float2 texcoord = LoadUV(input.vertexID);

    #if defined(_ALPHATEST_ON)
        output.uv = TRANSFORM_TEX(texcoord, _BaseMap);
    #endif
    output.positionCS = positionCS;
    return output;
}

half DepthOnlyFragment(Varyings input) : SV_TARGET
{
    #if defined(_ALPHATEST_ON)
        Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
    #endif

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    return input.positionCS.z;
}
#endif
