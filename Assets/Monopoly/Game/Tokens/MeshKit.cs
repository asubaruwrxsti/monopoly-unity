using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>Small procedural mesh toolkit used to build the classic metal tokens.</summary>
    public static class MeshKit
    {
        /// <summary>
        /// Revolves a profile around the Y axis. Profile points are (radius, height), ordered from the
        /// bottom of the shape to the top.
        /// </summary>
        public static Mesh Lathe(IList<Vector2> profile, int segments = 40)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int rings = profile.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                foreach (var p in profile) verts.Add(new Vector3(p.x * cos, p.y, p.x * sin));
            }
            for (int s = 0; s < segments; s++)
            for (int r = 0; r < rings - 1; r++)
            {
                int a = s * rings + r, b = (s + 1) * rings + r;
                tris.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            return Finish("Lathe", verts, tris, smooth: true);
        }

        /// <summary>
        /// Extrudes a 2D outline (in the XY plane, any winding, may be concave) along Z, centred on Z = 0.
        /// </summary>
        public static Mesh Extrude(IList<Vector2> outline, float depth)
        {
            var poly = new List<Vector2>(outline);
            if (SignedArea(poly) < 0) poly.Reverse(); // work counter-clockwise
            var cap = Triangulate(poly);
            float hz = depth / 2f;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Front (z = -hz, facing -Z) and back (z = +hz, facing +Z) caps.
            int front = verts.Count;
            foreach (var p in poly) verts.Add(new Vector3(p.x, p.y, -hz));
            for (int i = 0; i < cap.Count; i += 3) tris.AddRange(new[] { front + cap[i], front + cap[i + 2], front + cap[i + 1] });
            int back = verts.Count;
            foreach (var p in poly) verts.Add(new Vector3(p.x, p.y, hz));
            for (int i = 0; i < cap.Count; i += 3) tris.AddRange(new[] { back + cap[i], back + cap[i + 1], back + cap[i + 2] });

            // Side walls with their own vertices for crisp edges.
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                int v = verts.Count;
                verts.Add(new Vector3(a.x, a.y, -hz));
                verts.Add(new Vector3(b.x, b.y, -hz));
                verts.Add(new Vector3(b.x, b.y, hz));
                verts.Add(new Vector3(a.x, a.y, hz));
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
            return Finish("Extrude", verts, tris, smooth: false);
        }

        /// <summary>An open-topped tray: a box whose top is wider than its bottom.</summary>
        public static Mesh Tray(Vector2 bottom, Vector2 top, float height, float wall = 0.02f)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int v = verts.Count;
                verts.AddRange(new[] { a, b, c, d });
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 }); // double-sided
            }
            Vector3 B(float x, float z) => new Vector3(x * bottom.x / 2f, 0, z * bottom.y / 2f);
            Vector3 T(float x, float z) => new Vector3(x * top.x / 2f, height, z * top.y / 2f);
            Quad(B(-1, -1), B(-1, 1), B(1, 1), B(1, -1));
            Quad(B(-1, -1), B(1, -1), T(1, -1), T(-1, -1));
            Quad(B(1, 1), B(-1, 1), T(-1, 1), T(1, 1));
            Quad(B(-1, 1), B(-1, -1), T(-1, -1), T(-1, 1));
            Quad(B(1, -1), B(1, 1), T(1, 1), T(1, -1));
            return Finish("Tray", verts, tris, smooth: false);
        }

        private static Mesh Finish(string name, List<Vector3> verts, List<int> tris, bool smooth)
        {
            var mesh = new Mesh { name = name };
            if (!smooth)
            {
                // Unweld so every triangle gets a flat normal.
                var flat = new List<Vector3>(tris.Count);
                var flatTris = new List<int>(tris.Count);
                foreach (int t in tris)
                {
                    flatTris.Add(flat.Count);
                    flat.Add(verts[t]);
                }
                verts = flat;
                tris = flatTris;
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float SignedArea(IList<Vector2> poly)
        {
            float area = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area / 2f;
        }

        /// <summary>Ear-clipping triangulation of a simple counter-clockwise polygon.</summary>
        private static List<int> Triangulate(IList<Vector2> poly)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (Cross(b - a, c - b) <= 0) continue; // reflex corner
                    bool contains = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (InTriangle(poly[j], a, b, c)) { contains = true; break; }
                    }
                    if (contains) continue;
                    result.AddRange(new[] { ia, ib, ic });
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate input; fall through to a fan
            }
            for (int i = 1; i + 1 < idx.Count; i++) result.AddRange(new[] { idx[0], idx[i], idx[i + 1] });
            return result;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            => Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;
    }
}
