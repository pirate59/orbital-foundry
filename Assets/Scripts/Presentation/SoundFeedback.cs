using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Procedurally generated sound effects (no audio assets). Plays feedback for accepted and rejected commands and
    /// watches the world for sales, alerts and launches. Purely presentational: it never changes game state.
    /// </summary>
    public sealed class SoundFeedback
    {
        const int SampleRate = 22050;

        readonly AudioSource source;
        readonly GameSettings settings;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        int lastSales = -1;
        int lastAlertId = int.MaxValue;
        WorldState watched;

        public SoundFeedback(GameObject host, GameSettings settings)
        {
            this.settings = settings;
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            clips["place"] = Make(0.09f, t => Tone(t, 520f, 780f, 0.09f) * Env(t, 0.09f));
            clips["belt"] = Make(0.05f, t => Tone(t, 900f, 900f, 0.05f) * Env(t, 0.05f) * 0.5f);
            clips["remove"] = Make(0.12f, t => Tone(t, 420f, 180f, 0.12f) * Env(t, 0.12f));
            clips["sell"] = Make(0.18f, t => (Tone(t, 1320f, 1320f, 0.18f) * 0.6f + Tone(t, 1980f, 1980f, 0.18f) * 0.4f) * Env(t, 0.18f) * 0.6f);
            clips["error"] = Make(0.2f, t => Square(t, 140f) * Env(t, 0.2f) * 0.5f);
            clips["alert"] = Make(0.36f, t => Tone(t, t < 0.18f ? 660f : 880f, t < 0.18f ? 660f : 880f, 0.36f) * Env(t % 0.18f, 0.18f));
            clips["claim"] = Make(0.4f, t => Tone(t, 440f, 990f, 0.4f) * Env(t, 0.4f));
            clips["hit"] = Make(0.16f, t => Noise(t) * Env(t, 0.16f) * 0.8f);
            clips["zap"] = Make(0.12f, t => Tone(t, 1600f, 500f, 0.12f) * Env(t, 0.12f) * 0.35f);
            clips["boom"] = Make(0.45f, t => (Noise(t) * 0.7f + Tone(t, 120f, 50f, 0.45f) * 0.5f) * Env(t, 0.45f));
            clips["click"] = Make(0.04f, t => Tone(t, 1200f, 1000f, 0.04f) * Env(t, 0.04f) * 0.5f);
        }

        public void Play(string name, float volume = 1f, float minInterval = 0.05f)
        {
            AudioClip clip;
            if (!clips.TryGetValue(name, out clip)) return;
            float v = settings.EffectiveVolume * volume;
            if (v <= 0.001f) return;
            float last;
            if (lastPlayed.TryGetValue(name, out last) && Time.unscaledTime - last < minInterval) return;
            lastPlayed[name] = Time.unscaledTime;
            source.PlayOneShot(clip, v);
        }

        /// <summary>Feedback for a player command the HUD or world input just submitted.</summary>
        public void OnCommand(GameCommand cmd, CommandResult r)
        {
            if (cmd == null || r == null || cmd.actor != Perspective.Viewer) return;
            if (!r.success) { Play("error", 0.6f, 0.25f); return; }
            switch (cmd.kind)
            {
                case CommandKind.PlaceBuilding: Play("place"); break;
                case CommandKind.PlaceBelt: Play("belt", 0.7f, 0.06f); break;
                case CommandKind.Dismantle: Play("remove", 1f, 0.06f); break;
                case CommandKind.TacticalAttack: Play("hit"); break;
                case CommandKind.StartCapture: Play("claim", 0.6f); break;
                default: Play("click", 0.8f); break;
            }
        }

        /// <summary>Watches the world for events worth a sound: sales, alerts and launches.</summary>
        public void Watch(WorldState w)
        {
            if (w == null) return;
            var player = WorldLookup.GetFaction(w, Perspective.Viewer);
            if (w != watched)
            {
                watched = w;
                lastSales = player.cumulativeSales;
                lastAlertId = w.alerts.Count > 0 ? w.alerts[w.alerts.Count - 1].id : -1;
                return;
            }
            if (player.cumulativeSales > lastSales)
            {
                // Big sales chime louder; raw ore trickles stay quiet.
                float loud = Mathf.Clamp01((player.cumulativeSales - lastSales) / 25f);
                Play("sell", 0.25f + 0.75f * loud, 0.35f);
            }
            lastSales = player.cumulativeSales;
            foreach (var a in w.alerts)
            {
                if (a.id <= lastAlertId) continue;
                lastAlertId = a.id;
                if (a.to != FactionId.Neutral && a.to != Perspective.Viewer) continue; // another seat's news
                switch (a.kind)
                {
                    case AlertKind.Raid:
                    case AlertKind.Defeat:
                    case AlertKind.Territory:
                        Play("alert", 1f, 0.5f);
                        break;
                    case AlertKind.Victory:
                        Play("claim", 1f, 0.5f);
                        break;
                    case AlertKind.SaveError:
                        Play("error", 0.6f, 1f);
                        break;
                    default:
                        if (a.message.Contains("claimed") || a.message.Contains("launched")) Play("claim", 0.8f, 0.3f);
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- synthesis

        static AudioClip Make(float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave((float)i / SampleRate), -1f, 1f) * 0.6f;
            var clip = AudioClip.Create("sfx", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Sine that glides linearly from fromHz to toHz over <paramref name="length"/> seconds.</summary>
        static float Tone(float t, float fromHz, float toHz, float length)
        {
            float k = (toHz - fromHz) / Mathf.Max(0.001f, length);
            return Mathf.Sin(2f * Mathf.PI * (fromHz * t + 0.5f * k * t * t));
        }

        static float Square(float t, float hz) { return Mathf.Sin(2f * Mathf.PI * hz * t) >= 0 ? 0.6f : -0.6f; }

        static float Noise(float t)
        {
            int i = (int)(t * SampleRate);
            uint x = (uint)(i * 747796405 + 2891336453);
            x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
            return ((x >> 22) ^ x) / (float)uint.MaxValue * 2f - 1f;
        }

        static float Env(float t, float length)
        {
            float attack = Mathf.Clamp01(t / 0.005f);
            float release = Mathf.Clamp01(1f - t / length);
            return attack * release * release;
        }
    }
}
