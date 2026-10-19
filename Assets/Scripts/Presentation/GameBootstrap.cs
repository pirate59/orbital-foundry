using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The only scene component. Loads content, recovers or creates the world, builds camera, view and HUD at runtime,
    /// and drives GameLoop each frame. It holds references only; all game state lives in WorldState.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const string NewGameMapId = "map_a";

        ContentCatalog catalog;
        MapDefinition map, pendingMap;
        SaveService saves;
        readonly GameLoop loop = new GameLoop();
        readonly UiState ui = new UiState();
        SpaceSpriteLibrary sprites;
        WorldView view;
        CameraInput cameraInput;
        HudController hud;
        WorldInput input;
        UIDocument document;
        PanelSettings panelSettings;
        Vector2Int baseResolution = new Vector2Int(1600, 900);
        float appliedScale = -1f;
        bool appliedTouch;

        int appliedGraphics = -1;

        /// <summary>Low or High graphics, applied when it changes: one ship per fleet, shadows, post effects, render scale.</summary>
        void ApplyGraphics(bool low)
        {
            int mode = low ? 1 : 0;
            if (mode == appliedGraphics) return;
            appliedGraphics = mode;
            Fleet.MaxShown = low ? 1 : 0;
            RenderRig.SetLowQuality(low);
        }

        /// <summary>Draws the interface at the chosen size: a smaller reference resolution makes everything bigger.</summary>
        void ApplyInterfaceScale()
        {
            if (panelSettings == null) return;
            float scale = settings.InterfaceScale(Pointer.TouchMode, Mathf.Min(Screen.width, Screen.height), Screen.dpi);
            // The compact layout is built for a phone already, so its automatic size is a little smaller.
            if (settings.uiScale == 0 && settings.Compact(Pointer.TouchMode, Mathf.Min(Screen.width, Screen.height), Screen.dpi)) scale = Mathf.Min(scale, 1.5f);
            if (Mathf.Approximately(scale, appliedScale) && appliedTouch == Pointer.TouchMode) return;
            appliedScale = scale;
            appliedTouch = Pointer.TouchMode;
            panelSettings.referenceResolution = new Vector2Int(Mathf.RoundToInt(baseResolution.x / scale), Mathf.RoundToInt(baseResolution.y / scale));
        }
        int shownRevision = -1;
        bool running;
        SeatSetupPanel seatSetup;
        OnlinePanel online;
        TitleMenu titleMenu;
        /// <summary>The giant turning ship behind the title screen.</summary>
        TitleShowcase showcase;

        /// <summary>The relay connection of an online match (null playing alone).</summary>
        RelayClient net;
        /// <summary>This player hosts the online match (may take everyone back to the lobby).</summary>
        bool onlineHost;

        /// <summary>0 playing alone, 1 a player in an online match, 2 its host.</summary>
        int OnlineRole() { return net == null || loop.Lockstep == null ? 0 : onlineHost ? 2 : 1; }
        int framedBattleId = -1;
        bool wasTactical;
        WorldState pendingLoaded;
        GameSettings settings;
        SoundFeedback sound;
        BackgroundMusic music;
        MenuController menu;
        HelpController help;
        BattleSceneController battle;
        ResearchController research;
        ResourceBarController resourceBar;
        /// <summary>The testing overlay: frames and ticks per second, and where the time goes. F3 cycles it.</summary>
        Label perfLabel;

        void Awake()
        {
            Application.targetFrameRate = 60;
            settings = GameSettings.Load();
            sound = new SoundFeedback(gameObject, settings);
            music = new BackgroundMusic(gameObject, settings);
            loop.CommandSubmitted += sound.OnCommand;
            sprites = new SpaceSpriteLibrary(); // the UI's title art and battle scenes draw from it
            try
            {
                SetUpUi();
            }
            catch (Exception e)
            {
                Debug.LogError("UI failed to initialise: " + e);
                return;
            }

            try
            {
                catalog = CatalogRepository.LoadContent();
                map = CatalogRepository.LoadMap(NewGameMapId, catalog);
                hud.TitleCatalog = catalog;
            }
            catch (CatalogException e)
            {
                hud.ShowFatal("Game content failed validation:\n" + e.Message);
                return;
            }

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                hud.ShowFatal("This browser does not provide the WebGL 2 graphics the game needs.");
                return;
            }

            string saveDir;
            try { saveDir = Application.persistentDataPath; }
            catch (Exception) { saveDir = ""; }
            saves = new SaveService(saveDir);

            var camGo = new GameObject("Orbit Camera");
            var camera = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            view = new WorldView(sprites, new GameObject("World").transform);
            view.LineOf = (w, beltId) => loop.Services.Belts.LineOf(w, beltId);
            cameraInput = new CameraInput(camera, WorldView.MapBounds(map), Vector3.zero) { Map = map };
            RenderRig.Build(camera);
            // The giant turning ship behind the title.
            showcase = new TitleShowcase(camera);
            showcase.TextureChanged = texture => titleMenu.SetBackdrop(texture);
            titleMenu.SetBackdrop(showcase.Show());

            // Offer Resume when a valid save exists; report unreadable saves without overwriting them.
            string message = "";
            if (saves.HasSave())
            {
                WorldState loaded;
                var r = saves.TryLoad(catalog, map, out loaded);
                if (r.success)
                {
                    pendingLoaded = loaded;
                    pendingMap = saves.LoadedMap;
                    if (r.code == "RECOVERED_BACKUP") message = r.message;
                }
                else message = "Your saved game could not be loaded (" + r.message + "). It has been kept untouched; starting a new game will replace it.";
            }
            // The title line-up uses the comic battle portraits (pixel art if the toon shader is unavailable).
            System.Func<string, FactionId, Sprite> art = (type, owner) => ComicCraft.Portrait(type, owner) ?? sprites.GetCraftSprite(type, owner);
            hud.SetTitleArt(
                new[] { art("bomber", FactionId.Player), art("guard", FactionId.Player), art("fortress", FactionId.Player), art("bomber", FactionId.Ai), art("guard", FactionId.Ai) },
                new[] { 110f, 105f, 170f, 115f, 105f });
            hud.ShowStartScreen(pendingLoaded != null, message);
        }

        void SetUpUi()
        {
            document = gameObject.AddComponent<UIDocument>();
            var panel = Resources.Load<PanelSettings>("UI/GamePanelSettings");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1600, 900);
                panel.match = 0.5f;
            }
            if (panel.themeStyleSheet == null) panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/GameTheme");
            var tree = Resources.Load<VisualTreeAsset>("UI/Main");
            var style = Resources.Load<StyleSheet>("UI/Main");
            if (tree == null || style == null) throw new InvalidOperationException("UI assets UI/Main.uxml or UI/Main.uss are missing from Resources.");
            panel = Instantiate(panel); // a copy: the interface size is changed at run time, never in the asset
            panelSettings = panel;
            baseResolution = panel.referenceResolution;
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            document.rootVisualElement.styleSheets.Add(style);
            perfLabel = new Label { name = "perf-overlay", pickingMode = PickingMode.Ignore };
            perfLabel.AddToClassList("perf-overlay");
            document.rootVisualElement.Add(perfLabel);
            help = new HelpController();
            help.Initialize(document.rootVisualElement);
            help.Sound = name => sound.Play(name);
            hud = new HudController();
            hud.Help = help;
            hud.Initialize(document, loop, ui);
            hud.NewGameRequested = StartNewGame;
            hud.OnlineRole = OnlineRole;
            hud.EndOnlineRequested = () => { if (OnlineRole() == 2) net.Rematch(); else ReturnToMenu(); };
            hud.TestGameRequested = StartTestGame;
            hud.ResumeRequested = ResumeGame;
            resourceBar = new ResourceBarController();
            resourceBar.Initialize(document.rootVisualElement, sprites);
            menu = new MenuController(settings);
            menu.Initialize(document.rootVisualElement);
            menu.RestartRequested = () => { if (OnlineRole() == 2) net.Rematch(); else StartNewGame(); };
            menu.OnlineRole = OnlineRole;
            menu.ReturnToMenuRequested = ReturnToMenu;
            seatSetup = new SeatSetupPanel(settings);
            seatSetup.Initialize(document.rootVisualElement);
            seatSetup.Changed = () => sound.Play("click");
            online = new OnlinePanel();
            online.Initialize(document.rootVisualElement);
            online.Click = () => sound.Play("click");
            online.Started = StartOnline;
            titleMenu = new TitleMenu();
            titleMenu.Initialize(document.rootVisualElement, online);
            titleMenu.Click = () => sound.Play("click");
            menu.SaveRequested = () => running ? loop.FlushSave() : null;
            menu.SettingsChanged = ApplySettings;
            menu.VisualNote = () =>
            {
                if (catalog == null) return "";
                int items = MeshyLibrary.CountItems(catalog), types = MeshyLibrary.CountBuildings(catalog);
                return items + types == 0 ? "Meshy: no Meshy models are installed yet, so everything keeps its light-weight look."
                    : "Meshy: models for " + items + " of " + catalog.items.Count + " items and " + types + " of " + catalog.buildings.Count
                      + " buildings; the rest keep their light-weight look.";
            };
            menu.Feedback = name => sound.Play(name);
            battle = new BattleSceneController(sprites, settings);
            battle.Initialize(document.rootVisualElement, loop);
            battle.Report = message => { if (hud != null) hud.ShowStatus(message, false); };
            battle.WatchedBattle = () => ui.watchBattleId;
            battle.Sound = name => sound.Play(name, 0.7f, 0.04f);
            research = new ResearchController();
            research.Initialize(document.rootVisualElement, loop);
            research.Sound = name => sound.Play(name);
            research.Feedback = r => hud.ShowStatus(r.message, !r.success);
            hud.ResearchRequested = () => research.Open();
            hud.NewGameDifficulty = () => settings.difficulty;
            hud.DifficultyChosen = level => { settings.difficulty = level; settings.Save(); };
        }

        void StartNewGame()
        {
            string problem;
            if (seatSetup != null && !seatSetup.Valid(out problem)) { hud.ShowStartScreen(saves.HasSave(), problem); return; }
            saves.AllowOverwrite();
            // Every new game gets a freshly generated Four Corners field with the chosen seats; the seats and seed live
            // in the map ID, so the save can rebuild it.
            var on = new bool[4];
            var seats = new List<SeatSetup>();
            for (int i = 0; i < 4; i++)
            {
                if (settings.seatMode[i] == GameSettings.SeatOff) continue;
                on[i] = true;
                bool ai = settings.seatMode[i] == GameSettings.SeatAi;
                seats.Add(new SeatSetup { seat = (FactionId)(i + 1), ai = ai, aiProfileId = ai ? settings.SeatProfile(i) : "", team = settings.seatTeam[i] });
            }
            map = CatalogRepository.LoadMap(MapGenerator.IdForSeats(on, UnityEngine.Random.Range(1, int.MaxValue)), catalog);
            var world = GameFactory.CreateNew(catalog, map, UnityEngine.Random.Range(1, int.MaxValue), seats);
            world.difficulty = (Difficulty)settings.difficulty;
            Begin(world);
            loop.FlushSave();
            if (Perspective.Spectating)
                hud.ShowStatus("Exhibition: the AIs play and you watch. Click anything for its details; battles play out as they happen.", false);
            else
            {
                hud.ShowStatus("New game. Tap an Ion Crystal with an Extractor, then build a chain that sells.", false);
                help.ShowIntroOnce();
            }
        }

        /// <summary>Saves and goes back to the title screen, where the game can be resumed or a new one set up.</summary>
        void ReturnToMenu()
        {
            if (!running) return;
            running = false;
            if (net != null)
            {
                // Leaving an online match: the seat stands idle, theirs to rejoin with the room code.
                EndOnline("You left the online match. Join the room again with its code to take your seat back.", true);
                return;
            }
            loop.FlushSave();
            pendingLoaded = loop.World;
            pendingMap = map;
            hud.ShowStartScreen(true, "");
            TitleBackdrop();
            titleMenu.ShowMain();
        }



        /// <summary>
        /// An online match starts (or, after a dropped connection, starts again): every machine builds the same world
        /// from the relay's seed and seats and plays it in lockstep through the relay. A returning player replays the
        /// relay's log from the first tick and catches up.
        /// </summary>
        void StartOnline(RelayClient client, RelayMessage m)
        {
            if (net != null && net != client) net.Close();
            net = client;
            try
            {
                var world = OnlineMatch.Build(catalog, m, out map);
                Begin(world, OnlineMatch.LocalSeat(m));
                var session = OnlineMatch.Join(loop, client, m);
                session.HandedOver += seat => hud.ShowStatus("An AI now plays " + Perspective.SeatName(seat) + ": its player did not come back.", false);
                ApplySettings();
            }
            catch (CatalogException e)
            {
                net.Close();
                net = null;
                hud.ShowStartScreen(saves.HasSave(), "The online match could not be set up: " + e.Message);
                    titleMenu.ShowMain();
                return;
            }
            client.SpeedChanged -= OnSharedSpeed;
            client.SpeedChanged += OnSharedSpeed;
            client.Dropped -= OnDropped;
            client.Dropped += OnDropped;
            client.AwayChanged -= OnAwayChanged;
            client.AwayChanged += OnAwayChanged;
            client.Lobby -= OnBackToLobby;
            client.Lobby += OnBackToLobby;
            client.HostChanged -= OnHostChanged;
            client.HostChanged += OnHostChanged;
            onlineHost = m.host;
            online.Close();
            hud.ShowStatus(Perspective.Spectating ? "Watching online match " + m.code + "." : "Online match " + m.code + ": you are Seat " + m.you + ".", false);
        }

        /// <summary>
        /// Out of an online match to the title screen: leaving the room, or (the host took everyone back) onto the
        /// lobby. The saved single-player game is still there to resume.
        /// </summary>
        void EndOnline(string message, bool leaving)
        {
            running = false;
            if (leaving)
            {
                net.Leave();
                online.Close();
            }
            net = null;
            hud.HideOverlay();
            pendingLoaded = null;
            if (saves.HasSave())
            {
                WorldState loaded;
                if (saves.TryLoad(catalog, CatalogRepository.LoadMap(NewGameMapId, catalog), out loaded).success) { pendingLoaded = loaded; pendingMap = saves.LoadedMap; }
            }
            hud.ShowStartScreen(pendingLoaded != null, message);
            TitleBackdrop();
            if (leaving) titleMenu.ShowMain();
            else titleMenu.ShowLobby();
        }

        /// <summary>The turning ship back behind the title screen.</summary>
        void TitleBackdrop() { if (showcase != null) titleMenu.SetBackdrop(showcase.Show()); }

        void OnBackToLobby(RelayMessage m)
        {
            if (!running || loop.Lockstep == null) return;
            EndOnline("", false);
        }

        void OnHostChanged(bool host)
        {
            onlineHost = host;
            if (host) hud.ShowStatus("You now host this match: you can take everyone back to the lobby from the menu.", false);
        }

        void OnSharedSpeed(int speed, int by)
        {
            loop.SharedSpeed = speed;
            hud.ShowStatus(speed == 0 ? Perspective.SeatName((FactionId)by) + " paused the game." : "Seat " + by + " set the speed to " + speed + "x.", false);
        }

        void OnDropped(RelayMessage m)
        {
            hud.ShowStatus((m.name.Length > 0 ? m.name + " (Seat " + m.seat + ")" : "Seat " + m.seat)
                + (m.left ? " left the game." : " lost connection.") + " Their seat stands idle until they rejoin with the room code.", false);
        }

        void OnAwayChanged(int seat, bool away)
        {
            if ((FactionId)seat == Perspective.Viewer && !Perspective.Spectating) return;
            hud.ShowStatus(away ? Perspective.SeatName((FactionId)seat) + " is away: their factory runs on, giving no new orders."
                : Perspective.SeatName((FactionId)seat) + " is back.", false);
        }

        /// <summary>
        /// A mid-game test start (replacing the save): a new map with 20,000 credits and Ion and one of each ship
        /// type at the headquarters, for trying combat, research and the late game without playing up to them.
        /// </summary>
        void StartTestGame()
        {
            StartNewGame();
            var w = loop.World;
            GameFactory.MakeTestStart(w, catalog);
            loop.FlushSave();
            hud.ShowStatus("Test start: 20,000 credits and Ion, and one of each ship at your HQ.", false);
        }

        void ResumeGame()
        {
            if (pendingLoaded == null) { StartNewGame(); return; }
            if (pendingMap != null) map = pendingMap;
            Begin(pendingLoaded);
            pendingLoaded = null;
            hud.ShowStatus("Saved game restored.", false);
        }

        void Begin(WorldState world) { Begin(world, null); }

        /// <summary>Starts play. <paramref name="onlineSeat"/>: an online match, played from that seat (Neutral: watching).</summary>
        void Begin(WorldState world, FactionId? onlineSeat)
        {
            if (onlineSeat.HasValue)
            {
                Perspective.Spectating = onlineSeat.Value == FactionId.Neutral;
                Perspective.Viewer = Perspective.Spectating ? world.factions[0].id : onlineSeat.Value;
            }
            else
            {
                // The person plays the seat no AI plays; with none (an exhibition) they watch, following the first seat.
                var mine = world.factions.Find(f => !f.aiControlled);
                Perspective.Spectating = mine == null;
                Perspective.Viewer = mine != null ? mine.id : world.factions[0].id;
            }
            Perspective.Overview = Perspective.Spectating; // a spectator starts on the overview of every seat
            if (showcase != null) showcase.Hide(); // play starts: the title's ship and its camera are put away
            // An online match is never written over the single-player save: the relay keeps it.
            loop.Initialize(catalog, map, world, onlineSeat.HasValue ? null : saves);
            ui.activeTool = UiTool.Select;
            ui.selectedCraftId = -1;
            ui.selectedBuildingId = -1;
            ui.selectedNodeId = -1;
            ui.selectedBeltId = -1;
            ui.selectedCellId = -1;
            foreach (var c in world.cells) if (c.headquartersFor == Perspective.Viewer) ui.selectedCellId = c.id;
            cameraInput.Map = map;
            cameraInput.SetBounds(WorldView.MapBounds(map));
            view.BuildMap(map, world, cameraInput.Camera);
            cameraInput.FocusOn(ui.selectedCellId >= 0 ? WorldView.CellCenter(map, ui.selectedCellId) : Vector3.zero);
            input = new WorldInput(loop, cameraInput, view, hud, ui);
            hud.FocusCellRequested = cellId => cameraInput.GlideTo(WorldView.CellCenter(map, cellId));
            hud.TouchAction = action =>
            {
                switch (action)
                {
                    case "cancel": input.TouchCancel(); break;
                    case "rotate": input.TouchRotate(); break;
                    case "tilt-up": cameraInput.TiltBy(12f); break;
                    case "tilt-down": cameraInput.TiltBy(-12f); break;
                    case "top": cameraInput.TopDown(); break;
                    case "reset": cameraInput.ResetView(); break;
                    case "undo": input.Undo(); break;
                    case "group": input.ToggleGroupMode(); break;
                    case "group-all":
                    {
                        var lead = WorldLookup.GetCraft(loop.World, ui.selectedCraftId);
                        if (lead != null) input.SelectAllInCell(lead.cellId);
                        break;
                    }
                    case "group-clear": input.ClearGroup(); break;
                    default:
                        if (action.StartsWith("group-toggle:")) input.ToggleGroup(int.Parse(action.Substring("group-toggle:".Length)));
                        break;
                }
            };
            hud.CanUndo = () => input.CanUndo;
            hud.SpeedAction = SetSpeed;
            hud.GameSpeed = () => loop.Lockstep != null ? loop.SharedSpeed : userPaused ? 0 : settings.gameSpeed;
            userPaused = false;
            ApplySettings();
            hud.HideOverlay();
            shownRevision = -1;
            wasTactical = false;
            running = true;
        }

        /// <summary>Paused from the top bar (or Space): the world stands still, but you can still look around and build.</summary>
        bool userPaused;

        /// <summary>0 toggles pause; otherwise runs at that speed (unpausing) and remembers it. Online, for everyone.</summary>
        void SetSpeed(int speed)
        {
            if (net != null && loop.Lockstep != null)
            {
                net.SetSpeed(speed == 0 ? (loop.SharedSpeed == 0 ? 1 : 0) : speed);
                return;
            }
            if (speed == 0) { userPaused = !userPaused; return; }
            userPaused = false;
            if (settings.gameSpeed == speed) return;
            settings.gameSpeed = speed;
            settings.Save();
            ApplySettings();
        }

        void Update()
        {
            if (music != null) music.Tick();
            Perf.Frame(Time.unscaledDeltaTime);
            // The pointer first: mouse or touch, it is what the camera and the map read this frame.
            Pointer.Update(p => hud != null && hud.IsPointerOverUi(p));
            ApplyInterfaceScale();
            bool compactLayout = settings.Compact(Pointer.TouchMode, Mathf.Min(Screen.width, Screen.height), Screen.dpi);
            if (hud != null) hud.SetLayout(compactLayout, Screen.height > Screen.width);
            ApplyGraphics(settings.LowGraphics(compactLayout));
            if (hud != null) hud.RefreshTouch(running && loop.World != null);
            if (Input.GetKeyDown(KeyCode.F3))
            {
                // F3 steps through the readout's modes, the same choice as in Settings, and remembers it.
                settings.perfOverlay = (settings.perfOverlay + 1) % 3;
                settings.Save();
            }
            int perfMode = settings.perfOverlay;
            if (perfLabel != null)
            {
                perfLabel.style.display = perfMode == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                if (perfMode > 0 && Time.frameCount % 15 == 0)
                    perfLabel.text = Perf.Report(perfMode == 2) + "   [F3]" + (perfMode == 2 && loop.World != null ? "\n" + loop.Services.Ai.Brain.Report() : "");
            }
            if (!running || loop.World == null)
            {
                if (online != null) online.Poll();
                if (showcase != null) showcase.Tick(Time.unscaledDeltaTime);
                // The title screen: Esc closes its Settings window, or goes back a page.
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (menu != null && menu.IsOpen) menu.Close();
                    else if (titleMenu != null) titleMenu.Back();
                }
                return;
            }

            // Esc closes the menu, or opens it when there is no tool or selection to cancel.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool nothingToCancel = ui.activeTool == UiTool.Select && ui.selectedCraftId < 0 && ui.selectedBuildingId < 0 && ui.selectedNodeId < 0 && ui.selectedBeltId < 0;
                if (menu.IsOpen) { menu.Close(); return; }
                if (battle.IsPlaying) { battle.Skip(); return; }
                if (help.IsOpen) { help.Close(); return; }
                if (research.IsOpen) { research.Close(); return; }
                if (nothingToCancel) { menu.Open(); return; }
            }
            if (Time.frameCount % 10 == 0)
            {
                long rt = Perf.Begin();
                resourceBar.Refresh(loop.World, catalog);
                Perf.End(Perf.Stage.Hud, rt);
            }
            // An online match: what the relay has passed on, read before the tick. Opening the menu or research
            // does not stop it: the other players play on.
            if (net != null) net.Poll(Time.unscaledTime);
            bool onlineMatch = loop.Lockstep != null;
            battle.Collect();
            // Battle scenes play over the running game: battles no longer stop the world.
            if (battle.IsPlaying) battle.Tick(Time.unscaledDeltaTime);
            if (research.IsOpen)
            {
                // Paused while browsing research, like the menu.
                research.Refresh();
                if (onlineMatch) loop.Update(Time.deltaTime);
                if (Time.frameCount % 15 == 0) hud.Refresh(loop.World, catalog, loop.LastCommandResult);
                return;
            }
            if (menu.IsOpen)
            {
                // Paused: keep the HUD current but advance nothing (online, play goes on).
                if (onlineMatch) loop.Update(Time.deltaTime);
                if (Time.frameCount % 15 == 0) hud.Refresh(loop.World, catalog, loop.LastCommandResult);
                return;
            }

            // Space pauses (when it is your turn in a battle it ends the turn instead).
            var focus = Perspective.FocusBattle(loop.World, ui.selectedCraftId, ui.watchBattleId);
            bool myTurn = focus != null && focus.turn == Perspective.Viewer && !Perspective.Spectating;
            if (Input.GetKeyDown(KeyCode.Space) && loop.World.mode == MatchMode.Factory && !myTurn) SetSpeed(0);
            // Alone, the game rests while its window is not in front; online it plays on (the others are playing).
            loop.PageHidden = onlineMatch && RelayClient.PageHidden();
            if (onlineMatch || (!userPaused && Application.isFocused)) loop.Update(Time.deltaTime);
            sound.Watch(loop.World);
            long t = Perf.Begin();
            bool overUi = hud.IsPointerOverUi(Pointer.Position);
            cameraInput.Update(Time.unscaledDeltaTime, overUi, true);
            input.Update();
            Perf.End(Perf.Stage.Input, t);

            // A new battle you are part of brings the camera to it; when the last one ends, it returns.
            var shown = Perspective.FocusBattle(loop.World, ui.selectedCraftId, ui.watchBattleId);
            bool tactical = shown != null;
            if (shown != null && shown.id != framedBattleId && (Perspective.InBattle(shown) || shown.id == ui.watchBattleId))
            {
                cameraInput.FrameBattle(map, shown);
                framedBattleId = shown.id;
            }
            if (!tactical && wasTactical) { cameraInput.ReturnFromBattle(); framedBattleId = -1; }
            wasTactical = tactical;

            float fraction = loop.World.mode == MatchMode.Factory ? loop.TickFraction : 0f;
            t = Perf.Begin();
            view.Factory = loop.Services.Factory;
            view.Refresh(loop.World, catalog, loop.Revision, loop.Services.Pull, ui, fraction);
            Perf.End(Perf.Stage.View, t);
            if (shownRevision != loop.Revision || Time.frameCount % 15 == 0)
            {
                t = Perf.Begin();
                shownRevision = loop.Revision;
                hud.Refresh(loop.World, catalog, loop.LastCommandResult);
                Perf.End(Perf.Stage.Hud, t);
            }
        }

        void ApplySettings()
        {
            if (music != null) music.ApplyVolume();
            loop.SpeedMultiplier = settings.gameSpeed;
            // How fast AIs play their battle turns: a move every 0.6 s (Watch), 0.2 s (Quick), or a whole turn at once.
            // It is part of the rules, so a multiplayer lobby will have to agree it for everyone.
            // Online it keeps the standard pace on every machine: each must play the same rules.
            if (catalog != null && loop.Lockstep != null) catalog.rules.aiBattleStepTicks = 6;
            else if (catalog != null)
                catalog.rules.aiBattleStepTicks = settings.aiTurns == GameSettings.AiTurnsInstant ? 0 : settings.aiTurns == GameSettings.AiTurnsQuick ? 2 : 6;
            if (cameraInput != null)
            {
                cameraInput.PanMultiplier = settings.panSpeed;
                cameraInput.TwistTurnsBoard = settings.twistTurnsBoard;
            }
            if (view != null)
            {
                view.NeedIconMode = settings.needIcons;
                view.ShowBeltItemIcons = settings.beltItemIcons;
                view.ShowBottlenecks = settings.bottleneckMarkers;
            }
            bool meshy = settings.visualStyle == GameSettings.VisualMeshy;
            if (MeshyLibrary.Enabled != meshy)
            {
                MeshyLibrary.Enabled = meshy;
                if (view != null) view.ResetStyle(); // buildings, goods and ingredient icons are rebuilt in the new style
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && running) loop.FlushSave();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && running) loop.FlushSave();
        }

        void OnApplicationQuit()
        {
            if (net != null) net.Close();
            if (running) loop.FlushSave();
            RenderRig.SetLowQuality(false); // the pipeline asset's render scale is shared: never leave it lowered
        }
    }
}
