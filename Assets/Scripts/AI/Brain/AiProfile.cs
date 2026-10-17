using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// How the AI plays: which skills it uses, how bold it is, how fast it reacts, how much it sees and how hard it
    /// thinks. Every skill reads its settings from here, so a difficulty is one profile. The presets live in
    /// Resources/AI/ai_profiles.json (easy, medium, hard); the headless arena can give each side any profile, or a
    /// preset with some values changed. Whether it may expand or raid at all is decided by the rules (aiExpands,
    /// aiRaids, aiDefends); the profile decides how it goes about it.
    /// </summary>
    [Serializable]
    public sealed class AiProfile
    {
        /// <summary>Preset or arena id ("easy", "medium", "hard", or the arena's own).</summary>
        public string Id = "";
        public string Name = "";
        /// <summary>
        /// Which skills it uses: 0 the basics only (Easy), 1 the standard set (Medium), 2 everything, at its
        /// sharpest (Hard). Kept apart from the weights below so they can be tuned without switching skills on or off.
        /// </summary>
        public int Level = 1;
        /// <summary>0 to 1: how keen it is to attack and to expand towards its opponent (strike timing, claims at once, risk taken).</summary>
        public float Aggression;
        /// <summary>0 to 1: how long the odds it accepts. An attack needs strength of at least <see cref="NeededEase"/> times the defence.</summary>
        public float RiskTolerance;
        /// <summary>Ticks a new situation must last before it changes stance (Defend still reacts at once).</summary>
        public int ReactionTicks;
        /// <summary>Scales the planner's work per think (rules.aiWorkPerThink).</summary>
        public float WorkScale = 1f;
        /// <summary>How many thinks between threat assessments.</summary>
        public int AssessEveryThinks = 3;
        /// <summary>Its strike fleet in a military game, on top of one ship on guard per cell it holds.</summary>
        public int MaxCraft = 6;
        /// <summary>How many cells beyond its own it notices enemy fleets in (99: everywhere).</summary>
        public int SightCells = 2;
        /// <summary>Whether it reads its opponent's economy and trade-win progress.</summary>
        public bool SeesPlayerEconomy;
        /// <summary>Share of the opponent's trade target at which it switches to countering the win (above 1: never).</summary>
        public float CounterAt = 2f;
        /// <summary>No attack or raid before this tick: a grace period to get going.</summary>
        public int EarliestAttackTick = int.MaxValue;
        /// <summary>Research levels it chooses to keep queued (the rule allows either side ResearchService.MaxQueue).</summary>
        public int ResearchQueue = 1;
        /// <summary>Multiplies every skill's interval (2: half as often, cheaper; 0.5: twice as often).</summary>
        public float CadenceScale = 1f;
        /// <summary>Skills (by name, as AiTiming lists them) this profile leaves out, for testing what each is worth.</summary>
        public List<string> DisabledSkills = new List<string>();

        /// <summary>Strength over defence an attack needs: 1.6 for a cautious AI, 1.0 for a bold one.</summary>
        public float NeededEase { get { return 1.6f - RiskTolerance; } }

        /// <summary>The preset for a difficulty.</summary>
        public static AiProfile For(Difficulty difficulty)
        {
            var preset = AiProfiles.Get(difficulty == Difficulty.Easy ? "easy" : difficulty == Difficulty.Hard ? "hard" : "medium");
            return preset ?? BuiltIn(difficulty);
        }

        /// <summary>
        /// The profile the AI code is playing with just now: its side's own (the arena's), else the one the match
        /// started with (WorldState.aiProfileId), else the match difficulty's preset.
        /// </summary>
        public static AiProfile Current(WorldState w)
        {
            if (AiSide.Profile != null) return AiSide.Profile;
            var chosen = string.IsNullOrEmpty(w.aiProfileId) ? null : AiProfiles.Get(w.aiProfileId);
            return chosen ?? For(w.difficulty);
        }

        public AiProfile Clone()
        {
            return JsonUtility.FromJson<AiProfile>(JsonUtility.ToJson(this));
        }

        /// <summary>The presets as they were before the profile file (used if it is missing).</summary>
        public static AiProfile BuiltIn(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    return new AiProfile
                    {
                        Id = "easy", Name = "Easy", Level = 0, Aggression = 0.15f, RiskTolerance = 0.2f, ReactionTicks = 300, WorkScale = 0.5f,
                        AssessEveryThinks = 6, MaxCraft = 3, SightCells = 1, SeesPlayerEconomy = false, CounterAt = 2f,
                        ResearchQueue = 1
                    };
                case Difficulty.Hard:
                    return new AiProfile
                    {
                        Id = "hard", Name = "Hard", Level = 2, Aggression = 0.75f, RiskTolerance = 0.6f, ReactionTicks = 50, WorkScale = 1.5f,
                        AssessEveryThinks = 2, MaxCraft = 9, SightCells = 99, SeesPlayerEconomy = true, CounterAt = 0.7f,
                        EarliestAttackTick = 3000, // five minutes
                        ResearchQueue = 3
                    };
                default:
                    return new AiProfile
                    {
                        Id = "medium", Name = "Medium", Level = 1, Aggression = 0.45f, RiskTolerance = 0.4f, ReactionTicks = 150, WorkScale = 1f,
                        AssessEveryThinks = 3, MaxCraft = 6, SightCells = 2, SeesPlayerEconomy = true, CounterAt = 0.9f,
                        EarliestAttackTick = 6000, // ten minutes
                        ResearchQueue = 2
                    };
            }
        }
    }

    /// <summary>
    /// The AI profile presets, read once from Resources/AI/ai_profiles.json. A profile may name a "Base" preset and
    /// give only the values it changes; the arena reads its own profiles the same way (<see cref="Parse"/>).
    /// </summary>
    public static class AiProfiles
    {
        static Dictionary<string, AiProfile> presets;

        public static AiProfile Get(string id)
        {
            if (presets == null)
            {
                presets = new Dictionary<string, AiProfile>();
                var asset = Resources.Load<TextAsset>("AI/ai_profiles");
                if (asset != null) foreach (var p in Parse(asset.text, null)) presets[p.Id] = p;
            }
            AiProfile found;
            return id != null && presets.TryGetValue(id, out found) ? found : null;
        }

        /// <summary>All the presets, in the file's order of ids (easy, medium, hard).</summary>
        public static IEnumerable<string> PresetIds()
        {
            Get("");
            return presets.Keys;
        }

        [Serializable] sealed class BaseOnly { public string Base = ""; }

        /// <summary>
        /// Reads the "profiles" array of a JSON document. Each entry is a whole profile, or a "Base" preset id plus
        /// the values it changes. <paramref name="known"/> resolves bases defined earlier in the same file.
        /// </summary>
        public static List<AiProfile> Parse(string json, Dictionary<string, AiProfile> known)
        {
            var list = new List<AiProfile>();
            var local = known ?? new Dictionary<string, AiProfile>();
            foreach (var raw in JsonSlices.ArrayItems(json, "profiles"))
            {
                var b = JsonUtility.FromJson<BaseOnly>(raw);
                AiProfile basis = null;
                if (!string.IsNullOrEmpty(b.Base) && !local.TryGetValue(b.Base, out basis)) basis = Get(b.Base);
                if (!string.IsNullOrEmpty(b.Base) && basis == null) throw new ArgumentException("Unknown base profile '" + b.Base + "'.");
                var p = basis != null ? basis.Clone() : new AiProfile();
                JsonUtility.FromJsonOverwrite(raw, p);
                if (string.IsNullOrEmpty(p.Name)) p.Name = p.Id;
                local[p.Id] = p;
                list.Add(p);
            }
            return list;
        }
    }

    /// <summary>Just enough JSON splitting for documents JsonUtility cannot read whole: the raw items of a top-level array.</summary>
    public static class JsonSlices
    {
        /// <summary>The raw text of each object in the array under <paramref name="key"/>.</summary>
        public static List<string> ArrayItems(string json, string key)
        {
            var items = new List<string>();
            int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return items;
            int open = json.IndexOf('[', at);
            if (open < 0) return items;
            int depth = 0, start = -1;
            bool inString = false;
            for (int i = open + 1; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    if (ch == '\\') i++;
                    else if (ch == '"') inString = false;
                    continue;
                }
                if (ch == '"') inString = true;
                else if (ch == '{') { if (depth++ == 0) start = i; }
                else if (ch == '}') { if (--depth == 0 && start >= 0) { items.Add(json.Substring(start, i - start + 1)); start = -1; } }
                else if (ch == ']' && depth == 0) break;
            }
            return items;
        }
    }
}
