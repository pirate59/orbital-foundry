using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Builds low-poly models in code from a handful of primitives suited to 1950s streamline shapes: lathed profiles
    /// (domes, rocket bodies, saucers), boxes, tori, and extruded fins. Parts share vertices per material submesh, and a
    /// transform stack places them, so a whole model becomes one mesh with one renderer.
    /// </summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Material> materials = new List<Material>();
        readonly List<List<int>> submeshes = new List<List<int>>();
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        Matrix4x4 m = Matrix4x4.identity;

        // ---------------------------------------------------------------- transform stack

        /// <summary>Applies a local translation, rotation and scale to every part added until the matching <see cref="Pop"/>.</summary>
        public MeshKit Push(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            stack.Push(m);
            m = m * Matrix4x4.TRS(position, rotation, scale);
            return this;
        }

        public MeshKit Push(Vector3 position) { return Push(position, Quaternion.identity, Vector3.one); }
        public MeshKit Push(Vector3 position, Quaternion rotation) { return Push(position, rotation, Vector3.one); }

        public MeshKit Pop()
        {
            m = stack.Pop();
            return this;
        }

        List<int> Tris(Material mat)
        {
            int i = materials.IndexOf(mat);
            if (i >= 0) return submeshes[i];
            materials.Add(mat);
            var list = new List<int>();
            submeshes.Add(list);
            return list;
        }

        int Vert(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(m.MultiplyPoint3x4(p));
            normals.Add(m.MultiplyVector(n).normalized);
            uvs.Add(uv);
            return verts.Count - 1;
        }

        void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        // ---------------------------------------------------------------- primitives

        /// <summary>An axis-aligned box centred on <paramref name="center"/> with flat faces.</summary>
        public MeshKit Box(Vector3 center, Vector3 size, Material mat)
        {
            var t = Tris(mat);
            var h = size * 0.5f;
            Face(t, center, Vector3.right, Vector3.up, Vector3.forward, h.x, h.y, h.z);
            Face(t, center, Vector3.left, Vector3.up, Vector3.back, h.x, h.y, h.z);
            Face(t, center, Vector3.up, Vector3.forward, Vector3.right, h.y, h.z, h.x);
            Face(t, center, Vector3.down, Vector3.forward, Vector3.left, h.y, h.z, h.x);
            Face(t, center, Vector3.forward, Vector3.up, Vector3.left, h.z, h.y, h.x);
            Face(t, center, Vector3.back, Vector3.up, Vector3.right, h.z, h.y, h.x);
            return this;
        }

        void Face(List<int> t, Vector3 c, Vector3 n, Vector3 up, Vector3 side, float dn, float du, float ds)
        {
            var o = c + n * dn;
            int a = Vert(o - up * du - side * ds, n, new Vector2(0, 0));
            int b = Vert(o + up * du - side * ds, n, new Vector2(0, 1));
            int cc = Vert(o + up * du + side * ds, n, new Vector2(1, 1));
            int d = Vert(o - up * du + side * ds, n, new Vector2(1, 0));
            Quad(t, a, b, cc, d);
        }

        /// <summary>
        /// Revolves a profile of (radius, height) points around the Y axis. Each profile segment gets its own ring of
        /// vertices, so creases between segments stay crisp while the surface is smooth around its circumference.
        /// Points with radius 0 close the shape at the poles.
        /// </summary>
        public MeshKit Lathe(Vector2[] profile, int segments, Material mat, float startDeg = 0f, float sweepDeg = 360f)
        {
            var t = Tris(mat);
            bool full = Mathf.Approximately(sweepDeg, 360f);
            int cols = full ? segments : segments + 1;
            for (int s = 0; s < profile.Length - 1; s++)
            {
                var p0 = profile[s];
                var p1 = profile[s + 1];
                var edge = p1 - p0;
                if (edge.sqrMagnitude < 1e-8f) continue;
                // Outward normal in the profile plane: perpendicular to the edge, pointing away from the axis.
                var n2 = new Vector2(edge.y, -edge.x).normalized;
                int baseIndex = verts.Count;
                for (int i = 0; i < cols; i++)
                {
                    float a = (startDeg + sweepDeg * i / segments) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    var n = new Vector3(n2.x * cos, n2.y, n2.x * sin);
                    Vert(new Vector3(p0.x * cos, p0.y, p0.x * sin), n, new Vector2((float)i / segments, (float)s / profile.Length));
                    Vert(new Vector3(p1.x * cos, p1.y, p1.x * sin), n, new Vector2((float)i / segments, (float)(s + 1) / profile.Length));
                }
                for (int i = 0; i < segments; i++)
                {
                    int i0 = baseIndex + i * 2;
                    int i1 = baseIndex + ((i + 1) % cols) * 2;
                    // Winding so the face points outward when the profile runs bottom to top.
                    Quad(t, i0, i0 + 1, i1 + 1, i1);
                }
            }
            return this;
        }

        /// <summary>A closed cylinder standing on <paramref name="baseCenter"/>.</summary>
        public MeshKit Cylinder(Vector3 baseCenter, float radius, float height, int segments, Material mat)
        {
            Push(baseCenter);
            Lathe(new[] { new Vector2(0, 0), new Vector2(radius, 0), new Vector2(radius, height), new Vector2(0, height) }, segments, mat);
            return Pop();
        }

        /// <summary>A cone or truncated cone standing on <paramref name="baseCenter"/>.</summary>
        public MeshKit Cone(Vector3 baseCenter, float bottomRadius, float topRadius, float height, int segments, Material mat)
        {
            Push(baseCenter);
            Lathe(new[] { new Vector2(0, 0), new Vector2(bottomRadius, 0), new Vector2(topRadius, height), new Vector2(0, height) }, segments, mat);
            return Pop();
        }

        /// <summary>A UV sphere (or a dome when <paramref name="upperOnly"/>) centred on <paramref name="center"/>.</summary>
        public MeshKit Sphere(Vector3 center, float radius, int segments, Material mat, bool upperOnly = false)
        {
            int rings = Mathf.Max(3, segments / 2);
            int start = upperOnly ? rings / 2 : 0;
            var profile = new List<Vector2>();
            if (upperOnly) profile.Add(Vector2.zero);
            for (int i = start; i <= rings; i++)
            {
                float a = -Mathf.PI / 2 + Mathf.PI * i / rings;
                profile.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            Push(center);
            SmoothLathe(profile.ToArray(), segments, mat);
            return Pop();
        }

        /// <summary>Like <see cref="Lathe"/> but with normals averaged along the profile, for round shapes.</summary>
        public MeshKit SmoothLathe(Vector2[] profile, int segments, Material mat)
        {
            var t = Tris(mat);
            int baseIndex = verts.Count;
            int count = profile.Length;
            for (int s = 0; s < count; s++)
            {
                var prev = profile[Mathf.Max(0, s - 1)];
                var next = profile[Mathf.Min(count - 1, s + 1)];
                var edge = next - prev;
                var n2 = new Vector2(edge.y, -edge.x).normalized;
                for (int i = 0; i <= segments; i++)
                {
                    float a = 2 * Mathf.PI * i / segments;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    Vert(new Vector3(profile[s].x * cos, profile[s].y, profile[s].x * sin), new Vector3(n2.x * cos, n2.y, n2.x * sin), new Vector2((float)i / segments, (float)s / (count - 1)));
                }
            }
            int stride = segments + 1;
            for (int s = 0; s < count - 1; s++)
                for (int i = 0; i < segments; i++)
                {
                    int a = baseIndex + s * stride + i;
                    int b = a + stride;
                    Quad(t, a, b, b + 1, a + 1);
                }
            return this;
        }

        /// <summary>A torus lying flat (in XZ) around <paramref name="center"/>.</summary>
        public MeshKit Torus(Vector3 center, float majorRadius, float minorRadius, int segments, int sides, Material mat)
        {
            var t = Tris(mat);
            int baseIndex = verts.Count;
            for (int i = 0; i <= segments; i++)
            {
                float u = 2 * Mathf.PI * i / segments;
                var dir = new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u));
                for (int j = 0; j <= sides; j++)
                {
                    float v = 2 * Mathf.PI * j / sides;
                    var n = dir * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                    Vert(center + dir * majorRadius + n * minorRadius, n, new Vector2((float)i / segments, (float)j / sides));
                }
            }
            int stride = sides + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = baseIndex + i * stride + j;
                    int b = a + stride;
                    Quad(t, a, a + 1, b + 1, b);
                }
            return this;
        }

        /// <summary>
        /// Extrudes a convex outline drawn in the XY plane by <paramref name="thickness"/> along Z (centred), e.g. a
        /// tail fin. Rotate it into place with <see cref="Push(Vector3, Quaternion)"/>.
        /// </summary>
        public MeshKit Fin(Vector2[] outline, float thickness, Material mat)
        {
            var t = Tris(mat);
            float hz = thickness * 0.5f;
            int n = outline.Length;
            float signed = 0;
            for (int i = 0; i < n; i++) { var a = outline[i]; var b = outline[(i + 1) % n]; signed += a.x * b.y - b.x * a.y; }
            bool ccw = signed > 0;
            // Front and back faces as fans, wound clockwise as seen from outside.
            int front = verts.Count;
            foreach (var p in outline) Vert(new Vector3(p.x, p.y, -hz), Vector3.back, p);
            for (int i = 1; i < n - 1; i++)
            {
                t.Add(front);
                t.Add(front + (ccw ? i + 1 : i));
                t.Add(front + (ccw ? i : i + 1));
            }
            int back = verts.Count;
            foreach (var p in outline) Vert(new Vector3(p.x, p.y, hz), Vector3.forward, p);
            for (int i = 1; i < n - 1; i++)
            {
                t.Add(back);
                t.Add(back + (ccw ? i : i + 1));
                t.Add(back + (ccw ? i + 1 : i));
            }
            // Rim.
            for (int i = 0; i < n; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % n];
                var e = b - a;
                var n2 = (ccw ? new Vector2(e.y, -e.x) : new Vector2(-e.y, e.x)).normalized;
                var nn = new Vector3(n2.x, n2.y, 0);
                int i0 = Vert(new Vector3(a.x, a.y, -hz), nn, Vector2.zero);
                int i1 = Vert(new Vector3(b.x, b.y, -hz), nn, Vector2.right);
                int i2 = Vert(new Vector3(b.x, b.y, hz), nn, Vector2.one);
                int i3 = Vert(new Vector3(a.x, a.y, hz), nn, Vector2.up);
                if (ccw) Quad(t, i0, i1, i2, i3);
                else Quad(t, i0, i3, i2, i1);
            }
            return this;
        }

        /// <summary>A straight strut (a thin box) from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public MeshKit Strut(Vector3 a, Vector3 b, float width, Material mat)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return this;
            Push((a + b) * 0.5f, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up));
            Box(Vector3.zero, new Vector3(width, width, d.magnitude), mat);
            return Pop();
        }

        /// <summary>Repeats <paramref name="part"/> around the Y axis <paramref name="count"/> times.</summary>
        public MeshKit Radial(int count, float startDeg, System.Action<MeshKit> part)
        {
            for (int i = 0; i < count; i++)
            {
                Push(Vector3.zero, Quaternion.Euler(0, startDeg + 360f * i / count, 0));
                part(this);
                Pop();
            }
            return this;
        }

        // ---------------------------------------------------------------- output

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        public Material[] Materials => materials.ToArray();

        /// <summary>Makes a GameObject holding the built mesh under <paramref name="parent"/>.</summary>
        public GameObject Instantiate(string name, Transform parent, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Build(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }
    }
}
