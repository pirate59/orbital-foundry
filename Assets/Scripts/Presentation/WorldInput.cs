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
                        HandleBuildClick();
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
            if (Input.GetKeyDown(KeyCode.Escape)) CancelTool();
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
            if (Input.GetKeyDown(KeyCode.B)) SetTool(UiTool.DrawBelt);
            if (Input.GetKeyDown(KeyCode.X)) SetTool(UiTool.Dismantle);
        }

        void SetTool(UiTool tool)
        {
            Ui.activeTool = tool;
            beltDragging = false;
        }

        void CancelTool()
        {
            if (Ui.activeTool != UiTool.Select) Ui.activeTool = UiTool.Select;
            else { Ui.selectedCraftId = -1; Ui.selectedBuildingId = -1; }
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

        static string BuildingForTool(UiTool tool)
        {
            switch (tool)
            {
                case UiTool.BuildExtractor: return "extractor";
                case UiTool.BuildRefinery: return "refinery";
                case UiTool.BuildAssembler: return "assembler";
                case UiTool.BuildDepot: return "sale_depot";
                case UiTool.BuildShipyard: return "shipyard";
                case UiTool.BuildFort: return "fort";
                case UiTool.BuildStorage: return "storage";
                default: return null;
            }
        }

        void SelectAtPointer()
        {
            int craftId = view.PickCraft(Input.mousePosition, 28f, W);
            if (craftId >= 0)
            {
                Ui.selectedCraftId = craftId;
                Ui.selectedBuildingId = -1;
                var craft = WorldLookup.GetCraft(W, craftId);
                Ui.selectedCellId = craft.cellId;
                return;
            }
            if (!hoverValid) return;
            Ui.selectedCellId = hoverCell;
            var b = WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY);
            Ui.selectedBuildingId = b != null ? b.id : -1;
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
                var b = WorldLookup.GetBuildingAt(W, hoverCell, hoverX, hoverY);
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
