using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Creates the panel settings asset, the single-component Main scene, and the build scene list.</summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string PanelSettingsPath = "Assets/Resources/UI/GamePanelSettings.asset";
        public const string ThemePath = "Assets/Resources/UI/GameTheme.tss";

        [MenuItem("Tools/Orbital Foundry/Create Prototype Scene")]
        public static void CreatePrototypeScene()
        {
            if (!Run()) Fail("Prototype scene setup failed; see the Console for details.");
        }

        /// <summary>Batch-mode entry: -executeMethod OrbitalFoundry.EditorTools.ProjectSetup.CreatePrototypeSceneBatch</summary>
        public static void CreatePrototypeSceneBatch()
        {
            EditorApplication.Exit(Run() ? 0 : 1);
        }

        static bool Run()
        {
            if (typeof(GameBootstrap) == null) { Debug.LogError("The runtime assembly did not compile."); return false; }
            var active = EditorSceneManager.GetActiveScene();
            if (!Application.isBatchMode && active.isDirty && active.path != ScenePath)
            {
                Debug.LogError("Save or discard changes in the open scene before running setup.");
                return false;
            }

            ConfigurePlayer();
            if (!EnsurePanelSettings()) return false;

            Directory.CreateDirectory("Assets/Scenes");
            UnityEngine.SceneManagement.Scene scene;
            if (File.Exists(ScenePath)) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var existing = Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include);
            for (int i = 1; i < existing.Length; i++) Object.DestroyImmediate(existing[i].gameObject);
            if (existing.Length == 0) new GameObject("Orbital Foundry").AddComponent<GameBootstrap>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Could not save " + ScenePath + ".");
                return false;
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Orbital Foundry prototype scene ready at " + ScenePath + ".");
            return true;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Orbital Foundry";
            PlayerSettings.productName = "Orbital Foundry";
            PlayerSettings.defaultWebScreenWidth = 1600;
            PlayerSettings.defaultWebScreenHeight = 900;
            PlayerSettings.runInBackground = false;
        }

        static bool EnsurePanelSettings()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) { Debug.LogError("Theme " + ThemePath + " failed to import."); return false; }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            bool created = panel == null;
            if (created) panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            if (created) AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            else EditorUtility.SetDirty(panel);
            return true;
        }

        static void Fail(string message)
        {
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("Orbital Foundry", message, "OK");
        }
    }
}
