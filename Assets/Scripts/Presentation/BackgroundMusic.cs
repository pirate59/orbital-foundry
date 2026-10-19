using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>One continuous theme for the title screen and the game.</summary>
    public sealed class BackgroundMusic
    {
        readonly AudioSource source;
        readonly GameSettings settings;
        bool started;

        public BackgroundMusic(GameObject host, GameSettings settings)
        {
            this.settings = settings;
            var clip = Resources.Load<AudioClip>("Audio/OpeningTheme");
            if (clip == null)
            {
                Debug.LogWarning("Background music Audio/OpeningTheme could not be loaded.");
                return;
            }

            source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            ApplyVolume();

            // Browsers require a user gesture before audio can start. Desktop can play on the title screen at once.
            if (Application.platform != RuntimePlatform.WebGLPlayer) Start();
        }

        public void ApplyVolume()
        {
            if (source != null) source.volume = settings.muted ? 0f : Mathf.Clamp01(settings.musicVolume);
        }

        public void Tick()
        {
            if (started || source == null) return;
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.touchCount > 0)
                Start();
        }

        void Start()
        {
            if (source == null || started) return;
            source.Play();
            started = true;
        }
    }
}
