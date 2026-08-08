using Bato.Water;
using UnityEditor;
using UnityEngine;

namespace Bato.EditorTools
{
    /// <summary>
    /// Anime la mer dans la vue Scène hors mode Play.
    ///
    /// En jeu, c'est WaveField qui pousse les globales shader. Hors Play il ne tourne pas, et la
    /// mer resterait figée : impossible de régler la houle sans lancer une partie. Cet outil
    /// éditeur pousse les mêmes globales depuis le même WaveSettings, avec une horloge locale.
    /// </summary>
    [InitializeOnLoad]
    public static class OceanScenePreview
    {
        const string k_WaveSettingsPath = "Assets/_Bato/WaveSettings.asset";
        const string k_EnabledKey = "Bato.OceanScenePreview.Enabled";

        static readonly int s_WaveDirAmpId = Shader.PropertyToID("_BatoWaveDirAmp");
        static readonly int s_WaveShapeId = Shader.PropertyToID("_BatoWaveShape");
        static readonly int s_WaveCountId = Shader.PropertyToID("_BatoWaveCount");
        static readonly int s_WaveTimeId = Shader.PropertyToID("_BatoWaveTime");
        static readonly int s_SeaStateId = Shader.PropertyToID("_BatoSeaState");

        static readonly Vector4[] s_DirAmpBuffer = new Vector4[WaveSettings.MaxWaves];
        static readonly Vector4[] s_ShapeBuffer = new Vector4[WaveSettings.MaxWaves];

        static WaveSettings s_Settings;

        static OceanScenePreview()
        {
            EditorApplication.update += Tick;
        }

        static bool Enabled
        {
            get => EditorPrefs.GetBool(k_EnabledKey, true);
            set => EditorPrefs.SetBool(k_EnabledKey, value);
        }

        [MenuItem("Bato/Aperçu des vagues dans la vue Scène", priority = 20)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem("Bato/Aperçu des vagues dans la vue Scène", validate = true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked("Bato/Aperçu des vagues dans la vue Scène", Enabled);
            return true;
        }

        static void Tick()
        {
            // En Play, WaveField est la seule source : on ne veut surtout pas deux écrivains.
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Enabled) return;

            if (s_Settings == null)
            {
                s_Settings = AssetDatabase.LoadAssetAtPath<WaveSettings>(k_WaveSettingsPath);
                if (s_Settings == null) return;
            }

            int count = s_Settings.WaveCount;
            var waves = s_Settings.Waves;

            for (int i = 0; i < WaveSettings.MaxWaves; i++)
            {
                if (i < count)
                {
                    var wave = waves[i];
                    var direction = wave.Direction.normalized;
                    s_DirAmpBuffer[i] = new Vector4(direction.x, direction.y, wave.Amplitude, wave.Wavelength);
                    s_ShapeBuffer[i] = new Vector4(wave.Steepness, wave.SpeedMultiplier, 0f, 0f);
                }
                else
                {
                    s_DirAmpBuffer[i] = Vector4.zero;
                    s_ShapeBuffer[i] = Vector4.zero;
                }
            }

            Shader.SetGlobalVectorArray(s_WaveDirAmpId, s_DirAmpBuffer);
            Shader.SetGlobalVectorArray(s_WaveShapeId, s_ShapeBuffer);
            Shader.SetGlobalInt(s_WaveCountId, count);
            Shader.SetGlobalFloat(s_WaveTimeId, (float)EditorApplication.timeSinceStartup);
            Shader.SetGlobalFloat(s_SeaStateId, s_Settings.GlobalAmplitude);

            SceneView.RepaintAll();
        }
    }
}
