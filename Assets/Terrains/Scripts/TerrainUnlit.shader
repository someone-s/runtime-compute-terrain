Shader "Terrain/Unlit"
{
    Properties
    {
        // [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        // [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        // _Cutoff("AlphaCutout", Range(0.0, 1.0)) = 0.5

        // // BlendMode
        // _Surface("__surface", Float) = 0.0
        // _Blend("__mode", Float) = 0.0
        // _Cull("__cull", Float) = 2.0
        // [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        // [HideInInspector] _BlendOp("__blendop", Float) = 0.0
        // [HideInInspector] _SrcBlend("__src", Float) = 1.0
        // [HideInInspector] _DstBlend("__dst", Float) = 0.0
        // [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        // [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        // [HideInInspector] _ZWrite("__zw", Float) = 1.0
        // [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        // [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0

        // // Editmode props
        // _QueueOffset("Queue offset", Float) = 0.0

        // // ObsoleteProperties
        // [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        // [HideInInspector] _Color("Base Color", Color) = (0.5, 0.5, 0.5, 1)
        // [HideInInspector] _SampleGI("SampleGI", float) = 0.0 // needed from bakedlit
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "UniversalMaterialType" = "Unlit"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        // -------------------------------------
        // Render State Commands
        Blend [_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.UnlitShader"
}
