// Mer stylisée : déplacement de Gerstner en vertex, normale analytique, écume sur les crêtes.
//
// ⚠ BatoGerstner() ci-dessous est le miroir exact de WaveField.Displacement() et
// WaveField.SampleNormal() en C#. Les deux lisent les mêmes globales (_BatoWave*), poussées
// par WaveField. Si tu touches à la formule ici, touche à l'autre dans la même passe : sinon
// les bateaux flottent sur une surface décalée de celle qu'on voit.
Shader "Bato/Ocean"
{
    Properties
    {
        _DeepColor        ("Couleur des creux", Color)   = (0.02, 0.15, 0.28, 1)
        _ShallowColor     ("Couleur des crêtes", Color)  = (0.10, 0.45, 0.60, 1)
        _FoamColor        ("Couleur de l'écume", Color)  = (0.92, 0.97, 1.00, 1)

        _FoamThreshold    ("Seuil d'écume", Range(0, 1))     = 0.45
        _FoamSoftness     ("Douceur de l'écume", Range(0.01, 0.6)) = 0.18

        _Smoothness       ("Brillance", Range(0.01, 1))     = 0.75
        _SpecularStrength ("Force du spéculaire", Range(0, 4)) = 1.6
        _FresnelPower     ("Puissance du Fresnel", Range(0.5, 8)) = 4.0
        _FresnelStrength  ("Force du Fresnel", Range(0, 1))  = 0.35
        _AmbientBoost     ("Lumière ambiante", Range(0, 1))  = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Déclaré ici et pas dans une passe : le SRP Batcher exige que toutes les passes
        // partagent exactement le même bloc UnityPerMaterial.
        CBUFFER_START(UnityPerMaterial)
            float4 _DeepColor;
            float4 _ShallowColor;
            float4 _FoamColor;
            float  _FoamThreshold;
            float  _FoamSoftness;
            float  _Smoothness;
            float  _SpecularStrength;
            float  _FresnelPower;
            float  _FresnelStrength;
            float  _AmbientBoost;
        CBUFFER_END

        // Globales poussées par WaveField.PushShaderGlobals(). Hors du CBUFFER matériau :
        // ce sont des globales de shader, pas des propriétés par matériau.
        float4 _BatoWaveDirAmp[4];   // xy = direction, z = amplitude, w = longueur d'onde
        float4 _BatoWaveShape[4];    // x  = steepness, y = multiplicateur de vitesse
        int    _BatoWaveCount;
        float  _BatoWaveTime;
        float  _BatoSeaState;

        #define BATO_GRAVITY 9.81

        // Miroir exact de WaveField.Displacement() / SampleNormal().
        void BatoGerstner(float2 gridPosition, out float3 displacement, out float3 normalWS, out float crest)
        {
            displacement = float3(0, 0, 0);
            float nx = 0, ny = 0, nz = 0;

            int count = _BatoWaveCount;

            [loop]
            for (int i = 0; i < count; i++)
            {
                float2 direction  = _BatoWaveDirAmp[i].xy;
                float  amplitude  = _BatoWaveDirAmp[i].z * _BatoSeaState;
                float  wavelength = _BatoWaveDirAmp[i].w;
                float  steepness  = _BatoWaveShape[i].x;
                float  speedMul   = _BatoWaveShape[i].y;

                if (amplitude <= 0.0 || wavelength <= 0.0)
                    continue;

                direction = normalize(direction);

                float k     = 2.0 * PI / wavelength;
                float speed = sqrt(BATO_GRAVITY / k) * speedMul;
                float phase = k * (dot(direction, gridPosition) - speed * _BatoWaveTime);
                float q     = steepness / (k * amplitude * count);

                float c = cos(phase);
                float s = sin(phase);

                displacement.x += q * amplitude * direction.x * c;
                displacement.z += q * amplitude * direction.y * c;
                displacement.y += amplitude * s;

                float wa = k * amplitude;
                nx -= direction.x * wa * c;
                nz -= direction.y * wa * c;
                ny += q * wa * s;
            }

            normalWS = normalize(float3(nx, 1.0 - ny, nz));

            // ny est le facteur de pincement de la surface : maximal exactement là où la crête
            // se resserre, c'est-à-dire là où l'écume apparaît vraiment.
            crest = ny;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 waveData   : TEXCOORD2; // x = hauteur, y = pincement de crête
                float  fogFactor  : TEXCOORD3;
            };

            Varyings Vertex(Attributes IN)
            {
                Varyings OUT;

                // On travaille en monde : la houle ne doit pas dépendre de la transform du mesh.
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                float3 displacement;
                float3 normalWS;
                float  crest;
                BatoGerstner(positionWS.xz, displacement, normalWS, crest);

                positionWS += displacement;

                OUT.positionWS = positionWS;
                OUT.normalWS   = normalWS;
                OUT.waveData   = float2(displacement.y, crest);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);

                return OUT;
            }

            half4 Fragment(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                half3  viewDir  = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Creux sombres, crêtes claires.
                float heightBlend = saturate(IN.waveData.x * 0.5 + 0.5);
                half3 baseColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, heightBlend);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = mainLight.color * (_AmbientBoost + (1.0 - _AmbientBoost) * ndotl);

                half3 color = baseColor * lighting;

                // Spéculaire type Blinn-Phong : c'est lui qui fait scintiller la mer.
                half3 halfVector = normalize(mainLight.direction + viewDir);
                half specular = pow(saturate(dot(normalWS, halfVector)), _Smoothness * 128.0);
                color += mainLight.color * (specular * _SpecularStrength);

                // Fresnel : la mer s'éclaircit en regardant au loin.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                color = lerp(color, _ShallowColor.rgb, fresnel * _FresnelStrength);

                // Écume sur les crêtes pincées.
                half foam = smoothstep(_FoamThreshold, _FoamThreshold + _FoamSoftness, IN.waveData.y);
                color = lerp(color, _FoamColor.rgb, foam);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Sans cette passe, la mer serait absente de la depth texture : tout effet qui la lit
        // (SSAO, brouillard volumétrique, écume de contact) verrait à travers l'eau.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma target 3.0

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVertex(DepthAttributes IN)
            {
                DepthVaryings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                float3 displacement;
                float3 normalWS;
                float  crest;
                BatoGerstner(positionWS.xz, displacement, normalWS, crest);

                OUT.positionCS = TransformWorldToHClip(positionWS + displacement);
                return OUT;
            }

            half4 DepthFragment(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
