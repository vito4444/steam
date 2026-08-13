using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Worker.Game
{
    /// <summary>
    /// Builds the post-processing stack in code.
    ///
    /// This is the cheapest large improvement available to untextured geometry. Flat
    /// lit boxes look like plastic because nothing in the image has been through a
    /// camera: no exposure response, no lens falloff, no light bleeding off bright
    /// surfaces. Tonemapping and a restrained bloom supply exactly that, and cost
    /// nothing in modelling time.
    ///
    /// Values are deliberately conservative. Heavy bloom and crushed contrast are the
    /// standard way a hobby project announces itself; the goal here is for the image to
    /// look photographed rather than filtered.
    /// </summary>
    public static class PostProcessingRig
    {
        public static void Install(Camera camera, Transform parent)
        {
            if (camera == null) return;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = true;
                cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                cameraData.renderShadows = true;
            }

            var holder = new GameObject("PostProcessing");
            holder.transform.SetParent(parent, false);
            holder.layer = 0;

            var volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = BuildProfile();
        }

        private static VolumeProfile BuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;

            AddTonemapping(profile);
            AddColorAdjustments(profile);
            AddBloom(profile);
            AddVignette(profile);
            AddShadowsMidtonesHighlights(profile);

            return profile;
        }

        private static void AddTonemapping(VolumeProfile profile)
        {
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            // Neutral rather than ACES: ACES pushes saturated colours towards white, and
            // the whole readability scheme depends on each machine keeping its hue.
            tonemapping.mode.value = TonemappingMode.Neutral;
        }

        private static void AddColorAdjustments(VolumeProfile profile)
        {
            var color = profile.Add<ColorAdjustments>(true);

            color.postExposure.overrideState = true;
            // Lowered after the first pass blew out the middle of the floor: the floor
            // material had already been brightened for the 3D view, and exposure on top
            // of that pushed the centre of the image to near white.
            // Zero, not positive. Raising exposure to "compensate" for a dusk lighting
            // rig simply undoes it: the ambient reduction and the exposure lift cancel
            // and the image comes back out looking like noon with longer shadows.
            color.postExposure.value = 0.55f;

            color.contrast.overrideState = true;
            color.contrast.value = 11f;

            // The palette was authored for a flat renderer and reads grey once lit.
            color.saturation.overrideState = true;
            color.saturation.value = 30f;

            color.colorFilter.overrideState = true;
            color.colorFilter.value = new Color(1f, 0.985f, 0.955f);
        }

        private static void AddBloom(VolumeProfile profile)
        {
            var bloom = profile.Add<Bloom>(true);

            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.75f;

            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.9f;

            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;

            bloom.tint.overrideState = true;
            bloom.tint.value = new Color(1f, 0.96f, 0.88f);
        }

        private static void AddVignette(VolumeProfile profile)
        {
            var vignette = profile.Add<Vignette>(true);

            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.30f;

            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.55f;

            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.04f, 0.05f, 0.08f);
        }

        private static void AddShadowsMidtonesHighlights(VolumeProfile profile)
        {
            var grading = profile.Add<ShadowsMidtonesHighlights>(true);

            // Cool the shadows and warm the highlights. This is the oldest trick in
            // colour grading and it does more for a low-poly scene than extra polygons:
            // it separates lit from unlit by hue as well as by brightness.
            grading.shadows.overrideState = true;
            grading.shadows.value = new Vector4(0.88f, 0.94f, 1.12f, 0f);

            grading.midtones.overrideState = true;
            grading.midtones.value = new Vector4(1f, 1f, 1f, 0.02f);

            grading.highlights.overrideState = true;
            grading.highlights.value = new Vector4(1.08f, 1.02f, 0.92f, 0f);
        }
    }
}
