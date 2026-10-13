namespace OrbitalFoundry
{
    /// <summary>Control-point holds: timed during normal play, one opposing reply during a tactical encounter.</summary>
    public sealed class CaptureService
    {
        public CommandResult StartHold(WorldState w, GameCommand cmd, RulesDefinition rules)
        {
            var craft = WorldLookup.GetCraft(w, cmd.craftId);
            var cell = WorldLookup.GetCell(w, cmd.targetCellId);
            if (craft == null || cell == null) return WorldLookup.Fail("MALFORMED", "Choose one of your craft and the cell to claim.");
            if (!WorldLookup.IsCraftLive(craft)) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft cannot act.");
            if (craft.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That craft is not yours.");
            if (craft.cellId != cell.id) return WorldLookup.Fail("NOT_IN_CELL", "The craft must be inside the cell to hold its control point.");
            if (craft.worldPathCellIds.Count > 0) return WorldLookup.Fail("TRAVELLING", "The craft is still travelling.");
            if (cell.owner == cmd.actor) return WorldLookup.Fail("ALREADY_OWNED", "You already own this cell.");
            if (cell.defenderCraftId != -1 || WorldLookup.HasOpposingCraft(w, cell.id, cmd.actor))
                return WorldLookup.Fail("CONTESTED", "The control point is contested: clear every defender and reserve first.");
            foreach (var h in w.captureHolds)
            {
                if (h.cellId == cell.id && h.faction == cmd.actor) return WorldLookup.Fail("DUPLICATE_HOLD", "A craft is already holding this control point.");
                if (h.craftId == craft.id) return WorldLookup.Fail("DUPLICATE_HOLD", "That craft is already holding a control point.");
            }
            bool tactical = w.mode == MatchMode.Tactical;
            w.captureHolds.Add(new CaptureHoldState
            {
                cellId = cell.id,
                craftId = craft.id,
                faction = cmd.actor,
                tactical = tactical,
                progressTicks = 0,
                startedRound = tactical ? w.encounter.round : 0
            });
            return WorldLookup.Ok(tactical ? "Holding the control point; it is claimed if still clear after the enemy reply."
                                           : "Holding the control point.");
        }

        public void TickNormalHolds(WorldState w, RulesDefinition rules, OwnershipService ownership, ContentCatalog c)
        {
            CancelContestedHolds(w);
            for (int i = w.captureHolds.Count - 1; i >= 0; i--)
            {
                if (i >= w.captureHolds.Count) continue;
                var h = w.captureHolds[i];
                if (h.tactical) continue;
                h.progressTicks++;
                if (h.progressTicks < rules.normalCaptureTicks) continue;
                w.captureHolds.RemoveAt(i);
                var before = WorldLookup.GetCell(w, h.cellId);
                var previousOwner = before != null ? before.owner : FactionId.Neutral;
                var result = ownership.TransferCell(w, c, h.cellId, h.faction);
                if (result.success && h.faction == FactionId.Player)
                    WorldLookup.AddAlert(w, AlertKind.Info, "Cell " + h.cellId + " claimed.", h.cellId);
                else if (result.success && h.faction == FactionId.Ai)
                {
                    bool tookFromPlayer = previousOwner == FactionId.Player;
                    WorldLookup.AddAlert(w, tookFromPlayer ? AlertKind.Raid : AlertKind.Info,
                        tookFromPlayer ? "The AI captured your cell " + h.cellId + "!" : "The AI expanded into cell " + h.cellId + ".", h.cellId);
                }
                if (w.mode == MatchMode.Ended) return;
            }
        }

        /// <summary>Completes tactical holds owned by the side opposite <paramref name="replier"/> once that side finishes its turn.</summary>
        public void ResolveAfterReply(WorldState w, FactionId replier, OwnershipService ownership, ContentCatalog c)
        {
            CancelContestedHolds(w);
            for (int i = w.captureHolds.Count - 1; i >= 0; i--)
            {
                if (i >= w.captureHolds.Count) continue;
                var h = w.captureHolds[i];
                if (!h.tactical || h.faction == replier) continue;
                w.captureHolds.RemoveAt(i);
                ownership.TransferCell(w, c, h.cellId, h.faction);
                if (w.mode == MatchMode.Ended) return;
            }
        }

        /// <summary>Removes holds whose craft died, left, or now shares the cell with an opposing craft.</summary>
        public void CancelContestedHolds(WorldState w)
        {
            w.captureHolds.RemoveAll(h =>
            {
                var craft = WorldLookup.GetCraft(w, h.craftId);
                var cell = WorldLookup.GetCell(w, h.cellId);
                return craft == null || cell == null || !WorldLookup.IsCraftLive(craft) || craft.cellId != h.cellId
                    || craft.worldPathCellIds.Count > 0 || cell.owner == h.faction
                    || WorldLookup.HasOpposingCraft(w, h.cellId, h.faction);
            });
        }
    }
}
