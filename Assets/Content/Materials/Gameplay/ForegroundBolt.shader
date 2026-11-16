Shader "Screw/Foreground Bolt"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.68
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ForegroundVertex
            #pragma fragment LitPassFragment
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            Varyings ForegroundVertex(Attributes input)
            {
                Varyings output = LitPassVertex(input);
                // Keep the original projection and shading while putting depth just past the near plane.
                float eyeDepth = max(output.positionCS.w, _ProjectionParams.y);
                float depth = 0.015 * log2(eyeDepth / _ProjectionParams.y);
                output.positionCS.z = lerp(UNITY_NEAR_CLIP_VALUE, UNITY_RAW_FAR_CLIP_VALUE, depth) * output.positionCS.w;
                return output;
            }
            ENDHLSL
        }
    }
}
