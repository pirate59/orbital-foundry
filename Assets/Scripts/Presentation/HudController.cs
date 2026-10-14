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

        Label powerLabel;
        Label currencyLabel, salesLabel, goalLabel, selectedCellLabel, selectionDetails, availabilityDetails, saveIndicator, statusLabel;
        Label craftOrderHint;
        VisualElement craftOrders;
        Button claimButton;
        Label turnLabel, actionLabel, attackPreview, craftStats, resultTitle, resultText;
        VisualElement buildPanel, recipePanel, craftList, tacticalPanel, tacticalBanner, pendingDefender, resultOverlay;
        ScrollView alertList;
        Button beltUpgradeLine, beltUpgradeOne, beltViewButton;
        Button researchButton, captureButton, assignDefenderButton, endTurnButton, retreatButton, dismantleButton;
        Button linkedNodeButton, linkedExtractorButton;
        Button newGameButton, resumeButton, continueButton;
        readonly Dictionary<UiTool, Button> toolButtons = new Dictionary<UiTool, Button>();

        string choiceSignature = "", craftListSignature = "", pendingSignature = "", needsSignature = "";
        VisualElement needsList;
        readonly List<VisualElement> needRows = new List<VisualElement>();
        int lastAlertId = -1;
        float statusUntil;
        MatchOutcome shownOutcome = MatchOutcome.None;
        bool resultDismissed;

        public Action NewGameRequested;
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
            powerLabel = Find("power-label") as Label;
            salesLabel = Find("sales-label") as Label;
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
            pendingDefender = Find("pending-defender");
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
            BindTool("belt-tool", UiTool.DrawBelt);
            BindTool("dismantle-tool", UiTool.Dismantle);

            beltViewButton = Bind("belt-view-button", () =>
            {
                WorldInput.ToggleBeltView(ui);
                if (ui.beltView) ShowStatus("Belt View: buildings fade and ignore clicks. Click any belt to select, upgrade or dismantle it. Press V to switch back.", false);
            });
            researchButton = Bind("research-button", () => { if (ResearchRequested != null) ResearchRequested(); });
            beltUpgradeLine = Bind("belt-upgrade-line", () => Submit(new GameCommand { kind = CommandKind.UpgradeBelt, actor = FactionId.Player, beltId = ui.selectedBeltId, contentId = "line" }));
            beltUpgradeOne = Bind("belt-upgrade-one", () => Submit(new GameCommand { kind = CommandKind.UpgradeBelt, actor = FactionId.Player, beltId = ui.selectedBeltId }));
            captureButton = Bind("capture-button", CaptureSelected);
            assignDefenderButton = Bind("assign-defender-button", AssignSelected);
            endTurnButton = Bind("end-turn-button", () => Submit(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player }));
            retreatButton = Bind("retreat-button", () => Submit(new GameCommand { kind = CommandKind.Retreat, actor = FactionId.Player }));
            dismantleButton = Bind("dismantle-button", DismantleSelected);
            linkedNodeButton = Bind("linked-node-button", () =>
            {
                var selected = WorldLookup.GetBuilding(loop.World, ui.selectedBuildingId);
                var node = selected != null ? WorldLookup.GetNode(loop.World, selected.nodeId) : null;
                if (node == null) return;
                ui.selectedNodeId = node.id;
                ui.selectedBuildingId = -1;
                ui.selectedCraftId = -1;
                ui.selectedCellId = node.cellId;
            });
            linkedExtractorButton = Bind("linked-extractor-button", () =>
            {
                var selected = WorldLookup.GetNode(loop.World, ui.selectedNodeId);
                var extractor = selected != null ? WorldLookup.GetBuilding(loop.World, selected.extractorBuildingId) : null;
                if (extractor == null) return;
                ui.selectedBuildingId = extractor.id;
                ui.selectedNodeId = -1;
                ui.selectedCraftId = -1;
                ui.selectedCellId = extractor.cellId;
            });
            craftOrders = Find("craft-orders");
            craftOrderHint = Find("craft-order-hint") as Label;
            claimButton = Bind("claim-button", CaptureSelected);
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

        void BindTool(string name, UiTool tool)
        {
            var button = Bind(name, () =>
            {
                ui.activeTool = tool;
                ui.beltFromCellId = -1;
                if (WorldInput.IsBuildingTool(tool)) ui.beltView = false;
                ShowStatus(ToolHint(tool) + GoodsHint(tool), false);
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
                int have = EconomyService.StoredCount(loop.World, c, FactionId.Player, g.itemId);
                parts.Add(Math.Min(have, g.quantity) + "/" + g.quantity);
            }
            return "(" + string.Join(", ", parts) + ")";
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
                case UiTool.BuildShipyard: return "Shipyards build craft from Iron Bars and Signal Relays delivered by belt. Click an empty tile you own.";
                case UiTool.BuildSolarArray: return "A Solar Array adds solar power. It takes its goods from your Storage and needs a free slot (3 at your HQ, 2 per Fort cell). Click an empty tile there.";
                case UiTool.BuildPurifier: return "A Water Purifier turns Water from a refinery into Purified Water. Click an empty tile you own.";
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

        void CaptureSelected()
        {
            var craft = WorldLookup.GetCraft(loop.World, ui.selectedCraftId);
            if (craft == null) { ShowStatus("Select one of your craft first.", true); return; }
            Submit(new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Player, craftId = craft.id, targetCellId = craft.cellId });
        }

        void AssignSelected()
        {
            var craft = WorldLookup.GetCraft(loop.World, ui.selectedCraftId);
            if (craft == null) { ShowStatus("Select one of your craft first.", true); return; }
            Submit(new GameCommand { kind = CommandKind.AssignDefender, actor = FactionId.Player, craftId = craft.id, targetCellId = craft.cellId });
        }

        void DismantleSelected()
        {
            if (ui.selectedBuildingId < 0) { ShowStatus("Select a structure first.", true); return; }
            Submit(new GameCommand { kind = CommandKind.Dismantle, actor = FactionId.Player, buildingId = ui.selectedBuildingId });
            ui.selectedBuildingId = -1;
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(WorldState w, ContentCatalog c, CommandResult last)
        {
            if (w == null) return;
            var player = WorldLookup.GetFaction(w, FactionId.Player);
            var ai = WorldLookup.GetFaction(w, FactionId.Ai);
            bool tactical = w.mode == MatchMode.Tactical;
            bool factory = w.mode == MatchMode.Factory;

            currencyLabel.text = "Credits: " + player.currency.ToString("N0") + "  ·  Ion: " + player.ion.ToString("N0");
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
            salesLabel.text = "Sales: " + player.cumulativeSales.ToString("N0") + " / " + c.rules.playerSalesGoal.ToString("N0");
            bool salesDone = player.cumulativeSales >= c.rules.playerSalesGoal;
            bool hqDone = ai.suppressed;
            goalLabel.text = "Goals: Sales " + (salesDone ? "MET" : "not yet") + "  ·  AI HQ " + (hqDone ? "CAPTURED" : "standing");
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
            toolButtons[UiTool.Dismantle].SetEnabled(factory);
            toolButtons[UiTool.DrawBelt].text = "Belt (drag) - " + c.rules.beltCost + " per step";
            toolButtons[UiTool.DrawBelt].SetEnabled(factory);
            foreach (var kv in toolButtons) kv.Value.EnableInClassList("tool-active", ui.activeTool == kv.Key);
            if (beltViewButton != null) beltViewButton.EnableInClassList("tool-active", ui.beltView);

            Show(tacticalPanel, tactical);
            Show(tacticalBanner, tactical);
            if (tactical) RefreshTactical(w, c);
            else ShowPendingDefenderChoices(null, null);

            RefreshSelection(w, c);
            RefreshAlerts(w);
            RefreshSave();

            if (Time.unscaledTime > statusUntil && statusLabel.text.Length > 0 && !tactical) { statusLabel.text = ""; Show(statusLabel, false); }
            if (w.outcome != shownOutcome) ShowResult(w.outcome);
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
            bool goods = def.goodsCost.Count == 0 || EconomyService.HasStoredGoods(loop.World, c, FactionId.Player, def.goodsCost);
            // Goods are listed in the tool hint; the button only flags that they are needed.
            toolButtons[tool].text = (def.kind == BuildingKind.ConstructionZone ? "Fort Zone" : def.displayName) + " - " + def.cost
                + (def.materials.Count > 0 ? " + belt goods" : "")
                + (def.goodsCost.Count > 0 ? " + goods " + StoredProgress(c, def.goodsCost) : "");
            // Buildings that take stored goods stay clickable when short, so choosing the tool explains what is missing.
            toolButtons[tool].SetEnabled(factory && player.currency >= def.cost && (goods || def.goodsCost.Count > 0));
        }

        void RefreshTactical(WorldState w, ContentCatalog c)
        {
            var e = w.encounter;
            bool mine = e.turn == FactionId.Player;
            turnLabel.text = mine ? "Your turn - round " + e.round : "AI turn - round " + e.round + " (watch the map)";
            turnLabel.EnableInClassList("turn-ai", !mine);
            actionLabel.text = "Actions left: " + e.actionsRemaining + " / " + c.rules.tacticalActionsPerSide;
            bool pending = e.pendingDefenderCellId != -1 && e.pendingDefenderOwner == FactionId.Player;
            var craft = WorldLookup.GetCraft(w, ui.selectedCraftId);
            bool craftReady = mine && craft != null && craft.owner == FactionId.Player && e.participantCraftIds.Contains(craft.id) && !e.actedCraftIds.Contains(craft.id) && e.actionsRemaining > 0;
            captureButton.SetEnabled(craftReady && !pending);
            assignDefenderButton.SetEnabled(craftReady || pending);
            endTurnButton.SetEnabled(mine && !pending);
            retreatButton.SetEnabled(mine && e.attacker == FactionId.Player && !pending);
            retreatButton.text = e.attacker == FactionId.Player ? "Retreat" : "Retreat (attacker only)";

            if (pending)
            {
                var cell = WorldLookup.GetCell(w, e.pendingDefenderCellId);
                ShowPendingDefenderChoices(cell, WorldLookup.CraftsInCell(w, cell.id, FactionId.Player));
            }
            else ShowPendingDefenderChoices(null, null);

            Show(statusLabel, true);
            var events = loop.Services.Encounters.LastEvent;
            if (Time.unscaledTime > statusUntil && !string.IsNullOrEmpty(events)) statusLabel.text = events;
        }

        void RefreshSelection(WorldState w, ContentCatalog c)
        {
            var craft = WorldLookup.GetCraft(w, ui.selectedCraftId);
            if (craft != null && (craft.withdrawn || craft.currentHealth <= 0)) { ui.selectedCraftId = -1; craft = null; }
            var building = WorldLookup.GetBuilding(w, ui.selectedBuildingId);
            if (building == null) ui.selectedBuildingId = -1;
            var node = WorldLookup.GetNode(w, ui.selectedNodeId);
            if (node == null) ui.selectedNodeId = -1;
            var cell = WorldLookup.GetCell(w, ui.selectedCellId);
            var belt = WorldLookup.GetBelt(w, ui.selectedBeltId);
            if (belt == null) ui.selectedBeltId = -1;
            Show(beltUpgradeLine, false);
            Show(beltUpgradeOne, false);

            // The selection panel's "?" explains whatever is selected.
            string topic = belt != null && craft == null && building == null && node == null ? "belts"
                : craft != null ? "crafts"
                : building != null ? building.typeId
                : node != null ? (node.itemId == c.rules.ionItemId ? "ion" : "resource_nodes")
                : cell != null ? "territory" : "getting_started";
            HelpController.SetTopic(selectionHelp, Help != null && Help.HasTopic(topic) ? topic : "getting_started");

            Show(dismantleButton, false);
            Show(researchButton, false);
            Show(linkedNodeButton, false);
            Show(linkedExtractorButton, false);
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
                newChoiceSignature = ChoiceSignature(w, c, building);
                Show(linkedNodeButton, WorldLookup.GetNode(w, building.nodeId) != null);
                bool inPlayerCell = WorldLookup.GetCell(w, building.cellId).owner == FactionId.Player;
                var bdef = CatalogRepository.GetBuilding(c, building.typeId);
                bool hq = bdef != null && bdef.kind == BuildingKind.Headquarters;
                bool own = building.owner == FactionId.Player && inPlayerCell && !hq;   // Headquarters can never be dismantled
                bool clearAi = building.owner == FactionId.Ai && building.inactiveUntilTick != -1 && WorldLookup.IsSuppressed(w, FactionId.Ai) && inPlayerCell;
                dismantleButton.text = own ? "Dismantle (+" + (bdef != null ? bdef.cost * CommandRouter.DismantleRefundPercent / 100 : 0) + " refund)" : "Clear inactive AI structure";
                dismantleButton.SetEnabled(w.mode == MatchMode.Factory);
                Show(dismantleButton, own || clearAi);
                Show(researchButton, building.owner == FactionId.Player && hq);
            }
            else if (node != null)
            {
                selectedCellLabel.text = (WorldLookup.IsRichNode(w, node) ? "Rich " : "") + ItemName(c, node.itemId) + " node (cell " + node.cellId + ")";
                selectionDetails.text = NodeDetails(w, c, node);
                Show(linkedExtractorButton, WorldLookup.GetBuilding(w, node.extractorBuildingId) != null);
            }
            else if (belt != null)
            {
                var line = loop.Services.Belts.LineOf(w, belt.id);
                var tiers = c.rules.beltTiers;
                var tier = tiers[Mathf.Clamp(belt.tier, 0, tiers.Count - 1)];
                selectedCellLabel.text = (belt.owner == FactionId.Player ? "Your " : "Enemy ") + tier.name + " (cell " + belt.fromCellId + ")";
                var sb = new StringBuilder();
                if (belt.linkTicks > 0)
                {
                    // A drone link between asteroids: one item per round trip.
                    float oneWay = BeltNetwork.TravelTicks(c, belt) * c.rules.tickMilliseconds / 1000f;
                    selectedCellLabel.text = (belt.owner == FactionId.Player ? "Your " : "Enemy ") + "drone link (" + tier.name + ", cell " + belt.fromCellId + " to " + belt.toCellId + ")";
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
                bool mine = belt.owner == FactionId.Player && w.mode == MatchMode.Factory && WorldLookup.IsBeltActive(w, belt);
                if (mine && belt.tier + 1 < tiers.Count)
                {
                    var next = tiers[belt.tier + 1];
                    int lineCount = line.FindAll(s => s.tier <= belt.tier).Count;
                    var player = WorldLookup.GetFaction(w, FactionId.Player);
                    selectionDetails.text += belt.linkTicks > 0
                        ? "\nNext: " + next.name + ", " + (BeltNetwork.TravelTicks(c, new BeltSegmentState { tier = belt.tier + 1, linkTicks = belt.linkTicks }) * c.rules.tickMilliseconds / 1000f).ToString("0.0") + "s each way, " + next.upgradeCostPerSegment + " to upgrade."
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
                selectedCellLabel.text = CellTitle(cell);
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
            bool show = craft != null && craft.owner == FactionId.Player && w.mode == MatchMode.Factory;
            Show(craftOrders, show);
            if (!show) return;
            var cell = WorldLookup.GetCell(w, craft.cellId);
            bool holding = w.captureHolds.Exists(h => h.craftId == craft.id);
            bool travelling = craft.worldPathCellIds.Count > 0;
            bool claimable = !travelling && !holding && cell.owner != FactionId.Player && !WorldLookup.HasOpposingCraft(w, cell.id, FactionId.Player);
            var hold = w.captureHolds.Find(h => h.craftId == craft.id);
            string state = travelling ? "Travelling. "
                : hold != null ? "Claiming cell " + cell.id + ": " + hold.progressTicks * 100 / Math.Max(1, c.rules.normalCaptureTicks) + "%. Stay in the cell until it completes. "
                : "";
            craftOrderHint.text = state + "Right-click any cell on the map to send this unit there. Arriving in a neutral cell claims it automatically ("
                + (c.rules.normalCaptureTicks * c.rules.tickMilliseconds / 1000) + "s). To keep a claimed cell, leave the unit there or build a Fort, then the unit can move on. Right-click a cell holding enemy craft to attack it.";
            claimButton.text = claimable ? "Claim cell " + cell.id + " now" : cell.owner == FactionId.Player ? "Claim (already your cell)" : holding ? "Claiming..." : "Claim (move to a clear cell first)";
            claimButton.SetEnabled(claimable);
        }

        static string CellTitle(CellState cell)
        {
            string owner = cell.owner == FactionId.Player ? "Yours" : cell.owner == FactionId.Ai ? "AI-held" : "Neutral";
            string hq = cell.headquartersFor == FactionId.Player ? " · YOUR HQ" : cell.headquartersFor == FactionId.Ai ? " · AI HQ" : "";
            return "Cell " + cell.id + " - " + owner + hq;
        }

        static string ItemName(ContentCatalog c, string itemId)
        {
            var item = CatalogRepository.GetItem(c, itemId);
            return item != null ? item.displayName : itemId;
        }

        string NodeDetails(WorldState w, ContentCatalog c, ResourceNodeState node)
        {
            var sb = new StringBuilder();
            float seconds = node.rateTicks * c.rules.tickMilliseconds / 1000f;
            sb.Append("Resource: ").Append(ItemName(c, node.itemId)).Append('\n');
            if (node.itemId == c.rules.ionItemId)
            {
                int ionPerCycle = c.rules.ionPerExtraction * c.rules.nodeOutputPerCycle;
                sb.Append("Yield: adds ").Append(ionPerCycle).Append(" Ion to your stockpile every ").Append(seconds.ToString("0.#"))
                  .Append("s (").Append((ionPerCycle / Mathf.Max(0.01f, seconds)).ToString("0.#")).Append(" Ion/s).\n");
                sb.Append("Ion is never belted. Research draws it straight from the stockpile.\n");
                var tap = WorldLookup.GetBuilding(w, node.extractorBuildingId);
                sb.Append(tap == null ? "Status: untapped. Place an Extractor on this node to collect it."
                    : "Status: tapped by " + BuildingTitle(w, c, tap) + (WorldLookup.IsBuildingActive(w, tap) ? " (working)" : " (stopped)"));
                return sb.ToString();
            }
            int perCycle = c.rules.nodeOutputPerCycle;
            sb.Append("Output rate: ").Append(perCycle).Append(" every ").Append(seconds.ToString("0.#")).Append("s (up to ")
              .Append((perCycle / Mathf.Max(0.01f, seconds)).ToString("0.##")).Append("/s)\n");
            sb.Append("Reserve: renewable; this deposit does not run out.\n");
            var extractor = WorldLookup.GetBuilding(w, node.extractorBuildingId);
            if (extractor == null)
            {
                sb.Append("Status: untapped. Place an Extractor on this node to collect it.\n");
                sb.Append("Buffer capacity: no extractor yet.");
                return sb.ToString();
            }
            var def = CatalogRepository.GetBuilding(c, extractor.typeId);
            int capacity = def != null ? def.inventoryCapacity : 0;
            int stock = WorldLookup.TotalItems(extractor.inventory);
            bool active = WorldLookup.IsBuildingActive(w, extractor);
            sb.Append("Status: tapped by ").Append(BuildingTitle(w, c, extractor)).Append(active ? " (working)" : " (stopped)").Append('\n');
            sb.Append("Extractor buffer: ").Append(stock).Append('/').Append(capacity).Append(" items (capacity ").Append(capacity).Append(')');
            if (stock >= capacity && capacity > 0) sb.Append(" - full; output paused");
            else if (active) sb.Append(" · next in ").Append((node.ticksUntilOutput * c.rules.tickMilliseconds / 1000f).ToString("0.#")).Append('s');
            return sb.ToString();
        }

        string CellDetails(WorldState w, ContentCatalog c, CellState cell)
        {
            var sb = new StringBuilder();
            foreach (var n in w.nodes)
            {
                if (n.cellId != cell.id) continue;
                var item = CatalogRepository.GetItem(c, n.itemId);
                float perSecond = 1000f * c.rules.nodeOutputPerCycle / (n.rateTicks * c.rules.tickMilliseconds);
                bool ion = n.itemId == c.rules.ionItemId;
                if (ion) perSecond *= c.rules.ionPerExtraction;
                sb.Append(WorldLookup.IsRichNode(w, n) ? "RICH node: " : "Node: ").Append(item != null ? item.displayName : n.itemId).Append(" (").Append(perSecond.ToString("0.0")).Append(ion ? " Ion/s)" : "/s)")
                  .Append(n.extractorBuildingId != -1 ? " - extracted" : " - free").Append('\n');
            }
            var defender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            sb.Append("Defender: ").Append(defender != null ? CraftName(c, defender) : "none").Append('\n');
            if (cell.owner != FactionId.Neutral)
            {
                string who = cell.owner == FactionId.Player ? "" : "AI ";
                if (OwnershipService.HasOwnAnchor(w, c, cell.id, cell.owner))
                {
                    sb.Append("Held by: ").Append(who).Append(cell.headquartersFor != FactionId.Neutral ? "Headquarters" : "Fort").Append('\n');
                    int slots = 0, used = 0;
                    foreach (var b in w.buildings)
                    {
                        if (b.cellId != cell.id || b.owner != cell.owner) continue;
                        var d = CatalogRepository.GetBuilding(c, b.typeId);
                        if (d == null) continue;
                        if (b.anchor && !b.underConstruction) slots += d.arraySlots;
                        if (d.kind == BuildingKind.SolarArray) used++;
                    }
                    sb.Append("Factory: online · Solar Array slots ").Append(used).Append('/').Append(slots).Append('\n');
                }
                else if (cell.owner == FactionId.Player)
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
                string who = h.faction == FactionId.Player ? "You are" : "The AI is";
                if (h.tactical) sb.Append(who).Append(" holding the control point (completes after the enemy reply).\n");
                else sb.Append(who).Append(" holding the control point: ").Append(h.progressTicks * 100 / Math.Max(1, c.rules.normalCaptureTicks)).Append("%\n");
            }
            if (cell.owner != FactionId.Player)
                sb.Append(cell.defenderCraftId == -1 && !WorldLookup.HasOpposingCraft(w, cell.id, FactionId.Player)
                    ? "Claimable: move a craft here and hold the control point."
                    : "Defended: defeat every enemy craft here before claiming.");
            return sb.ToString().TrimEnd();
        }

        string BuildingTitle(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            string owner = b.owner == FactionId.Player ? "Your " : "AI ";
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
            if (def != null && def.powerSupply > 0)
                sb.Append("Generates: ").Append(ResearchService.SolarOutput(c, ownerFaction, def.powerSupply)).Append(" MW of solar power").Append(active ? "" : " (not while stopped)").Append(".\n");
            if (def != null && (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler))
            {
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                if (recipe == null) sb.Append("No recipe selected - choose one below.\n");
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
                if (node != null && node.itemId == c.rules.ionItemId)
                    sb.Append("Extracting: ").Append(c.rules.ionPerExtraction * c.rules.nodeOutputPerCycle).Append(" Ion every ")
                      .Append((node.rateTicks * c.rules.tickMilliseconds / 1000f).ToString("0.#")).Append("s, straight to your stockpile\n");
                else if (node != null) sb.Append("Extracting: ").Append(ItemName(c, node.itemId)).Append(" · up to ").Append(c.rules.nodeOutputPerCycle).Append(" every ")
                  .Append((node.rateTicks * c.rules.tickMilliseconds / 1000f).ToString("0.#")).Append("s\n");
            }
            if (def != null && def.kind == BuildingKind.Headquarters && b.owner == FactionId.Player)
                sb.Append("Your research office. Press Research to discover new recipes, buildings and bonuses.\n");
            if (def != null && def.kind == BuildingKind.SaleDepot) sb.Append("Sells delivered goods on its sell list. Machines are always served before depots.\n");
            if (def != null && def.kind == BuildingKind.Storage)
                sb.Append("Collects surplus from connected producers and supplies any connected machine, shipyard or depot. Machines are served first.\n");
            if (def != null && def.kind == BuildingKind.Shipyard)
            {
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                if (craft == null) sb.Append("Idle - choose a craft to build below.\n");
                else
                {
                    sb.Append("Building: ").Append(craft.displayName).Append(" · ").Append(b.recipeProgressTicks * 100 / Math.Max(1, craft.buildTicks)).Append("%\n");
                    sb.Append("Parts: ").Append(IngredientsText(c, craft.components)).Append('\n');
                    if (!FactorySimulation.HasComponents(b, craft)) sb.Append("Waiting for parts.\n");
                }
            }
            sb.Append("Stock: ");
            if (b.inventory.Count == 0) sb.Append("empty");
            for (int i = 0; i < b.inventory.Count; i++)
            {
                var item = CatalogRepository.GetItem(c, b.inventory[i].itemId);
                sb.Append(i > 0 ? ", " : "").Append(b.inventory[i].quantity).Append(' ').Append(item != null ? item.displayName : b.inventory[i].itemId);
            }
            if (def != null) sb.Append(" (").Append(WorldLookup.TotalItems(b.inventory)).Append('/').Append(def.inventoryCapacity).Append(")\n");
            string reason;
            if (loop.Services.Pull.BlockedReasonByBuilding.TryGetValue(b.id, out reason)) sb.Append("BLOCKED: ").Append(reason);
            return sb.ToString().TrimEnd();
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
            if (b == null || b.owner != FactionId.Player) return "";
            var f = WorldLookup.GetFaction(w, FactionId.Player);
            bool idleYard = string.IsNullOrEmpty(b.orderedCraftTypeId);
            string held = "";
            var bdef = CatalogRepository.GetBuilding(c, b.typeId);
            if (bdef != null && bdef.kind == BuildingKind.Storage)
                foreach (var st in b.inventory) held += st.itemId + st.quantity + ",";
            if (bdef != null && (bdef.kind == BuildingKind.Storage || bdef.kind == BuildingKind.SaleDepot))
                held += string.Join(",", ListedGoods(w, c, b));
            return b.id + ":" + b.selectedRecipeId + ":" + b.orderedCraftTypeId + ":" + string.Join(",", b.blockedItemIds) + ":" + held + ":"
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
            var f = WorldLookup.GetFaction(w, FactionId.Player);
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
                            () => Submit(new GameCommand { kind = CommandKind.SelectRecipe, actor = FactionId.Player, buildingId = b.id, contentId = id }));
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
                    string text = (current ? "Building " : "Build ") + craft.displayName + (craft.airborne ? " (air)" : "") + " - " + craft.cost + " credits + " + EconomyService.ComponentText(c, craft)
                        + " (" + (craft.buildTicks * c.rules.tickMilliseconds / 1000f).ToString("0.#") + "s)";
                    // Parts that still need research are named so the player knows what to unlock.
                    var locked = new List<string>();
                    foreach (var part in craft.components)
                        if (!ResearchService.IsItemUnlocked(c, f, part.itemId)) locked.Add(ItemName(c, part.itemId));
                    if (locked.Count > 0) text += "  (research " + string.Join(", ", locked) + ")";
                    AddChoice(text, current, idle && factory && f.currency >= craft.cost && locked.Count == 0,
                        () => Submit(new GameCommand { kind = CommandKind.BuyCraft, actor = FactionId.Player, contentId = id, buildingId = b.id }));
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
                        () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = FactionId.Player, buildingId = b.id, contentId = id }), true);
                    button.EnableInClassList("choice-off", !selling);
                }
            }
            else if (def.kind == BuildingKind.Storage)
            {
                var listed = ListedGoods(w, c, b);
                foreach (var item in c.items)
                {
                    if (item.id == c.rules.ionItemId || !listed.Contains(item.id)) continue;
                    string id = item.id;
                    bool accepting = !b.blockedItemIds.Contains(item.id);
                    int held = WorldLookup.CountItem(b.inventory, item.id);
                    string text = (accepting ? "ON  " : "OFF  ") + item.displayName + (held > 0 ? " - holding " + held : "")
                        + (ResearchService.IsItemUnlocked(c, f, item.id) ? "" : " (needs tech)");
                    var button = AddChoice(text, accepting, factory,
                        () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = FactionId.Player, buildingId = b.id, contentId = id }), true);
                    button.EnableInClassList("choice-off", !accepting);
                }
            }
            else return;
            Show(Find("working-on-title"), true);
        }

        /// <summary>
        /// Goods worth showing on a depot or storage toggle list: what the player's extractors and machines currently
        /// make, what this building holds, and anything already switched off. The full catalog is too long to list.
        /// </summary>
        static HashSet<string> ListedGoods(WorldState w, ContentCatalog c, BuildingState b)
        {
            var set = new HashSet<string>(b.blockedItemIds);
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
            foreach (var k in list) sig.Append(k.id).Append(':').Append(k.currentHealth).Append(ui.selectedCraftId == k.id ? "*" : "").Append(WorldLookup.IsDefender(w, k) ? "d" : "").Append(';');
            if (sig.ToString() == craftListSignature) return;
            craftListSignature = sig.ToString();
            craftList.Clear();
            foreach (var k in list)
            {
                var def = CatalogRepository.GetCraft(c, k.typeId);
                int id = k.id;
                var button = new Button(() => { ui.selectedCraftId = id; ui.selectedBuildingId = -1; ui.selectedNodeId = -1; })
                {
                    text = (ui.selectedCraftId == k.id ? "> " : "") + CraftName(c, k) + "  HP " + k.currentHealth + "/" + (def != null ? def.maxHealth : 0)
                        + (WorldLookup.IsDefender(w, k) ? "  [defender]" : "")
                };
                button.AddToClassList("tool");
                craftList.Add(button);
            }
        }

        static string CraftName(ContentCatalog c, CraftState k)
        {
            var def = CatalogRepository.GetCraft(c, k.typeId);
            return (k.owner == FactionId.Player ? "Your " : "Enemy ") + (def != null ? def.displayName : k.typeId);
        }

        string CraftStats(WorldState w, ContentCatalog c, CraftState k)
        {
            var d = CatalogRepository.GetCraft(c, k.typeId);
            if (d == null) return "";
            var sb = new StringBuilder();
            sb.Append(CraftName(c, k)).Append(d.airborne ? " (flies over defended cells)" : "").Append(WorldLookup.IsDefender(w, k) ? " - DEFENDER" : "").Append('\n');
            sb.Append("HP ").Append(k.currentHealth).Append('/').Append(d.maxHealth).Append("   Move ").Append(d.movement).Append('\n');
            // The four combat stats; attack and reactive are shown at current strength when damage scales with HP.
            int attack = CombatResolver.ScaleByHealth(c, d.attackDamage, k.currentHealth, d.maxHealth);
            int reactive = CombatResolver.ScaleByHealth(c, d.defenceDamage, k.currentHealth, d.maxHealth);
            sb.Append("Attack ").Append(attack).Append(attack != d.attackDamage ? " (of " + d.attackDamage + ")" : "")
              .Append("   Reactive ").Append(reactive).Append(reactive != d.defenceDamage ? " (of " + d.defenceDamage + ")" : "")
              .Append("   Armour ").Append(d.armour);
            if (d.defenderArmourBonus > 0 || d.defenderCounterBonus > 0)
                sb.Append("\nAs a cell's defender: +").Append(d.defenderArmourBonus).Append(" armour, +").Append(d.defenderCounterBonus).Append(" reactive");
            if (k.worldPathCellIds.Count > 0) sb.Append("\nTravelling to cell ").Append(k.worldPathCellIds[k.worldPathCellIds.Count - 1]).Append(k.worldDestinationCellId != -1 && k.worldDestinationCellId != k.worldPathCellIds[k.worldPathCellIds.Count - 1] ? " to attack cell " + k.worldDestinationCellId : "");
            if (w.mode == MatchMode.Tactical && w.encounter.actedCraftIds.Contains(k.id)) sb.Append("\nAlready acted this turn.");
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
                if (a.dismissed) continue;
                ShowAlert(a);
                shown++;
            }
            // Surface raids prominently in the status line as well.
            if (w.alerts.Count > 0 && newest > previous)
            {
                var latest = w.alerts[w.alerts.Count - 1];
                if (latest.kind == AlertKind.Territory) ShowStatus(latest.message, true);
                else if (latest.kind == AlertKind.Raid || latest.kind == AlertKind.Defeat || latest.kind == AlertKind.SaveError) ShowStatus(latest.message, latest.kind != AlertKind.Raid);
            }
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
                label.RegisterCallback<ClickEvent>(_ => { ui.selectedCellId = cellId; ui.selectedBuildingId = -1; ui.selectedCraftId = -1; ui.selectedNodeId = -1; });
            }
            alertList.Add(label);
        }

        void RefreshSave()
        {
            var r = loop.LastSaveResult;
            if (r == null) { saveIndicator.text = "Not saved yet"; return; }
            saveIndicator.text = r.success ? "Saved (tick " + loop.World.tick + ")" : "SAVE FAILED: " + r.message;
            saveIndicator.EnableInClassList("save-error", !r.success);
        }

        // ---------------------------------------------------------------- explicit displays

        public void ShowCombatPreview(CombatPreview p)
        {
            if (p == null) { attackPreview.text = "Select one of your craft, then hover an enemy cell to preview an attack. Right-click to act."; return; }
            if (!p.legal) { attackPreview.text = "Cannot attack: " + p.reason; return; }
            var w = loop.World;
            var c = loop.Catalog;
            var attacker = WorldLookup.GetCraft(w, p.attackerCraftId);
            var defender = WorldLookup.GetCraft(w, p.defenderCraftId);
            string text = CraftName(c, attacker) + " -> " + CraftName(c, defender) + ": " + p.attackStatName + " " + p.attackBase
                + " -> " + p.attackDamageAfterArmour + " damage after Armour (HP " + defender.currentHealth + " -> " + Math.Max(0, defender.currentHealth - p.attackDamageAfterArmour) + ").";
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
                var b = new Button(() => Submit(new GameCommand { kind = CommandKind.AssignDefender, actor = FactionId.Player, craftId = id, targetCellId = cellId }))
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
            if (outcome == MatchOutcome.AiConceded)
            {
                resultTitle.text = "Victory";
                resultText.text = "You captured the AI headquarters and reached the sales target. The AI formally concedes.\nYour factories keep running - build as long as you like.";
                Show(continueButton, true);
                Show(newGameButton, true);
            }
            else
            {
                resultTitle.text = "Defeat";
                resultText.text = "The AI captured your headquarters. This map is lost.";
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
        }

        public bool OverlayVisible
        {
            get { return titleOverlay != null && titleOverlay.resolvedStyle.display == DisplayStyle.Flex || resultOverlay.resolvedStyle.display == DisplayStyle.Flex && !resultDismissed; }
        }

        public void ShowStatus(string message, bool error)
        {
            Show(statusLabel, !string.IsNullOrEmpty(message));
            statusLabel.text = message ?? "";
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
