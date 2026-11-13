using UnityEngine;

namespace FrontierStrike
{
    /// <summary>
    /// Everything that can take damage (player, enemies, destructibles) implements this.
    /// Projectiles and enemy hitscan look it up with GetComponentInParent.
    /// </summary>
    public interface IDamageable
    {
        bool IsDead { get; }
        void TakeDamage(DamageInfo info);
    }

    public readonly struct DamageInfo
    {
        public float Amount { get; }
        public Vector3 Point { get; }
        public Vector3 Direction { get; }
        /// <summary>The exact collider that was hit (used for headshots). Can be null.</summary>
        public Collider HitCollider { get; }
        /// <summary>Whoever caused the damage. Can be null.</summary>
        public GameObject Source { get; }

        public DamageInfo(float amount, Vector3 point, Vector3 direction, Collider hitCollider = null, GameObject source = null)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            HitCollider = hitCollider;
            Source = source;
        }

        public DamageInfo WithAmount(float amount) => new DamageInfo(amount, Point, Direction, HitCollider, Source);
    }
}
