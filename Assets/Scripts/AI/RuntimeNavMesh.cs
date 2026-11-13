using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FrontierStrike
{
    /// <summary>
    /// Bakes a NavMesh for the level at runtime from its physics colliders, so enemies can path
    /// around the military base without a baked NavMesh in the scene.
    /// </summary>
    public class RuntimeNavMesh
    {
        // Leave out layers that are not walkable level geometry:
        // TransparentFX, Ignore Raycast, UI, First Person View, Character, Post Processing (x3), Projectile.
        private const int ExcludedLayers = (1 << 1) | (1 << 2) | (1 << 5) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12) | (1 << 13) | (1 << 15);

        private NavMeshData data;
        private NavMeshDataInstance instance;
        private AsyncOperation operation;

        public bool IsDone => operation == null || operation.isDone;
        public bool IsValid { get; private set; }

        /// <summary>Starts an async bake of a square area around <paramref name="center"/>.</summary>
        public void BuildAsync(Vector3 center, float size)
        {
            var bounds = new Bounds(center, new Vector3(size, 120.0f, size));

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~ExcludedLayers, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);

            // Skip trigger volumes, they are not walls or floors.
            sources.RemoveAll(s => s.component is Collider c && c.isTrigger);

            // Mesh colliders whose meshes are not CPU-readable can't be baked in a player build
            // (the military base fences, for example). Approximate them with an oriented box.
            for (int i = 0; i < sources.Count; i++)
            {
                NavMeshBuildSource s = sources[i];
                if (s.shape != NavMeshBuildSourceShape.Mesh || !(s.sourceObject is Mesh mesh) || mesh.isReadable)
                    continue;
                s.shape = NavMeshBuildSourceShape.Box;
                s.transform = s.transform * Matrix4x4.Translate(mesh.bounds.center);
                s.size = mesh.bounds.size;
                s.sourceObject = null;
                sources[i] = s;
            }

            if (sources.Count == 0)
            {
                Debug.LogWarning("[Frontier Strike] No colliders found for the runtime NavMesh. Enemies will use direct steering.");
                return;
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 40.0f;

            data = new NavMeshData(settings.agentTypeID);
            instance = NavMesh.AddNavMeshData(data);
            operation = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
            operation.completed += _ => IsValid = instance.valid && HasAnyNavMesh(center, size);
        }

        private static bool HasAnyNavMesh(Vector3 center, float size)
        {
            return NavMesh.SamplePosition(center, out _, size * 0.5f, NavMesh.AllAreas);
        }

        public void Dispose()
        {
            if (instance.valid)
                instance.Remove();
            if (data != null)
                Object.Destroy(data);
        }

        /// <summary>
        /// Size of the area to bake: the terrain when there is one, otherwise a fixed square. Capped to keep the bake quick.
        /// </summary>
        public static (Vector3 center, float size) GetLevelArea(Vector3 fallbackCenter)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 tSize = terrain.terrainData.size;
                Vector3 c = terrain.transform.position + new Vector3(tSize.x * 0.5f, 0.0f, tSize.z * 0.5f);
                float s = Mathf.Min(Mathf.Max(tSize.x, tSize.z), 320.0f);
                // If the terrain is bigger than the cap, centre the bake on the player instead.
                if (Mathf.Max(tSize.x, tSize.z) > s)
                    c = new Vector3(fallbackCenter.x, c.y, fallbackCenter.z);
                c.y = fallbackCenter.y;
                return (c, s);
            }
            return (fallbackCenter, 200.0f);
        }
    }
}
