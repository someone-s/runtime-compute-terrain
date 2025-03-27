#ifndef GRASS_SHADOW_CASTER_PASS_INCLUDED
#define GRASS_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

// Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
// For Directional lights, _LightDirection is used when applying shadow Normal Bias.
// For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
float3 _LightDirection;
float3 _LightPosition;

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


Varyings ShadowPassVertex(Attributes input)
{
    Varyings output;

    float3 positionOS = LoadPosition(input.vertexID);
    positionOS.x *= _JumpScale;
    positionOS.z *= _JumpScale;
    float4x4 objectToWorld = _TransformMatrices[input.instanceID * _Jump];
    float3 positionWS = mul(objectToWorld, float4(positionOS, 1)).xyz;

    float xOffset = randomRange(float2(input.vertexID, input.instanceID), -1.0, 1.0);
    positionWS.x += sin((_Time.y * _WindFrequency) + xOffset) * (_WindAmplitude * positionOS.y);

    float zOffset = randomRange(float2(input.instanceID, input.vertexID), -1.0, 1.0);
    positionWS.z += sin((_Time.y * _WindFrequency) + zOffset) * (_WindAmplitude * positionOS.y);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float3 normalOS = LoadNormal(input.vertexID);
    float3 normalWS = mul(objectToWorld, float4(normalOS, 0)).xyz;

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    positionCS = ApplyShadowClamping(positionCS);

    float2 texcoord = LoadUV(input.vertexID);

    #if defined(_ALPHATEST_ON)
    output.uv = TRANSFORM_TEX(texcoord, _BaseMap);
    #endif

    output.positionCS = positionCS;
    return output;
}

half4 ShadowPassFragment(Varyings input) : SV_TARGET
{
    #if defined(_ALPHATEST_ON)
        Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
    #endif

    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.positionCS);
    #endif

    return 0;
}

#endif
