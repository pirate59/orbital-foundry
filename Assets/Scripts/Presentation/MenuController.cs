using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The pause menu: resume, save, restart (with confirmation) and player settings. Opening it pauses the game.
    /// From the title screen it opens as a plain Settings window (<see cref="OpenSettings"/>).
    /// Settings changes are applied through <see cref="SettingsChanged"/> and saved to PlayerPrefs.
    /// </summary>
    public sealed class MenuController
    {
        readonly GameSettings settings;
        VisualElement overlay;
        Label status, speedLabel, title, visualNote;
        Button restartButton, resumeButton, saveButton, helpButton;
        Slider volume, pan;
        Toggle mute, needIcons;
        readonly Button[] speedButtons = new Button[GameSettings.Speeds.Length];
        readonly Button[] battleButtons = new Button[GameSettings.BattleSceneNames.Length];
        readonly Button[] visualButtons = new Button[GameSettings.VisualStyleNames.Length];
        bool confirmingRestart;
        int homeIndex = -1; // the overlay's place among its siblings, restored after showing it over the title

        public Action RestartRequested;
        public Func<CommandResult> SaveRequested;
        public Action SettingsChanged;
        public Action<string> Feedback;
        /// <summary>A line under the visual style buttons, e.g. how many Meshy models there are.</summary>
        public Func<string> VisualNote;

        public bool IsOpen { get; private set; }

        public MenuController(GameSettings settings)
        {
            this.settings = settings;
        }

        public void Initialize(VisualElement root)
        {
            overlay = root.Q<VisualElement>("menu-overlay");
            status = root.Q<Label>("menu-status");
            speedLabel = root.Q<Label>("speed-label");
            restartButton = root.Q<Button>("menu-restart");
            volume = root.Q<Slider>("sfx-volume");
            pan = root.Q<Slider>("pan-speed");
            mute = root.Q<Toggle>("mute-toggle");
            needIcons = root.Q<Toggle>("need-icons-toggle");
            if (overlay == null || status == null || restartButton == null || volume == null || pan == null || mute == null || needIcons == null)
            {
                Debug.LogError("Menu UI elements are missing from Main.uxml.");
                return;
            }

            title = root.Q<Label>("menu-title");
            visualNote = root.Q<Label>("visual-note");
            resumeButton = root.Q<Button>("menu-resume");
            saveButton = root.Q<Button>("menu-save");
            helpButton = root.Q<Button>("help-index");
            root.Q<Button>("menu-button").clicked += Open;
            resumeButton.clicked += Close;
            var titleSettings = root.Q<Button>("title-settings");
            if (titleSettings != null) titleSettings.clicked += OpenSettings;
            saveButton.clicked += () =>
            {
                var r = SaveRequested != null ? SaveRequested() : null;
                status.text = r == null ? "Nothing to save yet." : r.success ? "Game saved." : r.message;
                Click();
            };
            restartButton.clicked += () =>
            {
                if (!confirmingRestart)
                {
                    confirmingRestart = true;
                    restartButton.text = "Click again to restart";
                    status.text = "Restarting starts a new game and replaces your save.";
                    Click();
                    return;
                }
                ResetRestart();
                Close();
                if (RestartRequested != null) RestartRequested();
            };

            volume.value = settings.sfxVolume;
            volume.RegisterValueChangedCallback(e => { settings.sfxVolume = e.newValue; Apply(); });
            volume.RegisterCallback<PointerUpEvent>(_ => Click());
            mute.value = settings.muted;
            mute.RegisterValueChangedCallback(e => { settings.muted = e.newValue; Apply(); });
            pan.value = settings.panSpeed;
            pan.RegisterValueChangedCallback(e => { settings.panSpeed = e.newValue; Apply(); });
            needIcons.value = settings.showNeedIcons;
            needIcons.RegisterValueChangedCallback(e => { settings.showNeedIcons = e.newValue; Apply(); });

            for (int i = 0; i < GameSettings.Speeds.Length; i++)
            {
                int speed = GameSettings.Speeds[i];
                var b = root.Q<Button>("speed-" + speed);
                speedButtons[i] = b;
                if (b != null) b.clicked += () => { settings.gameSpeed = speed; Apply(); Click(); };
            }
            for (int i = 0; i < battleButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("battle-" + i);
                battleButtons[i] = b;
                if (b != null) b.clicked += () => { settings.battleScenes = mode; Apply(); Click(); };
            }
            for (int i = 0; i < visualButtons.Length; i++)
            {
                int style = i;
                var b = root.Q<Button>("visual-" + i);
                visualButtons[i] = b;
                if (b != null) b.clicked += () => { settings.visualStyle = style; Apply(); Click(); };
            }
            overlay.style.display = DisplayStyle.None;
            homeIndex = overlay.parent != null ? overlay.parent.IndexOf(overlay) : -1;
            RefreshSpeed();
        }

        void Apply()
        {
            settings.Save();
            RefreshSpeed();
            if (SettingsChanged != null) SettingsChanged();
        }

        void Click()
        {
            if (Feedback != null) Feedback("click");
        }

        void RefreshSpeed()
        {
            for (int i = 0; i < speedButtons.Length; i++)
                if (speedButtons[i] != null) speedButtons[i].EnableInClassList("choice-active", GameSettings.Speeds[i] == settings.gameSpeed);
            for (int i = 0; i < battleButtons.Length; i++)
                if (battleButtons[i] != null) battleButtons[i].EnableInClassList("choice-active", i == settings.battleScenes);
            for (int i = 0; i < visualButtons.Length; i++)
                if (visualButtons[i] != null) visualButtons[i].EnableInClassList("choice-active", i == settings.visualStyle);
            if (visualNote != null) visualNote.text = VisualNote != null ? VisualNote() : "";
            if (speedLabel != null) speedLabel.text = settings.gameSpeed > 1 ? "Speed " + settings.gameSpeed + "x" : "";
        }

        void ResetRestart()
        {
            confirmingRestart = false;
            restartButton.text = "Restart game";
        }

        public void Open()
        {
            if (overlay == null) return;
            ShowGameButtons(true);
            RefreshSpeed();
            IsOpen = true;
            status.text = "";
            ResetRestart();
            overlay.style.display = DisplayStyle.Flex;
            Click();
        }

        public void Close()
        {
            if (overlay == null) return;
            IsOpen = false;
            ResetRestart();
            overlay.style.display = DisplayStyle.None;
            if (homeIndex >= 0 && overlay.parent != null && overlay.parent.IndexOf(overlay) != homeIndex)
                overlay.parent.Insert(homeIndex, overlay);
        }

        /// <summary>The settings alone, over the title screen: no save, restart or resume.</summary>
        public void OpenSettings()
        {
            if (overlay == null) return;
            ShowGameButtons(false);
            IsOpen = true;
            status.text = "";
            RefreshSpeed();
            overlay.style.display = DisplayStyle.Flex;
            overlay.BringToFront();
            Click();
        }

        void ShowGameButtons(bool game)
        {
            var shown = game ? DisplayStyle.Flex : DisplayStyle.None;
            saveButton.style.display = shown;
            restartButton.style.display = shown;
            if (helpButton != null) helpButton.style.display = shown;
            resumeButton.text = game ? "Resume" : "Close";
            if (title != null) title.text = game ? "Paused" : "Settings";
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }
    }
}
