/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

Shader "Unlit/BlendMode/Blit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [PerRendererData] _ReversedZ("_Reversed_Z", Int) = 0
    }
    SubShader
    {
        Blend   One OneMinusSrcAlpha, One OneMinusSrcAlpha

        Tags {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite   Off
        ZTest [_ReversedZ]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            sampler2D _MainTex;
            float4 _MainTex_ST;
            CBUFFER_END

            float4 _MainTex_TexelSize;
            float _LuoEdgeBlurEnabled;
            float _LuoEdgeBlurStrength;
            float4 _LuoEdgeWrapColor;
            float _LuoEdgeWrapStrength;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Setting vertex position
                OUT.vertex = IN.vertex;

                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;

                // If reversed Z is enabled, flip the Y coordinate.
                #if UNITY_REVERSED_Z
                OUT.vertex.y = -OUT.vertex.y;
                #endif

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 center = tex2D(_MainTex, IN.texcoord);
                float enabled = step(0.5, _LuoEdgeBlurEnabled);
                float strength = saturate(_LuoEdgeBlurStrength) * enabled;
                if (strength <= 0.0001)
                    return center * IN.color;

                // The Cubism common buffer is already premultiplied. Blur the complete
                // character once, and only mix the result where neighbouring alpha
                // changes; opaque inner artwork therefore stays sharp.
                float radius = max(0.5, strength * 6.0);
                float2 dx = float2(_MainTex_TexelSize.x * radius, 0.0);
                float2 dy = float2(0.0, _MainTex_TexelSize.y * radius);
                float2 diagonal = float2(dx.x, dy.y) * 0.70710678;

                half4 left = tex2D(_MainTex, IN.texcoord - dx);
                half4 right = tex2D(_MainTex, IN.texcoord + dx);
                half4 down = tex2D(_MainTex, IN.texcoord - dy);
                half4 up = tex2D(_MainTex, IN.texcoord + dy);
                half4 downLeft = tex2D(_MainTex, IN.texcoord - diagonal);
                half4 upRight = tex2D(_MainTex, IN.texcoord + diagonal);
                half4 upLeft = tex2D(_MainTex, IN.texcoord + float2(-diagonal.x, diagonal.y));
                half4 downRight = tex2D(_MainTex, IN.texcoord + float2(diagonal.x, -diagonal.y));

                half4 blurred = center * 0.24h +
                                (left + right + down + up) * 0.12h +
                                (downLeft + upRight + upLeft + downRight) * 0.07h;
                half minimumAlpha = min(center.a, min(min(left.a, right.a), min(down.a, up.a)));
                half maximumAlpha = max(center.a, max(max(left.a, right.a), max(down.a, up.a)));
                half edge = saturate((maximumAlpha - minimumAlpha) * 3.0h);
                half4 result = lerp(center, blurred, edge * strength);

                // Use the sampled camera environment mainly at the inner silhouette.
                // Multiplying by alpha keeps the premultiplied buffer valid and prevents halos.
                half innerEdge = edge * saturate(center.a * 4.0h);
                half wrapAmount = innerEdge * saturate(_LuoEdgeWrapStrength);
                half3 wrappedColor = _LuoEdgeWrapColor.rgb * result.a;
                result.rgb = lerp(result.rgb, wrappedColor, wrapAmount);
                return result * IN.color;
            }
            ENDHLSL
        }
    }
}
