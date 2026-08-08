using Unity.Netcode;
using UnityEngine;

namespace Bato.Water
{
    /// <summary>
    /// La mer, côté logique.
    ///
    /// Rien de la surface n'est répliqué : la hauteur de l'eau est une fonction pure
    /// hauteur(position, temps). Tous les clients partagent le même <see cref="WaveSettings"/>
    /// (asset du projet) et la même horloge (<see cref="NetworkManager.ServerTime"/>), donc ils
    /// calculent tous exactement la même surface, sans échanger un seul octet par frame.
    ///
    /// Seul l'état de mer (un float, 0 = calme plat, 1 = normal, plus = tempête) passe par une
    /// NetworkVariable : c'est le seul levier qui peut changer en cours de partie, et seul le
    /// serveur peut l'écrire.
    ///
    /// Ce composant pousse aussi les mêmes paramètres dans les globales shader, pour que le GPU
    /// dessine exactement la surface sur laquelle le CPU fait flotter les bateaux.
    /// </summary>
    public class WaveField : NetworkBehaviour
    {
        public static WaveField Instance { get; private set; }

        [SerializeField] WaveSettings m_Settings;

        [Tooltip("État de mer initial imposé par le serveur au démarrage.")]
        [Range(0f, 2f)]
        [SerializeField] float m_InitialSeaState = 1f;

        [Tooltip("Itérations de résolution pour retrouver la crête au-dessus d'un point donné. 3 suffit.")]
        [Range(1, 6)]
        [SerializeField] int m_SolverIterations = 3;

        readonly NetworkVariable<float> m_SeaState = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Copie locale : lisible avant le spawn réseau (écran de connexion, mode éditeur).
        float m_SeaStateLocal = 1f;

        // Tampons réutilisés pour ne rien allouer par frame.
        static readonly int s_WaveDirAmpId = Shader.PropertyToID("_BatoWaveDirAmp");
        static readonly int s_WaveShapeId = Shader.PropertyToID("_BatoWaveShape");
        static readonly int s_WaveCountId = Shader.PropertyToID("_BatoWaveCount");
        static readonly int s_WaveTimeId = Shader.PropertyToID("_BatoWaveTime");
        static readonly int s_SeaStateId = Shader.PropertyToID("_BatoSeaState");
        static readonly int s_WaveHeightId = Shader.PropertyToID("_BatoWaveHeight");

        readonly Vector4[] m_DirAmpBuffer = new Vector4[WaveSettings.MaxWaves];
        readonly Vector4[] m_ShapeBuffer = new Vector4[WaveSettings.MaxWaves];

        public WaveSettings Settings => m_Settings;

        /// <summary>Amplitude globale effective, état de mer réseau inclus.</summary>
        public float SeaState => m_SeaStateLocal;

        /// <summary>
        /// Horloge des vagues. Le temps serveur est partagé par tous les clients ; avant la
        /// connexion (menu, éditeur) on retombe sur le temps local pour que ça bouge quand même.
        /// </summary>
        public float WaveTime
        {
            get
            {
                var manager = NetworkManager.Singleton;
                if (manager != null && manager.IsListening)
                {
                    return manager.ServerTime.TimeAsFloat;
                }
                return Time.time;
            }
        }

        void Awake()
        {
            Instance = this;
            m_SeaStateLocal = m_InitialSeaState;

            if (m_Settings == null)
            {
                Debug.LogError("[Bato] WaveField n'a pas de WaveSettings : la mer sera plate.");
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) m_SeaState.Value = m_InitialSeaState;

            m_SeaStateLocal = m_SeaState.Value;
            m_SeaState.OnValueChanged += OnSeaStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_SeaState.OnValueChanged -= OnSeaStateChanged;
        }

        void OnSeaStateChanged(float _, float current) => m_SeaStateLocal = current;

        /// <summary>Serveur uniquement : calme la mer ou déclenche la tempête, pour tout le monde.</summary>
        public void SetSeaState(float value)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Bato] Seul le serveur peut changer l'état de mer.");
                return;
            }
            m_SeaState.Value = Mathf.Clamp(value, 0f, 2f);
        }

        void LateUpdate()
        {
            PushShaderGlobals();
        }

        /// <summary>
        /// Envoie au GPU exactement les nombres utilisés par le CPU. Ce sont des globales et non
        /// des propriétés de matériau : tout shader qui veut de l'écume ou un reflet cohérent
        /// (coque mouillée, sillage…) lit les mêmes.
        /// </summary>
        void PushShaderGlobals()
        {
            if (m_Settings == null) return;

            int count = m_Settings.WaveCount;
            var waves = m_Settings.Waves;

            for (int i = 0; i < WaveSettings.MaxWaves; i++)
            {
                if (i < count)
                {
                    var wave = waves[i];
                    var direction = wave.Direction.normalized;
                    m_DirAmpBuffer[i] = new Vector4(direction.x, direction.y, wave.Amplitude, wave.Wavelength);
                    m_ShapeBuffer[i] = new Vector4(wave.Steepness, wave.SpeedMultiplier, 0f, 0f);
                }
                else
                {
                    m_DirAmpBuffer[i] = Vector4.zero;
                    m_ShapeBuffer[i] = Vector4.zero;
                }
            }

            Shader.SetGlobalVectorArray(s_WaveDirAmpId, m_DirAmpBuffer);
            Shader.SetGlobalVectorArray(s_WaveShapeId, m_ShapeBuffer);
            float amplitudeScale = m_SeaStateLocal * m_Settings.GlobalAmplitude;

            Shader.SetGlobalInt(s_WaveCountId, count);
            Shader.SetGlobalFloat(s_WaveTimeId, WaveTime);
            Shader.SetGlobalFloat(s_SeaStateId, amplitudeScale);

            // Hauteur totale de la mer. Le shader s'en sert pour deux choses : normaliser ses
            // dégradés, et retrouver le même q que le CPU. C'est donc une valeur du miroir, pas
            // un simple réglage de rendu.
            Shader.SetGlobalFloat(s_WaveHeightId, TotalAmplitudeScaled(amplitudeScale));
        }

        // ------------------------------------------------------- Échantillonnage

        /// <summary>
        /// Somme des amplitudes une fois l'état de mer appliqué. Le plancher évite une division
        /// par zéro dans le calcul de q quand la mer est complètement calmée.
        /// </summary>
        float TotalAmplitudeScaled(float amplitudeScale)
            => Mathf.Max(0.01f, m_Settings.TotalAmplitude * amplitudeScale);

        /// <summary>
        /// Déplacement de Gerstner appliqué au point de grille <paramref name="gridPosition"/>.
        /// Retourne (dx, hauteur, dz).
        ///
        /// ⚠ Cette fonction doit rester le strict miroir de BatoGerstnerDisplacement() dans
        /// Ocean.shader. Si l'une change, l'autre change : sinon les bateaux flottent sur une
        /// surface invisible décalée de la surface affichée.
        /// </summary>
        public Vector3 Displacement(Vector2 gridPosition, float time)
        {
            var result = Vector3.zero;
            if (m_Settings == null) return result;

            int count = m_Settings.WaveCount;
            if (count == 0) return result;

            float amplitudeScale = m_SeaStateLocal * m_Settings.GlobalAmplitude;
            float totalAmplitude = TotalAmplitudeScaled(amplitudeScale);
            var waves = m_Settings.Waves;

            for (int i = 0; i < count; i++)
            {
                var wave = waves[i];
                var direction = wave.Direction.normalized;

                float amplitude = wave.Amplitude * amplitudeScale;
                if (amplitude <= 0f) continue;

                float k = 2f * Mathf.PI / wave.Wavelength;          // nombre d'onde
                float speed = Mathf.Sqrt(WaveSettings.Gravity / k) * wave.SpeedMultiplier;
                float phase = k * (Vector2.Dot(direction, gridPosition) - speed * time);

                // Budget de pincement réparti au prorata de l'amplitude, pas du nombre de vagues.
                // La condition de non-repli est sum(q·k·A) <= 1 : en divisant par l'amplitude
                // TOTALE, chaque vague prend exactement sa part et la houle dominante garde une
                // vraie crête pointue même quand on ajoute du clapot derrière elle.
                float q = wave.Steepness / (k * totalAmplitude);

                float cos = Mathf.Cos(phase);
                float sin = Mathf.Sin(phase);

                result.x += q * amplitude * direction.x * cos;
                result.z += q * amplitude * direction.y * cos;
                result.y += amplitude * sin;
            }

            return result;
        }

        /// <summary>
        /// Hauteur de l'eau à l'aplomb d'un point du monde.
        ///
        /// Gerstner déplace aussi horizontalement : le sommet qui finit au-dessus de (x,z) ne
        /// vient pas de (x,z). On remonte donc à sa position d'origine par quelques itérations
        /// de point fixe, sinon l'erreur atteint facilement un mètre sur les crêtes pincées.
        /// </summary>
        public float SampleHeight(Vector3 worldPosition)
        {
            if (m_Settings == null) return 0f;

            float time = WaveTime;
            var target = new Vector2(worldPosition.x, worldPosition.z);
            return Displacement(SolveGridPosition(target, time), time).y;
        }

        /// <summary>
        /// Retrouve le point de grille dont le déplacement de Gerstner atterrit sur
        /// <paramref name="target"/>. Converge en 2-3 itérations tant que la steepness reste
        /// sous 1 (ce que WaveSettings garantit).
        /// </summary>
        Vector2 SolveGridPosition(Vector2 target, float time)
        {
            var guess = target;
            for (int i = 0; i < m_SolverIterations; i++)
            {
                var displacement = Displacement(guess, time);
                var landed = guess + new Vector2(displacement.x, displacement.z);
                guess += target - landed;
            }
            return guess;
        }

        /// <summary>Normale de la surface, formule analytique (pas de différences finies).</summary>
        public Vector3 SampleNormal(Vector3 worldPosition)
        {
            if (m_Settings == null) return Vector3.up;

            int count = m_Settings.WaveCount;
            if (count == 0) return Vector3.up;

            float time = WaveTime;
            var gridPosition = SolveGridPosition(new Vector2(worldPosition.x, worldPosition.z), time);

            float amplitudeScale = m_SeaStateLocal * m_Settings.GlobalAmplitude;
            float totalAmplitude = TotalAmplitudeScaled(amplitudeScale);
            var waves = m_Settings.Waves;

            float nx = 0f, ny = 0f, nz = 0f;

            for (int i = 0; i < count; i++)
            {
                var wave = waves[i];
                var direction = wave.Direction.normalized;

                float amplitude = wave.Amplitude * amplitudeScale;
                if (amplitude <= 0f) continue;

                float k = 2f * Mathf.PI / wave.Wavelength;
                float speed = Mathf.Sqrt(WaveSettings.Gravity / k) * wave.SpeedMultiplier;
                float phase = k * (Vector2.Dot(direction, gridPosition) - speed * time);
                float q = wave.Steepness / (k * totalAmplitude);

                float wa = k * amplitude;
                nx -= direction.x * wa * Mathf.Cos(phase);
                nz -= direction.y * wa * Mathf.Cos(phase);
                ny += q * wa * Mathf.Sin(phase);
            }

            return new Vector3(nx, 1f - ny, nz).normalized;
        }
    }
}
