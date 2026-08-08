using System.Collections;
using Features.Player;
using Unity.Netcode;
using UnityEngine;

namespace Bato
{
    /// <summary>
    /// Statuts appliqués par le serveur (brûlure DoT, ralentissement).
    /// </summary>
    public class BoatStatusEffects : NetworkBehaviour
    {
        [SerializeField] float m_BurnTickInterval = 0.5f;
        [SerializeField] int m_BurnDamagePerTick = 4;
        [SerializeField] float m_DefaultBurnDuration = 4f;
        [SerializeField] float m_DefaultSlowDuration = 4f;
        [SerializeField] float m_SlowMultiplier = 0.45f;

        readonly NetworkVariable<float> m_SpeedMultiplier = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        BoatHealth m_Health;
        BoatMovementController m_Movement;
        Coroutine m_BurnRoutine;
        Coroutine m_SlowRoutine;

        public float SpeedMultiplier => m_SpeedMultiplier.Value;

        void Awake()
        {
            m_Health = GetComponent<BoatHealth>();
            m_Movement = GetComponent<BoatMovementController>();
        }

        public override void OnNetworkSpawn()
        {
            m_SpeedMultiplier.OnValueChanged += OnSpeedChanged;
            ApplySpeedLocal(m_SpeedMultiplier.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_SpeedMultiplier.OnValueChanged -= OnSpeedChanged;
        }

        void OnSpeedChanged(float _, float value) => ApplySpeedLocal(value);

        void ApplySpeedLocal(float value)
        {
            if (m_Movement != null) m_Movement.SpeedMultiplier = value;
        }

        /// <summary>Serveur : brûlure qui retire des PV à intervalles réguliers.</summary>
        public void ApplyBurn(ulong attackerClientId, float duration = -1f)
        {
            if (!IsServer || m_Health == null || !m_Health.IsAlive) return;
            if (duration <= 0f) duration = m_DefaultBurnDuration;

            if (m_BurnRoutine != null) StopCoroutine(m_BurnRoutine);
            m_BurnRoutine = StartCoroutine(BurnRoutine(attackerClientId, duration));
        }

        /// <summary>Serveur : ralentit le bateau adverse.</summary>
        public void ApplySlow(float duration = -1f, float multiplier = -1f)
        {
            if (!IsServer || m_Health == null || !m_Health.IsAlive) return;
            if (duration <= 0f) duration = m_DefaultSlowDuration;
            if (multiplier <= 0f) multiplier = m_SlowMultiplier;

            if (m_SlowRoutine != null) StopCoroutine(m_SlowRoutine);
            m_SlowRoutine = StartCoroutine(SlowRoutine(duration, multiplier));
        }

        public void ClearAll()
        {
            if (!IsServer) return;
            if (m_BurnRoutine != null) StopCoroutine(m_BurnRoutine);
            if (m_SlowRoutine != null) StopCoroutine(m_SlowRoutine);
            m_BurnRoutine = null;
            m_SlowRoutine = null;
            m_SpeedMultiplier.Value = 1f;
        }

        IEnumerator BurnRoutine(ulong attackerClientId, float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end && m_Health != null && m_Health.IsAlive)
            {
                m_Health.ApplyDamage(m_BurnDamagePerTick, attackerClientId);
                yield return new WaitForSeconds(m_BurnTickInterval);
            }

            m_BurnRoutine = null;
        }

        IEnumerator SlowRoutine(float duration, float multiplier)
        {
            m_SpeedMultiplier.Value = multiplier;
            yield return new WaitForSeconds(duration);
            m_SpeedMultiplier.Value = 1f;
            m_SlowRoutine = null;
        }
    }
}
