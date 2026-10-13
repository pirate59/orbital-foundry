using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The pause menu: resume, save, restart (with confirmation) and player settings. Opening it pauses the game.
    /// Settings changes are applied through <see cref="SettingsChanged"/> and saved to PlayerPrefs.
    /// </summary>
    public sealed class MenuController
    {
        readonly GameSettings settings;
        VisualElement overlay;
        Label status, speedLabel;
        Button restartButton;
        Slider volume, pan;
        Toggle mute, needIcons;
        readonly Button[] speedButtons = new Button[GameSettings.Speeds.Length];
        readonly Button[] battleButtons = new Button[GameSettings.BattleSceneNames.Length];
        bool confirmingRestart;

        public Action RestartRequested;
        public Func<CommandResult> SaveRequested;
        public Action SettingsChanged;
        public Action<string> Feedback;

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

            root.Q<Button>("menu-button").clicked += Open;
            root.Q<Button>("menu-resume").clicked += Close;
            root.Q<Button>("menu-save").clicked += () =>
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
            overlay.style.display = DisplayStyle.None;
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
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }
    }
}
