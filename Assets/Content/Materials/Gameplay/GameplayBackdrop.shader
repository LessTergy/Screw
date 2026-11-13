Shader "Screw/Gameplay Backdrop"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.639, 0.800, 0.890, 1)
        _BottomColor ("Bottom", Color) = (0.776, 0.863, 0.922, 1)
        _CenterColor ("Center", Color) = (0.776, 0.890, 0.937, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
                half4 _CenterColor;
            CBUFFER_END

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
            };

            Varyings Vert(float4 position : POSITION)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(position);
                output.screenPosition = ComputeScreenPos(output.position);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.screenPosition.xy / input.screenPosition.w;
                half3 gradient = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(uv.y));
                float2 offset = (uv - float2(0.5, 0.46)) * float2(1.6, 2.2);
                half glow = 1.0 - smoothstep(0.0, 1.0, dot(offset, offset));
                return half4(lerp(gradient, _CenterColor.rgb, glow * 0.6), 1.0);
            }
            ENDHLSL
        }
    }
}
