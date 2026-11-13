using System.Collections.Generic;
using UnityEngine;

namespace FrontierStrike
{
    /// <summary>
    /// Helpers for building simple visuals from primitives at runtime, so the game loop
    /// needs no hand-wired prefabs or scene edits.
    /// </summary>
    public static class RuntimeVisuals
    {
        private static Shader litShader;
        private static readonly Dictionary<(Color, Color), Material> Cache = new Dictionary<(Color, Color), Material>();

        private static Shader LitShader
        {
            get
            {
                if (litShader != null)
                    return litShader;

                litShader = Shader.Find("Standard");
                if (litShader == null)
                    litShader = Shader.Find("Legacy Shaders/Diffuse");
                if (litShader == null)
                {
                    // Last resort: borrow whatever shader the level already uses (guaranteed to be in the build).
                    var anyRenderer = Object.FindFirstObjectByType<MeshRenderer>();
                    if (anyRenderer != null && anyRenderer.sharedMaterial != null)
                        litShader = anyRenderer.sharedMaterial.shader;
                }
                return litShader;
            }
        }

        public static Material Mat(Color color) => Mat(color, Color.black);

        public static Material Mat(Color color, Color emission)
        {
            if (Cache.TryGetValue((color, emission), out Material cached) && cached != null)
                return cached;

            var mat = new Material(LitShader) { color = color };
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0.15f);
            if (emission != Color.black && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
            }
            Cache[(color, emission)] = mat;
            return mat;
        }

        public static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!keepCollider)
                // Immediate, so callers that gather colliders right after building never see a dying one.
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
