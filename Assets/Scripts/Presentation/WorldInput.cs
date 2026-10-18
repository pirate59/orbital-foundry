using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Translates mouse and keyboard input into UiState changes and GameCommands. Left-click selects or builds,
    /// left-drag with the belt tool lays one directed belt per tile step, right-click orders the selected craft
    /// (move / attack / hold) and otherwise cancels the active tool. Never changes WorldState directly.
    /// </summary>
    public sealed class WorldInput
    {
        /// <summary>The seat played by the person at this machine: every order here is given as that seat.</summary>
        static FactionId Me { get { return Perspective.Viewer; } }

        readonly GameLoop loop;
        readonly CameraInput cam;
        readonly WorldView view;
        readonly HudController hud;
        public readonly UiState Ui;

        bool hoverValid;
        int hoverCell, hoverX, hoverY, hoverGX, hoverGY;
        bool beltDragging;
        bool dismantling;
        Vector3 hoverPoint;
        int lastGX, lastGY;
        int lastPreviewCraft = -2, lastPreviewCell = -2, lastPreviewTarget = -1, lastPreviewRevision = -1;
        readonly HashSet<int> moveTargets = new HashSet<int>();
        readonly HashSet<int> attackTargets = new HashSet<int>();

        /// <summary>One undoable action: a placed building, or every belt laid in one drag or route.</summary>
        sealed class UndoStep
        {
            public readonly List<int> buildings = new List<int>();
            public readonly List<int> belts = new List<int>();
        }
        readonly List<UndoStep> undo = new List<UndoStep>();
        UndoStep dragStep;
        const int UndoDepth = 30;

        public WorldInput(GameLoop loop, CameraInput cam, WorldView view, HudController hud, UiState ui)
        {
            this.loop = loop;
            this.cam = cam;
            this.view = view;
            this.hud = hud;
            Ui = ui;
        }

        WorldState W { get { return loop.World; } }

        public void Update()
        {
            if (W == null) return;
            Ui.pointerOverUi = hud.IsPointerOverUi(Pointer.Position);
            // On a touch screen one finger draws with the belt and dismantle tools, and pans the board otherwise.
            Pointer.DrawMode = Ui.activeTool == UiTool.DrawBelt || Ui.activeTool == UiTool.Dismantle;
            HandleKeyboard();
            UpdateSelection();

            if (W.mode == MatchMode.Ended) return;
            if (Ui.activeTool == UiTool.DrawBelt) HandleBeltDrag();
            if (Ui.activeTool == UiTool.Dismantle) HandleDismantle();
            if (Ui.activeTool != UiTool.RouteBelt) routeFrom = -1;
            HandleBoxSelect();
            if (Ui.pointerOverUi) return;

            // A right click, or a long press on a touch screen.
            if ((Input.GetMouseButtonUp(1) && !cam.DragMoved) || Pointer.Secondary)
            {
                var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
                if (Ui.activeTool == UiTool.Select && craft != null && craft.owner == Me && hoverValid)
                {
                    if (W.mode == MatchMode.Tactical) HandleTacticalClick();
                    else HandleWorldCraftOrder();
                }
                else if (Ui.activeTool == UiTool.BuildShipyard) Ui.shipyardRotation = (Ui.shipyardRotation + 1) % 4;
                else CancelTool();
            }

            if (Pointer.PrimaryDown)
            {
                switch (Ui.activeTool)
                {
                    case UiTool.BuildExtractor:
                    case UiTool.BuildRefinery:
                    case UiTool.BuildAssembler:
                    case UiTool.BuildDepot:
                    case UiTool.BuildShipyard:
                    case UiTool.BuildFort:
                    case UiTool.BuildStorage:
                    case UiTool.BuildPurifier:
                    case UiTool.BuildSolarArray:
                    case UiTool.BuildDronePad:
                        if (ConfirmTap("place it")) HandleBuildClick();
                        break;
                    case UiTool.LinkDronePad:
                        if (ConfirmTap("link the pad there")) HandleLinkClick();
                        break;
                    case UiTool.RouteBelt:
                        HandleRouteClick();
                        break;
                    case UiTool.DrawBelt:
                    case UiTool.Dismantle:
                        break; // handled by drag
                    default:
                        SelectAtPointer();
                        break;
                }
            }
        }

        int armedCell = -1, armedX, armedY;

        /// <summary>
        /// On a touch screen a building is placed (or a pad linked) with a second tap on the same tile: the first
        /// moves the ghost there, so a slip of the finger never spends credits. With a mouse, one click does it.
        /// </summary>
        bool ConfirmTap(string what)
        {
            if (!Pointer.TouchMode) return true;
            if (!hoverValid) return false;
            if (armedCell == hoverCell && armedX == hoverX && armedY == hoverY) { armedCell = -1; return true; }
            armedCell = hoverCell;
            armedX = hoverX;
            armedY = hoverY;
            hud.ShowStatus("Tap the same tile again to " + what + ". Tap elsewhere to move it; long-press or Cancel to stop.", false);
            return false;
        }

        /// <summary>The touch bar's buttons: what the Esc and Z keys do with a keyboard.</summary>
        public void TouchCancel()
        {
            if (hud.BeltTierPickerOpen) hud.CloseBeltTierPicker();
            else if (hud.ConfigOpen) hud.CloseConfig();
            else CancelTool();
            armedCell = -1;
        }

        public void TouchRotate()
        {
            if (Ui.activeTool == UiTool.BuildShipyard) Ui.shipyardRotation = (Ui.shipyardRotation + 1) % 4;
        }

        void HandleKeyboard()
        {
            // Esc closes the settings pop-up before it cancels anything else.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (hud.BeltTierPickerOpen) hud.CloseBeltTierPicker();
                else if (hud.ConfigOpen) hud.CloseConfig();
                else CancelTool();
            }
            bool command = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (W.mode == MatchMode.Factory && command)
            {
                if (Input.GetKeyDown(KeyCode.C)) hud.CopySettings();
                if (Input.GetKeyDown(KeyCode.V)) hud.PasteSettings();
                if (Input.GetKeyDown(KeyCode.Z)) Undo();
                return; // Ctrl/Cmd shortcuts never also trigger the plain-key tools
            }
            if (Input.GetKeyDown(KeyCode.C) && Ui.selectedBuildingId >= 0) hud.OpenConfig(Ui.selectedBuildingId);
            if (W.mode == MatchMode.Tactical && Input.GetKeyDown(KeyCode.Space)
                && W.encounter.turn == Me)
                Report(loop.Submit(new GameCommand { kind = CommandKind.EndTurn, actor = Me }));
            if (Input.GetKeyDown(KeyCode.P)) hud.ToggleProduction();
            if (W.mode != MatchMode.Factory) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetTool(UiTool.BuildExtractor);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetTool(UiTool.BuildRefinery);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetTool(UiTool.BuildAssembler);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetTool(UiTool.BuildDepot);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetTool(UiTool.BuildShipyard);
            if (Input.GetKeyDown(KeyCode.Alpha6)) SetTool(UiTool.BuildFort);
            if (Input.GetKeyDown(KeyCode.Alpha7)) SetTool(UiTool.BuildStorage);
            if (Input.GetKeyDown(KeyCode.Alpha8)) SetTool(UiTool.BuildPurifier);
            if (Input.GetKeyDown(KeyCode.Alpha9)) SetTool(UiTool.BuildSolarArray);
            if (Input.GetKeyDown(KeyCode.Alpha0)) SetTool(UiTool.BuildDronePad);
            if (Input.GetKeyDown(KeyCode.B))
            {
                // B picks the belt tool; pressed again while it is active, it steps through the speed tiers.
                if (Ui.activeTool == UiTool.DrawBelt) hud.SetBeltTier((Ui.beltTier + 1) % loop.Catalog.rules.beltTiers.Count);
                else SetTool(UiTool.DrawBelt);
            }
            if (Input.GetKeyDown(KeyCode.X)) SetTool(UiTool.Dismantle);
            if (Input.GetKeyDown(KeyCode.N)) { SetTool(UiTool.RouteBelt); hud.ShowStatus(Pointer.Words("Auto belt: click the machine the goods come from, then the one they go to. Esc to stop."), false); }
            if (Input.GetKeyDown(KeyCode.V)) ToggleBeltView(Ui);
            if (Ui.activeTool == UiTool.BuildShipyard && Input.GetKeyDown(KeyCode.Z))
                Ui.shipyardRotation = (Ui.shipyardRotation + (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 3 : 1)) % 4;
        }

        void SetTool(UiTool tool)
        {
            Ui.activeTool = tool;
            beltDragging = false;
            armedCell = -1;
            if (IsBuildingTool(tool)) Ui.beltView = false; // placing machines needs to see them
        }

        /// <summary>Belt View on or off; turning it on drops any building selection.</summary>
        public static void ToggleBeltView(UiState ui)
        {
            ui.beltView = !ui.beltView;
            if (ui.beltView)
            {
                ui.selectedBuildingId = -1;
                ui.selectedNodeId = -1;
                if (IsBuildingTool(ui.activeTool)) ui.activeTool = UiTool.Select;
            }
        }

        public static bool IsBuildingTool(UiTool tool)
        {
            return BuildingForTool(tool) != null;
        }

        void CancelTool()
        {
            if (Ui.activeTool == UiTool.RouteBelt && routeFrom >= 0) { routeFrom = -1; return; } // Esc first drops the picked source
            if (Ui.activeTool != UiTool.Select) Ui.activeTool = UiTool.Select;
            else { Ui.selectedCraftId = -1; Ui.selectedBuildingId = -1; Ui.selectedNodeId = -1; Ui.selectedBeltId = -1; Ui.groupCraftIds.Clear(); Ui.groupMode = false; }
            beltDragging = false;
            dismantling = false;
            Ui.beltFromCellId = -1;
        }

        /// <summary>Hover tracking, placement ghost, tactical overlays and attack preview.</summary>
        public void UpdateSelection()
        {
            int gx = 0, gy = 0;
            Vector3 hit = Vector3.zero;
            hoverValid = !Ui.pointerOverUi && cam.TryGetGroundTile(Pointer.Position, out gx, out gy, out hit)
                && GridRules.TryGlobalToLocal(loop.Map, gx, gy, out hoverCell, out hoverX, out hoverY);
            if (hoverValid) { hoverGX = gx; hoverGY = gy; hoverPoint = hit; }

            // The building under the mouse (its model, else the structure on the tile), for needs shown on hover.
            int hovered = -1;
            if (!Ui.pointerOverUi)
            {
                hovered = view.PickBuilding(Pointer.Position, 5f);
                if (hovered < 0 && hoverValid)
                {
                    var under = WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY) ?? GridRules.FootprintAt(W, loop.Catalog, hoverCell, hoverX, hoverY);
                    if (under != null) hovered = under.id;
                }
            }
            view.HoverBuildingId = hovered;

            // Outcrops stand up from their tile, so in perspective the ground under the cursor is often the tile
            // behind one. The Extractor tool snaps to the outcrop the cursor is actually over.
            if (!Ui.pointerOverUi && Ui.activeTool == UiTool.BuildExtractor)
            {
                var node = WorldLookup.GetNode(W, view.PickNode(Pointer.Position, 4f));
                if (node != null)
                {
                    hoverValid = true;
                    hoverCell = node.cellId;
                    hoverX = node.x;
                    hoverY = node.y;
                }
            }

            // Placement ghost.
            string buildingType = BuildingForTool(Ui.activeTool);
            if (hoverValid && buildingType != null && W.mode == MatchMode.Factory)
            {
                var def = CatalogRepository.GetBuilding(loop.Catalog, buildingType);
                bool shipyard = Ui.activeTool == UiTool.BuildShipyard;
                var ok = GridRules.CanPlaceBuilding(W, loop.Catalog, loop.Map, def, Me, hoverCell, hoverX, hoverY, shipyard ? Ui.shipyardRotation : 0);
                if (shipyard)
                {
                    view.SetHover(true, hoverCell, hoverX, hoverY, ok.success ? new Color(0.3f, 1f, 0.5f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f),
                        GridRules.ShipyardExtraTiles(def, hoverX, hoverY, Ui.shipyardRotation));
                }
                else view.SetHover(true, hoverCell, hoverX, hoverY, ok.success ? new Color(0.3f, 1f, 0.5f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f));
            }
            else if (hoverValid && Ui.activeTool == UiTool.DrawBelt && W.mode == MatchMode.Factory)
            {
                var cell = WorldLookup.GetCell(W, hoverCell);
                bool owned = cell != null && cell.owner == Me;
                view.SetHover(true, hoverCell, hoverX, hoverY, owned ? new Color(0.5f, 0.85f, 1f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f));
            }
            else if (Ui.activeTool == UiTool.LinkDronePad && W.mode == MatchMode.Factory)
                UpdateLinkHover();
            else if (Ui.activeTool == UiTool.RouteBelt && W.mode == MatchMode.Factory)
                UpdateRouteHover();
            else if (hoverValid && Ui.activeTool == UiTool.Dismantle && W.mode == MatchMode.Factory)
                view.SetHover(true, hoverCell, hoverX, hoverY, new Color(1f, 0.45f, 0.2f, 0.4f));
            else view.SetHover(false, 0, 0, 0, Color.clear);

            // Tactical overlays for the selected craft.
            moveTargets.Clear();
            attackTargets.Clear();
            var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            var e = W.encounter;
            if (W.mode == MatchMode.Tactical && craft != null && craft.owner == Me && e.participantCraftIds.Contains(craft.id))
            {
                foreach (int cellId in e.battleCellIds)
                {
                    if (loop.Services.Movement.FindTacticalPath(W, loop.Map, loop.Catalog, craft.id, cellId) != null) moveTargets.Add(cellId);
                    if (CombatResolver.HasTargets(W, loop.Catalog, cellId, Me) && loop.Services.Combat.Preview(W, loop.Catalog, loop.Map, craft.id, cellId).legal)
                        attackTargets.Add(cellId);
                }
            }
            else if (W.mode == MatchMode.Tactical && craft != null && craft.owner == Me && WorldLookup.IsCraftLive(craft))
            {
                // A craft outside the battle can join it: any battle cell within its movement the enemy does not hold.
                var def = CatalogRepository.GetCraft(loop.Catalog, craft.typeId);
                int reach = def != null ? System.Math.Max(1, def.movement) : 1;
                foreach (int cellId in e.battleCellIds)
                {
                    int d = WorldLookup.CellDistance(loop.Map, craft.cellId, cellId);
                    if (d >= 0 && d <= reach && !CombatResolver.HasTargets(W, loop.Catalog, cellId, Me)) moveTargets.Add(cellId);
                }
            }
            view.SetTacticalFocus(e, moveTargets, attackTargets);

            if (W.mode == MatchMode.Factory) UpdateOrderHint(craft);

            if (W.mode == MatchMode.Tactical)
            {
                int craftId = craft != null && craft.owner == Me ? craft.id : -1;
                int cellId = hoverValid ? hoverCell : -1;
                // The enemy ship or Fort under the pointer is the target; elsewhere in a cell, the first that may be struck.
                int targetCraft, targetFort;
                int targetCell = HoveredTarget(out targetCraft, out targetFort);
                if (targetCell >= 0) cellId = targetCell;
                int targetKey = targetCraft >= 0 ? targetCraft : targetFort >= 0 ? -2 - targetFort : -1;
                if (craftId != lastPreviewCraft || cellId != lastPreviewCell || targetKey != lastPreviewTarget || loop.Revision != lastPreviewRevision)
                {
                    lastPreviewCraft = craftId;
                    lastPreviewCell = cellId;
                    lastPreviewTarget = targetKey;
                    lastPreviewRevision = loop.Revision;
                    if (craftId >= 0 && cellId >= 0 && CombatResolver.HasTargets(W, loop.Catalog, cellId, Me))
                        hud.ShowCombatPreview(loop.Services.Combat.Preview(W, loop.Catalog, loop.Map, craftId, cellId, targetCraft, targetFort));
                    else hud.ShowCombatPreview(null);
                }
            }
        }

        int lastHintCraft = -2, lastHintCell = -2;

        /// <summary>With one of your units selected, says what a right-click on the hovered cell will do.</summary>
        void UpdateOrderHint(CraftState craft)
        {
            int craftId = craft != null && craft.owner == Me && Ui.activeTool == UiTool.Select ? craft.id : -1;
            int cellId = hoverValid && craftId >= 0 ? hoverCell : -1;
            if (craftId == lastHintCraft && cellId == lastHintCell) return;
            lastHintCraft = craftId;
            lastHintCell = cellId;
            if (craftId < 0 || cellId < 0) return;
            var def = CatalogRepository.GetCraft(loop.Catalog, craft.typeId);
            string name = def != null ? def.displayName : "unit";
            var cell = WorldLookup.GetCell(W, cellId);
            string text;
            if (cellId == craft.cellId)
                text = cell.owner == Me ? name + " is in your cell " + cellId + "."
                    : WorldLookup.AreAllies(W, cell.owner, Me) ? name + " is in your ally's cell " + cellId + "."
                    : "Right-click: " + name + " claims cell " + cellId + " (holds its control point).";
            else if (!WorldLookup.AreAllies(W, cell.owner, Me) && CombatResolver.HasTargets(W, loop.Catalog, cellId, Me))
                text = "Right-click: send " + name + " to attack cell " + cellId + ".";
            else if (!WorldLookup.AreAllies(W, cell.owner, Me))
                text = "Right-click: send " + name + " to claim cell " + cellId + ".";
            else
                text = "Right-click: move " + name + " to cell " + cellId + ".";
            hud.ShowStatus(text, false);
        }

        public static string BuildingForTool(UiTool tool)
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
                default: return null;
            }
        }

        void SelectAtPointer()
        {
            // Shift-click (or the touch Group switch) adds units to the selection or takes them out of it.
            bool adding = W.mode == MatchMode.Factory && (Ui.groupMode || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            int craftId = view.PickCraft(Pointer.Position, 28f, W);
            if (adding)
            {
                var picked = WorldLookup.GetCraft(W, craftId);
                if (picked != null && picked.owner == Me) ToggleGroup(picked.id);
                return; // a slip onto open ground keeps the group
            }
            Ui.groupCraftIds.Clear();
            Ui.selectedBeltId = -1;
            if (craftId >= 0)
            {
                Ui.selectedCraftId = craftId;
                Ui.selectedBuildingId = -1;
                Ui.selectedNodeId = -1;
                var craft = WorldLookup.GetCraft(W, craftId);
                Ui.selectedCellId = craft.cellId;
                return;
            }
            if (Ui.beltView)
            {
                // Buildings are see-through and inert: a click picks the belt on (or nearest to) the tile.
                if (!hoverValid) return;
                Ui.selectedCellId = hoverCell;
                Ui.selectedCraftId = -1;
                Ui.selectedNodeId = -1;
                Ui.selectedBuildingId = -1;
                // The belt under the pointer at belt height, then any belt on the tile.
                var under = BeltUnderPointer(0.45f);
                Ui.selectedBeltId = under != null ? under.id : BeltAtTile(hoverCell, hoverX, hoverY);
                return;
            }
            int buildingId = view.PickBuilding(Pointer.Position, 5f);
            var pickedBuilding = WorldLookup.GetBuilding(W, buildingId);
            if (pickedBuilding != null)
            {
                Ui.selectedBuildingId = pickedBuilding.id;
                Ui.selectedNodeId = -1;
                Ui.selectedCraftId = -1;
                Ui.selectedCellId = pickedBuilding.cellId;
                return;
            }
            int nodeId = view.PickNode(Pointer.Position, 5f);
            var pickedNode = WorldLookup.GetNode(W, nodeId);
            var groundBuilding = hoverValid ? WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY)
                ?? GridRules.FootprintAt(W, loop.Catalog, hoverCell, hoverX, hoverY) : null;
            // A structure owns its ground tile. Untapped asteroids can also be selected by their
            // floating sprite; a tapped deposit is reachable through its extractor's link button.
            if (pickedNode != null && groundBuilding == null)
            {
                Ui.selectedNodeId = pickedNode.id;
                Ui.selectedCellId = pickedNode.cellId;
                Ui.selectedBuildingId = -1;
                Ui.selectedCraftId = -1;
                return;
            }
            if (!hoverValid) return;
            // A belt the pointer is actually on wins over the tile it happens to be over.
            var pointed = BeltUnderPointer(0.26f);
            if (pointed != null)
            {
                Ui.selectedCellId = hoverCell;
                Ui.selectedCraftId = -1;
                Ui.selectedNodeId = -1;
                Ui.selectedBuildingId = -1;
                Ui.selectedBeltId = pointed.id;
                return;
            }
            Ui.selectedCellId = hoverCell;
            Ui.selectedCraftId = -1;
            Ui.selectedNodeId = -1;
            Ui.selectedBuildingId = groundBuilding != null ? groundBuilding.id : -1;
            if (groundBuilding == null)
            {
                var tileNode = WorldLookup.GetNodeAt(W, hoverCell, hoverX, hoverY);
                if (tileNode != null) Ui.selectedNodeId = tileNode.id;
                else Ui.selectedBeltId = BeltAtTile(hoverCell, hoverX, hoverY);
            }
        }

        /// <summary>A belt on this tile: one leaving it if any, otherwise one arriving; -1 when there is none.</summary>
        int BeltAtTile(int cellId, int x, int y)
        {
            int arriving = -1;
            foreach (var s in W.belts)
            {
                if (s.fromCellId == cellId && s.fromX == x && s.fromY == y) return s.id;
                if (arriving < 0 && s.toCellId == cellId && s.toX == x && s.toY == y) arriving = s.id;
            }
            return arriving;
        }

        public void HandleBuildClick()
        {
            if (!hoverValid) return;
            var type = BuildingForTool(Ui.activeTool);
            var r = loop.Submit(new GameCommand { kind = CommandKind.PlaceBuilding, actor = Me, contentId = type, fromCellId = hoverCell, fromX = hoverX, fromY = hoverY, rotation = type == "shipyard" ? Ui.shipyardRotation : 0 });
            Report(r);
            if (r.success)
            {
                Ui.selectedCellId = hoverCell;
                var b = WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY);
                Ui.selectedBuildingId = b != null ? b.id : -1;
                if (b != null) { var step = new UndoStep(); step.buildings.Add(b.id); PushUndo(step); }
                // A new machine starts on the recipe picked in the build menu; with none picked, its settings open.
                var placedDef = b != null ? CatalogRepository.GetBuilding(loop.Catalog, b.typeId) : null;
                string recipe;
                if (placedDef != null && placedDef.allowedRecipeIds.Count > 0 && Ui.placeRecipe.TryGetValue(type, out recipe) && !string.IsNullOrEmpty(recipe))
                    loop.Submit(new GameCommand { kind = CommandKind.SelectRecipe, actor = Me, buildingId = b.id, contentId = recipe });
                if (placedDef != null && placedDef.allowedRecipeIds.Count > 0 && string.IsNullOrEmpty(b.selectedRecipeId)) hud.OpenConfig(b.id);
                Ui.selectedCraftId = -1;
                Ui.selectedNodeId = -1;
            }
        }

        /// <summary>Each tile step under the dragged pointer becomes one PlaceBelt command.</summary>
        public void HandleBeltDrag()
        {
            if (W.mode != MatchMode.Factory) { beltDragging = false; return; }
            if (Pointer.PrimaryDown && !Ui.pointerOverUi && hoverValid)
            {
                beltDragging = true;
                dragStep = new UndoStep();
                PushUndo(dragStep);
                lastGX = hoverGX;
                lastGY = hoverGY;
                Ui.beltFromCellId = hoverCell;
                Ui.beltFromX = hoverX;
                Ui.beltFromY = hoverY;
                return;
            }
            if (!beltDragging) return;
            if (!Pointer.PrimaryHeld) { beltDragging = false; Ui.beltFromCellId = -1; return; }
            if (!hoverValid || (hoverGX == lastGX && hoverGY == lastGY)) return;

            // Walk one orthogonal step at a time toward the pointer (x first), so fast drags never jump diagonally.
            int guard = 0;
            while ((lastGX != hoverGX || lastGY != hoverGY) && guard++ < 24)
            {
                int nx = lastGX + (hoverGX > lastGX ? 1 : hoverGX < lastGX ? -1 : 0);
                int ny = nx != lastGX ? lastGY : lastGY + (hoverGY > lastGY ? 1 : -1);
                int fromCell, fx, fy, toCell, tx, ty;
                if (!GridRules.TryGlobalToLocal(loop.Map, lastGX, lastGY, out fromCell, out fx, out fy)
                    || !GridRules.TryGlobalToLocal(loop.Map, nx, ny, out toCell, out tx, out ty)) { beltDragging = false; return; }
                var r = loop.Submit(new GameCommand
                {
                    kind = CommandKind.PlaceBelt, actor = Me,
                    fromCellId = fromCell, fromX = fx, fromY = fy, toCellId = toCell, toX = tx, toY = ty
                });
                if (r.success) RecordBelt(dragStep, fromCell, fx, fy, toCell, tx, ty);
                if (!r.success && r.code != "DUPLICATE_EDGE")
                {
                    Report(r);
                    beltDragging = false;
                    Ui.beltFromCellId = -1;
                    return;
                }
                // Lay it at the chosen speed tier: the new belt (or one already there) is upgraded a tier at a time.
                if (!RaiseToChosenTier(fromCell, fx, fy, toCell, tx, ty))
                {
                    beltDragging = false;
                    Ui.beltFromCellId = -1;
                    return;
                }
                lastGX = nx;
                lastGY = ny;
                Ui.beltFromCellId = toCell;
                Ui.beltFromX = tx;
                Ui.beltFromY = ty;
            }
        }

        // ------------------------------------------------------------ groups of units

        /// <summary>
        /// The player's units selected now: the selected unit first, then the rest of its group. Only live units
        /// that are still the player's.
        /// </summary>
        public static List<CraftState> SelectedUnits(WorldState w, UiState ui)
        {
            var units = new List<CraftState>();
            var first = WorldLookup.GetCraft(w, ui.selectedCraftId);
            if (first != null && first.owner == Me && WorldLookup.IsCraftLive(first)) units.Add(first);
            foreach (int id in ui.groupCraftIds)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k != null && k.owner == Me && WorldLookup.IsCraftLive(k) && !units.Contains(k)) units.Add(k);
            }
            return units;
        }

        /// <summary>Adds a unit to the selected group, or takes it out.</summary>
        public void ToggleGroup(int craftId)
        {
            var unit = WorldLookup.GetCraft(W, craftId);
            if (unit == null || unit.owner != Me || W.mode != MatchMode.Factory) return;
            var first = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            if (first == null || first.owner != Me)
            {
                Ui.selectedCraftId = craftId;
                Ui.groupCraftIds.Clear();
            }
            else if (craftId == first.id)
            {
                // Taking out the lead unit hands the lead to the next one.
                Ui.selectedCraftId = Ui.groupCraftIds.Count > 0 ? Ui.groupCraftIds[0] : -1;
                if (Ui.groupCraftIds.Count > 0) Ui.groupCraftIds.RemoveAt(0);
            }
            else if (!Ui.groupCraftIds.Remove(craftId)) Ui.groupCraftIds.Add(craftId);
            Ui.selectedBuildingId = -1;
            Ui.selectedNodeId = -1;
            Ui.selectedBeltId = -1;
            var lead = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            if (lead != null) Ui.selectedCellId = lead.cellId;
            int n = SelectedUnits(W, Ui).Count;
            hud.ShowStatus(n == 0 ? "No units selected." : n == 1 ? "1 unit selected." : n + " units selected. " + Pointer.Words("Right-click a cell to order them all."), false);
        }

        /// <summary>Selects every unit of the player's in a cell (the panel's "Select all here").</summary>
        public void SelectAllInCell(int cellId)
        {
            var ids = new List<int>();
            foreach (var k in W.crafts)
                if (k.owner == Me && k.cellId == cellId && WorldLookup.IsCraftLive(k)) ids.Add(k.id);
            SetGroup(ids, false);
        }

        void SetGroup(List<int> ids, bool add)
        {
            if (ids.Count == 0) return;
            if (!add || WorldLookup.GetCraft(W, Ui.selectedCraftId) == null)
            {
                Ui.selectedCraftId = ids[0];
                Ui.groupCraftIds.Clear();
            }
            foreach (int id in ids)
                if (id != Ui.selectedCraftId && !Ui.groupCraftIds.Contains(id)) Ui.groupCraftIds.Add(id);
            Ui.selectedBuildingId = -1;
            Ui.selectedNodeId = -1;
            Ui.selectedBeltId = -1;
            Ui.selectedCellId = WorldLookup.GetCraft(W, Ui.selectedCraftId).cellId;
            int n = SelectedUnits(W, Ui).Count;
            hud.ShowStatus(n == 1 ? "1 unit selected." : n + " units selected. " + Pointer.Words("Right-click a cell to order them all."), false);
        }

        // Box selection: drag with the left mouse button over the map (Shift adds to the selection).
        bool boxArmed, boxing;
        Vector2 boxStart;

        void HandleBoxSelect()
        {
            if (Pointer.TouchMode || W.mode != MatchMode.Factory || Ui.activeTool != UiTool.Select) { EndBox(); return; }
            if (Pointer.PrimaryDown && !Ui.pointerOverUi) { boxArmed = true; boxing = false; boxStart = Pointer.Position; return; }
            if (!boxArmed) return;
            Vector2 now = Pointer.Position;
            if (Pointer.PrimaryHeld)
            {
                if (!boxing && (now - boxStart).magnitude > 12f) boxing = true;
                if (boxing) hud.ShowSelectBox(true, boxStart, now);
                return;
            }
            if (boxing)
            {
                var rect = Rect.MinMaxRect(Mathf.Min(boxStart.x, now.x), Mathf.Min(boxStart.y, now.y), Mathf.Max(boxStart.x, now.x), Mathf.Max(boxStart.y, now.y));
                bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                var ids = view.CraftsInScreenRect(rect, W, Me);
                if (ids.Count > 0) SetGroup(ids, add);
            }
            EndBox();
        }

        void EndBox()
        {
            if (boxing) hud.ShowSelectBox(false, Vector2.zero, Vector2.zero);
            boxArmed = boxing = false;
        }

        /// <summary>Keeps only the lead unit selected.</summary>
        public void ClearGroup()
        {
            Ui.groupCraftIds.Clear();
            Ui.groupMode = false;
            hud.ShowStatus("Group cleared.", false);
        }

        /// <summary>Touch: switches Group on or off (while on, taps on units add them to the selection or take them out).</summary>
        public void ToggleGroupMode()
        {
            Ui.groupMode = !Ui.groupMode;
            hud.ShowStatus(Ui.groupMode ? "Group on: tap units to add them to the selection or take them out. Long-press a cell to order them all."
                                        : "Group off: a tap selects one thing.", false);
        }

        // ------------------------------------------------------------ auto belt

        int routeFrom = -1;
        int routeKey = int.MinValue, routeRevision = -1, routeTier = -1;
        BeltPlanResult routePlan;
        readonly AIBeltPlanner routePlanner = new AIBeltPlanner();

        /// <summary>The player's building on the hovered tile (or whose footprint covers it).</summary>
        BuildingState HoveredOwnBuilding()
        {
            if (!hoverValid) return null;
            var b = WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY) ?? GridRules.FootprintAt(W, loop.Catalog, hoverCell, hoverX, hoverY);
            return b != null && b.owner == Me ? b : null;
        }

        /// <summary>
        /// The belts a route from the picked machine to <paramref name="to"/> would add. Goods may enter a shipyard on
        /// any of its tiles, so that end is the tile pointed at; otherwise each end is the building's own tile.
        /// Cached until the target, the world or the belt speed changes.
        /// </summary>
        BeltPlanResult RouteTo(BuildingState to)
        {
            var from = WorldLookup.GetBuilding(W, routeFrom);
            if (from == null || to == null) return null;
            var toDef = CatalogRepository.GetBuilding(loop.Catalog, to.typeId);
            bool anyTile = toDef != null && toDef.kind == BuildingKind.Shipyard;
            int tx = anyTile ? hoverX : to.x, ty = anyTile ? hoverY : to.y;
            int key = to.id * 4096 + tx * 64 + ty;
            if (key == routeKey && routeRevision == loop.Revision && routeTier == Ui.beltTier && routePlan != null) return routePlan;
            routeKey = key;
            routeRevision = loop.Revision;
            routeTier = Ui.beltTier;
            routePlan = routePlanner.PlanBetween(W, loop.Catalog, loop.Map, Me, from.cellId, from.x, from.y, to.cellId, tx, ty);
            // The planner prices plain belts; the price at the chosen speed is worked out here and checked on click.
            if (!routePlan.success && routePlan.reason.StartsWith("Route needs")) { routePlan.success = true; routePlan.reason = ""; }
            if (routePlan.success || routePlan.steps.Count > 0)
            {
                int tier = Mathf.Clamp(Ui.beltTier, 0, loop.Catalog.rules.beltTiers.Count - 1);
                routePlan.totalCost = routePlan.steps.Count * (loop.Catalog.rules.beltCost + HudController.TierUpgradeCost(loop.Catalog, tier));
            }
            return routePlan;
        }

        /// <summary>Lights the source machine, and the path the route would take to the machine under the pointer.</summary>
        void UpdateRouteHover()
        {
            var under = HoveredOwnBuilding();
            var from = WorldLookup.GetBuilding(W, routeFrom);
            if (from == null)
            {
                if (hoverValid) view.SetHover(true, hoverCell, hoverX, hoverY, under != null ? new Color(0.5f, 0.85f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.15f));
                else view.SetHover(false, 0, 0, 0, Color.clear);
                return;
            }
            var tiles = new List<int[]>();
            Color colour = new Color(1f, 1f, 1f, 0.15f);
            if (under != null && under.id != from.id)
            {
                var plan = RouteTo(under);
                bool affordable = plan != null && plan.totalCost <= WorldLookup.GetFaction(W, Me).currency;
                if (plan != null && plan.success)
                    foreach (var st in plan.steps) if (st.toCellId == from.cellId) tiles.Add(new[] { st.toX, st.toY });
                colour = plan != null && plan.steps.Count > 0 && affordable ? new Color(0.3f, 1f, 0.5f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f);
            }
            tiles.Add(new[] { from.x, from.y });
            // The highlight lives in the source's cell (routes never leave an asteroid).
            view.SetHover(true, from.cellId, from.x, from.y, colour, tiles);
        }

        /// <summary>First click picks the source machine; the second lays the route to the machine clicked.</summary>
        void HandleRouteClick()
        {
            if (W.mode != MatchMode.Factory) return;
            var under = HoveredOwnBuilding();
            if (under == null) { hud.ShowStatus(Pointer.Words("Click one of your machines."), true); return; }
            var name = CatalogRepository.GetBuilding(loop.Catalog, under.typeId);
            string label = name != null ? name.displayName : under.typeId;
            if (routeFrom < 0 || under.id == routeFrom || WorldLookup.GetBuilding(W, routeFrom) == null)
            {
                routeFrom = under.id == routeFrom ? -1 : under.id;
                armedCell = -1;
                hud.ShowStatus(routeFrom < 0 ? Pointer.Words("Auto belt: click the machine the goods come from.")
                                             : Pointer.Words("From " + label + ": now click the machine the goods go to."), false);
                return;
            }
            if (under.cellId != WorldLookup.GetBuilding(W, routeFrom).cellId) { hud.ShowStatus("Belts cannot cross between asteroids; use a Drone Pad.", true); return; }
            var plan = RouteTo(under);
            if (plan == null || !plan.success) { hud.ShowStatus(plan != null ? plan.reason : "No route.", true); return; }
            if (plan.steps.Count == 0) { hud.ShowStatus("Those machines are already connected.", false); routeFrom = -1; return; }
            if (plan.totalCost > WorldLookup.GetFaction(W, Me).currency)
            {
                hud.ShowStatus("That route needs " + plan.totalCost + " credits (" + plan.steps.Count + " belts).", true);
                return;
            }
            if (!ConfirmTap("lay " + plan.steps.Count + " belts for " + plan.totalCost)) return;
            var step = new UndoStep();
            PushUndo(step);
            int laid = 0;
            foreach (var st in plan.steps)
            {
                var r = loop.Submit(new GameCommand
                {
                    kind = CommandKind.PlaceBelt, actor = Me,
                    fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY, toCellId = st.toCellId, toX = st.toX, toY = st.toY
                });
                if (!r.success && r.code != "DUPLICATE_EDGE") { Report(r); break; }
                if (r.success) { RecordBelt(step, st.fromCellId, st.fromX, st.fromY, st.toCellId, st.toX, st.toY); laid++; }
                if (!RaiseToChosenTier(st.fromCellId, st.fromX, st.fromY, st.toCellId, st.toX, st.toY)) break;
            }
            routeFrom = -1;
            routePlan = null;
            if (laid > 0) hud.ShowStatus("Laid " + laid + " belt" + (laid == 1 ? "" : "s") + " to " + label + ". " + Pointer.Words("Ctrl+Z undoes it.").Replace("Ctrl+Z", Pointer.TouchMode ? "Undo" : "Ctrl+Z"), false);
        }

        BeltSegmentState PlayerBelt(int fromCell, int fx, int fy, int toCell, int tx, int ty)
        {
            foreach (var s in W.belts)
                if (s.owner == Me && s.fromCellId == fromCell && s.fromX == fx && s.fromY == fy && s.toCellId == toCell && s.toX == tx && s.toY == ty) return s;
            return null;
        }

        /// <summary>Adds a belt just laid (not one reversed for free) to an undo step.</summary>
        void RecordBelt(UndoStep step, int fromCell, int fx, int fy, int toCell, int tx, int ty)
        {
            var belt = PlayerBelt(fromCell, fx, fy, toCell, tx, ty);
            if (step != null && belt != null && belt.placedTick >= 0 && !step.belts.Contains(belt.id)) step.belts.Add(belt.id);
        }

        void PushUndo(UndoStep step)
        {
            undo.RemoveAll(u => u.buildings.Count == 0 && u.belts.Count == 0); // drags that laid nothing
            undo.Add(step);
            if (undo.Count > UndoDepth) undo.RemoveAt(0);
        }

        /// <summary>True when there is a recent placement that Undo would take back.</summary>
        public bool CanUndo
        {
            get
            {
                for (int i = undo.Count - 1; i >= 0; i--)
                    if (StepLive(undo[i])) return true;
                return false;
            }
        }

        bool StepLive(UndoStep step)
        {
            foreach (var id in step.buildings)
            {
                var b = WorldLookup.GetBuilding(W, id);
                if (b != null && CommandRouter.CanUndo(W, Me, b.owner, b.placedTick)) return true;
            }
            foreach (var id in step.belts)
            {
                var b = WorldLookup.GetBelt(W, id);
                if (b != null && CommandRouter.CanUndo(W, Me, b.owner, b.placedTick)) return true;
            }
            return false;
        }

        /// <summary>
        /// Takes back the last placement (a building, or a whole belt drag) for a full refund, if it was placed in the
        /// last minute. Older steps, and ones already dismantled, are skipped.
        /// </summary>
        public void Undo()
        {
            if (W.mode != MatchMode.Factory) { hud.ShowStatus("Undo is paused during battle.", true); return; }
            while (undo.Count > 0)
            {
                var step = undo[undo.Count - 1];
                undo.RemoveAt(undo.Count - 1);
                if (!StepLive(step)) continue;
                int refund = 0, removed = 0;
                for (int i = step.belts.Count - 1; i >= 0; i--)
                {
                    var b = WorldLookup.GetBelt(W, step.belts[i]);
                    if (b == null) continue;
                    int paid = b.paidCredits;
                    if (loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = Me, beltId = b.id, contentId = "undo" }).success) { refund += paid; removed++; }
                }
                string name = null;
                foreach (var id in step.buildings)
                {
                    var b = WorldLookup.GetBuilding(W, id);
                    if (b == null) continue;
                    int paid = b.paidCredits;
                    var def = CatalogRepository.GetBuilding(loop.Catalog, b.typeId);
                    if (loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = Me, buildingId = b.id, contentId = "undo" }).success)
                    {
                        refund += paid; removed++;
                        name = def != null ? def.displayName : b.typeId;
                        if (Ui.selectedBuildingId == id) Ui.selectedBuildingId = -1;
                        if (hud.ConfigOpen) hud.CloseConfig();
                    }
                }
                if (removed == 0) continue;
                string what = name ?? (removed == 1 ? "1 belt" : removed + " belts");
                hud.ShowStatus("Undid " + what + (refund > 0 ? " (+" + refund + " refunded)." : "."), false);
                return;
            }
            hud.ShowStatus("Nothing to undo (only placements from the last minute can be undone).", true);
        }

        /// <summary>
        /// Upgrades the player's belt between two tiles up to the chosen tier. False (with the reason shown) when an
        /// upgrade could not be paid for, so the drag stops there.
        /// </summary>
        bool RaiseToChosenTier(int fromCell, int fx, int fy, int toCell, int tx, int ty)
        {
            BeltSegmentState belt = null;
            foreach (var s in W.belts)
                if (s.owner == Me && s.fromCellId == fromCell && s.fromX == fx && s.fromY == fy && s.toCellId == toCell && s.toX == tx && s.toY == ty) { belt = s; break; }
            if (belt == null) return true;
            int want = Mathf.Clamp(Ui.beltTier, 0, loop.Catalog.rules.beltTiers.Count - 1);
            for (int guard = 0; belt.tier < want && guard < 4; guard++)
            {
                var r = loop.Submit(new GameCommand { kind = CommandKind.UpgradeBelt, actor = Me, beltId = belt.id });
                if (!r.success) { Report(r); return false; }
            }
            return true;
        }

        /// <summary>
        /// Dismantle tool: pressing on a machine removes it; pressing or dragging over belts removes each belt whose
        /// midpoint passes under the pointer.
        /// </summary>
        public void HandleDismantle()
        {
            if (W.mode != MatchMode.Factory) { dismantling = false; return; }
            if (Pointer.PrimaryDown && !Ui.pointerOverUi && hoverValid)
            {
                dismantling = true;
                var b = Ui.beltView ? null : WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY); // Belt View: belts only
                // A press nearer a belt's midpoint than the machine's centre means the belt, so dragging along a
                // belt line never removes the machine it starts from.
                var nearBelt = BeltUnderPointer(0.4f);
                if (b != null && nearBelt != null)
                {
                    var center = WorldView.TileCenter(loop.Map, b.cellId, b.x, b.y);
                    var mid = (WorldView.TileCenter(loop.Map, nearBelt.fromCellId, nearBelt.fromX, nearBelt.fromY) + WorldView.TileCenter(loop.Map, nearBelt.toCellId, nearBelt.toX, nearBelt.toY)) * 0.5f;
                    if (Vector3.Distance(hoverPoint, mid) < Vector3.Distance(hoverPoint, center)) b = null;
                }
                if (b != null)
                {
                    dismantling = false;
                    Report(loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = Me, buildingId = b.id }));
                    if (Ui.selectedBuildingId == b.id) Ui.selectedBuildingId = -1;
                    return;
                }
            }
            if (!dismantling) return;
            if (!Pointer.PrimaryHeld) { dismantling = false; return; }
            if (!hoverValid) return;
            var belt = BeltUnderPointer(0.4f);
            if (belt == null) return;
            var r = loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = Me, beltId = belt.id });
            if (!r.success) { Report(r); dismantling = false; }
        }

        /// <summary>
        /// The player's belt the pointer is on: the pointer's ray meets the belt surface (raised above the deck, so
        /// a ground point would land behind the belt when seen at an angle), and the nearest belt along its length
        /// within <paramref name="maxDistance"/> wins. Drone links are left out.
        /// </summary>
        BeltSegmentState BeltUnderPointer(float maxDistance)
        {
            if (!hoverValid || cam == null || cam.Camera == null) return null;
            var ray = cam.Camera.ScreenPointToRay(Pointer.Position);
            var plane = new Plane(Vector3.up, new Vector3(0f, hoverPoint.y + ModelLibrary.BeltSurface, 0f));
            float enter;
            if (!plane.Raycast(ray, out enter)) return null;
            var p = ray.GetPoint(enter);
            var point = new Vector2(p.x, p.z);
            BeltSegmentState best = null;
            float bestDist = maxDistance;
            foreach (var s in W.belts)
            {
                if (s.owner != Me || s.fromCellId != s.toCellId) continue;
                var a3 = WorldView.TileCenter(loop.Map, s.fromCellId, s.fromX, s.fromY);
                var b3 = WorldView.TileCenter(loop.Map, s.toCellId, s.toX, s.toY);
                var a = new Vector2(a3.x, a3.z);
                var ab = new Vector2(b3.x, b3.z) - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector2.Distance(point, a + ab * t);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        /// <summary>Right-click order in normal play: hold here, attack a defended cell, or travel.</summary>
        public void HandleWorldCraftOrder()
        {
            var units = SelectedUnits(W, Ui);
            if (units.Count > 1) { OrderGroup(units); return; }
            var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            if (craft == null) return;
            var target = WorldLookup.GetCell(W, hoverCell);
            GameCommand cmd;
            if (hoverCell == craft.cellId && craft.worldPathCellIds.Count == 0)
                cmd = new GameCommand { kind = CommandKind.StartCapture, actor = Me, craftId = craft.id, targetCellId = hoverCell };
            else if (!WorldLookup.AreAllies(W, target.owner, Me) && CombatResolver.HasTargets(W, loop.Catalog, hoverCell, Me))
                cmd = new GameCommand { kind = CommandKind.StartAttack, actor = Me, craftId = craft.id, targetCellId = hoverCell };
            else
                cmd = new GameCommand { kind = CommandKind.MoveWorld, actor = Me, craftId = craft.id, targetCellId = hoverCell };
            Report(loop.Submit(cmd));
        }

        /// <summary>
        /// One order for a whole group: each unit holds, attacks or travels to the hovered cell as it would alone.
        /// Units that must travel are sent first, so an attack that opens a battle at once does not stop the rest
        /// setting off; they join it as reinforcements once they arrive.
        /// </summary>
        void OrderGroup(List<CraftState> units)
        {
            var target = WorldLookup.GetCell(W, hoverCell);
            if (target == null) return;
            bool defended = !WorldLookup.AreAllies(W, target.owner, Me) && CombatResolver.HasTargets(W, loop.Catalog, hoverCell, Me);
            units.Sort((a, b) => WorldLookup.CellDistance(loop.Map, b.cellId, hoverCell).CompareTo(WorldLookup.CellDistance(loop.Map, a.cellId, hoverCell)));
            int ok = 0;
            string failure = null;
            foreach (var craft in units)
            {
                GameCommand cmd;
                if (hoverCell == craft.cellId && craft.worldPathCellIds.Count == 0)
                {
                    if (target.owner == Me) { ok++; continue; } // already there
                    cmd = new GameCommand { kind = CommandKind.StartCapture, actor = Me, craftId = craft.id, targetCellId = hoverCell };
                }
                else if (defended)
                    cmd = new GameCommand { kind = CommandKind.StartAttack, actor = Me, craftId = craft.id, targetCellId = hoverCell };
                else
                    cmd = new GameCommand { kind = CommandKind.MoveWorld, actor = Me, craftId = craft.id, targetCellId = hoverCell };
                var r = loop.Submit(cmd);
                if (r.success) ok++;
                else if (failure == null) failure = r.message;
                if (W.mode != MatchMode.Factory) break; // a battle began: the rest can join from the battle panel
            }
            string verb = defended ? "attacking" : "heading to";
            if (W.mode == MatchMode.Tactical)
                hud.ShowStatus("Battle for cell " + hoverCell + " begins. Units still on their way can join it as reinforcements.", false);
            else
                hud.ShowStatus(ok + " of " + units.Count + " units " + verb + " cell " + hoverCell + "." + (failure != null ? " Not all could go: " + failure : ""), ok == 0);
        }

        int lastLinkHoverCell = -1, lastLinkHoverX = -1, lastLinkHoverY = -1;

        /// <summary>
        /// Link mode: the hovered tile glows green where the selected pad can link (with the flight time) and red
        /// where it cannot (with why). Leaving the pad unselected ends the mode.
        /// </summary>
        void UpdateLinkHover()
        {
            var pad = WorldLookup.GetBuilding(W, Ui.selectedBuildingId);
            if (pad == null || !DroneLinks.IsPad(loop.Catalog, pad)) { Ui.activeTool = UiTool.Select; view.SetHover(false, 0, 0, 0, Color.clear); return; }
            if (!hoverValid) { view.SetHover(false, 0, 0, 0, Color.clear); lastLinkHoverCell = -1; return; }
            BuildingState target;
            var refusal = DroneLinks.CheckLink(W, loop.Catalog, loop.Map, Me, pad, hoverCell, hoverX, hoverY, out target);
            view.SetHover(true, hoverCell, hoverX, hoverY, refusal == null ? new Color(0.3f, 1f, 0.6f, 0.55f) : new Color(1f, 0.25f, 0.2f, 0.45f));
            if (hoverCell == lastLinkHoverCell && hoverX == lastLinkHoverX && hoverY == lastLinkHoverY) return;
            lastLinkHoverCell = hoverCell; lastLinkHoverX = hoverX; lastLinkHoverY = hoverY;
            if (hoverCell == pad.cellId) return; // the home asteroid: nothing to say until the cursor reaches another
            if (refusal != null) { hud.ShowStatus(refusal.message, true); return; }
            float distance = MapLayout.Distance(loop.Map, pad.cellId, pad.x, pad.y, hoverCell, hoverX, hoverY);
            int ticks = BeltNetwork.TravelTicks(W, loop.Catalog, new BeltSegmentState
            {
                owner = Me,
                linkTicks = (int)System.Math.Ceiling(distance * loop.Catalog.rules.droneTicksPerUnit)
            });
            var padDef = CatalogRepository.GetBuilding(loop.Catalog, pad.typeId);
            hud.ShowStatus("Link here: " + (ticks * loop.Catalog.rules.tickMilliseconds / 1000f).ToString("0.0") + "s each way"
                + (target != null ? ", to your pad." : ", building a receiving pad (" + padDef.cost + ")."), false);
        }

        void HandleLinkClick()
        {
            if (Ui.pointerOverUi || !hoverValid) return;
            var r = loop.Submit(new GameCommand
            {
                kind = CommandKind.LinkDronePad, actor = Me, buildingId = Ui.selectedBuildingId,
                toCellId = hoverCell, toX = hoverX, toY = hoverY
            });
            Report(r);
            if (r.success) Ui.activeTool = UiTool.Select; // keep the pad selected to show the new link
        }

        /// <summary>
        /// The enemy ship or Fort under the pointer in battle: its cell, with the craft or Fort id (the other -1); -1 when
        /// the pointer is over no enemy.
        /// </summary>
        int HoveredTarget(out int craftId, out int fortId)
        {
            craftId = fortId = -1;
            if (Ui.pointerOverUi) return -1;
            var picked = WorldLookup.GetCraft(W, view.PickCraft(Pointer.Position, 28f, W));
            if (picked != null && WorldLookup.AreEnemies(W, Me, picked.owner)) { craftId = picked.id; return picked.cellId; }
            var building = WorldLookup.GetBuilding(W, view.PickBuilding(Pointer.Position, 5f));
            if (building != null && WorldLookup.AreEnemies(W, Me, building.owner) && CombatResolver.IsAttackable(W, loop.Catalog, building)) { fortId = building.id; return building.cellId; }
            return -1;
        }

        /// <summary>
        /// Right-click order in battle: attack the enemy ship or Fort under the pointer (or the first that may be struck
        /// in that cell), move to a legal cell (joining the battle from outside it), or hold the current one.
        /// </summary>
        public void HandleTacticalClick()
        {
            var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            if (craft == null) return;
            GameCommand cmd;
            int targetCraft, targetFort;
            int targetCell = HoveredTarget(out targetCraft, out targetFort);
            if (targetCell >= 0)
                cmd = new GameCommand { kind = CommandKind.TacticalAttack, actor = Me, craftId = craft.id, targetCellId = targetCell, targetCraftId = targetCraft, buildingId = targetFort };
            else if (CombatResolver.HasTargets(W, loop.Catalog, hoverCell, Me))
                cmd = new GameCommand { kind = CommandKind.TacticalAttack, actor = Me, craftId = craft.id, targetCellId = hoverCell };
            else if (hoverCell != craft.cellId)
                cmd = new GameCommand { kind = CommandKind.TacticalMove, actor = Me, craftId = craft.id, targetCellId = hoverCell };
            else
                cmd = new GameCommand { kind = CommandKind.StartCapture, actor = Me, craftId = craft.id, targetCellId = hoverCell };
            Report(loop.Submit(cmd));
        }

        void Report(CommandResult r)
        {
            if (r == null) return;
            if (!r.success) hud.ShowStatus(r.message, true);
            else if (!string.IsNullOrEmpty(r.message)) hud.ShowStatus(r.message, false);
        }
    }
}
