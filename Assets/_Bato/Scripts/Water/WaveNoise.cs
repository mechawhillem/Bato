using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// Bruit ajouté à la houle, pour casser la régularité des trains de Gerstner.
    ///
    /// Il ne déforme QUE la hauteur, jamais le déplacement horizontal : c'est ce qui permet à
    /// <see cref="WaveField"/> de continuer à inverser Gerstner par point fixe sans que le bruit
    /// entre dans l'équation.
    ///
    /// ⚠ Miroir exact de BatoNoise* dans Ocean.shader. Le hash est en entiers 32 bits non signés,
    /// exact des deux côtés : une version à base de sin() dériverait entre le CPU et le GPU, et le
    /// bateau flotterait sur une surface légèrement différente de celle qu'on voit.
    /// </summary>
    public static class WaveNoise
    {
        /// <summary>Écart, en mètres, utilisé pour la pente par différences finies.</summary>
        public const float GradientEpsilon = 0.5f;

        static uint Hash(int x, int y)
        {
            unchecked
            {
                uint n = (uint)(x * 374761393 + y * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return n ^ (n >> 16);
            }
        }

        static float Hash01(int x, int y) => (Hash(x, y) & 0xFFFFFFu) / 16777216f;

        /// <summary>Bruit de valeur bilinéaire, lissé en smoothstep. Renvoie 0 à 1.</summary>
        static float ValueNoise(float x, float y)
        {
            float fx = Mathf.Floor(x);
            float fy = Mathf.Floor(y);
            int ix = (int)fx;
            int iy = (int)fy;

            float tx = x - fx;
            float ty = y - fy;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);

            float a = Hash01(ix, iy);
            float b = Hash01(ix + 1, iy);
            float c = Hash01(ix, iy + 1);
            float d = Hash01(ix + 1, iy + 1);

            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>
        /// Deux octaves qui dérivent dans des directions différentes : sans ça le motif glisse en
        /// bloc et se lit comme une texture qu'on fait défiler.
        /// </summary>
        public static float Sample(float x, float z, float scale, float time)
        {
            float sx = x * scale;
            float sz = z * scale;

            float n = ValueNoise(sx + time * 0.35f, sz - time * 0.35f) * 0.65f;
            n += ValueNoise(sx * 2.17f - time * 0.8f, sz * 2.17f + time * 0.56f) * 0.35f;

            return n * 2f - 1f;      // ramené dans [-1, 1]
        }

        /// <summary>
        /// Pente du bruit par différences finies, pour que l'éclairage suive les bosses au lieu de
        /// les ignorer. Renvoie (dHauteur/dx, dHauteur/dz) pour une amplitude de 1.
        /// </summary>
        public static Vector2 Gradient(float x, float z, float scale, float time)
        {
            const float e = GradientEpsilon;

            float dx = Sample(x + e, z, scale, time) - Sample(x - e, z, scale, time);
            float dz = Sample(x, z + e, scale, time) - Sample(x, z - e, scale, time);

            return new Vector2(dx, dz) / (2f * e);
        }
    }
}
