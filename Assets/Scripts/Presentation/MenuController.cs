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
        Toggle mute;
        readonly Button[] speedButtons = new Button[GameSettings.Speeds.Length];
        readonly Button[] battleButtons = new Button[GameSettings.BattleSceneNames.Length];
        readonly Button[] aiTurnButtons = new Button[GameSettings.AiTurnNames.Length];
        readonly Button[] visualButtons = new Button[GameSettings.VisualStyleNames.Length];
        readonly Button[] needButtons = new Button[GameSettings.NeedIconNames.Length];
        readonly Button[] perfButtons = new Button[GameSettings.PerfOverlayNames.Length];
        readonly Button[] scaleButtons = new Button[GameSettings.UiScales.Length];
        readonly Button[] layoutButtons = new Button[3];
        readonly Button[] graphicsButtons = new Button[3];
        Button beltIconsShow, beltIconsHide, twistBoard, twistCamera, bottlenecksShow, bottlenecksHide;
        bool confirmingRestart;
        int homeIndex = -1; // the overlay's place among its siblings, restored after showing it over the title

        public Action RestartRequested;

        /// <summary>Saves and goes back to the title screen.</summary>

        public Action ReturnToMenuRequested;
        /// <summary>Online: 0 playing alone, 1 a player in an online match, 2 its host.</summary>
        public Func<int> OnlineRole;
        int Role { get { return OnlineRole != null ? OnlineRole() : 0; } }
        Button toMenuButton;
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
            if (overlay == null || status == null || restartButton == null || volume == null || pan == null || mute == null)
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
            // Back to the title screen (the game is saved and can be resumed from there).
            var toMenu = new Button(() =>
            {
                ResetRestart();
                Close();
                if (ReturnToMenuRequested != null) ReturnToMenuRequested();
            }) { name = "menu-title", text = "Return to menu" };
            toMenuButton = toMenu;
            toMenu.AddToClassList("tool");
            toMenu.AddToClassList("big");
            restartButton.parent.Insert(restartButton.parent.IndexOf(restartButton), toMenu);
            restartButton.clicked += () =>
            {
                if (!confirmingRestart)
                {
                    confirmingRestart = true;
                    restartButton.text = Role == 2 ? "Click again to end it" : "Click again to restart";
                    status.text = Role == 2 ? "Ends the match for everyone and takes all the players back to the lobby."
                        : "Restarting starts a new game and replaces your save.";
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
            for (int i = 0; i < needButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("needs-" + i);
                needButtons[i] = b;
                if (b != null) b.clicked += () => { settings.needIcons = mode; Apply(); Click(); };
            }
            for (int i = 0; i < perfButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("perf-" + i);
                perfButtons[i] = b;
                if (b != null) b.clicked += () => { settings.perfOverlay = mode; Apply(); Click(); };
            }
            for (int i = 0; i < scaleButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("uiscale-" + i);
                scaleButtons[i] = b;
                if (b != null) b.clicked += () => { settings.uiScale = mode; Apply(); Click(); };
            }
            for (int i = 0; i < layoutButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("layout-" + i);
                layoutButtons[i] = b;
                if (b != null) b.clicked += () => { settings.layout = mode; Apply(); Click(); };
            }
            for (int i = 0; i < graphicsButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("graphics-" + i);
                graphicsButtons[i] = b;
                if (b != null) b.clicked += () => { settings.graphics = mode; Apply(); Click(); };
            }
            twistBoard = root.Q<Button>("twist-board");
            twistCamera = root.Q<Button>("twist-camera");
            if (twistBoard != null) twistBoard.clicked += () => { settings.twistTurnsBoard = true; Apply(); Click(); };
            if (twistCamera != null) twistCamera.clicked += () => { settings.twistTurnsBoard = false; Apply(); Click(); };
            beltIconsShow = root.Q<Button>("belt-icons-show");
            beltIconsHide = root.Q<Button>("belt-icons-hide");
            if (beltIconsShow != null) beltIconsShow.clicked += () => { settings.beltItemIcons = true; Apply(); Click(); };
            if (beltIconsHide != null) beltIconsHide.clicked += () => { settings.beltItemIcons = false; Apply(); Click(); };
            bottlenecksShow = root.Q<Button>("bottlenecks-show");
            bottlenecksHide = root.Q<Button>("bottlenecks-hide");
            if (bottlenecksShow != null) bottlenecksShow.clicked += () => { settings.bottleneckMarkers = true; Apply(); Click(); };
            if (bottlenecksHide != null) bottlenecksHide.clicked += () => { settings.bottleneckMarkers = false; Apply(); Click(); };

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
            for (int i = 0; i < aiTurnButtons.Length; i++)
            {
                int mode = i;
                var b = root.Q<Button>("ai-turns-" + i);
                aiTurnButtons[i] = b;
                if (b != null) b.clicked += () => { settings.aiTurns = mode; Apply(); Click(); };
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
            for (int i = 0; i < aiTurnButtons.Length; i++)
                if (aiTurnButtons[i] != null) aiTurnButtons[i].EnableInClassList("choice-active", i == settings.aiTurns);
            for (int i = 0; i < visualButtons.Length; i++)
                if (visualButtons[i] != null) visualButtons[i].EnableInClassList("choice-active", i == settings.visualStyle);
            for (int i = 0; i < needButtons.Length; i++)
                if (needButtons[i] != null) needButtons[i].EnableInClassList("choice-active", i == settings.needIcons);
            for (int i = 0; i < perfButtons.Length; i++)
                if (perfButtons[i] != null) perfButtons[i].EnableInClassList("choice-active", i == settings.perfOverlay);
            for (int i = 0; i < scaleButtons.Length; i++)
                if (scaleButtons[i] != null) scaleButtons[i].EnableInClassList("choice-active", i == settings.uiScale);
            for (int i = 0; i < layoutButtons.Length; i++)
                if (layoutButtons[i] != null) layoutButtons[i].EnableInClassList("choice-active", i == settings.layout);
            for (int i = 0; i < graphicsButtons.Length; i++)
                if (graphicsButtons[i] != null) graphicsButtons[i].EnableInClassList("choice-active", i == settings.graphics);
            if (beltIconsShow != null) beltIconsShow.EnableInClassList("choice-active", settings.beltItemIcons);
            if (twistBoard != null) twistBoard.EnableInClassList("choice-active", settings.twistTurnsBoard);
            if (twistCamera != null) twistCamera.EnableInClassList("choice-active", !settings.twistTurnsBoard);
            if (beltIconsHide != null) beltIconsHide.EnableInClassList("choice-active", !settings.beltItemIcons);
            if (bottlenecksShow != null) bottlenecksShow.EnableInClassList("choice-active", settings.bottleneckMarkers);
            if (bottlenecksHide != null) bottlenecksHide.EnableInClassList("choice-active", !settings.bottleneckMarkers);
            if (visualNote != null) visualNote.text = VisualNote != null ? VisualNote() : "";
            if (speedLabel != null) speedLabel.text = settings.gameSpeed > 1 ? "Speed " + settings.gameSpeed + "x" : "";
        }

        void ResetRestart()
        {
            confirmingRestart = false;
            restartButton.text = Role == 2 ? "End the match for everyone" : "Restart game";
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
            // Online there is no save here, and only the host may end the match (back to the lobby for everyone).
            int role = Role;
            saveButton.style.display = game && role == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            restartButton.style.display = game && role != 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (toMenuButton != null) toMenuButton.text = role > 0 ? "Leave the match" : "Return to menu";
            if (helpButton != null) helpButton.style.display = shown;
            resumeButton.text = game ? "Resume" : "Close";
            if (title != null) title.text = !game ? "Settings" : role > 0 ? "Menu (the match plays on)" : "Paused";
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }
    }
}
