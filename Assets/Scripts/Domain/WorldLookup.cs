using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Read helpers and small shared mutations over WorldState. Every rule service uses these so that
    /// "is this structure working", "who is contesting this cell" and inventory arithmetic have one definition.
    /// </summary>
    public static class WorldLookup
    {
        public static int AllocateId(WorldState w)
        {
            return w.nextId++;
        }

        /// <summary>The other side in a two-seat match. The AI still plays one opponent; with more seats use the team helpers below.</summary>
        public static FactionId Opponent(FactionId f)
        {
            return f == FactionId.Player ? FactionId.Ai : f == FactionId.Ai ? FactionId.Player : FactionId.Neutral;
        }

        // ---------------------------------------------------------------- battles

        /// <summary>The battle in progress with this id, or null.</summary>
        public static EncounterState GetBattle(WorldState w, int id)
        {
            foreach (var e in w.encounters) if (e.active && e.id == id) return e;
            return null;
        }

        /// <summary>The battle a craft is fighting in, or null.</summary>
        public static EncounterState BattleOf(WorldState w, int craftId)
        {
            foreach (var e in w.encounters) if (e.active && e.participantCraftIds.Contains(craftId)) return e;
            return null;
        }

        /// <summary>The battle a cell is part of, or null.</summary>
        public static EncounterState BattleAt(WorldState w, int cellId)
        {
            foreach (var e in w.encounters) if (e.active && e.battleCellIds.Contains(cellId)) return e;
            return null;
        }

        /// <summary>Whether a craft is committed to a battle (it takes battle turns, not map orders).</summary>
        public static bool InBattle(WorldState w, int craftId) { return BattleOf(w, craftId) != null; }

        /// <summary>Whether a cell is the target of a battle: under siege, its production stops.</summary>
        public static bool UnderSiege(WorldState w, int cellId)
        {
            foreach (var e in w.encounters) if (e.active && e.targetCellId == cellId) return true;
            return false;
        }

        // ---------------------------------------------------------------- seats and teams

        /// <summary>Size of an array indexed by <see cref="FactionId"/>: Neutral and the four seats.</summary>
        public const int FactionSlots = (int)FactionId.Seat4 + 1;

        /// <summary>A seat's team for comparing sides: its team number, or a team of its own; -1 for Neutral.</summary>
        public static int TeamOf(WorldState w, FactionId f)
        {
            if (f == FactionId.Neutral) return -1;
            var fs = GetFaction(w, f);
            return fs != null && fs.team != 0 ? fs.team : 1000 + (int)f;
        }

        /// <summary>The same seat, or two seats on one team. Neutral is nobody's ally.</summary>
        public static bool AreAllies(WorldState w, FactionId a, FactionId b)
        {
            return a != FactionId.Neutral && b != FactionId.Neutral && TeamOf(w, a) == TeamOf(w, b);
        }

        /// <summary>Two seats on different teams. Neutral is nobody's enemy.</summary>
        public static bool AreEnemies(WorldState w, FactionId a, FactionId b)
        {
            return a != FactionId.Neutral && b != FactionId.Neutral && TeamOf(w, a) != TeamOf(w, b);
        }

        /// <summary>Whether the seat is on the team that won (false until the match is decided).</summary>
        public static bool HasWon(WorldState w, FactionId f)
        {
            return w.outcome == MatchOutcome.Decided && w.winners.Contains(f);
        }

        /// <summary>The seat's saved AI state.</summary>
        public static AiState AiStateOf(WorldState w, FactionId f)
        {
            var fs = GetFaction(w, f);
            return fs != null ? fs.ai : null;
        }

        /// <summary>
        /// How <paramref name="viewer"/> refers to <paramref name="f"/>: "you", "your ally", "the AI" for an AI enemy,
        /// else "the enemy". For alerts, which are written for their recipient.
        /// </summary>
        public static string Who(WorldState w, FactionId viewer, FactionId f)
        {
            if (f == viewer) return "you";
            if (AreAllies(w, viewer, f)) return "your ally";
            var fs = GetFaction(w, f);
            return fs != null && fs.aiControlled ? "the AI" : "the enemy";
        }

        /// <summary><see cref="Who"/> with a capital letter, to start a sentence.</summary>
        public static string WhoStart(WorldState w, FactionId viewer, FactionId f)
        {
            string s = Who(w, viewer, f);
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // ---------------------------------------------------------------- entity lookup

        public static FactionState GetFaction(WorldState w, FactionId id)
        {
            foreach (var f in w.factions) if (f.id == id) return f;
            return null;
        }

        public static CellState GetCell(WorldState w, int id)
        {
            foreach (var c in w.cells) if (c.id == id) return c;
            return null;
        }

        public static MapCellDefinition GetCellDef(MapDefinition map, int id)
        {
            foreach (var c in map.cells) if (c.id == id) return c;
            return null;
        }

        public static CraftState GetCraft(WorldState w, int id)
        {
            if (id < 0) return null;
            foreach (var c in w.crafts) if (c.id == id) return c;
            return null;
        }

        public static BuildingState GetBuilding(WorldState w, int id)
        {
            if (id < 0) return null;
            foreach (var b in w.buildings) if (b.id == id) return b;
            return null;
        }

        public static BeltSegmentState GetBelt(WorldState w, int id)
        {
            if (id < 0) return null;
            foreach (var b in w.belts) if (b.id == id) return b;
            return null;
        }

        public static ResourceNodeState GetNode(WorldState w, int id)
        {
            if (id < 0) return null;
            foreach (var n in w.nodes) if (n.id == id) return n;
            return null;
        }

        public static BuildingState GetBuildingAt(WorldState w, int cellId, int x, int y)
        {
            foreach (var b in w.buildings) if (b.cellId == cellId && b.x == x && b.y == y) return b;
            return null;
        }

        public static ResourceNodeState GetNodeAt(WorldState w, int cellId, int x, int y)
        {
            foreach (var n in w.nodes) if (n.cellId == cellId && n.x == x && n.y == y) return n;
            return null;
        }

        // ---------------------------------------------------------------- activity

        public static bool IsSuppressed(WorldState w, FactionId f)
        {
            var fs = GetFaction(w, f);
            return fs != null && fs.suppressed;
        }

        /// <summary>A building works only when its owner holds its cell, it has no grace timer and its faction is not suppressed.</summary>
        /// <summary>
        /// A building works when its owner holds its cell (no recapture grace, not withdrawn), it is not a
        /// construction site, and the cell's factory is online: a Headquarters or completed Fort is there.
        /// </summary>
        public static bool IsBuildingActive(WorldState w, BuildingState b)
        {
            if (!IsBuildingHeld(w, b) || b.underConstruction) return false;
            return b.anchor || GetCell(w, b.cellId).factoryOnline;
        }

        /// <summary>The owner holds the building's cell and the building has no recapture grace timer.</summary>
        public static bool IsBuildingHeld(WorldState w, BuildingState b)
        {
            if (b == null || b.inactiveUntilTick != -1) return false;
            var cell = GetCell(w, b.cellId);
            return cell != null && cell.owner == b.owner && !IsSuppressed(w, b.owner);
        }

        /// <summary>Can take deliveries: a working building, or a held construction site waiting for materials.</summary>
        public static bool CanReceive(WorldState w, BuildingState b)
        {
            return b != null && (b.underConstruction ? IsBuildingHeld(w, b) : IsBuildingActive(w, b));
        }

        /// <summary>An edge works only when its owner holds both endpoint cells, it has no grace timer and its faction is not suppressed.</summary>
        public static bool IsBeltActive(WorldState w, BeltSegmentState s)
        {
            if (s == null || s.inactiveUntilTick != -1) return false;
            var a = GetCell(w, s.fromCellId);
            var b = GetCell(w, s.toCellId);
            return a != null && b != null && a.owner == s.owner && b.owner == s.owner && !IsSuppressed(w, s.owner);
        }

        /// <summary>A node is rich when it outputs faster than the slowest node of the same item on this map.</summary>
        public static bool IsRichNode(WorldState w, ResourceNodeState node)
        {
            int slowest = 0;
            foreach (var n in w.nodes) if (n.itemId == node.itemId && n.rateTicks > slowest) slowest = n.rateTicks;
            return node.rateTicks < slowest;
        }

        // ---------------------------------------------------------------- inventory

        public static int CountItem(List<ItemStack> inv, string itemId)
        {
            int n = 0;
            foreach (var s in inv) if (s.itemId == itemId) n += s.quantity;
            return n;
        }

        public static int TotalItems(List<ItemStack> inv)
        {
            int n = 0;
            foreach (var s in inv) n += s.quantity;
            return n;
        }

        public static void AddItem(List<ItemStack> inv, string itemId, int qty)
        {
            if (qty <= 0) return;
            foreach (var s in inv)
            {
                if (s.itemId == itemId) { s.quantity += qty; return; }
            }
            inv.Add(new ItemStack { itemId = itemId, quantity = qty });
        }

        /// <summary>Removes qty units; returns false and leaves the inventory unchanged when there is not enough.</summary>
        public static bool RemoveItem(List<ItemStack> inv, string itemId, int qty)
        {
            if (CountItem(inv, itemId) < qty) return false;
            for (int i = inv.Count - 1; i >= 0 && qty > 0; i--)
            {
                var s = inv[i];
                if (s.itemId != itemId) continue;
                int take = s.quantity < qty ? s.quantity : qty;
                s.quantity -= take;
                qty -= take;
                if (s.quantity == 0) inv.RemoveAt(i);
            }
            return true;
        }

        // ---------------------------------------------------------------- craft and cells

        public static bool IsCraftLive(CraftState c)
        {
            return c != null && c.currentHealth > 0 && !c.withdrawn;
        }

        public static List<CraftState> CraftsInCell(WorldState w, int cellId, FactionId owner)
        {
            var list = new List<CraftState>();
            foreach (var c in w.crafts)
                if (c.cellId == cellId && c.owner == owner && IsCraftLive(c)) list.Add(c);
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }

        /// <summary>True when a live craft of an enemy of <paramref name="faction"/> (another team) is in the cell.</summary>
        public static bool HasOpposingCraft(WorldState w, int cellId, FactionId faction)
        {
            foreach (var c in w.crafts)
                if (c.cellId == cellId && c.owner != faction && IsCraftLive(c) && AreEnemies(w, faction, c.owner)) return true;
            return false;
        }

        public static bool IsAdjacent(MapDefinition map, int a, int b)
        {
            var ca = GetCellDef(map, a);
            var cb = GetCellDef(map, b);
            if (ca == null || cb == null) return false;
            int dx = ca.mapX - cb.mapX, dy = ca.mapY - cb.mapY;
            return (dx == 0 && (dy == 1 || dy == -1)) || (dy == 0 && (dx == 1 || dx == -1));
        }

        public static List<int> Neighbors(MapDefinition map, int cellId)
        {
            var list = new List<int>();
            foreach (var c in map.cells)
                if (c.id != cellId && IsAdjacent(map, cellId, c.id)) list.Add(c.id);
            list.Sort();
            return list;
        }

        /// <summary>Cell-graph distance in adjacent steps, or -1 when disconnected or absent.</summary>
        public static int CellDistance(MapDefinition map, int from, int to)
        {
            if (GetCellDef(map, from) == null || GetCellDef(map, to) == null) return -1;
            if (from == to) return 0;
            var dist = new Dictionary<int, int> { { from, 0 } };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int n in Neighbors(map, cur))
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[cur] + 1;
                    if (n == to) return dist[n];
                    queue.Enqueue(n);
                }
            }
            return -1;
        }

        /// <summary>
        /// Picks the lowest-ID live friendly craft in the cell as its defender when the owner holds the cell and none is assigned.
        /// </summary>
        public static void AutoAssignDefender(WorldState w, int cellId)
        {
            var cell = GetCell(w, cellId);
            if (cell == null || cell.owner == FactionId.Neutral) return;
            var current = GetCraft(w, cell.defenderCraftId);
            if (current != null && IsCraftLive(current) && current.cellId == cellId && current.owner == cell.owner) return;
            cell.defenderCraftId = -1;
            var candidates = CraftsInCell(w, cellId, cell.owner);
            foreach (var c in candidates)
            {
                if (c.worldPathCellIds.Count > 0) continue; // travelling craft do not stop to guard
                cell.defenderCraftId = c.id;
                return;
            }
        }

        /// <summary>Clears the defender slot of whichever cell this craft defends.</summary>
        public static int ClearDefenderSlot(WorldState w, int craftId)
        {
            foreach (var cell in w.cells)
            {
                if (cell.defenderCraftId == craftId)
                {
                    cell.defenderCraftId = -1;
                    return cell.id;
                }
            }
            return -1;
        }

        public static bool IsDefender(WorldState w, CraftState c)
        {
            var cell = GetCell(w, c.cellId);
            return cell != null && cell.defenderCraftId == c.id;
        }

        // ---------------------------------------------------------------- alerts

        /// <summary>
        /// Adds an alert for <paramref name="to"/>, worded for that seat; Neutral sends it to everyone. Alerts are for
        /// people, so one for a seat an AI plays is dropped (null).
        /// </summary>
        public static AlertState AddAlert(WorldState w, AlertKind kind, string message, int cellId, FactionId to)
        {
            var seat = to == FactionId.Neutral ? null : GetFaction(w, to);
            if (seat != null && seat.aiControlled) return null;
            var alert = new AlertState
            {
                id = AllocateId(w),
                kind = kind,
                message = message,
                cellId = cellId,
                createdTick = w.tick,
                to = to
            };
            w.alerts.Add(alert);
            // Keep the saved alert history short.
            while (w.alerts.Count > 30) w.alerts.RemoveAt(0);
            return alert;
        }

        public static CommandResult Ok(string message = "")
        {
            return new CommandResult { success = true, stateChanged = true, code = "OK", message = message };
        }

        public static CommandResult Fail(string code, string message)
        {
            return new CommandResult { success = false, stateChanged = false, code = code, message = message };
        }
    }
}
