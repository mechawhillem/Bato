// Masque de découpe de l'eau : la « différence booléenne » entre la mer et le volume intérieur
// d'une coque, faite par pixel dans le stencil plutôt que sur la géométrie.
//
// Ce shader ne dessine RIEN (ColorMask 0, ZWrite Off). Il se contente de marquer les pixels
// couverts par le volume du masque avec la valeur de stencil 1. Ocean.shader teste ce stencil
// et jette ses fragments là où il vaut 1 : plus une goutte d'eau dessinée à l'intérieur du
// bateau, quelle que soit la profondeur d'enfoncement de la coque.
//
// Le mesh à utiliser n'est PAS la coque visible : c'est un volume fermé simple qui remplit
// l'intérieur du bateau (un cube étiré suffit). Utiliser la coque entière découperait aussi les
// vagues censées passer DEVANT le bateau.
//
// Cull Front : on marque les faces arrière du volume, donc toute sa silhouette.
Shader "Bato/WaterMask"
{
    SubShader
    {
        // Avant la mer (Geometry = 2000) : le stencil doit être posé quand l'eau se teste.
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry-100"
        }

        Pass
        {
            Name "WaterMask"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest Always
            ColorMask 0

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vertex(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 Fragment(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
