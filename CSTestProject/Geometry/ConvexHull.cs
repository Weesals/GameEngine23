using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Geometry {
    public class ConvexHull {

        public struct Edge {
            public int Corner1;
            public int Corner2;
            public int PolygonL;
            public int PolygonR;
            public Edge(int corner1, int corner2, int polyL, int polyR) {
                Corner1 = corner1;
                Corner2 = corner2;
                PolygonL = polyL;
                PolygonR = polyR;
            }
            public bool GetSignByCorner(int id) { return Corner2 == id; }
            public int GetPolygonBySign(bool sign) { return sign ? PolygonR : PolygonL; }
            public int GetCorner(int index) { return index == 0 ? Corner1 : Corner2; }
            public bool HasCorner(int id) { return Corner1 == id || Corner2 == id; }
            public override string ToString() { return $"<{Corner1} - {Corner2}>"; }
            public static readonly Edge Invalid = new() { Corner1 = -1, Corner2 = -1, PolygonL = -1, PolygonR = -1, };
        }

        private SparseArray<Vector3> corners = new();
        private SparseArray<Edge> edges = new();
        private int polyCounter = 0;

        public int CornerCount => corners.PreciseCount;

        public void Clear() {
            corners.Clear();
            polyCounter = 0;
            edges.Clear();
        }
        // Winding should be (0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), ...
        public void FromBox(BoundingBox box) {
            Span<Vector3> corners = stackalloc Vector3[8];
            for (int z = 0; z < 2; z++) {
                for (int y = 0; y < 2; y++) {
                    for (int x = 0; x < 2; x++) {
                        corners[x + y * 2 + z * 4] = box.Lerp(new Vector3(x, y, z));
                    }
                }
            }
            FromBox(corners);
        }
        public void FromBox(Span<Vector3> points) {
            Clear();
            Debug.Assert(points.Length == 8);
            Span<int> corners = stackalloc int[8];
            for (int i = 0; i < points.Length; i++) {
                corners[i] = InsertCorner(points[i]);
            }
            Span<int> polygons = stackalloc int[6];
            foreach (ref var poly in polygons) poly = AllocatePolygon();
            Span<Edge> boxEdges = stackalloc Edge[] {
                new(0, 1, 0, 1), // Bottom
                new(1, 3, 0, 2),
                new(3, 2, 0, 4),
                new(2, 0, 0, 5),
                new(4, 6, 3, 5), // Top
                new(6, 7, 3, 4),
                new(7, 5, 3, 2),
                new(5, 4, 3, 1),
                new(0, 4, 1, 5), // Mid
                new(1, 5, 2, 1),
                new(3, 7, 4, 2),
                new(2, 6, 5, 4),
            };
            foreach (var edge in boxEdges) {
                edges.Add(new(corners[edge.Corner1], corners[edge.Corner2], edge.PolygonL, edge.PolygonR));
            }
        }

        public bool Slice(Plane plane) {
            int cullCount = 0;
            Span<ulong> pointMasks = stackalloc ulong[(corners.MaximumCount + 63) / 64];
            Span<float> dpCache = stackalloc float[corners.MaximumCount];
            for (var it = corners.GetEnumerator(); it.MoveNext();) {
                var dp = Plane.DotCoordinate(plane, it.Current);
                dpCache[it.Index] = dp;
                if (dp >= 0.01f) continue;
                pointMasks[it.Index / 64] |= 1ul << it.Index;
                cullCount++;
            }
            if (cullCount == 0) return false;
            // Create new corners and remove orphaned edges
            using var insertedCorners = new PooledList<Int2>();
            for (var it = edges.GetEnumerator(); it.MoveNext();) {
                var keep1 = (pointMasks[it.Current.Corner1 / 64] & (1ul << it.Current.Corner1)) == 0;
                var keep2 = (pointMasks[it.Current.Corner2 / 64] & (1ul << it.Current.Corner2)) == 0;
                if (keep1 != keep2) {
                    // Need to insert a corner here
                    insertedCorners.Add(new Int2(it.Index, keep1 ? 1 : 0));
                    ref var edge = ref edges[it.Index];
                    var dp1 = dpCache[edge.Corner1];
                    var dp2 = dpCache[edge.Corner2];
                    var newCorner = InsertCorner(Vector3.Lerp(
                        corners[edge.Corner1],
                        corners[edge.Corner2],
                        (0 - dp1) / (dp2 - dp1))
                    );
                    (keep1 ? ref edge.Corner2 : ref edge.Corner1) = newCorner;
                } else if (!keep1) {
                    // Edge is entirely on wrong side, delete it
                    RemoveEdge(it.Index);
                    it.RepairIterator();
                }
            }
            // Stitch newly inserted corners
            var newPoly = AllocatePolygon();
            for (int c1 = 0; c1 < insertedCorners.Count; ++c1) {
                var corner1 = insertedCorners[c1];
                var edge1 = edges[corner1.X];
                var nextPoly1 = edge1.GetPolygonBySign((corner1.Y != 0));
                int c2 = 0;
                if (c1 < insertedCorners.Count - 1) {
                    for (c2 = c1 + 1; c2 < insertedCorners.Count; c2++) {
                        var tcorner = insertedCorners[c2];
                        var tprevPoly = edges[tcorner.X].GetPolygonBySign(!(tcorner.Y != 0));
                        if (tprevPoly == nextPoly1) break;
                    }
                    Debug.Assert(c2 < insertedCorners.Count);
                    insertedCorners.Swap(c2, c1 + 1);
                    c2 = c1 + 1;
                }
                var corner2 = insertedCorners[c2];
                var edge2 = edges[corner2.X];
                Debug.Assert(edge1.GetPolygonBySign((corner1.Y != 0))
                    == edge2.GetPolygonBySign(!(corner2.Y != 0)));
                InsertEdge(new(edge1.GetCorner(corner1.Y), edge2.GetCorner(corner2.Y), newPoly, nextPoly1));
            }
            // Delete unreferenced corners
            for (int i = 0; i < pointMasks.Length; i++) {
                for (var bits = pointMasks[i];  bits != 0; bits &= bits - 1) {
                    RemoveCorner(i * 64 + BitOperations.TrailingZeroCount(bits));
                }
            }
            foreach (var edge in edges) {
                Debug.Assert(corners.ContainsIndex(edge.Corner1));
                Debug.Assert(corners.ContainsIndex(edge.Corner2));
                var poly0 = edge.PolygonL;
                int edgeMask = 0;
                foreach (var edge2 in edges) {
                    if (edge2.PolygonL == poly0 || edge2.PolygonR == poly0) {
                        edgeMask ^= edge2.Corner1;
                        edgeMask ^= edge2.Corner2;
                    }
                }
                Debug.Assert(edgeMask == 0);
            }
            return true;
        }
        public bool Slice(BoundingBox bounds) {
            bool change = false;
            change |= Slice(new Plane(new Vector3(+1f, 0f, 0f), -bounds.Min.X));
            change |= Slice(new Plane(new Vector3(-1f, 0f, 0f), +bounds.Max.X));
            change |= Slice(new Plane(new Vector3(0f, +1f, 0f), -bounds.Min.Y));
            change |= Slice(new Plane(new Vector3(0f, -1f, 0f), +bounds.Max.Y));
            change |= Slice(new Plane(new Vector3(0f, 0f, +1f), -bounds.Min.Z));
            change |= Slice(new Plane(new Vector3(0f, 0f, -1f), +bounds.Max.Z));
            return change;
        }
        public bool Slice(Frustum frustum) {
            Span<Plane> planes = stackalloc Plane[6];
            frustum.GetPlanes(planes);
            bool change = false;
            for (int i = 0; i < planes.Length; i++) {
                change |= Slice(planes[i]);
            }
            return change;
        }

        private int AllocatePolygon() {
            return polyCounter++;
        }
        private int InsertEdge(Edge edge) {
            Debug.Assert(corners.ContainsIndex(edge.Corner1));
            Debug.Assert(corners.ContainsIndex(edge.Corner2));
            return edges.Add(edge);
        }
        private int InsertCorner(Vector3 pos) {
            return corners.Add(pos);
        }

        private void RemoveEdge(int edgeId) {
            edges.Return(edgeId);
        }
        private void RemoveCorner(int cornerId) {
            corners.Return(cornerId);
        }

        public int GetCorners(Span<Vector3> outCorners) {
            int i = 0;
            foreach (var corner in corners) {
                outCorners[i++] = corner;
            }
            return i;
        }
        public BoundingBox GetAABB() {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (var corner in corners) {
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }
            return BoundingBox.FromMinMax(min, max);
        }

        public void DrawGizmos() {
            for (var it = corners.GetEnumerator(); it.MoveNext();) {
                Handles.Label(it.Current, it.Index.ToString());
            }
            for (var it = edges.GetEnumerator(); it.MoveNext();) {
                var corner1 = corners[it.Current.Corner1];
                var corner2 = corners[it.Current.Corner2];
                Handles.DrawLine(corner1, corner2, new Color(255, 255, 255, 128), 2f);
                Handles.Label(Vector3.Lerp(corner1, corner2, 0.5f), it.Index.ToString());
            }
        }

        public void FromFrustum(Frustum frustum) {
            Span<Vector3> corners = stackalloc Vector3[8];
            frustum.GetCorners(corners);
            FromBox(corners);
        }
    }
}
