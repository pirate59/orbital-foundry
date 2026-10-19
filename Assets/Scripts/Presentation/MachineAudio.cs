using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>Quiet, local machine loops for buildings that can work right now.</summary>
    public sealed class MachineAudio
    {
        const int VoiceCount = 3;
        const float AudibleDistance = 90f;

        struct Candidate
        {
            public int Id;
            public AudioClip Clip;
            public float Score;
            public float Gain;
        }

        sealed class Voice
        {
            public AudioSource Source;
            public int BuildingId = -1;
            public float Target;
        }

        readonly GameSettings settings;
        readonly Voice[] voices = new Voice[VoiceCount];
        readonly List<Candidate> candidates = new List<Candidate>();
        readonly AudioClip drill, refinery, assembler, shipyard;
        float nextScan;

        public MachineAudio(GameObject host, GameSettings settings)
        {
            this.settings = settings;
            drill = Resources.Load<AudioClip>("Audio/DrillLoop");
            refinery = Resources.Load<AudioClip>("Audio/RefineryLoop");
            assembler = Resources.Load<AudioClip>("Audio/AssemblerLoop");
            shipyard = Resources.Load<AudioClip>("Audio/ShipyardLoop");
            if (drill == null || refinery == null || assembler == null || shipyard == null)
                Debug.LogWarning("One or more machine audio loops could not be loaded.");
            for (int i = 0; i < voices.Length; i++)
            {
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                voices[i] = new Voice { Source = source };
            }
        }

        public void Tick(WorldState world, ContentCatalog catalog, MapDefinition map, Camera camera, int selectedBuildingId)
        {
            if (world == null || catalog == null || map == null || camera == null || world.mode != MatchMode.Factory)
            {
                foreach (var voice in voices) voice.Target = 0f;
                nextScan = 0f;
            }
            else if (Time.unscaledTime >= nextScan)
            {
                Scan(world, catalog, map, camera, selectedBuildingId);
                nextScan = Time.unscaledTime + 0.2f;
            }

            foreach (var voice in voices)
            {
                voice.Source.volume = Mathf.MoveTowards(voice.Source.volume, voice.Target, Time.unscaledDeltaTime * 1.5f);
                if (voice.Target == 0f && voice.Source.volume < 0.001f && voice.BuildingId != -1)
                {
                    voice.Source.Stop();
                    voice.BuildingId = -1;
                }
            }
        }

        void Scan(WorldState world, ContentCatalog catalog, MapDefinition map, Camera camera, int selectedBuildingId)
        {
            candidates.Clear();
            foreach (var building in world.buildings)
            {
                if (!WorldLookup.IsBuildingActive(world, building)) continue;
                var def = CatalogRepository.GetBuilding(catalog, building.typeId);
                var clip = def != null ? ClipFor(def.kind) : null;
                if (clip == null || !FactorySimulation.WantsToWork(world, catalog, building, def)) continue;
                var faction = WorldLookup.GetFaction(world, building.owner);
                float power = faction.powerDemand > 0 ? Mathf.Clamp01((float)faction.powerSupply / faction.powerDemand) : 1f;
                if (power <= 0f) continue;

                var position = WorldView.TileCenter(map, building.cellId, building.x, building.y);
                float distance = Vector3.Distance(camera.transform.position, position);
                bool selected = building.id == selectedBuildingId;
                if (!selected && distance > AudibleDistance) continue;
                var screen = camera.WorldToViewportPoint(position);
                if (!selected && (screen.z <= 0f || screen.x < -0.1f || screen.x > 1.1f || screen.y < -0.1f || screen.y > 1.1f)) continue;

                float gain = (selected ? 1f : 0.25f + 0.75f * (1f - distance / AudibleDistance)) * power;
                candidates.Add(new Candidate { Id = building.id, Clip = clip, Gain = gain, Score = gain + (selected ? 2f : 0f) });
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            int count = Mathf.Min(VoiceCount, candidates.Count);
            foreach (var voice in voices) voice.Target = 0f;
            for (int i = 0; i < count; i++)
            {
                var candidate = candidates[i];
                foreach (var voice in voices)
                {
                    if (voice.BuildingId != candidate.Id) continue;
                    voice.Target = Volume(candidate.Gain);
                    break;
                }
            }
            for (int i = 0; i < count; i++)
            {
                var candidate = candidates[i];
                bool assigned = false;
                foreach (var voice in voices) if (voice.BuildingId == candidate.Id) { assigned = true; break; }
                if (assigned) continue;
                foreach (var voice in voices)
                {
                    if (voice.Target > 0f) continue;
                    voice.Source.Stop();
                    voice.Source.clip = candidate.Clip;
                    voice.Source.pitch = 0.96f + (candidate.Id % 5) * 0.02f;
                    voice.Source.volume = 0f;
                    voice.Source.Play();
                    voice.BuildingId = candidate.Id;
                    voice.Target = Volume(candidate.Gain);
                    break;
                }
            }
        }

        float Volume(float gain) { return settings.EffectiveVolume * 0.38f * gain; }

        AudioClip ClipFor(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Extractor: return drill;
                case BuildingKind.Refinery: return refinery;
                case BuildingKind.Assembler: return assembler;
                case BuildingKind.Shipyard: return shipyard;
                default: return null;
            }
        }
    }
}
