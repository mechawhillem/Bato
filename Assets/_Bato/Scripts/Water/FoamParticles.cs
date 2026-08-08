using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Fabrique des systèmes de particules d'écume par code.
    ///
    /// Volontairement sans asset : en jam, un prefab de VFX à câbler à la main est une source
    /// d'erreurs et de conflits de merge. Ici on pose un composant, il se construit tout seul.
    /// </summary>
    public static class FoamParticles
    {
        static Material s_Material;

        /// <summary>Matériau partagé par tous les systèmes d'écume, créé une seule fois.</summary>
        public static Material SharedMaterial
        {
            get
            {
                if (s_Material != null) return s_Material;

                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Sprites/Default");

                s_Material = new Material(shader)
                {
                    name = "FoamParticles (généré)",
                    hideFlags = HideFlags.DontSave,
                    mainTexture = CreateSoftDot(64),
                };

                if (s_Material.HasProperty("_BaseMap")) s_Material.SetTexture("_BaseMap", s_Material.mainTexture);
                if (s_Material.HasProperty("_Surface")) s_Material.SetFloat("_Surface", 1f); // transparent

                return s_Material;
            }
        }

        /// <summary>Point flou : une particule carrée se voit tout de suite, un dégradé non.</summary>
        static Texture2D CreateSoftDot(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FoamDot (généré)",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            float centre = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x - centre, y - centre).magnitude / centre;
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha;                       // bord plus doux
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Ajoute et configure un ParticleSystem d'écume. Émission manuelle uniquement :
        /// l'appelant décide quand et où, via Emit().
        /// </summary>
        public static ParticleSystem Create(GameObject host, int maxParticles, float lifetime, float size)
        {
            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startSpeed = 0f;                 // la vitesse est donnée par EmitParams
            main.gravityModifier = 0.9f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = Color.white;

            var emission = system.emission;
            emission.enabled = false;             // uniquement des Emit() explicites

            var shape = system.shape;
            shape.enabled = false;

            // L'écume s'estompe et s'étale en retombant.
            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.75f, 0.35f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.3f));

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = SharedMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
            return system;
        }

        // ------------------------------------- Systèmes fournis à la main

        /// <summary>
        /// Met un ParticleSystem câblé dans l'inspecteur en état d'être piloté par Emit().
        ///
        /// On ne touche à AUCUN de ses réglages visuels : c'est tout l'intérêt d'en fournir un.
        /// On se contente de le démarrer (un système arrêté ne simule pas les particules qu'on lui
        /// donne) et de signaler la seule erreur de config qui rend le résultat incompréhensible.
        /// </summary>
        public static void PrepareForManualEmission(ParticleSystem system, Object owner)
        {
            if (system == null) return;

            if (system.emission.enabled)
            {
                Debug.LogWarning(
                    $"[Bato] Le ParticleSystem '{system.name}' a son module Emission activé : il va " +
                    "cracher des particules tout seul EN PLUS de celles que le jeu lui demande. " +
                    "Décoche Emission (Rate over Time / Bursts) et garde tout le reste.", owner);
            }

            if (!system.isPlaying) system.Play();
        }

        /// <summary>
        /// Convertit une position monde vers l'espace de simulation du système.
        ///
        /// EmitParams.position n'est PAS toujours en monde : elle est exprimée dans l'espace de
        /// simulation. Un système réglé en Local recevrait donc nos positions absolues comme des
        /// offsets locaux, et l'écume partirait à des dizaines de mètres du bateau.
        /// </summary>
        public static Vector3 ToSimulationSpace(ParticleSystem system, Vector3 worldPosition)
        {
            var reference = SimulationReference(system);
            return reference != null ? reference.InverseTransformPoint(worldPosition) : worldPosition;
        }

        /// <summary>Idem pour une vitesse : direction et échelle, sans translation.</summary>
        public static Vector3 VelocityToSimulationSpace(ParticleSystem system, Vector3 worldVelocity)
        {
            var reference = SimulationReference(system);
            return reference != null ? reference.InverseTransformDirection(worldVelocity) : worldVelocity;
        }

        /// <summary>Transform dans lequel le système simule, ou null s'il simule en monde.</summary>
        static Transform SimulationReference(ParticleSystem system)
        {
            if (system == null) return null;

            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World: return null;
                case ParticleSystemSimulationSpace.Custom: return main.customSimulationSpace;
                default: return system.transform;
            }
        }
    }
}
