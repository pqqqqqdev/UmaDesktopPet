Shader "UmaDesktopPet/AppOwnedProp"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _FaceShading ("Face Shading", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite On
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
                half3 normalWS : TEXCOORD0;
            };

            half4 _Color;
            half _FaceShading;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 lightDirection = normalize(half3(-0.35h, 0.80h, 0.45h));
                half3 fillDirection = normalize(half3(0.50h, 0.45h, -0.30h));
                half key = smoothstep(
                    0.08h,
                    0.92h,
                    saturate(dot(normalWS, lightDirection)));
                half fill = saturate(dot(normalWS, fillDirection));
                half upward = saturate(normalWS.y * 0.5h + 0.5h);
                half shade = (0.58h + 0.38h * key + 0.10h * fill) *
                    lerp(0.82h, 1.0h, upward);
                half appliedShade = lerp(
                    1.0h,
                    shade,
                    saturate(_FaceShading));
                return half4(_Color.rgb * appliedShade, _Color.a);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Back
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed _FaceShading;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed3 normalWS : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.normalWS = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed3 normalWS = normalize(input.normalWS);
                fixed3 lightDirection = normalize(fixed3(-0.35, 0.80, 0.45));
                fixed3 fillDirection = normalize(fixed3(0.50, 0.45, -0.30));
                fixed key = smoothstep(
                    0.08,
                    0.92,
                    saturate(dot(normalWS, lightDirection)));
                fixed fill = saturate(dot(normalWS, fillDirection));
                fixed upward = saturate(normalWS.y * 0.5 + 0.5);
                fixed shade = (0.58 + 0.38 * key + 0.10 * fill) *
                    lerp(0.82, 1.0, upward);
                fixed appliedShade = lerp(
                    1.0,
                    shade,
                    saturate(_FaceShading));
                return fixed4(_Color.rgb * appliedShade, _Color.a);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
