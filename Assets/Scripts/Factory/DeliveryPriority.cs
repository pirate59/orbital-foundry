namespace OrbitalFoundry
{
    /// <summary>
    /// A receiving building's delivery priority: Very low, Low, Normal, High or Very high. It is purely local: it only
    /// decides between buildings that want the same goods from the same sources at the same time (requests are served
    /// highest priority first, so it never matters where nobody competes). It does not carry downstream: a High
    /// Storage hands goods on to the machines it feeds, which compete among themselves on their own priorities. A
    /// machine's first batch is still served before any priority, so a Very high Storage can take a machine's top-ups
    /// but never stall it. Everything starts at Normal, which keeps the default order.
    /// </summary>
    public static class DeliveryPriority
    {
        public const int Min = -2, Max = 2;
        public static readonly string[] Names = { "very_low", "low", "normal", "high", "very_high" };
        public static readonly string[] Labels = { "Very low", "Low", "Normal", "High", "Very high" };

        public static string Label(int priority)
        {
            return Labels[Clamp(priority) - Min];
        }

        public static bool TryParse(string name, out int priority)
        {
            for (int i = 0; i < Names.Length; i++)
                if (Names[i] == name) { priority = i + Min; return true; }
            priority = 0;
            return false;
        }

        public static int Clamp(int priority)
        {
            return priority < Min ? Min : priority > Max ? Max : priority;
        }

        /// <summary>Buildings that take deliveries and so can be given a priority.</summary>
        public static bool Applies(BuildingDefinition def)
        {
            if (def == null) return false;
            switch (def.kind)
            {
                case BuildingKind.Refinery:
                case BuildingKind.Assembler:
                case BuildingKind.SaleDepot:
                case BuildingKind.Shipyard:
                case BuildingKind.Storage:
                case BuildingKind.ConstructionZone:
                    return true;
                default:
                    return false;
            }
        }

        public static CommandResult Set(WorldState w, ContentCatalog c, GameCommand cmd)
        {
            var b = WorldLookup.GetBuilding(w, cmd.buildingId);
            if (b == null || b.owner != cmd.actor) return WorldLookup.Fail("BAD_BUILDING", "Select one of your buildings.");
            if (!Applies(CatalogRepository.GetBuilding(c, b.typeId))) return WorldLookup.Fail("NO_PRIORITY", "This building takes no deliveries.");
            int level;
            if (!TryParse(cmd.contentId, out level)) return WorldLookup.Fail("MALFORMED", "Unknown priority.");
            b.priority = level;
            return WorldLookup.Ok("Delivery priority: " + Label(level) + ".");
        }
    }
}
