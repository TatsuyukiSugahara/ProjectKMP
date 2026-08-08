Shader "ProjectKMP/Field/SkyDayNight"
{
    Properties
    {
        [NoScaleOffset] _DayCube ("昼の空(キューブマップ)", Cube) = "grey" {}
        _UseCubemap ("キューブマップを使う(0=グラデ空 1=キューブマップ)", Range(0,1)) = 0
        _Rotation ("空の回転(度)", Range(0,360)) = 0

        [Header(Day)]
        _DayZenith ("昼:天頂の色", Color) = (0.10,0.38,0.88,1)
        _DayHorizon ("昼:地平の色", Color) = (0.62,0.83,0.97,1)
        _DayGround ("昼:地平より下の色", Color) = (0.38,0.50,0.36,1)
        _DayTint ("昼:全体の色味", Color) = (1,1,1,1)
        _DayExposure ("昼:明るさ", Range(0,8)) = 1.1
        _HorizonSharpness ("地平のぼかしの鋭さ", Range(0.2,8)) = 3.5

        [Header(Cloud)]
        _CloudColor ("雲の明るい面の色", Color) = (1,1,1,1)
        _CloudShadowColor ("雲の影の色", Color) = (0.70,0.78,0.90,1)
        _CloudCoverage ("雲の量", Range(0,1)) = 0.45
        _CloudSoftness ("雲のふちのやわらかさ", Range(0.01,0.6)) = 0.20
        _CloudScale ("雲の細かさ", Range(0.1,8)) = 1.3
        _CloudSpeed ("雲が流れる速さ", Range(0,0.5)) = 0.012
        _CloudWind ("雲が流れる向き(XZ)", Vector) = (1,0.35,0,0)
        _CloudHeightCurve ("雲の層の高さ(小さいほど地平まで伸びる)", Range(0.05,1.5)) = 0.35
        _CloudOpacity ("雲の濃さ", Range(0,1)) = 1

        [Header(Night)]
        _NightZenith ("夜:天頂の色", Color) = (0.010,0.020,0.055,1)
        _NightHorizon ("夜:地平の色", Color) = (0.045,0.075,0.150,1)
        _NightTint ("夜:雲に残す色味", Color) = (0.30,0.42,0.78,1)
        _NightExposure ("夜:明るさ", Range(0,4)) = 0.35
        _NightCloudKeep ("夜:昼の雲を残す量", Range(0,1)) = 0.35
        _NightBlend ("夜への遷移(0=昼 1=夜)", Range(0,1)) = 0

        [Header(Sun)]
        _SunDirection ("太陽の向き(スクリプトが更新)", Vector) = (0.32,0.62,0.72,0)
        [HDR]_SunColor ("太陽の色", Color) = (1,0.96,0.86,1)
        _SunSize ("太陽の大きさ", Range(0,0.05)) = 0.008
        _SunHalo ("太陽まわりのにじみ", Range(0,2)) = 0.4
        _SunIntensity ("太陽の強さ(0で消す)", Range(0,10)) = 1.2

        [Header(Star)]
        [HDR]_StarColor ("星の色", Color) = (0.85,0.92,1,1)
        _StarDensity ("星の細かさ", Range(20,400)) = 240
        _StarAmount ("星の量", Range(0,1)) = 0.45
        _StarIntensity ("星の強さ", Range(0,8)) = 3.2
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_DayCube);
            SAMPLER(sampler_DayCube);

            CBUFFER_START(UnityPerMaterial)
                float _UseCubemap;
                float _Rotation;
                half4 _DayZenith;
                half4 _DayHorizon;
                half4 _DayGround;
                half4 _DayTint;
                half _DayExposure;
                half _HorizonSharpness;
                half4 _CloudColor;
                half4 _CloudShadowColor;
                half _CloudCoverage;
                half _CloudSoftness;
                half _CloudScale;
                half _CloudSpeed;
                float4 _CloudWind;
                half _CloudHeightCurve;
                half _CloudOpacity;
                half4 _NightZenith;
                half4 _NightHorizon;
                half4 _NightTint;
                half _NightExposure;
                half _NightCloudKeep;
                half _NightBlend;
                float4 _SunDirection;
                half4 _SunColor;
                half _SunSize;
                half _SunHalo;
                half _SunIntensity;
                half4 _StarColor;
                half _StarDensity;
                half _StarAmount;
                half _StarIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS : TEXCOORD0;
            };

            // Y軸まわりに空を回す
            float3 RotateAroundY(float3 v, float degrees)
            {
                float rad = radians(degrees);
                float s = sin(rad);
                float c = cos(rad);
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            // マス目の四隅の乱数をなめらかにつないだ、もやもやの元
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 細かさを変えたノイズを4枚重ねて、雲らしいむらを作る
            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * ValueNoise(p);
                    p = p * 2.03 + 17.7;
                    amplitude *= 0.5;
                }
                return value;
            }

            // マス目ごとに1個だけ星を置き、方向との距離で点にする
            half Stars(float3 dir)
            {
                float density = max(1.0, _StarDensity);
                float3 cell = floor(dir * density);
                float h = Hash31(cell);

                // 量が少ないほどマス目に星が入る確率を下げる
                float present = step(1.0 - _StarAmount * 0.08, h);
                if (present <= 0.0) return 0.0;

                float3 jitter = float3(Hash31(cell + 1.7), Hash31(cell + 3.1), Hash31(cell + 5.3)) - 0.5;
                float3 center = normalize((cell + 0.5 + jitter * 0.5) / density);

                float d = distance(center, dir);
                float dot01 = 1.0 - smoothstep(0.0, 0.45 / density, d);

                // ちらつき。時間で明るさを揺らす
                float twinkle = 0.55 + 0.45 * sin(_Time.y * 2.5 + h * 62.8);
                return dot01 * twinkle;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirWS = RotateAroundY(IN.positionOS.xyz, _Rotation);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dirWS);
                float up = dir.y;

                // 地平から天頂へのグラデーション
                float t = pow(saturate(up), 1.0 / max(0.2, _HorizonSharpness));
                half3 gradient = up >= 0.0
                    ? lerp(_DayHorizon.rgb, _DayZenith.rgb, t)
                    : lerp(_DayHorizon.rgb, _DayGround.rgb, saturate(-up * 3.0));

                half3 cube = SAMPLE_TEXTURECUBE_LOD(_DayCube, sampler_DayCube, dir, 0).rgb;
                half3 day = lerp(gradient, cube, saturate(_UseCubemap)) * _DayTint.rgb * _DayExposure;

                float3 sunDir = normalize(_SunDirection.xyz + float3(0.0, 0.0001, 0.0));

                // 雲。空を見上げる方向をひとつの平面に投影して、その上をノイズが流れる。
                // キューブマップの空には雲が描かれているので、そのときは出さない
                float denom = max(up, 0.02) + _CloudHeightCurve;
                float2 cloudUV = dir.xz / denom * _CloudScale;
                cloudUV += _Time.y * _CloudSpeed * normalize(_CloudWind.xy + float2(0.0001, 0.0001));

                // 座標そのものを別のノイズで歪めると、筋ではなくもこもこした固まりになる
                float warp = ValueNoise(cloudUV * 0.6);
                float noise = Fbm(cloudUV + warp * 0.9);
                float threshold = 1.0 - _CloudCoverage;
                float edge = max(0.001, _CloudSoftness);

                // 地平線ぎわは雲を消す(投影が引き伸ばされて筋になるため)
                float horizonFade = smoothstep(0.02, 0.28, up);
                float cloud = smoothstep(threshold, threshold + edge, noise)
                    * horizonFade * _CloudOpacity * (1.0 - saturate(_UseCubemap));

                // 厚いところほど白く、薄いところは影の色にして立体感を出す
                half3 cloudColor = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, saturate((noise - threshold) / (edge * 2.0)));
                cloudColor += _SunColor.rgb * pow(saturate(dot(dir, sunDir)), 16.0) * 0.5;

                day = lerp(day, cloudColor, cloud);

                // 太陽。雲に隠れる。夜へ向かうほど弱める
                float sunDot = saturate(dot(dir, sunDir));
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.25, sunDot);
                float halo = pow(sunDot, 48.0) * _SunHalo;
                day += _SunColor.rgb * (disc + halo) * _SunIntensity * (1.0 - _NightBlend) * (1.0 - cloud * 0.85);

                // 夜。昼の絵の明暗だけ薄く残すと雲の形が夜空にも残る
                half lum = dot(day, half3(0.299, 0.587, 0.114));
                half3 nightGradient = up >= 0.0
                    ? lerp(_NightHorizon.rgb, _NightZenith.rgb, t)
                    : _NightHorizon.rgb * 0.5;

                half3 night = nightGradient + lum * _NightCloudKeep * _NightTint.rgb;
                night *= _NightExposure;

                // 星は雲に隠れる
                night += Stars(dir) * _StarColor.rgb * _StarIntensity * saturate(up * 2.0 + 0.05) * (1.0 - cloud);

                half3 color = lerp(day, night, saturate(_NightBlend));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
