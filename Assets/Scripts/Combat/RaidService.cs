using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Normal-play craft travel. A craft whose worldDestinationCellId differs from its path's last cell is on an attack
    /// order: on arrival at the staging cell it opens an encounter (or walks in and holds an undefended target).
    /// </summary>
    public sealed class RaidService
    {
        readonly MovementRules movement;
        readonly CaptureService capture;

        public RaidService(MovementRules movement, CaptureService capture)
        {
            this.movement = movement;
            this.capture = capture;
        }

        /// <summary>Orders a craft to travel to a legal attack position next to <paramref name="targetCellId"/>.</summary>
        public CommandResult ScheduleRaid(WorldState w, MapDefinition map, ContentCatalog c, int craftId, int targetCellId)
        {
            var craft = WorldLookup.GetCraft(w, craftId);
            if (craft == null || !WorldLookup.IsCraftLive(craft)) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft is unavailable.");
            var path = movement.FindStagingPath(w, map, craft.owner, craft.cellId, targetCellId);
            if (path == null) return WorldLookup.Fail("NO_ROUTE", "No route reaches a cell next to the target.");
            BeginTravel(w, c, craft, path, targetCellId);
            if (craft.owner == FactionId.Ai)
            {
                w.ai.raidCraftId = craft.id;
                w.ai.raidTargetCellId = targetCellId;
                w.ai.raidWarningShown = false;
            }
            return WorldLookup.Ok("Craft en route to attack cell " + targetCellId + ".");
        }

        public void BeginTravel(WorldState w, ContentCatalog c, CraftState craft, List<int> path, int destinationCellId)
        {
            craft.worldPathCellIds = new List<int>(path);
            craft.worldPathIndex = 0;
            craft.worldDestinationCellId = destinationCellId;
            craft.worldTicksUntilNextCell = c.rules.worldMoveTicksPerCell;
            if (path.Count > 0)
            {
                // A departing defender hands its slot to a reserve.
                int oldCell = WorldLookup.ClearDefenderSlot(w, craft.id);
                if (oldCell != -1) WorldLookup.AutoAssignDefender(w, oldCell);
            }
        }

        public void TickWorldTravel(WorldState w, ContentCatalog c, MapDefinition map, EncounterService encounters)
        {
            var ordered = new List<CraftState>(w.crafts);
            ordered.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var craft in ordered)
            {
                if (w.mode != MatchMode.Factory) return;
                if (!WorldLookup.IsCraftLive(craft)) continue;
                if (craft.worldPathCellIds.Count == 0)
                {
                    if (craft.worldDestinationCellId != -1 && craft.worldDestinationCellId != craft.cellId)
                        Arrive(w, c, map, craft, encounters);
                    continue;
                }
                if (craft.worldTicksUntilNextCell > 0) craft.worldTicksUntilNextCell--;
                if (craft.worldTicksUntilNextCell > 0) continue;

                int next = craft.worldPathCellIds[craft.worldPathIndex];
                if (WorldLookup.HasOpposingCraft(w, next, craft.owner))
                {
                    StopTravel(craft);
                    if (craft.owner == FactionId.Player)
                        WorldLookup.AddAlert(w, AlertKind.Info, "A craft stopped: its route is blocked by enemy craft.", craft.cellId);
                    WorldLookup.AutoAssignDefender(w, craft.cellId);
                    continue;
                }
                int oldCell = WorldLookup.ClearDefenderSlot(w, craft.id);
                craft.cellId = next;
                if (oldCell != -1) WorldLookup.AutoAssignDefender(w, oldCell);
                craft.worldPathIndex++;
                craft.worldTicksUntilNextCell = c.rules.worldMoveTicksPerCell;
                capture.CancelContestedHolds(w);
                if (craft.worldPathIndex >= craft.worldPathCellIds.Count)
                {
                    craft.worldPathCellIds.Clear();
                    craft.worldPathIndex = 0;
                    Arrive(w, c, map, craft, encounters);
                }
            }
        }

        void Arrive(WorldState w, ContentCatalog c, MapDefinition map, CraftState craft, EncounterService encounters)
        {
            int target = craft.worldDestinationCellId;
            craft.worldDestinationCellId = -1;
            var cell = WorldLookup.GetCell(w, craft.cellId);
            if (cell != null && cell.owner == craft.owner) WorldLookup.AutoAssignDefender(w, craft.cellId);
            if (target == -1 || target == craft.cellId)
            {
                if (craft.owner == FactionId.Ai && w.ai.raidCraftId == craft.id) ClearRaid(w);
                // Arriving in a clear cell you do not own starts claiming it automatically.
                if (target == craft.cellId && cell != null && cell.owner != craft.owner && !CombatResolver.HasTargets(w, c, cell.id, craft.owner))
                {
                    var hold = capture.StartHold(w, new GameCommand { kind = CommandKind.StartCapture, actor = craft.owner, craftId = craft.id, targetCellId = cell.id }, c.rules, c);
                    if (hold.success && craft.owner == FactionId.Player)
                        WorldLookup.AddAlert(w, AlertKind.Info, "Claiming cell " + cell.id + ": hold for " + (c.rules.normalCaptureTicks * c.rules.tickMilliseconds / 1000) + "s.", cell.id);
                }
                return;
            }

            // Attack order: decide between battle and a walk-in claim.
            var targetCell = WorldLookup.GetCell(w, target);
            if (targetCell == null || targetCell.owner == craft.owner)
            {
                if (craft.owner == FactionId.Ai) ClearRaid(w);
                return;
            }
            // Defended by craft or a Fort: a battle. Otherwise walk in and claim it.
            if (CombatResolver.HasTargets(w, c, target, craft.owner))
            {
                if (targetCell.defenderCraftId == -1) WorldLookup.AutoAssignDefender(w, target);
                encounters.Start(w, c, map, target, craft.owner);
            }
            else
            {
                // Undefended: move in and hold the control point without a battle.
                int oldCell = WorldLookup.ClearDefenderSlot(w, craft.id);
                craft.cellId = target;
                if (oldCell != -1) WorldLookup.AutoAssignDefender(w, oldCell);
                capture.StartHold(w, new GameCommand { kind = CommandKind.StartCapture, actor = craft.owner, craftId = craft.id, targetCellId = target }, c.rules, c);
                if (craft.owner == FactionId.Ai)
                    WorldLookup.AddAlert(w, AlertKind.Raid, "Enemy craft is claiming your undefended cell " + target + "! Send a craft to contest it.", target);
            }
            if (craft.owner == FactionId.Ai) ClearRaid(w);
        }

        static void StopTravel(CraftState craft)
        {
            craft.worldPathCellIds.Clear();
            craft.worldPathIndex = 0;
            craft.worldDestinationCellId = -1;
        }

        static void ClearRaid(WorldState w)
        {
            w.ai.raidCraftId = -1;
            w.ai.raidTargetCellId = -1;
            w.ai.raidWarningShown = false;
        }

        /// <summary>Alerts the player once when the AI raider is within raidWarningTicks of arrival (immediately if closer).</summary>
        public void RaiseWarningIfDue(WorldState w, RulesDefinition rules)
        {
            if (w.ai.raidCraftId == -1 || w.ai.raidWarningShown) return;
            if (WorldLookup.IsSuppressed(w, FactionId.Ai)) { ClearRaid(w); return; }
            var craft = WorldLookup.GetCraft(w, w.ai.raidCraftId);
            if (craft == null || !WorldLookup.IsCraftLive(craft)) { ClearRaid(w); return; }
            int remainingCells = craft.worldPathCellIds.Count - craft.worldPathIndex;
            int eta = remainingCells <= 0 ? 0 : (remainingCells - 1) * rules.worldMoveTicksPerCell + craft.worldTicksUntilNextCell;
            if (eta > rules.raidWarningTicks) return;
            w.ai.raidWarningShown = true;
            WorldLookup.AddAlert(w, AlertKind.Raid,
                "Incoming raid on cell " + w.ai.raidTargetCellId + " in about " + (eta * rules.tickMilliseconds / 1000) + "s. Assign a defender!",
                w.ai.raidTargetCellId);
        }
    }
}
