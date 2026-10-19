using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry
{
    /// <summary>Optional, state-driven first factory guide. It observes production; it never changes game rules.</summary>
    public sealed class IntroGuide
    {
        readonly VisualElement card;
        readonly Label heading, instruction;
        readonly Button toggle, hide;
        bool active, visible;
        bool ironExtractor, ironBar, copperExtractor, copperBar, relay, soldRelay;
        string saveKey;
        int step;
        float nextCheck;

        public IntroGuide(VisualElement root)
        {
            card = root.Q<VisualElement>("intro-guide");
            heading = root.Q<Label>("intro-heading");
            instruction = root.Q<Label>("intro-instruction");
            toggle = root.Q<Button>("intro-toggle");
            hide = root.Q<Button>("intro-hide");
            toggle.clicked += () => { if (active) { visible = !visible; Save(); Render(); } };
            hide.clicked += () => { visible = false; Save(); Render(); };
            Stop();
        }

        public void StartNewGame(string mapId)
        {
            saveKey = "intro_relay_" + mapId;
            active = visible = true;
            ironExtractor = ironBar = copperExtractor = copperBar = relay = soldRelay = false;
            step = 0;
            nextCheck = 0f;
            Save();
            Render();
        }

        public void Resume(string mapId)
        {
            saveKey = "intro_relay_" + mapId;
            if (!PlayerPrefs.HasKey(saveKey)) { Stop(); return; }
            int flags = PlayerPrefs.GetInt(saveKey);
            active = true;
            visible = PlayerPrefs.GetInt(saveKey + "_visible", 1) != 0;
            ironExtractor = (flags & 1) != 0;
            ironBar = (flags & 2) != 0;
            copperExtractor = (flags & 4) != 0;
            copperBar = (flags & 8) != 0;
            relay = (flags & 16) != 0;
            soldRelay = (flags & 32) != 0;
            step = 0;
            while (step < 6 && Done(step)) step++;
            nextCheck = 0f;
            Render();
        }

        public void Stop()
        {
            active = visible = false;
            Render();
        }

        public void SuppressForTestGame()
        {
            if (!string.IsNullOrEmpty(saveKey)) PlayerPrefs.DeleteKey(saveKey);
            Stop();
        }

        public void Tick(GameLoop loop)
        {
            if (!active || loop.World == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.3f;
            var world = loop.World;
            int oldFlags = Flags();
            foreach (var building in world.buildings)
            {
                if (building.owner != Perspective.Viewer || building.nodeId < 0 || building.typeId != "extractor") continue;
                var node = WorldLookup.GetNode(world, building.nodeId);
                if (node == null) continue;
                if (node.itemId == "ore") ironExtractor = true;
                if (node.itemId == "copper_ore") copperExtractor = true;
            }
            var stats = loop.Services.Factory.Stats;
            var catalog = loop.Catalog;
            ironBar |= stats.PerMinute(catalog, Perspective.Viewer, ProductionStats.Kind.Made, "iron_bar") > 0f;
            copperBar |= stats.PerMinute(catalog, Perspective.Viewer, ProductionStats.Kind.Made, "copper_bar") > 0f;
            relay |= stats.PerMinute(catalog, Perspective.Viewer, ProductionStats.Kind.Made, "relay") > 0f;
            soldRelay |= stats.PerMinute(catalog, Perspective.Viewer, ProductionStats.Kind.Sold, "relay") > 0f;
            int before = step;
            while (step < 6 && Done(step)) step++;
            if (Flags() != oldFlags) Save();
            if (step != before || visible) Render(world);
        }

        int Flags()
        {
            return (ironExtractor ? 1 : 0) | (ironBar ? 2 : 0) | (copperExtractor ? 4 : 0)
                | (copperBar ? 8 : 0) | (relay ? 16 : 0) | (soldRelay ? 32 : 0);
        }

        void Save()
        {
            if (string.IsNullOrEmpty(saveKey)) return;
            PlayerPrefs.SetInt(saveKey, Flags());
            PlayerPrefs.SetInt(saveKey + "_visible", visible ? 1 : 0);
            PlayerPrefs.Save();
        }

        bool Done(int index)
        {
            switch (index)
            {
                case 0: return ironExtractor;
                case 1: return ironBar;
                case 2: return copperExtractor;
                case 3: return copperBar;
                case 4: return relay;
                case 5: return soldRelay;
                default: return false;
            }
        }

        static bool HasRecipe(WorldState world, string typeId, string recipeId)
        {
            if (world == null) return false;
            foreach (var building in world.buildings)
                if (building.owner == Perspective.Viewer && building.typeId == typeId && building.selectedRecipeId == recipeId)
                    return true;
            return false;
        }

        static bool HasDepot(WorldState world)
        {
            if (world == null) return false;
            foreach (var building in world.buildings)
                if (building.owner == Perspective.Viewer && building.typeId == "sale_depot") return true;
            return false;
        }

        void Render(WorldState world = null)
        {
            toggle.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            card.style.display = active && visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active) return;
            toggle.text = step == 6 ? "Guide ✓" : "Guide " + (step + 1) + "/6";
            if (!visible) return;
            if (step == 6)
            {
                heading.text = "FIRST FACTORY COMPLETE";
                instruction.text = "Your Signal Relay was sold. Keep the line running, or open Research at your HQ to grow it.";
                return;
            }
            heading.text = "SIGNAL RELAY  ·  STEP " + (step + 1) + " OF 6";
            switch (step)
            {
                case 0:
                    instruction.text = "Press 1, then click an Iron Ore outcrop on your HQ asteroid to place an Extractor.";
                    break;
                case 1:
                    instruction.text = HasRecipe(world, "refinery", "refine_ore")
                        ? "Press N (Auto belt), click the Iron Extractor, then the Iron Bar Refinery. Wait for its first bar."
                        : "Press 2, place a Refinery near the Iron Extractor, then choose Iron Bar in its recipe Settings.";
                    break;
                case 2:
                    instruction.text = "Press 1 and place a second Extractor on a Copper Ore outcrop on the same asteroid.";
                    break;
                case 3:
                    instruction.text = HasRecipe(world, "refinery", "refine_copper")
                        ? "Press N and connect the Copper Extractor to the Copper Bar Refinery. Wait for its first bar."
                        : "Press 2, place another Refinery, and choose Copper Bar in its recipe Settings.";
                    break;
                case 4:
                    instruction.text = HasRecipe(world, "assembler", "build_relay")
                        ? "Press N twice: connect each Bar Refinery to the Signal Relay Assembler. It needs one of each bar."
                        : "Press 3, place an Assembler, and choose Signal Relay. It needs one Iron Bar and one Copper Bar.";
                    break;
                case 5:
                    instruction.text = HasDepot(world)
                        ? "Press N to connect the Assembler to the Sale Depot. In Depot Settings, allow Signal Relays; wait for a sale."
                        : "Press 4 to place a Sale Depot near the Assembler. It sells delivered Signal Relays for credits.";
                    break;
            }
        }
    }
}
