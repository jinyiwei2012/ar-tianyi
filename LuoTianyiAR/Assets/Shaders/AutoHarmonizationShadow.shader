Shader "LuoTianyiAR/SoftShadow"
{
    Properties
    {
        _BaseColor("Shadow Color", Color) = (0.018, 0.020, 0.028, 0.25)
        _Softness("Softness", Range(0.0, 1.0)) = 0.62
        _ShadowMask("Character Shadow Mask", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SoftShadow"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Softness;
            CBUFFER_END
            TEXTURE2D(_ShadowMask);
            SAMPLER(sampler_ShadowMask);
            float4 _ShadowMask_TexelSize;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half hardness = 1.0h - _Softness;
                // 最软端覆盖源遮罩约 50 px 半径；5x5 二项式权重避免大半径稀疏采样
                // 产生多个重影轮廓。最硬端仍保留约 1 px 的抗锯齿过渡。
                float radiusPixels = lerp(50.0, 1.0, hardness);
                float2 blurStep = _ShadowMask_TexelSize.xy * radiusPixels * 0.5;
                half alpha = 0.0h;
                const half weights[5] = { 1.0h, 4.0h, 6.0h, 4.0h, 1.0h };
                UNITY_UNROLL
                for (int y = -2; y <= 2; y++)
                {
                    UNITY_UNROLL
                    for (int x = -2; x <= 2; x++)
                    {
                        half weight = weights[x + 2] * weights[y + 2];
                        alpha += SAMPLE_TEXTURE2D(
                            _ShadowMask,
                            sampler_ShadowMask,
                            input.uv + float2(x, y) * blurStep).a * weight;
                    }
                }
                alpha *= 0.00390625h;
                alpha *= _BaseColor.a;
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
