using System;
using UnityEngine;

namespace FrontierStrike
{
    /// <summary>
    /// Player hit points with delayed regeneration. Added to the player at runtime by <see cref="GameManager"/>.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100.0f;
        [Tooltip("Seconds without taking damage before health starts regenerating.")]
        [SerializeField] private float regenDelay = 5.0f;
        [Tooltip("Health regenerated per second.")]
        [SerializeField] private float regenRate = 8.0f;
        [Tooltip("Regeneration never goes above this fraction of max health. Pickups can go above it.")]
        [Range(0, 1)]
        [SerializeField] private float regenCap = 0.6f;

        public float Current { get; private set; }
        public float Max => maxHealth;
        public float Normalized => Current / maxHealth;
        public bool IsDead { get; private set; }
        public bool Invulnerable { get; set; }

        /// <summary>Raised with the world-space direction the damage came from.</summary>
        public event Action<DamageInfo> Damaged;
        public event Action Died;

        private float lastDamageTime = -999.0f;

        private void Awake() => Current = maxHealth;

        private void Update()
        {
            if (IsDead || Time.time - lastDamageTime < regenDelay)
                return;
            float cap = maxHealth * regenCap;
            if (Current < cap)
                Current = Mathf.Min(cap, Current + regenRate * Time.deltaTime);
        }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || Invulnerable || info.Amount <= 0.0f)
                return;

            Current = Mathf.Max(0.0f, Current - info.Amount);
            lastDamageTime = Time.time;
            Damaged?.Invoke(info);

            if (Current <= 0.0f)
            {
                IsDead = true;
                Died?.Invoke();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead)
                return;
            Current = Mathf.Min(maxHealth, Current + amount);
        }
    }
}
