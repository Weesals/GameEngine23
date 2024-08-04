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

        public struct Corner {
            public Vector3 Position;
            public RangeInt Edges;
            public override string ToString() { return Position.ToString(); }
            public static readonly Corner Invalid = new() { Edges = new(-10, -10), Position = new(-1f), };
        }
        public struct Polygon {
        }
        public struct Edge {
            public int Corner1;
            public int Corner2;
            public int PolygonL;
            public int PolygonR;
            public bool GetSignByCorner(int id) { return Corner2 == id; }
            public int GetPolygonBySign(bool sign) { return sign ? PolygonR : PolygonL; }
            public int GetCorner(int index) { return index == 0 ? Corner1 : Corner2; }
            public bool HasCorner(int id) { return Corner1 == id || Corner2 == id; }
            public override string ToString() { return $"<{Corner1} - {Corner2}>"; }
            public static readonly Edge Invalid = new() { Corner1 = -1, Corner2 = -1, PolygonL = -1, PolygonR = -1, };
        }

        private SparseArray<Corner> corners = new();
        private SparseArray<Polygon> polygons = new();
        private SparseArray<Edge> edges = new();

        public int CornerCount => corners.PreciseCount;

        public void Clear() {
            foreach (ref var corner in corners) corner = Corner.Invalid;
            foreach (ref var edge in edges) edge = Edge.Invalid;
            corners.Clear();
            polygons.Clear();
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
            Span<int> edges = stackalloc int[] {
                0, 1, 0, 1, // Bottom
                1, 3, 0, 2,
                3, 2, 0, 4,
                2, 0, 0, 5,
                4, 6, 3, 5, // Top
                6, 7, 3, 4,
                7, 5, 3, 2,
                5, 4, 3, 1,
                0, 4, 1, 5, // Mid
                1, 5, 2, 1,
                3, 7, 4, 2,
                2, 6, 5, 4,
            };
            for (int e = 0; e < edges.Length; e += 4) {
                var e0 = edges[e + 0];
                var e1 = edges[e + 1];
                var p0 = edges[e + 2];
                var p1 = edges[e + 3];
                InsertEdge(e0, e1, p0, p1);
            }
        }

        public bool Slice(Plane plane) {
            //Span<ulong> pointMasks = stackalloc ulong[(corners.MaximumCount + 63) >> 6];
            int cullCount = 0;
            Span<float> dpCache = stackalloc float[corners.MaximumCount];
            for (var it = corners.GetEnumerator(); it.MoveNext();) {
                var dp = Plane.DotCoordinate(plane, it.Current.Position);
                //if (dp <= 0.01f) continue;
                //pointMasks[it.Index >> 6] |= 1ul << (it.Index & 63);
                dpCache[it.Index] = dp;
                if (dp <= 0.00000000000001f) cullCount++;
            }
            if (cullCount == 0) return false;
            float minDP = float.MaxValue;
            using var insertedCorners = new PooledList<Int2>();
            //using var insertedCornerP = new PooledList<Vector3>();
            for (var it = edges.GetEnumerator(); it.MoveNext();) {
                var dp1 = dpCache[it.Current.Corner1];
                var dp2 = dpCache[it.Current.Corner2];
                var bit1 = dp1 >= 0.01f;
                var bit2 = dp2 >= 0.01f;
                if (bit1 == bit2) {
                    if (!bit1) {
                        RemoveEdge(it.Index);
                        it.RepairIterator();
                    }
                    continue;
                }
                insertedCorners.Add(new Int2(it.Index, bit1 ? 1 : 0));
            }
            for (int i = 0; i < insertedCorners.Count; i++) {
                ref var edge = ref edges[insertedCorners[i].X];
                var dp1 = dpCache[edge.Corner1];
                var dp2 = dpCache[edge.Corner2];
                var bit1 = insertedCorners[i].Y != 0;
                var corner1 = corners[edge.Corner1].Position;
                var corner2 = corners[edge.Corner2].Position;
                var dpDelta = (dp2 - dp1);
                minDP = Math.Min(minDP, Math.Abs(dpDelta));
                var intersect = Math.Abs(dpDelta) < 0.0000000001f ? edge.Corner1
                    : RequireCorner(Vector3.Lerp(corner1, corner2, (0 - dp1) / dpDelta));
                OverwriteCorner(ref (bit1 ? ref edge.Corner2 : ref edge.Corner1), intersect);
                Debug.Assert(edge.HasCorner(intersect));
            }
            var newPoly = AllocatePolygon();
            for (int c1 = 0; c1 < insertedCorners.Count; ++c1) {
                var corner1 = insertedCorners[c1];
                var edge1 = edges[corner1.X];
                var nextPoly1 = edge1.GetPolygonBySign((corner1.Y != 0));
                int c2 = (c1 + 1) % insertedCorners.Count;
                if (c2 > 0) {
                    int o = c2;
                    for (; o < insertedCorners.Count; o++) {
                        var tcorner = insertedCorners[o];
                        var tedge = edges[tcorner.X];
                        var tprevPoly = tedge.GetPolygonBySign(!(tcorner.Y != 0));
                        if (tprevPoly == nextPoly1) break;
                    }
                    Debug.Assert(o < insertedCorners.Count);
                    insertedCorners.Swap(o, c2);
                }
                var corner2 = insertedCorners[c2];
                var edge2 = edges[corner2.X];
                Debug.Assert(edge1.GetPolygonBySign((corner1.Y != 0))
                    == edge2.GetPolygonBySign(!(corner2.Y != 0)));
                InsertEdge(edge1.GetCorner(corner1.Y), edge2.GetCorner(corner2.Y), newPoly, nextPoly1);
            }
            return cullCount > 0;
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

        private void OverwriteCorner(ref int edgeCorner, int newCorner) {
            if (edgeCorner == newCorner) return;
            var oldCorner = edgeCorner;
            edgeCorner = newCorner;
            corners[newCorner].Edges.Length++;
            RemoveFromCorner(oldCorner, -1);
        }

        private static bool GetBit(Span<ulong> pages, int index) {
            return (pages[index >> 6] & (1ul << (index & 63))) != 0;
        }

        private int AllocatePolygon() {
            return polygons.Allocate();
        }
        private int InsertEdge(int corner1, int corner2, int poly1, int poly2) {
            Debug.Assert(corners.ContainsIndex(corner1));
            Debug.Assert(corners.ContainsIndex(corner2));
            corners[corner1].Edges.Length++;
            corners[corner2].Edges.Length++;
            return edges.Add(new Edge() {
                Corner1 = corner1,
                Corner2 = corner2,
                PolygonL = poly1,
                PolygonR = poly2,
            });
        }
        private int RequireCorner(Vector3 pos) {
            for (var it = corners.GetEnumerator(); it.MoveNext();) {
                if (it.Current.Position == pos) return it.Index;
            }
            return InsertCorner(pos);
        }
        private int InsertCorner(Vector3 pos) {
            var cornerId = corners.Add(new Corner() {
                Position = pos,
                Edges = default,
            });
            foreach (var edge in edges) Debug.Assert(!edge.HasCorner(cornerId));
            return cornerId;
        }

        private void RemoveEdge(int edgeId) {
            ref var edge = ref edges[edgeId];
            var edgeCopy = edge;
            edge = Edge.Invalid;
            edges.Return(edgeId);
            RemoveFromCorner(edgeCopy.Corner1, edgeId);
            RemoveFromCorner(edgeCopy.Corner2, edgeId);
        }
        private void RemoveFromCorner(int cornerId, int edgeId) {
            ref var corner = ref corners[cornerId];
            corner.Edges.Length -= 1;
            if (corner.Edges.Length == 0) {
                foreach (var edge in edges) Debug.Assert(!edge.HasCorner(cornerId));
                corner = Corner.Invalid;
                corners.Return(cornerId);
            }
        }

        public void GetCorners(Span<Vector3> outCorners) {
            int i = 0;
            foreach (var corner in corners) {
                outCorners[i++] = corner.Position;
            }
        }
        public BoundingBox GetAABB() {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (var corner in corners) {
                min = Vector3.Min(min, corner.Position);
                max = Vector3.Max(max, corner.Position);
            }
            return BoundingBox.FromMinMax(min, max);
        }

        public void DrawGizmos() {
            for (var it = corners.GetEnumerator(); it.MoveNext();) {
                Handles.Label(it.Current.Position, it.Index.ToString());
            }
            for (var it = edges.GetEnumerator(); it.MoveNext();) {
                var corner1 = corners[it.Current.Corner1];
                var corner2 = corners[it.Current.Corner2];
                Handles.DrawLine(corner1.Position, corner2.Position, new Color(255, 255, 255, 128), 2f);
                Handles.Label(Vector3.Lerp(corner1.Position, corner2.Position, 0.5f),
                    it.Index.ToString()
                );
            }
        }

        public void FromFrustum(Frustum frustum) {
            Span<Vector3> corners = stackalloc Vector3[8];
            frustum.GetCorners(corners);
            FromBox(corners);
        }
    }
}
