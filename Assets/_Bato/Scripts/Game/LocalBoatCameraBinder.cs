using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bato
{
    /// <summary>
    /// Accroche la caméra locale au bateau que ce client possède.
    ///
    /// La caméra n'est PAS un objet réseau et ne doit pas l'être : chaque joueur regarde sa propre
    /// partie. Si elle vit dans le prefab joueur, chaque bateau spawné en amène une — donc autant
    /// de Camera et d'AudioListener que de joueurs, ce qu'Unity ne pardonne pas.
    ///
    /// Le bateau, lui, est bien réseau. Ce composant est le pont entre les deux : une seule caméra
    /// posée dans la scène, qui va chercher le bateau local dès qu'il apparaît.
    ///
    /// À poser sur le même GameObject que BoatCameraController (le prefab « Player Camera »).
    /// </summary>
    public class LocalBoatCameraBinder : MonoBehaviour
    {
        [SerializeField] Features.Camera.BoatCameraController m_Camera;

        [Tooltip("Optionnel. Sert à donner au bateau le même schéma de contrôle qu'à la caméra.")]
        [SerializeField] Features.Input.InputDeviceModeToggle m_InputMode;

        NetworkObject m_BoundPlayer;

        void Awake()
        {
            if (m_Camera == null) m_Camera = GetComponent<Features.Camera.BoatCameraController>();
            if (m_InputMode == null) m_InputMode = GetComponent<Features.Input.InputDeviceModeToggle>();

            if (m_Camera == null)
            {
                Debug.LogError("[Bato] LocalBoatCameraBinder ne trouve pas de BoatCameraController.", this);
                enabled = false;
            }
        }

        void Update()
        {
            var manager = NetworkManager.Singleton;
            var playerObject = manager != null && manager.IsClient
                ? manager.LocalClient?.PlayerObject
                : null;

            if (playerObject == m_BoundPlayer) return;

            if (playerObject == null)
            {
                Unbind();
                return;
            }

            Bind(playerObject);
        }

        void Bind(NetworkObject playerObject)
        {
            // GetComponentInChildren et pas GetComponent : le prefab joueur peut être le bateau
            // lui-même, ou un objet racine qui le contient. Les deux marchent.
            var authority = playerObject.GetComponentInChildren<BoatNetworkAuthority>();
            if (authority == null)
            {
                Debug.LogError(
                    $"[Bato] Le prefab joueur '{playerObject.name}' n'a pas de BoatNetworkAuthority : " +
                    "la caméra n'a rien à suivre.", playerObject);
                return;
            }

            m_Camera.SetTarget(authority.transform, authority.GetComponent<Rigidbody>());
            if (!AlignControlScheme(authority.GetComponent<PlayerInput>())) return;

            m_BoundPlayer = playerObject;
        }

        void Unbind()
        {
            if (m_BoundPlayer == null) return;

            m_Camera.SetTarget(null, null);
            m_BoundPlayer = null;
        }

        /// <summary>
        /// Force le PlayerInput du bateau sur les mêmes périphériques que celui de la caméra.
        ///
        /// Deux PlayerInput coexistent (un sur le bateau, un sur la caméra) et l'Input System
        /// apparie les périphériques par utilisateur : sans ça, le second n'en reçoit aucun et
        /// reste muet. InputDeviceModeToggle fait déjà ce travail, mais sa liste ne peut pas
        /// référencer un bateau qui n'existe qu'au runtime — on complète ici.
        /// </summary>
        bool AlignControlScheme(PlayerInput boatInput)
        {
            if (boatInput == null || m_InputMode == null) return true;
            if (!boatInput.isActiveAndEnabled || !boatInput.user.valid) return false;

            boatInput.neverAutoSwitchControlSchemes = true;

            if (m_InputMode.UseGamepad)
            {
                if (Gamepad.current == null)
                {
                    Debug.LogWarning("[Bato] Mode manette demandé mais aucune manette détectée : le bateau ne répondra pas.", this);
                    return false;
                }
                boatInput.SwitchCurrentControlScheme("Gamepad", Gamepad.current);
            }
            else
            {
                if (Keyboard.current == null || Mouse.current == null)
                {
                    Debug.LogWarning("[Bato] Clavier ou souris absent : le bateau ne répondra pas.", this);
                    return false;
                }
                boatInput.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard.current, Mouse.current);
            }

            return true;
        }
    }
}
