Shader "Terrain/TerrainLit_Instance"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
       Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False" "TerrainCompatible" = "True"}
        LOD 100

        Pass
        {
            Name "VTFeedback"
            Tags { "LightMode" = "VTFeedback" }

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols    
            #pragma target 3.5

            #include "FeedbackCommon.hlsl"	
            #pragma vertex FeedbackVert
            #pragma fragment FeedbackFrag

            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            //#pragma enable_d3d11_debug_symbols
            #pragma target 3.5
            #pragma multi_compile __ _TERRAIN_VIRTUAL_TEXTURE

            #include "TerrainLitInclude.hlsl"


            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment

            ENDHLSL
        }

    }
}
