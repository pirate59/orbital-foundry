using UnityEditor;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Meshy models dropped into Assets/Resources/Meshy/ are merged into one mesh at run time, which needs their mesh
    /// data readable in a build. This turns that on, and keeps the importer's materials (the game swaps in its own).
    /// </summary>
    public sealed class MeshyImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Meshy/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
        }
    }
}
