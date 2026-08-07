Shader "ProjectKMP/Field/GrassBlade"
{
    // 草を GPU で揺らすためのシェーダー。頂点を風で動かし、根元から先端への色の変化も付ける。
    // 揺れの重みには UV の縦方向(0=根元 1=先端)を使うので、GrassChunk が張る UV が前提。
    Properties
    {
        _BaseColor("先端の色", Color) = (0.45, 0.75, 0.25, 1)
        _RootColor("根元の色", Color) = (0.16, 0.36, 0.12, 1)

        _WindDirection("風の向き(XZ)", Vector) = (1, 0, 0.4, 0)
        _WindSpeed("風の速さ", Float) = 1.6
        _WindStrength("先端が動く量(m)", Float) = 0.07
        _WindWaveLength("風の波の長さ(m)", Float) = 5
        _GustStrength("突風の強さ(倍率)", Float) = 0.6
        _GustSpeed("突風の速さ", Float) = 0.3
        _FlutterStrength("細かい揺れの強さ(倍率)", Float) = 0.25
        _AmbientBoost("明るさの底上げ", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // 草は板1枚なので裏からも見えるようにする
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #define GRASS_TWO_PI 6.28318530718

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RootColor;
            float4 _WindDirection;
            float _WindSpeed;
            float _WindStrength;
            float _WindWaveLength;
            float _GustStrength;
            float _GustSpeed;
            float _FlutterStrength;
            float _AmbientBoost;
        CBUFFER_END

        // ワールド座標を風で揺らす。heightRatio は 0(根元)〜1(先端)
        float3 ApplyWind(float3 positionWS, float heightRatio)
        {
            float2 direction = normalize(float2(_WindDirection.x, _WindDirection.z) + float2(0.0001, 0.0001));
            float2 side = float2(-direction.y, direction.x);

            // 風の位相は場所によってずらす。全体が同時に揺れると波打って見えない
            float wave = GRASS_TWO_PI * dot(positionWS.xz, direction) / max(0.01, _WindWaveLength);
            float phase = wave - _Time.y * _WindSpeed;

            float sway = sin(phase);
            float gust = sin(phase * 0.22 - _Time.y * _GustSpeed) * _GustStrength;
            float flutter = sin(phase * 2.7 + positionWS.x * 3.1) * _FlutterStrength;

            // 根元は動かさず、先端ほど大きく動かす
            float weight = heightRatio * heightRatio;
            float2 offset = direction * ((sway + gust) * _WindStrength * weight)
                          + side * (flutter * _WindStrength * weight);

            positionWS.xz += offset;

            // 横へ流れたぶんだけ少し沈めないと、草が伸びたように見えてしまう
            positionWS.y -= length(offset) * 0.25;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyWind(positionWS, saturate(input.uv.y));

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                // 根元を暗くすると、草が重なって生えているように見える
                half3 albedo = lerp(_RootColor.rgb, _BaseColor.rgb, saturate(input.uv.y));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS) + _AmbientBoost;
                half3 lighting = ambient + mainLight.color * (ndotl * mainLight.shadowAttenuation);

                return half4(albedo * lighting, 1);
            }
            ENDHLSL
        }

        Pass
        {
            // 深度だけを描くパス。風で動いたあとの形で深度を書かないと他の効果とズレる
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS = ApplyWind(positionWS, saturate(input.uv.y));
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
