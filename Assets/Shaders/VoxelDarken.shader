// Затемнение блока под прицелом: полупрозрачный чёрный поверх блока.
Shader "Custom/VoxelDarken"
{
    Properties
    {
        _Alpha("Сила затемнения", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off // не пишем глубину, чтобы не ломать прозрачность

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half _Alpha;

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, _Alpha);
            }
            ENDHLSL
        }
    }
}