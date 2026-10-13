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

        Label currencyLabel, salesLabel, goalLabel, selectedCellLabel, selectionDetails, saveIndicator, statusLabel;
        Label craftHint, craftOrderHint;
        VisualElement craftOrders;
        Button claimButton;
        Label turnLabel, actionLabel, attackPreview, craftStats, resultTitle, resultText;
        VisualElement buildPanel, recipePanel, craftList, tacticalPanel, tacticalBanner, pendingDefender, resultOverlay, investPanel, craftPanel;
        ScrollView alertList;
        Button upgradeButton, techButton, captureButton, assignDefenderButton, endTurnButton, retreatButton, dismantleButton;
        Button newGameButton, resumeButton, continueButton;
        readonly Dictionary<UiTool, Button> toolButtons = new Dictionary<UiTool, Button>();
        readonly Dictionary<string, Button> craftButtons = new Dictionary<string, Button>();

        string choiceSignature = "", craftListSignature = "", pendingSignature = "", needsSignature = "";
        VisualElement needsList;
        readonly List<VisualElement> needRows = new List<VisualElement>();
        int lastAlertId = -1;
        float statusUntil;
        MatchOutcome shownOutcome = MatchOutcome.None;
        bool resultDismissed;

        public Action NewGameRequested;
        public Action ResumeRequested;

        public static readonly string[] RequiredNames =
        {
            "hud-root", "currency-label", "sales-label", "goal-label", "selected-cell-label", "build-panel", "build-extractor",
            "build-refinery", "build-assembler", "build-depot", "belt-tool", "recipe-panel", "upgrade-button", "tech-button",
            "buy-tank", "buy-artillery", "buy-guard", "buy-strike", "alert-list", "save-indicator", "tactical-panel", "turn-label",
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
            salesLabel = Find("sales-label") as Label;
            goalLabel = Find("goal-label") as Label;
            selectedCellLabel = Find("selected-cell-label") as Label;
            selectionDetails = Find("selection-details") as Label;
            saveIndicator = Find("save-indicator") as Label;
            statusLabel = Find("status-label") as Label;
            turnLabel = Find("turn-label") as Label;
            actionLabel = Find("action-label") as Label;
            attackPreview = Find("attack-preview") as Label;
            craftStats = Find("selected-craft-stats") as Label;
            resultTitle = Find("result-title") as Label;
            resultText = Find("result-text") as Label;
            buildPanel = Find("build-panel");
            investPanel = Find("invest-panel");
            craftPanel = Find("craft-panel");
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
            BindTool("belt-tool", UiTool.DrawBelt);
            BindTool("dismantle-tool", UiTool.Dismantle);
            craftHint = Find("craft-hint") as Label;

            upgradeButton = Bind("upgrade-button", () => Submit(new GameCommand { kind = CommandKind.BuyUpgrade, actor = FactionId.Player, contentId = FirstUpgradeId() }));
            techButton = Bind("tech-button", () => Submit(new GameCommand { kind = CommandKind.UnlockTech, actor = FactionId.Player, contentId = FirstTechId() }));
            foreach (var type in new[] { "tank", "artillery", "guard", "strike" })
            {
                string t = type;
                craftButtons[t] = Bind("buy-" + t, () =>
                {
                    var yard = SelectedShipyard();
                    Submit(new GameCommand { kind = CommandKind.BuyCraft, actor = FactionId.Player, contentId = t, buildingId = yard != null ? yard.id : -1 });
                });
            }
            captureButton = Bind("capture-button", CaptureSelected);
            assignDefenderButton = Bind("assign-defender-button", AssignSelected);
            endTurnButton = Bind("end-turn-button", () => Submit(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player }));
            retreatButton = Bind("retreat-button", () => Submit(new GameCommand { kind = CommandKind.Retreat, actor = FactionId.Player }));
            dismantleButton = Bind("dismantle-button", DismantleSelected);
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

        void BindTool(string name, UiTool tool)
        {
            toolButtons[tool] = Bind(name, () =>
            {
                ui.activeTool = tool;
                ui.beltFromCellId = -1;
                ShowStatus(ToolHint(tool), false);
            });
        }

        static string ToolHint(UiTool tool)
        {
            switch (tool)
            {
                case UiTool.BuildExtractor: return "Click a resource node in a cell you own. Right-click or Esc to stop.";
                case UiTool.Dismantle: return "Click one of your machines, or drag across belts, to dismantle them (half the cost is refunded).";
                case UiTool.BuildShipyard: return "Shipyards build craft from Alloy Plates and Signal Relays delivered by belt. Click an empty tile you own.";
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

        string FirstUpgradeId() { return loop.Catalog.upgrades.Count > 0 ? loop.Catalog.upgrades[0].id : ""; }
        string FirstTechId() { return loop.Catalog.techs.Count > 0 ? loop.Catalog.techs[0].id : ""; }

        /// <summary>The selected building when it is one of the player's shipyards, else null.</summary>
        BuildingState SelectedShipyard()
        {
            var b = WorldLookup.GetBuilding(loop.World, ui.selectedBuildingId);
            if (b == null || b.owner != FactionId.Player) return null;
            var def = CatalogRepository.GetBuilding(loop.Catalog, b.typeId);
            return def != null && def.kind == BuildingKind.Shipyard ? b : null;
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

            currencyLabel.text = "Credits: " + player.currency.ToString("N0");
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
            toolButtons[UiTool.Dismantle].SetEnabled(factory);
            toolButtons[UiTool.DrawBelt].text = "Belt (drag) - " + c.rules.beltCost + " per step";
            toolButtons[UiTool.DrawBelt].SetEnabled(factory);
            foreach (var kv in toolButtons) kv.Value.EnableInClassList("tool-active", ui.activeTool == kv.Key);

            var upgrade = c.upgrades.Count > 0 ? c.upgrades[0] : null;
            if (upgrade != null)
            {
                bool owned = player.purchasedUpgradeIds.Contains(upgrade.id);
                upgradeButton.text = upgrade.displayName + (owned ? " - owned (+" + (upgrade.saleMultiplierPercent - 100) + "% sale price)" : " - " + upgrade.cost + " (+" + (upgrade.saleMultiplierPercent - 100) + "% sale price)");
                upgradeButton.SetEnabled(factory && !owned && player.currency >= upgrade.cost);
            }
            var tech = c.techs.Count > 0 ? c.techs[0] : null;
            if (tech != null)
            {
                bool owned = player.unlockedTechIds.Contains(tech.id);
                var item = CatalogRepository.GetItem(c, tech.unlockedItemId);
                techButton.text = tech.displayName + (owned ? " - unlocked" : " - " + tech.cost + " (unlocks " + (item != null ? item.displayName : tech.unlockedItemId) + ")");
                techButton.SetEnabled(factory && !owned && player.currency >= tech.cost);
            }
            var yard = SelectedShipyard();
            bool yardIdle = yard != null && string.IsNullOrEmpty(yard.orderedCraftTypeId) && WorldLookup.IsBuildingActive(w, yard);
            foreach (var kv in craftButtons)
            {
                var def = CatalogRepository.GetCraft(c, kv.Key);
                if (def == null) continue;
                kv.Value.text = def.displayName + " - " + def.cost + " + " + EconomyService.ComponentText(c, def) + (def.airborne ? " (air)" : "");
                kv.Value.SetEnabled(factory && yardIdle && player.currency >= def.cost);
            }
            craftHint.text = yard == null ? "Build a Shipyard and select it. Craft are made from factory goods delivered by belt."
                : yardIdle ? "Choose a craft. Its parts are pulled to this shipyard by belt."
                : !string.IsNullOrEmpty(yard.orderedCraftTypeId) ? "This shipyard is busy building a craft."
                : "This shipyard is not working.";

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
            toolButtons[tool].text = def.displayName + " - " + def.cost;
            toolButtons[tool].SetEnabled(factory && player.currency >= def.cost);
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
            var cell = WorldLookup.GetCell(w, ui.selectedCellId);

            Show(dismantleButton, false);
            string newChoiceSignature = "";
            if (building != null)
            {
                selectedCellLabel.text = BuildingTitle(w, c, building);
                selectionDetails.text = BuildingDetails(w, c, building);
                newChoiceSignature = ChoiceSignature(w, c, building);
                bool inPlayerCell = WorldLookup.GetCell(w, building.cellId).owner == FactionId.Player;
                bool own = building.owner == FactionId.Player && inPlayerCell;
                bool clearAi = building.owner == FactionId.Ai && building.inactiveUntilTick != -1 && WorldLookup.IsSuppressed(w, FactionId.Ai) && inPlayerCell;
                var bdef = CatalogRepository.GetBuilding(c, building.typeId);
                dismantleButton.text = own ? "Dismantle (+" + (bdef != null ? bdef.cost * CommandRouter.DismantleRefundPercent / 100 : 0) + " refund)" : "Clear inactive AI structure";
                dismantleButton.SetEnabled(w.mode == MatchMode.Factory);
                Show(dismantleButton, own || clearAi);
            }
            else if (cell != null)
            {
                selectedCellLabel.text = CellTitle(cell);
                selectionDetails.text = CellDetails(w, c, cell);
            }
            else
            {
                selectedCellLabel.text = "Nothing selected. Click a cell, machine or craft.";
                selectionDetails.text = "";
            }
            if (newChoiceSignature != choiceSignature)
            {
                choiceSignature = newChoiceSignature;
                RebuildChoices(w, c, building);
            }
            RefreshNeeds(w, c, building);

            craftStats.text = craft != null ? CraftStats(w, c, craft) : "";
            RefreshCraftOrders(w, c, craft);
            RebuildCraftList(w, c, cell);
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
                + (c.rules.normalCaptureTicks * c.rules.tickMilliseconds / 1000) + "s). Right-click a cell holding enemy craft to attack it.";
            claimButton.text = claimable ? "Claim cell " + cell.id + " now" : cell.owner == FactionId.Player ? "Claim (already your cell)" : holding ? "Claiming..." : "Claim (move to a clear cell first)";
            claimButton.SetEnabled(claimable);
        }

        static string CellTitle(CellState cell)
        {
            string owner = cell.owner == FactionId.Player ? "Yours" : cell.owner == FactionId.Ai ? "AI-held" : "Neutral";
            string hq = cell.headquartersFor == FactionId.Player ? " · YOUR HQ" : cell.headquartersFor == FactionId.Ai ? " · AI HQ" : "";
            return "Cell " + cell.id + " - " + owner + hq;
        }

        string CellDetails(WorldState w, ContentCatalog c, CellState cell)
        {
            var sb = new StringBuilder();
            foreach (var n in w.nodes)
            {
                if (n.cellId != cell.id) continue;
                var item = CatalogRepository.GetItem(c, n.itemId);
                float perSecond = 1000f / (n.rateTicks * c.rules.tickMilliseconds);
                sb.Append("Node: ").Append(item != null ? item.displayName : n.itemId).Append(" (").Append(perSecond.ToString("0.0")).Append("/s)")
                  .Append(n.extractorBuildingId != -1 ? " - extracted" : " - free").Append('\n');
            }
            var defender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            sb.Append("Defender: ").Append(defender != null ? CraftName(c, defender) : "none").Append('\n');
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
                else sb.Append("STOPPED: its cell is not held by its owner.\n");
            }
            if (def != null && (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler))
            {
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                if (recipe == null) sb.Append("No recipe selected - choose one below.\n");
                else
                {
                    sb.Append("Progress: ").Append(b.recipeProgressTicks * 100 / Math.Max(1, recipe.craftTicks)).Append("%");
                    if (active && !FactorySimulation.HasInputs(b, recipe)) sb.Append(" - waiting for ingredients (see Needs)");
                    sb.Append('\n');
                }
            }
            if (def != null && def.kind == BuildingKind.Extractor)
            {
                var node = WorldLookup.GetNode(w, b.nodeId);
                if (node != null) sb.Append("Extracting 1 every ").Append(node.rateTicks * c.rules.tickMilliseconds / 1000f).Append("s\n");
            }
            if (def != null && def.kind == BuildingKind.SaleDepot) sb.Append("Sells delivered goods on its sell list. Machines are always served before depots.\n");
            if (def != null && def.kind == BuildingKind.Shipyard)
            {
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                if (craft == null) sb.Append("Idle - choose a craft to build below.\n");
                else if (FactorySimulation.HasComponents(b, craft))
                    sb.Append("Assembling ").Append(craft.displayName).Append(": ").Append(b.recipeProgressTicks * 100 / Math.Max(1, craft.buildTicks)).Append("%\n");
                else sb.Append("Building ").Append(craft.displayName).Append(" - waiting for parts (see Needs).\n");
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

        /// <summary>Changes whenever the set or state of the building's choices changes, so buttons are rebuilt only then.</summary>
        string ChoiceSignature(WorldState w, ContentCatalog c, BuildingState b)
        {
            if (b == null || b.owner != FactionId.Player) return "";
            var f = WorldLookup.GetFaction(w, FactionId.Player);
            bool idleYard = string.IsNullOrEmpty(b.orderedCraftTypeId);
            return b.id + ":" + b.selectedRecipeId + ":" + b.orderedCraftTypeId + ":" + string.Join(",", b.blockedItemIds) + ":"
                + string.Join(",", f.unlockedTechIds) + ":" + w.mode + ":" + (idleYard ? f.currency / 10 : 0);
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
                foreach (var rid in def.allowedRecipeIds)
                {
                    var recipe = CatalogRepository.GetRecipe(c, rid);
                    bool unlocked = EconomyService.IsRecipeUnlocked(c, f, recipe);
                    bool current = b.selectedRecipeId == rid;
                    string id = rid;
                    string text = (current ? "Making " : "Make ") + RecipeText(c, recipe);
                    if (!unlocked)
                    {
                        var tech = CatalogRepository.GetTech(c, recipe.requiredTechId);
                        text += "  (needs " + (tech != null ? tech.displayName : recipe.requiredTechId) + ")";
                    }
                    AddChoice(text, current, unlocked && !current && factory,
                        () => Submit(new GameCommand { kind = CommandKind.SelectRecipe, actor = FactionId.Player, buildingId = b.id, contentId = id }));
                }
            }
            else if (def.kind == BuildingKind.Shipyard)
            {
                bool idle = string.IsNullOrEmpty(b.orderedCraftTypeId);
                foreach (var craft in c.crafts)
                {
                    string id = craft.id;
                    bool current = b.orderedCraftTypeId == craft.id;
                    string text = (current ? "Building " : "Build ") + craft.displayName + " - " + craft.cost + " credits + " + EconomyService.ComponentText(c, craft)
                        + " (" + (craft.buildTicks * c.rules.tickMilliseconds / 1000f).ToString("0.#") + "s)";
                    AddChoice(text, current, idle && factory && f.currency >= craft.cost,
                        () => Submit(new GameCommand { kind = CommandKind.BuyCraft, actor = FactionId.Player, contentId = id, buildingId = b.id }));
                }
            }
            else if (def.kind == BuildingKind.SaleDepot)
            {
                foreach (var item in c.items)
                {
                    if (!item.sellable) continue;
                    string id = item.id;
                    bool selling = !b.blockedItemIds.Contains(item.id);
                    bool unlocked = EconomyService.IsItemUnlocked(c, f, item.id);
                    string text = (selling ? "Selling " : "Not selling ") + item.displayName + " - " + EconomyService.SalePrice(f, item) + " each"
                        + (unlocked ? "" : " (locked)") + (selling ? "  [click to stop]" : "  [click to sell]");
                    var button = AddChoice(text, selling, factory,
                        () => Submit(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = FactionId.Player, buildingId = b.id, contentId = id }));
                    button.EnableInClassList("choice-off", !selling);
                }
            }
            else return;
            Show(Find("working-on-title"), true);
        }

        Button AddChoice(string text, bool active, bool enabled, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("tool");
            button.EnableInClassList("choice-active", active);
            button.SetEnabled(enabled || active);
            if (active) button.pickingMode = PickingMode.Ignore;
            recipePanel.Add(button);
            return button;
        }

        static string RecipeText(ContentCatalog c, RecipeDefinition recipe)
        {
            var sb = new StringBuilder();
            var outItem = CatalogRepository.GetItem(c, recipe.outputItemId);
            sb.Append(outItem != null ? outItem.displayName : recipe.outputItemId).Append(" from ");
            for (int i = 0; i < recipe.inputs.Count; i++)
            {
                var inItem = CatalogRepository.GetItem(c, recipe.inputs[i].itemId);
                sb.Append(i > 0 ? " + " : "").Append(recipe.inputs[i].quantity).Append(' ').Append(inItem != null ? inItem.displayName : recipe.inputs[i].itemId);
            }
            sb.Append(" (").Append((recipe.craftTicks * c.rules.tickMilliseconds / 1000f).ToString("0.#")).Append("s)");
            return sb.ToString();
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
            bool active = WorldLookup.IsBuildingActive(w, b);
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
                var button = new Button(() => { ui.selectedCraftId = id; ui.selectedBuildingId = -1; })
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
            sb.Append(CraftName(c, k)).Append(d.airborne ? " (airborne)" : " (ground)").Append(WorldLookup.IsDefender(w, k) ? " - DEFENDER" : "").Append('\n');
            sb.Append("HP ").Append(k.currentHealth).Append('/').Append(d.maxHealth).Append("   Armour ").Append(d.armour);
            if (d.defenderArmourBonus > 0 || d.defenderCounterBonus > 0) sb.Append(" (+").Append(d.defenderArmourBonus).Append(" as defender)");
            sb.Append('\n');
            sb.Append("Move ").Append(d.movement).Append("   Range ").Append(d.range).Append('\n');
            sb.Append("Attack ").Append(d.attackDamage).Append("   Ranged ").Append(d.rangedAttackDamage).Append("   Air ").Append(d.airAttackDamage).Append('\n');
            sb.Append("Defence ").Append(d.defenceDamage).Append("   Ranged Def ").Append(d.rangedDefenceDamage).Append("   Air Def ").Append(d.airDefenceDamage);
            if (d.defenderCounterBonus > 0) sb.Append(" (+").Append(d.defenderCounterBonus).Append(" counter as defender)");
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
                if (latest.kind == AlertKind.Raid || latest.kind == AlertKind.Defeat || latest.kind == AlertKind.SaveError) ShowStatus(latest.message, latest.kind != AlertKind.Raid);
            }
        }

        public void ShowAlert(AlertState a)
        {
            string prefix = a.kind == AlertKind.Raid ? "RAID: " : a.kind == AlertKind.BlockedRoute ? "Blocked: " : a.kind == AlertKind.SaveError ? "Save error: " : a.kind == AlertKind.Victory ? "VICTORY: " : a.kind == AlertKind.Defeat ? "DEFEAT: " : "";
            var label = new Label(prefix + a.message);
            label.AddToClassList("alert");
            if (a.kind == AlertKind.Raid || a.kind == AlertKind.Defeat) label.AddToClassList("alert-raid");
            if (a.kind == AlertKind.BlockedRoute) label.AddToClassList("alert-blocked");
            if (a.kind == AlertKind.SaveError) label.AddToClassList("alert-error");
            if (a.kind == AlertKind.Victory) label.AddToClassList("alert-victory");
            if (a.cellId >= 0)
            {
                int cellId = a.cellId;
                label.RegisterCallback<ClickEvent>(_ => { ui.selectedCellId = cellId; ui.selectedBuildingId = -1; });
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
            Show(resultOverlay, true);
            resultTitle.text = "Orbital Foundry";
            resultText.text = (string.IsNullOrEmpty(message) ? "" : message + "\n\n")
                + "Build connected factories, sell goods, claim territory and take the AI headquarters.\n"
                + "Win by reaching the sales target AND capturing the AI HQ.";
            Show(resumeButton, hasSave);
            Show(continueButton, false);
            Show(newGameButton, true);
            newGameButton.text = hasSave ? "New game (replaces save)" : "New game";
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
            shownOutcome = loop.World != null ? loop.World.outcome : MatchOutcome.None;
            resultDismissed = true;
        }

        public bool OverlayVisible { get { return resultOverlay.resolvedStyle.display == DisplayStyle.Flex && !resultDismissed; } }

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
