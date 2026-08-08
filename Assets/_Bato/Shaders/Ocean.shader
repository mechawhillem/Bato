// Mer stylisée façon Sea of Thieves : déplacement de Gerstner en vertex, normale analytique,
// dégradé de profondeur, diffusion sous-surface sur les crêtes et écume pilotée par le pincement.
//
// Ce qui fait le look de Rare, dans l'ordre d'importance :
//   1. la GÉOMÉTRIE — des crêtes réellement pointues, pas des dunes (voir WaveSettings) ;
//   2. la diffusion sous-surface — une crête vue à contre-jour devient vert-turquoise lumineux,
//      c'est la signature visuelle du jeu ;
//   3. un dégradé creux/crête très contrasté et très saturé ;
//   4. de l'écume blanche, UNIQUEMENT là où la crête se pince.
//
// Volontairement PAS de cel-shading : la mer de SoT est lissée, son côté « peint » vient de la
// palette et du contre-jour, pas de bandes d'éclairage.
//
// ⚠ BatoGerstner() ci-dessous est le miroir exact de WaveField.Displacement() et
// WaveField.SampleNormal() en C#. Les deux lisent les mêmes globales (_BatoWave*), poussées
// par WaveField. Si tu touches à la formule ici, touche à l'autre dans la même passe : sinon
// les bateaux flottent sur une surface décalée de celle qu'on voit.
Shader "Bato/Ocean"
{
    Properties
    {
        [Header(Couleurs)]
        _DeepColor        ("Creux", Color)                   = (0.004, 0.105, 0.18, 1)
        _ShallowColor     ("Crêtes", Color)                  = (0.05, 0.42, 0.48, 1)
        _HorizonColor     ("Horizon", Color)                 = (0.32, 0.66, 0.75, 1)

        [Header(Contre jour)]
        _SubsurfaceColor  ("Couleur de transparence", Color) = (0.12, 0.78, 0.62, 1)
        _SubsurfaceStrength ("Force de transparence", Range(0, 4)) = 1.9
        _SubsurfacePower  ("Concentration du contre-jour", Range(1, 8)) = 3.5

        [Header(Ecume)]
        _FoamColor        ("Couleur de l'écume", Color)      = (0.92, 0.97, 0.98, 1)
        _FoamStrength     ("Force de l'écume", Range(0, 1))  = 0.6
        _FoamThreshold    ("Seuil de l'écume", Range(0, 1))  = 0.5

        [Header(Lumiere)]
        _AmbientBoost     ("Lumière ambiante", Range(0, 1))  = 0.45
        _Smoothness       ("Brillance", Range(0.01, 1))      = 0.75
        _SpecularStrength ("Force du spéculaire", Range(0, 4)) = 1.6
        _FresnelPower     ("Puissance du Fresnel", Range(0.5, 8)) = 5
        _FresnelStrength  ("Force du Fresnel", Range(0, 1))  = 0.5
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
            float4 _HorizonColor;
            float4 _SubsurfaceColor;
            float4 _FoamColor;
            float  _SubsurfaceStrength;
            float  _SubsurfacePower;
            float  _FoamStrength;
            float  _FoamThreshold;
            float  _AmbientBoost;
            float  _Smoothness;
            float  _SpecularStrength;
            float  _FresnelPower;
            float  _FresnelStrength;
        CBUFFER_END

        // Globales poussées par WaveField.PushShaderGlobals(). Hors du CBUFFER matériau :
        // ce sont des globales de shader, pas des propriétés par matériau.
        // Taille fixée à WaveSettings.MaxWaves : si tu changes l'un, change l'autre.
        float4 _BatoWaveDirAmp[5];   // xy = direction, z = amplitude, w = longueur d'onde
        float4 _BatoWaveShape[5];    // x  = steepness, y = multiplicateur de vitesse
        int    _BatoWaveCount;
        float  _BatoWaveTime;
        float  _BatoSeaState;
        float  _BatoWaveHeight;      // somme des amplitudes, état de mer inclus

        #define BATO_GRAVITY 9.81

        // Miroir exact de WaveField.Displacement() / SampleNormal().
        void BatoGerstner(float2 gridPosition, out float3 displacement, out float3 normalWS, out float pinch)
        {
            displacement = float3(0, 0, 0);
            float nx = 0, ny = 0, nz = 0;

            int count = _BatoWaveCount;
            float totalAmplitude = max(0.01, _BatoWaveHeight);

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

                // Budget de pincement au prorata de l'amplitude — voir WaveField.Displacement().
                float q     = steepness / (k * totalAmplitude);

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

            // ny mesure la compression horizontale de la surface : il vaut 0 au repos et tend
            // vers 1 quand la crête est sur le point de se replier. C'est exactement le critère
            // physique de déferlement, donc le bon pilote pour l'écume — et il est nul partout
            // ailleurs que sur une crête, ce qu'un bruit ne peut pas garantir.
            pinch = ny;
        }

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Découpe par les volumes de coque (voir WaterMask.shader) : on ne dessine pas
            // d'eau là où un masque a posé un 1 dans le stencil.
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

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
                float  pinch;
                BatoGerstner(positionWS.xz, displacement, normalWS, pinch);

                positionWS += displacement;

                OUT.positionWS = positionWS;
                OUT.normalWS   = normalWS;
                OUT.waveData   = float2(displacement.y, pinch);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);

                return OUT;
            }

            half4 Fragment(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                half3  viewDir  = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Hauteur normalisée par la hauteur réelle de la mer : les couleurs restent en
                // place quand on change l'amplitude des vagues. -1 dans les creux, +1 aux crêtes.
                half height = IN.waveData.x / _BatoWaveHeight;
                half pinch  = saturate(IN.waveData.y);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));

                // 1. Dégradé de profondeur. Tout le contraste de la mer vient de là.
                half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(height * 0.5 + 0.5));

                // 2. Diffus lissé, avec un plancher ambiant élevé : l'eau ne devient jamais noire.
                half3 lighting = mainLight.color * (_AmbientBoost + (1.0 - _AmbientBoost) * ndotl);
                color *= lighting;

                // 3. Contre-jour. Une crête est mince, donc la lumière la traverse : c'est le vert
                //    lumineux qu'on voit dans SoT quand une vague passe entre le soleil et la
                //    caméra. Conditionné à la hauteur, pour que ça ne bave pas dans les creux.
                half backLight = saturate(dot(viewDir, -mainLight.direction));
                half thinness  = saturate(height * 1.4 - 0.15);
                half subsurface = pow(backLight, _SubsurfacePower) * thinness;
                color += _SubsurfaceColor.rgb * mainLight.color * (subsurface * _SubsurfaceStrength);

                // 4. Fresnel : la mer vire vers la couleur d'horizon quand on la regarde de loin.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                color = lerp(color, _HorizonColor.rgb, saturate(fresnel * _FresnelStrength));

                // 5. Éclat du soleil sur l'eau.
                half3 halfVector = normalize(mainLight.direction + viewDir);
                half specular = pow(saturate(dot(normalWS, halfVector)), _Smoothness * 250.0);
                color += mainLight.color * (specular * _SpecularStrength);

                // 6. Écume, seulement là où la vague se pince assez pour déferler. Aucun bruit :
                //    la répartition vient de la géométrie, donc elle suit les crêtes au lieu de
                //    tacheter la surface. _FoamStrength à 0 la retire complètement.
                half foam = smoothstep(_FoamThreshold, 1.0, pinch);
                color = lerp(color, _FoamColor.rgb * lighting, foam * _FoamStrength);

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
                float  pinch;
                BatoGerstner(positionWS.xz, displacement, normalWS, pinch);

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
