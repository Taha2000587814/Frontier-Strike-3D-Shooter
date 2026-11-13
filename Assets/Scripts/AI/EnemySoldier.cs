using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace FrontierStrike
{
    /// <summary>Per-spawn tuning, scaled by the wave number in <see cref="GameManager"/>.</summary>
    public struct EnemyStats
    {
        public float Health;
        public float MoveSpeed;
        public float Damage;
        /// <summary>Hit chance at point-blank range (0..1). Falls off with distance.</summary>
        public float Accuracy;
        public float AttackRange;
        public int BurstSize;
        public Vector2 BurstCooldown;

        public static EnemyStats ForWave(int wave)
        {
            int w = Mathf.Max(0, wave - 1);
            return new EnemyStats
            {
                Health = 100.0f + 15.0f * w,
                MoveSpeed = 3.2f + 0.2f * w,
                Damage = 6.0f + 1.0f * w,
                Accuracy = Mathf.Min(0.75f, 0.40f + 0.05f * w),
                AttackRange = 28.0f,
                BurstSize = 3 + w / 2,
                BurstCooldown = new Vector2(Mathf.Max(0.8f, 1.6f - 0.15f * w), Mathf.Max(1.4f, 2.6f - 0.2f * w)),
            };
        }
    }

    /// <summary>
    /// Hostile soldier: advances on the player, stops to fire bursts when it has line of sight,
    /// strafes between bursts and dies with a simple procedural fall. Built from primitives at runtime.
    /// Uses a NavMeshAgent when a NavMesh exists, otherwise falls back to direct steering.
    /// </summary>
    public class EnemySoldier : MonoBehaviour, IDamageable
    {
        private enum State { Advance, Attack, Dead }

        // Layers the enemy's line-of-sight / shot rays ignore: TransparentFX, Ignore Raycast, UI, First Person View, Projectile.
        private const int IgnoredLayers = (1 << 1) | (1 << 2) | (1 << 5) | (1 << 9) | (1 << 15);
        private const int SightMask = ~IgnoredLayers;
        private const float EyeHeight = 1.6f;
        private const float RepathInterval = 0.25f;

        public bool IsDead => state == State.Dead;

        private EnemyStats stats;
        private float health;
        private State state;

        private NavMeshAgent agent;
        private bool useNavMesh;
        private Transform model;
        private Transform legLeft, legRight;
        private Transform muzzle;
        private Collider headCollider;
        private Collider[] colliders;
        private Renderer[] renderers;
        private LineRenderer tracer;
        private AudioSource audioSource;
        private Light muzzleLight;
        private MaterialPropertyBlock flashBlock;

        private float nextRepathTime;
        private float nextBurstTime;
        private float lastSeenPlayerTime = -999.0f;
        private Vector3 strafeTarget;
        private bool hasStrafeTarget;
        private float strafeGiveUpTime;
        private bool firing;
        private float walkCycle;
        private float recoil;

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        #region Construction

        public static EnemySoldier Spawn(Vector3 position, Quaternion rotation, EnemyStats stats, bool navMeshAvailable)
        {
            var root = new GameObject("Enemy Soldier");
            root.transform.SetPositionAndRotation(position, rotation);

            // Physics: kinematic body so projectiles register proper hits on a moving compound collider.
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0.0f, 0.75f, 0.0f);
            capsule.height = 1.5f;
            capsule.radius = 0.32f;

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0.0f, 1.64f, 0.0f);
            var headSphere = head.AddComponent<SphereCollider>();
            headSphere.radius = 0.2f;

            var soldier = root.AddComponent<EnemySoldier>();
            soldier.headCollider = headSphere;
            soldier.BuildModel();
            soldier.Init(stats, navMeshAvailable);
            return soldier;
        }

        private void BuildModel()
        {
            Material uniform = RuntimeVisuals.Mat(new Color(0.33f, 0.36f, 0.24f));
            Material uniformDark = RuntimeVisuals.Mat(new Color(0.20f, 0.22f, 0.15f));
            Material boots = RuntimeVisuals.Mat(new Color(0.12f, 0.10f, 0.08f));
            Material skin = RuntimeVisuals.Mat(new Color(0.78f, 0.60f, 0.46f));
            Material metal = RuntimeVisuals.Mat(new Color(0.10f, 0.10f, 0.11f));
            Material visor = RuntimeVisuals.Mat(new Color(1.0f, 0.15f, 0.1f), new Color(2.0f, 0.2f, 0.1f));

            model = new GameObject("Model").transform;
            model.SetParent(transform, false);

            // Legs hang from hip pivots so they can swing while walking.
            legLeft = MakeLeg(-0.12f, uniformDark, boots);
            legRight = MakeLeg(0.12f, uniformDark, boots);

            RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(0, 1.2f, 0), new Vector3(0.5f, 0.6f, 0.28f), uniform);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(0, 1.22f, 0), new Vector3(0.54f, 0.42f, 0.33f), uniformDark);
            RuntimeVisuals.Primitive(PrimitiveType.Sphere, model, new Vector3(0, 1.62f, 0), Vector3.one * 0.3f, skin);
            RuntimeVisuals.Primitive(PrimitiveType.Sphere, model, new Vector3(0, 1.69f, -0.01f), new Vector3(0.35f, 0.24f, 0.37f), uniformDark);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(0, 1.63f, 0.14f), new Vector3(0.24f, 0.06f, 0.04f), visor);

            // Arms holding a rifle forward.
            GameObject armR = RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(0.22f, 1.3f, 0.2f), new Vector3(0.12f, 0.12f, 0.45f), uniform);
            armR.transform.localRotation = Quaternion.Euler(0, -10, 0);
            GameObject armL = RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(-0.1f, 1.3f, 0.3f), new Vector3(0.12f, 0.12f, 0.45f), uniform);
            armL.transform.localRotation = Quaternion.Euler(0, 30, 0);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(0.1f, 1.34f, 0.5f), new Vector3(0.07f, 0.13f, 0.75f), metal);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, model, new Vector3(0.1f, 1.24f, 0.42f), new Vector3(0.05f, 0.14f, 0.08f), metal);

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0.1f, 1.35f, 0.9f);

            var lightGo = new GameObject("Muzzle Light");
            lightGo.transform.SetParent(muzzle, false);
            muzzleLight = lightGo.AddComponent<Light>();
            muzzleLight.type = LightType.Point;
            muzzleLight.color = new Color(1.0f, 0.75f, 0.35f);
            muzzleLight.range = 6.0f;
            muzzleLight.intensity = 3.0f;
            muzzleLight.enabled = false;

            tracer = gameObject.AddComponent<LineRenderer>();
            tracer.positionCount = 2;
            tracer.useWorldSpace = true;
            tracer.startWidth = 0.035f;
            tracer.endWidth = 0.01f;
            tracer.sharedMaterial = RuntimeVisuals.Mat(new Color(1.0f, 0.85f, 0.4f), new Color(3.0f, 2.2f, 0.8f));
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.enabled = false;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1.0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 4.0f;
            audioSource.maxDistance = 70.0f;
            audioSource.volume = 0.55f;
        }

        private Transform MakeLeg(float x, Material trousers, Material boots)
        {
            var pivot = new GameObject("Leg Pivot").transform;
            pivot.SetParent(model, false);
            pivot.localPosition = new Vector3(x, 0.9f, 0);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, pivot, new Vector3(0, -0.42f, 0), new Vector3(0.18f, 0.84f, 0.2f), trousers);
            RuntimeVisuals.Primitive(PrimitiveType.Cube, pivot, new Vector3(0, -0.86f, 0.04f), new Vector3(0.2f, 0.1f, 0.3f), boots);
            return pivot;
        }

        private void Init(EnemyStats enemyStats, bool navMeshAvailable)
        {
            stats = enemyStats;
            health = stats.Health;
            colliders = GetComponentsInChildren<Collider>();
            renderers = model.GetComponentsInChildren<Renderer>();
            flashBlock = new MaterialPropertyBlock();
            nextBurstTime = Time.time + Random.Range(0.8f, 2.0f);

            if (navMeshAvailable && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                agent = gameObject.AddComponent<NavMeshAgent>();
                agent.radius = 0.35f;
                agent.height = 1.8f;
                agent.speed = stats.MoveSpeed;
                agent.angularSpeed = 540.0f;
                agent.acceleration = 14.0f;
                agent.stoppingDistance = 1.0f;
                agent.autoBraking = true;
                agent.avoidancePriority = Random.Range(30, 70);
                useNavMesh = agent.isOnNavMesh;
                if (!useNavMesh)
                    agent.enabled = false;
            }
        }

        #endregion

        #region Update

        private void Update()
        {
            if (state == State.Dead)
                return;

            GameManager gm = GameManager.Instance;
            Transform player = gm != null && gm.Player != null ? gm.Player.transform : null;
            if (player == null || gm.PlayerHealth == null || gm.PlayerHealth.IsDead)
            {
                StopMoving();
                AnimateModel(0.0f);
                return;
            }

            Vector3 playerAim = GetPlayerAimPoint(gm);
            float distance = Vector3.Distance(transform.position, player.position);
            bool canSee = distance < 80.0f && HasLineOfSight(playerAim);
            if (canSee)
                lastSeenPlayerTime = Time.time;

            switch (state)
            {
                case State.Advance:
                    MoveTowards(player.position);
                    if (canSee && distance <= stats.AttackRange)
                    {
                        state = State.Attack;
                        hasStrafeTarget = false;
                    }
                    break;

                case State.Attack:
                    FaceTowards(player.position);
                    // Lost the player for a while, or they ran away: push forward again.
                    if (distance > stats.AttackRange * 1.25f || Time.time - lastSeenPlayerTime > 1.5f)
                    {
                        state = State.Advance;
                        break;
                    }

                    if (!firing && Time.time >= nextBurstTime && canSee)
                        StartCoroutine(FireBurst(gm));

                    // Strafe between bursts so enemies are not sitting ducks.
                    if (!firing)
                    {
                        if (!hasStrafeTarget)
                            PickStrafeTarget(player.position);
                        MoveTowards(strafeTarget, faceMovement: false);
                        // Arrival radius must exceed the agent's stopping distance (1 m) or it never "arrives".
                        Vector3 toStrafe = strafeTarget - transform.position;
                        toStrafe.y = 0.0f;
                        if (toStrafe.sqrMagnitude < 1.5f * 1.5f || Time.time > strafeGiveUpTime)
                            hasStrafeTarget = false;
                    }
                    else
                        StopMoving();
                    break;
            }

            float speed = useNavMesh ? agent.velocity.magnitude : fallbackSpeed;
            AnimateModel(speed);
        }

        private Vector3 GetPlayerAimPoint(GameManager gm)
        {
            Camera cam = gm.Player.GetCameraWorld();
            Vector3 head = cam != null ? cam.transform.position : gm.Player.transform.position + Vector3.up * 1.6f;
            // Aim at upper chest rather than the eyes.
            return head + Vector3.down * 0.35f;
        }

        private bool HasLineOfSight(Vector3 target)
        {
            Vector3 eye = transform.position + Vector3.up * EyeHeight + transform.forward * 0.35f;
            Vector3 dir = target - eye;
            if (!Physics.Raycast(eye, dir.normalized, out RaycastHit hit, dir.magnitude + 0.5f, SightMask, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.GetComponentInParent<PlayerHealth>() != null;
        }

        private void PickStrafeTarget(Vector3 playerPos)
        {
            Vector3 toPlayer = (playerPos - transform.position);
            toPlayer.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer.normalized) * (Random.value < 0.5f ? -1.0f : 1.0f);
            Vector3 candidate = transform.position + side * Random.Range(2.0f, 5.0f) + toPlayer.normalized * Random.Range(-1.0f, 2.0f);

            if (useNavMesh && NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                candidate = hit.position;
            strafeTarget = candidate;
            hasStrafeTarget = true;
            strafeGiveUpTime = Time.time + 3.0f;
        }

        #endregion

        #region Movement

        private float fallbackSpeed;

        private void MoveTowards(Vector3 destination, bool faceMovement = true)
        {
            if (useNavMesh)
            {
                agent.isStopped = false;
                agent.updateRotation = faceMovement;
                if (Time.time >= nextRepathTime)
                {
                    agent.SetDestination(destination);
                    nextRepathTime = Time.time + RepathInterval;
                }
                return;
            }

            // Fallback steering without a NavMesh: walk straight, snap to ground, stop at walls.
            Vector3 to = destination - transform.position;
            to.y = 0.0f;
            if (to.magnitude < 1.0f)
            {
                fallbackSpeed = 0.0f;
                return;
            }

            Vector3 dir = to.normalized;
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            if (Physics.SphereCast(origin, 0.3f, dir, out RaycastHit block, 0.8f, SightMask, QueryTriggerInteraction.Ignore)
                && block.collider.GetComponentInParent<EnemySoldier>() != this)
            {
                // Slide along the obstacle.
                dir = Vector3.ProjectOnPlane(dir, block.normal).normalized;
            }

            float step = stats.MoveSpeed * Time.deltaTime;
            Vector3 next = transform.position + dir * step;
            if (TryFindGround(next, out float groundY))
                next.y = groundY;
            transform.position = next;
            fallbackSpeed = stats.MoveSpeed;

            if (faceMovement && dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540.0f * Time.deltaTime);
        }

        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        /// <summary>Highest ground point under <paramref name="position"/>, ignoring enemies (including this one).</summary>
        private static bool TryFindGround(Vector3 position, out float groundY)
        {
            groundY = position.y;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 2.5f, Vector3.down, GroundHits, 7.0f, SightMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = GroundHits[i];
                if (h.collider.GetComponentInParent<EnemySoldier>() != null || h.collider.GetComponentInParent<PlayerHealth>() != null)
                    continue;
                if (h.point.y > best)
                    best = h.point.y;
            }
            if (float.IsNegativeInfinity(best))
                return false;
            groundY = best;
            return true;
        }

        private void StopMoving()
        {
            fallbackSpeed = 0.0f;
            if (useNavMesh && agent.isOnNavMesh)
                agent.isStopped = true;
        }

        private void FaceTowards(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.001f)
                return;
            if (useNavMesh)
                agent.updateRotation = false;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 400.0f * Time.deltaTime);
        }

        private void AnimateModel(float speed)
        {
            float moveAmount = Mathf.Clamp01(speed / Mathf.Max(0.1f, stats.MoveSpeed));
            walkCycle += Time.deltaTime * (4.0f + speed * 1.6f) * moveAmount;
            float swing = Mathf.Sin(walkCycle) * 35.0f * moveAmount;

            legLeft.localRotation = Quaternion.Euler(swing, 0, 0);
            legRight.localRotation = Quaternion.Euler(-swing, 0, 0);

            recoil = Mathf.MoveTowards(recoil, 0.0f, Time.deltaTime * 8.0f);
            float bob = Mathf.Abs(Mathf.Cos(walkCycle)) * 0.05f * moveAmount;
            model.localPosition = new Vector3(0, bob, -recoil * 0.06f);
            model.localRotation = Quaternion.Euler(moveAmount * 6.0f - recoil * 4.0f, 0, 0);
        }

        #endregion

        #region Combat

        private IEnumerator FireBurst(GameManager gm)
        {
            firing = true;
            for (int i = 0; i < stats.BurstSize; i++)
            {
                if (state == State.Dead || gm.PlayerHealth == null || gm.PlayerHealth.IsDead)
                    break;
                FireShot(gm);
                yield return new WaitForSeconds(0.13f);
            }
            firing = false;
            nextBurstTime = Time.time + Random.Range(stats.BurstCooldown.x, stats.BurstCooldown.y);
        }

        private void FireShot(GameManager gm)
        {
            Vector3 target = GetPlayerAimPoint(gm);
            Vector3 from = muzzle.position;
            float distance = Vector3.Distance(from, target);

            // Hit chance: drops with distance, worse against a sprinting player.
            float chance = stats.Accuracy * Mathf.Lerp(1.0f, 0.35f, Mathf.InverseLerp(5.0f, stats.AttackRange * 1.25f, distance));
            if (gm.Player.IsRunning())
                chance *= 0.6f;
            bool hit = Random.value < chance;

            Vector3 aimPoint = hit ? target : target + Random.insideUnitSphere * Mathf.Lerp(0.6f, 1.6f, distance / 40.0f) + Vector3.up * 0.3f;
            Vector3 dir = (aimPoint - from).normalized;
            Vector3 end = from + dir * 120.0f;

            if (Physics.Raycast(from, dir, out RaycastHit rayHit, 120.0f, SightMask, QueryTriggerInteraction.Ignore))
            {
                end = rayHit.point;
                var damageable = rayHit.collider.GetComponentInParent<IDamageable>();
                if (damageable is PlayerHealth playerHealth)
                {
                    if (hit)
                        playerHealth.TakeDamage(new DamageInfo(stats.Damage, rayHit.point, dir, rayHit.collider, gameObject));
                }
            }

            recoil = 1.0f;
            StartCoroutine(ShowTracer(from, end));

            AudioClip clip = gm.EnemyFireClip;
            if (clip != null)
            {
                audioSource.pitch = Random.Range(0.82f, 0.92f);
                audioSource.PlayOneShot(clip);
            }
        }

        private IEnumerator ShowTracer(Vector3 from, Vector3 to)
        {
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.enabled = true;
            muzzleLight.enabled = true;
            yield return new WaitForSeconds(0.04f);
            tracer.enabled = false;
            muzzleLight.enabled = false;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (state == State.Dead)
                return;

            bool headshot = info.HitCollider != null && info.HitCollider == headCollider;
            float amount = headshot ? info.Amount * 2.0f : info.Amount;
            health -= amount;

            // Getting shot always reveals where the player is.
            lastSeenPlayerTime = Time.time;
            if (state == State.Advance)
                nextRepathTime = 0.0f;

            GameManager gm = GameManager.Instance;
            bool killed = health <= 0.0f;
            if (gm != null)
                gm.OnEnemyHit(this, headshot, killed);

            if (killed)
                Die(info.Direction, headshot);
            else
                StartCoroutine(HitFlash());
        }

        private IEnumerator HitFlash()
        {
            flashBlock.SetColor(ColorId, new Color(1.0f, 0.35f, 0.3f));
            foreach (Renderer r in renderers)
                r.SetPropertyBlock(flashBlock);
            yield return new WaitForSeconds(0.07f);
            foreach (Renderer r in renderers)
                if (r != null)
                    r.SetPropertyBlock(null);
        }

        private void Die(Vector3 hitDirection, bool headshot)
        {
            state = State.Dead;
            StopAllCoroutines();
            tracer.enabled = false;
            muzzleLight.enabled = false;
            foreach (Renderer r in renderers)
                r.SetPropertyBlock(null);

            if (agent != null)
                agent.enabled = false;
            foreach (Collider c in colliders)
                if (c != null)
                    c.enabled = false;

            if (GameManager.Instance != null)
                GameManager.Instance.OnEnemyKilled(this, headshot);

            StartCoroutine(DeathRoutine(hitDirection));
        }

        private IEnumerator DeathRoutine(Vector3 hitDirection)
        {
            // Fall away from the shot.
            Vector3 flat = hitDirection;
            flat.y = 0.0f;
            if (flat.sqrMagnitude < 0.001f)
                flat = -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, flat.normalized);

            Quaternion start = transform.rotation;
            Quaternion end = Quaternion.AngleAxis(88.0f, axis) * start;
            Vector3 pos = transform.position;

            for (float t = 0; t < 1.0f; t += Time.deltaTime / 0.45f)
            {
                float eased = t * t;
                transform.rotation = Quaternion.Slerp(start, end, eased);
                yield return null;
            }
            transform.rotation = end;

            yield return new WaitForSeconds(4.0f);

            // Sink into the ground, then clean up.
            for (float t = 0; t < 1.0f; t += Time.deltaTime / 1.5f)
            {
                transform.position = pos + Vector3.down * (t * 0.6f);
                yield return null;
            }
            Destroy(gameObject);
        }

        #endregion
    }
}
