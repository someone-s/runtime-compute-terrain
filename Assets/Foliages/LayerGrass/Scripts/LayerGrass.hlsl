// MIT License

// Copyright (c) 2021 NedMakesGames

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

// Make sure this file is not included twice
#ifndef GRASSLAYERS_INCLUDED
#define GRASSLAYERS_INCLUDED

// Include some helper functions
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "NMGLayerGrassHelpers.hlsl"

struct Attributes {
    float3 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 uvAndHeight  : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
    float4 uvAndHeight  : TEXCOORD0; // (U, V, clipping noise height, color lerp)
    float3 positionWS   : TEXCOORD1; // Position in world space
    float3 normalWS     : TEXCOORD2; // Normal vector in world space

    float4 positionCS   : SV_POSITION; // Position in clip space
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// These two textures are combined to create the grass pattern in the fragment function
TEXTURE2D(_NoiseATexture); SAMPLER(sampler_NoiseATexture);
TEXTURE2D(_NoiseBTexture); SAMPLER(sampler_NoiseBTexture);

// Wind properties
TEXTURE2D(_WindTexture); SAMPLER(sampler_WindTexture);

CBUFFER_START(UnityPerMaterial)
// Properties
float4 _BaseColor;
float4 _TopColor;
float _WorldPositionUVScale;
// These two textures are combined to create the grass pattern in the fragment function
float4 _NoiseATexture_ST;
float _NoiseABoost;
float _NoiseAScale;
float4 _NoiseBTexture_ST;
float _NoiseBBoost;
float _NoiseBScale;
// Wind properties
float4 _WindTexture_ST;
float _WindFrequency;
float _WindAmplitude;
CBUFFER_END

// Vertex function

VertexOutput Vertex(Attributes input) {
    // Initialize the output struct
    VertexOutput output = (VertexOutput)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    output.positionWS = GetVertexPositionInputs(input.positionOS).positionWS;
    output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;
    output.uvAndHeight = input.uvAndHeight;
    output.positionCS = TransformWorldToHClip(output.positionWS);

    return output;
}

// Fragment functions

half4 Fragment(VertexOutput input) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(input);

#ifdef USE_WORLD_POSITION_AS_UV
    float2 uv = input.positionWS.xz * _WorldPositionUVScale;
#else
    float2 uv = input.uvAndHeight.xy;
#endif
    float clipHeight = input.uvAndHeight.z;
	float layerHeight = input.uvAndHeight.w;

    // Calculate wind
    // Get the wind noise texture uv by applying scale and offset and then adding a time offset
    float2 windUV = TRANSFORM_TEX(uv, _WindTexture) + _Time.y * _WindFrequency;
    // Sample the wind noise texture and remap to range from -1 to 1
    float2 windNoise = SAMPLE_TEXTURE2D(_WindTexture, sampler_WindTexture, windUV).xy * 2 - 1;
    // Offset the grass UV by the wind. Higher layers are affected more
	uv += windNoise * (_WindAmplitude * layerHeight);

    // Sample the two noise textures, applying their scale and offset, and then the boost offset value
	float sampleA = SAMPLE_TEXTURE2D(_NoiseATexture, sampler_NoiseATexture, TRANSFORM_TEX(uv, _NoiseATexture)).r + _NoiseABoost;
	float sampleB = SAMPLE_TEXTURE2D(_NoiseBTexture, sampler_NoiseBTexture, TRANSFORM_TEX(uv, _NoiseBTexture)).r + _NoiseBBoost;
    // Combine the textures together using these scale variables. Lower values will reduce a texture's influence
	sampleA = 1 - (1 - sampleA) * _NoiseAScale;
	sampleB = 1 - (1 - sampleB) * _NoiseBScale;
    // If detailNoise * smoothNoise is less than height, this pixel will be discarded by the renderer
    // and will not render. The fragment function returns as well
	clip(sampleA * sampleB - clipHeight);

    // If the code reaches this far, this pixel should render

    // Gather some data for the lighting algorithm
    InputData lightingInput = (InputData)0;
    lightingInput.positionWS = input.positionWS;
    lightingInput.normalWS = NormalizeNormalPerPixel(input.normalWS); // Renormalize the normal to reduce interpolation errors
    lightingInput.viewDirectionWS = GetViewDirectionFromPosition(input.positionWS); // Calculate the view direction
    lightingInput.shadowCoord = CalculateShadowCoord(input.positionWS, input.positionCS); // Calculate the shadow map coord

    // Lerp between the three grass colors based on layer height
	float3 albedo = lerp(_BaseColor.rgb, _TopColor.rgb, layerHeight);

    // The URP simple lit algorithm
    // The arguments are lighting input data, albedo color, metalic color, specular color, smoothness, occlusion, emission color, and alpha

    SurfaceData surfaceData;
    surfaceData.albedo = albedo;
    surfaceData.specular = 1;
    surfaceData.metallic = 0;
    surfaceData.smoothness = 0.05;
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.emission = 0;
    surfaceData.occlusion = 1;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 1;

    return UniversalFragmentPBR(lightingInput, surfaceData);
}

#endif