using System;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>Player preferences, kept in PlayerPrefs (browser storage on the Web build). Not part of the game save.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        const string PrefsKey = "OrbitalFoundry.Settings";

        public float sfxVolume = 0.7f;
        public bool muted;
        public int gameSpeed = 1;
        public float panSpeed = 1f;
        /// <summary>Older settings saved an on/off switch for machine needs; it seeds <see cref="needIcons"/> once.</summary>
        public bool showNeedIcons = true;
        /// <summary>When a machine's ingredient icons show above it: always, while the mouse is over it, or never.</summary>
        public int needIcons = -1;
        /// <summary>Whether goods riding the belts carry an icon above them naming the item.</summary>
        public bool beltItemIcons = true;
        /// <summary>Whether machines holding up their line (starved, backed up, short of power) are marked.</summary>
        public bool bottleneckMarkers = true;
        /// <summary>The performance readout in the corner: 0 off, 1 frame and tick rates, 2 with the costliest stages.</summary>
        public int perfOverlay = PerfRates;
        /// <summary>How big the interface is drawn: 0 automatic (bigger on small touch screens), else 100% to 175%.</summary>
        public int uiScale;
        /// <summary>0 automatic (compact on phones), 1 the full desktop layout, 2 the compact phone layout.</summary>
        public int layout;
        /// <summary>Graphics: 0 automatic (Low on phones), 1 High, 2 Low (one ship per fleet, no shadows or post effects).</summary>
        public int graphics;
        /// <summary>
        /// What a two-finger twist turns: true, the board, which follows your fingers like a map; false, the camera,
        /// so the board turns the other way.
        /// </summary>
        public bool twistTurnsBoard = true;
        public int battleScenes = BattleScenesFast;
        /// <summary>How the AI's battle turns play: watched move by move, quickly, or all at once without their scenes.</summary>
        public int aiTurns = AiTurnsWatch;
        /// <summary>Difficulty for new games: 0 Easy, 1 Medium, 2 Hard.</summary>
        public int difficulty = 1;
        /// <summary>0 Light weight (the built-in models), 1 Meshy (Meshy models where there are some).</summary>
        public int visualStyle = VisualLightWeight;

        public static readonly int[] Speeds = { 1, 2, 4 };
        public const int BattleScenesOff = 0, BattleScenesFast = 1, BattleScenesFull = 2;
        public const int AiTurnsWatch = 0, AiTurnsQuick = 1, AiTurnsInstant = 2;
        public static readonly string[] AiTurnNames = { "Watch", "Quick", "Instant" };
        public static readonly string[] BattleSceneNames = { "Off", "Fast", "Full" };
        public const int VisualLightWeight = 0, VisualMeshy = 1;
        public static readonly string[] VisualStyleNames = { "Light weight", "Meshy" };
        public const int NeedsAlways = 0, NeedsOnHover = 1, NeedsNever = 2;
        public const int PerfOff = 0, PerfRates = 1, PerfDetailed = 2;
        public static readonly float[] UiScales = { 0f, 1f, 1.25f, 1.5f, 1.75f };
        public static readonly string[] PerfOverlayNames = { "Off", "Rates", "Detailed" };
        public static readonly string[] NeedIconNames = { "Always", "On hover", "Never" };

        /// <summary>
        /// The interface scale to use. Automatic is 100% with a mouse; on a touch screen it grows as the screen's
        /// short side shrinks (a phone gets the most), so buttons stay big enough for a finger.
        /// </summary>
        public float InterfaceScale(bool touch, float shortSidePixels, float dpi)
        {
            if (uiScale > 0) return UiScales[uiScale];
            if (!touch) return 1f;
            float inches = dpi > 0f ? shortSidePixels / dpi : 0f;
            if (inches > 0f) return inches < 3.5f ? 1.75f : inches < 6f ? 1.5f : 1.25f;
            return shortSidePixels < 900f ? 1.75f : shortSidePixels < 1400f ? 1.5f : 1.25f;
        }

        /// <summary>
        /// Whether to use the compact layout: chosen outright, or automatically on a touch screen whose short side is
        /// under about five inches (a phone, not an iPad).
        /// </summary>
        public bool Compact(bool touch, float shortSidePixels, float dpi)
        {
            if (layout == 1) return false;
            if (layout == 2) return true;
            if (!touch) return false;
            float inches = dpi > 0f ? shortSidePixels / dpi : 0f;
            return inches > 0f ? inches < 5f : shortSidePixels < 1300f;
        }

        /// <summary>Whether to draw with Low graphics: chosen outright, or automatically with the compact phone layout.</summary>
        public bool LowGraphics(bool compactLayout)
        {
            return graphics == 2 || (graphics == 0 && compactLayout);
        }

        public float EffectiveVolume { get { return muted ? 0f : Mathf.Clamp01(sfxVolume); } }

        public static GameSettings Load()
        {
            try
            {
                if (PlayerPrefs.HasKey(PrefsKey))
                {
                    var s = JsonUtility.FromJson<GameSettings>(PlayerPrefs.GetString(PrefsKey));
                    if (s != null) { s.Sanitise(); return s; }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Settings could not be read; using defaults. " + e.Message);
            }
            return new GameSettings();
        }

        public void Save()
        {
            Sanitise();
            try
            {
                PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Settings could not be saved. " + e.Message);
            }
        }

        /// <summary>Holds every value in range, and moves an older on/off needs switch onto the needs mode.</summary>
        public void Sanitise()
        {
            sfxVolume = Mathf.Clamp01(sfxVolume);
            panSpeed = Mathf.Clamp(panSpeed, 0.25f, 3f);
            if (Array.IndexOf(Speeds, gameSpeed) < 0) gameSpeed = 1;
            if (battleScenes < BattleScenesOff || battleScenes > BattleScenesFull) battleScenes = BattleScenesFast;
            if (aiTurns < AiTurnsWatch || aiTurns > AiTurnsInstant) aiTurns = AiTurnsWatch;
            if (difficulty < 0 || difficulty > 2) difficulty = 1;
            if (visualStyle < VisualLightWeight || visualStyle > VisualMeshy) visualStyle = VisualLightWeight;
            if (needIcons < NeedsAlways || needIcons > NeedsNever) needIcons = showNeedIcons ? NeedsAlways : NeedsNever;
            showNeedIcons = needIcons != NeedsNever;
            if (perfOverlay < PerfOff || perfOverlay > PerfDetailed) perfOverlay = PerfRates;
            if (uiScale < 0 || uiScale >= UiScales.Length) uiScale = 0;
            if (layout < 0 || layout > 2) layout = 0;
            if (graphics < 0 || graphics > 2) graphics = 0;
        }
    }
}
