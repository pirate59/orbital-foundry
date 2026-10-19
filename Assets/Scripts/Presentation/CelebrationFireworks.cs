using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>One-time factory milestone fireworks. All randomness and particles are presentation-only.</summary>
    public sealed class CelebrationFireworks
    {
        const int FirstBar = 1, FirstRelay = 2, FirstSale = 4;

        sealed class Rocket
        {
            public Vector3 Start, End;
            public Color Color;
            public float At, Duration;
            public ParticleSystem Trail;
        }

        static readonly Color[] Colours =
        {
            Palette.Mint, Palette.Atomic, Palette.Mustard, Palette.Glass, Palette.Cherry, Palette.Ivory
        };

        readonly Transform root;
        readonly SoundFeedback sound;
        readonly System.Random random = new System.Random();
        readonly List<Rocket> rockets = new List<Rocket>();
        FactorySimulation factory;
        WorldState world;
        MapDefinition map;
        string saveKey;
        int fired;

        public CelebrationFireworks(GameObject host, SoundFeedback sound)
        {
            this.sound = sound;
            root = new GameObject("Milestone Fireworks").transform;
            root.SetParent(host.transform, false);
        }

        public void Attach(GameLoop loop)
        {
            Detach();
            world = loop.World;
            map = loop.Map;
            saveKey = "milestone_fireworks_" + world.mapId + "_" + (int)Perspective.Viewer;
            fired = PlayerPrefs.GetInt(saveKey, 0);
            factory = loop.Services.Factory;
            factory.ItemProduced += Produced;
            factory.ItemSold += Sold;
        }

        public void Detach()
        {
            if (factory != null)
            {
                factory.ItemProduced -= Produced;
                factory.ItemSold -= Sold;
            }
            factory = null;
            world = null;
            map = null;
            rockets.Clear();
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
        }

        void Produced(BuildingState building, string itemId)
        {
            if (building.owner != Perspective.Viewer || Perspective.Spectating) return;
            if (itemId == "iron_bar" && (fired & FirstBar) == 0)
                Celebrate(FirstBar, building, 2, 3, false);
            else if (itemId == "relay" && (fired & FirstRelay) == 0)
                Celebrate(FirstRelay, building, 3, 5, true);
        }

        void Sold(BuildingState building, string itemId)
        {
            if (building.owner == Perspective.Viewer && !Perspective.Spectating && itemId == "relay" && (fired & FirstSale) == 0)
                Celebrate(FirstSale, building, 4, 6, true);
        }

        void Celebrate(int milestone, BuildingState source, int minimum, int maximum, bool hqAnswer)
        {
            fired |= milestone;
            PlayerPrefs.SetInt(saveKey, fired);
            PlayerPrefs.Save();
            if (map == null || world == null) return;
            var at = WorldView.TileCenter(map, source.cellId, source.x, source.y) + Vector3.up * 1.8f;
            LaunchGroup(at, random.Next(minimum, maximum + 1), 0f);
            if (hqAnswer)
            {
                foreach (var building in world.buildings)
                {
                    if (building.owner != Perspective.Viewer || building.typeId != "headquarters") continue;
                    var hq = WorldView.TileCenter(map, building.cellId, building.x, building.y) + Vector3.up * 2.4f;
                    LaunchGroup(hq, random.Next(2, milestone == FirstSale ? 5 : 4), 0.25f);
                    break;
                }
            }
            sound.Play("firework", milestone == FirstBar ? 0.55f : 0.9f, 0.1f);
        }

        void LaunchGroup(Vector3 origin, int count, float delay)
        {
            for (int i = 0; i < count; i++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * 1.7f;
                float z = ((float)random.NextDouble() - 0.5f) * 1.7f;
                float height = 3.4f + (float)random.NextDouble() * 1.8f;
                rockets.Add(new Rocket
                {
                    Start = origin + new Vector3(x * 0.15f, 0f, z * 0.15f),
                    End = origin + new Vector3(x, height, z),
                    Color = Colours[random.Next(Colours.Length)],
                    At = Time.unscaledTime + delay + i * 0.14f + (float)random.NextDouble() * 0.08f,
                    Duration = 0.48f + (float)random.NextDouble() * 0.2f
                });
            }
        }

        public void Tick()
        {
            float now = Time.unscaledTime;
            for (int i = rockets.Count - 1; i >= 0; i--)
            {
                var rocket = rockets[i];
                if (now < rocket.At) continue;
                if (rocket.Trail == null) rocket.Trail = MakeTrail(rocket.Start, rocket.Color);
                float t = Mathf.Clamp01((now - rocket.At) / rocket.Duration);
                rocket.Trail.transform.position = Vector3.Lerp(rocket.Start, rocket.End, t);
                if (t < 1f) continue;
                Burst(rocket.End, rocket.Color);
                rocket.Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                UnityEngine.Object.Destroy(rocket.Trail.gameObject, 0.6f);
                rockets.RemoveAt(i);
            }
        }

        ParticleSystem MakeTrail(Vector3 at, Color color)
        {
            var go = new GameObject("Firework launch");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 0.23f;
            main.startSpeed = 0f;
            main.startSize = 0.13f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            var emission = ps.emission;
            emission.rateOverTime = 60f;
            var shape = ps.shape;
            shape.enabled = false;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = Fade();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Palette.Flat(Color.white, true);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void Burst(Vector3 at, Color color)
        {
            var go = new GameObject("Firework burst");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
            main.startColor = color;
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 44;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)random.Next(22, 37)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = Fade();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Palette.Flat(Color.white, true);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        static ParticleSystem.MinMaxGradient Fade()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.45f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }
    }
}
