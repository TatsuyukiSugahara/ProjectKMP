// カメラと対象の間に入った物を、市松に間引いて透かすためのシェーダー。
// 半透明にすると描画順の問題が出るので、不透明のまま画素を抜く(ディザリング)方式にしている。
Shader "ProjectKMP/Field/DitherFade"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Fade("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Fade;
            CBUFFER_END

            // 4x4 のしきい値。画面の位置ごとに抜くかどうかを変えることで、
            // ざらつきを散らして『薄く見える』状態を作る
            static const float DITHER[16] =
            {
                0.03125, 0.53125, 0.15625, 0.65625,
                0.78125, 0.28125, 0.90625, 0.40625,
                0.21875, 0.71875, 0.09375, 0.59375,
                0.96875, 0.46875, 0.84375, 0.34375
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // 画面上の画素の位置で、抜くかどうかを決める。
                // 物ではなく画面に対して並ぶので、動いてもざらつきが暴れない
                uint x = (uint)input.positionCS.x & 3;
                uint y = (uint)input.positionCS.y & 3;
                clip(_Fade - DITHER[y * 4 + x]);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));

                half3 lighting = mainLight.color * ndotl + SampleSH(normalWS);

                return half4(albedo.rgb * lighting, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
