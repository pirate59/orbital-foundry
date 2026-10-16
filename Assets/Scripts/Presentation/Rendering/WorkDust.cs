using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Dust where a drill meets the ground: soft grey-brown puffs that rise and fade, and small dark rock chips that
    /// fly out and fall. Both stop emitting when the building stops (see <see cref="Model.Effects"/>).
    /// </summary>
    public static class WorkDust
    {
        static Material material;
        static bool looked;

        /// <summary>The two emitters at <paramref name="tip"/> under <paramref name="parent"/>, or null without the material.</summary>
        public static ParticleSystem[] Make(Transform parent, Vector3 tip, int tiles)
        {
            if (!looked) { looked = true; material = Resources.Load<Material>("Materials/Dust"); }
            if (material == null) return null;
            float scale = Mathf.Max(1, tiles);
            return new[]
            {
                Emitter("Dust", parent, tip, scale, rate: 14f, life: new Vector2(0.9f, 1.6f), speed: new Vector2(0.12f, 0.35f),
                    size: new Vector2(0.10f, 0.22f), gravity: -0.05f, angle: 70f,
                    a: new Color(0.62f, 0.55f, 0.48f, 0.55f), b: new Color(0.45f, 0.4f, 0.38f, 0.4f), grow: true),
                Emitter("Chips", parent, tip, scale, rate: 9f, life: new Vector2(0.4f, 0.8f), speed: new Vector2(0.5f, 1.0f),
                    size: new Vector2(0.025f, 0.05f), gravity: 1.6f, angle: 55f,
                    a: new Color(0.32f, 0.26f, 0.22f, 1f), b: new Color(0.5f, 0.42f, 0.34f, 1f), grow: false)
            };
        }

        /// <summary>
        /// White steam (or grey smoke) rising from a vent <paramref name="width"/> across at <paramref name="at"/>,
        /// swelling and fading; null without the material.
        /// </summary>
        public static ParticleSystem Steam(Transform parent, Vector3 at, float width, bool smoke = false)
        {
            if (!looked) { looked = true; material = Resources.Load<Material>("Materials/Dust"); }
            if (material == null) return null;
            float scale = Mathf.Max(0.05f, width) / 0.12f;
            var ps = smoke
                ? Emitter("Smoke", parent, at, scale, rate: 8f, life: new Vector2(2.4f, 3.6f), speed: new Vector2(0.06f, 0.12f),
                    size: new Vector2(0.07f, 0.12f), gravity: -0.02f, angle: 10f,
                    a: new Color(0.42f, 0.4f, 0.42f, 0.6f), b: new Color(0.6f, 0.58f, 0.6f, 0.45f), grow: true)
                : Emitter("Steam", parent, at, scale, rate: 10f, life: new Vector2(1.8f, 2.8f), speed: new Vector2(0.08f, 0.16f),
                    size: new Vector2(0.06f, 0.1f), gravity: -0.03f, angle: 12f,
                    a: new Color(0.95f, 0.96f, 1f, 0.55f), b: new Color(0.85f, 0.88f, 0.92f, 0.4f), grow: true);
            var shape = ps.shape;
            shape.radius = width * 0.35f;
            var main = ps.main;
            main.maxParticles = 50;
            return ps;
        }

        /// <summary>Welding sparks: tiny bright orange-white flecks thrown out fast, falling and fading; null without the material.</summary>
        public static ParticleSystem Sparks(Transform parent, Vector3 at)
        {
            if (!looked) { looked = true; material = Resources.Load<Material>("Materials/Dust"); }
            if (material == null) return null;
            var ps = Emitter("Sparks", parent, at, 1f, rate: 70f, life: new Vector2(0.18f, 0.45f), speed: new Vector2(0.4f, 1.1f),
                size: new Vector2(0.012f, 0.024f), gravity: 2.2f, angle: 80f,
                a: new Color(1f, 0.95f, 0.7f, 1f), b: new Color(1f, 0.6f, 0.15f, 1f), grow: false);
            var main = ps.main;
            main.maxParticles = 60;
            var shape = ps.shape;
            shape.radius = 0.005f;
            // Sparks stretch along their flight so they read as streaks.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.5f;
            return ps;
        }

        /// <summary>Teleport sparkles: bright motes drifting up through a tube <paramref name="radius"/> across, <paramref name="height"/> tall.</summary>
        public static ParticleSystem Teleport(Transform parent, Vector3 floor, float radius, float height, Color colour)
        {
            if (!looked) { looked = true; material = Resources.Load<Material>("Materials/Dust"); }
            if (material == null) return null;
            var ps = Emitter("Teleport", parent, floor, 1f, rate: 40f, life: new Vector2(0.5f, 1.0f), speed: new Vector2(height * 0.4f, height * 0.9f),
                size: new Vector2(0.012f, 0.03f), gravity: -0.1f, angle: 4f,
                a: new Color(1f, 1f, 1f, 1f), b: new Color(colour.r, colour.g, colour.b, 1f), grow: false);
            var shape = ps.shape;
            shape.radius = radius * 0.85f;
            var main = ps.main;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            return ps;
        }

        static ParticleSystem Emitter(string name, Transform parent, Vector3 tip, float scale, float rate, Vector2 life, Vector2 speed,
            Vector2 size, float gravity, float angle, Color a, Color b, bool grow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = tip;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // emit upward
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x * scale, speed.y * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x * scale, size.y * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.04f * scale;

            // Puffs swell and fade; chips just fade out at the end.
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, grow ? AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f) : AnimationCurve.Linear(0f, 1f, 1f, 0.6f));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }
    }
}
