using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// What goods are worth to make: the power a whole chain draws per unit (machine time times each machine's draw,
    /// down to the extractors) and so the credits a minute each megawatt earns making it. Worked out once per catalog.
    /// </summary>
    public static class AiEconomics
    {
        static ContentCatalog cached;
        static readonly Dictionary<string, float> mwSeconds = new Dictionary<string, float>();

        /// <summary>Megawatt-seconds the whole chain draws to make one unit (raw goods: an extractor's share).</summary>
        public static float MwSecondsPerUnit(ContentCatalog c, string itemId)
        {
            if (cached != c) { cached = c; mwSeconds.Clear(); }
            float v;
            if (mwSeconds.TryGetValue(itemId, out v)) return v;
            mwSeconds[itemId] = 0f; // guards a loop in bad content
            RecipeDefinition recipe = null;
            foreach (var r in c.recipes) if (r.outputItemId == itemId) { recipe = r; break; }
            if (recipe == null)
            {
                // Raw: an extractor (its draw) shares a node's output among many units.
                float draw = 1f;
                foreach (var b in c.buildings) if (b.kind == BuildingKind.Extractor) draw = b.powerDraw;
                v = draw / 30f;
            }
            else
            {
                float draw = 3f;
                foreach (var b in c.buildings) if (b.allowedRecipeIds.Contains(recipe.id)) { draw = b.powerDraw; break; }
                float seconds = recipe.craftTicks * c.rules.tickMilliseconds / 1000f / System.Math.Max(1, recipe.outputQuantity);
                v = seconds * draw;
                foreach (var input in recipe.inputs) v += MwSecondsPerUnit(c, input.itemId) * input.quantity / System.Math.Max(1, recipe.outputQuantity);
            }
            mwSeconds[itemId] = v;
            return v;
        }

        /// <summary>Credits a minute one megawatt earns making and selling the good, at the faction's prices.</summary>
        public static float CreditsPerMwMinute(ContentCatalog c, FactionState f, string itemId)
        {
            var item = CatalogRepository.GetItem(c, itemId);
            float mws = MwSecondsPerUnit(c, itemId);
            if (item == null || !item.sellable || mws <= 0f) return 0f;
            return EconomyService.SalePrice(c, f, item) * 60f / mws;
        }
    }
}
