using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Creates renderable objects from the generated meshes.
    ///
    /// Replaces <c>GameObject.CreatePrimitive</c> throughout the isometric view. Beyond
    /// the bevelled silhouette, this also skips the collider that CreatePrimitive
    /// attaches: nothing in the scene is ever raycast, since selection works off the
    /// simulation grid, and a few hundred unused colliders is pure waste.
    /// </summary>
    public static class MeshObjects
    {
        public static Transform Box(string name, Transform parent, Material material,
            Vector3 scale, Vector3 localPosition)
        {
            var holder = Create(name, parent, material, ProceduralMesh.BeveledBox());
            holder.localScale = scale;
            holder.localPosition = localPosition;
            return holder;
        }

        public static Transform Cylinder(string name, Transform parent, Material material,
            Vector3 scale, Vector3 localPosition)
        {
            var holder = Create(name, parent, material, ProceduralMesh.BeveledCylinder());
            holder.localScale = scale;
            holder.localPosition = localPosition;
            return holder;
        }

        public static Transform Box(string name, Transform parent, Material material)
            => Create(name, parent, material, ProceduralMesh.BeveledBox());

        public static Transform Cylinder(string name, Transform parent, Material material)
            => Create(name, parent, material, ProceduralMesh.BeveledCylinder());

        /// <summary>Sphere still comes from Unity: a smooth ball has no edges to chamfer.</summary>
        public static Transform Sphere(string name, Transform parent, Material material)
        {
            var holder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            holder.name = name;
            holder.transform.SetParent(parent, false);

            var collider = holder.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            if (material != null) holder.GetComponent<Renderer>().sharedMaterial = material;
            return holder.transform;
        }

        public static Transform Capsule(string name, Transform parent, Material material)
        {
            var holder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            holder.name = name;
            holder.transform.SetParent(parent, false);

            var collider = holder.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            if (material != null) holder.GetComponent<Renderer>().sharedMaterial = material;
            return holder.transform;
        }

        private static Transform Create(string name, Transform parent, Material material, Mesh mesh)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);

            holder.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = holder.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            return holder.transform;
        }
    }
}
