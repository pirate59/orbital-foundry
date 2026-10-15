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
        int lastPreviewCraft = -2, lastPreviewCell = -2, lastPreviewRevision = -1;
        readonly HashSet<int> moveTargets = new HashSet<int>();
        readonly HashSet<int> attackTargets = new HashSet<int>();

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
            Ui.pointerOverUi = hud.IsPointerOverUi(Input.mousePosition);
            HandleKeyboard();
            UpdateSelection();

            if (W.mode == MatchMode.Ended) return;
            if (Ui.activeTool == UiTool.DrawBelt) HandleBeltDrag();
            if (Ui.activeTool == UiTool.Dismantle) HandleDismantle();
            if (Ui.pointerOverUi) return;

            if (Input.GetMouseButtonUp(1) && !cam.DragMoved)
            {
                var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
                if (Ui.activeTool == UiTool.Select && craft != null && craft.owner == FactionId.Player && hoverValid)
                {
                    if (W.mode == MatchMode.Tactical) HandleTacticalClick();
                    else HandleWorldCraftOrder();
                }
                else CancelTool();
            }

            if (Input.GetMouseButtonDown(0))
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
                        HandleBuildClick();
                        break;
                    case UiTool.LinkDronePad:
                        HandleLinkClick();
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

        void HandleKeyboard()
        {
            // Esc closes the settings pop-up before it cancels anything else.
            if (Input.GetKeyDown(KeyCode.Escape)) { if (hud.ConfigOpen) hud.CloseConfig(); else CancelTool(); }
            bool command = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (W.mode == MatchMode.Factory && command)
            {
                if (Input.GetKeyDown(KeyCode.C)) hud.CopySettings();
                if (Input.GetKeyDown(KeyCode.V)) hud.PasteSettings();
                return; // Ctrl/Cmd shortcuts never also trigger the plain-key tools
            }
            if (Input.GetKeyDown(KeyCode.C) && Ui.selectedBuildingId >= 0) hud.OpenConfig(Ui.selectedBuildingId);
            if (W.mode == MatchMode.Tactical && Input.GetKeyDown(KeyCode.Space)
                && W.encounter.turn == FactionId.Player)
                Report(loop.Submit(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player }));
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
            if (Input.GetKeyDown(KeyCode.B)) SetTool(UiTool.DrawBelt);
            if (Input.GetKeyDown(KeyCode.X)) SetTool(UiTool.Dismantle);
            if (Input.GetKeyDown(KeyCode.V)) ToggleBeltView(Ui);
        }

        void SetTool(UiTool tool)
        {
            Ui.activeTool = tool;
            beltDragging = false;
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
            if (Ui.activeTool != UiTool.Select) Ui.activeTool = UiTool.Select;
            else { Ui.selectedCraftId = -1; Ui.selectedBuildingId = -1; Ui.selectedNodeId = -1; Ui.selectedBeltId = -1; }
            beltDragging = false;
            dismantling = false;
            Ui.beltFromCellId = -1;
        }

        /// <summary>Hover tracking, placement ghost, tactical overlays and attack preview.</summary>
        public void UpdateSelection()
        {
            int gx = 0, gy = 0;
            Vector3 hit = Vector3.zero;
            hoverValid = !Ui.pointerOverUi && cam.TryGetGroundTile(Input.mousePosition, out gx, out gy, out hit)
                && GridRules.TryGlobalToLocal(loop.Map, gx, gy, out hoverCell, out hoverX, out hoverY);
            if (hoverValid) { hoverGX = gx; hoverGY = gy; hoverPoint = hit; }

            // Outcrops stand up from their tile, so in perspective the ground under the cursor is often the tile
            // behind one. The Extractor tool snaps to the outcrop the cursor is actually over.
            if (!Ui.pointerOverUi && Ui.activeTool == UiTool.BuildExtractor)
            {
                var node = WorldLookup.GetNode(W, view.PickNode(Input.mousePosition, 4f));
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
                var ok = GridRules.CanPlaceBuilding(W, loop.Catalog, loop.Map, def, FactionId.Player, hoverCell, hoverX, hoverY);
                view.SetHover(true, hoverCell, hoverX, hoverY, ok.success ? new Color(0.3f, 1f, 0.5f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f));
            }
            else if (hoverValid && Ui.activeTool == UiTool.DrawBelt && W.mode == MatchMode.Factory)
            {
                var cell = WorldLookup.GetCell(W, hoverCell);
                bool owned = cell != null && cell.owner == FactionId.Player;
                view.SetHover(true, hoverCell, hoverX, hoverY, owned ? new Color(0.5f, 0.85f, 1f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f));
            }
            else if (Ui.activeTool == UiTool.LinkDronePad && W.mode == MatchMode.Factory)
                UpdateLinkHover();
            else if (hoverValid && Ui.activeTool == UiTool.Dismantle && W.mode == MatchMode.Factory)
                view.SetHover(true, hoverCell, hoverX, hoverY, new Color(1f, 0.45f, 0.2f, 0.4f));
            else view.SetHover(false, 0, 0, 0, Color.clear);

            // Tactical overlays for the selected craft.
            moveTargets.Clear();
            attackTargets.Clear();
            var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            var e = W.encounter;
            if (W.mode == MatchMode.Tactical && craft != null && craft.owner == FactionId.Player && e.participantCraftIds.Contains(craft.id))
            {
                foreach (int cellId in e.battleCellIds)
                {
                    if (loop.Services.Movement.FindTacticalPath(W, loop.Map, loop.Catalog, craft.id, cellId) != null) moveTargets.Add(cellId);
                    if (WorldLookup.HasOpposingCraft(W, cellId, FactionId.Player) && loop.Services.Combat.Preview(W, loop.Catalog, loop.Map, craft.id, cellId).legal)
                        attackTargets.Add(cellId);
                }
            }
            view.SetTacticalFocus(e, moveTargets, attackTargets);

            if (W.mode == MatchMode.Factory) UpdateOrderHint(craft);

            if (W.mode == MatchMode.Tactical)
            {
                int craftId = craft != null && craft.owner == FactionId.Player ? craft.id : -1;
                int cellId = hoverValid ? hoverCell : -1;
                if (craftId != lastPreviewCraft || cellId != lastPreviewCell || loop.Revision != lastPreviewRevision)
                {
                    lastPreviewCraft = craftId;
                    lastPreviewCell = cellId;
                    lastPreviewRevision = loop.Revision;
                    if (craftId >= 0 && cellId >= 0 && WorldLookup.HasOpposingCraft(W, cellId, FactionId.Player))
                        hud.ShowCombatPreview(loop.Services.Combat.Preview(W, loop.Catalog, loop.Map, craftId, cellId));
                    else hud.ShowCombatPreview(null);
                }
            }
        }

        int lastHintCraft = -2, lastHintCell = -2;

        /// <summary>With one of your units selected, says what a right-click on the hovered cell will do.</summary>
        void UpdateOrderHint(CraftState craft)
        {
            int craftId = craft != null && craft.owner == FactionId.Player && Ui.activeTool == UiTool.Select ? craft.id : -1;
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
                text = cell.owner == FactionId.Player ? name + " is in your cell " + cellId + "." : "Right-click: " + name + " claims cell " + cellId + " (holds its control point).";
            else if (cell.owner != FactionId.Player && WorldLookup.HasOpposingCraft(W, cellId, FactionId.Player))
                text = "Right-click: send " + name + " to attack cell " + cellId + ".";
            else if (cell.owner != FactionId.Player)
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
            Ui.selectedBeltId = -1;
            int craftId = view.PickCraft(Input.mousePosition, 28f, W);
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
                int onTile = BeltAtTile(hoverCell, hoverX, hoverY);
                var near = onTile < 0 ? NearestBelt(hoverPoint, 0.6f) : null;
                Ui.selectedBeltId = onTile >= 0 ? onTile : near != null ? near.id : -1;
                return;
            }
            int buildingId = view.PickBuilding(Input.mousePosition, 5f);
            var pickedBuilding = WorldLookup.GetBuilding(W, buildingId);
            if (pickedBuilding != null)
            {
                Ui.selectedBuildingId = pickedBuilding.id;
                Ui.selectedNodeId = -1;
                Ui.selectedCraftId = -1;
                Ui.selectedCellId = pickedBuilding.cellId;
                return;
            }
            int nodeId = view.PickNode(Input.mousePosition, 5f);
            var pickedNode = WorldLookup.GetNode(W, nodeId);
            var groundBuilding = hoverValid ? WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY) : null;
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
            var r = loop.Submit(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Player, contentId = type, fromCellId = hoverCell, fromX = hoverX, fromY = hoverY });
            Report(r);
            if (r.success)
            {
                Ui.selectedCellId = hoverCell;
                var b = WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY);
                Ui.selectedBuildingId = b != null ? b.id : -1;
                // A new machine has no recipe: open its settings so the player chooses one.
                var placedDef = b != null ? CatalogRepository.GetBuilding(loop.Catalog, b.typeId) : null;
                if (placedDef != null && placedDef.allowedRecipeIds.Count > 0 && string.IsNullOrEmpty(b.selectedRecipeId)) hud.OpenConfig(b.id);
                Ui.selectedCraftId = -1;
                Ui.selectedNodeId = -1;
            }
        }

        /// <summary>Each tile step under the dragged pointer becomes one PlaceBelt command.</summary>
        public void HandleBeltDrag()
        {
            if (W.mode != MatchMode.Factory) { beltDragging = false; return; }
            if (Input.GetMouseButtonDown(0) && !Ui.pointerOverUi && hoverValid)
            {
                beltDragging = true;
                lastGX = hoverGX;
                lastGY = hoverGY;
                Ui.beltFromCellId = hoverCell;
                Ui.beltFromX = hoverX;
                Ui.beltFromY = hoverY;
                return;
            }
            if (!beltDragging) return;
            if (!Input.GetMouseButton(0)) { beltDragging = false; Ui.beltFromCellId = -1; return; }
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
                    kind = CommandKind.PlaceBelt, actor = FactionId.Player,
                    fromCellId = fromCell, fromX = fx, fromY = fy, toCellId = toCell, toX = tx, toY = ty
                });
                if (!r.success && r.code != "DUPLICATE_EDGE")
                {
                    Report(r);
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

        /// <summary>
        /// Dismantle tool: pressing on a machine removes it; pressing or dragging over belts removes each belt whose
        /// midpoint passes under the pointer.
        /// </summary>
        public void HandleDismantle()
        {
            if (W.mode != MatchMode.Factory) { dismantling = false; return; }
            if (Input.GetMouseButtonDown(0) && !Ui.pointerOverUi && hoverValid)
            {
                dismantling = true;
                var b = Ui.beltView ? null : WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY); // Belt View: belts only
                // A press nearer a belt's midpoint than the machine's centre means the belt, so dragging along a
                // belt line never removes the machine it starts from.
                var nearBelt = NearestBelt(hoverPoint, 0.4f);
                if (b != null && nearBelt != null)
                {
                    var center = WorldView.TileCenter(loop.Map, b.cellId, b.x, b.y);
                    var mid = (WorldView.TileCenter(loop.Map, nearBelt.fromCellId, nearBelt.fromX, nearBelt.fromY) + WorldView.TileCenter(loop.Map, nearBelt.toCellId, nearBelt.toX, nearBelt.toY)) * 0.5f;
                    if (Vector3.Distance(hoverPoint, mid) < Vector3.Distance(hoverPoint, center)) b = null;
                }
                if (b != null)
                {
                    dismantling = false;
                    Report(loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = FactionId.Player, buildingId = b.id }));
                    if (Ui.selectedBuildingId == b.id) Ui.selectedBuildingId = -1;
                    return;
                }
            }
            if (!dismantling) return;
            if (!Input.GetMouseButton(0)) { dismantling = false; return; }
            if (!hoverValid) return;
            var belt = NearestBelt(hoverPoint, 0.4f);
            if (belt == null) return;
            var r = loop.Submit(new GameCommand { kind = CommandKind.Dismantle, actor = FactionId.Player, beltId = belt.id });
            if (!r.success) { Report(r); dismantling = false; }
        }

        BeltSegmentState NearestBelt(Vector3 point, float maxDistance)
        {
            BeltSegmentState best = null;
            float bestDist = maxDistance;
            foreach (var s in W.belts)
            {
                if (s.owner != FactionId.Player) continue;
                var mid = (WorldView.TileCenter(loop.Map, s.fromCellId, s.fromX, s.fromY) + WorldView.TileCenter(loop.Map, s.toCellId, s.toX, s.toY)) * 0.5f;
                float d = Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(point.x, point.z));
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        /// <summary>Right-click order in normal play: hold here, attack a defended cell, or travel.</summary>
        public void HandleWorldCraftOrder()
        {
            var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            if (craft == null) return;
            var target = WorldLookup.GetCell(W, hoverCell);
            GameCommand cmd;
            if (hoverCell == craft.cellId && craft.worldPathCellIds.Count == 0)
                cmd = new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Player, craftId = craft.id, targetCellId = hoverCell };
            else if (target.owner != FactionId.Player && WorldLookup.HasOpposingCraft(W, hoverCell, FactionId.Player))
                cmd = new GameCommand { kind = CommandKind.StartAttack, actor = FactionId.Player, craftId = craft.id, targetCellId = hoverCell };
            else
                cmd = new GameCommand { kind = CommandKind.MoveWorld, actor = FactionId.Player, craftId = craft.id, targetCellId = hoverCell };
            Report(loop.Submit(cmd));
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
            var refusal = DroneLinks.CheckLink(W, loop.Catalog, loop.Map, FactionId.Player, pad, hoverCell, hoverX, hoverY, out target);
            view.SetHover(true, hoverCell, hoverX, hoverY, refusal == null ? new Color(0.3f, 1f, 0.6f, 0.55f) : new Color(1f, 0.25f, 0.2f, 0.45f));
            if (hoverCell == lastLinkHoverCell && hoverX == lastLinkHoverX && hoverY == lastLinkHoverY) return;
            lastLinkHoverCell = hoverCell; lastLinkHoverX = hoverX; lastLinkHoverY = hoverY;
            if (hoverCell == pad.cellId) return; // the home asteroid: nothing to say until the cursor reaches another
            if (refusal != null) { hud.ShowStatus(refusal.message, true); return; }
            float distance = MapLayout.Distance(loop.Map, pad.cellId, pad.x, pad.y, hoverCell, hoverX, hoverY);
            int ticks = BeltNetwork.TravelTicks(W, loop.Catalog, new BeltSegmentState
            {
                owner = FactionId.Player,
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
                kind = CommandKind.LinkDronePad, actor = FactionId.Player, buildingId = Ui.selectedBuildingId,
                toCellId = hoverCell, toX = hoverX, toY = hoverY
            });
            Report(r);
            if (r.success) Ui.activeTool = UiTool.Select; // keep the pad selected to show the new link
        }

        /// <summary>Right-click order in battle: attack an enemy cell, move to a legal cell, or hold the current one.</summary>
        public void HandleTacticalClick()
        {
            var craft = WorldLookup.GetCraft(W, Ui.selectedCraftId);
            if (craft == null) return;
            GameCommand cmd;
            if (WorldLookup.HasOpposingCraft(W, hoverCell, FactionId.Player))
                cmd = new GameCommand { kind = CommandKind.TacticalAttack, actor = FactionId.Player, craftId = craft.id, targetCellId = hoverCell };
            else if (hoverCell != craft.cellId)
                cmd = new GameCommand { kind = CommandKind.TacticalMove, actor = FactionId.Player, craftId = craft.id, targetCellId = hoverCell };
            else
                cmd = new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Player, craftId = craft.id, targetCellId = hoverCell };
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
