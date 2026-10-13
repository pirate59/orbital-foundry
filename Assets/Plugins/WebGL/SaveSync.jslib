// Flushes Unity's in-memory file system (persistentDataPath) to the browser's IndexedDB.
mergeInto(LibraryManager.library, {
  OrbitalFoundry_SyncFs: function () {
    FS.syncfs(false, function (err) {
      if (err) console.error("Orbital Foundry: save sync to IndexedDB failed: " + err);
    });
  }
});
