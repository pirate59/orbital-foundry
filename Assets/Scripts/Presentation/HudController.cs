using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// Binds the UI Toolkit HUD to GameLoop. Buttons only create GameCommands or change the runtime UiState; every label
    /// is derived from WorldState and the catalog.
    /// </summary>
    public sealed class HudController
    {
        GameLoop loop;
        UiState ui;
        VisualElement root;
        readonly Dictionary<string, VisualElement> named = new Dictionary<string, VisualElement>();
        readonly List<string> missing = new List<string>();

        Label powerLabel, tradeHoldLabel;
        VisualElement tradeHold, tradeHoldFill;
        Label currencyLabel, salesLabel, goalLabel, selectedCellLabel, selectionDetails, availabilityDetails, saveIndicator, statusLabel;
        Label craftOrderHint;
        VisualElement craftOrders;
        Button claimButton, repairButton, groupAllButton, groupClearButton, touchGroup;
        VisualElement selectBox;
        Label turnLabel, actionLabel, attackPreview, craftStats, resultTitle, resultText;
        VisualElement buildPanel, recipePanel, craftList, tacticalPanel, tacticalBanner, pendingDefender, resultOverlay, targetList, shipList;
        Label targetTitle;
        string targetSignature = "";
        VisualElement touchBar, pressRing, placeRecipe, placeRecipeList;
        Label placeRecipeTitle;
        string placeRecipeSignature = "";

        /// <summary>
        /// While a machine tool is active: a strip of the recipes the player can run on it, the one new machines will
        /// start on highlighted, and "Choose after placing" (the settings pop-up then opens as before).
        /// </summary>
        void RefreshPlaceRecipe(ContentCatalog c, FactionState f)
        {
            if (placeRecipe == null) return;
            string type = WorldInput.BuildingForTool(ui.activeTool);
            var def = type != null ? CatalogRepository.GetBuilding(c, type) : null;
            bool show = def != null && def.allowedRecipeIds.Count > 0 && loop.World.mode == MatchMode.Factory;
            Show(placeRecipe, show);
            if (!show) { placeRecipeSignature = ""; return; }
            string chosen;
            ui.placeRecipe.TryGetValue(type, out chosen);
            string sig = type + "|" + chosen + "|" + f.research.Count;
            if (sig == placeRecipeSignature) return;
            placeRecipeSignature = sig;
            placeRecipeTitle.text = "NEW " + def.displayName.ToUpperInvariant() + "S MAKE";
            placeRecipeList.Clear();
            var later = new Button(() => { ui.placeRecipe.Remove(type); placeRecipeSignature = ""; }) { text = "Choose after placing" };
            later.AddToClassList("tool");
            if (string.IsNullOrEmpty(chosen)) later.AddToClassList("choice-active");
            placeRecipeList.Add(later);
            foreach (var id in def.allowedRecipeIds)
            {
                var recipe = CatalogRepository.GetRecipe(c, id);
                if (recipe == null || !ResearchService.IsRecipeUnlocked(c, f, recipe)) continue;
                string rid = id;
                var b = new Button(() => { ui.placeRecipe[type] = rid; placeRecipeSignature = ""; }) { text = ItemName(c, recipe.outputItemId) };
                b.AddToClassList("tool");
                if (chosen == id) b.AddToClassList("choice-active");
                placeRecipeList.Add(b);
            }
        }
        static readonly string[] Sheets = { "build", "info", "alerts", "network" };
        readonly Dictionary<string, Button> dockButtons = new Dictionary<string, Button>();
        string openSheet = "";
        bool compact, menuWrapped;
        string lastSelectionKey = "";

        /// <summary>
        /// Switches between the full layout and the compact phone layout (and notes the orientation). The compact one
        /// shows every panel body (their Hide buttons are gone) and lets the menu scroll.
        /// </summary>
        public void SetLayout(bool useCompact, bool portrait)
        {
            if (root == null) return;
            root.EnableInClassList("portrait", portrait);
            if (useCompact == compact) return;
            compact = useCompact;
            root.EnableInClassList("compact", compact);
            if (!compact) OpenSheet("");
            if (compact)
            {
                foreach (var body in new[] { "build-tools", "selection-body", "alert-list" }) { var e = Find(body); if (e != null) e.style.display = DisplayStyle.Flex; }
                foreach (var panel in new[] { "build-panel", "selection-panel", "alert-panel" }) { var e = Find(panel); if (e != null) e.RemoveFromClassList("panel-collapsed"); }
                WrapMenu();
            }
        }

        /// <summary>Opens one of the compact layout's sheets, or closes it when it is already open ("" closes all).</summary>
        public void OpenSheet(string sheet)
        {
            openSheet = sheet == openSheet ? "" : sheet;
            foreach (var name in Sheets)
            {
                root.EnableInClassList("sheet-" + name, openSheet == name);
                Button b;
                if (dockButtons.TryGetValue(name, out b) && b != null) b.EnableInClassList("dock-active", openSheet == name);
            }
        }

        /// <summary>The menu's settings are taller than a phone: put the card's contents in a scroll view.</summary>
        void WrapMenu()
        {
            if (menuWrapped) return;
            var card = root.Q(className: "menu-card");
            if (card == null) return;
            menuWrapped = true;
            var scroll = new ScrollView();
            scroll.AddToClassList("menu-scroll");
            var children = new List<VisualElement>(card.Children());
            foreach (var child in children) scroll.Add(child);
            card.Add(scroll);
        }
        Button touchRotate, undoButton, topResearch, speedPause, productionButton;
        VisualElement topBar, productionPanel, productionList;
        Label productionSummary;
        bool productionOpen;
        int productionFrame;
        readonly Button[] speedButtons = new Button[GameSettings.Speeds.Length];
        /// <summary>A speed button was pressed: 0 toggles pause, otherwise the speed (1, 2 or 4) to run at.</summary>
        public Action<int> SpeedAction;
        /// <summary>The current game speed, 0 while paused.</summary>
        public Func<int> GameSpeed;
        /// <summary>A touch bar button was pressed: "cancel", "rotate", "tilt-up", "tilt-down", "top" or "reset"; or "undo".</summary>
        public Action<string> TouchAction;
        /// <summary>Whether there is a recent placement to undo (the Undo button shows only then).</summary>
        public Func<bool> CanUndo;

        /// <summary>
        /// Every frame on a touch screen: shows the touch bar (Rotate only while placing a shipyard) and the ring
        /// that fills under a finger held for a long press.
        /// </summary>
        bool keyLabelsStripped;

        public void RefreshTouch(bool playing)
        {
            if (touchBar == null) return;
            // Every frame (tools change without a world revision): the recipe strip for the machine being placed.
            if (playing && loop.World != null) RefreshPlaceRecipe(loop.Catalog, WorldLookup.GetFaction(loop.World, Me));
            else Show(placeRecipe, false);
            // Pop-ups that hang under the top bar sit just below it, however many rows it wraps to.
            if (topBar != null && topBar.layout.height > 1f)
            {
                float below = topBar.layout.yMax + 6f;
                if (placeRecipe != null) placeRecipe.style.top = below;
                if (productionPanel != null) productionPanel.style.top = below;
            }
            if (productionPanel != null)
            {
                Show(productionPanel, productionOpen && playing);
                if (productionButton != null) productionButton.EnableInClassList("tool-active", productionOpen);
                if (productionOpen && playing && productionFrame++ % 20 == 0) RefreshProduction();
            }
            RefreshTradeHold(playing);
            if (speedPause != null && GameSpeed != null)
            {
                int speed = GameSpeed();
                speedPause.EnableInClassList("choice-active", speed == 0);
                string pauseText = (speed == 0 ? "Paused" : "Pause") + (Pointer.TouchMode ? "" : " [Space]");
                if (speedPause.text != pauseText) speedPause.text = pauseText;
                for (int i = 0; i < speedButtons.Length; i++)
                    if (speedButtons[i] != null) speedButtons[i].EnableInClassList("choice-active", GameSettings.Speeds[i] == speed);
            }
            if (topResearch != null)
            {
                // What the research queue is working on, with its progress; a tap opens the research web.
                var f = playing && loop.World != null ? WorldLookup.GetFaction(loop.World, Me) : null;
                var job = ResearchService.Current(f);
                var node = job != null ? CatalogRepository.GetResearch(loop.Catalog, job.nodeId) : null;
                Show(topResearch, node != null);
                if (node != null)
                {
                    string text = (compact ? "" : "Researching ") + node.displayName + " " + (job.ticksDone * 100 / Math.Max(1, job.ticksTotal)) + "%"
                        + (compact ? "" : "  " + ResearchService.Clock(loop.Catalog, job.ticksTotal - job.ticksDone))
                        + (f.researchQueue.Count > 1 ? "  +" + (f.researchQueue.Count - 1) : "");
                    if (topResearch.text != text) topResearch.text = text;
                }
            }
            if (undoButton != null)
                Show(undoButton, playing && loop.World != null && loop.World.mode == MatchMode.Factory && CanUndo != null && CanUndo());
            bool touch = Pointer.TouchMode && playing;
            // Compact: the camera buttons give way to an open sheet.
            if (compact && openSheet.Length > 0) touch = false;
            if (Pointer.TouchMode && !keyLabelsStripped)
            {
                // No keyboard: drop the "[Esc]", "[Space]" key reminders from button labels.
                keyLabelsStripped = true;
                root.Query<Button>().ForEach(b => { if (b.text != null) b.text = System.Text.RegularExpressions.Regex.Replace(b.text, @"\s*\[[^\]]+\]", ""); });
                var controls = root.Q<Label>("controls-text");
                if (controls != null)
                    controls.text = "Tap select / place (tap twice to build)  ·  Long-press order the selected unit, rotate a shipyard or cancel  ·  "
                        + "One finger drag pans (with Belts or Dismantle it draws)  ·  Two fingers pan, pinch to zoom, twist to turn  ·  "
                        + "Three fingers up or down tilt  ·  Touch bar: Cancel, Rotate, Tilt, Top view, Reset view";
            }
            Show(touchBar, touch);
            if (touchRotate != null) Show(touchRotate, touch && ui.activeTool == UiTool.BuildShipyard);
            if (touchGroup != null)
            {
                // Group: shown with the Select tool in normal play, lit while on.
                Show(touchGroup, touch && ui.activeTool == UiTool.Select && loop.World != null && loop.World.mode == MatchMode.Factory);
                touchGroup.EnableInClassList("choice-active", ui.groupMode);
                string label = ui.groupMode ? "Group: on" : "Group";
                if (touchGroup.text != label) touchGroup.text = label;
            }
            float progress = Pointer.LongPressProgress;
            bool ring = touch && progress > 0.15f && root.panel != null;
            Show(pressRing, ring);
            if (!ring) return;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Pointer.Position.x, Screen.height - Pointer.Position.y));
            float size = 28f + 52f * progress;
            pressRing.style.width = size;
            pressRing.style.height = size;
            pressRing.style.borderTopLeftRadius = pressRing.style.borderTopRightRadius = pressRing.style.borderBottomLeftRadius = pressRing.style.borderBottomRightRadius = size / 2f;
            pressRing.style.left = p.x - size / 2f;
            pressRing.style.top = p.y - size / 2f;
        }
        ScrollView alertList;
        Button beltUpgradeLine, beltUpgradeOne, beltViewButton, padLinkButton, padUnlinkButton;
        VisualElement priorityPanel, configWindow;
        Label priorityHint, configTitle, configClipboardLabel;
        VisualElement configActions;
        Button configOpenButton, configPasteButton;
        /// <summary>The building whose settings pop-up is open, or -1.</summary>
        int configBuildingId = -1;
        /// <summary>Settings copied from a building, to paste onto others.</summary>
        sealed class CopiedSettings
        {
            public string TypeId, DisplayName, RecipeId;
            public int Priority;
            public List<string> Blocked, Stored;
        }
        CopiedSettings clipboard;
        readonly Button[] priorityButtons = new Button[DeliveryPriority.Names.Length];
        Button researchButton, captureButton, assignDefenderButton, endTurnButton, retreatButton, dismantleButton;
        Button newGameButton, resumeButton, continueButton;
        readonly Dictionary<UiTool, Button> toolButtons = new Dictionary<UiTool, Button>();

        string choiceSignature = "", craftListSignature = "", pendingSignature = "", needsSignature = "";
        VisualElement needsList;
        readonly List<VisualElement> needRows = new List<VisualElement>();
        int lastAlertId = -1;
        float statusUntil;
        MatchOutcome shownOutcome = MatchOutcome.None;
        bool resultDismissed;
        /// <summary>The "you are out" notice has been shown for this match (the match goes on without the local seat).</summary>
        bool outShown;

        public Action NewGameRequested;
        /// <summary>The title screen's test start: a fresh map with plenty of credits and Ion and one of each ship.</summary>
        public Action TestGameRequested;
        /// <summary>The difficulty new games start at (0 Easy, 1 Medium, 2 Hard): read, and set from the title screen.</summary>
        public Func<int> NewGameDifficulty;
        public Action<int> DifficultyChosen;
        /// <summary>The catalog, for the title screen (before any game has started).</summary>
        public ContentCatalog TitleCatalog;
        readonly Button[] difficultyButtons = new Button[3];
        Label titleFooter;
        /// <summary>An alert was clicked: fly the camera to its cell.</summary>
        public Action<int> FocusCellRequested;
        /// <summary>Set before Initialize so the HUD can add "?" buttons.</summary>
        public HelpController Help;
        Button selectionHelp, titleResume, titleNew;
        VisualElement titleOverlay;
        Label titleNotice;
        public Action ResearchRequested;
        public Action ResumeRequested;

        public static readonly string[] RequiredNames =
        {
            "hud-root", "currency-label", "sales-label", "goal-label", "selected-cell-label", "build-panel", "build-extractor",
            "build-refinery", "build-assembler", "build-depot", "belt-tool", "recipe-panel", "research-button",
            "alert-list", "save-indicator", "tactical-panel", "turn-label",
            "action-label", "selected-craft-stats", "attack-preview", "capture-button", "assign-defender-button", "end-turn-button",
            "retreat-button", "status-label", "result-overlay", "new-game-button", "resume-button"
        };

        public void Initialize(UIDocument document, GameLoop gameLoop, UiState uiState)
        {
            loop = gameLoop;
            ui = uiState;
            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            foreach (var name in RequiredNames) Find(name);
            if (missing.Count > 0) Debug.LogError("HUD is missing elements: " + string.Join(", ", missing));

            currencyLabel = Find("currency-label") as Label;
            // Elapsed game time, ahead of the credits: it runs with the game speed and stops when paused.
            clockLabel = new Label("0:00") { name = "game-clock" };
            clockLabel.AddToClassList("stat");
            clockLabel.AddToClassList("stat-tight");
            clockLabel.tooltip = "Game time: how long this match has been played (faster at 2x and 4x, stopped when paused).";
            currencyLabel.parent.Insert(currencyLabel.parent.IndexOf(currencyLabel), clockLabel);
            powerLabel = Find("power-label") as Label;
            salesLabel = Find("sales-label") as Label;
            tradeHold = Find("trade-hold");
            tradeHoldFill = Find("trade-hold-fill");
            tradeHoldLabel = Find("trade-hold-label") as Label;
            goalLabel = Find("goal-label") as Label;
            selectedCellLabel = Find("selected-cell-label") as Label;
            selectionDetails = Find("selection-details") as Label;
            availabilityDetails = Find("availability-details") as Label;
            saveIndicator = Find("save-indicator") as Label;
            statusLabel = Find("status-label") as Label;
            turnLabel = Find("turn-label") as Label;
            actionLabel = Find("action-label") as Label;
            attackPreview = Find("attack-preview") as Label;
            craftStats = Find("selected-craft-stats") as Label;
            resultTitle = Find("result-title") as Label;
            resultText = Find("result-text") as Label;
            buildPanel = Find("build-panel");
            // Side panels fold away to free the screen; each choice is remembered between sessions.
            MakeCollapsible(buildPanel, "build-tools", "build-collapse", "hud_build_collapsed");
            MakeCollapsible(Find("selection-panel"), "selection-body", "selection-collapse", "hud_selection_collapsed");
            MakeCollapsible(Find("alert-panel"), "alert-list", "alert-collapse", "hud_alerts_collapsed");
            recipePanel = Find("recipe-panel");
            needsList = Find("needs-list");
            craftList = Find("craft-list");
            tacticalPanel = Find("tactical-panel");
            tacticalBanner = Find("tactical-banner");
            // Battles between other seats, to watch if the player chooses (built here: the layout file has no slot for it).
            elsewherePanel = new VisualElement { name = "battles-elsewhere", pickingMode = PickingMode.Position };
            elsewherePanel.AddToClassList("panel");
            elsewherePanel.style.position = Position.Absolute;
            elsewherePanel.style.top = 96;
            elsewherePanel.style.right = 12;
            elsewherePanel.style.maxWidth = 340;
            root.Add(elsewherePanel);
            Show(elsewherePanel, false);
            pendingDefender = Find("pending-defender");
            targetList = Find("target-list");
            touchBar = Find("touch-bar");
            topBar = Find("top-bar");
            productionPanel = Find("production-panel");
            productionSummary = Find("production-summary") as Label;
            productionList = Find("production-list");
            Bind("production-close", () => ToggleProduction());
            productionButton = Bind("production-button", () => ToggleProduction());
            placeRecipe = Find("place-recipe");
            placeRecipeList = Find("place-recipe-list");
            placeRecipeTitle = Find("place-recipe-title") as Label;
            foreach (var name in Sheets)
            {
                string sheet = name;
                dockButtons[sheet] = Bind("dock-" + sheet, () => OpenSheet(sheet));
            }
            pressRing = Find("press-ring");
            touchRotate = Bind("touch-rotate", () => { if (TouchAction != null) TouchAction("rotate"); });
            touchGroup = Bind("touch-group", () => { if (TouchAction != null) TouchAction("group"); });
            selectBox = Find("select-box");
            Bind("touch-cancel", () => { if (TouchAction != null) TouchAction("cancel"); });
            Bind("touch-tilt-up", () => { if (TouchAction != null) TouchAction("tilt-up"); });
            Bind("touch-tilt-down", () => { if (TouchAction != null) TouchAction("tilt-down"); });
            Bind("touch-top", () => { if (TouchAction != null) TouchAction("top"); });
            Bind("touch-reset", () => { if (TouchAction != null) TouchAction("reset"); });
            undoButton = Bind("undo-button", () => { if (TouchAction != null) TouchAction("undo"); });
            topResearch = Bind("top-research", () => { if (ResearchRequested != null) ResearchRequested(); });
            speedPause = Bind("top-speed-pause", () => { if (SpeedAction != null) SpeedAction(0); });
            for (int i = 0; i < GameSettings.Speeds.Length; i++)
            {
                int speed = GameSettings.Speeds[i];
                speedButtons[i] = Bind("top-speed-" + speed, () => { if (SpeedAction != null) SpeedAction(speed); });
            }
            shipList = Find("ship-list");
            targetTitle = Find("target-title") as Label;
            resultOverlay = Find("result-overlay");
            alertList = Find("alert-list") as ScrollView;

            BindTool("select-tool", UiTool.Select);
            BindTool("build-extractor", UiTool.BuildExtractor);
            BindTool("build-refinery", UiTool.BuildRefinery);
            BindTool("build-assembler", UiTool.BuildAssembler);
            BindTool("build-depot", UiTool.BuildDepot);
            BindTool("build-shipyard", UiTool.BuildShipyard);
            BindTool("build-fort", UiTool.BuildFort);
            BindTool("build-storage", UiTool.BuildStorage);
            BindTool("build-purifier", UiTool.BuildPurifier);
            BindTool("build-solar", UiTool.BuildSolarArray);
            BindTool("build-drone-pad", UiTool.BuildDronePad);
            padLinkButton = Bind("pad-link-button", () =>
            {
                ui.activeTool = UiTool.LinkDronePad;
                ui.beltView = false;
                ShowStatus(ToolHint(UiTool.LinkDronePad), false);
            });
            configWindow = root.Q<VisualElement>("config-window");
            configTitle = root.Q<Label>("config-title");
            configClipboardLabel = root.Q<Label>("config-clipboard");
            configActions = root.Q<VisualElement>("config-actions");
            configOpenButton = Bind("config-open", () => OpenConfig(ui.selectedBuildingId));
            Bind("config-close", CloseConfig);
            Bind("config-copy", () => CopySettings());
            configPasteButton = Bind("config-paste", () => PasteSettings());
            priorityPanel = root.Q<VisualElement>("priority-panel");
            priorityHint = root.Q<Label>("priority-hint");
            for (int i = 0; i < DeliveryPriority.Names.Length; i++)
            {
                string level = DeliveryPriority.Names[i];
                priorityButtons[i] = Bind("priority-" + level, () => Submit(new GameCommand { kind = CommandKind.SetPriority, actor = Me, buildingId = ui.selectedBuildingId, contentId = level }));
            }
            padUnlinkButton = Bind("pad-unlink-button", () => Submit(new GameCommand { kind = CommandKind.LinkDronePad, actor = Me, buildingId = ui.selectedBuildingId, contentId = "unlink" }));
            BindTool("belt-tool", UiTool.DrawBelt);
            BindTool("route-tool", UiTool.RouteBelt);
            SetUpBeltTierPicker();
            BindTool("dismantle-tool", UiTool.Dismantle);

            beltViewButton = Bind("belt-view-button", () =>
            {
                WorldInput.ToggleBeltView(ui);
                if (ui.beltView) ShowStatus("Belt View: buildings fade and ignore clicks. Click any belt to select, upgrade or dismantle it. Press V to switch back.", false);
            });
            researchButton = Bind("research-button", () => { if (ResearchRequested != null) ResearchRequested(); });
            beltUpgradeLine = Bind("belt-upgrade-line", () => Submit(new GameCommand { kind = CommandKind.UpgradeBelt, actor = Me, beltId = ui.selectedBeltId, contentId = "line" }));
            beltUpgradeOne = Bind("belt-upgrade-one", () => Submit(new GameCommand { kind = CommandKind.UpgradeBelt, actor = Me, beltId = ui.selectedBeltId }));
            captureButton = Bind("capture-button", CaptureSelected);
            assignDefenderButton = Bind("assign-defender-button", AssignSelected);
            endTurnButton = Bind("end-turn-button", () => Submit(new GameCommand { kind = CommandKind.EndTurn, actor = Me, battleId = FocusedBattleId() }));
            retreatButton = Bind("retreat-button", () => Submit(new GameCommand { kind = CommandKind.Retreat, actor = Me, battleId = FocusedBattleId() }));
            dismantleButton = Bind("dismantle-button", DismantleSelected);
            craftOrders = Find("craft-orders");
            craftOrderHint = Find("craft-order-hint") as Label;
            claimButton = Bind("claim-button", CaptureSelected);
            repairButton = Bind("repair-button", RepairSelected);
            groupAllButton = Bind("group-all-button", () => { if (TouchAction != null) TouchAction("group-all"); });
            groupClearButton = Bind("group-clear-button", () => { if (TouchAction != null) TouchAction("group-clear"); });
            // Sections that only apply to a selected building or unit start hidden.
            Show(Find("working-on-title"), false);
            Show(Find("needs-title"), false);
            Show(craftOrders, false);
            newGameButton = Bind("new-game-button", () => { if (NewGameRequested != null) NewGameRequested(); });
            resumeButton = Bind("resume-button", () => { if (ResumeRequested != null) ResumeRequested(); });
            continueButton = Bind("continue-button", () => { resultDismissed = true; Show(resultOverlay, false); });
            titleOverlay = Find("title-overlay");
            titleNotice = Find("title-notice") as Label;
            titleNew = Bind("title-new", () => { if (NewGameRequested != null) NewGameRequested(); });
            titleFooter = Find("title-footer") as Label;
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                int level = i;
                difficultyButtons[i] = Bind("difficulty-" + i, () =>
                {
                    if (DifficultyChosen != null) DifficultyChosen(level);
                    RefreshDifficulty();
                });
            }
            Bind("title-test", () => { if (TestGameRequested != null) TestGameRequested(); });
            titleResume = Bind("title-resume", () => { if (ResumeRequested != null) ResumeRequested(); });
            BuildStarfield(Find("title-stars"));
            if (Help != null)
            {
                selectionHelp = Help.CreateButton("getting_started");
                var titleRow = Find("selection-title-row");
                if (titleRow != null) titleRow.Add(selectionHelp);
            }
        }

        VisualElement Find(string name)
        {
            VisualElement e;
            if (named.TryGetValue(name, out e)) return e;
            var results = root.Query<VisualElement>(name).ToList();
            if (results.Count != 1)
            {
                if (!missing.Contains(name)) missing.Add(name + (results.Count > 1 ? " (duplicate)" : ""));
                e = new VisualElement(); // detached fallback keeps the HUD usable
            }
            else e = results[0];
            named[name] = e;
            return e;
        }

        Button Bind(string name, Action action)
        {
            var b = Find(name) as Button;
            if (b == null) { b = new Button(); }
            b.clicked += action;
            return b;
        }

        /// <summary>
        /// A Hide/Show button in a panel's header folds its body away. A collapsed panel also stops stretching, so
        /// the space goes back to the map. The state is kept in PlayerPrefs under <paramref name="prefsKey"/>.
        /// </summary>
        void MakeCollapsible(VisualElement panel, string bodyName, string buttonName, string prefsKey)
        {
            var body = Find(bodyName);
            var button = Find(buttonName) as Button;
            if (panel == null || body == null || button == null) return;
            Action<bool> set = collapsed =>
            {
                Show(body, !collapsed);
                button.text = collapsed ? "Show" : "Hide";
                panel.EnableInClassList("panel-collapsed", collapsed);
                PlayerPrefs.SetInt(prefsKey, collapsed ? 1 : 0);
            };
            set(PlayerPrefs.GetInt(prefsKey, 0) == 1);
            button.clicked += () => set(!panel.ClassListContains("panel-collapsed"));
        }

        const string BeltTierKey = "OrbitalFoundry.BeltTier";
        /// <summary>How long a press on the Belt button must last to open the tier picker, in milliseconds.</summary>
        const long LongPressMs = 450;
        VisualElement beltTierPicker;
        bool suppressNextBeltClick;

        /// <summary>What upgrading one new belt from the base tier to <paramref name="tier"/> costs, on top of the belt.</summary>
        public static int TierUpgradeCost(ContentCatalog c, int tier)
        {
            int cost = 0;
            for (int i = 1; i <= tier && i < c.rules.beltTiers.Count; i++) cost += c.rules.beltTiers[i].upgradeCostPerSegment;
            return cost;
        }

        /// <summary>Chooses the speed tier new belts are laid at, remembers it, and picks the belt tool.</summary>
        public void SetBeltTier(int tier)
        {
            var c = loop.Catalog;
            int count = c != null ? c.rules.beltTiers.Count : 3;
            ui.beltTier = Mathf.Clamp(tier, 0, count - 1);
            try { PlayerPrefs.SetInt(BeltTierKey, ui.beltTier); PlayerPrefs.Save(); } catch (System.Exception) { }
            ui.activeTool = UiTool.DrawBelt;
            ui.beltFromCellId = -1;
            if (beltTierPicker != null) beltTierPicker.style.display = DisplayStyle.None;
            if (c != null)
                ShowStatus("New belts: " + c.rules.beltTiers[ui.beltTier].name + " (" + (c.rules.beltCost + TierUpgradeCost(c, ui.beltTier))
                    + " per step). Long-press Belt or press B again to change.", false);
        }

        /// <summary>
        /// Long-pressing the Belt button opens a small picker of speed tiers beside it; a short press still just picks
        /// the tool. The last tier chosen is remembered.
        /// </summary>
        void SetUpBeltTierPicker()
        {
            try { ui.beltTier = PlayerPrefs.GetInt(BeltTierKey, 0); } catch (System.Exception) { ui.beltTier = 0; }
            Button belt;
            if (!toolButtons.TryGetValue(UiTool.DrawBelt, out belt) || belt == null) return;
            beltTierPicker = new VisualElement { name = "belt-tier-picker" };
            beltTierPicker.AddToClassList("belt-tier-picker");
            beltTierPicker.style.display = DisplayStyle.None;
            root.Add(beltTierPicker);
            IVisualElementScheduledItem hold = null;
            belt.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                hold = belt.schedule.Execute(() =>
                {
                    suppressNextBeltClick = true;
                    OpenBeltTierPicker(belt);
                }).StartingIn(LongPressMs);
            }, TrickleDown.TrickleDown);
            System.Action cancel = () => { if (hold != null) { hold.Pause(); hold = null; } };
            belt.RegisterCallback<PointerUpEvent>(e => cancel(), TrickleDown.TrickleDown);
            belt.RegisterCallback<PointerLeaveEvent>(e => cancel());
        }

        public bool BeltTierPickerOpen => beltTierPicker != null && beltTierPicker.style.display == DisplayStyle.Flex;

        public void CloseBeltTierPicker()
        {
            if (beltTierPicker != null) beltTierPicker.style.display = DisplayStyle.None;
        }

        void OpenBeltTierPicker(Button anchor)
        {
            var c = loop.Catalog;
            if (c == null || beltTierPicker == null) return;
            beltTierPicker.Clear();
            var title = new Label("BELT SPEED");
            title.AddToClassList("panel-subtitle");
            beltTierPicker.Add(title);
            for (int i = 0; i < c.rules.beltTiers.Count; i++)
            {
                int tier = i;
                var t = c.rules.beltTiers[i];
                var choice = new Button(() => SetBeltTier(tier))
                {
                    text = "T" + (i + 1) + "  " + t.name + "  ·  " + (c.rules.beltCost + TierUpgradeCost(c, i)) + " per step"
                };
                choice.AddToClassList("tool");
                choice.EnableInClassList("choice-active", i == ui.beltTier);
                beltTierPicker.Add(choice);
            }
            var box = anchor.worldBound;
            beltTierPicker.style.left = box.xMax + 8f;
            beltTierPicker.style.top = box.yMin - 30f;
            beltTierPicker.style.display = DisplayStyle.Flex;
            beltTierPicker.BringToFront();
        }

        void BindTool(string name, UiTool tool)
        {
            var button = Bind(name, () =>
            {
                // A long press on the Belt button opened the tier picker: that release is not a click.
                if (tool == UiTool.DrawBelt && suppressNextBeltClick) { suppressNextBeltClick = false; return; }
                ui.activeTool = tool;
                ui.beltFromCellId = -1;
                if (WorldInput.IsBuildingTool(tool)) ui.beltView = false;
                ShowStatus(ToolHint(tool) + GoodsHint(tool), false);
                if (compact && openSheet == "build") OpenSheet(""); // get the sheet out of the way to place it
            });
            toolButtons[tool] = button;
            string topic = ToolTopic(tool);
            if (button == null || Help == null || topic == null) return;
            // Each build tool sits in a row with its own "?".
            var row = new VisualElement();
            row.AddToClassList("tool-row");
            button.parent.Insert(button.parent.IndexOf(button), row);
            row.Add(button);
            row.Add(Help.CreateButton(topic));
        }

        /// <summary>" Needs 6 Iron Plate, 4 Copper Wire by belt." for tools whose building takes goods.</summary>
        string GoodsHint(UiTool tool)
        {
            var def = CatalogRepository.GetBuilding(loop.Catalog, WorldInput.BuildingForTool(tool));
            if (def == null) return "";
            if (def.materials.Count > 0) return " Needs " + IngredientsText(loop.Catalog, def.materials) + " by belt.";
            if (def.goodsCost.Count > 0)
                return " Takes " + IngredientsText(loop.Catalog, def.goodsCost) + " from your Storage (you have " + StoredProgress(loop.Catalog, def.goodsCost)
                    + "). Refine them and belt them into a Storage.";
            return "";
        }

        /// <summary>"2/4 Copper Bar, 0/2 Silicon Ingot" held across the player's Storage buildings.</summary>
        string StoredProgress(ContentCatalog c, List<IngredientDefinition> goods)
        {
            var parts = new List<string>();
            foreach (var g in goods)
            {
                int have = EconomyService.StoredCount(loop.World, c, Me, g.itemId);
                parts.Add(Math.Min(have, g.quantity) + "/" + g.quantity);
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        // ---------------------------------------------------------------- settings pop-up

        /// <summary>Player buildings with settings: a recipe, a craft order, a sell or accept list, or a delivery priority.</summary>
        static bool HasSettings(ContentCatalog c, BuildingState b)
        {
            if (b == null || b.owner != Me) return false;
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            return def != null && (def.allowedRecipeIds.Count > 0 || DeliveryPriority.Applies(def));
        }

        /// <summary>
        /// Whether the Settings pop-up has anything for this building: a recipe to choose, a sell or accept list, or
        /// (for a shipyard, where the pop-up is called Build) a craft to order.
        /// Delivery priority sits in the right bar, so a building with only that has no pop-up (though copy and
        /// paste still carry it).
        /// </summary>
        static bool HasPopupSettings(ContentCatalog c, BuildingState b)
        {
            if (b == null || b.owner != Me) return false;
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            return def != null && (def.allowedRecipeIds.Count > 0 || def.kind == BuildingKind.SaleDepot || def.kind == BuildingKind.Storage
                || def.kind == BuildingKind.Shipyard);
        }

        public bool ConfigOpen => configBuildingId != -1;

        /// <summary>Opens the settings pop-up for a building (and selects it).</summary>
        public void OpenConfig(int buildingId)
        {
            var b = loop.World != null ? WorldLookup.GetBuilding(loop.World, buildingId) : null;
            if (Perspective.Spectating || !HasPopupSettings(loop.Catalog, b)) return; // a spectator changes no settings
            ui.selectedBuildingId = b.id;
            ui.selectedCraftId = -1;
            ui.selectedNodeId = -1;
            ui.selectedBeltId = -1;
            configBuildingId = b.id;
            choiceSignature = "";
        }

        public void CloseConfig()
        {
            configBuildingId = -1;
            choiceSignature = "";
            if (configWindow != null) configWindow.style.display = DisplayStyle.None;
        }

        /// <summary>One line per setting for the right bar: what it makes and how it is prioritised.</summary>
        string SettingsSummary(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            var sb = new StringBuilder("\n");
            if (def.allowedRecipeIds.Count > 0)
            {
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                sb.Append(recipe == null ? "\nNo recipe chosen: press Settings to choose what it makes." : "\nRecipe: " + ItemName(c, recipe.outputItemId) + ".");
            }
            if (def.kind == BuildingKind.Storage)
                sb.Append(b.storedItemIds.Count == 0 ? "\nStoring nothing yet: press Settings to choose the goods to collect."
                    : "\nStoring " + b.storedItemIds.Count + " good" + (b.storedItemIds.Count == 1 ? "" : "s") + " (change in Settings).");
            else if (b.blockedItemIds.Count > 0) sb.Append("\n" + b.blockedItemIds.Count + " good" + (b.blockedItemIds.Count == 1 ? "" : "s") + " switched off in Settings.");
            return sb.ToString();
        }

        /// <summary>Copies the selected building's settings (recipe, priority, sell or accept list).</summary>
        public void CopySettings()
        {
            var w = loop.World;
            var b = w != null ? WorldLookup.GetBuilding(w, ui.selectedBuildingId) : null;
            if (!HasSettings(loop.Catalog, b)) { ShowStatus("Select one of your buildings to copy its settings.", true); return; }
            var def = CatalogRepository.GetBuilding(loop.Catalog, b.typeId);
            clipboard = new CopiedSettings
            {
                TypeId = b.typeId, DisplayName = def.displayName, RecipeId = b.selectedRecipeId,
                Priority = b.priority, Blocked = new List<string>(b.blockedItemIds), Stored = new List<string>(b.storedItemIds)
            };
            ShowStatus("Copied " + def.displayName + " settings.", false);
        }

        /// <summary>
        /// Pastes copied settings onto the selected building: everything onto the same kind of building, just the
        /// priority onto any other that takes deliveries. Each change goes through the normal commands.
        /// </summary>
        public void PasteSettings()
        {
            var w = loop.World;
            var c = loop.Catalog;
            var b = w != null ? WorldLookup.GetBuilding(w, ui.selectedBuildingId) : null;
            if (clipboard == null) { ShowStatus("Nothing copied yet: select a building and press Copy settings.", true); return; }
            if (!HasSettings(c, b)) { ShowStatus("Select one of your buildings to paste onto.", true); return; }
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            bool sameKind = b.typeId == clipboard.TypeId;
            int changes = 0;
            var problems = new List<string>();
            if (sameKind && !string.IsNullOrEmpty(clipboard.RecipeId) && clipboard.RecipeId != b.selectedRecipeId)
            {
                var r = loop.Submit(new GameCommand { kind = CommandKind.SelectRecipe, actor = Me, buildingId = b.id, contentId = clipboard.RecipeId });
                if (r.success) changes++; else problems.Add(r.message);
            }
            if (sameKind)
            {
                // Sell and accept lists are toggles: switch each good whose state differs.
                bool storage = def.kind == BuildingKind.Storage;
                var differ = new HashSet<string>(storage ? clipboard.Stored : clipboard.Blocked);
                differ.SymmetricExceptWith(storage ? b.storedItemIds : b.blockedItemIds);
                foreach (var item in differ)
                {
                    var r = loop.Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = Me, buildingId = b.id, contentId = item });
                    if (r.success) changes++; else problems.Add(r.message);
                }
            }
            if (DeliveryPriority.Applies(def) && clipboard.Priority != b.priority)
            {
                var r = loop.Submit(new GameCommand { kind = CommandKind.SetPriority, actor = Me, buildingId = b.id, contentId = DeliveryPriority.Names[DeliveryPriority.Clamp(clipboard.Priority) - DeliveryPriority.Min] });
                if (r.success) changes++; else problems.Add(r.message);
            }
            choiceSignature = "";
            if (problems.Count > 0) ShowStatus("Pasted with problems: " + problems[0], true);
            else ShowStatus(changes == 0 ? "Already matches the copied settings."
                : sameKind ? "Pasted " + clipboard.DisplayName + " settings." : "Pasted the copied priority (other settings only fit a " + clipboard.DisplayName + ").", false);
        }

        /// <summary>A pad's range, where it sends, its flight time and what feeds it.</summary>
        string PadDetails(WorldState w, ContentCatalog c, BuildingState pad)
        {
            var player = WorldLookup.GetFaction(w, Me);
            var sb = new StringBuilder("\n\nDrone range: ").Append(DroneLinks.Range(c, player).ToString("0")).Append(" units (the ring). ")
                .Append("Carries ").Append(DroneLinks.Capacity(c, player)).Append(" per trip.");
            var link = DroneLinks.Outgoing(w, pad);
            if (link == null) sb.Append("\nNot linked: press Link and click a tile on another asteroid you hold.");
            else
            {
                float oneWay = BeltNetwork.TravelTicks(w, c, link) * c.rules.tickMilliseconds / 1000f;
                sb.Append("\nSends to cell ").Append(link.toCellId).Append(": ").Append(oneWay.ToString("0.0")).Append("s each way")
                  .Append(BeltNetwork.DroneAway(w, link) ? " (drone flying home)." : ".");
            }
            int incoming = w.belts.FindAll(s => s.fromCellId != s.toCellId && s.toCellId == pad.cellId && s.toX == pad.x && s.toY == pad.y).Count;
            if (incoming > 0) sb.Append("\nReceives from ").Append(incoming).Append(incoming == 1 ? " pad." : " pads.");
            return sb.ToString();
        }

        static string ToolTopic(UiTool tool)
        {
            switch (tool)
            {
                case UiTool.BuildExtractor: return "extractor";
                case UiTool.BuildRefinery: return "refinery";
                case UiTool.BuildAssembler: return "assembler";
                case UiTool.BuildDepot: return "sale_depot";
                case UiTool.BuildShipyard: return "shipyard";
                case UiTool.BuildFort: return "fort_site";
                case UiTool.BuildStorage: return "storage";
                case UiTool.BuildPurifier: return "purifier";
                case UiTool.BuildSolarArray: return "solar_array";
                case UiTool.BuildDronePad: return "drone_pad";
                case UiTool.DrawBelt: return "belts";
                default: return null;
            }
        }

        /// <summary>Shows or hides a tool together with its "?" row.</summary>
        void ShowTool(UiTool tool, bool visible)
        {
            var b = toolButtons[tool];
            Show(b.parent != null && b.parent.ClassListContains("tool-row") ? b.parent : b, visible);
        }

        static string ToolHint(UiTool tool)
        {
            switch (tool)
            {
                case UiTool.BuildExtractor: return "Click a resource node in a cell you own. Right-click or Esc to stop.";
                case UiTool.Dismantle: return "Click one of your machines, or drag across belts, to dismantle them (half the cost is refunded).";
                case UiTool.BuildStorage: return "Storage collects surplus from connected producers and supplies any connected machine. Run several lines into it to join them up.";
                case UiTool.BuildFort: return "Place on an edge tile of a cell you hold, facing the cell to expand into. Belt in the materials; once full and the neighbour is claimed, it deploys a Fort there.";
                case UiTool.BuildShipyard: return "Shipyards need two adjacent tiles. Z or right-click rotates the preview; Shift+Z turns it back. Left-click to place, Esc to cancel.";
                case UiTool.BuildSolarArray: return "A Solar Array adds solar power. It takes its goods from your Storage and needs a free slot (3 at your HQ, 2 per Fort cell). Click an empty tile there.";
                case UiTool.BuildPurifier: return "A Water Purifier turns Water from a refinery into Purified Water. Click an empty tile you own.";
                case UiTool.BuildDronePad: return "A Drone Pad sends goods to another of your asteroids. Place it, belt goods into it, then select it and press Link.";
                case UiTool.LinkDronePad: return "Click a tile on another of your asteroids inside the range ring. A receiving pad is built there. Right-click or Esc to cancel.";
                case UiTool.RouteBelt: return "Click the machine the goods come from, then the one they go to: the shortest free path is laid at your belt speed. Esc to stop.";
                case UiTool.DrawBelt: return "Drag from a machine across neighbouring tiles; each step is a one-way belt. Branch or merge by starting on an existing belt.";
                case UiTool.Select: return "Left-click to select. With your craft selected, right-click a cell to move, attack, or hold its control point.";
                default: return "Click an empty tile in a cell you own. Right-click or Esc to stop.";
            }
        }

        // ---------------------------------------------------------------- commands from buttons

        void Submit(GameCommand cmd)
        {
            var r = loop.Submit(cmd);
            ShowStatus(string.IsNullOrEmpty(r.message) ? (r.success ? "Done." : r.code) : r.message, !r.success);
        }

        /// <summary>A one-choice pop-up (a machine's recipe, a shipyard's craft): once the choice is made, close it.</summary>
        void SubmitAndClose(GameCommand cmd)
        {
            var r = loop.Submit(cmd);
            ShowStatus(string.IsNullOrEmpty(r.message) ? (r.success ? "Done." : r.code) : r.message, !r.success);
            if (r.success) CloseConfig();
        }

        Label clockLabel;

        /// <summary>Elapsed game time, "m:ss" (or "h:mm:ss" past the hour), from the match's tick count.</summary>
        public static string GameClock(WorldState w, ContentCatalog c)
        {
            if (w == null || c == null) return "0:00";
            long seconds = (long)w.tick * c.rules.tickMilliseconds / 1000;
            long h = seconds / 3600, m = seconds / 60 % 60, sec = seconds % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + sec.ToString("00") : m + ":" + sec.ToString("00");
        }

        /// <summary>
        /// The clock's hover text: what game time is, and when each AI may first attack this match (its profile's grace
        /// period, shifted by the match's variety), so its offence can be watched for.
        /// </summary>
        static string ClockTooltip(WorldState w, ContentCatalog c)
        {
            var sb = new StringBuilder("Game time: how long this match has been played (faster at 2x and 4x, stopped when paused).");
            foreach (var f in w.factions)
            {
                if (!f.aiControlled || f.suppressed) continue;
                var p = (string.IsNullOrEmpty(f.aiProfileId) ? null : AiProfiles.Get(f.aiProfileId)) ?? AiProfile.For(w.difficulty);
                var varied = AiProfile.Varied(p, w, f.id);
                sb.Append('\n').Append(Perspective.Spectating ? Perspective.SeatName(f.id) : SideLabel(w, f.id)).Append(" (").Append(p.Name).Append("): ");
                if (varied.EarliestAttackTick == int.MaxValue || !c.rules.aiRaids) sb.Append("never attacks");
                else
                {
                    var at = new WorldState { tick = varied.EarliestAttackTick };
                    sb.Append("first attack no sooner than ").Append(GameClock(at, c));
                }
                sb.Append(c.rules.aiExpands ? ", expands" : ", never expands");
            }
            return sb.ToString();
        }

        VisualElement elsewherePanel;
        string elsewhereSignature = "";

        /// <summary>
        /// The battles the player is not part of (other seats fighting each other), each a button that watches it: the
        /// camera goes there and the battle panel shows it, read-only. Clicking the watched one again stops watching.
        /// Hidden when there are none, and for a spectator, who sees every battle anyway.
        /// </summary>
        void RefreshElsewhere(WorldState w, ContentCatalog c)
        {
            if (ui.watchBattleId >= 0 && WorldLookup.GetBattle(w, ui.watchBattleId) == null) ui.watchBattleId = -1; // it ended
            var others = Perspective.Spectating ? new List<EncounterState>() : w.encounters.FindAll(e => e.active && !Perspective.InBattle(e));
            var sb = new System.Text.StringBuilder();
            foreach (var e in others)
                sb.Append(e.id).Append(':').Append(e.turn).Append(':').Append(EncounterService.TicksLeft(w, e) * c.rules.tickMilliseconds / 1000).Append(';');
            sb.Append(ui.watchBattleId);
            string sig = sb.ToString();
            if (sig == elsewhereSignature) return;
            elsewhereSignature = sig;
            elsewherePanel.Clear();
            Show(elsewherePanel, others.Count > 0);
            if (others.Count == 0) return;
            var title = new Label("BATTLES ELSEWHERE");
            title.AddToClassList("panel-title");
            elsewherePanel.Add(title);
            foreach (var e in others)
            {
                int id = e.id;
                bool watching = ui.watchBattleId == id;
                var sides = new List<string>();
                foreach (var f in e.sides) sides.Add(SideLabel(w, f));
                int seconds = (EncounterService.TicksLeft(w, e) * c.rules.tickMilliseconds + 999) / 1000;
                var b = new Button(() => { ui.watchBattleId = ui.watchBattleId == id ? -1 : id; elsewhereSignature = ""; })
                {
                    text = "Cell " + e.targetCellId + ": " + string.Join(" v ", sides) + "  -  " + SideLabel(w, e.turn) + "'s turn "
                        + (seconds / 60) + ":" + (seconds % 60).ToString("00") + (watching ? "\nWatching - click to stop" : "\nClick to watch")
                };
                b.AddToClassList("tool");
                b.EnableInClassList("tool-active", watching);
                elsewherePanel.Add(b);
            }
        }

        /// <summary>The id of the battle the panel shows, or -1.</summary>
        int FocusedBattleId()
        {
            var e = Perspective.FocusBattle(loop.World, ui.selectedCraftId, ui.watchBattleId);
            return e != null ? e.id : -1;
        }

        void CaptureSelected()
        {
            var craft = WorldLookup.GetCraft(loop.World, ui.selectedCraftId);
            if (craft == null) { ShowStatus("Select one of your craft first.", true); return; }
            Submit(new GameCommand { kind = CommandKind.StartCapture, actor = Me, craftId = craft.id, targetCellId = craft.cellId });
        }

        /// <summary>
        /// The trade-win hold bar beside the income readout: shown once the rate reaches the target, it fills over
        /// the hold time (smoothly between the five-second checks) and empties if the rate slips.
        /// </summary>
        void RefreshTradeHold(bool playing)
        {
            if (tradeHold == null) return;
            var w = playing ? loop.World : null;
            var c = loop.Catalog;
            bool show = w != null && !(Perspective.Spectating && Perspective.Overview) && (MyTrade(w).heldSlices > 0 || MyTrade(w).won);
            Show(tradeHold, show);
            // While the rate is being held the bar takes the Goals line's place in the top bar.
            if (goalLabel != null)
            {
                if (show) goalLabel.style.display = DisplayStyle.None;
                else goalLabel.style.display = StyleKeyword.Null; // back to the stylesheet (hidden in the compact layout)
            }
            if (!show) return;
            float holdSeconds = Math.Max(1, c.rules.tradeWinHoldSeconds);
            float held;
            if (MyTrade(w).won) held = holdSeconds;
            else
            {
                float sliceTicks = Math.Max(1, TradeService.SliceSeconds * 1000 / Math.Max(1, c.rules.tickMilliseconds));
                float into = (w.tick % (int)sliceTicks + (w.mode == MatchMode.Factory ? loop.TickFraction : 0f)) / sliceTicks;
                held = Mathf.Min(holdSeconds, (MyTrade(w).heldSlices + into) * TradeService.SliceSeconds);
            }
            tradeHoldFill.style.width = Length.Percent(held * 100f / holdSeconds);
            string text = MyTrade(w).won ? "Trade win!" : (compact ? "" : "Trade win: ") + Mathf.FloorToInt(held) + "s / " + (int)holdSeconds + "s";
            if (tradeHoldLabel.text != text) tradeHoldLabel.text = text;
        }

        /// <summary>Draws (or hides) the box being dragged to select units; corners in screen pixels.</summary>
        public void ShowSelectBox(bool show, Vector2 a, Vector2 b)
        {
            if (selectBox == null) return;
            Show(selectBox, show && root.panel != null);
            if (!show || root.panel == null) return;
            var pa = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(a.x, Screen.height - a.y));
            var pb = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(b.x, Screen.height - b.y));
            selectBox.style.left = Mathf.Min(pa.x, pb.x);
            selectBox.style.top = Mathf.Min(pa.y, pb.y);
            selectBox.style.width = Mathf.Abs(pa.x - pb.x);
            selectBox.style.height = Mathf.Abs(pa.y - pb.y);
        }

        /// <summary>Opens or closes the production overview (also the P key).</summary>
        public void ToggleProduction()
        {
            productionOpen = !productionOpen;
            productionFrame = 0;
        }

        /// <summary>
        /// The production overview: per item, how much the player made, used and sold over the last minute, and what
        /// was left over (made less used and sold; negative when stock is being drawn down). Above it, sales per
        /// minute and how close the trade win is.
        /// </summary>
        void RefreshProduction()
        {
            var w = loop.World;
            var c = loop.Catalog;
            var f = WorldLookup.GetFaction(w, Me);
            if (w == null || f == null || productionList == null) return;
            var stats = loop.Services.Factory.Stats;
            float window = stats.WindowSeconds(c);
            float perMinute = stats.CreditsPerMinute(c, Me);
            int target = TradeService.Target(w, c);
            string hold = MyTrade(w).won ? "trade win reached"
                : MyTrade(w).heldSlices > 0 ? "at the target: hold " + (c.rules.tradeWinHoldSeconds - TradeService.HeldSeconds(w, Perspective.Viewer)) + "s more"
                : (target - MyTrade(w).rate).ToString("N0") + " a minute short";
            productionSummary.text = "Sales: " + Mathf.RoundToInt(perMinute).ToString("N0") + " credits a minute  ·  Trade win (" + w.difficulty + "): "
                + MyTrade(w).rate.ToString("N0") + " / " + target.ToString("N0") + " a minute of goods you make, held "
                + c.rules.tradeWinHoldSeconds + "s  ·  " + hold
                + (window < 59f ? "\n(Measuring: " + Mathf.FloorToInt(window) + "s of the last minute so far.)" : "");
            productionList.Clear();
            int rows = 0;
            foreach (var item in c.items)
            {
                float made = stats.PerMinute(c, Me, ProductionStats.Kind.Made, item.id);
                float used = stats.PerMinute(c, Me, ProductionStats.Kind.Used, item.id);
                float sold = stats.PerMinute(c, Me, ProductionStats.Kind.Sold, item.id);
                if (made < 0.05f && used < 0.05f && sold < 0.05f) continue;
                float spare = made - used - sold;
                var row = new VisualElement();
                row.AddToClassList("production-row");
                row.Add(ProductionCell(item.displayName, "production-name"));
                row.Add(ProductionCell(PerMinuteText(made), null));
                row.Add(ProductionCell(PerMinuteText(used), null));
                row.Add(ProductionCell(PerMinuteText(sold), null));
                row.Add(ProductionCell((spare > 0.05f ? "+" : "") + PerMinuteText(spare), spare < -0.05f ? "production-short" : spare > 0.05f ? "production-spare" : null));
                productionList.Add(row);
                rows++;
            }
            if (rows == 0) productionList.Add(ProductionCell("Nothing made in the last minute yet.", "production-name"));
        }

        static Label ProductionCell(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList("production-cell");
            if (cls != null) l.AddToClassList(cls);
            return l;
        }

        static string PerMinuteText(float perMinute)
        {
            float a = Math.Abs(perMinute);
            string s = a >= 100f ? Mathf.RoundToInt(perMinute).ToString("N0") : a >= 10f ? perMinute.ToString("0") : perMinute.ToString("0.#");
            return s == "-0" ? "0" : s;
        }

        /// <summary>Sends the selected damaged unit to the nearest shipyard it can reach, where it is repaired.</summary>
        void RepairSelected()
        {
            var w = loop.World;
            var units = WorldInput.SelectedUnits(w, ui);
            if (units.Count > 1)
            {
                // A group: every damaged unit heads for its own nearest yard.
                int sent = 0, docked = 0, stuck = 0;
                foreach (var k in units)
                {
                    var d = CatalogRepository.GetCraft(loop.Catalog, k.typeId);
                    if (d == null || k.currentHealth >= d.maxHealth) continue;
                    int at = FactorySimulation.NearestRepairYard(w, loop.Catalog, loop.Map, loop.Services.Movement, k);
                    if (at < 0) { stuck++; continue; }
                    if (at == k.cellId) { docked++; continue; }
                    if (loop.Submit(new GameCommand { kind = CommandKind.MoveWorld, actor = Me, craftId = k.id, targetCellId = at }).success) sent++;
                    else stuck++;
                }
                ShowStatus(sent + " damaged unit" + (sent == 1 ? "" : "s") + " heading for repairs"
                    + (docked > 0 ? ", " + docked + " already docked" : "") + (stuck > 0 ? ", " + stuck + " with no shipyard in reach" : "") + ".", sent == 0 && docked == 0);
                return;
            }
            var craft = WorldLookup.GetCraft(w, ui.selectedCraftId);
            if (craft == null || craft.owner != Me) { ShowStatus("Select one of your craft first.", true); return; }
            int yard = FactorySimulation.NearestRepairYard(w, loop.Catalog, loop.Map, loop.Services.Movement, craft);
            if (yard < 0) { ShowStatus("No shipyard of yours can be reached. Build one, or clear the way.", true); return; }
            if (yard == craft.cellId)
            {
                var def = CatalogRepository.GetCraft(loop.Catalog, craft.typeId);
                ShowStatus("Docked at your shipyard in cell " + yard + ". It is repaired while the yard is not building a ship"
                    + (def != null && def.repairParts.Count > 0 ? ", using " + EconomyService.IngredientText(loop.Catalog, def.repairParts) + " per HP." : "."), false);
                return;
            }
            var r = loop.Submit(new GameCommand { kind = CommandKind.MoveWorld, actor = Me, craftId = craft.id, targetCellId = yard });
            ShowStatus(r.success ? "Heading to your shipyard in cell " + yard + " for repairs." : r.message, !r.success);
        }

        void AssignSelected()
        {
            var craft = WorldLookup.GetCraft(loop.World, ui.selectedCraftId);
            if (craft == null) { ShowStatus("Select one of your craft first.", true); return; }
            Submit(new GameCommand { kind = CommandKind.AssignDefender, actor = Me, craftId = craft.id, targetCellId = craft.cellId });
        }

        void DismantleSelected()
        {
            if (ui.selectedBuildingId < 0) { ShowStatus("Select a structure first.", true); return; }
            Submit(new GameCommand { kind = CommandKind.Dismantle, actor = Me, buildingId = ui.selectedBuildingId });
            ui.selectedBuildingId = -1;
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(WorldState w, ContentCatalog c, CommandResult last)
        {
            if (w == null) return;
            var player = WorldLookup.GetFaction(w, Me);
            // The battle the panel shows; the factory keeps running around it.
            var battle = Perspective.FocusBattle(w, ui.selectedCraftId, ui.watchBattleId);
            bool tactical = battle != null;
            bool factory = w.mode == MatchMode.Factory;

            clockLabel.text = GameClock(w, c);
            clockLabel.tooltip = ClockTooltip(w, c);
            bool overview = Perspective.Spectating && Perspective.Overview;
            currencyLabel.text = overview ? "Watching · every seat"
                : (Perspective.Spectating ? "Watching · " + Perspective.SeatName(Me) + ": " : "")
                + "Credits: " + player.currency.ToString("N0") + "  ·  Ion: " + player.ion.ToString("N0");
            // The overview has no one seat's power or income: the spectator panel lists every seat's instead.
            if (powerLabel != null) powerLabel.style.display = overview ? DisplayStyle.None : StyleKeyword.Null;
            salesLabel.style.display = overview ? DisplayStyle.None : StyleKeyword.Null;
            // A spectator gives no orders, so the build tools are put away.
            var buildPanel = root.Q<VisualElement>("build-panel");
            if (buildPanel != null) Show(buildPanel, !Perspective.Spectating);
            if (powerLabel != null)
            {
                // Spare power (supply minus demand), like a rate: positive is headroom, negative slows every machine.
                int spare = player.powerSupply - player.powerDemand;
                bool low = spare < 0;
                powerLabel.text = "Power: " + player.powerDemand + " / " + player.powerSupply + " MW ("
                    + (spare >= 0 ? "+" : "") + spare
                    + (low ? " · " + (player.powerSupply * 100 / Math.Max(1, player.powerDemand)) + "% speed" : "") + ")";
                powerLabel.EnableInClassList("power-low", low);
            }
            int target = TradeService.Target(w, c);
            {
                // Income like power: the rate of goods made and sold (credits a minute, last 30 s) against the target.
                int gap = MyTrade(w).rate - target;
                bool met = gap >= 0;
                salesLabel.text = (compact ? "" : "Income: ") + MyTrade(w).rate.ToString("N0") + " / " + target.ToString("N0") + " a min ("
                    + (met ? "+" : "") + gap.ToString("N0") + ")";
                salesLabel.EnableInClassList("income-met", met);
                salesLabel.EnableInClassList("income-low", !met && MyTrade(w).rate > 0);
            }
            bool salesDone = MyTrade(w).won;
            // Conquest: every enemy seat's headquarters taken.
            int rivals = 0, fallen = 0;
            foreach (var f in w.factions)
                if (WorldLookup.AreEnemies(w, Me, f.id)) { rivals++; if (f.suppressed) fallen++; }
            bool hqDone = rivals > 0 && fallen == rivals;
            string trade = salesDone ? "MET"
                : MyTrade(w).heldSlices > 0 ? "holding " + TradeService.HeldSeconds(w, Perspective.Viewer) + "s / " + c.rules.tradeWinHoldSeconds + "s"
                : Mathf.Min(99, (int)(MyTrade(w).rate * 100L / Math.Max(1, target))) + "%";
            goalLabel.text = overview
                ? "Goals: sell " + target.ToString("N0") + " a min for " + c.rules.tradeWinHoldSeconds + "s, or take every rival HQ"
                : "Goals (" + w.difficulty + "): Trade " + trade + "  ·  "
                + (rivals == 1 ? "Enemy HQ " + (hqDone ? "CAPTURED" : "standing") : "Enemy HQs " + fallen + " / " + rivals + " taken");
            goalLabel.EnableInClassList("goal-done", salesDone && hqDone);

            // Tool buttons and costs.
            SetCost(UiTool.BuildExtractor, c, "extractor", player, factory);
            SetCost(UiTool.BuildRefinery, c, "refinery", player, factory);
            SetCost(UiTool.BuildAssembler, c, "assembler", player, factory);
            SetCost(UiTool.BuildDepot, c, "sale_depot", player, factory);
            SetCost(UiTool.BuildShipyard, c, "shipyard", player, factory);
            SetCost(UiTool.BuildFort, c, "fort_site", player, factory);
            SetCost(UiTool.BuildStorage, c, "storage", player, factory);
            SetCost(UiTool.BuildPurifier, c, "purifier", player, factory);
            SetCost(UiTool.BuildSolarArray, c, "solar_array", player, factory);
            SetCost(UiTool.BuildDronePad, c, "drone_pad", player, factory);
            toolButtons[UiTool.Dismantle].SetEnabled(factory);
            {
                int tier = Mathf.Clamp(ui.beltTier, 0, c.rules.beltTiers.Count - 1);
                int perStep = c.rules.beltCost + TierUpgradeCost(c, tier);
                toolButtons[UiTool.DrawBelt].text = "Belt: " + c.rules.beltTiers[tier].name + " (drag) - " + perStep + " per step";
            }
            toolButtons[UiTool.DrawBelt].SetEnabled(factory);
            if (toolButtons.ContainsKey(UiTool.RouteBelt)) toolButtons[UiTool.RouteBelt].SetEnabled(factory);
            foreach (var kv in toolButtons) kv.Value.EnableInClassList("tool-active", ui.activeTool == kv.Key);
            if (beltViewButton != null) beltViewButton.EnableInClassList("tool-active", ui.beltView);

            Show(tacticalPanel, tactical);
            Show(tacticalBanner, tactical);
            if (tactical) RefreshTactical(w, c, battle);
            else ShowPendingDefenderChoices(null, null);
            RefreshElsewhere(w, c);

            RefreshSelection(w, c);
            RefreshAlerts(w);
            RefreshSave();
            RefreshSpectator(w, c);
            RefreshLockstep(w, c);

            if (Time.unscaledTime > statusUntil && statusLabel.text.Length > 0 && !tactical) { statusLabel.text = ""; Show(statusLabel, false); }
            if (w.outcome != shownOutcome) ShowResult(w.outcome);
            else if (!outShown && !Perspective.Spectating && w.outcome == MatchOutcome.None && WorldLookup.IsSuppressed(w, Me)) ShowOut(w);
        }

        // ---------------------------------------------------------------- lockstep

        string lockstepNote = "";

        /// <summary>In a networked match: who the game is waiting for, or that this machine has fallen out of step.</summary>
        void RefreshLockstep(WorldState w, ContentCatalog c)
        {
            var session = loop.Lockstep;
            if (session == null) return;
            string note = "";
            bool error = false;
            if (session.DesyncTick >= 0)
            {
                note = "Out of step with " + Perspective.SeatName(session.DesyncWith) + "'s machine since "
                    + GameClock(new WorldState { tick = session.DesyncTick }, c) + ": this game no longer matches theirs.";
                error = true;
            }
            else if (loop.WaitingFor != FactionId.Neutral) note = "Waiting for " + Perspective.SeatName(loop.WaitingFor) + "...";
            if (note == lockstepNote) return;
            // Clearing takes away only this note, never a newer message (such as an AI taking over the seat).
            bool showing = statusLabel.text == Pointer.Words(lockstepNote);
            lockstepNote = note;
            if (note.Length > 0) ShowStatus(note, error);
            else if (showing) ShowStatus("", false);
        }

        // ---------------------------------------------------------------- spectating

        /// <summary>Every control that gives an order; a spectator only looks, so all are put away.</summary>
        static readonly string[] OrderControls =
        {
            "build-panel", "undo-button", "top-research", "research-button", "priority-panel", "config-open",
            "craft-orders", "craft-order-hint", "claim-button", "repair-button", "group-all-button", "group-clear-button",
            "pad-link-button", "pad-unlink-button", "belt-upgrade-line", "belt-upgrade-one", "dismantle-button",
            "pending-defender", "capture-button", "assign-defender-button", "end-turn-button", "retreat-button",
            "place-recipe", "dock-build", "touch-cancel", "touch-rotate", "touch-group",
        };

        VisualElement spectatorPanel, spectatorTabs, spectatorRows;
        Label spectatorHint;
        readonly List<Button> spectatorTabButtons = new List<Button>();
        readonly List<SeatRow> seatRows = new List<SeatRow>();
        string spectatorSeats = "";
        bool ordersHidden;

        sealed class SeatRow
        {
            public FactionId Seat;
            public Label Name, Status, Income, Standing;
        }

        /// <summary>
        /// Steps a spectator's view through the overview and each seat in the match (Tab, or Shift+Tab backwards).
        /// </summary>
        public void CycleSpectatorView(int step)
        {
            var w = loop != null ? loop.World : null;
            if (w == null || !Perspective.Spectating || w.factions.Count == 0) return;
            // Position 0 is the overview, then the seats in order.
            int at = Perspective.Overview ? 0 : 1 + Math.Max(0, w.factions.FindIndex(f => f.id == Perspective.Viewer));
            int count = w.factions.Count + 1;
            at = ((at + step) % count + count) % count;
            if (at == 0) WatchAll();
            else Watch(w.factions[at - 1].id);
        }

        /// <summary>Follows one seat: its own credits, power, income, goals and alerts, read-only.</summary>
        void Watch(FactionId seat)
        {
            Perspective.Overview = false;
            Perspective.Viewer = seat;
            ForgetSignatures();
        }

        /// <summary>The overview of every seat.</summary>
        void WatchAll()
        {
            Perspective.Overview = true;
            ForgetSignatures();
        }

        /// <summary>Lists cached by signature are worded for a seat, so a new view rebuilds them.</summary>
        void ForgetSignatures()
        {
            choiceSignature = craftListSignature = pendingSignature = needsSignature = "";
        }

        void RefreshSpectator(WorldState w, ContentCatalog c)
        {
            if (!Perspective.Spectating)
            {
                if (spectatorPanel != null) Show(spectatorPanel, false);
                if (ordersHidden)
                {
                    // Back to playing: the stylesheet and the refresh decide again what shows.
                    foreach (var name in OrderControls) Find(name).style.display = StyleKeyword.Null;
                    ordersHidden = false;
                }
                return;
            }
            if (spectatorPanel == null) BuildSpectatorPanel();
            Show(spectatorPanel, true);
            foreach (var name in OrderControls) Show(Find(name), false);
            ordersHidden = true;
            if (ConfigOpen) CloseConfig();

            // Tabs and rows are rebuilt only when the seats in the match change (a new match).
            string seats = "";
            foreach (var f in w.factions) seats += (int)f.id + ",";
            if (seats != spectatorSeats) BuildSeatTabs(w);
            spectatorSeats = seats;

            spectatorTabButtons[0].EnableInClassList("tool-active", Perspective.Overview);
            for (int i = 1; i < spectatorTabButtons.Count; i++)
                spectatorTabButtons[i].EnableInClassList("tool-active", !Perspective.Overview && w.factions[i - 1].id == Perspective.Viewer);
            Show(spectatorRows, Perspective.Overview);
            spectatorHint.text = Perspective.Overview
                ? "Every seat at a glance. Click a seat, or press Tab, to see its own view."
                : "Seat " + (int)Perspective.Viewer + "'s view: its credits, power, income and alerts. Tab for the next.";
            if (!Perspective.Overview) return;

            int target = TradeService.Target(w, c);
            foreach (var row in seatRows)
            {
                var f = WorldLookup.GetFaction(w, row.Seat);
                if (f == null) continue;
                int cells = 0;
                foreach (var cell in w.cells) if (cell.owner == f.id) cells++;
                bool fighting = w.encounters.Exists(e => e.active && e.sides.Contains(f.id));
                row.Status.text = w.winners.Contains(f.id) ? "WON" : f.suppressed ? "OUT" : fighting ? "IN BATTLE" : "PLAYING";
                row.Status.style.color = w.winners.Contains(f.id) ? new Color(0.59f, 0.97f, 0.78f)
                    : f.suppressed ? new Color(0.6f, 0.6f, 0.6f) : fighting ? new Color(1f, 0.69f, 0.13f) : new Color(0.85f, 0.85f, 0.9f);
                string trade = f.trade.won ? "trade goal MET"
                    : f.trade.heldSlices > 0 ? "holding " + TradeService.HeldSeconds(w, f.id) + "s / " + c.rules.tradeWinHoldSeconds + "s"
                    : Mathf.Min(99, (int)(f.trade.rate * 100L / Math.Max(1, target))) + "% of the goal";
                row.Income.text = "Income " + f.trade.rate.ToString("N0") + " / " + target.ToString("N0") + " a min · " + trade;
                row.Standing.text = "Cells " + cells + "  ·  Battles " + f.battlesWon + " won, " + f.battlesLost + " lost"
                    + "  ·  Credits " + f.currency.ToString("N0");
            }
        }

        void BuildSpectatorPanel()
        {
            spectatorPanel = new VisualElement { name = "spectator-panel" };
            spectatorPanel.AddToClassList("panel");
            var title = new VisualElement();
            title.AddToClassList("title-row");
            var label = new Label("WATCHING");
            label.AddToClassList("panel-title");
            title.Add(label);
            spectatorPanel.Add(title);
            spectatorTabs = new VisualElement();
            spectatorTabs.style.flexDirection = FlexDirection.Row;
            spectatorTabs.style.flexWrap = Wrap.Wrap;
            spectatorPanel.Add(spectatorTabs);
            spectatorHint = new Label();
            spectatorHint.style.whiteSpace = WhiteSpace.Normal;
            spectatorHint.style.fontSize = 11;
            spectatorHint.style.marginTop = 4;
            spectatorHint.style.marginBottom = 4;
            spectatorPanel.Add(spectatorHint);
            spectatorRows = new VisualElement();
            spectatorPanel.Add(spectatorRows);
            var column = Find("left-column");
            column.Insert(0, spectatorPanel);
        }

        void BuildSeatTabs(WorldState w)
        {
            spectatorTabs.Clear();
            spectatorTabButtons.Clear();
            spectatorRows.Clear();
            seatRows.Clear();
            spectatorTabButtons.Add(new Button(WatchAll) { text = "Overview" });
            foreach (var f in w.factions)
            {
                var seat = f.id;
                var tab = new Button(() => Watch(seat)) { text = Perspective.SeatName(seat) };
                // The seat's colour as an underline, so the label stays readable on the button.
                tab.style.borderBottomWidth = 3;
                tab.style.borderBottomColor = SpaceSpriteLibrary.FactionColor(seat);
                spectatorTabButtons.Add(tab);

                var profile = (string.IsNullOrEmpty(f.aiProfileId) ? null : AiProfiles.Get(f.aiProfileId)) ?? AiProfile.For(w.difficulty);
                var row = new SeatRow { Seat = seat };
                var box = new VisualElement();
                box.style.borderLeftWidth = 3;
                box.style.borderLeftColor = SpaceSpriteLibrary.FactionColor(seat);
                box.style.paddingLeft = 6;
                box.style.marginBottom = 6;
                box.tooltip = "Click to see " + Perspective.SeatName(seat) + "'s own view.";
                box.RegisterCallback<ClickEvent>(_ => Watch(seat));
                var head = new VisualElement();
                head.style.flexDirection = FlexDirection.Row;
                head.style.justifyContent = Justify.SpaceBetween;
                row.Name = new Label(Perspective.SeatName(seat) + " · " + profile.Name + (f.team > 0 ? " · team " + f.team : ""));
                row.Name.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Name.style.color = SpaceSpriteLibrary.FactionColor(seat);
                row.Status = new Label();
                row.Status.style.unityFontStyleAndWeight = FontStyle.Bold;
                head.Add(row.Name);
                head.Add(row.Status);
                box.Add(head);
                row.Income = new Label();
                row.Standing = new Label();
                foreach (var l in new[] { row.Income, row.Standing }) { l.style.fontSize = 11; l.style.whiteSpace = WhiteSpace.Normal; box.Add(l); }
                spectatorRows.Add(box);
                seatRows.Add(row);
            }
            foreach (var b in spectatorTabButtons) spectatorTabs.Add(b);
        }

        /// <summary>
        /// The local seat is out but the match goes on (other teams are still fighting): say so, and offer to watch the
        /// rest play out or start again.
        /// </summary>
        void ShowOut(WorldState w)
        {
            outShown = true;
            resultDismissed = false;
            Show(resultOverlay, true);
            Show(resumeButton, false);
            resultTitle.text = "Out of the match";
            bool allyStanding = w.factions.Exists(f => f.id != Me && !f.suppressed && WorldLookup.AreAllies(w, Me, f.id));
            resultText.text = "Your headquarters has fallen and your side's craft have withdrawn."
                + (allyStanding ? "\nYour ally fights on: if your team wins, you win with it." : "\nThe other sides fight on without you.");
            continueButton.text = "Watch";
            Show(continueButton, true);
            Show(newGameButton, true);
        }

        void SetCost(UiTool tool, ContentCatalog c, string buildingId, FactionState player, bool factory)
        {
            var def = CatalogRepository.GetBuilding(c, buildingId);
            if (def == null) return;
            if (!ResearchService.IsBuildingUnlocked(c, player, def.id))
            {
                // Undiscovered buildings stay off the toolbar until their research is in sight.
                var gate = ResearchService.BuildingGate(c, def.id);
                bool known = gate != null && ResearchService.Visibility(c, player, gate) >= ResearchVisibility.Open;
                ShowTool(tool, known);
                toolButtons[tool].text = def.displayName + " (research " + (gate != null ? gate.displayName : "") + ")";
                toolButtons[tool].SetEnabled(false);
                return;
            }
            ShowTool(tool, true);
            bool goods = def.goodsCost.Count == 0 || EconomyService.HasStoredGoods(loop.World, c, Me, def.goodsCost);
            // Goods are listed in the tool hint; the button only flags that they are needed.
            toolButtons[tool].text = (def.kind == BuildingKind.ConstructionZone ? "Fort Zone" : def.displayName) + " - " + def.cost
                + (def.materials.Count > 0 ? " + belt goods" : "")
                + (def.goodsCost.Count > 0 ? " + goods " + StoredProgress(c, def.goodsCost) : "");
            // Buildings that take stored goods stay clickable when short, so choosing the tool explains what is missing.
            toolButtons[tool].SetEnabled(factory && player.currency >= def.cost && (goods || def.goodsCost.Count > 0));
        }

        void RefreshTactical(WorldState w, ContentCatalog c, EncounterState e)
        {
            bool mine = e.turn == Me && !Perspective.Spectating;
            int limit = c.rules.battleRoundLimit;
            string round = "cell " + e.targetCellId + ", round " + e.round + (limit > 0 ? " of " + limit : "");
            // The turn timer: a clock, and in its last stretch a burning fuse.
            int left = EncounterService.TicksLeft(w, e);
            int seconds = (left * c.rules.tickMilliseconds + 999) / 1000;
            bool fuse = left <= c.rules.battleFuseTicks;
            string clock = (fuse ? "FUSE " : "") + (seconds / 60) + ":" + (seconds % 60).ToString("00");
            int others = w.encounters.FindAll(x => x.active && x != e && (Perspective.Spectating || x.sides.Contains(Me))).Count;
            turnLabel.text = (Perspective.InBattle(e) ? "" : "Watching: ") + (mine ? "Your turn - " : SideLabel(w, e.turn) + " turn - ") + clock + "  -  " + round
                + (mine ? "" : " (watch the map)") + (others > 0 ? "  -  +" + others + " more battle" + (others > 1 ? "s" : "") : "");
            turnLabel.EnableInClassList("turn-ai", !mine);
            turnLabel.style.color = fuse ? new StyleColor(new Color(1f, 0.45f, 0.2f)) : new StyleColor(StyleKeyword.Null);
            // Every ship in the battle acts once a turn; ships outside it can join, which uses their turn.
            int idx = e.sides.IndexOf(Me);
            int strikes = idx >= 0 && idx < e.strikes.Count ? e.strikes[idx] : 0;
            actionLabel.text = "Ships ready: " + e.actionsRemaining
                + (strikes > 0 ? "   -  you ran out of time: your turns here are short until you act in time" : "")
                + (limit > 0 && e.round >= limit - 2 ? "   -  " + (e.attacker == Me ? "your" : SideLabel(w, e.attacker) + "'s") + " attack tires after round " + limit : "");
            bool pending = e.pendingDefenderCellId != -1 && e.pendingDefenderOwner == Me;
            var craft = WorldLookup.GetCraft(w, ui.selectedCraftId);
            bool craftReady = mine && craft != null && craft.owner == Me && e.participantCraftIds.Contains(craft.id) && !e.actedCraftIds.Contains(craft.id) && e.actionsRemaining > 0;
            captureButton.SetEnabled(craftReady && !pending);
            assignDefenderButton.SetEnabled(craftReady || pending);
            endTurnButton.SetEnabled(mine && !pending);
            retreatButton.SetEnabled(mine && e.attacker == Me && !pending);
            retreatButton.text = e.attacker == Me ? "Retreat" : "Retreat (attacker only)";

            RefreshBattleLists(w, c, e);

            if (pending)
            {
                var cell = WorldLookup.GetCell(w, e.pendingDefenderCellId);
                ShowPendingDefenderChoices(cell, WorldLookup.CraftsInCell(w, cell.id, Me));
            }
            else ShowPendingDefenderChoices(null, null);

            Show(statusLabel, true);
            var events = loop.Services.Encounters.LastEvent;
            if (Time.unscaledTime > statusUntil && !string.IsNullOrEmpty(events)) statusLabel.text = events;
        }

        /// <summary>
        /// The battle panel's two lists. Your ships: every ship of yours in the battle (ready, done for this turn or
        /// recharging), then nearby ships that could join; click one to select it. Targets: for the selected ship,
        /// every enemy ship or Fort it may strike with the damage it would do and take (click to attack), and greyed
        /// entries for ships a Fort or defender protects or that are out of reach.
        /// </summary>
        void RefreshBattleLists(WorldState w, ContentCatalog c, EncounterState e)
        {
            var selected = WorldLookup.GetCraft(w, ui.selectedCraftId);
            string sig = loop.Revision + "|" + ui.selectedCraftId + "|" + e.id + "|" + e.turn + "|" + e.round;
            if (sig == targetSignature) return;
            targetSignature = sig;
            bool mine = e.turn == Me;

            shipList.Clear();
            var joinable = new List<CraftState>();
            foreach (var k in w.crafts)
            {
                if (k.owner != Me || !WorldLookup.IsCraftLive(k)) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (!e.participantCraftIds.Contains(k.id))
                {
                    // Close enough to join this turn: a battle cell within its movement.
                    int reach = d != null ? Math.Max(1, d.movement) : 1;
                    foreach (int cellId in e.battleCellIds)
                    {
                        int dist = WorldLookup.CellDistance(loop.Map, k.cellId, cellId);
                        if (dist >= 0 && dist <= reach) { joinable.Add(k); break; }
                    }
                    continue;
                }
                string status = k.cooldownTurns > 0 ? "RECHARGING " + k.cooldownTurns
                    : e.actedCraftIds.Contains(k.id) ? "DONE" : mine ? "READY" : "waiting";
                AddShipEntry(c, k, d, "cell " + k.cellId + "  -  HP " + k.currentHealth + "/" + (d != null ? d.maxHealth : 0)
                    + (WorldLookup.IsDefender(w, k) ? "  -  defender" : "") + "  -  " + status, status == "READY" || status == "waiting");
            }
            if (joinable.Count > 0)
            {
                var note = new Label(Pointer.Words("Nearby - select one, then right-click a highlighted cell to bring it in (uses its turn):"));
                note.AddToClassList("battle-note");
                shipList.Add(note);
                foreach (var k in joinable)
                {
                    var d = CatalogRepository.GetCraft(c, k.typeId);
                    AddShipEntry(c, k, d, "cell " + k.cellId + "  -  HP " + k.currentHealth + "/" + (d != null ? d.maxHealth : 0) + "  -  can join", true);
                }
            }

            targetList.Clear();
            bool inBattle = selected != null && selected.owner == Me && e.participantCraftIds.Contains(selected.id);
            targetTitle.text = inBattle ? "TARGETS FOR " + CraftName(c, selected).ToUpperInvariant() : "TARGETS";
            if (!inBattle)
            {
                Note(targetList, "Select one of your ships on the left to see what it can attack.");
                return;
            }
            bool canAct = mine && !e.actedCraftIds.Contains(selected.id) && e.pendingDefenderCellId == -1;
            if (!mine) Note(targetList, "Wait for your turn.");
            else if (e.actedCraftIds.Contains(selected.id)) Note(targetList, CraftName(c, selected) + " has already acted this turn.");
            int count = 0;
            foreach (int cellId in e.battleCellIds)
            {
                bool guarded;
                var targets = CombatResolver.Targets(w, c, cellId, Me, out guarded);
                foreach (var t in targets)
                {
                    int craftTarget = t.IsFort ? -1 : t.Craft.id, fortTarget = t.IsFort ? t.Fort.id : -1;
                    var p = loop.Services.Combat.Preview(w, c, loop.Map, selected.id, cellId, craftTarget, fortTarget);
                    string name = t.IsFort ? "Enemy Fort" : CraftName(c, t.Craft);
                    int hp = p.defenderMaxHealth > 0 ? p.defenderHealth : t.IsFort ? 0 : t.Craft.currentHealth;
                    string text = name + "  -  cell " + cellId + "  -  HP " + hp + "\n" + (p.legal
                        ? "deal " + p.attackDamageAfterArmour + (p.defenderSurvives ? "" : " (destroys it)") + "  -  " + (p.counterDamageAfterArmour > 0 ? "take " + p.counterDamageAfterArmour : "no return fire")
                        : p.reason);
                    int cid = cellId;
                    var button = new Button(() => Submit(new GameCommand { kind = CommandKind.TacticalAttack, actor = Me, craftId = selected.id,
                        targetCellId = cid, targetCraftId = craftTarget, buildingId = fortTarget })) { text = text };
                    button.AddToClassList("battle-entry");
                    button.AddToClassList(p.legal && canAct ? "battle-entry-target" : "battle-entry-blocked");
                    button.SetEnabled(p.legal && canAct);
                    targetList.Add(button);
                    count++;
                }
                if (guarded)
                    foreach (var k in w.crafts)
                    {
                        if (k.cellId != cellId || k.owner == Me || !WorldLookup.IsCraftLive(k) || targets.Exists(t => !t.IsFort && t.Craft.id == k.id)) continue;
                        var hidden = new Button { text = CraftName(c, k) + "  -  cell " + cellId + "\nprotected by its Fort or defender" };
                        hidden.AddToClassList("battle-entry");
                        hidden.AddToClassList("battle-entry-blocked");
                        hidden.SetEnabled(false);
                        targetList.Add(hidden);
                        count++;
                    }
            }
            if (count == 0) Note(targetList, "No enemies in this battle. Move into the target cell to take it.");
        }

        void AddShipEntry(ContentCatalog c, CraftState k, CraftDefinition d, string detail, bool ready)
        {
            int id = k.id;
            var entry = new Button(() => { ui.selectedCraftId = id; ui.selectedBuildingId = -1; ui.selectedNodeId = -1; targetSignature = ""; })
                { text = CraftName(c, k) + "\n" + detail };
            entry.AddToClassList("battle-entry");
            if (!ready) entry.AddToClassList("battle-entry-done");
            if (ui.selectedCraftId == id) entry.AddToClassList("battle-entry-selected");
            shipList.Add(entry);
        }

        static void Note(VisualElement list, string text)
        {
            var label = new Label(text);
            label.AddToClassList("battle-note");
            list.Add(label);
        }

        void RefreshSelection(WorldState w, ContentCatalog c)
        {
            // Compact: picking something on the map opens its Info sheet.
            string key = ui.selectedCraftId + "/" + ui.selectedBuildingId + "/" + ui.selectedNodeId + "/" + ui.selectedBeltId;
            if (compact && key != lastSelectionKey && (ui.selectedCraftId >= 0 || ui.selectedBuildingId >= 0 || ui.selectedNodeId >= 0 || ui.selectedBeltId >= 0)
                && ui.activeTool == UiTool.Select && openSheet != "info")
                OpenSheet("info");
            lastSelectionKey = key;
            var craft = WorldLookup.GetCraft(w, ui.selectedCraftId);
            if (craft != null && (craft.withdrawn || craft.currentHealth <= 0)) { ui.selectedCraftId = -1; craft = null; }
            var building = WorldLookup.GetBuilding(w, ui.selectedBuildingId);
            if (building == null) ui.selectedBuildingId = -1;
            var node = WorldLookup.GetNode(w, ui.selectedNodeId);
            if (node == null) ui.selectedNodeId = -1;
            // A tapped deposit and its extractor are one thing: selecting the deposit shows the extractor's panel.
            if (node != null && building == null && craft == null)
            {
                var tap = WorldLookup.GetBuilding(w, node.extractorBuildingId);
                if (tap != null)
                {
                    ui.selectedBuildingId = tap.id;
                    ui.selectedNodeId = -1;
                    building = tap;
                    node = null;
                }
            }
            var cell = WorldLookup.GetCell(w, ui.selectedCellId);
            var belt = WorldLookup.GetBelt(w, ui.selectedBeltId);
            if (belt == null) ui.selectedBeltId = -1;
            Show(beltUpgradeLine, false);
            Show(beltUpgradeOne, false);
            Show(padLinkButton, false);
            Show(padUnlinkButton, false);
            Show(priorityPanel, false);
            Show(configOpenButton, false);

            // The settings pop-up follows the selection while open, and closes when the selection has no settings.
            if (configBuildingId != -1)
            {
                if (craft != null || building == null || !HasPopupSettings(c, building)) CloseConfig();
                else configBuildingId = building.id;
            }
            if (configWindow != null) configWindow.style.display = configBuildingId != -1 ? DisplayStyle.Flex : DisplayStyle.None;

            // The selection panel's "?" explains whatever is selected.
            string topic = belt != null && craft == null && building == null && node == null ? "belts"
                : craft != null ? "crafts"
                : building != null ? building.typeId
                : node != null ? (node.itemId == c.rules.ionItemId ? "ion" : "resource_nodes")
                : cell != null ? "territory" : "getting_started";
            HelpController.SetTopic(selectionHelp, Help != null && Help.HasTopic(topic) ? topic : "getting_started");

            Show(dismantleButton, false);
            Show(researchButton, false);
            string newChoiceSignature = "";
            if (craft != null)
            {
                selectedCellLabel.text = CraftName(c, craft) + " (cell " + craft.cellId + ")";
                selectionDetails.text = CraftStats(w, c, craft);
            }
            else if (building != null)
            {
                selectedCellLabel.text = BuildingTitle(w, c, building);
                selectionDetails.text = BuildingDetails(w, c, building);
                if (configBuildingId == building.id) newChoiceSignature = ChoiceSignature(w, c, building);
                bool inPlayerCell = WorldLookup.GetCell(w, building.cellId).owner == Me;
                var bdef = CatalogRepository.GetBuilding(c, building.typeId);
                bool hq = bdef != null && bdef.kind == BuildingKind.Headquarters;
                bool own = building.owner == Me && inPlayerCell && !hq;   // Headquarters can never be dismantled
                // A seat that is out of the match leaves its structures behind for whoever holds the cell.
                bool clearAi = building.owner != Me && building.inactiveUntilTick != -1 && WorldLookup.IsSuppressed(w, building.owner) && inPlayerCell;
                dismantleButton.text = own ? "Dismantle (+" + (bdef != null ? bdef.cost * CommandRouter.DismantleRefundPercent / 100 : 0) + " refund)" : "Clear inactive structure";
                dismantleButton.SetEnabled(w.mode == MatchMode.Factory);
                Show(dismantleButton, own || clearAi);
                Show(researchButton, building.owner == Me && hq);
                if (HasPopupSettings(c, building))
                {
                    // The right bar shows what the building is doing; its settings live in the pop-up.
                    selectionDetails.text += SettingsSummary(w, c, building);
                    // A shipyard's pop-up orders craft, so it is called Build and has no settings to copy.
                    bool yard = bdef != null && bdef.kind == BuildingKind.Shipyard;
                    configOpenButton.text = yard ? "Build  [C]" : "Settings  [C]";
                    Show(configOpenButton, configBuildingId != building.id);
                    configTitle.text = (yard ? "BUILD - " : "SETTINGS - ") + (bdef != null ? bdef.displayName.ToUpperInvariant() : building.typeId);
                    Show(configActions, !yard);
                    Show(configClipboardLabel, !yard);
                    configPasteButton.SetEnabled(clipboard != null && w.mode == MatchMode.Factory);
                    configClipboardLabel.text = clipboard == null ? "Copy a building's settings to paste them onto others of the same kind."
                        : "Copied: " + clipboard.DisplayName + " settings. Paste them onto another " + clipboard.DisplayName + " (or just its priority onto any other).";
                }
                if (building.owner == Me && DeliveryPriority.Applies(bdef))
                {
                    // Priority only settles which of several buildings wanting the same goods is served first.
                    Show(priorityPanel, true);
                    int level = DeliveryPriority.Clamp(building.priority);
                    for (int i = 0; i < priorityButtons.Length; i++)
                    {
                        priorityButtons[i].EnableInClassList("priority-active", i + DeliveryPriority.Min == level);
                        priorityButtons[i].SetEnabled(w.mode == MatchMode.Factory);
                    }
                    priorityHint.text = level == 0
                        ? "Normal. Raise it to be served before other buildings that want the same goods; it does not change what this building passes on."
                        : level > 0 ? DeliveryPriority.Label(level) + ": served before lower-priority buildings that want the same goods, and waits on a busy belt rather than lose them."
                        : DeliveryPriority.Label(level) + ": takes only what higher-priority buildings wanting the same goods leave (overflow).";
                }
                if (bdef != null && bdef.kind == BuildingKind.DronePad && building.owner == Me)
                {
                    selectionDetails.text += PadDetails(w, c, building);
                    bool factory = w.mode == MatchMode.Factory;
                    bool linking = ui.activeTool == UiTool.LinkDronePad;
                    padLinkButton.text = linking ? "Click a tile on another asteroid..." : DroneLinks.Outgoing(w, building) != null ? "Relink to another asteroid" : "Link to another asteroid";
                    padLinkButton.SetEnabled(factory && !linking);
                    Show(padLinkButton, true);
                    padUnlinkButton.SetEnabled(factory);
                    Show(padUnlinkButton, DroneLinks.Outgoing(w, building) != null);
                }
            }
            else if (node != null)
            {
                selectedCellLabel.text = (WorldLookup.IsRichNode(w, node) ? "Rich " : "") + ItemName(c, node.itemId) + " node (cell " + node.cellId + ")";
                selectionDetails.text = NodeDetails(w, c, node);
            }
            else if (belt != null)
            {
                var line = loop.Services.Belts.LineOf(w, belt.id);
                var tiers = c.rules.beltTiers;
                var tier = tiers[Mathf.Clamp(belt.tier, 0, tiers.Count - 1)];
                selectedCellLabel.text = OwnerWord(w, belt.owner) + tier.name + " (cell " + belt.fromCellId + ")";
                var sb = new StringBuilder();
                if (belt.linkTicks > 0)
                {
                    // A drone link between asteroids: one item per round trip.
                    float oneWay = BeltNetwork.TravelTicks(w, c, belt) * c.rules.tickMilliseconds / 1000f;
                    selectedCellLabel.text = OwnerWord(w, belt.owner) + "drone link (" + tier.name + ", cell " + belt.fromCellId + " to " + belt.toCellId + ")";
                    sb.Append("Flight: ").Append(oneWay.ToString("0.0")).Append("s each way, so one item every ")
                      .Append((oneWay * 2f).ToString("0.0")).Append("s.\n");
                    if (BeltNetwork.DroneAway(w, belt)) sb.Append("The drone is flying home.\n");
                    sb.Append("Shipping between asteroids is slow: refine and assemble where the ore is, and send finished goods. Upgrading the link speeds the drone up.");
                }
                else
                {
                    sb.Append("Speed: ").Append(tier.ticksPerSegment).Append(" ticks per tile (")
                      .Append((1000f / (tier.ticksPerSegment * c.rules.tickMilliseconds)).ToString("0.#")).Append(" items/s at most).\n");
                    sb.Append("Line: ").Append(line.Count).Append(" belt").Append(line.Count == 1 ? "" : "s").Append(" between junctions or buildings (highlighted).\n");
                    sb.Append("Each belt carries one item at a time. Run a second lane out of a busy producer to carry more.");
                }
                selectionDetails.text = sb.ToString();
                bool mine = belt.owner == Me && w.mode == MatchMode.Factory && WorldLookup.IsBeltActive(w, belt);
                if (mine && belt.tier + 1 < tiers.Count)
                {
                    var next = tiers[belt.tier + 1];
                    int lineCount = line.FindAll(s => s.tier <= belt.tier).Count;
                    var player = WorldLookup.GetFaction(w, Me);
                    selectionDetails.text += belt.linkTicks > 0
                        ? "\nNext: " + next.name + ", " + (BeltNetwork.TravelTicks(w, c, new BeltSegmentState { tier = belt.tier + 1, linkTicks = belt.linkTicks, owner = belt.owner }) * c.rules.tickMilliseconds / 1000f).ToString("0.0") + "s each way, " + next.upgradeCostPerSegment + " to upgrade."
                        : "\nNext: " + next.name + ", " + next.ticksPerSegment + " ticks per tile, " + next.upgradeCostPerSegment + " per belt.";
                    beltUpgradeLine.text = "Upgrade line (" + lineCount + ") - " + next.upgradeCostPerSegment * lineCount;
                    beltUpgradeOne.text = "Upgrade this belt - " + next.upgradeCostPerSegment;
                    beltUpgradeLine.SetEnabled(player.currency >= next.upgradeCostPerSegment * lineCount);
                    beltUpgradeOne.SetEnabled(player.currency >= next.upgradeCostPerSegment);
                    Show(beltUpgradeLine, true);
                    Show(beltUpgradeOne, true);
                }
            }
            else if (cell != null)
            {
                selectedCellLabel.text = CellTitle(w, cell);
                selectionDetails.text = CellDetails(w, c, cell);
            }
            else
            {
                selectedCellLabel.text = "Nothing selected. Click a cell, node, machine or craft.";
                selectionDetails.text = "";
            }
            if (newChoiceSignature != choiceSignature)
            {
                choiceSignature = newChoiceSignature;
                RebuildChoices(w, c, craft == null ? building : null);
            }
            RefreshNeeds(w, c, craft == null ? building : null);
            RefreshAvailability(w, c, craft == null ? building : null);

            craftStats.text = "";
            RefreshCraftOrders(w, c, craft);
            RebuildCraftList(w, c, cell);
            Show(craftList, building == null && node == null);
        }

        /// <summary>Explains how to command the selected unit in normal play and offers a one-click claim.</summary>
        void RefreshCraftOrders(WorldState w, ContentCatalog c, CraftState craft)
        {
            bool show = craft != null && craft.owner == Me && w.mode == MatchMode.Factory;
            Show(craftOrders, show);
            if (!show) return;
            var cell = WorldLookup.GetCell(w, craft.cellId);
            bool holding = w.captureHolds.Exists(h => h.craftId == craft.id);
            bool travelling = craft.worldPathCellIds.Count > 0;
            bool claimable = !travelling && !holding && !WorldLookup.AreAllies(w, cell.owner, Me) && !WorldLookup.HasOpposingCraft(w, cell.id, Me);
            var hold = w.captureHolds.Find(h => h.craftId == craft.id);
            string state = travelling ? "Travelling. "
                : hold != null ? "Claiming cell " + cell.id + ": " + hold.progressTicks * 100 / Math.Max(1, c.rules.normalCaptureTicks) + "%. Stay in the cell until it completes. "
                : "";
            var units = WorldInput.SelectedUnits(w, ui);
            if (units.Count > 1)
            {
                var names = new List<string>();
                foreach (var k in units) { var d = CatalogRepository.GetCraft(c, k.typeId); names.Add(d != null ? d.displayName : k.typeId); }
                state = "GROUP of " + units.Count + ": " + string.Join(", ", names) + ". " + state;
            }
            int here = 0;
            foreach (var k in w.crafts) if (k.owner == Me && k.cellId == craft.cellId && WorldLookup.IsCraftLive(k)) here++;
            Show(groupAllButton, here > 1 && units.Count < here);
            Show(groupClearButton, units.Count > 1);
            if (units.Count > 1)
                craftOrderHint.text = Pointer.Words(state + "Right-click a cell to send them all: each holds, attacks or travels there. ")
                    + (Pointer.TouchMode ? "With Group on, tap a unit to add or remove it." : "Shift-click a unit to add or remove it.");
            else
                craftOrderHint.text = Pointer.Words(state + "Right-click any cell on the map to send this unit there. Arriving in a neutral cell claims it automatically ("
                    + (c.rules.normalCaptureTicks * c.rules.tickMilliseconds / 1000) + "s). To keep a claimed cell, leave the unit there or build a Fort, then the unit can move on. Right-click a cell holding enemy craft to attack it.");
            claimButton.text = claimable ? "Claim cell " + cell.id + " now" : cell.owner == Me ? "Claim (already your cell)" : holding ? "Claiming..." : "Claim (move to a clear cell first)";
            claimButton.SetEnabled(claimable);
            var craftDef = CatalogRepository.GetCraft(c, craft.typeId);
            int hurt = 0;
            foreach (var k in units) { var d = CatalogRepository.GetCraft(c, k.typeId); if (d != null && k.currentHealth < d.maxHealth) hurt++; }
            bool damaged = units.Count > 1 ? hurt > 0 : craftDef != null && craft.currentHealth < craftDef.maxHealth;
            Show(repairButton, damaged);
            if (damaged)
                repairButton.text = units.Count > 1 ? "Return " + hurt + " damaged for repairs"
                    : "Return for repairs (HP " + craft.currentHealth + " / " + craftDef.maxHealth + ")";
        }

        static string CellTitle(WorldState w, CellState cell)
        {
            string owner = cell.owner == Me && !Perspective.Spectating ? "Yours" : cell.owner == FactionId.Neutral ? "Neutral" : SideLabel(w, cell.owner) + "-held";
            string hq = cell.headquartersFor == Me && !Perspective.Spectating ? " · YOUR HQ"
                : cell.headquartersFor != FactionId.Neutral ? " · " + SideLabel(w, cell.headquartersFor).ToUpperInvariant() + " HQ" : "";
            return "Cell " + cell.id + " - " + owner + hq;
        }

        /// <summary>
        /// How the local player names another seat: "AI" or "Enemy", or "Ally", with the seat number when there are
        /// more than two seats, so four seats can be told apart.
        /// </summary>
        /// <summary>"Your ", "Allied " or "Enemy ", to start a thing's name.</summary>
        static string OwnerWord(WorldState w, FactionId owner)
        {
            if (Perspective.Spectating) return Perspective.SeatName(owner) + " ";
            return owner == Me ? "Your " : WorldLookup.AreAllies(w, Me, owner) ? "Allied " : "Enemy ";
        }

        static string SideLabel(WorldState w, FactionId f)
        {
            if (Perspective.Spectating) return Perspective.SeatName(f);
            var fs = WorldLookup.GetFaction(w, f);
            string kind = WorldLookup.AreAllies(w, Me, f) ? "Ally" : fs != null && fs.aiControlled ? "AI" : "Enemy";
            return w.factions.Count > 2 ? kind + " " + (int)f : kind;
        }

        static string ItemName(ContentCatalog c, string itemId)
        {
            var item = CatalogRepository.GetItem(c, itemId);
            return item != null ? item.displayName : itemId;
        }

        /// <summary>
        /// How much a deposit yields a second for whoever works it, with their research: one plain rate, so deposits
        /// with different paces never show odd cycle times. Ion counts Ion; other goods count units.
        /// </summary>
        static float PerSecond(ContentCatalog c, FactionState worker, ResourceNodeState node)
        {
            int perCycle = node.itemId == c.rules.ionItemId ? c.rules.ionPerExtraction : ResearchService.ExtractionOutput(c, worker, node.itemId);
            return perCycle / Mathf.Max(0.01f, ResearchService.ExtractionSeconds(c, worker, node.itemId, node.rateTicks));
        }

        static string Rate(float perSecond) { return perSecond >= 10f ? perSecond.ToString("0") : perSecond.ToString("0.#"); }

        string NodeDetails(WorldState w, ContentCatalog c, ResourceNodeState node)
        {
            var sb = new StringBuilder();
            // Rates are for whoever works the deposit (the player when untapped), with their research.
            var tapper = WorldLookup.GetBuilding(w, node.extractorBuildingId);
            var worker = WorldLookup.GetFaction(w, tapper != null ? tapper.owner : Me);
            sb.Append("Resource: ").Append(ItemName(c, node.itemId)).Append('\n');
            if (node.itemId == c.rules.ionItemId)
            {
                sb.Append("Yield: about ").Append(Rate(PerSecond(c, worker, node))).Append(" Ion a second, straight to your stockpile.\n");
                sb.Append("Ion is never belted. Research draws it straight from the stockpile.\n");
                var tap = WorldLookup.GetBuilding(w, node.extractorBuildingId);
                sb.Append(tap == null ? "Status: untapped. Place an Extractor on this node to collect it."
                    : "Status: tapped by " + BuildingTitle(w, c, tap) + (WorldLookup.IsBuildingActive(w, tap) ? " (working)" : " (stopped)"));
                return sb.ToString();
            }
            int perCycle = ResearchService.ExtractionOutput(c, worker, node.itemId);
            sb.Append("Output rate: about ").Append(Rate(PerSecond(c, worker, node))).Append(' ').Append(ItemName(c, node.itemId)).Append(" a second\n");
            sb.Append("Reserve: renewable; this deposit does not run out.\n");
            var extractor = WorldLookup.GetBuilding(w, node.extractorBuildingId);
            if (extractor == null)
            {
                sb.Append("Status: untapped. Place an Extractor on this node to collect it.\n");
                sb.Append("Buffer capacity: no extractor yet.");
                return sb.ToString();
            }
            var def = CatalogRepository.GetBuilding(c, extractor.typeId);
            int capacity = def != null ? ResearchService.ExtractorCapacity(def, perCycle) : 0;
            int stock = WorldLookup.TotalItems(extractor.inventory);
            bool active = WorldLookup.IsBuildingActive(w, extractor);
            sb.Append("Status: tapped by ").Append(BuildingTitle(w, c, extractor)).Append(active ? " (working)" : " (stopped)").Append('\n');
            sb.Append("Extractor buffer: ").Append(stock).Append('/').Append(capacity).Append(" items (capacity ").Append(capacity).Append(')');
            if (stock >= capacity && capacity > 0) sb.Append(" - full; output paused");

            return sb.ToString();
        }

        string CellDetails(WorldState w, ContentCatalog c, CellState cell)
        {
            var sb = new StringBuilder();
            foreach (var n in w.nodes)
            {
                if (n.cellId != cell.id) continue;
                var item = CatalogRepository.GetItem(c, n.itemId);
                bool ion = n.itemId == c.rules.ionItemId;
                var nodeTap = WorldLookup.GetBuilding(w, n.extractorBuildingId);
                float perSecond = PerSecond(c, WorldLookup.GetFaction(w, nodeTap != null ? nodeTap.owner : Me), n);
                sb.Append(WorldLookup.IsRichNode(w, n) ? "RICH node: " : "Node: ").Append(item != null ? item.displayName : n.itemId).Append(" (").Append(perSecond.ToString("0.0")).Append(ion ? " Ion/s)" : "/s)")
                  .Append(n.extractorBuildingId != -1 ? " - extracted" : " - free").Append('\n');
            }
            var defender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            sb.Append("Defender: ").Append(defender != null ? CraftName(c, defender) : "none").Append('\n');
            if (cell.owner != FactionId.Neutral)
            {
                string who = cell.owner == Me ? "" : SideLabel(w, cell.owner) + " ";
                if (OwnershipService.HasOwnAnchor(w, c, cell.id, cell.owner))
                {
                    sb.Append("Held by: ").Append(who).Append(cell.headquartersFor != FactionId.Neutral ? "Headquarters" : "Fort").Append('\n');
                    int used;
                    int slots = GridRules.ArraySlots(w, c, cell.owner, cell.id, out used);
                    sb.Append("Factory: online · Solar Array slots ").Append(used).Append('/').Append(slots).Append('\n');
                }
                else if (cell.owner == Me)
                    sb.Append("Factory: OFFLINE - deploy a Fort here from a Fort Construction Zone on a neighbouring cell's border\n");
                else
                {
                    var unit = OwnershipService.StationedUnit(w, cell.id, cell.owner);
                    if (unit != null) sb.Append("Held by: ").Append(CraftName(c, unit)).Append(" (it must stay, or build a Fort)\n");
                    else if (cell.unheldSinceTick != -1)
                    {
                        int left = Math.Max(0, (cell.unheldSinceTick + c.rules.abandonTicks - w.tick) * c.rules.tickMilliseconds / 1000);
                        sb.Append("UNGUARDED: lost in ").Append(left).Append("s - build a Fort or station a unit\n");
                    }
                }
            }
            foreach (var h in w.captureHolds)
            {
                if (h.cellId != cell.id) continue;
                string who = h.faction == Me ? "You are" : WorldLookup.WhoStart(w, Me, h.faction) + " is";
                if (h.tactical) sb.Append(who).Append(" holding the control point (completes after the enemy reply).\n");
                else sb.Append(who).Append(" holding the control point: ").Append(h.progressTicks * 100 / Math.Max(1, c.rules.normalCaptureTicks)).Append("%\n");
            }
            if (!WorldLookup.AreAllies(w, cell.owner, Me))
                sb.Append(cell.defenderCraftId == -1 && !WorldLookup.HasOpposingCraft(w, cell.id, Me)
                    ? "Claimable: move a craft here and hold the control point."
                    : "Defended: defeat every enemy craft here before claiming.");
            return sb.ToString().TrimEnd();
        }

        string BuildingTitle(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            string owner = OwnerWord(w, b.owner);
            return owner + (def != null ? def.displayName : b.typeId) + " (cell " + b.cellId + ")";
        }

        string BuildingDetails(WorldState w, ContentCatalog c, BuildingState b)
        {
            var sb = new StringBuilder();
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            bool active = WorldLookup.IsBuildingActive(w, b);
            if (!active)
            {
                if (b.inactiveUntilTick != -1)
                {
                    int secs = Math.Max(0, (b.inactiveUntilTick - w.tick) * c.rules.tickMilliseconds / 1000);
                    sb.Append("STOPPED: cell lost. Recapture within ").Append(secs / 60).Append(':').Append((secs % 60).ToString("00")).Append(" or it is cleared.\n");
                }
                else if (WorldLookup.IsSuppressed(w, b.owner)) sb.Append("STOPPED: owner has withdrawn.\n");
                else if (b.underConstruction) { }
                else if (WorldLookup.IsBuildingHeld(w, b)) sb.Append("STOPPED: this cell has no working Fort. Deploy one from a Fort Construction Zone next door.\n");
                else sb.Append("STOPPED: its cell is not held by its owner.\n");
            }
            if (def != null && b.underConstruction)
            {
                sb.Append("Under construction: belt in ").Append(IngredientsText(c, def.materials)).Append(".\n");
                if (b.targetCellId >= 0)
                {
                    var target = WorldLookup.GetCell(w, b.targetCellId);
                    bool full = FactorySimulation.HasAll(b, def.materials);
                    sb.Append("Deploys a Fort into cell ").Append(b.targetCellId).Append(" when full");
                    if (target != null && target.owner != b.owner) sb.Append(full ? ". READY: claim cell " + b.targetCellId + " with a unit to deploy it." : " and you hold that cell (claim it with a unit).");
                    sb.Append('\n');
                }
            }
            var ownerFaction = WorldLookup.GetFaction(w, b.owner);
            if (def != null && def.powerDraw > 0)
            {
                sb.Append("Power: ").Append(def.powerDraw).Append(" MW while working");
                float factor = loop.Services.Factory.PowerFactor(b.owner);
                if (active && factor < 1f) sb.Append(" - LOW POWER, running at ").Append(Mathf.RoundToInt(factor * 100)).Append("% speed");
                sb.Append('\n');
            }
            // Uptime: how much of the last half-minute it actually spent working (100% = never paused for
            // ingredients, a full store or power).
            float uptime = loop.Services.Factory.Uptime(b.id);
            if (uptime >= 0f && b.owner == Me)
                sb.Append("Uptime: ").Append(Mathf.RoundToInt(uptime * 100)).Append("% (last ").Append(FactorySimulation.UptimeTicks * c.rules.tickMilliseconds / 1000).Append("s)\n");
            if (def != null && def.powerSupply > 0)
                sb.Append("Generates: ").Append(ResearchService.SolarOutput(c, ownerFaction, def.powerSupply)).Append(" MW of solar power").Append(active ? "" : " (not while stopped)").Append(".\n");
            if (def != null && (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler))
            {
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                if (recipe == null) sb.Append("Idle: no recipe chosen yet.\n");
                else
                {
                    var owner = WorldLookup.GetFaction(w, b.owner);
                    int ticks = ResearchService.CraftTicks(c, owner, b.typeId, recipe);
                    sb.Append("Makes: ").Append(recipe.outputQuantity).Append(' ').Append(ItemName(c, recipe.outputItemId))
                      .Append(" every ").Append((ticks * c.rules.tickMilliseconds / 1000f).ToString("0.#")).Append("s\n");
                    sb.Append("Recipe: ").Append(IngredientsText(c, recipe.inputs));
                    sb.Append('\n');
                    sb.Append("Progress: ").Append(Math.Min(100, b.recipeProgressTicks * 100 / Math.Max(1, ticks))).Append("%");
                    if (active && !FactorySimulation.HasInputs(b, recipe)) sb.Append(" - waiting for ingredients");
                    sb.Append('\n');
                }
            }
            if (def != null && def.kind == BuildingKind.Extractor)
            {
                var node = WorldLookup.GetNode(w, b.nodeId);
                var worker = WorldLookup.GetFaction(w, b.owner);
                if (node != null && node.itemId == c.rules.ionItemId)
                    sb.Append("Extracting: about ").Append(Rate(PerSecond(c, worker, node))).Append(" Ion a second, straight to your stockpile\n");
                else if (node != null)
                {
                    sb.Append("Extracting: about ").Append(Rate(PerSecond(c, worker, node))).Append(' ').Append(ItemName(c, node.itemId)).Append(" a second\n");
                    sb.Append("Deposit: ").Append(WorldLookup.IsRichNode(w, node) ? "rich (faster than most " + ItemName(c, node.itemId) + "), " : "")
                      .Append("renewable; it never runs out\n");
                    // How many of its exit belts it feeds: one chute, more with Multi-Chute research.
                    int exits = 0;
                    foreach (var belt in w.belts)
                        if (belt.owner == b.owner && belt.fromCellId == b.cellId && belt.fromX == b.x && belt.fromY == b.y) exits++;
                    int chutes = ResearchService.Chutes(c, worker);
                    sb.Append("Chutes: ").Append(chutes).Append(chutes == 1 ? " exit belt" : " exit belts").Append(" fed at once");
                    if (exits > chutes) sb.Append(" (").Append(exits).Append(" built; the oldest ").Append(chutes).Append(chutes == 1 ? " is" : " are").Append(" used; research Multi-Chute Extractors for more)");
                    sb.Append('\n');
                }
            }
            if (def != null && def.combatHealth > 0)
                sb.Append("Defences: ").Append(def.combatHealth - b.damage).Append('/').Append(def.combatHealth).Append(" HP, armour ").Append(def.combatArmour)
                  .Append(", return fire ").Append(def.combatReturnFire).Append(" against attackers next to it. It cannot attack.\n");
            if (def != null && def.kind == BuildingKind.Headquarters && b.owner == Me && !Perspective.Spectating)
                sb.Append("Your research office. Press Research to discover new recipes, buildings and bonuses.\n");
            if (def != null && def.kind == BuildingKind.SaleDepot) sb.Append("Sells delivered goods on its sell list. Machines are always served before depots.\n");
            if (def != null && def.kind == BuildingKind.Storage)
                sb.Append("Collects surplus from connected producers and supplies any connected machine, shipyard or depot. Machines are served first.\n");
            if (def != null && def.kind == BuildingKind.Shipyard)
            {
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                var docked = craft == null ? FactorySimulation.RepairTarget(w, c, b) : null;
                var dockedDef = docked != null ? CatalogRepository.GetCraft(c, docked.typeId) : null;
                if (dockedDef != null)
                    sb.Append("Repairing ").Append(dockedDef.displayName).Append(": ").Append(docked.currentHealth).Append('/').Append(dockedDef.maxHealth)
                      .Append(" HP. Each HP takes ").Append(IngredientsText(c, dockedDef.repairParts)).Append(". Press Build to order a craft instead.\n");
                else if (craft == null) sb.Append("Idle - press Build to choose a craft. Damaged craft parked in this cell are repaired here.\n");
                else
                {
                    sb.Append("Building: ").Append(craft.displayName).Append(" · ").Append(b.recipeProgressTicks * 100 / Math.Max(1, craft.buildTicks)).Append("%\n");
                    sb.Append("Parts: ").Append(IngredientsText(c, craft.components)).Append('\n');
                    if (!FactorySimulation.HasComponents(b, craft)) sb.Append("Waiting for parts.\n");
                }
            }
            var buckets = FactorySimulation.RecipeOf(c, b, def);
            if (buckets != null)
            {
                // A bucket for each ingredient and one for the product, each with its own room.
                sb.Append("In: ");
                for (int i = 0; i < buckets.inputs.Count; i++)
                    AppendBucket(sb.Append(i > 0 ? ", " : ""), c, def, buckets, b, buckets.inputs[i].itemId);
                AppendBucket(sb.Append("\nOut: "), c, def, buckets, b, buckets.outputItemId);
                foreach (var st in b.inventory)
                    if (st.itemId != buckets.outputItemId && !buckets.inputs.Exists(x => x.itemId == st.itemId))
                        AppendBucket(sb.Append(", left over "), c, def, buckets, b, st.itemId);
                sb.Append('\n');
                string why;
                if (loop.Services.Pull.BlockedReasonByBuilding.TryGetValue(b.id, out why)) sb.Append("BLOCKED: ").Append(why);
                return sb.ToString().TrimEnd();
            }
            sb.Append("Stock: ");
            if (b.inventory.Count == 0) sb.Append("empty");
            for (int i = 0; i < b.inventory.Count; i++)
            {
                var item = CatalogRepository.GetItem(c, b.inventory[i].itemId);
                sb.Append(i > 0 ? ", " : "").Append(b.inventory[i].quantity).Append(' ').Append(item != null ? item.displayName : b.inventory[i].itemId);
            }
            if (def != null)
            {
                int shownCapacity = def.inventoryCapacity;
                var stockNode = def.kind == BuildingKind.Extractor ? WorldLookup.GetNode(w, b.nodeId) : null;
                if (stockNode != null) shownCapacity = ResearchService.ExtractorCapacity(def, ResearchService.ExtractionOutput(c, WorldLookup.GetFaction(w, b.owner), stockNode.itemId));
                sb.Append(" (").Append(WorldLookup.TotalItems(b.inventory)).Append('/').Append(shownCapacity).Append(")\n");
            }
            string reason;
            if (loop.Services.Pull.BlockedReasonByBuilding.TryGetValue(b.id, out reason)) sb.Append("BLOCKED: ").Append(reason);
            return sb.ToString().TrimEnd();
        }

        static void AppendBucket(StringBuilder sb, ContentCatalog c, BuildingDefinition def, RecipeDefinition recipe, BuildingState b, string itemId)
        {
            var item = CatalogRepository.GetItem(c, itemId);
            sb.Append(WorldLookup.CountItem(b.inventory, itemId)).Append('/').Append(FactorySimulation.BucketCapacity(def, recipe, itemId))
                .Append(' ').Append(item != null ? item.displayName : itemId);
        }

        static string ResearchSignature(FactionState f)
        {
            var sb = new StringBuilder();
            foreach (var r in f.research) sb.Append(r.nodeId).Append(r.level).Append(',');
            return sb.ToString();
        }

        /// <summary>Changes whenever the set or state of the building's choices changes, so buttons are rebuilt only then.</summary>
        string ChoiceSignature(WorldState w, ContentCatalog c, BuildingState b)
        {
            if (b == null || b.owner != Me) return "";
            var f = WorldLookup.GetFaction(w, Me);
            bool idleYard = string.IsNullOrEmpty(b.orderedCraftTypeId);
            string held = "";
            var bdef = CatalogRepository.GetBuilding(c, b.typeId);
            if (bdef != null && bdef.kind == BuildingKind.Storage)
                foreach (var st in b.inventory) held += st.itemId + st.quantity + ",";
            if (bdef != null && (bdef.kind == BuildingKind.Storage || bdef.kind == BuildingKind.SaleDepot))
                held += string.Join(",", ListedGoods(w, c, b));
            return b.id + ":" + b.selectedRecipeId + ":" + b.orderedCraftTypeId + ":" + string.Join(",", b.blockedItemIds) + ":" + string.Join(",", b.storedItemIds) + ":" + held + ":"
                + ResearchSignature(f) + ":" + w.mode + ":" + (idleYard ? f.currency / 10 : 0);
        }

        /// <summary>
        /// "Working on" choices for the selected building: recipes for refineries and assemblers, craft orders for
        /// shipyards, and a sell list for depots.
        /// </summary>
        void RebuildChoices(WorldState w, ContentCatalog c, BuildingState b)
        {
            recipePanel.Clear();
            Show(Find("working-on-title"), false);
            if (string.IsNullOrEmpty(choiceSignature) || b == null) return;
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return;
            var f = WorldLookup.GetFaction(w, Me);
            bool factory = w.mode == MatchMode.Factory;

            if (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler)
            {
                // Unlocked recipes, then locked ones whose research is in sight; the rest stay undiscovered.
                int undiscovered = 0;
                foreach (bool lockedPass in new[] { false, true })
                    foreach (var rid in def.allowedRecipeIds)
                    {
                        var recipe = CatalogRepository.GetRecipe(c, rid);
                        bool unlocked = ResearchService.IsRecipeUnlocked(c, f, recipe);
                        if (unlocked == lockedPass) continue;
                        bool current = b.selectedRecipeId == rid;
                        string id = rid;
                        string text = (current ? "Making " : "Make ") + RecipeText(c, f, b.typeId, recipe);
                        if (!unlocked)
                        {
                            var gate = ResearchService.RecipeGate(c, rid);
                            if (gate == null || ResearchService.Visibility(c, f, gate) < ResearchVisibility.Open) { undiscovered++; continue; }
                            text += "  (research " + gate.displayName + ")";
                        }
                        AddChoice(text, current, unlocked && !current && factory,
                            () => SubmitAndClose(new GameCommand { kind = CommandKind.SelectRecipe, actor = Me, buildingId = b.id, contentId = id }));
                    }
                if (undiscovered > 0)
                {
                    var more = new Label("+" + undiscovered + " more recipe" + (undiscovered == 1 ? "" : "s") + " still to discover through research at your HQ.");
                    more.AddToClassList("body-text");
                    more.AddToClassList("small");
                    recipePanel.Add(more);
                }
            }
            else if (def.kind == BuildingKind.Shipyard)
            {
                bool idle = string.IsNullOrEmpty(b.orderedCraftTypeId);
                foreach (var craft in c.crafts)
                {
                    string id = craft.id;
                    bool current = b.orderedCraftTypeId == craft.id;
                    string text = (current ? "Building " : "Build ") + craft.displayName + " - " + craft.cost + " credits"
                        + (craft.ionCost > 0 ? ", " + craft.ionCost + " Ion" : "") + " + " + EconomyService.ComponentText(c, craft)
                        + " (" + (craft.buildTicks * c.rules.tickMilliseconds / 1000f).ToString("0.#") + "s)";
                    // Parts that still need research are named so the player knows what to unlock.
                    var locked = new List<string>();
                    foreach (var part in craft.components)
                        if (!ResearchService.IsItemUnlocked(c, f, part.itemId)) locked.Add(ItemName(c, part.itemId));
                    if (locked.Count > 0) text += "  (research " + string.Join(", ", locked) + ")";
                    AddChoice(text, current, idle && factory && f.currency >= craft.cost && f.ion >= craft.ionCost && locked.Count == 0,
                        () => SubmitAndClose(new GameCommand { kind = CommandKind.BuyCraft, actor = Me, contentId = id, buildingId = b.id }));
                }
            }
            else if (def.kind == BuildingKind.SaleDepot)
            {
                var listed = ListedGoods(w, c, b);
                foreach (var item in c.items)
                {
                    if (!item.sellable || !listed.Contains(item.id)) continue;
                    string id = item.id;
                    bool selling = !b.blockedItemIds.Contains(item.id);
                    bool unlocked = ResearchService.IsItemUnlocked(c, f, item.id);
                    string text = (selling ? "ON  " : "OFF  ") + item.displayName + " - " + EconomyService.SalePrice(c, f, item) + " each"
                        + (unlocked ? "" : " (needs tech)");
                    var button = AddChoice(text, selling, factory,
                        () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = Me, buildingId = b.id, contentId = id }), true);
                    button.EnableInClassList("choice-off", !selling);
                }
            }
            else if (def.kind == BuildingKind.Storage)
            {
                // Storages start with nothing on their list; these switch every good on or off at once.
                AddChoice("Store all", false, factory,
                    () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = Me, buildingId = b.id, contentId = CommandRouter.StoreAll }), true);
                AddChoice("Store none", false, factory && b.storedItemIds.Count > 0,
                    () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = Me, buildingId = b.id, contentId = CommandRouter.StoreNone }), true);
                var listed = ListedGoods(w, c, b);
                foreach (var item in c.items)
                {
                    if (item.id == c.rules.ionItemId || !listed.Contains(item.id)) continue;
                    string id = item.id;
                    bool accepting = b.storedItemIds.Contains(item.id);
                    int held = WorldLookup.CountItem(b.inventory, item.id);
                    string text = (accepting ? "ON  " : "OFF  ") + item.displayName + (held > 0 ? " - holding " + held : "")
                        + (ResearchService.IsItemUnlocked(c, f, item.id) ? "" : " (needs tech)");
                    var button = AddChoice(text, accepting, factory,
                        () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = Me, buildingId = b.id, contentId = id }), true);
                    button.EnableInClassList("choice-off", !accepting);
                }
            }
            else return;
            Show(Find("working-on-title"), true);
        }

        /// <summary>
        /// Goods worth showing on a depot or storage toggle list: what the player's extractors and machines currently
        /// make, what this building holds, and anything already switched off (or, for a storage, on). The full catalog
        /// is too long to list.
        /// </summary>
        static HashSet<string> ListedGoods(WorldState w, ContentCatalog c, BuildingState b)
        {
            var set = new HashSet<string>(b.blockedItemIds);
            // Goods switched on are listed too, unless that is nearly everything (after Store all).
            if (b.storedItemIds.Count <= 12) foreach (var id in b.storedItemIds) set.Add(id);
            foreach (var st in b.inventory) set.Add(st.itemId);
            foreach (var other in w.buildings)
            {
                if (other.owner != b.owner) continue;
                var node = WorldLookup.GetNode(w, other.nodeId);
                if (node != null) set.Add(node.itemId);
                var recipe = CatalogRepository.GetRecipe(c, other.selectedRecipeId);
                if (recipe != null) set.Add(recipe.outputItemId);
            }
            return set;
        }

        /// <summary>
        /// Adds a choice button. For one-of-many choices (recipes, craft) the active one is inert; toggles (depot sell
        /// list) stay clickable in both states.
        /// </summary>
        Button AddChoice(string text, bool active, bool enabled, Action onClick, bool toggle = false)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("tool");
            button.EnableInClassList("choice-active", active);
            button.SetEnabled(enabled || active);
            if (active && !toggle) button.pickingMode = PickingMode.Ignore;
            recipePanel.Add(button);
            return button;
        }

        static string RecipeText(ContentCatalog c, FactionState f, string buildingId, RecipeDefinition recipe)
        {
            var sb = new StringBuilder();
            var outItem = CatalogRepository.GetItem(c, recipe.outputItemId);
            sb.Append(recipe.outputQuantity).Append(' ').Append(outItem != null ? outItem.displayName : recipe.outputItemId)
              .Append(" from ").Append(IngredientsText(c, recipe.inputs));
            sb.Append(" (").Append((ResearchService.CraftTicks(c, f, buildingId, recipe) * c.rules.tickMilliseconds / 1000f).ToString("0.#")).Append("s");
            sb.Append(')');
            return sb.ToString();
        }

        static string IngredientsText(ContentCatalog c, List<IngredientDefinition> ingredients)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ingredients.Count; i++)
                sb.Append(i > 0 ? " + " : "").Append(ingredients[i].quantity).Append(' ')
                  .Append(ItemName(c, ingredients[i].itemId));
            return sb.ToString();
        }

        /// <summary>Stock at this facility, already in transit, and held by active producers with a directed belt route here.</summary>
        void RefreshAvailability(WorldState w, ContentCatalog c, BuildingState building)
        {
            var items = new HashSet<string>();
            var def = building != null ? CatalogRepository.GetBuilding(c, building.typeId) : null;
            if (def != null && (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler))
            {
                // Only the selected recipe's inputs: machines now offer too many recipes to list every ingredient.
                var recipe = CatalogRepository.GetRecipe(c, building.selectedRecipeId);
                if (recipe != null) foreach (var input in recipe.inputs) items.Add(input.itemId);
            }
            else if (def != null && def.kind == BuildingKind.Shipyard)
            {
                foreach (var craft in c.crafts)
                    foreach (var part in craft.components) items.Add(part.itemId);
            }

            bool visible = items.Count > 0;
            Show(Find("availability-title"), visible);
            Show(availabilityDetails, visible);
            if (!visible) { availabilityDetails.text = ""; return; }

            var sb = new StringBuilder();
            var needs = FactorySimulation.CurrentNeeds(w, c, building);
            foreach (var item in c.items)
            {
                if (!items.Contains(item.id)) continue;
                int here = WorldLookup.CountItem(building.inventory, item.id);
                int incoming = FactorySimulation.Inbound(w, building.id, item.id);
                int reachable = 0;
                foreach (var source in w.buildings)
                {
                    if (source.id == building.id || source.owner != building.owner || !WorldLookup.IsBuildingActive(w, source)) continue;
                    int held = WorldLookup.CountItem(source.inventory, item.id);
                    if (held == 0 || !PullRouter.Offers(w, c, source, item.id)) continue;
                    if (loop.Services.Belts.FindShortestRoute(w, source.id, building.id).success) reachable += held;
                }
                int required = 0;
                foreach (var need in needs) if (need.itemId == item.id) required += need.quantity;
                sb.Append(item.displayName).Append(": ").Append(here).Append(" here");
                if (required > 0) sb.Append(" / ").Append(required).Append(" needed");
                sb.Append(" · ").Append(incoming).Append(" inbound · ").Append(reachable).Append(" upstream\n");
            }
            sb.Append("Upstream stock has an active belt route here; other machines may claim it first.");
            availabilityDetails.text = sb.ToString();
        }

        /// <summary>One row per needed item: colour swatch, have/need count, and what is holding it up.</summary>
        void RefreshNeeds(WorldState w, ContentCatalog c, BuildingState b)
        {
            var needs = b != null ? FactorySimulation.CurrentNeeds(w, c, b) : null;
            string sig = needs == null || needs.Count == 0 ? "" : b.id + ":" + string.Join(",", needs.ConvertAll(n => n.itemId));
            if (sig != needsSignature)
            {
                needsSignature = sig;
                needsList.Clear();
                needRows.Clear();
                if (sig.Length > 0)
                {
                    foreach (var need in needs)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("need-row");
                        var swatch = new VisualElement();
                        swatch.AddToClassList("need-swatch");
                        var item = CatalogRepository.GetItem(c, need.itemId);
                        swatch.style.backgroundColor = SpaceSpriteLibrary.Hex(item != null ? item.colorHex : "", Color.magenta);
                        var label = new Label();
                        label.AddToClassList("need-text");
                        row.Add(swatch);
                        row.Add(label);
                        needsList.Add(row);
                        needRows.Add(row);
                    }
                }
            }
            Show(Find("needs-title"), sig.Length > 0);
            if (sig.Length == 0) return;
            bool active = WorldLookup.CanReceive(w, b); // a construction site takes deliveries before it works
            bool full = FactorySimulation.IsStorageFull(w, c, b);
            for (int i = 0; i < needs.Count && i < needRows.Count; i++)
            {
                var need = needs[i];
                var row = needRows[i];
                var item = CatalogRepository.GetItem(c, need.itemId);
                string name = item != null ? item.displayName : need.itemId;
                int have = WorldLookup.CountItem(b.inventory, need.itemId);
                int coming = FactorySimulation.Inbound(w, b.id, need.itemId);
                string status;
                string cls;
                if (have >= need.quantity) { status = "ready"; cls = "need-ok"; }
                else if (coming > 0) { status = coming + " on the way"; cls = "need-coming"; }
                else
                {
                    string reason = loop.Services.Pull.GetItemReason(b.id, need.itemId);
                    if (!active) { status = "machine stopped"; cls = "need-missing"; }
                    else if (full) { status = "paused - storage full; connect its output to something that uses it"; cls = "need-coming"; }
                    else { status = "MISSING - " + (reason ?? "requesting"); cls = "need-missing"; }
                }
                ((Label)row[1]).text = name + "  " + have + "/" + need.quantity + "  -  " + status;
                row.EnableInClassList("need-ok", cls == "need-ok");
                row.EnableInClassList("need-coming", cls == "need-coming");
                row.EnableInClassList("need-missing", cls == "need-missing");
            }
        }

        void RebuildCraftList(WorldState w, ContentCatalog c, CellState cell)
        {
            var list = new List<CraftState>();
            if (cell != null)
                foreach (var k in w.crafts)
                    if (k.cellId == cell.id && WorldLookup.IsCraftLive(k)) list.Add(k);
            list.Sort((a, b) => a.id.CompareTo(b.id));
            var sig = new StringBuilder();
            foreach (var k in list) sig.Append(k.id).Append(':').Append(k.currentHealth).Append(ui.selectedCraftId == k.id ? "*" : ui.groupCraftIds.Contains(k.id) ? "+" : "").Append(WorldLookup.IsDefender(w, k) ? "d" : "").Append(';');
            if (sig.ToString() == craftListSignature) return;
            craftListSignature = sig.ToString();
            craftList.Clear();
            foreach (var k in list)
            {
                var def = CatalogRepository.GetCraft(c, k.typeId);
                int id = k.id;
                var button = new Button(() =>
                {
                    // With Group on (or Shift held) the list adds and removes units like taps on the map.
                    bool adding = ui.groupMode || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (adding && w.mode == MatchMode.Factory && TouchAction != null) { TouchAction("group-toggle:" + id); return; }
                    ui.groupCraftIds.Clear();
                    ui.selectedCraftId = id; ui.selectedBuildingId = -1; ui.selectedNodeId = -1;
                })
                {
                    text = (ui.selectedCraftId == k.id ? "> " : ui.groupCraftIds.Contains(k.id) ? "+ " : "") + CraftName(c, k) + "  HP " + k.currentHealth + "/" + (def != null ? def.maxHealth : 0)
                        + (WorldLookup.IsDefender(w, k) ? "  [defender]" : "")
                };
                button.AddToClassList("tool");
                craftList.Add(button);
            }
        }

        static string CraftName(ContentCatalog c, CraftState k)
        {
            var def = CatalogRepository.GetCraft(c, k.typeId);
            return Perspective.OwnerWord(k.owner) + (def != null ? def.displayName : k.typeId);
        }

        string CraftStats(WorldState w, ContentCatalog c, CraftState k)
        {
            var d = CatalogRepository.GetCraft(c, k.typeId);
            if (d == null) return "";
            var sb = new StringBuilder();
            sb.Append(CraftName(c, k)).Append(WorldLookup.IsDefender(w, k) ? " - DEFENDER" : "").Append('\n');
            sb.Append("HP ").Append(k.currentHealth).Append('/').Append(d.maxHealth).Append("   Ships ").Append(Fleet.Ships(d, k.currentHealth)).Append('/').Append(Fleet.FullFleet(d))
              .Append("   Move ").Append(d.movement).Append('\n');
            // The four combat stats; attack and reactive are shown at current strength when damage scales with HP.
            int attack = CombatResolver.ScaleByHealth(c, d.attackDamage, k.currentHealth, d.maxHealth);
            int reactive = CombatResolver.ScaleByHealth(c, d.defenceDamage, k.currentHealth, d.maxHealth);
            sb.Append("Attack ").Append(attack).Append(attack != d.attackDamage ? " (of " + d.attackDamage + ")" : "")
              .Append("   Return fire ").Append(reactive).Append(reactive != d.defenceDamage ? " (of " + d.defenceDamage + ")" : "")
              .Append("   Armour ").Append(d.armour);
            int shielded = CombatResolver.ShieldBonus(w, c, k);
            if (shielded > 0) sb.Append(" (+").Append(shielded).Append(" shielded)");
            if (d.shieldArmour > 0)
                sb.Append("\nShield flagship: every other ship on its side in its cell has +").Append(d.shieldArmour).Append(" armour.");
            if (d.range > 1)
            {
                int ranged = CombatResolver.ScaleByHealth(c, d.rangedAttackDamage, k.currentHealth, d.maxHealth);
                sb.Append("\nRanged attack ").Append(ranged).Append(" up to ").Append(d.range).Append(" cells away, where nothing can fire back.");
            }
            int cooldown = ResearchService.CooldownTurns(c, WorldLookup.GetFaction(w, k.owner), d);
            if (cooldown > 0)
                sb.Append("\nFires once every ").Append(cooldown).Append(cooldown == 1 ? " turn" : " turns").Append(k.cooldownTurns > 0 ? " - recharging: " + k.cooldownTurns + (k.cooldownTurns == 1 ? " turn" : " turns") : " - ready");
            if (d.defenderArmourBonus > 0 || d.defenderCounterBonus > 0)
                sb.Append("\nAs a cell's defender: +").Append(d.defenderArmourBonus).Append(" armour, +").Append(d.defenderCounterBonus).Append(" return fire");
            if (k.worldPathCellIds.Count > 0) sb.Append("\nTravelling to cell ").Append(k.worldPathCellIds[k.worldPathCellIds.Count - 1]).Append(k.worldDestinationCellId != -1 && k.worldDestinationCellId != k.worldPathCellIds[k.worldPathCellIds.Count - 1] ? " to attack cell " + k.worldDestinationCellId : "");
            var fighting = WorldLookup.BattleOf(w, k.id);
            if (fighting != null) sb.Append(fighting.actedCraftIds.Contains(k.id) ? "\nIn the battle for cell " + fighting.targetCellId + ": already acted this turn." : "\nIn the battle for cell " + fighting.targetCellId + ".");
            return sb.ToString();
        }

        void RefreshAlerts(WorldState w)
        {
            int newest = w.alerts.Count > 0 ? w.alerts[w.alerts.Count - 1].id : -1;
            if (newest == lastAlertId) return;
            int previous = lastAlertId;
            lastAlertId = newest;
            alertList.Clear();
            int shown = 0;
            for (int i = w.alerts.Count - 1; i >= 0 && shown < 10; i--)
            {
                var a = w.alerts[i];
                if (a.dismissed || !ForMe(a)) continue;
                ShowAlert(a);
                shown++;
            }
            // Surface raids prominently in the status line as well.
            if (w.alerts.Count > 0 && newest > previous && ForMe(w.alerts[w.alerts.Count - 1]))
            {
                var latest = w.alerts[w.alerts.Count - 1];
                if (latest.kind == AlertKind.Territory) ShowStatus(latest.message, true);
                else if (latest.kind == AlertKind.Raid || latest.kind == AlertKind.Defeat || latest.kind == AlertKind.SaveError) ShowStatus(latest.message, latest.kind != AlertKind.Raid);
            }
        }

        /// <summary>The seat played by the person at this machine.</summary>
        static FactionId Me { get { return Perspective.Viewer; } }

        /// <summary>Whether an alert is for the person at this machine (or for everyone); a spectator's overview hears them all.</summary>
        static bool ForMe(AlertState a)
        {
            return (Perspective.Spectating && Perspective.Overview) || a.to == FactionId.Neutral || a.to == Perspective.Viewer;
        }

        /// <summary>The trade ledger of the seat played at this machine.</summary>
        static TradeLedger MyTrade(WorldState w)
        {
            var me = WorldLookup.GetFaction(w, Perspective.Viewer);
            return me != null ? me.trade : new TradeLedger();
        }

        public void ShowAlert(AlertState a)
        {
            string prefix = a.kind == AlertKind.Raid ? "RAID: " : a.kind == AlertKind.BlockedRoute ? "Blocked: " : a.kind == AlertKind.SaveError ? "Save error: " : a.kind == AlertKind.Territory ? "TERRITORY: " : a.kind == AlertKind.Victory ? "VICTORY: " : a.kind == AlertKind.Defeat ? "DEFEAT: " : "";
            var label = new Label(prefix + a.message);
            label.AddToClassList("alert");
            if (a.kind == AlertKind.Raid || a.kind == AlertKind.Defeat) label.AddToClassList("alert-raid");
            if (a.kind == AlertKind.BlockedRoute || a.kind == AlertKind.Territory) label.AddToClassList("alert-blocked");
            if (a.kind == AlertKind.SaveError) label.AddToClassList("alert-error");
            if (a.kind == AlertKind.Victory) label.AddToClassList("alert-victory");
            if (a.cellId >= 0)
            {
                int cellId = a.cellId;
                // Clicking the alert flies the camera to its cell and selects it.
                label.text += "  \u203a";
                label.AddToClassList("alert-jump");
                label.RegisterCallback<ClickEvent>(_ =>
                {
                    ui.selectedCellId = cellId; ui.selectedBuildingId = -1; ui.selectedCraftId = -1; ui.selectedNodeId = -1;
                    if (FocusCellRequested != null) FocusCellRequested(cellId);
                    if (compact) OpenSheet("");
                });
            }
            alertList.Add(label);
        }

        void RefreshSave()
        {
            var r = loop.LastSaveResult;
            // An online match is kept by the relay, not saved here.
            if (loop.Lockstep != null) { saveIndicator.text = "Online"; saveIndicator.EnableInClassList("save-error", false); return; }
            if (r == null) { saveIndicator.text = "Not saved yet"; return; }
            saveIndicator.text = r.success ? "Saved at " + GameClock(loop.World, loop.Catalog) : "SAVE FAILED: " + r.message;
            saveIndicator.EnableInClassList("save-error", !r.success);
        }

        // ---------------------------------------------------------------- explicit displays

        public void ShowCombatPreview(CombatPreview p)
        {
            if (p == null) { attackPreview.text = Pointer.Words("Select one of your ships: its targets are listed below - click one to attack. Or right-click an enemy ship or Fort on the map. A cell's Fort and defender must fall before its other ships can be targeted."); return; }
            if (!p.legal) { attackPreview.text = "Cannot attack: " + p.reason; return; }
            var w = loop.World;
            var c = loop.Catalog;
            var attacker = WorldLookup.GetCraft(w, p.attackerCraftId);
            var defender = WorldLookup.GetCraft(w, p.defenderCraftId);
            string targetName = defender != null ? CraftName(c, defender) : "Enemy Fort";
            string text = CraftName(c, attacker) + " -> " + targetName + ": " + p.attackStatName + " " + p.attackBase
                + " -> " + p.attackDamageAfterArmour + " damage after Armour (HP " + p.defenderHealth + " -> " + Math.Max(0, p.defenderHealth - p.attackDamageAfterArmour) + ").";
            if (!p.defenderSurvives) text += " Destroys it - no counterattack.";
            else if (p.counterDamageAfterArmour > 0) text += " Counter: " + p.counterStatName + " " + p.counterBase + " -> " + p.counterDamageAfterArmour + " to you (HP " + attacker.currentHealth + " -> " + Math.Max(0, attacker.currentHealth - p.counterDamageAfterArmour) + ").";
            else text += " No counterattack (" + p.counterStatName + " 0).";
            attackPreview.text = text;
        }

        public void ShowPendingDefenderChoices(CellState cell, List<CraftState> reserves)
        {
            string sig = cell == null ? "" : cell.id + ":" + reserves.Count;
            if (sig == pendingSignature) return;
            pendingSignature = sig;
            pendingDefender.Clear();
            if (cell == null) return;
            var c = loop.Catalog;
            pendingDefender.Add(new Label("Your defender in cell " + cell.id + " fell. Choose a replacement (free):"));
            foreach (var k in reserves)
            {
                int id = k.id;
                int cellId = cell.id;
                var b = new Button(() => Submit(new GameCommand { kind = CommandKind.AssignDefender, actor = Me, craftId = id, targetCellId = cellId }))
                {
                    text = CraftName(c, k) + " HP " + k.currentHealth
                };
                b.AddToClassList("tool");
                pendingDefender.Add(b);
            }
        }

        public void ShowResult(MatchOutcome outcome)
        {
            shownOutcome = outcome;
            if (outcome == MatchOutcome.None) return;
            resultDismissed = false;
            Show(resultOverlay, true);
            Show(resumeButton, false);
            var w = loop != null ? loop.World : null;
            continueButton.text = "Keep building";
            if (w != null && Perspective.Spectating)
            {
                resultTitle.text = "Match over";
                var names = w.winners.ConvertAll(f => Perspective.SeatName(f));
                resultText.text = string.Join(" and ", names) + (names.Count == 1 ? " wins." : " win.");
                continueButton.text = "Keep watching";
                Show(continueButton, w.mode != MatchMode.Ended);
                Show(newGameButton, true);
            }
            else if (w != null && WorldLookup.HasWon(w, Perspective.Viewer))
            {
                resultTitle.text = "Victory";
                var rich = w.factions.Find(f => w.winners.Contains(f.id) && f.trade.won);
                resultText.text = (rich == null
                    ? "Every rival headquarters has fallen. Your rivals formally concede."
                    : WorldLookup.WhoStart(w, Perspective.Viewer, rich.id) + " sold " + rich.trade.rate.ToString("N0") + " credits a minute of "
                      + (rich.id == Perspective.Viewer ? "your" : "its") + " own goods: a trade victory. Your rivals formally concede.")
                    + (w.mode == MatchMode.Ended ? "" : "\nYour factories keep running - build as long as you like.");
                Show(continueButton, true);
                Show(newGameButton, true);
            }
            else
            {
                resultTitle.text = "Defeat";
                resultText.text = "Your side has lost. This map is lost.";
                Show(continueButton, false);
                Show(newGameButton, true);
            }
        }

        public void ShowStartScreen(bool hasSave, string message)
        {
            Show(titleOverlay, true);
            titleNotice.text = message ?? "";
            Show(titleNotice, !string.IsNullOrEmpty(message));
            Show(titleResume, hasSave);
            titleNew.text = hasSave ? "New game (replaces save)" : "New game";
            RefreshDifficulty();
        }

        /// <summary>Lights the chosen difficulty and says what its trade win asks for.</summary>
        void RefreshDifficulty()
        {
            int level = NewGameDifficulty != null ? NewGameDifficulty() : 1;
            for (int i = 0; i < difficultyButtons.Length; i++)
                if (difficultyButtons[i] != null) difficultyButtons[i].EnableInClassList("choice-active", i == level);
            var c = TitleCatalog ?? (loop != null ? loop.Catalog : null);
            if (titleFooter == null || c == null) return;
            var rates = c.rules.tradeWinPerMinute;
            int rate = rates.Count > level ? rates[level] : 0;
            titleFooter.text = "Win by capturing every rival headquarters, or by trade: sell " + rate.ToString("N0")
                + " credits a minute of goods you make, and hold it for " + c.rules.tradeWinHoldSeconds
                + " seconds. Difficulty applies to new games.  Look for the small ? buttons whenever you are unsure.";
        }

        /// <summary>Title screen art: runtime-generated sprites, largest in the middle.</summary>
        public void SetTitleArt(IList<Sprite> sprites, IList<float> heights)
        {
            var art = Find("title-art");
            if (art == null) return;
            art.Clear();
            for (int i = 0; i < sprites.Count; i++)
            {
                if (sprites[i] == null) continue;
                var image = new Image { sprite = sprites[i], scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("title-sprite");
                image.style.height = heights[i];
                image.style.width = heights[i] * sprites[i].rect.width / sprites[i].rect.height;
                art.Add(image);
            }
        }

        static void BuildStarfield(VisualElement field)
        {
            if (field == null) return;
            var rng = new System.Random(103);
            var colours = new[] { new Color32(80, 59, 107, 255), new Color32(120, 87, 135, 255), new Color32(164, 124, 161, 255), new Color32(255, 222, 126, 255) };
            for (int i = 0; i < 140; i++)
            {
                var star = new VisualElement { pickingMode = PickingMode.Ignore };
                star.AddToClassList("title-star");
                float size = rng.Next(4) == 0 ? 3f : 2f;
                star.style.left = Length.Percent((float)rng.NextDouble() * 100f);
                star.style.top = Length.Percent((float)rng.NextDouble() * 100f);
                star.style.width = size;
                star.style.height = size;
                star.style.backgroundColor = (Color)colours[rng.Next(colours.Length)];
                field.Add(star);
            }
        }

        public void ShowFatal(string message)
        {
            Show(resultOverlay, true);
            resultTitle.text = "Cannot start";
            resultText.text = message;
            Show(resumeButton, false);
            Show(continueButton, false);
            Show(newGameButton, false);
        }

        public void HideOverlay()
        {
            Show(resultOverlay, false);
            Show(titleOverlay, false);
            shownOutcome = loop.World != null ? loop.World.outcome : MatchOutcome.None;
            resultDismissed = true;
            outShown = loop.World != null && WorldLookup.IsSuppressed(loop.World, Me); // a loaded game already out says nothing new
        }

        public bool OverlayVisible
        {
            get { return titleOverlay != null && titleOverlay.resolvedStyle.display == DisplayStyle.Flex || resultOverlay.resolvedStyle.display == DisplayStyle.Flex && !resultDismissed; }
        }

        public void ShowStatus(string message, bool error)
        {
            Show(statusLabel, !string.IsNullOrEmpty(message));
            statusLabel.text = Pointer.Words(message ?? "");
            statusLabel.EnableInClassList("status-error", error);
            statusUntil = Time.unscaledTime + (error ? 5f : 4f);
        }

        static void Show(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>True when the pointer is over an interactive HUD element.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (root == null || root.panel == null) return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = root.panel.Pick(panelPos);
            return picked != null && picked != root;
        }
    }
}
