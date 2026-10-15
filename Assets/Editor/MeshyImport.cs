using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Prepares Meshy models dropped into Assets/Resources/Meshy/ for the game: meshes are compressed and kept
    /// readable (the game merges them into one mesh at run time), and animation, cameras and lights are skipped.
    /// Models are imported as made; one over its triangle budget (buildings 8,000, items 1,500) gets a warning in the
    /// Console, since every copy on the board draws all of its triangles.
    /// </summary>
    public sealed class MeshyImport : AssetPostprocessor
    {
        public const int BuildingTriangles = 8000, ItemTriangles = 1500;

        /// <summary>Raise this when the import rules change, so Unity re-imports every model with them.</summary>
        public override uint GetVersion() { return 7; }

        bool IsMeshy => assetPath.Replace('\\', '/').Contains("/Resources/Meshy/");

        void OnPreprocessModel()
        {
            if (!IsMeshy) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.High;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.None;
        }

        /// <summary>Colour maps beside Meshy models: 1024 pixels is plenty for a one-tile building and keeps the download small.</summary>
        void OnPreprocessTexture()
        {
            if (!IsMeshy) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        void OnPostprocessModel(GameObject model)
        {
            if (!IsMeshy) return;
            int budget = assetPath.Replace('\\', '/').Contains("/Meshy/Buildings/") ? BuildingTriangles : ItemTriangles;
            long total = 0;
            foreach (var f in model.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null)
                    for (int s = 0; s < f.sharedMesh.subMeshCount; s++) total += f.sharedMesh.GetIndexCount(s) / 3;
            if (total > budget)
                Debug.LogWarning("Meshy model " + assetPath + " has " + total.ToString("N0") + " triangles; the budget is "
                    + budget.ToString("N0") + ". It will work, but large models make the download bigger and slow drawing when many are on screen.");
        }
    }
}
