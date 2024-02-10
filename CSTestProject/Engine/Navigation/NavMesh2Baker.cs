using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Linq;


using CornerId = System.UInt16;
using TriangleId = System.UInt16;
using EdgeAdjacency = Navigation.NavMesh.EdgeAdjacency;
using Weesals.Engine.Profiling;
using Weesals.Utility;
using Weesals.Engine;
using System.Diagnostics;
using System.Numerics;

// Store adjacency with key = [min-corner-index << 16 | max-corner-index]
// Adajacency should store pair of triangle ids; left+right
// Makes it accesible without requiring a triangle id (just with pair of points)

namespace Navigation {
    public class NavMesh2Baker : IDisposable {

        public const ushort InvalidTriId = NavMesh.InvalidTriId;
        public const int GridCellSize = NavMesh.GridCellSize;
        public const int TriGridShift = NavMesh.TriGridShift;

        private static readonly ProfilerMarker requireTriPointMarker = new ProfilerMarker("RequireTriPoint");
        private static readonly ProfilerMarker pinEdgeMarker = new ProfilerMarker("Pin Edge");
        private static readonly ProfilerMarker swapEdgeMarker = new ProfilerMarker("Swap Edge");
        private static readonly ProfilerMarker setTriTypeMarker = new ProfilerMarker("Set Type");
        private static readonly ProfilerMarker getTriangleAtMarker = new ProfilerMarker("GetTriangleAt");
        private static readonly ProfilerMarker triangulatePolyMarker = new ProfilerMarker("Triangulate Polygon");

        public NavMesh NavMesh;

        internal PooledList<ushort> triIdByVert;
        public MultiHashMap<int, int> vertIdByHash;
        internal HashSet<Edge> pinnedEdges;

        internal SparseArray<Coordinate> corners => NavMesh.corners;
        internal SparseArray<Triangle> triangles => NavMesh.triangles;
        internal Dictionary<Edge, EdgeAdjacency> adjacency => NavMesh.adjacency;
        internal ref TriangleGrid triangleGrid => ref NavMesh.triangleGrid;

        public bool IsCreated => vertIdByHash.IsCreated;

        public NavMesh2Baker(NavMesh navMesh) {
            NavMesh = navMesh;
        }

        public void Allocate() {
            triIdByVert = new(128);
            vertIdByHash = new(128);
            pinnedEdges = new(128);
        }

        public void Dispose() {
            //pinnedEdges.Dispose();
            vertIdByHash.Dispose();
            triIdByVert.Dispose();
        }

        public void Clear() {
            NavMesh.Clear();
            triIdByVert.Clear();
            vertIdByHash.Clear();
            pinnedEdges.Clear();
        }

        public CornerId RequireVertexId(Coordinate vert) {
            var mutator = new VertexMutator(this);
            return mutator.RequireVertexId(vert);
        }
        public void RemoveVertex(int vertId) {
            var vert = corners[vertId];
            var vHash = vert.GetHashCode();
            vertIdByHash.Remove(vHash, vertId);
            corners.Return(vertId);
        }


        public void InsertRectangle(RectI rect, TriangleType type) {
            Span<CornerId> corners = stackalloc CornerId[4];
            corners[0] = RequireVertexId(new Coordinate((ushort)rect.Min.X, (ushort)rect.Min.Y));
            corners[1] = RequireVertexId(new Coordinate((ushort)rect.Min.X, (ushort)rect.Max.Y));
            corners[2] = RequireVertexId(new Coordinate((ushort)rect.Max.X, (ushort)rect.Max.Y));
            corners[3] = RequireVertexId(new Coordinate((ushort)rect.Max.X, (ushort)rect.Min.Y));
            var mutator = new Mutator(this);
            mutator.InsertPolygon(corners, type, false);
        }

        public struct VertexMutator {
            public readonly NavMesh2Baker NavBaker;
            internal SparseArray<Coordinate> corners => NavBaker.corners;
            internal MultiHashMap<int, int> vertIdByHash => NavBaker.vertIdByHash;
            internal ref PooledList<ushort> triIdByVert => ref NavBaker.triIdByVert;
            public VertexMutator(NavMesh2Baker baker) {
                NavBaker = baker;
            }
            public CornerId RequireVertexId(Coordinate vert) {
                var hash = vert.GetHashCode();
                foreach (var index in vertIdByHash.GetValuesForKey(hash)) {
                    if (corners[index].Equals(vert)) return (CornerId)index;
                }
                var vertId = corners.Allocate();
                if (vertId >= triIdByVert.Count) triIdByVert.Add(InvalidTriId);
                corners[vertId] = vert;
                triIdByVert[vertId] = InvalidTriId;
                vertIdByHash.Add(hash, vertId);
                return (CornerId)vertId;
            }
        }
        public struct Mutator {
            public NavMesh2Baker NavBaker;
            internal SparseArray<Coordinate> corners => NavBaker.corners;
            internal SparseArray<Triangle> triangles => NavBaker.triangles;
            internal Dictionary<Edge, EdgeAdjacency> adjacency => NavBaker.adjacency;
            internal HashSet<Edge> pinnedEdges => NavBaker.pinnedEdges;
            internal ref TriangleGrid triangleGrid => ref NavBaker.triangleGrid;
            public Mutator(NavMesh2Baker baker) {
                NavBaker = baker;
            }

            public VertexMutator CreateVertexMutator() { return new VertexMutator(NavBaker); }
            public NavMesh.ReadOnly CreateReadOnly() { return new NavMesh.ReadOnly(NavBaker.NavMesh); }
            public NavMesh.ReadAdjacency CreateAdjacency() { return new NavMesh.ReadAdjacency(NavBaker.NavMesh); }

            private bool GetTriangleContains(Triangle tri, Int2 p) {
                var c1 = (Int2)corners[tri.C1];
                var c2 = (Int2)corners[tri.C2];
                var c3 = (Int2)corners[tri.C3];
                return NavUtility.TriangleContainsCW(c1, c2, c3, p);
            }
            public TriangleId GetTriangleAt(Coordinate p) {
                using var marker = getTriangleAtMarker.Auto();
                var ro = CreateReadOnly();
                var aj = CreateAdjacency();
#if UNITY_EDITOR
                if (!triangles.ContainsIndex(0)) Debug.LogError("Triangle 0 is invalid");
#endif
                return aj.GetTriangleAt(ro, p);
                /*var origTriI = triangleQTree.FindTriangleAt(((Int2)p) >> TriGridShift, out var isLeafItem);
                if (origTriI == InvalidTriId) origTriI = 0;
                var triI = aj.MoveTo(ro, origTriI, p);
                if (!isLeafItem) {
                    triangleQTree.InsertTriangle(triI, ((Int2)p) >> TriGridShift);
                }
                /*var otriI = ro.GetTriangleAt(p);
                if (triI != otriI) {
                    var tri = triangles[triI];
                    var otri = triangles[otriI];
                    int sharedCorners = 0;
                    for (int i = 0; i < 3; i++) {
                        if (otri.HasCorner(tri.GetCorner(i))) ++sharedCorners;
                    }
                    if (sharedCorners == 0) {
                        int a = 0;
                        Debug.LogError("Triangles do not share corner");
                        return InvalidTriId;
                    }
                }
                return triI;*/
            }
            public unsafe void InsertPolygon(Span<CornerId> cornerIds, TriangleType type, bool pinEdges) {
                if (pinEdges) {
                    for (int i = 0; i < cornerIds.Length; i++) {
                        var corner0 = cornerIds[i];
                        var corner1 = cornerIds[(i + 1) % cornerIds.Length];
                        pinnedEdges.Add(new Edge(corner0, corner1));
                    }
                }
                TriangulatePolygon(cornerIds, type);
            }

            private int RequireTriPoint(ushort cornerI) {
                using var marker = requireTriPointMarker.Auto();
                var p = corners[cornerI];
                var triI = GetTriangleAt(p);
                if (triI == InvalidTriId) return InvalidTriId;
                var tri = triangles[triI];
                if (tri.HasCorner(cornerI)) return triI;
#if UNITY_EDITOR
                if (!GetTriangleContains(tri, p)) {
                    Debug.LogError("Triangle does not contain point!");
                }
#endif
                for (int i = 0; i < 3; i++) {
                    var edge = tri.GetEdge(i);
                    var c1 = (Int2)corners[edge.Corner1];
                    var c2 = (Int2)corners[edge.Corner2];
                    var cN = (c2 - c1).YX * new Int2(1, -1);
                    if (Int2.Dot(p - c1, cN) == 0) {
                        PokeEdge(edge, cornerI);
                        //ValidateAllTriangles();
                        return triI;
                    }
                }
                PokeTriangle(triI, cornerI);
                //ValidateAllTriangles();
                return triI;
            }
            public unsafe bool PinEdge(CornerId corner1, CornerId corner2) {
                using var marker = pinEdgeMarker.Auto();
                var edge = new Edge(corner1, corner2);
                pinnedEdges.Add(edge);
                // Does this edge already exist?
                if (adjacency.ContainsKey(edge)) return true;

                // Edge does not exist. We need to cut triangles
                if (RequireTriPoint(corner1) == InvalidTriId) return false;
                if (RequireTriPoint(corner2) == InvalidTriId) return false;

                // Does this edge already exist?
                if (adjacency.ContainsKey(edge)) return true;

                Span<Coordinate> path = stackalloc Coordinate[2];
                path[0] = corners[corner1];
                path[1] = corners[corner2];

                var it = new NavMesh.PolygonIntersectEnumerator(CreateReadOnly(), CreateAdjacency(), path);
                //var startTriI = GetCornersTriangle(corner1, (Int2)path[1] - path[0]);
                //var endTriI = GetCornersTriangle(corner2, (Int2)path[0] - path[1]);
                //it.PrimeTriangle(startTriI);
                if (!it.PrimeSkipZeroDistance()) return false;

                {
                    using var edgeMarker = swapEdgeMarker.Auto();
                    using var toRemTris = new PooledList<TriangleId>(8);
                    using var edges0 = new PooledList<CornerId>(8);
                    using var edges1 = new PooledList<CornerId>(8);
                    edges0.Add(corner1);
                    edges1.Add(corner1);
                    TriangleType type = default;
                    //toRemTris.Add(it.TriangleEdge.TriangleId);
                    for (var i = 0; i < 100 && it.MoveNext(); ++i) {
                        toRemTris.Add(it.TriangleEdge.TriangleId);
                        var triEdge = it.TriangleEdge;
                        if (triEdge.EdgeId == InvalidTriId) continue;
                        var tri = triangles[triEdge.TriangleId];
                        type = tri.Type;
                        var c0 = tri.GetCorner(triEdge.EdgeId);
                        var c1 = tri.GetCornerWrapped(triEdge.EdgeId + 1);
                        Debug.Assert(!pinnedEdges.Contains(new Edge(c0, c1)));
                        if (edges0[^1] != c0) edges0.Add(c0);
                        if (edges1[^1] != c1) edges1.Add(c1);
                    }
                    toRemTris.Add(it.TriangleEdge.TriangleId);
                    // This will be caught by the above adjacency check
                    Debug.Assert(edges0.Count > 1 && edges1.Count > 1, "The edge is already valid");
                    // Only portals should be added - not the final vert
                    Debug.Assert(edges0[^1] != corner2 && edges1[^1] != corner2, "Corner is already added?");
                    edges0.Add(corner2);
                    edges1.Add(corner2);
                    for (int i = 0; i < toRemTris.Count; i++) {
                        triangles.Return(toRemTris[i]);
                    }
                    for (int i = 0; i < edges1.Count / 2; i++) {
                        var t = edges1[i];
                        edges1[i] = edges1[edges1.Count - 1 - i];
                        edges1[edges1.Count - 1 - i] = t;
                    }
                    var tri1 = edges0.Count >= 3 ? TriangulatePolygon(edges0, type) : InvalidTriId;
                    var tri2 = edges1.Count >= 3 ? TriangulatePolygon(edges1, type) : InvalidTriId;
                    //if (tri1 != InvalidTriId) SetAdjacenctTriangle(corner2, corner1, tri1);
                    //if (tri2 != InvalidTriId) SetAdjacenctTriangle(corner1, corner2, tri2);
                    ValidTriangleAdjacency(tri1);
                    ValidTriangleAdjacency(tri2);
                    //ValidateAllTriangles();
                    return true;
                }
            }

            private TriangleId GetCornersTriangle(ushort cornerI, Int2 dir) {
                var corner1 = corners[cornerI];
                var dirN = new Int2(-dir.Y, dir.X);
                var otriI = CreateAdjacency().GetTriangleAt(CreateReadOnly(), corner1);
                var triI = otriI;
                for (int t = 0; t < 100; ++t) {
                    if (triI == InvalidTriId) break;
                    var tri = triangles[triI];
                    var c1I = tri.FindCorner(cornerI);
                    var c2I = tri.GetCornerWrapped(c1I + 1);
                    var c3I = tri.GetCornerWrapped(c1I + 2);
                    if (Int2.Dot(dirN, (Int2)corners[c2I] - corner1) >= 0 && Int2.Dot(dirN, (Int2)corners[c3I] - corner1) <= 0) {
                        return triI;
                    }
                    triI = adjacency[new Edge(cornerI, c2I)].GetOtherTriangle(triI);
                }
                return otriI;
            }

            public TriangleId SetTriangleTypeByEdge(CornerId c0, CornerId c1, TriangleType type, bool propagate = false) {
                using var marker = setTriTypeMarker.Auto();
                var edge = new Edge(c0, c1);
                if (!adjacency.TryGetValue(edge, out var adjacent)) return InvalidTriId;
                var triI = adjacent.GetTriangle(edge.GetSign(c0));
                if (triI == InvalidTriId) return InvalidTriId;
                var triangle = triangles[triI];
                if (triangle.Type.Equals(type)) return triI;
                triangle.Type = type;
                triangles[triI] = triangle;
                if (propagate) {
                    var cornerI = triangle.FindCorner(c1);
                    var corner0 = c1;
                    for (int i = 1; i < 3; i++) {
                        var corner1 = triangle.GetCornerWrapped(cornerI + i);
                        var oedge = new Edge(corner0, corner1);
                        if (!pinnedEdges.Contains(oedge)) {
                            SetTriangleTypeByEdge(corner1, corner0, type, true);
                        }
                        corner0 = corner1;
                    }
                }
                return triI;
            }
            
            // Currently the navmesh can be left in a non-delaunay state
            // this repairs it back to delaunay
            public int RepairSwap() {
                ValidateAllTriangles();
                int steps = 0;
                for (; steps < 100 && RepairStep() > 0; ++steps) ;
                ValidateAllTriangles();
                return steps;
            }
            private int RepairStep() {
                int count = 0;
                for (var it = triangles.GetEnumerator(); it.MoveNext();) {
                    var tri = it.Current;
                    for (int i = 0; i < 3; i++) {
                        var edge = tri.GetEdge(i);
                        if (!GetRequireSwap(edge)) continue;
                        SwapEdge(edge);
                        ++count;
                    }
                }
                return count;
            }
            private void CheckSwap(Edge edge) {
                if (GetRequireSwap(edge)) SwapEdge(edge);
            }

            // Does this edge need to be swapped to maintain Delaunay
            private bool GetRequireSwap(Edge edge) {
                return GetSwapScore(edge) > 0;
            }
            private long GetSwapScore(Edge edge) {
                if (pinnedEdges.Contains(edge)) return int.MinValue;
                if (!adjacency.TryGetValue(edge, out var adjacent)) return int.MinValue;
                if (adjacent.Triangle1 == InvalidTriId) return int.MinValue;
                if (adjacent.Triangle2 == InvalidTriId) return int.MinValue;
                var tri1 = triangles[adjacent.Triangle1];
                var tri2 = triangles[adjacent.Triangle2];
                Debug.Assert(tri1.Type.Equals(tri2.Type), "Non-pinned edge does not share type!");
                var c0 = corners[edge.Corner1];
                var c1 = corners[edge.Corner2];
                var e0 = corners[tri1.FindNextCorner(edge.Corner1)];
                var e1 = corners[tri2.FindNextCorner(edge.Corner2)];
                var score = NavUtility.InCircle(c0, e0, c1, e1);
                return -score;
            }

            // Return the new splitting edge
            private Edge SwapEdge(Edge edge) {
                if (!adjacency.TryGetValue(edge, out var adjacent)) return Edge.Invalid;
                ValidTriangleWinding(adjacent.Triangle1);
                ValidTriangleWinding(adjacent.Triangle2);
                ValidTriangleAdjacency(adjacent.Triangle1);
                ValidTriangleAdjacency(adjacent.Triangle2);
                var tri1 = triangles[adjacent.Triangle1];
                var tri2 = triangles[adjacent.Triangle2];
                var e1I = tri1.FindCorner(edge.Corner1);
                var e2I = tri2.FindCorner(edge.Corner2);
                var ne1I = tri1.GetCornerWrapped(e1I + 1);
                var ne2I = tri2.GetCornerWrapped(e2I + 1);
                if (ne1I == ne2I) {
                    Debug.Fail("Corners match!");
                }
                tri1.SetCorner(e1I, ne2I);
                tri2.SetCorner(e2I, ne1I);
                OverwriteTriangle(adjacent.Triangle1, tri1);
                OverwriteTriangle(adjacent.Triangle2, tri2);
                adjacency.Remove(edge);
                Debug.Assert(!pinnedEdges.Contains(edge));
                ValidTriangleAdjacency(adjacent.Triangle1);
                ValidTriangleAdjacency(adjacent.Triangle2);
                //ValidateAllTriangles();
                var newEdge = new Edge(ne1I, ne2I);
                return newEdge;
            }
            private TriangleId AppendTriangle(CornerId c1, CornerId c2, CornerId c3, TriangleType type) {
                var triI = (TriangleId)triangles.Allocate();
                if (c1 == InvalidTriId || c2 == InvalidTriId || c3 == InvalidTriId) {
                    Debug.Fail("Attempting to insert invalid triangle");
                    return InvalidTriId;
                }
                InitializeTriangle(triI, new Triangle() { C1 = c1, C2 = c2, C3 = c3, Type = type, });
                return triI;
            }
            private void InitializeTriangle(TriangleId triI, Triangle tri) {
                triangles[triI] = tri;
                RegisterToMap(triI, true);
                SetAllAdjacency(triI);
                ValidTriangleWinding(triI);
            }
            private void OverwriteTriangle(TriangleId triI, Triangle tri) {
                RegisterToMap(triI, false);
                triangles[triI] = tri;
                ValidTriangleWinding(triI);
                RegisterToMap(triI, true);
                SetAllAdjacency(triI);
            }
            private void InitializeTriangleNoAdj(TriangleId triI, Triangle tri) {
                triangles[triI] = tri;
                RegisterToMap(triI, true);
            }
            private void OverwriteTriangleNoAdj(TriangleId triI, Triangle tri) {
                RegisterToMap(triI, false);
                triangles[triI] = tri;
                ValidTriangleWinding(triI);
                RegisterToMap(triI, true);
            }

            private void RegisterToMap(TriangleId triId, bool enable) {
                var tri = triangles[triId];
                if (enable) {
                    var c1 = (Int2)corners[tri.C1];
                    var c2 = (Int2)corners[tri.C2];
                    var c3 = (Int2)corners[tri.C3];
                    var triMin = Int2.Min(c1, Int2.Min(c2, c3)) + (1 << (TriGridShift - 1)) - 1;
                    var triMax = Int2.Max(c1, Int2.Max(c2, c3)) - (1 << (TriGridShift - 1)) - 1;
                    triMin >>= TriGridShift;
                    triMax >>= TriGridShift;
                    for (int y = triMin.Y; y <= triMax.Y; y++) {
                        int yI = (y << TriGridShift) | (1 << (TriGridShift - 1));
                        int minX = triMin.X, maxX = triMax.X + 1;
                        ApplyBound(ref minX, ref maxX, yI, c1, c2);
                        ApplyBound(ref minX, ref maxX, yI, c2, c3);
                        ApplyBound(ref minX, ref maxX, yI, c3, c1);
                        for (int x = minX; x <= maxX; x++) {
                            triangleGrid.InsertTriangle(triId, new Int2(x, y));
                        }
                    }
                }
            }

            private void ApplyBound(ref int minX, ref int maxX, int y, Int2 c1, Int2 c2) {
                if (c1.Y == c2.Y) return;
                bool isMinEdge = c1.Y < c2.Y;
                int x = (c2.X - c1.X) * (y - c1.Y);
                x /= (c2.Y - c1.Y);
                x += c1.X;
                if (isMinEdge) {
                    x += 1 << (TriGridShift - 1) - 1;
                    x >>= TriGridShift;
                    minX = Math.Max(minX, x);
                } else {
                    x -= 1 << (TriGridShift - 1) - 1;
                    x >>= TriGridShift;
                    maxX = Math.Min(maxX, x);
                }
            }

            private void GetMinMax(TriangleId triangleId, out Int2 min, out Int2 max) {
                var tri = triangles[triangleId];
                min = max = (Int2)corners[tri.C1];
                var c2 = (Int2)corners[tri.C2];
                var c3 = (Int2)corners[tri.C3];
                min = Int2.Min(min, Int2.Min(c2, c3));
                max = Int2.Max(max, Int2.Max(c2, c3));
            }

            // Find another triangle which shares corner c1
            private ushort FindOtherTriangle(TriangleId triId, CornerId c1, CornerId c2, CornerId c3) {
                var adj = adjacency[new Edge(c1, c2)];
                var otri = adj.GetOtherTriangle(triId);
                if (otri != InvalidTriId) return otri;
                adj = adjacency[new Edge(c1, c3)];
                otri = adj.GetOtherTriangle(triId);
                if (otri != InvalidTriId) return otri;
                return InvalidTriId;
            }

            private TriangleId GetAdjacenctTriangle(ushort c1, ushort c2) {
                var edge = new Edge(c1, c2);
                if (!adjacency.TryGetValue(edge, out var adjacent)) adjacent = EdgeAdjacency.None;
                return adjacent.GetTriangle(edge.GetSign(c1));
            }
            // PLACEHOLDER: This will be replaced by specialized adjacency routines
            private void SetAllAdjacency(TriangleId triI) {
                var tri = triangles[triI];
                for (int i = 0; i < 3; i++) {
                    var c0 = tri.GetCorner(i);
                    var c1 = tri.GetCornerWrapped(i + 1);
                    SetAdjacenctTriangle(c0, c1, triI);
                    Debug.Assert(GetAdjacenctTriangle(c0, c1) == triI);
                }
            }
            private void SetAdjacency(CornerId c1, CornerId c2, TriangleId triI, TriangleId otriI) {
                var edge = new Edge(c1, c2);
                var adjacenct = new EdgeAdjacency() { Triangle1 = InvalidTriId, Triangle2 = InvalidTriId, };
                adjacenct.SetTriangle(edge.GetSign(c1), triI);
                adjacenct.SetTriangle(!edge.GetSign(c1), otriI);
                adjacency[edge] = adjacenct;
            }
            private void SetAdjacenctTriangle(ushort c1, ushort c2, TriangleId triI) {
                var edge = new Edge(c1, c2);
                if (!adjacency.TryGetValue(edge, out var adjacent)) adjacent = EdgeAdjacency.None;
                adjacent.SetTriangle(edge.GetSign(c1), triI);
                adjacency[edge] = adjacent;
            }
            [System.Diagnostics.Conditional("DEBUG")]
            private void ValidateAllTriangles() {
                for (var it = triangles.GetEnumerator(); it.MoveNext();) {
                    ValidTriangleAdjacency((TriangleId)it.Index);
                }
            }
            [System.Diagnostics.Conditional("DEBUG")]
            private void ValidTriangleAdjacency(TriangleId triI) {
                //return;
                if (triI == InvalidTriId) return;
                var tri = triangles[triI];
                for (int i = 0; i < 3; i++) {
                    var c0 = tri.GetCorner(i);
                    var edge = tri.GetEdge(i);
                    var adj = adjacency[edge];
                    if (adj.GetTriangle(edge.GetSign(c0)) != triI) {
                        Debug.Fail("Invalid adjacency: " + triI);
                    }
                    var otriI = adj.GetTriangle(!edge.GetSign(c0));
                    if (otriI == InvalidTriId) continue;
                    var otri = triangles[otriI];
                    var oedge = otri.GetEdgeWrapped(otri.FindCorner(c0) + 2);
                    if (!oedge.Equals(edge)) {
                        Debug.Fail("Adjacency mismatch");
                    }
                }
            }
            [System.Diagnostics.Conditional("DEBUG")]
            private void ValidTriangleWinding(TriangleId triI) {
                //return;
                if (triI == InvalidTriId) return;
                var tri = triangles[triI];
                var c1 = (Int2)corners[tri.C1];
                var c2 = (Int2)corners[tri.C2];
                var c3 = (Int2)corners[tri.C3];
                if (NavUtility.Cross(c2 - c1, c3 - c1) > 0) {
                    Debug.Fail("Triangle has bad winding");
                }
            }

            // Insert a vertex in inside of a triangle
            private void PokeTriangle(TriangleId triI, CornerId corner) {
                ValidTriangleAdjacency(triI);
                ValidTriangleWinding(triI);
                var tri2I = (TriangleId)triangles.Allocate();
                var tri3I = (TriangleId)triangles.Allocate();
                var tri1 = triangles[triI];
                var tri2 = tri1;
                var tri3 = tri1;
                tri1.C1 = corner;
                tri2.C2 = corner;
                tri3.C3 = corner;
                OverwriteTriangleNoAdj(triI, tri1);
                InitializeTriangleNoAdj(tri2I, tri2);
                InitializeTriangleNoAdj(tri3I, tri3);
                SetAdjacency(tri1.C1, tri1.C2, triI, tri3I);
                SetAdjacency(tri2.C2, tri2.C3, tri2I, triI);
                SetAdjacency(tri3.C3, tri3.C1, tri3I, tri2I);
                SetAdjacenctTriangle(tri2.C3, tri2.C1, tri2I);
                SetAdjacenctTriangle(tri3.C1, tri3.C2, tri3I);

                CheckSwap(new Edge(tri1.C2, tri1.C3));
                CheckSwap(new Edge(tri1.C3, tri2.C1));
                CheckSwap(new Edge(tri2.C1, tri1.C2));

                ValidTriangleAdjacency(triI);
                ValidTriangleAdjacency(tri2I);
                ValidTriangleAdjacency(tri3I);
                //ValidateAllTriangles();
            }

            private void PokeEdge(Edge edge, CornerId corner) {
                var adjacent = adjacency[edge];
                ValidTriangleAdjacency(adjacent.Triangle1);
                ValidTriangleAdjacency(adjacent.Triangle2);
                if (adjacent.Triangle1 != InvalidTriId) {
                    SplitTriangle(adjacent.Triangle1, edge.Corner2, corner);
                }
                if (adjacent.Triangle2 != InvalidTriId) {
                    SplitTriangle(adjacent.Triangle2, edge.Corner1, corner);
                }
                adjacency.Remove(edge);
                Debug.Assert(!pinnedEdges.Contains(edge));
                ValidTriangleAdjacency(adjacent.Triangle1);
                ValidTriangleAdjacency(adjacent.Triangle2);
                //ValidateAllTriangles();
            }

            private TriangleId SplitTriangle(TriangleId triI, CornerId triCorner, CornerId corner) {
                var tri = triangles[triI];
                var edgeI = tri.FindCorner(triCorner);
                var triCorner1 = tri.GetCornerWrapped(edgeI + 1);
                var triCorner2 = tri.GetCornerWrapped(edgeI + 2);
                var newTriI = AppendTriangle(triCorner2, triCorner, corner, tri.Type);
                tri.SetCorner(edgeI, corner);
                OverwriteTriangle(triI, tri);
                CheckSwap(new Edge(triCorner1, triCorner2));
                CheckSwap(new Edge(triCorner2, triCorner));
                return newTriI;
            }

            private unsafe bool GetPolygonIsCW(Span<CornerId> cornerIds) {
                long sum = 0;
                for (int i = 2; i < cornerIds.Length; i++) {
                    var last = (Int2)corners[cornerIds[0]];
                    var i0 = (Int2)corners[cornerIds[i - 1]] - last;
                    var i1 = (Int2)corners[cornerIds[i + 0]] - last;
                    sum += NavUtility.Cross(i0, i1);
                }
                return sum <= 0;
            }
            private unsafe ushort SetInvalid(TriangleEdge* edges, int len) {
                if (edges != null) for (int i = 0; i < len; i++) edges[i] = TriangleEdge.Invalid;
                return InvalidTriId;
            }
            private unsafe bool CheckValid(TriangleEdge* edges, int len) {
                if (edges != null) for (int i = 0; i < len; i++) if (!edges[i].IsValid) return false;
                return true;
            }

            private unsafe TriangleId TriangulatePolygon(Span<CornerId> cornerIds, TriangleType type, TriangleEdge* edges = null) {
                using var edgeMarker = triangulatePolyMarker.Auto();
                Debug.Assert(cornerIds.Length >= 3, "Polygon has too few points");
                if (!GetPolygonIsCW(cornerIds)) {
                    Debug.Fail("Polygon is not CW");
                }
                if (cornerIds.Length < 3) return SetInvalid(edges, cornerIds.Length);
                SetInvalid(edges, cornerIds.Length);
                var p0 = (Int2)corners[cornerIds[0]];
                var pN = (Int2)corners[cornerIds[^1]];
                // Polygon might have a pinched triangle
                var bestI = -1;
                for (int i = 1; i < cornerIds.Length - 1; i++) {
                    // Cannot construct tri with shared edge
                    if (cornerIds[i] == cornerIds[0] || cornerIds[i] == cornerIds[^1]) continue;
                    // Cannot construct tri when hard edge blocks
                    // This broke a degenerate case (0, 1, 0, 2, 3, 4); Should connect 0, 1, 4
                    //if (cornerIds[i + 1] == cornerIds[0] || cornerIds[i - 1] == cornerIds[len - 1]) continue;
                    var test = (Int2)corners[cornerIds[i]];
                    var score = NavUtility.GetAreaCW(p0, test, pN);
                    if (score < 0) continue;
                    if (bestI >= 0) {
                        var best = corners[cornerIds[bestI]];
                        if (NavUtility.InCircle(p0, pN, best, test) <= 0) continue;
                    }
                    bool valid = true;
                    for (int j = 1; j < cornerIds.Length; j++) {
                        var j0 = (Int2)corners[cornerIds[j - 1]];
                        var j1 = (Int2)corners[cornerIds[j]];
                        if (!NavUtility.IsCCW(j0, j1, test)) continue;
                        if (cornerIds[j] == cornerIds[0]) {
                            continue;
                        } else if (cornerIds[j - 1] == cornerIds[^1]) {
                            continue;
                        } else if (cornerIds[j - 1] == cornerIds[0] && cornerIds[j] == cornerIds[^1]) {
                            // p0 - pN edge is repeated in the polygon; ignore it
                            continue;
                        } else if (cornerIds[j - 1] == cornerIds[0]) {
                            if (p0.Equals(pN)) continue;
                            // Edge is shared; if it extends between test and pN, then it is blocking
                            if (!NavUtility.IsCCW(p0, j1, pN) && !NavUtility.IsCCW(p0, test, j1)) {
                                valid = false;
                                break;
                            }
                        } else if (cornerIds[j] == cornerIds[^1]) {
                            if (p0.Equals(pN)) continue;
                            // Same as above, but for other edge
                            if (!NavUtility.IsCCW(p0, j0, pN) && !NavUtility.IsCCW(pN, j0, test)) {
                                valid = false;
                                break;
                            }
                        } else {
                            if (NavUtility.GetIntersects(p0, test, j0, j1)) { valid = false; break; }
                            if (NavUtility.GetIntersects(pN, test, j0, j1)) { valid = false; break; }
                        }
                    }
                    if (!valid) continue;
                    bestI = i;
                }
                if (bestI == -1) {
                    Debug.Fail("Failed to find valid triangle");
                    if (cornerIds.Length == 3) bestI = 1;
                    else return SetInvalid(edges, cornerIds.Length);
                }
                var triI = AppendTriangle(cornerIds[0], cornerIds[bestI], cornerIds[^1], type);
                if (bestI > 1) {
                    if (cornerIds[1] == cornerIds[bestI]) {
                        var otherTriI = TriangulatePolygon(cornerIds.Slice(1, bestI - 1), type, edges != null ? edges + 1 : null);
                        if (edges != null) edges[bestI - 1] = new TriangleEdge(otherTriI, 2);
                    } else if (cornerIds[0] == cornerIds[bestI - 1]) {
                        var otherTriI = TriangulatePolygon(cornerIds.Slice(0, bestI - 1), type, edges);
                        if (edges != null) edges[bestI - 2] = new TriangleEdge(otherTriI, 2);
                    } else {
                        var otherTriI = TriangulatePolygon(cornerIds.Slice(0, bestI + 1), type, edges);
                        //SetAdjacency(new TriangleEdge(triI, 0), new TriangleEdge(otherTriI, 2));
                    }
                }
                if (bestI < cornerIds.Length - 2) {
                    var cutO = cornerIds[^1] == cornerIds[bestI + 1] ? 2 : cornerIds[^2] == cornerIds[bestI] ? 1 : 0;
                    var cutI = bestI + cutO;
                    var otherTriI = TriangulatePolygon(cornerIds.Slice(cutI, cornerIds.Length - (bestI + (cutO != 0 ? 2 : 0))), type, edges != null ? edges + cutI : null);
                    if (cutO == 0) ;// SetAdjacency(new TriangleEdge(triI, 1), new TriangleEdge(otherTriI, 2));
                    else if (edges != null) {
                        if (cutO == 1) {
                            edges[bestI] = new TriangleEdge(otherTriI, 2);
                        } else {
                            edges[bestI + 1] = new TriangleEdge(otherTriI, 2);
                        }
                    }
                }
                if (edges != null) {
                    if (cornerIds[1] == cornerIds[bestI]) edges[0] = new TriangleEdge(triI, 0);
                    if (cornerIds[0] == cornerIds[bestI - 1]) edges[bestI - 1] = new TriangleEdge(triI, 0);
                    if (cornerIds[^2] == cornerIds[bestI]) edges[cornerIds.Length - 2] = new TriangleEdge(triI, 1);
                    if (cornerIds[^1] == cornerIds[bestI + 1]) edges[bestI] = new TriangleEdge(triI, 1);
                    edges[cornerIds.Length - 1] = new TriangleEdge(triI, 2);
                }
                if (!CheckValid(edges, cornerIds.Length)) {
                    Debug.Fail("Invalid edge was detected");
                }
                return triI;
            }
        }



        public TriangleId GetTriangleFromCornerPair(CornerId c0, CornerId c1) {
            var edge = new Edge(c0, c1);
            if (!adjacency.TryGetValue(edge, out var adjacent)) return InvalidTriId;
            return adjacent.GetTriangle(edge.GetSign(c0));
        }

        public TriangleEdge GetAdjacentEdge(TriangleEdge triEdge) {
            var tri = triangles[triEdge.TriangleId];
            var c0 = tri.GetCorner(triEdge.EdgeId);
            var c1 = tri.GetCornerWrapped(triEdge.EdgeId + 1);
            var edge = new Edge(c0, c1);
            if (!adjacency.TryGetValue(edge, out var adjacent)) adjacent = EdgeAdjacency.None;
            var newEdge = new TriangleEdge(adjacent.GetTriangle(edge.GetSign(c1)), InvalidTriId);
            if (newEdge.TriangleId != InvalidTriId) {
                var adjTri = triangles[newEdge.TriangleId];
                newEdge.EdgeId = (ushort)adjTri.FindCorner(c1);
            }
            return newEdge;
        }

        public Vector3 GetCentre(int triangleId) {
            var tri = triangles[triangleId];
            return (corners[tri.C1].ToUVector3(0f) + corners[tri.C2].ToUVector3(0f) + corners[tri.C3].ToUVector3(0f)) / 3f;
        }
        private Int2 GetCentreInt2_3(ushort triangleId) {
            var tri = triangles[triangleId];
            return ((Int2)corners[tri.C1] + (Int2)corners[tri.C2] + (Int2)corners[tri.C3]);
        }
        public Int2 GetCentreInt2(ushort triangleId, int scale = 1) {
            return GetCentreInt2_3(triangleId) * scale / 3;
        }
        private Edge GetEdge(TriangleEdge triangleEdge) {
            var tri = triangles[triangleEdge.TriangleId];
            var c0 = tri.GetCorner(triangleEdge.EdgeId);
            var c1 = tri.GetCornerWrapped(triangleEdge.EdgeId + 1);
            return new Edge(c0, c1);
        }

        public SparseArray<Triangle>.Enumerator GetTriangleEnumerator() {
            return triangles.GetEnumerator();
        }

        public TriangleId GetTriangleAt(Coordinate p) {
            return NavMesh.GetTriangleAt(p);
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


        private static ushort GetCellHashAt(Coordinate p) {
            return GetCellHashAt((int)p.X / GridCellSize, (int)p.Z / GridCellSize);
        }
        private static ushort GetCellHashAt(int cX, int cY) {
            uint v = (uint)(cX + (cY << 8));
            return (ushort)(v ^ (v >> 3));
        }
    }
}
