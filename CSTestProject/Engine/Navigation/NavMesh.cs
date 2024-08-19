using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Weesals;
using Weesals.Engine;
using Weesals.Utility;
using CornerId = System.UInt16;
using TriangleId = System.UInt16;

namespace Navigation {

    public class NavMesh : IDisposable {

        public const ushort InvalidTriId = Triangle.InvalidTriId;
        public const int GridCellSize = 512;
        public const int TriGridShift = 7;

        // Tri1 is Left Tri2 is Right, if Edge is going from 0,0, to 0,1 (facing forward)
        // Sign true = Left, false = Right
        // Matching Edge.GetSign(C0) == true (C0 = left of edge when tri is forward)
        public struct EdgeAdjacency : IEquatable<EdgeAdjacency> {
            public TriangleId Triangle1, Triangle2;
            public bool IsEmpty => Triangle1 == InvalidTriId && Triangle2 == InvalidTriId;
            public TriangleId GetTriangle(bool sign) { return sign ? Triangle1 : Triangle2; }
            public TriangleId GetOtherTriangle(TriangleId triId) { return Triangle1 == triId ? Triangle2 : Triangle1; }
            public void SetTriangle(bool side, TriangleId triangle) {
#if UNITY_EDITOR
                if (triangle == GetTriangle(!side)) {
                    Debug.LogError("Triangles should not match!");
                }
#endif
                if (side) Triangle1 = triangle; else Triangle2 = triangle;
            }
            public bool Equals(EdgeAdjacency o) { return Triangle1 == o.Triangle1 && Triangle2 == o.Triangle2; }
            public override int GetHashCode() { return Triangle1 * 49157 + Triangle2; }
            public static readonly EdgeAdjacency None = new EdgeAdjacency() { Triangle1 = InvalidTriId, Triangle2 = InvalidTriId, };
        }

        internal SparseArray<Coordinate> corners;
        internal SparseArray<Triangle> triangles;
        internal Dictionary<Edge, EdgeAdjacency> adjacency;

        internal TriangleGrid triangleGrid;

        public NavMesh() {
        }
        public void Allocate(Int2 size) {
            corners = new(128);
            triangles = new(128);
            adjacency = new(128);

            triangleGrid = new((size >> TriGridShift) + 1);
        }
        public void Dispose() {
            triangleGrid.Dispose();
        }

        public void Clear() {
            corners.Clear();
            triangles.Clear();
            adjacency.Clear();

            triangleGrid.Clear();
        }

        public TriangleId GetTriangleAt(Coordinate p) {
            var ro = GetReadOnly();
            var aj = GetAdjacency();
            return aj.GetTriangleAt(ro, p);
        }
        public Triangle GetTriangle(int id) {
            return triangles[id];
        }
        public CornerId GetCornerIndex(TriangleEdge e0) {
            return triangles[e0.TriangleId].GetCorner(e0.EdgeId);
        }
        public Coordinate GetCorner(int id) {
            return corners[id];
        }

        public ReadOnly GetReadOnly() {
            return new ReadOnly(this);
        }
        public ReadAdjacency GetAdjacency() {
            return new ReadAdjacency(this);
        }

        public SparseArray<Coordinate>.Enumerator GetCornerEnumerator() {
            return corners.GetEnumerator();
        }

        public struct ReadOnly {
            public readonly NavMesh NavMesh;
            internal Coordinate[] corners => NavMesh.corners.Items;
            internal SparseArray<Triangle> triangles => NavMesh.triangles;

            public ReadOnly(NavMesh mesh) {
                NavMesh = mesh;
            }

            public SparseArray<Triangle>.Enumerator GetTriangleEnumerator() {
                return triangles.GetEnumerator();
            }
            public Triangle GetTriangle(int id) {
                return triangles[id];
            }
            public Coordinate GetCorner(int id) {
                return corners[id];
            }
            public Int2 GetCentreInt2(TriangleEdge portal, int scale = 1) {
                var tri = triangles[portal.TriangleId];
                var ctr = ((Int2)corners[tri.GetCorner(portal.EdgeId)] + (Int2)corners[tri.GetCornerWrapped(portal.EdgeId + 1)]);
                return ctr * scale / 2;
            }
            public Int2 GetCentreInt2(ushort triangleId, int scale = 1) {
                var tri = triangles[triangleId];
                var ctr = ((Int2)corners[tri.C1] + (Int2)corners[tri.C2] + (Int2)corners[tri.C3]);
                return ctr * scale / 3;
            }

            public bool GetTriangleContains(TriangleId triI, Int2 p) {
                var tri = triangles[triI];
                var c1 = (Int2)corners[tri.C1];
                var c2 = (Int2)corners[tri.C2];
                var c3 = (Int2)corners[tri.C3];
                return NavUtility.TriangleContainsCW(c1, c2, c3, p);
            }
            public Int2 GetNearestPointInTriangle(TriangleId triI, Int2 from) {
                var tri = triangles[triI];
                var c0 = (Int2)corners[tri.C3];
                var nearest = c0;
                var nearestDst2 = Int2.Dot(from - nearest, from - nearest);
                int insideC = 0;
                for (int i = 0; i < 3; i++) {
                    var c0D = from - c0;
                    var dst2 = Int2.Dot(c0D, c0D);
                    if (dst2 < nearestDst2) {
                        nearest = c0;
                        nearestDst2 = dst2;
                    }
                    var c1 = (Int2)corners[tri.GetCorner(i)];
                    var edge = c1 - c0;
                    var ndp = Int2.Dot(new Int2(edge.Y, -edge.X), c0D);
                    if (ndp > 0) {
                        if (insideC == 2) return from;
                        ++insideC;
                    } else {
                        var edp = (int)Int2.Dot(edge, c0D);
                        var eL2 = (int)Int2.Dot(edge, edge);
                        if (edp > 0 && edp < eL2) {
                            var proj = c0 + FixedMath.MultiplyRatio(edge, edp, eL2);
                            var projD = from - proj;
                            dst2 = Int2.Dot(projD, projD);
                            if (dst2 < nearestDst2) {
                                nearest = proj;
                                nearestDst2 = dst2;
                            }
                        }
                    }
                    c0 = c1;
                }
                return nearest;
            }
            public Int2 GetNearestPointOnEdge(TriangleEdge edge, Int2 position) {
                var tri = triangles[edge.TriangleId];
                var c0i = tri.GetCorner(edge.EdgeId);
                var c1i = tri.GetCornerWrapped(edge.EdgeId + 1);
                var c0 = (Int2)corners[c0i];
                var c1 = (Int2)corners[c1i];
                var cD = c1 - c0;
                int num = (int)Int2.Dot(cD, position - c0);
                if (num <= 0) return c0;
                int div = (int)Int2.Dot(cD, cD);
                if (num >= div) return c1;      // Covers case of div == 0
                return NavUtility.Lerp(c0, c1, num, div);
            }
        }
        public struct ReadAdjacency {
            public readonly NavMesh NavMesh;
            internal Dictionary<Edge, EdgeAdjacency> adjacency => NavMesh.adjacency;
            internal ref TriangleGrid triangleGrid => ref NavMesh.triangleGrid;
            public ReadAdjacency(NavMesh mesh) {
                NavMesh = mesh;
            }
            public Edge GetEdge(TriangleEdge triEdge, ReadOnly readOnly) {
                var tri = readOnly.GetTriangle(triEdge.TriangleId);
                var c0 = tri.GetCorner(triEdge.EdgeId);
                var c1 = tri.GetCornerWrapped(triEdge.EdgeId + 1);
                return new Edge(c0, c1);
            }
            public TriangleEdge GetAdjacentEdge(TriangleEdge triEdge, ReadOnly readOnly) {
                var tri = readOnly.GetTriangle(triEdge.TriangleId);
                var c0 = tri.GetCorner(triEdge.EdgeId);
                var c1 = tri.GetCornerWrapped(triEdge.EdgeId + 1);
                var edge = new Edge(c0, c1);
                if (!adjacency.TryGetValue(edge, out var adjacent)) adjacent = EdgeAdjacency.None;
                var newEdge = new TriangleEdge(adjacent.GetTriangle(!edge.GetSign(c0)), InvalidTriId);
                if (newEdge.TriangleId != InvalidTriId) {
                    var adjTri = readOnly.GetTriangle(newEdge.TriangleId);
                    newEdge.EdgeId = (ushort)adjTri.FindCorner(c1);
                }
                return newEdge;
            }
            public TriangleId MoveTo(in ReadOnly readOnly, TriangleId from, Int2 p) {
                return MoveTo(readOnly, from, p, default);
            }
            public struct TriangleDirectionWalker {
                public TriangleId TriI;
                public CornerId CornerI;
                public Int2 Target;
                public bool IsEnded => TriI == InvalidTriId;
                public void Initialise(TriangleId from, Int2 target) {
                    TriI = from;
                    CornerI = InvalidTriId;
                    Target = target;
                }
                public bool Step3Way(in ReadOnly ro, in ReadAdjacency aj) {
                    var tri = ro.GetTriangle(TriI);
                    var c1 = (Int2)ro.GetCorner(tri.C1);
                    var c2 = (Int2)ro.GetCorner(tri.C2);
                    var c3 = (Int2)ro.GetCorner(tri.C3);
                    var n1 = ((c2 - c1).YX * new Int2(-1, 1));
                    var n2 = ((c3 - c2).YX * new Int2(-1, 1));
                    var n3 = ((c1 - c3).YX * new Int2(-1, 1));
                    var dp1 = Int2.Dot(n1, Target - c2);
                    var dp2 = Int2.Dot(n2, Target - c2);
                    var dp3 = Int2.Dot(n3, Target - c3);
                    if (dp1 <= 0 && dp2 <= 0 && dp3 <= 0) return false;
                    int edgeI = dp1 > dp2 && dp1 > dp3 ? 0
                        : dp2 > dp3 ? 1 : 2;
                    TriI = aj.adjacency[tri.GetEdge(edgeI)].GetOtherTriangle(TriI);
                    CornerI = tri.GetCorner(edgeI);
                    return (TriI != InvalidTriId);
                }
                public bool Step2Way(in ReadOnly ro, in ReadAdjacency aj) {
                    var tri = ro.GetTriangle(TriI);
                    var edgeI = tri.FindCorner(CornerI);
                    var c1I = tri.GetCorner(edgeI);
                    var c2I = tri.GetCornerWrapped(edgeI + 1);
                    var c3I = tri.GetCornerWrapped(edgeI + 2);
                    var c1 = (Int2)ro.GetCorner(c1I);
                    var c2 = (Int2)ro.GetCorner(c2I);
                    var c3 = (Int2)ro.GetCorner(c3I);
                    var n1 = ((c2 - c1).YX * new Int2(-1, 1));
                    var n2 = ((c3 - c2).YX * new Int2(-1, 1));
                    var dp1 = Int2.Dot(n1, Target - c2);
                    var dp2 = Int2.Dot(n2, Target - c2);
                    if (dp1 <= 0 && dp2 <= 0) return false;
                    if (dp2 > dp1) edgeI = (int)((uint)(edgeI + 1) % 3);
                    TriI = aj.adjacency[tri.GetEdge(edgeI)].GetOtherTriangle(TriI);
                    CornerI = tri.GetCorner(edgeI);
                    return (TriI != InvalidTriId);
                }
            }
            public TriangleId MoveTo(in ReadOnly readOnly, TriangleId from, Int2 p, PooledList<ushort> items) {
                bool trackTris = items.IsCreated;
                var stepper = new TriangleDirectionWalker();
                stepper.Initialise(from, p);
                if (!stepper.Step3Way(readOnly, this)) return stepper.TriI;
                int i = 0;
                for (; i < 200; ++i) {
                    if (trackTris) items.Add(stepper.TriI);
                    if (!stepper.Step2Way(readOnly, this)) return stepper.TriI;
                }
                if (i >= 200) {
                    Debug.Fail("Didnt find tri");
                    return InvalidTriId;
                }
                return stepper.TriI;
            }

            public TriangleId GetTriangleAt(in ReadOnly ro, Coordinate p) {
                //var origTriI = triangleQTree.FindTriangleAt(((Int2)p) >> TriGridShift, out var isLeafItem);
                var triI = triangleGrid.FindTriangleAt(((Int2)p) >> TriGridShift);
                if (triI != InvalidTriId) triI = MoveTo(ro, triI, p);
                if (triI == InvalidTriId) {
                    //Debug.Fail("Is this required?");
                    for (var it = ro.triangles.GetEnumerator(); it.MoveNext();) {
                        var tri = ro.triangles[it.Index];
                        var c1 = (Int2)ro.corners[tri.C1];
                        var c2 = (Int2)ro.corners[tri.C2];
                        var c3 = (Int2)ro.corners[tri.C3];
                        if (NavUtility.TriangleContainsCW(c1, c2, c3, p)) { triI = (ushort)it.Index; break; }
                    }
                }
                //triangleQTree.OverrideLeafTriangle(triI, ((Int2)p) >> TriGridShift);
                return triI;
            }
            public bool TryGetValue(Edge edge, out EdgeAdjacency adjacent) {
                return adjacency.TryGetValue(edge, out adjacent);
            }

            private void ObserveNearest(in ReadOnly ro, TriangleId triI, byte navMask, Int2 p, ref int bestDst2, ref ushort bestTri, ref PooledList<ushort> stack) {
                if ((ro.triangles[triI].Type.TypeMask & navMask) == 0) {
                    if (!stack.Contains(triI)) stack.Add(triI);
                    return;
                }
                var delta = ro.GetNearestPointInTriangle(triI, p) - p;
                var dst2 = (int)Int2.Dot(delta, delta);
                if (dst2 >= bestDst2) return;
                bestDst2 = dst2;
                bestTri = triI;
            }
            public ushort FindNearestPathable(in ReadOnly ro, byte navMask, Int2 p) {
                int minDst2 = int.MaxValue;
                TriangleId bestTri = InvalidTriId;
                using var stack = new PooledList<ushort>(8);
                /*var fromTriI = triangleGrid.FindTriangleAt(((Int2)p) >> TriGridShift);
                if ((ro.triangles[fromTriI].Type.TypeId & navMask) != 0) {
                    var delta = (ro.GetCentreInt2(fromTriI) - p);
                    minDst2 = Int2.Dot(delta, delta);
                    bestTri = fromTriI;
                }*/
                {
                    var triI = triangleGrid.FindTriangleAt(((Int2)p) >> TriGridShift);
                    if (triI == InvalidTriId) return InvalidTriId;
                    var stepper = new TriangleDirectionWalker();
                    stepper.Initialise(triI, p);
                    ObserveNearest(ro, stepper.TriI, navMask, p, ref minDst2, ref bestTri, ref stack.AsMutable());
                    if (stepper.Step3Way(ro, this)) {
                        for (int i = 0; i < 100; i++) {
                            ObserveNearest(ro, stepper.TriI, navMask, p, ref minDst2, ref bestTri, ref stack.AsMutable());
                            if (!stepper.Step2Way(ro, this)) break;
                        }
                    }
                    ObserveNearest(ro, stepper.TriI, navMask, p, ref minDst2, ref bestTri, ref stack.AsMutable());
                    if (bestTri == stepper.TriI) return bestTri;
                    stack.Add(stepper.TriI);
                }
                for (int i = stack.Count - 1; i < stack.Count; i++) {
                    var triI = stack[i];
                    for (int e = 0; e < 3; e++) {
                        var edge = new TriangleEdge(triI, (ushort)e);
                        var oedge = GetAdjacentEdge(edge, ro);
                        if (oedge.TriangleId == InvalidTriId) continue;
                        ObserveNearest(ro, oedge.TriangleId, navMask, p, ref minDst2, ref bestTri, ref stack.AsMutable());
                    }
                    if (stack.Count > 20) break;
                }
                return bestTri;

                /*var triI = triangleGrid.FindTriangleAt(((Int2)p) >> TriGridShift);
                var tri = ro.GetTriangle(triI);
                if ((tri.Type.TypeId & navMask) != 0) return triI;
                var stack = new NativeList<TriangleId>(16, Allocator.Temp);
                stack.Add(triI);
                for (int i = 0; i < stack.Length; ++i) {
                    triI = stack[i];
                    tri = ro.GetTriangle(triI);
                    for (int e = 0; e < 3; e++) {
                        var c0I = tri.GetCorner(e);
                        var c1I = tri.GetCornerWrapped(e + 1);
                        var c0 = (Int2)ro.GetCorner(c0I);
                        var c1 = (Int2)ro.GetCorner(c1I);
                        if (NavUtility.Cross(c1 - c0, p - c0) > 0) continue;
                        var otriI = adjacency[new Edge(c0I, c1I)].GetOtherTriangle(triI);
                        if (otriI == InvalidTriId) continue;
                        var otri = ro.GetTriangle(otriI);
                        if ((otri.Type.TypeId & navMask) != 0) return triI;
                        if (stack.Contains(otriI)) continue;
                        stack.Add(otriI);
                    }
                    if (stack.Length > 100) break;
                }
                return InvalidTriId;*/
            }
        }
        public ref struct PolygonIntersectEnumerator {
            private ReadOnly mesh;
            private ReadAdjacency adjacency;
            private Span<Coordinate> path;
            //private NativeParallelHashMap<Edge, EdgeAdjacency> adjacency;
            private int triangleId;
            private int pathId;
            private int edge;
            private bool wrapPath;
            private bool useNE;

            public ushort Edge => (ushort)edge;
            public ushort TriangleId => (ushort)triangleId;
            public bool IsValid => triangleId != InvalidTriId;
            public int PathIndex => pathId;
            public TriangleEdge TriangleEdge => new TriangleEdge(TriangleId, Edge);
            public PolygonIntersectEnumerator(NavMesh navMesh, Span<Coordinate> _path)
                : this(new ReadOnly(navMesh), new ReadAdjacency(navMesh), _path) { }
            public PolygonIntersectEnumerator(ReadOnly ro, ReadAdjacency aj, Span<Coordinate> _path) {
                mesh = ro;
                adjacency = aj;
                path = _path;
                triangleId = -1;
                pathId = 0;
                edge = -1;
                wrapPath = false;
                useNE = false;
            }
            public TriangleEdge GetAdjacentTriangle(TriangleId triId, int edgeI) {
                var tri = mesh.GetTriangle(triId);
                var c0 = tri.GetCorner(edgeI);
                var c1 = tri.GetCornerWrapped(edgeI + 1);
                var edge = new Edge(c0, c1);
                if (!adjacency.TryGetValue(edge, out var adjacent)) adjacent = EdgeAdjacency.None;
                var adjTriI = adjacent.GetTriangle(!edge.GetSign(c0));
                if (adjTriI == InvalidTriId) return TriangleEdge.Invalid;
                var adjTri = mesh.GetTriangle(adjTriI);
                return new TriangleEdge(adjTriI, (ushort)adjTri.FindCorner(c1));
            }
            private bool RequireTriangleId() {
                if (triangleId == -1) {
                    triangleId = adjacency.GetTriangleAt(mesh, path[0]);
                    if (triangleId == InvalidTriId) return false;
                }
                return true;
            }
            public bool PrimeSkipZeroDistance() {
                if (!RequireTriangleId()) return false;
                var p0 = (Int2)path[0];
                var p1 = (Int2)path[1];
                var pSub0 = p0 + (p0 - p1);
                for (int t = 0; t < 10; ++t) {
                    var e1 = GetPathIncrement(triangleId, pSub0, p0);
                    if (!GetPathPassesPortal(triangleId, e1, pSub0, p0)) break;
                    var nextTriId = GetAdjacentTriangle((TriangleId)triangleId, e1);
                    if (!nextTriId.IsValid) return false;
                    triangleId = nextTriId.TriangleId;
                }
                useNE = true;
                return true;
            }
            public void PrimeTriangle(TriangleId triI) {
                triangleId = triI;
                edge = -1;
                useNE = true;
            }
            // Call when the path is a closed loop - it ensures that the portals
            // visited will wrap correctly (fix for when path is exactly on an edge)
            public bool PrimeClosedLoop() {
                if (!RequireTriangleId()) return false;
                wrapPath = true;
                var p0 = path[0];
                var p1 = path[1];
                for (int t = 0; t < 10; ++t) {
                    var e1 = GetPathIncrement(triangleId, p1, p0);
                    if (!GetPathPassesPortal(triangleId, e1, p1, p0)) break;
                    var nextTriId = GetAdjacentTriangle((TriangleId)triangleId, e1);
                    if (!nextTriId.IsValid) return false;
                    triangleId = nextTriId.TriangleId;
                }
                var pN = path[path.Length - 1];
                for (int t = 0; t < 10; ++t) {
                    var e1 = GetPathIncrement(triangleId, pN, p0);
                    if (!GetPathPassesPortal(triangleId, e1, pN, p0)) break;
                    var nextTriId = GetAdjacentTriangle((TriangleId)triangleId, e1);
                    if (!nextTriId.IsValid) return false;
                    triangleId = nextTriId.TriangleId;
                }
                return true;
            }
            public unsafe bool MoveNext() {
                if (!RequireTriangleId()) return false;
                if (edge != -1) {
                    if (!FlipEdge()) return false;
                }
                if (IncrementPathToPortal()) return true;
                return false;
            }

            private unsafe bool IncrementPathToPortal() {
                if (triangleId == InvalidTriId) {
                    PathFail();
                    return false;
                }
                int len = path.Length;
                var forLen = wrapPath ? len : len - 1;
                for (; pathId < forLen; ++pathId) {
                    Int2 p0 = path[pathId];
                    Int2 p1 = path[NextWrap(pathId + 1, len)];
                    var e = GetPathIncrement(triangleId, p0, p1);
                    if (!GetPathPassesPortal(triangleId, e, p0, p1)) continue;
                    edge = e;
                    return true;
                }
                return false;
            }

            [SkipLocalsInit]
            private unsafe int GetPathIncrement(int triId, Int2 p0, Int2 p1) {
                Int2 pD = p1 - p0;
                Int2 pN = new Int2(pD.Y, -pD.X);

                var tri = mesh.GetTriangle(triId);
                var triCI = stackalloc ushort[] { tri.C1, tri.C2, tri.C3, };
                var triCorners = stackalloc Int2[4];
                for (int i = 0; i < 3; i++) triCorners[i] = mesh.GetCorner(triCI[i]) - p0;
                triCorners[3] = triCorners[0];
                int ff = -1, ffC = 0, bf = -1;
                for (int i = 0; i < 3; i++) {
                    var cD = triCorners[i + 1] - triCorners[i + 0];
                    if (Int2.Dot(cD, pN) < 0) { bf = i; continue; }
                    ++ffC;
                    if (ff == -1 && bf != -1) ff = i;
                }
                if (ff == -1) ff = 0;
                if (ffC == 1) return ff;
                var piv = NextWrap(ff + 1, 3);
                return Int2.Dot(triCorners[piv], pN) >= 0 ? ff : piv;
            }
            private unsafe bool GetPathPassesPortal(int triId, int e, Int2 p0, Int2 p1) {
                var tri = mesh.GetTriangle(triId);
                var triCorners = stackalloc ushort[] { tri.C1, tri.C2, tri.C3, tri.C1, };
                var e0 = (Int2)mesh.GetCorner(triCorners[e + 0]);
                var e1 = (Int2)mesh.GetCorner(triCorners[e + 1]);
                var eN = e1 - e0;
                eN = new Int2(-eN.Y, eN.X);
                var eDp = Int2.Dot(eN, p1 - e0);
                if (useNE) eDp--;
                if (eDp >= 0) return true;
                return false;
            }

            private bool FlipEdge() {
                var adj = GetAdjacentTriangle((TriangleId)triangleId, edge);
                triangleId = adj.TriangleId;
                if (triangleId == InvalidTriId) return false;
                edge = adj.EdgeId;
                return true;
            }

            public Coordinate GetIntersectionPoint() {
                var tri = mesh.GetTriangle((TriangleId)triangleId);
                var e0 = (Int2)mesh.GetCorner(tri.GetCorner(edge));
                var e1 = (Int2)mesh.GetCorner(tri.GetCornerWrapped(edge + 1));
                Int2 p0 = path[pathId];
                Int2 p1 = path[NextWrap(pathId + 1, path.Length)];
                var pD = p1 - p0;
                var eD = e1 - e0;
                var eN = new Int2(eD.Y, -eD.X);
                var proj = (int)Int2.Dot(eN, e0 - p0);
                var nrm = (int)Int2.Dot(pD, eN);
                var p = p0 + FixedMath.MultiplyRatio(pD, proj, nrm);
                return Coordinate.FromInt2(p);
            }
        }

        private static int Wrap(int v, int len) { return v < 0 ? v + len : v >= len ? (int)((uint)v % len) : v; }
        private static int NextWrap(int v, int len) { return v >= len ? v - len : v; }
        private static int PreWrap(int v, int l) { return v < 0 ? l + v : v; }

        private static void PathFail() {
            Debug.WriteLine("Path Fail");
        }
    }

}