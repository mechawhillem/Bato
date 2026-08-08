using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bato
{
    /// <summary>
    /// Tir chargé : O = gauche, P = droit.
    /// Appui court = tir presque droit. Maintien = petite courbe qui grossit progressivement.
    /// </summary>
    public class BoatCannon : NetworkBehaviour
    {
        const byte k_Left = 0;
        const byte k_Right = 1;
        const float k_MuzzleOffset = 0.6f;

        [SerializeField] GameObject m_CannonballPrefab;
        [Tooltip("Canon bâbord / gauche — touche O (AttackLeft).")]
        [SerializeField] Transform m_MuzzleLeft;
        [Tooltip("Canon tribord / droit — touche P (Attack).")]
        [SerializeField] Transform m_MuzzleRight;
        [SerializeField] float m_Cooldown = 0.8f;
        [Tooltip("Vitesse du tir tap (presque droit).")]
        [SerializeField] float m_MinMuzzleSpeed = 24f;
        [Tooltip("Vitesse à charge max.")]
        [SerializeField] float m_MaxMuzzleSpeed = 38f;
        [Tooltip("Angle vers le haut au tap — quasi droit (petite courbe).")]
        [SerializeField] float m_MinElevation = 3f;
        [Tooltip("Angle vers le haut à charge max.")]
        [SerializeField] float m_MaxElevation = 38f;
        [Tooltip("Temps pour atteindre la puissance max en maintenant la touche.")]
        [SerializeField] float m_ChargeDuration = 2.5f;
        [Tooltip(">1 = la courbe grossit lentement au début, plus vite ensuite.")]
        [SerializeField] float m_ArcEase = 1.75f;

        BoatNetworkAuthority m_Authority;
        BoatHealth m_Health;
        CannonAimPreview m_AimPreview;
        float m_LocalNextFireLeft;
        float m_LocalNextFireRight;
        float m_ServerNextFireLeft;
        float m_ServerNextFireRight;
        float m_ChargeStartLeft = -1f;
        float m_ChargeStartRight = -1f;

        /// <summary>0 = pas de charge, sinon 0→1 pour la jauge HUD.</summary>
        public float ChargePower { get; private set; }

        void Awake()
        {
            m_Authority = GetComponent<BoatNetworkAuthority>();
            m_Health = GetComponent<BoatHealth>();
            m_AimPreview = GetComponent<CannonAimPreview>();
            if (m_AimPreview == null) m_AimPreview = gameObject.AddComponent<CannonAimPreview>();
        }

        void Update()
        {
            ChargePower = 0f;

            if (!IsOwner || m_Authority == null)
            {
                m_AimPreview?.Hide();
                return;
            }

            if (m_Health != null && !m_Health.IsAlive)
            {
                CancelCharge(ref m_ChargeStartLeft);
                CancelCharge(ref m_ChargeStartRight);
                m_AimPreview?.Hide();
                return;
            }

            UpdateSide(m_Authority.FireLeftAction, k_Left, ref m_ChargeStartLeft, ref m_LocalNextFireLeft);
            UpdateSide(m_Authority.FireRightAction, k_Right, ref m_ChargeStartRight, ref m_LocalNextFireRight);

            float leftPower = GetChargeProgress(m_ChargeStartLeft);
            float rightPower = GetChargeProgress(m_ChargeStartRight);
            ChargePower = Mathf.Max(leftPower, rightPower);

            UpdateAimPreview(leftPower, rightPower);
        }

        void UpdateAimPreview(float leftPower, float rightPower)
        {
            if (m_AimPreview == null) return;

            Transform muzzle = null;
            float power = 0f;

            // Priorité au côté en cours de charge (le plus avancé si les deux).
            if (rightPower >= leftPower && rightPower > 0f)
            {
                muzzle = m_MuzzleRight;
                power = rightPower;
            }
            else if (leftPower > 0f)
            {
                muzzle = m_MuzzleLeft;
                power = leftPower;
            }

            if (muzzle == null)
            {
                m_AimPreview.Hide();
                return;
            }

            Vector3 velocity = GetLaunchVelocity(muzzle, power);
            Vector3 origin = muzzle.position + velocity.normalized * k_MuzzleOffset;
            m_AimPreview.Show(origin, velocity);
        }

        /// <summary>
        /// Tap (power 0) ≈ tir droit. Plus on charge, plus l'angle monte — lentement au début.
        /// </summary>
        Vector3 GetLaunchVelocity(Transform muzzle, float power)
        {
            power = Mathf.Clamp01(power);

            Vector3 flat = muzzle != null ? muzzle.forward : transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) flat = transform.forward;
            flat.y = 0f;
            flat.Normalize();

            // Ease-in : au début la courbe reste toute petite, puis elle grandit.
            float arc = Mathf.Pow(power, Mathf.Max(1f, m_ArcEase));
            float elevationDeg = Mathf.Lerp(m_MinElevation, m_MaxElevation, arc);
            float elevationRad = elevationDeg * Mathf.Deg2Rad;
            Vector3 direction = (flat * Mathf.Cos(elevationRad) + Vector3.up * Mathf.Sin(elevationRad)).normalized;

            float speed = Mathf.Lerp(m_MinMuzzleSpeed, m_MaxMuzzleSpeed, power);
            return direction * speed;
        }

        void UpdateSide(InputAction action, byte side, ref float chargeStart, ref float nextLocal)
        {
            if (action == null) return;

            if (action.WasPressedThisFrame() && Time.time >= nextLocal)
                chargeStart = Time.time;

            if (chargeStart < 0f) return;

            if (action.WasReleasedThisFrame())
            {
                float power = GetChargeProgress(chargeStart);
                chargeStart = -1f;
                nextLocal = Time.time + m_Cooldown;
                FireRpc(side, power);
                return;
            }

            if (!action.IsPressed())
                chargeStart = -1f;
        }

        float GetChargeProgress(float chargeStart)
        {
            if (chargeStart < 0f) return 0f;
            return Mathf.Clamp01((Time.time - chargeStart) / Mathf.Max(0.01f, m_ChargeDuration));
        }

        static void CancelCharge(ref float chargeStart) => chargeStart = -1f;

        [Rpc(SendTo.Server)]
        void FireRpc(byte side, float power)
        {
            if (m_Health != null && !m_Health.IsAlive) return;

            power = Mathf.Clamp01(power);

            if (side == k_Left)
            {
                if (Time.time < m_ServerNextFireLeft) return;
                m_ServerNextFireLeft = Time.time + m_Cooldown * 0.9f;
            }
            else
            {
                if (Time.time < m_ServerNextFireRight) return;
                m_ServerNextFireRight = Time.time + m_Cooldown * 0.9f;
            }

            if (m_CannonballPrefab == null)
            {
                Debug.LogError("[BoatCannon] Prefab de boulet non assigné.");
                return;
            }

            var muzzle = side == k_Left ? m_MuzzleLeft : m_MuzzleRight;
            if (muzzle == null)
            {
                Debug.LogWarning($"[BoatCannon] Muzzle {(side == k_Left ? "Left" : "Right")} non assigné.");
                return;
            }

            var velocity = GetLaunchVelocity(muzzle, power);
            var direction = velocity.normalized;
            var ball = Instantiate(
                m_CannonballPrefab,
                muzzle.position + direction * k_MuzzleOffset,
                Quaternion.LookRotation(direction));
            ball.GetComponent<NetworkObject>().Spawn();
            ball.GetComponent<Cannonball>().Launch(velocity, OwnerClientId);
        }
    }
}
