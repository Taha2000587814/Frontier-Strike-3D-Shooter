using UnityEngine;

namespace FrontierStrike
{
    /// <summary>
    /// Floating medkit dropped by enemies. Heals the player on touch, despawns after a while.
    /// </summary>
    public class HealthPickup : MonoBehaviour
    {
        private const float HealAmount = 30.0f;
        private const float Lifetime = 25.0f;
        private const float PickupRadius = 1.4f;

        private Vector3 basePosition;
        private float spawnTime;
        private Transform visual;

        public static HealthPickup Spawn(Vector3 groundPosition)
        {
            var root = new GameObject("Health Pickup");
            root.transform.position = groundPosition + Vector3.up * 0.6f;

            // Visual: white box with a red cross, built from primitives.
            var box = RuntimeVisuals.Primitive(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.45f, 0.3f, 0.45f), RuntimeVisuals.Mat(new Color(0.92f, 0.92f, 0.9f)));
            Material red = RuntimeVisuals.Mat(new Color(0.85f, 0.1f, 0.1f), new Color(0.6f, 0.0f, 0.0f));
            RuntimeVisuals.Primitive(PrimitiveType.Cube, box.transform, new Vector3(0, 0.51f, 0), new Vector3(0.7f, 0.05f, 0.2f), red);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, box.transform, new Vector3(0, 0.51f, 0), new Vector3(0.2f, 0.05f, 0.7f), red);

            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 1.0f, 0.5f);
            light.range = 3.0f;
            light.intensity = 1.5f;

            var pickup = root.AddComponent<HealthPickup>();
            pickup.visual = box.transform;
            return pickup;
        }

        private void Start()
        {
            basePosition = transform.position;
            spawnTime = Time.time;
        }

        private void Update()
        {
            float age = Time.time - spawnTime;
            if (age > Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = basePosition + Vector3.up * (Mathf.Sin(age * 2.5f) * 0.12f);
            if (visual != null)
                visual.Rotate(0.0f, 90.0f * Time.deltaTime, 0.0f, Space.World);

            // Blink during the last 5 seconds.
            if (visual != null && age > Lifetime - 5.0f)
                visual.gameObject.SetActive(Mathf.Repeat(age * 6.0f, 1.0f) > 0.3f);

            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Player == null || gm.PlayerHealth == null)
                return;

            Vector3 playerPos = gm.Player.transform.position;
            Vector3 flat = playerPos - transform.position;
            flat.y = 0.0f;
            if (flat.sqrMagnitude > PickupRadius * PickupRadius || Mathf.Abs(playerPos.y - transform.position.y) > 2.5f)
                return;

            PlayerHealth health = gm.PlayerHealth;
            if (health.IsDead || health.Current >= health.Max)
                return;

            health.Heal(HealAmount);
            gm.OnPickupCollected(transform.position);
            Destroy(gameObject);
        }
    }
}
