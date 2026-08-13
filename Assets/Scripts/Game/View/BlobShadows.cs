using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Painted ground shadows, offset along the key light direction.
    ///
    /// Real-time shadow mapping does not render at all under llvmpipe on this build
    /// machine. Two independent video reviews reported the scene as unlit, and a
    /// diagnostic build with every point light removed still produced none, while the
    /// player logged <c>shadows=Soft strength=1 distance=150</c>. The shadows were
    /// configured correctly and simply never appeared, which means the earlier stills
    /// that seemed to show them were showing unlit faces instead.
    ///
    /// Rather than depend on a feature that cannot be verified here, every object gets
    /// a dark quad on the floor. This is what most stylised isometric games do
    /// anyway: it costs one transparent quad per object, is stable under any renderer,
    /// and gives the contact cue that stops objects floating. Real shadows can be layered
    /// on top later on hardware that renders them.
    /// </summary>
    public static class BlobShadows
    {
        /// <summary>
        /// Ground direction the shadow is thrown, derived from the key light's yaw.
        /// The key sits at yaw -42, whose horizontal projection is this vector.
        /// </summary>
        public static Vector2 Direction = new Vector2(-0.669f, 0.743f);

        /// <summary>
        /// Shadow length as a multiple of object height: cot(pitch), and the key light
        /// sits at 24 degrees.
        ///
        /// The first attempt used 0.4, which put the blob almost entirely underneath the
        /// object that cast it. Nothing was visible and it looked like the quads were
        /// not being created at all; they were, and were hidden by the machine standing
        /// on top of them.
        /// </summary>
        /// Softened from the geometric 2.25 to 1.5. The true value is correct for a
        /// single hard sun, but an opaque quad at full length reads as a dirty smear
        /// under every object rather than as a shadow, and stacked props end up overlapping
        /// each other's blobs across the whole floor.
        public static float LengthPerHeight = 1.5f;

        public static Material CreateMaterial()
        {
            // Opaque, not transparent.
            //
            // The first version used a transparent unlit material and produced nothing
            // visible at all, on top of real shadows already producing nothing under
            // llvmpipe. Rather than debug two invisible things at once, the blob is now
            // an opaque quad in a colour darker than the floor. It cannot blend with what
            // is under it, which costs a little realism on textured ground, but it is
            // guaranteed to draw under any renderer and can be verified here.
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Standard");

            if (shader == null) return null;

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            // Lifted close to the floor tone. A review called the previous value "black
            // paper stuck to the ground": an opaque quad cannot blend, so the only lever
            // is to keep it near what it sits on and let the difference do the work.
            var color = new Color(0.37f, 0.36f, 0.37f);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);

            return material;
        }

        /// <summary>
        /// Adds a blob under an object of the given footprint. Height controls how far
        /// the blob is pushed and how much it spreads, so a tall wall throws further
        /// than a low shelf.
        /// </summary>
        public static Transform Attach(Transform parent, Material material, float width, float depth, float height)
        {
            if (material == null) return null;

            float reach = height * LengthPerHeight;

            // Stretched along the throw direction rather than scaled uniformly: a low
            // sun makes long shadows, not big ones.
            float alongX = Mathf.Abs(Direction.x) * reach;
            float alongZ = Mathf.Abs(Direction.y) * reach;

            var blob = MeshObjects.Box("Shadow", parent, material,
                new Vector3(width + alongX * 0.7f, 0.02f, depth + alongZ * 0.7f),
                new Vector3(Direction.x * reach * 0.5f, 0.035f, Direction.y * reach * 0.5f));

            // The blob must never cast or receive anything itself.
            var renderer = blob.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return blob;
        }
    }
}
