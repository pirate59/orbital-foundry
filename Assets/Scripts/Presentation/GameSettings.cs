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
        public bool showNeedIcons = true;

        public static readonly int[] Speeds = { 1, 2, 4 };

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

        void Sanitise()
        {
            sfxVolume = Mathf.Clamp01(sfxVolume);
            panSpeed = Mathf.Clamp(panSpeed, 0.25f, 3f);
            if (Array.IndexOf(Speeds, gameSpeed) < 0) gameSpeed = 1;
        }
    }
}
