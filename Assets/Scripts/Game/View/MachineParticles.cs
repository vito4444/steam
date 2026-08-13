using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Builds the small particle effects that sit on working machines.
    ///
    /// Sawdust off a blade and steam off a lathe are the cheapest possible signal that
    /// a machine is doing something, and unlike a progress bar they are visible from
    /// anywhere on the floor without being read. Both are gated on the machine actually
    /// working, so a stalled bench is silent as well as dim.
    ///
    /// Particles are rendered as tiny meshes rather than billboards because there is no
    /// particle texture in the project and a flat white quad would look worse than a
    /// chip of wood.
    /// </summary>
    public static class MachineParticles
    {
        /// <summary>Chips thrown off a cutting tool: fast, short lived, falling.</summary>
        public static ParticleSystem AttachSawdust(Transform parent, Vector3 localPosition, Material material)
        {
            var system = CreateSystem("Sawdust", parent, localPosition);

            var main = system.main;
            main.startLifetime = 0.7f;
            main.startSpeed = 2.4f;
            main.startSize = 0.055f;
            main.gravityModifier = 2.2f;
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);

            var emission = system.emission;
            emission.rateOverTime = 34f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.04f;
            shape.rotation = new Vector3(-70f, 0f, 0f);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

            ConfigureRenderer(system, material, 0.05f);
            return system;
        }

        /// <summary>Warm exhaust: slow, rising, fading.</summary>
        public static ParticleSystem AttachSteam(Transform parent, Vector3 localPosition, Material material)
        {
            var system = CreateSystem("Steam", parent, localPosition);

            var main = system.main;
            main.startLifetime = 2.1f;
            main.startSpeed = 0.55f;
            main.startSize = 0.14f;
            main.gravityModifier = -0.12f;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.rateOverTime = 7f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.06f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Puffs grow and thin out as they rise, which is what separates steam from
            // a stream of white dots.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.45f, 1f, 1.6f));

            ConfigureRenderer(system, material, 0.12f);
            return system;
        }

        private static ParticleSystem CreateSystem(string name, Transform parent, Vector3 localPosition)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;

            var system = holder.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private static void ConfigureRenderer(ParticleSystem system, Material material, float meshSize)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = ProceduralMesh.BeveledBox(0.12f);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Particles are numerous and tiny; alignment work on them is wasted.
            renderer.alignment = ParticleSystemRenderSpace.World;
        }

        /// <summary>Starts or stops emission without clearing particles already in flight.</summary>
        public static void SetEmitting(ParticleSystem system, bool emitting)
        {
            if (system == null) return;

            var emission = system.emission;
            if (emission.enabled == emitting) return;

            emission.enabled = emitting;

            if (emitting && !system.isPlaying) system.Play();
            else if (!emitting && system.isPlaying) system.Play();
        }
    }
}
