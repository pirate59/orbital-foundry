using System;
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
        int shownRevision = -1;
        bool running;
        bool wasTactical;
        WorldState pendingLoaded;
        GameSettings settings;
        SoundFeedback sound;
        MenuController menu;
        HelpController help;
        BattleSceneController battle;
        ResearchController research;

        void Awake()
        {
            Application.targetFrameRate = 60;
            settings = GameSettings.Load();
            sound = new SoundFeedback(gameObject, settings);
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

            var camGo = new GameObject("Isometric Camera");
            var camera = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            view = new WorldView(sprites, new GameObject("World").transform);
            view.LineOf = (w, beltId) => loop.Services.Belts.LineOf(w, beltId);
            cameraInput = new CameraInput(camera, WorldView.MapBounds(map), Vector3.zero);
            BuildStarfield();

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
            hud.SetTitleArt(
                new[]
                {
                    sprites.GetAsteroidSprite("crystal", 3, true),
                    sprites.GetStationSprite("solar_array", FactionId.Player),
                    sprites.GetStationSprite("headquarters", FactionId.Player),
                    sprites.GetCraftSprite("strike", FactionId.Player),
                    sprites.GetAsteroidSprite("unobtanium_ore", 1, true),
                },
                new[] { 80f, 120f, 170f, 100f, 80f });
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
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            document.rootVisualElement.styleSheets.Add(style);
            help = new HelpController();
            help.Initialize(document.rootVisualElement);
            help.Sound = name => sound.Play(name);
            hud = new HudController();
            hud.Help = help;
            hud.Initialize(document, loop, ui);
            hud.NewGameRequested = StartNewGame;
            hud.ResumeRequested = ResumeGame;
            menu = new MenuController(settings);
            menu.Initialize(document.rootVisualElement);
            menu.RestartRequested = StartNewGame;
            menu.SaveRequested = () => running ? loop.FlushSave() : null;
            menu.SettingsChanged = ApplySettings;
            menu.Feedback = name => sound.Play(name);
            battle = new BattleSceneController(sprites, settings);
            battle.Initialize(document.rootVisualElement, loop);
            battle.Sound = name => sound.Play(name, 0.7f, 0.04f);
            research = new ResearchController();
            research.Initialize(document.rootVisualElement, loop);
            research.Sound = name => sound.Play(name);
            research.Feedback = r => hud.ShowStatus(r.message, !r.success);
            hud.ResearchRequested = () => research.Open();
        }

        void StartNewGame()
        {
            saves.AllowOverwrite();
            // Every new game gets a freshly generated map; its seed lives in the map ID, so the save can rebuild it.
            map = CatalogRepository.LoadMap(MapGenerator.IdFor(UnityEngine.Random.Range(1, int.MaxValue)), catalog);
            var world = GameFactory.CreateNew(catalog, map);
            Begin(world);
            loop.FlushSave();
            hud.ShowStatus("New game. Tap an Ion Crystal with an Extractor, then build a chain that sells.", false);
            help.ShowIntroOnce();
        }

        void ResumeGame()
        {
            if (pendingLoaded == null) { StartNewGame(); return; }
            if (pendingMap != null) map = pendingMap;
            Begin(pendingLoaded);
            pendingLoaded = null;
            hud.ShowStatus("Saved game restored.", false);
        }

        void Begin(WorldState world)
        {
            loop.Initialize(catalog, map, world, saves);
            ui.activeTool = UiTool.Select;
            ui.selectedCraftId = -1;
            ui.selectedBuildingId = -1;
            ui.selectedNodeId = -1;
            ui.selectedBeltId = -1;
            ui.selectedCellId = -1;
            foreach (var c in world.cells) if (c.headquartersFor == FactionId.Player) ui.selectedCellId = c.id;
            view.BuildMap(map, world, cameraInput.Camera);
            cameraInput.FocusOn(ui.selectedCellId >= 0 ? WorldView.CellCenter(map, ui.selectedCellId) : Vector3.zero);
            input = new WorldInput(loop, cameraInput, view, hud, ui);
            ApplySettings();
            hud.HideOverlay();
            shownRevision = -1;
            wasTactical = false;
            running = true;
        }

        void Update()
        {
            if (!running || loop.World == null) return;

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
            battle.Collect();
            if (battle.IsPlaying)
            {
                // Play waits while a battle scene runs: no AI steps, no input, no factory time.
                battle.Tick(Time.unscaledDeltaTime);
                view.Refresh(loop.World, catalog, loop.Revision, loop.Services.Pull, ui, 0f);
                return;
            }
            if (research.IsOpen)
            {
                // Paused while browsing research, like the menu.
                research.Refresh();
                if (Time.frameCount % 15 == 0) hud.Refresh(loop.World, catalog, loop.LastCommandResult);
                return;
            }
            if (menu.IsOpen)
            {
                // Paused: keep the HUD current but advance nothing.
                if (Time.frameCount % 15 == 0) hud.Refresh(loop.World, catalog, loop.LastCommandResult);
                return;
            }

            loop.Update(Time.deltaTime);
            sound.Watch(loop.World);
            bool overUi = hud.IsPointerOverUi(Input.mousePosition);
            cameraInput.Update(Time.unscaledDeltaTime, overUi, true);
            input.Update();

            bool tactical = loop.World.mode == MatchMode.Tactical;
            if (tactical && !wasTactical) cameraInput.FrameBattle(map, loop.World.encounter);
            if (!tactical && wasTactical) cameraInput.ReturnFromBattle();
            wasTactical = tactical;

            float fraction = loop.World.mode == MatchMode.Factory ? loop.TickFraction : 0f;
            view.Refresh(loop.World, catalog, loop.Revision, loop.Services.Pull, ui, fraction);
            if (shownRevision != loop.Revision || Time.frameCount % 15 == 0)
            {
                shownRevision = loop.Revision;
                hud.Refresh(loop.World, catalog, loop.LastCommandResult);
            }
        }

        void ApplySettings()
        {
            loop.SpeedMultiplier = settings.gameSpeed;
            if (cameraInput != null) cameraInput.PanMultiplier = settings.panSpeed;
            if (view != null) view.ShowNeedIcons = settings.showNeedIcons;
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
            if (running) loop.FlushSave();
        }

        void BuildStarfield()
        {
            var rng = new System.Random(7);
            var parent = new GameObject("Starfield").transform;
            var star = sprites.GetTokenSprite("star");
            var bounds = WorldView.MapBounds(map);
            for (int i = 0; i < 260; i++)
            {
                var go = new GameObject("Star");
                go.transform.SetParent(parent, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = star;
                sr.sortingOrder = -200;
                float b = 0.3f + (float)rng.NextDouble() * 0.6f;
                sr.color = i % 7 == 0
                    ? new Color(b + 0.1f, b * 0.82f, b * 0.55f, 0.8f)
                    : new Color(b * 0.78f, b * 0.7f, b, 0.8f);
                go.transform.position = new Vector3(
                    bounds.min.x - 20 + (float)rng.NextDouble() * (bounds.size.x + 40),
                    -6f,
                    bounds.min.z - 20 + (float)rng.NextDouble() * (bounds.size.z + 40));
                go.transform.rotation = Quaternion.Euler(90, 0, 0);
                go.transform.localScale = Vector3.one * (0.05f + (float)rng.NextDouble() * 0.12f);
            }
        }
    }
}
