using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Hunter.Rendering
{
    /// Injects the ray-marched volumetric pass just before post-processing so bloom picks
    /// up the light shafts.
    public class VolumetricFogFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Range(0f, 0.6f)] public float density = 0.075f;
            [Range(8, 96)] public int steps = 40;
            [Range(10f, 400f)] public float maxDistance = 110f;
            [Range(-0.95f, 0.95f)] public float anisotropy = 0.62f;

            public Color scatterColor = new Color(1f, 0.76f, 0.36f);
            [Range(0f, 8f)] public float intensity = 1.55f;

            public float heightBase = 0f;
            [Range(0.005f, 1f)] public float heightFalloff = 0.055f;
            [Range(0.005f, 0.5f)] public float noiseScale = 0.045f;
            [Range(0f, 1f)] public float noiseStrength = 0.65f;

            [Range(0f, 12f)] public float sunBoost = 3.4f;
            [Range(0f, 2f)] public float ambientFloor = 0.16f;
            [Range(0f, 4f)] public float pointLightGain = 0.9f;

            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public Settings settings = new();
        [SerializeField] Shader shader;

        Material _material;
        VolumetricFogPass _pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hunter/VolumetricFog");
            if (shader == null) return;

            _material = CoreUtils.CreateEngineMaterial(shader);
            _pass = new VolumetricFogPass(_material, settings)
            {
                renderPassEvent = settings.renderPassEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _material == null) return;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
        }

        class VolumetricFogPass : ScriptableRenderPass
        {
            static readonly int ParamsId = Shader.PropertyToID("_VolFogParams");
            static readonly int ColorId = Shader.PropertyToID("_VolFogColor");
            static readonly int HeightId = Shader.PropertyToID("_VolFogHeight");
            static readonly int BoostId = Shader.PropertyToID("_VolFogLightBoost");

            readonly Material _material;
            readonly Settings _settings;

            public VolumetricFogPass(Material material, Settings settings)
            {
                _material = material;
                _settings = settings;
            }

            void Apply()
            {
                _material.SetVector(ParamsId, new Vector4(
                    _settings.density, _settings.steps, _settings.maxDistance, _settings.anisotropy));
                _material.SetVector(ColorId, new Vector4(
                    _settings.scatterColor.r, _settings.scatterColor.g, _settings.scatterColor.b,
                    _settings.intensity));
                _material.SetVector(HeightId, new Vector4(
                    _settings.heightBase, _settings.heightFalloff,
                    _settings.noiseScale, _settings.noiseStrength));
                _material.SetVector(BoostId, new Vector4(
                    _settings.sunBoost, _settings.ambientFloor, _settings.pointLightGain, 0f));
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                if (resourceData.isActiveTargetBackBuffer) return;

                Apply();

                var descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;

                var destination = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, descriptor, "_VolumetricFogTarget", false);

                var source = resourceData.activeColorTexture;

                var blitParams = new RenderGraphUtils.BlitMaterialParameters(
                    source, destination, _material, 0);
                renderGraph.AddBlitPass(blitParams, "Hunter Volumetric Fog");

                resourceData.cameraColor = destination;
            }
        }
    }
}
