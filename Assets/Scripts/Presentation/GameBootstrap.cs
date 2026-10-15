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
        ResourceBarController resourceBar;
        /// <summary>The testing overlay: frames and ticks per second, and where the time goes. F3 cycles it.</summary>
        Label perfLabel;
        int perfMode = 1; // 0 hidden, 1 rates, 2 rates and costliest stages

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

            var camGo = new GameObject("Orbit Camera");
            var camera = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            view = new WorldView(sprites, new GameObject("World").transform);
            view.LineOf = (w, beltId) => loop.Services.Belts.LineOf(w, beltId);
            cameraInput = new CameraInput(camera, WorldView.MapBounds(map), Vector3.zero) { Map = map };
            RenderRig.Build(camera);

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
                new[] { art("artillery", FactionId.Player), art("guard", FactionId.Player), art("strike", FactionId.Player), art("tank", FactionId.Ai), art("guard", FactionId.Ai) },
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
            hud.ResumeRequested = ResumeGame;
            resourceBar = new ResourceBarController();
            resourceBar.Initialize(document.rootVisualElement, sprites);
            menu = new MenuController(settings);
            menu.Initialize(document.rootVisualElement);
            menu.RestartRequested = StartNewGame;
            menu.SaveRequested = () => running ? loop.FlushSave() : null;
            menu.SettingsChanged = ApplySettings;
            menu.VisualNote = () =>
            {
                if (catalog == null) return "";
                int n = MeshyLibrary.CountItems(catalog);
                return n == 0 ? "Meshy: no Meshy models are installed yet, so items keep their light-weight look."
                    : "Meshy: models for " + n + " of " + catalog.items.Count + " items; the rest keep their light-weight look.";
            };
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
            cameraInput.Map = map;
            cameraInput.SetBounds(WorldView.MapBounds(map));
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
            Perf.Frame(Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.F3)) perfMode = (perfMode + 1) % 3;
            if (perfLabel != null)
            {
                perfLabel.style.display = perfMode == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                if (perfMode > 0 && Time.frameCount % 15 == 0) perfLabel.text = Perf.Report(perfMode == 2) + "   [F3]";
            }
            if (!running || loop.World == null)
            {
                // The title screen: Esc closes its Settings window.
                if (Input.GetKeyDown(KeyCode.Escape) && menu != null && menu.IsOpen) menu.Close();
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
            long t = Perf.Begin();
            bool overUi = hud.IsPointerOverUi(Input.mousePosition);
            cameraInput.Update(Time.unscaledDeltaTime, overUi, true);
            input.Update();
            Perf.End(Perf.Stage.Input, t);

            bool tactical = loop.World.mode == MatchMode.Tactical;
            if (tactical && !wasTactical) cameraInput.FrameBattle(map, loop.World.encounter);
            if (!tactical && wasTactical) cameraInput.ReturnFromBattle();
            wasTactical = tactical;

            float fraction = loop.World.mode == MatchMode.Factory ? loop.TickFraction : 0f;
            t = Perf.Begin();
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
            loop.SpeedMultiplier = settings.gameSpeed;
            if (cameraInput != null) cameraInput.PanMultiplier = settings.panSpeed;
            if (view != null) view.ShowNeedIcons = settings.showNeedIcons;
            bool meshy = settings.visualStyle == GameSettings.VisualMeshy;
            if (MeshyLibrary.Enabled != meshy)
            {
                MeshyLibrary.Enabled = meshy;
                if (view != null) view.ResetCargo(); // goods and ingredient icons are rebuilt in the new style
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
            if (running) loop.FlushSave();
        }
    }
}
