using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Prepares Meshy models dropped into Assets/Resources/Meshy/ for the game: meshes are compressed and kept
    /// readable (the game merges them into one mesh at run time), and animation, cameras and lights are skipped.
    /// Models are imported as made; one over its triangle budget (buildings 8,000, the headquarters 25,000, ships 3,000,
    /// items 1,500) gets a warning in the
    /// Console, since every copy on the board draws all of its triangles.
    /// </summary>
    public sealed class MeshyImport : AssetPostprocessor
    {
        public const int BuildingTriangles = 8000, ItemTriangles = 1500;
        /// <summary>A ship is drawn once per ship in its fleet (ten for a Mobile Guard), so each must stay light.</summary>
        public const int CraftTriangles = 3000;
        /// <summary>The headquarters is one big centrepiece (3x3 tiles, two on the map), so it may carry far more detail.</summary>
        public const int HeadquartersTriangles = 25000;

        bool IsHeadquarters => assetPath.Replace('\\', '/').Contains("/Meshy/Buildings/headquarters");

        /// <summary>Raise this when the import rules change, so Unity re-imports every model with them.</summary>
        public override uint GetVersion() { return 9; }

        bool IsCraft => assetPath.Replace('\\', '/').Contains("/Meshy/Crafts/");

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
            // 1024 is plenty for a one-tile building; the 3x3 headquarters keeps 2048 so it stays sharp up close, and a
            // ship (a fraction of a tile up to a little over one) needs only 512.
            importer.maxTextureSize = IsHeadquarters ? 2048 : IsCraft ? 512 : 1024;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        void OnPostprocessModel(GameObject model)
        {
            if (!IsMeshy) return;
            int budget = IsHeadquarters ? HeadquartersTriangles : IsCraft ? CraftTriangles
                : assetPath.Replace('\\', '/').Contains("/Meshy/Buildings/") ? BuildingTriangles : ItemTriangles;
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
