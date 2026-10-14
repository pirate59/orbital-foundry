using System;
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
        public const int SchemaVersion = 2;
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
            var v = StateValidator.ValidateSave(envelope, catalog, usedMap);
            if (!v.success) return v;
            world = envelope.world;
            return new CommandResult { success = true, stateChanged = false, code = "LOADED", message = "Saved game restored." };
        }
    }
}
