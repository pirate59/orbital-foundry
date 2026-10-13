using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>Cell transfer, grace-period expiry of captured structures, and match outcome.</summary>
    public sealed class OwnershipService
    {
        public CommandResult TransferCell(WorldState w, ContentCatalog c, int cellId, FactionId newOwner)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null) return WorldLookup.Fail("BAD_CELL", "That cell does not exist.");
            if (newOwner == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_CLAIM", "Cells cannot be returned to neutral.");
            if (cell.owner == newOwner) return WorldLookup.Fail("ALREADY_OWNED", "That cell already belongs to this faction.");

            FactionId oldOwner = cell.owner;
            cell.owner = newOwner;
            cell.lastOwnerChangeTick = w.tick;

            // Former owner's structures stop for the grace period.
            if (oldOwner != FactionId.Neutral)
            {
                int until = w.tick + c.rules.graceTicks;
                foreach (var b in w.buildings)
                    if (b.cellId == cellId && b.owner == oldOwner && b.inactiveUntilTick == -1) b.inactiveUntilTick = until;
                foreach (var s in w.belts)
                    if (s.toCellId == cellId && s.owner == oldOwner && s.inactiveUntilTick == -1) s.inactiveUntilTick = until;
            }
            // Recapture before expiry restores the new owner's own structures.
            foreach (var b in w.buildings)
                if (b.cellId == cellId && b.owner == newOwner) b.inactiveUntilTick = -1;
            foreach (var s in w.belts)
                if (s.toCellId == cellId && s.owner == newOwner) s.inactiveUntilTick = -1;

            RefreshTokenFreeze(w);

            // Defender slot follows the new owner.
            var oldDefender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            if (oldDefender == null || oldDefender.owner != newOwner) cell.defenderCraftId = -1;
            WorldLookup.AutoAssignDefender(w, cellId);

            // Any remaining holds on this cell are resolved by the transfer.
            w.captureHolds.RemoveAll(h => h.cellId == cellId);

            if (cell.headquartersFor == FactionId.Player && newOwner == FactionId.Ai)
            {
                w.mode = MatchMode.Ended;
                w.outcome = MatchOutcome.PlayerDefeat;
                w.encounter.active = false;
                WorldLookup.AddAlert(w, AlertKind.Defeat, "Your headquarters has fallen. The map is lost.", cellId);
                return WorldLookup.Ok("Player headquarters captured.");
            }
            if (cell.headquartersFor == FactionId.Ai && newOwner == FactionId.Player)
            {
                SuppressAi(w);
                WorldLookup.AddAlert(w, AlertKind.Victory, "AI headquarters captured! Its craft withdraw and its factories stop.", cellId);
            }
            EvaluateOutcome(w, c.rules);
            return WorldLookup.Ok();
        }

        static void SuppressAi(WorldState w)
        {
            var ai = WorldLookup.GetFaction(w, FactionId.Ai);
            if (ai != null) ai.suppressed = true;
            foreach (var craft in w.crafts)
            {
                if (craft.owner != FactionId.Ai) continue;
                craft.withdrawn = true;
                craft.worldPathCellIds.Clear();
                craft.worldDestinationCellId = -1;
                WorldLookup.ClearDefenderSlot(w, craft.id);
            }
            w.captureHolds.RemoveAll(h => h.faction == FactionId.Ai);
            w.pullRequests.RemoveAll(r =>
            {
                var b = WorldLookup.GetBuilding(w, r.targetBuildingId);
                return b == null || b.owner == FactionId.Ai;
            });
            w.ai.queuedCommands.Clear();
            w.ai.goal = AiGoal.None;
            w.ai.raidCraftId = -1;
            w.ai.raidTargetCellId = -1;
            w.ai.raidWarningShown = false;
            if (w.encounter.active)
            {
                w.encounter.active = false;
                w.encounter.pendingDefenderCellId = -1;
                if (w.mode == MatchMode.Tactical) w.mode = MatchMode.Factory;
                w.captureHolds.RemoveAll(h => h.tactical);
            }
        }

        /// <summary>Freezes tokens sitting on inactive edges and wakes tokens whose edge became active again.</summary>
        public static void RefreshTokenFreeze(WorldState w)
        {
            var belts = new Dictionary<int, BeltSegmentState>();
            foreach (var s in w.belts) belts[s.id] = s;
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                BeltSegmentState seg;
                if (!belts.TryGetValue(t.routeSegmentIds[t.segmentIndex], out seg)) continue;
                bool active = WorldLookup.IsBeltActive(w, seg);
                if (!active) t.status = TokenStatus.Frozen;
                else if (t.status == TokenStatus.Frozen) t.status = TokenStatus.Moving;
            }
        }

        /// <summary>Removes structures whose grace period has elapsed, plus tokens on removed edges.</summary>
        public void ExpireInactiveStructures(WorldState w, ContentCatalog c)
        {
            var removedBelts = new HashSet<int>();
            var removedBuildings = new HashSet<int>();
            foreach (var s in w.belts)
                if (s.inactiveUntilTick != -1 && w.tick >= s.inactiveUntilTick) removedBelts.Add(s.id);
            foreach (var b in w.buildings)
                if (b.inactiveUntilTick != -1 && w.tick >= b.inactiveUntilTick) removedBuildings.Add(b.id);
            if (removedBelts.Count == 0 && removedBuildings.Count == 0) return;
            RemoveStructures(w, removedBuildings, removedBelts);
        }

        /// <summary>Removes the given structures and every token or request that depends on them.</summary>
        public static void RemoveStructures(WorldState w, HashSet<int> buildingIds, HashSet<int> beltIds)
        {
            w.belts.RemoveAll(s => beltIds.Contains(s.id));
            w.buildings.RemoveAll(b => buildingIds.Contains(b.id));
            foreach (var n in w.nodes)
                if (buildingIds.Contains(n.extractorBuildingId)) n.extractorBuildingId = -1;
            w.tokens.RemoveAll(t =>
                t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count
                || beltIds.Contains(t.routeSegmentIds[t.segmentIndex])
                || buildingIds.Contains(t.targetBuildingId));
            w.pullRequests.RemoveAll(r => buildingIds.Contains(r.targetBuildingId));
        }

        public void EvaluateOutcome(WorldState w, RulesDefinition rules)
        {
            if (w.outcome == MatchOutcome.PlayerDefeat) return;
            foreach (var cell in w.cells)
            {
                if (cell.headquartersFor == FactionId.Player && cell.owner == FactionId.Ai)
                {
                    w.outcome = MatchOutcome.PlayerDefeat;
                    w.mode = MatchMode.Ended;
                    return;
                }
            }
            if (w.outcome == MatchOutcome.AiConceded) return;
            var ai = WorldLookup.GetFaction(w, FactionId.Ai);
            var player = WorldLookup.GetFaction(w, FactionId.Player);
            if (ai != null && player != null && ai.suppressed && player.cumulativeSales >= rules.playerSalesGoal)
            {
                w.outcome = MatchOutcome.AiConceded;
                if (w.mode != MatchMode.Ended) w.mode = MatchMode.Factory;
                WorldLookup.AddAlert(w, AlertKind.Victory, "Victory! The AI has formally conceded. Keep building as long as you like.", -1);
            }
        }
    }
}
