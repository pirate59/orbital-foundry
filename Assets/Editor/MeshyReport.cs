using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Prints what each Meshy model contains: meshes, triangles, bounds, materials and textures.</summary>
    public static class MeshyReport
    {
        [MenuItem("Orbital Foundry/Report Meshy models")]
        public static void Run()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Meshy" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var sb = new System.Text.StringBuilder("MESHY " + path + "\n");
                foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = f.sharedMesh;
                    sb.Append("  mesh ").Append(f.name).Append(" tris ").Append(m.triangles.Length / 3).Append(" verts ").Append(m.vertexCount)
                      .Append(" bounds ").Append(m.bounds.size).Append(" rot ").Append(f.transform.rotation.eulerAngles).Append(" scale ").Append(f.transform.lossyScale)
                      .Append(" colors ").Append(m.colors32.Length).Append(" uv ").Append(m.uv.Length).Append('\n');
                    foreach (var mat in f.GetComponent<MeshRenderer>().sharedMaterials)
                    {
                        if (mat == null) { sb.Append("    material null\n"); continue; }
                        sb.Append("    material ").Append(mat.name).Append(" shader ").Append(mat.shader.name);
                        foreach (var p in mat.GetTexturePropertyNames())
                        {
                            var t = mat.GetTexture(p);
                            if (t != null) sb.Append(" ").Append(p).Append("=").Append(t.name).Append(" ").Append(t.width).Append("x").Append(t.height);
                        }
                        if (mat.HasProperty("_BaseColor")) sb.Append(" base ").Append(mat.GetColor("_BaseColor"));
                        sb.Append('\n');
                    }
                }
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (a is Texture) sb.Append("  embedded texture ").Append(a.name).Append('\n');
                Debug.Log(sb.ToString());
            }
        }
    }
}
