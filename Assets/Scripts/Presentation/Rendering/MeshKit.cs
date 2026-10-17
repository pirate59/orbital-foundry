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

        /// <summary>
        /// A faceted, lumpy rock: a subdivided octahedron pushed in and out by a seeded hash, flat shaded so each
        /// facet catches the light. <paramref name="squash"/> scales it vertically.
        /// </summary>
        public MeshKit Rock(Vector3 center, float radius, int seed, float roughness, Material mat, float squash = 0.8f)
        {
            var t = Tris(mat);
            var corners = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var faces = new[] { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            var tris = new List<Vector3[]>();
            for (int f = 0; f < faces.Length; f += 3) tris.Add(new[] { corners[faces[f]], corners[faces[f + 1]], corners[faces[f + 2]] });
            for (int level = 0; level < 2; level++)
            {
                var next = new List<Vector3[]>();
                foreach (var tri in tris)
                {
                    var ab = (tri[0] + tri[1]).normalized;
                    var bc = (tri[1] + tri[2]).normalized;
                    var ca = (tri[2] + tri[0]).normalized;
                    next.Add(new[] { tri[0], ab, ca });
                    next.Add(new[] { ab, tri[1], bc });
                    next.Add(new[] { ca, bc, tri[2] });
                    next.Add(new[] { ab, bc, ca });
                }
                tris = next;
            }
            System.Func<Vector3, Vector3> shape = d =>
            {
                float n = Hash(d * 3.1f, seed) * 0.6f + Hash(d * 7.3f, seed + 17) * 0.4f;
                float r = radius * (1f + (n - 0.5f) * 2f * roughness);
                return center + new Vector3(d.x * r, d.y * r * squash, d.z * r);
            };
            foreach (var tri in tris)
            {
                var a = shape(tri[0]);
                var b = shape(tri[1]);
                var c = shape(tri[2]);
                // Unity's front faces are clockwise, whose Cross(b - a, c - a) points outward; flip inward ones.
                var n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0) { var s = b; b = c; c = s; n = -n; }
                int i0 = Vert(a, n, Vector2.zero), i1 = Vert(b, n, Vector2.up), i2 = Vert(c, n, Vector2.one);
                t.Add(i0); t.Add(i1); t.Add(i2);
            }
            return this;
        }

        /// <summary>
        /// An asteroid with a levelled top: the <paramref name="width"/> x <paramref name="height"/> grid lies flat at
        /// <paramref name="top"/> plus <paramref name="relief"/>(x, z), and the rocky margin round it follows the same
        /// relief out to a ragged, rounded-square rim, then a short cliff and a jagged underside tapering to a point far below. Flat shaded.
        /// The top and cliff carry planar UVs, <paramref name="uvScale"/> texture repeats per unit, for a ground texture.
        /// </summary>
        public MeshKit Island(int width, int height, int seed, float top, System.Func<float, float, float> relief, float uvScale,
            Material topMat, Material cliffMat, Material underMat)
        {
            const int levels = 6;
            const int rings = 8; // between the grid's edge and the rim
            var rng = new System.Random(seed * 7349 + 11);
            float gw = width * 0.5f, gh = height * 0.5f;
            float hw = gw + 2.2f, hh = gh + 2.2f;
            var shift = new Vector2(seed * 0.37f, seed * 0.61f);
            System.Func<Vector3, Vector2> planar = p => new Vector2(p.x, p.z) * uvScale + shift;

            // Even angles round the rim, plus the grid's corners so the flat grid stays square.
            var angles = new List<float>();
            for (int i = 0; i < 64; i++) angles.Add(2f * Mathf.PI * i / 64);
            float corner = Mathf.Atan2(gh, gw);
            angles.Add(corner); angles.Add(Mathf.PI - corner); angles.Add(Mathf.PI + corner); angles.Add(2f * Mathf.PI - corner);
            angles.Sort();
            int n = angles.Count;

            var rim = new Vector3[n];
            var edge = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float c = Mathf.Cos(angles[i]), s = Mathf.Sin(angles[i]);
                // Superellipse: square enough to hold the grid's corners, round enough to read as rock.
                float r = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(c) / hw, 4f) + Mathf.Pow(Mathf.Abs(s) / hh, 4f), 0.25f);
                r *= 1f + (float)rng.NextDouble() * 0.09f;
                rim[i] = new Vector3(c * r, top, s * r);
                float e = Mathf.Min(Mathf.Abs(c) > 1e-5f ? gw / Mathf.Abs(c) : float.MaxValue, Mathf.Abs(s) > 1e-5f ? gh / Mathf.Abs(s) : float.MaxValue);
                edge[i] = new Vector3(c * e, top, s * e);
            }

            // The grid, flat, then rings of rising rock out to the rim.
            for (int i = 0; i < n; i++) edge[i].y = top + relief(edge[i].x, edge[i].z);
            var tt = Tris(topMat);
            var center = new Vector3(0, top + relief(0f, 0f), 0);
            for (int i = 0; i < n; i++)
            {
                var a = edge[(i + 1) % n];
                FlatTri(tt, center, a, edge[i], Vector3.up, planar(center), planar(a), planar(edge[i]));
            }
            var prev = edge;
            for (int k = 1; k <= rings; k++)
            {
                var ring = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float f = k == rings ? 1f : (k + ((float)rng.NextDouble() - 0.5f) * 0.5f) / rings;
                    var p = Vector3.Lerp(edge[i], rim[i], f);
                    p.y = top + relief(p.x, p.z);
                    ring[i] = p;
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    FlatTri(tt, prev[i], prev[j], ring[j], Vector3.up, planar(prev[i]), planar(prev[j]), planar(ring[j]));
                    FlatTri(tt, prev[i], ring[j], ring[i], Vector3.up, planar(prev[i]), planar(ring[j]), planar(ring[i]));
                }
                prev = ring;
            }
            var rimTop = prev;

            // Distance round the rim, for the cliff texture's u.
            var arc = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                var d = rimTop[(i + 1) % n] - rimTop[i];
                arc[i + 1] = arc[i] + new Vector2(d.x, d.z).magnitude;
            }

            float depth = 6.5f + (float)rng.NextDouble() * 3f;
            for (int k = 1; k <= levels; k++)
            {
                float y = top - 0.5f - depth * Mathf.Pow((k - 1f) / (levels - 1f), 1.25f);
                float scale = k == 1 ? 0.97f : 0.97f * (1f - Mathf.Pow((k - 1f) / (levels - 1f), 1.6f));
                var ring = new Vector3[n];
                var tip = new Vector3(((float)rng.NextDouble() - 0.5f) * 2f, y, ((float)rng.NextDouble() - 0.5f) * 2f);
                for (int i = 0; i < n; i++)
                {
                    if (k == levels) { ring[i] = tip; continue; }
                    float jitter = 1f + ((float)rng.NextDouble() - 0.5f) * (k == 1 ? 0.04f : 0.22f);
                    // The cliff's foot follows the rim's height, so a raised rim makes a taller cliff.
                    float ry = k == 1 ? rimTop[i].y - 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.1f : y + ((float)rng.NextDouble() - 0.5f) * 0.8f;
                    ring[i] = new Vector3(rim[i].x * scale * jitter, ry, rim[i].z * scale * jitter);
                }
                var t = Tris(k == 1 ? cliffMat : underMat);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    var mid = (prev[i] + prev[j] + ring[i] + ring[j]) * 0.25f;
                    var outward = new Vector3(mid.x, 0, mid.z);
                    if (k == 1)
                    {
                        Vector2 Uv(float u, Vector3 p) => new Vector2(u, p.y) * uvScale + shift;
                        FlatTri(t, prev[i], prev[j], ring[j], outward, Uv(arc[i], prev[i]), Uv(arc[i + 1], prev[j]), Uv(arc[i + 1], ring[j]));
                        FlatTri(t, prev[i], ring[j], ring[i], outward, Uv(arc[i], prev[i]), Uv(arc[i + 1], ring[j]), Uv(arc[i], ring[i]));
                        continue;
                    }
                    FlatTri(t, prev[i], prev[j], ring[j], outward);
                    if (k < levels) FlatTri(t, prev[i], ring[j], ring[i], outward);
                }
                prev = ring;
            }
            return this;
        }

        /// <summary>One flat-shaded triangle, wound so its face points along <paramref name="outward"/>.</summary>
        void FlatTri(List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            FlatTri(t, a, b, c, outward, Vector2.zero, Vector2.up, Vector2.one);
        }

        void FlatTri(List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            if (Vector3.Dot(n, outward) < 0) { var s = b; b = c; c = s; var su = ub; ub = uc; uc = su; n = -n; }
            int i0 = Vert(a, n, ua), i1 = Vert(b, n, ub), i2 = Vert(c, n, uc);
            t.Add(i0); t.Add(i1); t.Add(i2);
        }

        /// <summary>
        /// One flat-shaded triangle facing along <paramref name="outward"/>, its UVs projected from above at
        /// <paramref name="uvScale"/> texture repeats per unit, for ground that shares a texture with the deck.
        /// </summary>
        public MeshKit Facet(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Material mat, float uvScale, Vector2 uvShift)
        {
            System.Func<Vector3, Vector2> uv = p => new Vector2(p.x, p.z) * uvScale + uvShift;
            FlatTri(Tris(mat), a, b, c, outward, uv(a), uv(b), uv(c));
            return this;
        }

        /// <summary>A flat, upward-facing rectangle with planar UVs, <paramref name="uvScale"/> texture repeats per unit.</summary>
        public MeshKit Plane(Vector3 center, Vector2 size, Material mat, float uvScale, Vector2 uvShift)
        {
            var t = Tris(mat);
            var h = new Vector3(size.x * 0.5f, 0, size.y * 0.5f);
            var p = new[] { center + new Vector3(-h.x, 0, -h.z), center + new Vector3(-h.x, 0, h.z), center + new Vector3(h.x, 0, h.z), center + new Vector3(h.x, 0, -h.z) };
            int a = Vert(p[0], Vector3.up, new Vector2(p[0].x, p[0].z) * uvScale + uvShift);
            int b = Vert(p[1], Vector3.up, new Vector2(p[1].x, p[1].z) * uvScale + uvShift);
            int c = Vert(p[2], Vector3.up, new Vector2(p[2].x, p[2].z) * uvScale + uvShift);
            int d = Vert(p[3], Vector3.up, new Vector2(p[3].x, p[3].z) * uvScale + uvShift);
            Quad(t, a, b, c, d);
            return this;
        }

        /// <summary>Smooth value noise, 0-1, at <paramref name="p"/> (one lattice cell per unit).</summary>
        public static float Noise(Vector3 p, int seed) { return Hash(p, seed); }

        static float Hash(Vector3 p, int seed)
        {
            // Smooth-ish value noise on the unit sphere: blend hashed lattice corners.
            var i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            var f = p - i;
            f = new Vector3(f.x * f.x * (3 - 2 * f.x), f.y * f.y * (3 - 2 * f.y), f.z * f.z * (3 - 2 * f.z));
            float Corner(float x, float y, float z)
            {
                float h = Mathf.Sin((i.x + x) * 127.1f + (i.y + y) * 311.7f + (i.z + z) * 74.7f + seed * 13.13f) * 43758.5453f;
                return h - Mathf.Floor(h);
            }
            float x00 = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), f.x);
            float x10 = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), f.x);
            float x01 = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), f.x);
            float x11 = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), f.x);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, f.y), Mathf.Lerp(x01, x11, f.y), f.z);
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

        public Mesh Build(string name) { return Build(name, false); }

        /// <summary>
        /// Builds the mesh. <paramref name="outlineNormals"/> also stores, in UV channel 3, each vertex's normal averaged
        /// over every vertex at the same position, so an ink outline extruded along it stays closed at hard creases.
        /// </summary>
        public Mesh Build(string name, bool outlineNormals)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (outlineNormals)
            {
                var sums = new Dictionary<Vector3Int, Vector3>();
                var keys = new Vector3Int[verts.Count];
                for (int i = 0; i < verts.Count; i++)
                {
                    var p = verts[i] * 2000f;
                    keys[i] = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                    Vector3 sum;
                    sums.TryGetValue(keys[i], out sum);
                    sums[keys[i]] = sum + normals[i];
                }
                var smooth = new List<Vector3>(verts.Count);
                for (int i = 0; i < verts.Count; i++)
                {
                    var n = sums[keys[i]];
                    smooth.Add(n.sqrMagnitude > 1e-6f ? n.normalized : normals[i]);
                }
                mesh.SetUVs(3, smooth);
            }
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
