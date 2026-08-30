Shader "UmaDesktopPet/AppOwnedPropOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.035, 0.114, 0.133, 1)
        _OutlineWidth ("Outline Width Pixels", Range(0, 4)) = 1.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+1"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            half4 _OutlineColor;
            half _OutlineWidth;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 direction = mul(
                    (float3x3)UNITY_MATRIX_VP,
                    normalWS).xy;
                float lengthSquared = dot(direction, direction);
                direction = lengthSquared > 0.000001
                    ? direction * rsqrt(lengthSquared)
                    : float2(0.0, 1.0);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionHCS.xy += direction *
                    (_OutlineWidth * 2.0 / _ScreenParams.xy) *
                    output.positionHCS.w;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata input)
            {
                v2f output;
                float3 normalWS = UnityObjectToWorldNormal(input.normal);
                float2 direction = mul(
                    (float3x3)UNITY_MATRIX_VP,
                    normalWS).xy;
                float lengthSquared = dot(direction, direction);
                direction = lengthSquared > 0.000001
                    ? direction * rsqrt(lengthSquared)
                    : float2(0.0, 1.0);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.vertex.xy += direction *
                    (_OutlineWidth * 2.0 / _ScreenParams.xy) *
                    output.vertex.w;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }

    Fallback Off
}
