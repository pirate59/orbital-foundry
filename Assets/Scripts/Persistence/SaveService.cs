using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// One local save slot. Writes slot1.tmp, rotates the previous good file to slot1.bak, then moves the new file into
    /// place. Loads validate before acceptance and fall back to the backup. Incompatible saves are never deleted.
    /// On the Web build, baseDirectory is Application.persistentDataPath, which Unity maps to IndexedDB.
    /// </summary>
    public sealed class SaveService
    {
        // 3: storages list what they store (they used to list what they refuse).
        // 4: seats: each faction keeps its own AI state, AI profile and trade ledger; a decided match lists its winners.
        public const int SchemaVersion = 4;
        public const string SlotFile = "slot1.json";
        public const string BackupFile = "slot1.bak";
        public const string TempFile = "slot1.tmp";

        readonly string baseDirectory;

        /// <summary>
        /// Set when an existing save could not be loaded (corrupt or newer version). Saving is refused until the player
        /// explicitly chooses to overwrite it, so an incompatible save is never silently destroyed.
        /// </summary>
        public bool ProtectExisting { get; private set; }

        public void AllowOverwrite() { ProtectExisting = false; }

        public SaveService(string baseDirectory)
        {
            this.baseDirectory = baseDirectory ?? "";
        }

        public string SlotPath { get { return Path.Combine(baseDirectory, SlotFile); } }
        public string BackupPath { get { return Path.Combine(baseDirectory, BackupFile); } }
        string TempPath { get { return Path.Combine(baseDirectory, TempFile); } }

        public bool HasSave()
        {
            try { return File.Exists(SlotPath) || File.Exists(BackupPath); }
            catch (Exception) { return false; }
        }

        public CommandResult Save(WorldState world, ContentCatalog catalog)
        {
            if (world == null || catalog == null) return WorldLookup.Fail("SAVE_FAILED", "Nothing to save.");
            if (ProtectExisting) return WorldLookup.Fail("SAVE_BLOCKED", "Saving is paused to protect an unreadable save. Start a New Game to overwrite it.");
            try
            {
                var envelope = new SaveEnvelope
                {
                    schemaVersion = SchemaVersion,
                    catalogVersion = catalog.version,
                    savedUtcTicks = DateTime.UtcNow.Ticks,
                    world = world
                };
                string json = JsonUtility.ToJson(envelope);
                if (!Directory.Exists(baseDirectory)) Directory.CreateDirectory(baseDirectory);
                File.WriteAllText(TempPath, json);
                if (File.Exists(SlotPath))
                {
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    File.Move(SlotPath, BackupPath);
                }
                File.Move(TempPath, SlotPath);
                FlushBrowserStorage();
                return new CommandResult { success = true, stateChanged = false, code = "SAVED", message = "Game saved." };
            }
            catch (Exception e)
            {
                return WorldLookup.Fail("SAVE_FAILED", "Could not save: " + e.Message);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void OrbitalFoundry_SyncFs();
#endif

        /// <summary>On the Web build, persists the in-memory file system to IndexedDB; elsewhere a no-op.</summary>
        static void FlushBrowserStorage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            OrbitalFoundry_SyncFs();
#endif
        }

        /// <summary>The map the last successful load was validated against: the save's own map when it names one.</summary>
        public MapDefinition LoadedMap { get; private set; }

        /// <summary>
        /// Loads the save. When the save names a different map than <paramref name="map"/> (generated maps carry their
        /// seed in their ID), that map is rebuilt and used instead; see <see cref="LoadedMap"/>.
        /// </summary>
        public CommandResult TryLoad(ContentCatalog catalog, MapDefinition map, out WorldState world)
        {
            world = null;
            WorldState primary;
            MapDefinition primaryMap, backupMap;
            var r = TryLoadFile(SlotPath, catalog, map, out primary, out primaryMap);
            if (r.success) { world = primary; LoadedMap = primaryMap; return r; }
            WorldState backup;
            var rb = TryLoadFile(BackupPath, catalog, map, out backup, out backupMap);
            if (rb.success)
            {
                world = backup;
                LoadedMap = backupMap;
                return new CommandResult { success = true, stateChanged = false, code = "RECOVERED_BACKUP", message = "The latest save was unreadable (" + r.message + "); restored the previous save." };
            }
            if (r.code == "NO_SAVE" && rb.code == "NO_SAVE") return r;
            ProtectExisting = true;
            return r.code != "NO_SAVE" ? r : rb;
        }

        /// <summary>Craft types since replaced, and the type each now becomes in older saves.</summary>
        static readonly Dictionary<string, string> RetiredCraft = new Dictionary<string, string>
        {
            { "tank", "fortress" }, { "artillery", "bomber" }, { "strike", "bomber" }
        };

        /// <summary>
        /// Saves from before the three-ship line-up name craft that no longer exist: each becomes its successor, and
        /// shipyard orders for them likewise. Every craft's health is held within its type's current maximum, since
        /// that can drop between versions (the Mobile Guard went from 11 HP to 10).
        /// </summary>
        /// <summary>
        /// Saves from before schema 3 kept, for each storage, the goods it refused (everything else was collected).
        /// Storages now keep the goods they collect, so each becomes every good it did not refuse, as it was.
        /// </summary>
        public static void UpgradeStorageLists(WorldState w, ContentCatalog catalog)
        {
            if (w.buildings == null) return;
            foreach (var b in w.buildings)
            {
                var def = b != null ? CatalogRepository.GetBuilding(catalog, b.typeId) : null;
                if (def == null || def.kind != BuildingKind.Storage) continue;
                if (b.storedItemIds == null) b.storedItemIds = new List<string>();
                b.storedItemIds.Clear();
                foreach (var item in catalog.items)
                    if (item.id != catalog.rules.ionItemId && (b.blockedItemIds == null || !b.blockedItemIds.Contains(item.id))) b.storedItemIds.Add(item.id);
                if (b.blockedItemIds != null) b.blockedItemIds.Clear();
            }
        }

        /// <summary>
        /// Saves from before schema 4 had two sides: the player, and one AI whose state, profile and the player's trade
        /// ledger lived on the world. Each now belongs to its seat, the AI's seat is marked AI-played, and the old
        /// outcomes become a decided match with its winners.
        /// </summary>
        public static void UpgradeSeats(WorldState w)
        {
            if (w.factions == null) return;
            foreach (var f in w.factions)
            {
                if (f == null) continue;
                if (f.ai == null) f.ai = new AiState();
                if (f.trade == null) f.trade = new TradeLedger();
                if (f.aiProfileId == null) f.aiProfileId = "";
                if (f.id == FactionId.Ai)
                {
                    f.aiControlled = true;
                    if (w.ai != null) f.ai = w.ai;
                    f.aiProfileId = w.aiProfileId ?? "";
                }
                if (f.id == FactionId.Player && w.trade != null) f.trade = w.trade;
            }
            w.ai = new AiState();
            w.trade = new TradeLedger();
            w.aiProfileId = "";
            if (w.winners == null) w.winners = new List<FactionId>();
            if (w.outcome == MatchOutcome.PlayerDefeat) { w.outcome = MatchOutcome.Decided; w.winners.Clear(); w.winners.Add(FactionId.Ai); }
            else if (w.outcome == MatchOutcome.AiConceded) { w.outcome = MatchOutcome.Decided; w.winners.Clear(); w.winners.Add(FactionId.Player); }
            if (w.alerts != null) foreach (var a in w.alerts) if (a != null) a.to = FactionId.Player; // the old alerts were all the player's
            if (w.encounter != null && w.encounter.sides == null) w.encounter.sides = new List<FactionId>();
            if (w.encounter != null && w.encounter.active && w.encounter.sides.Count == 0)
            {
                w.encounter.sides.Add(FactionId.Player);
                w.encounter.sides.Add(FactionId.Ai);
            }
        }

        public static void UpgradeRetiredCraft(WorldState w, ContentCatalog catalog)
        {
            string next;
            if (w.crafts != null)
                foreach (var craft in w.crafts)
                {
                    if (craft == null) continue;
                    if (CatalogRepository.GetCraft(catalog, craft.typeId) == null && RetiredCraft.TryGetValue(craft.typeId, out next)
                        && CatalogRepository.GetCraft(catalog, next) != null)
                        craft.typeId = next;
                    var def = CatalogRepository.GetCraft(catalog, craft.typeId);
                    if (def != null) craft.currentHealth = Math.Min(craft.currentHealth, def.maxHealth);
                }
            if (w.buildings != null)
                foreach (var b in w.buildings)
                    if (b != null && !string.IsNullOrEmpty(b.orderedCraftTypeId) && CatalogRepository.GetCraft(catalog, b.orderedCraftTypeId) == null
                        && RetiredCraft.TryGetValue(b.orderedCraftTypeId, out next))
                        b.orderedCraftTypeId = next;
        }

        static CommandResult TryLoadFile(string path, ContentCatalog catalog, MapDefinition map, out WorldState world, out MapDefinition usedMap)
        {
            world = null;
            usedMap = map;
            string json;
            try
            {
                if (!File.Exists(path)) return WorldLookup.Fail("NO_SAVE", "No saved game was found.");
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return WorldLookup.Fail("LOAD_FAILED", "Could not read the save: " + e.Message);
            }
            SaveEnvelope envelope;
            try { envelope = JsonUtility.FromJson<SaveEnvelope>(json); }
            catch (Exception e) { return WorldLookup.Fail("CORRUPT_SAVE", "The save file is damaged: " + e.Message); }
            if (envelope == null) return WorldLookup.Fail("CORRUPT_SAVE", "The save file is empty.");
            if (envelope.world != null && (map == null || envelope.world.mapId != map.id) && MapGenerator.IsGenerated(envelope.world.mapId))
            {
                try { usedMap = CatalogRepository.LoadMap(envelope.world.mapId, catalog); }
                catch (CatalogException e) { return WorldLookup.Fail("MAP_CHANGED", "The save's map could not be rebuilt: " + e.Message); }
            }
            if (envelope.world != null) UpgradeRetiredCraft(envelope.world, catalog);
            if (envelope.world != null && envelope.schemaVersion < 3) UpgradeStorageLists(envelope.world, catalog);
            if (envelope.world != null && envelope.schemaVersion < 4) UpgradeSeats(envelope.world);
            var v = StateValidator.ValidateSave(envelope, catalog, usedMap);
            if (!v.success) return v;
            world = envelope.world;
            return new CommandResult { success = true, stateChanged = false, code = "LOADED", message = "Saved game restored." };
        }
    }
}
