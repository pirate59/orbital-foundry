using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OrbitalFoundry
{
    /// <summary>
    /// Lighting and grading for the 3D board: a warm low sun with soft shadows, a cool teal fill, the retro sky, and
    /// a post stack (bloom, filmic tonemapping, a slight warm grade and vignette) for the 1950s pulp-cover look.
    /// </summary>
    public static class RenderRig
    {
        public static readonly Vector3 SunEuler = new Vector3(42f, -12f, 0f);

        public static Light Build(Camera camera)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(SunEuler);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.76f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;

            var fillGo = new GameObject("Rim Light");
            fillGo.transform.rotation = Quaternion.Euler(24f, 150f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.45f, 0.78f, 0.95f);
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;

            // A faint violet bounce from below so the undersides of platforms and asteroids read against space.
            var bounceGo = new GameObject("Bounce Light");
            bounceGo.transform.rotation = Quaternion.Euler(-55f, 200f, 0f);
            var bounce = bounceGo.AddComponent<Light>();
            bounce.type = LightType.Directional;
            bounce.color = new Color(0.55f, 0.42f, 0.78f);
            bounce.intensity = 0.55f;
            bounce.shadows = LightShadows.None;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.42f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.28f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.14f);
            RenderSettings.sun = sun;

            var sky = Resources.Load<Material>("Materials/RetroSky");
            if (sky != null)
            {
                sky = new Material(sky);
                sky.SetVector("_SunDir", -sunGo.transform.forward);
                RenderSettings.skybox = sky;
                camera.clearFlags = CameraClearFlags.Skybox;
            }

            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // MSAA on the pipeline asset handles edges
            data.renderShadows = true;

            var volumeGo = new GameObject("Post Processing");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.75f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.7f));
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.35f);
            grade.contrast.Override(8f);
            grade.saturation.Override(6f);
            grade.colorFilter.Override(new Color(1f, 0.97f, 0.92f));
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.05f, 0.02f, 0.06f));
            volume.sharedProfile = profile;
            return sun;
        }
    }
}
