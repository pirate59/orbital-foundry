using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>
    /// The resource bar along the bottom of the screen: how much of each core resource the player's network holds,
    /// counting stock in every building the player owns plus goods on the player's belts and drones. The core
    /// resources are always listed; any other item appears once the network holds some.
    /// </summary>
    public sealed class ResourceBarController
    {
        /// <summary>Always shown, in this order: the common ores and ice, and what a Refinery makes of them.</summary>
        static readonly string[] Core = { "ore", "copper_ore", "ice", "iron_bar", "copper_bar", "water" };

        VisualElement bar;
        SpaceSpriteLibrary sprites;
        readonly Dictionary<string, (Label count, Label moving)> chips = new Dictionary<string, (Label, Label)>();
        readonly Dictionary<string, int> stored = new Dictionary<string, int>();
        readonly Dictionary<string, int> moving = new Dictionary<string, int>();
        string shown = "";

        public void Initialize(VisualElement root, SpaceSpriteLibrary spriteLibrary)
        {
            bar = root.Q<VisualElement>("resource-bar");
            sprites = spriteLibrary;
            if (bar == null) Debug.LogError("The resource bar is missing from Main.uxml.");
        }

        public void Refresh(WorldState w, ContentCatalog c)
        {
            if (bar == null || w == null || c == null) return;
            Count(w, c);

            // Core items, then anything else the network holds, in catalog order.
            var items = new List<string>();
            foreach (var id in Core) if (CatalogRepository.GetItem(c, id) != null) items.Add(id);
            foreach (var item in c.items)
            {
                if (item.id == c.rules.ionItemId || items.Contains(item.id)) continue;
                if (Get(stored, item.id) + Get(moving, item.id) > 0) items.Add(item.id);
            }
            string signature = string.Join(",", items);
            if (signature != shown)
            {
                shown = signature;
                Rebuild(c, items);
            }
            foreach (var id in items)
            {
                var chip = chips[id];
                int held = Get(stored, id), onTheWay = Get(moving, id);
                chip.count.text = (held + onTheWay).ToString("N0");
                chip.moving.text = onTheWay > 0 ? "+" + onTheWay.ToString("N0") : "";
                chip.count.EnableInClassList("resource-empty", held + onTheWay == 0);
            }
        }

        void Count(WorldState w, ContentCatalog c)
        {
            stored.Clear();
            moving.Clear();
            var mine = new HashSet<int>();
            foreach (var b in w.buildings)
            {
                if (b.owner != Perspective.Viewer) continue;
                mine.Add(b.id);
                foreach (var stack in b.inventory) Add(stored, stack.itemId, stack.quantity);
            }
            foreach (var t in w.tokens)
                if (mine.Contains(t.targetBuildingId)) Add(moving, t.itemId, t.Units);
        }

        void Rebuild(ContentCatalog c, List<string> items)
        {
            bar.Clear();
            chips.Clear();
            var title = new Label("NETWORK  (+ on belts)") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("resource-title");
            bar.Add(title);
            foreach (var id in items)
            {
                var item = CatalogRepository.GetItem(c, id);
                var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("resource-chip");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("resource-icon");
                var sprite = sprites != null ? sprites.GetItemSprite(id) : null;
                if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                else icon.style.backgroundColor = SpaceSpriteLibrary.Hex(item != null ? item.colorHex : "", Color.grey);
                chip.Add(icon);
                // One line: icon, name, count (fixed width) and goods on the way (fixed width), so nothing shifts.
                var name = new Label(item != null ? item.displayName : id) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("resource-name");
                var count = new Label("0") { pickingMode = PickingMode.Ignore };
                count.AddToClassList("resource-count");
                var move = new Label("") { pickingMode = PickingMode.Ignore };
                move.AddToClassList("resource-moving");
                chip.Add(name);
                chip.Add(count);
                chip.Add(move);
                bar.Add(chip);
                chips[id] = (count, move);
            }
        }

        static void Add(Dictionary<string, int> table, string id, int n)
        {
            int v;
            table.TryGetValue(id, out v);
            table[id] = v + n;
        }

        static int Get(Dictionary<string, int> table, string id)
        {
            int v;
            return table.TryGetValue(id, out v) ? v : 0;
        }
    }
}
