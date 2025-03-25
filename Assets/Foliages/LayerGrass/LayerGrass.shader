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

Shader "Grass/LayerGrass" {
    Properties {
        _BaseColor("Base color", Color) = (0, 0.5, 0, 1) // Color of the lowest layer
        _TopColor("Top color", Color) = (0, 1, 0, 1) // Color of the highest layer
        _NoiseATexture("Noise A", 2D) = "white" {} // Texture A used to clip layers
        _NoiseABoost("Noise A offset", Float) = 0 // An offset to texture A
        _NoiseAScale("Noise A scale", Float) = 1 // The influence of rexture A
        _NoiseBTexture("Noise B", 2D) = "white" {} // Texture B used to clip layers
        _NoiseBBoost("Noise B offset", Float) = 0 // An offset to texture B
        _NoiseBScale("Noise B scale", Float) = 1 // The influence of rexture B
        _WindTexture("Wind noise texture", 2D) = "white" {} // A wind noise texture
        _WindFrequency("Wind frequency", Float) = 1 // Wind noise offset by time
        _WindAmplitude("Wind strength", Float) = 1 // The largest UV offset of wind
        [Toggle(USE_WORLD_POSITION_AS_UV)] _UseWorldPosition("Use world pos as UV", Float) = 0
        _WorldPositionUVScale("World pos UV scale", Float) = 1
    }

    SubShader {
        // UniversalPipeline needed to have this render in URP
        Tags{"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"}

        // Forward Lit Pass
        Pass {

            Name "ForwardLit"
            Tags{"LightMode" = "UniversalForward"}

            HLSLPROGRAM
            #pragma target 4.5

            // Signal this shader requires a compute buffer
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            // Lighting and shadow keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            // Register own on keyword
            #pragma shader_feature USE_WORLD_POSITION_AS_UV

            // Register our functions
            #pragma vertex Vertex
            #pragma fragment Fragment

            // Incude our logic file
            #include "LayerGrass.hlsl"    

            ENDHLSL
        }
    }
}
