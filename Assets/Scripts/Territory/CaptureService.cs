namespace OrbitalFoundry
{
    /// <summary>Control-point holds: timed outside battle, one opposing reply in a battle over the cell.</summary>
    public sealed class CaptureService
    {
        public CommandResult StartHold(WorldState w, GameCommand cmd, RulesDefinition rules, ContentCatalog catalog = null)
        {
            var craft = WorldLookup.GetCraft(w, cmd.craftId);
            var cell = WorldLookup.GetCell(w, cmd.targetCellId);
            if (craft == null || cell == null) return WorldLookup.Fail("MALFORMED", "Choose one of your craft and the cell to claim.");
            if (!WorldLookup.IsCraftLive(craft)) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft cannot act.");
            if (craft.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That craft is not yours.");
            if (craft.cellId != cell.id) return WorldLookup.Fail("NOT_IN_CELL", "The craft must be inside the cell to hold its control point.");
            if (craft.worldPathCellIds.Count > 0) return WorldLookup.Fail("TRAVELLING", "The craft is still travelling.");
            if (cell.owner == cmd.actor) return WorldLookup.Fail("ALREADY_OWNED", "You already own this cell.");
            if (WorldLookup.AreAllies(w, cmd.actor, cell.owner)) return WorldLookup.Fail("ALLIED_CELL", "That cell belongs to an ally.");
            if (cell.defenderCraftId != -1 || WorldLookup.HasOpposingCraft(w, cell.id, cmd.actor))
                return WorldLookup.Fail("CONTESTED", "The control point is contested: clear every defender and reserve first.");
            if (catalog != null && w.buildings.Exists(b => b.cellId == cell.id && WorldLookup.AreEnemies(w, cmd.actor, b.owner) && CombatResolver.IsAttackable(w, catalog, b)))
                return WorldLookup.Fail("CONTESTED", "A Fort holds this cell: destroy it before claiming the control point.");
            foreach (var h in w.captureHolds)
            {
                if (h.cellId == cell.id && h.faction == cmd.actor) return WorldLookup.Fail("DUPLICATE_HOLD", "A craft is already holding this control point.");
                if (h.craftId == craft.id) return WorldLookup.Fail("DUPLICATE_HOLD", "That craft is already holding a control point.");
            }
            var battle = WorldLookup.BattleAt(w, cell.id);
            bool tactical = battle != null;
            w.captureHolds.Add(new CaptureHoldState
            {
                cellId = cell.id,
                craftId = craft.id,
                faction = cmd.actor,
                tactical = tactical,
                progressTicks = 0,
                startedRound = tactical ? battle.round : 0,
                battleId = tactical ? battle.id : -1
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
                if (result.success)
                {
                    WorldLookup.AddAlert(w, AlertKind.Info, "Cell " + h.cellId + " claimed.", h.cellId, h.faction);
                    // Everyone else hears of it: the seat that lost the cell loudly, the rest in passing.
                    foreach (var f in w.factions)
                    {
                        if (f.id == h.faction) continue;
                        bool theirs = f.id == previousOwner;
                        WorldLookup.AddAlert(w, theirs ? AlertKind.Raid : AlertKind.Info,
                            WorldLookup.WhoStart(w, f.id, h.faction) + (theirs ? " captured your cell " + h.cellId + "!"
                                : previousOwner == FactionId.Neutral ? " expanded into cell " + h.cellId + "." : " took cell " + h.cellId + "."), h.cellId, f.id);
                    }
                }
                if (w.mode == MatchMode.Ended) return;
            }
        }

        /// <summary>
        /// Completes a battle's holds by <paramref name="replier"/>'s enemies once it finishes its turn in that battle
        /// without contesting them.
        /// </summary>
        public void ResolveAfterReply(WorldState w, EncounterState battle, FactionId replier, OwnershipService ownership, ContentCatalog c)
        {
            CancelContestedHolds(w);
            for (int i = w.captureHolds.Count - 1; i >= 0; i--)
            {
                if (i >= w.captureHolds.Count) continue;
                var h = w.captureHolds[i];
                if (!h.tactical || h.battleId != battle.id || !WorldLookup.AreEnemies(w, h.faction, replier)) continue;
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
