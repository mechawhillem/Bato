using UnityEngine;
using UnityEngine.InputSystem;

namespace Bato
{
    /// <summary>
    /// Lit les actions du InputActionAsset du projet (Assets/InputSystem_Actions.inputactions,
    /// déjà fourni par le template URP) et les expose sous une forme « bateau ».
    /// N'est actif que sur le bateau que ce client possède.
    /// </summary>
    public class BoatInput : MonoBehaviour
    {
        [SerializeField] InputActionAsset m_Actions;
        [SerializeField] string m_ActionMap = "Player";

        InputAction m_Move;
        InputAction m_Fire;

        /// <summary>-1 (marche arrière) à 1 (plein gaz).</summary>
        public float Throttle { get; private set; }
        /// <summary>-1 (bâbord) à 1 (tribord).</summary>
        public float Steer { get; private set; }
        /// <summary>Tir maintenu. Le canon a son propre cooldown, pas besoin de spammer.</summary>
        /// Lu directement sur l'action pour ne pas dépendre de l'ordre d'exécution des Update.
        public bool FireHeld => enabled && m_Fire != null && m_Fire.IsPressed();

        void OnEnable()
        {
            // Repli sur les actions projet-wide si le champ n'est pas assigné.
            var asset = m_Actions != null ? m_Actions : InputSystem.actions;
            if (asset == null)
            {
                Debug.LogError("[BoatInput] Aucun InputActionAsset assigné.");
                enabled = false;
                return;
            }

            var map = asset.FindActionMap(m_ActionMap, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError($"[BoatInput] Action map '{m_ActionMap}' introuvable.");
                enabled = false;
                return;
            }

            m_Move = map.FindAction("Move", throwIfNotFound: false);
            m_Fire = map.FindAction("Attack", throwIfNotFound: false);
            map.Enable();
        }

        void OnDisable()
        {
            Throttle = Steer = 0f;
        }

        void Update()
        {
            var move = m_Move != null ? m_Move.ReadValue<Vector2>() : Vector2.zero;
            Throttle = move.y;
            Steer = move.x;
        }
    }
}
