#ifndef GRASS_GBUFFER_PASS_INCLUDED
#define GRASS_GBUFFER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

// keep this file in sync with LitForwardPass.hlsl

struct Attributes
{
    uint vertexID : SV_VertexID;
    uint instanceID : SV_InstanceID;
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;

    float3 posWS                    : TEXCOORD1;    // xyz: posWS

    #ifdef _NORMALMAP
        half4 normal                   : TEXCOORD2;    // xyz: normal, w: viewDir.x
        half4 tangent                  : TEXCOORD3;    // xyz: tangent, w: viewDir.y
        half4 bitangent                : TEXCOORD4;    // xyz: bitangent, w: viewDir.z
    #else
        half3  normal                  : TEXCOORD2;
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half3 vertexLighting            : TEXCOORD5; // xyz: vertex light
    #endif

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        float4 shadowCoord              : TEXCOORD6;
    #endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD8; // Dynamic lightmap UVs
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD9;
#endif

    float4 positionCS               : SV_POSITION;
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.posWS;
    inputData.positionCS = input.positionCS;

    #ifdef _NORMALMAP
        half3 viewDirWS = half3(input.normal.w, input.tangent.w, input.bitangent.w);
        inputData.normalWS = TransformTangentToWorld(normalTS,half3x3(input.tangent.xyz, input.bitangent.xyz, input.normal.xyz));
    #else
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(inputData.positionWS);
        inputData.normalWS = input.normal;
    #endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    viewDirWS = SafeNormalize(viewDirWS);

    inputData.viewDirectionWS = viewDirWS;

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        inputData.shadowCoord = input.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        inputData.vertexLighting = input.vertexLighting.xyz;
    #else
        inputData.vertexLighting = half3(0, 0, 0);
    #endif

    inputData.fogCoord = 0; // we don't apply fog in the gbuffer pass
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.staticLightmapUV; // Force fallback to static uv
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
    #else
    inputData.vertexSH = input.vertexSH;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = input.probeOcclusion;
    #endif
    #endif
}


// Calculates the shadow texture coordinate for lighting calculations
float4 CalculateShadowCoord(float3 positionWS, float4 positionCS) {
    // Calculate the shadow coordinate depending on the type of shadows currently in use
#if SHADOWS_SCREEN
    return ComputeScreenPos(positionCS);
#else
	return TransformWorldToShadowCoord(positionWS);
#endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
    // Force fallback to static uv for dynamic calculations
#if defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        inputData.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Simple Lighting) shader
Varyings LitPassVertexSimple(Attributes input)
{
    Varyings output = (Varyings)0;

    float3 positionOS = LoadPosition(input.vertexID);
    positionOS.x *= _JumpScale;
    positionOS.z *= _JumpScale;
    float4x4 objectToWorld = _TransformMatrices[input.instanceID * _Jump];
    float3 positionWS = mul(objectToWorld, float4(positionOS, 1)).xyz;

    float xOffset = randomRange(float2(1.0, input.instanceID), -1.0, 1.0);
    positionWS.x += sin((_Time.y * _WindFrequency) + xOffset) * (_WindAmplitude * positionOS.y);

    float zOffset = randomRange(float2(input.instanceID, 1.0), -1.0, 1.0);
    positionWS.z += sin((_Time.y * _WindFrequency) + zOffset) * (_WindAmplitude * positionOS.y);

    float4 positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1));

    float3 normalOS = LoadNormal(input.vertexID);
    float3 normalWS = mul(objectToWorld, float4(normalOS, 0)).xyz;

    float3 tangentOS = LoadTangent(input.vertexID);
    float3 tangentWS = mul(objectToWorld, float4(tangentOS, 0)).xyz;

    float3 bitangentWS = cross(normalWS, tangentWS);

    float2 staticLightmapUV = LoadStaticLightmapUV(input.vertexID);

    float2 texcoord = LoadUV(input.vertexID);


    output.uv = TRANSFORM_TEX(texcoord, _BaseMap);
    output.posWS.xyz = positionWS;
    output.positionCS = positionCS;

    #ifdef _NORMALMAP
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
        output.normal = half4(normalWS, viewDirWS.x);
        output.tangent = half4(tangentWS, viewDirWS.y);
        output.bitangent = half4(bitangentWS, viewDirWS.z);
    #else
        output.normal = NormalizeNormalPerVertex(normalWS);
    #endif

    OUTPUT_LIGHTMAP_UV(staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = staticLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw; // Force fallback to static uv
#endif
    OUTPUT_SH4(positionWS, output.normal.xyz, GetWorldSpaceNormalizeViewDir(positionWS), output.vertexSH, output.probeOcclusion);

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half3 vertexLight = VertexLighting(positionWS, normalWS);
        output.vertexLighting = vertexLight;
    #endif

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        output.shadowCoord = CalculateShadowCoord(positionWS, positionCS);
    #endif

    return output;
}



// Used for StandardSimpleLighting shader
FragmentOutput LitPassFragmentSimple(Varyings input)
{
    SurfaceData surfaceData;
    InitializeSimpleLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
    half4 color = half4(inputData.bakedGI * surfaceData.albedo + surfaceData.emission, surfaceData.alpha);

    return SurfaceDataToGbuffer(surfaceData, inputData, color.rgb, kLightingSimpleLit);
};

#endif
