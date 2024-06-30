using Navigation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using UnityEngine;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Utility;

public class NavGrid : IDisposable {

    public enum LandscapeModes : byte {
        None = 0,
        Water = 0x40,
        Impassable = 0x80,
        Mask = 0xc0,
    }

    public const int Granularity = 2;

    public Int2 Size { get; private set; }
    private byte[] map = Array.Empty<byte>();
    private Int2 changeMin, changeMax;
    public bool HasChanges => changeMax.X >= changeMin.X;

    public void Allocate(Int2 size) {
        Size = size;
        map = new byte[Size.X * Size.Y];
        changeMin = 0;
        changeMax = Size - 1;
    }
    public void Dispose() {
    }

    public Int2 SimulationToGrid(Int2 pnt) {
        return (pnt * Granularity + Granularity / 2) / 1024;
    }
    public Int2 GridToSimulation(Int2 pnt) {
        return (pnt * 1024 + 512) / Granularity;
    }
    public void AppendGeometry(Span<Int2> polygon, int delta) {
        var gridIt = new GridIteratorUtility(1024, Granularity, Size);
        gridIt.ComputeAABB(polygon, out var aabbMin, out var aabbMax);
        using var xCoords = new PooledList<int>(4);
        for (int y = aabbMin.Y; y <= aabbMax.Y; y++) {
            int iY = y * Size.X;
            gridIt.ComputeXRange(polygon, y, ref xCoords.AsMutable());
            for (int iX = 0; iX < xCoords.Count; iX += 2) {
                var xMin = xCoords[iX] + iY;
                var xMax = xCoords[iX + 1] + iY;
                for (int x = xMin; x < xMax; x++) {
                    ref var cell = ref map[x];
                    var otypeId = ConvertTypeId(cell);
                    cell = (byte)(cell + delta);
                    var ntypeId = ConvertTypeId(cell);
                    if (otypeId != ntypeId) {
                        NotifyMutation(new Int2(x - iY, y), otypeId, ntypeId);
                    }
                }
            }
            xCoords.Clear();
        }
    }
    public Accessor GetAccessor() {
        return new Accessor(this);
    }

    public struct Accessor {
        NavGrid grid;
        public Int2 Size;
        public byte[] Map;
        public Accessor(NavGrid grid) {
            this.grid = grid;
            Size = grid.Size;
            Map = grid.map;
        }
        public void SetLandscapePassable(Int2 pnt, LandscapeModes mode) {
            int i = pnt.X + pnt.Y * Size.X;
            ref var cell = ref Map[i];
            var otypeId = ConvertTypeId(cell);
            cell = (byte)((cell & ~(byte)LandscapeModes.Mask) | (byte)mode);
            var ntypeId = ConvertTypeId(cell);
            if (otypeId != ntypeId) {
                grid.NotifyMutation(pnt, otypeId, ntypeId);
            }
        }
    }

    private void NotifyMutation(Int2 pnt, byte otypeId, byte ntypeId) {
        // Could track hash to detect when changes are reversed in the same frame (no actual change)
        //ulong i = (ulong)pnt.X + ((ulong)pnt.Y << 16);
        //GridHash -= ComputeHash(i, otypeId);
        //GridHash += ComputeHash(i, ntypeId);
        changeMin = Int2.Min(changeMin, pnt);
        changeMax = Int2.Max(changeMax, pnt);
    }

    public struct GridIteratorUtility {
        public int GranularityFrom;     // Simulation: 1024
        public int GranularityTo;       // Grid: 2
        public Int2 Size;
        public GridIteratorUtility(int granFrom, int granTo, Int2 gridSize) {
            GranularityFrom = granFrom;
            GranularityTo = granTo;
            Size = gridSize;
        }
        public void ComputeAABB(Span<Int2> polygon, out Int2 aabbMin, out Int2 aabbMax) {
            aabbMax = aabbMin = polygon[0];
            for (int i = 1; i < polygon.Length; i++) {
                var pnt = polygon[i];
                aabbMin = Int2.Min(aabbMin, pnt);
                aabbMax = Int2.Max(aabbMax, pnt);
            }
            aabbMin = SimulationToGrid(aabbMin + GranularityFrom / 2 / GranularityTo);
            aabbMax = SimulationToGrid(aabbMax - GranularityFrom / 2 / GranularityTo);
            aabbMin = Int2.Max(aabbMin, 0);
            aabbMax = Int2.Min(aabbMax, Size - 1);
        }
        public void ComputeXRange(Span<Int2> polygon, int yPos, ref PooledList<int> xCoords) {
            yPos = GridToSimulationY(yPos);
            for (int p = 0; p < polygon.Length; p++) {
                var p0 = polygon[p];
                var p1 = polygon[(p + 1) % polygon.Length];
                if ((p1.Y > yPos) != (p0.Y > yPos)) {
                    var x = p0.X + (p1.X - p0.X) * (yPos - p0.Y) / (p1.Y - p0.Y);
                    x = SimulationToGridX(x + GranularityFrom / 2 / GranularityTo);
                    x = Math.Clamp(x, 0, Size.X - 1);
                    var i = 0;
                    for (; i < xCoords.Count; i++) if (xCoords[i] > x) break;
                    xCoords.Insert(i, x);
                }
            }
        }
        private Int2 SimulationToGrid(Int2 pnt) {
            return (pnt * GranularityTo) / GranularityFrom;
        }
        private int SimulationToGridX(int value) {
            return (value * GranularityTo) / GranularityFrom;
        }
        public int GridToSimulationY(int value) {
            return (value * GranularityFrom + GranularityFrom / 2) / GranularityTo;
        }
    }

    internal struct AdjacencyIds {
        public TriangleType Type1, Type2;
    }
    public struct AdjacencyPushJob {
        public Int2 Size;
        public RectI Range;
        public byte[] map;
        public NavMesh2Baker.Mutator Mutator;
        public bool Enable;

        unsafe public struct CoordEdge : IEquatable<CoordEdge>, IComparable<CoordEdge> {
            public Coordinate C1, C2;
            public CoordEdge(Coordinate c1, Coordinate c2) {
                var cmp = Compare(c1, c2);
                if (cmp >= 0) { C1 = c1; C2 = c2; } else { C1 = c2; C2 = c1; }
            }
            public int CompareTo(CoordEdge other) {
                int cmp = Compare(C1, other.C1);
                if (cmp == 0) cmp = Compare(C2, other.C2);
                return cmp;
            }
            public bool Equals(CoordEdge other) {
                return C1.Equals(other.C1) && C2.Equals(other.C2);
            }
            private static int Compare(Coordinate c1, Coordinate c2) {
                int cmp = c1.X.CompareTo(c2.X);
                if (cmp == 0) cmp = c1.Z.CompareTo(c2.Z);
                return cmp;
            }
            public override int GetHashCode() {
                var hash = C1.GetHashCode() * 1543 + C2.GetHashCode();
                hash ^= hash >> 16;
                return hash;
            }
            public bool GetSign(Coordinate c1) {
                return C1.Equals(c1);
            }
        }

        private void GetEdgesInRange(ref PooledHashMap<Edge, AdjacencyIds> edgeValues) {
            var meshSize = Size * Coordinate.Granularity / Granularity;
            using (var marker = new ProfilerMarker("NavMesh Triangulate").Auto()) {
                var cornerMutator = Mutator.CreateVertexMutator();
                for (int d = 0; d < 2; d++) {
                    var swizDir = d == 0 ? new Int2(1, 0) : new Int2(0, 1);
                    var swizMin = Swizzle(Range.Min, d == 1);
                    var swizMax = Swizzle(Range.Max, d == 1);
                    var swizEnd = Swizzle(Size, d == 1);
                    var row0 = new PooledList<Int2>(32);
                    var row1 = new PooledList<Int2>(32);
                    for (int iy = swizMin.Y - 1; iy <= swizMax.Y; iy++) {
                        Swap(ref row0, ref row1);
                        row1.Clear();
                        // Calculate runs of types
                        if (iy < 0 || iy >= swizEnd.Y) {
                            row1.Add(new Int2(swizMax.X, -1));
                        } else {
                            for (int ix = swizMin.X; ix < swizMax.X;) {
                                var pnt = Swizzle(new Int2(ix, iy), d == 1);
                                var pntT = ConvertTypeId(map[pnt.X + pnt.Y * Size.X]);
                                for (; ix < swizMax.X; ix++) {
                                    var nxtT = ConvertTypeId(map[pnt.X + pnt.Y * Size.X]);
                                    if (nxtT != pntT) break;
                                    pnt += swizDir;
                                }
                                row1.Add(new Int2(ix, pntT));
                            }
                        }
                        // First iteration, ignore
                        if (row0.Count == 0) continue;
                        // Find where runs have a type mismatch
                        int r0 = 0, r1 = 0;
                        for (int ix = swizMin.X; ix < swizMax.X;) {
                            Int2 x0 = row0[r0], x1 = row1[r1];
                            int c = Math.Min(x0.X, x1.X);
                            if (x0.Y != x1.Y) {
                                var pnt = Swizzle(new Int2(ix, iy), d == 1);
                                var c0 = Coordinate.FromInt2(Int2.Clamp((pnt + swizDir * 0) * Coordinate.Granularity / Granularity, 0, meshSize));
                                var c1 = Coordinate.FromInt2(Int2.Clamp((pnt + swizDir * (c - ix)) * Coordinate.Granularity / Granularity, 0, meshSize));
                                InsertEdge(ref edgeValues, c0, c1,
                                    new TriangleType((byte)x0.Y),
                                    new TriangleType((byte)x1.Y),
                                    d == 1
                                );
                            }
                            ix = c;
                            if (c == x0.X) ++r0;
                            if (c == x1.X) ++r1;
                        }
                    }
                    row0.Dispose();
                    row1.Dispose();
                }
            }
        }

        private Int2 Swizzle(Int2 value, bool swizzle) {
            return swizzle ? value.YX : value;
        }

        /*public void ExecuteNew() {
            var edgeValues = new PooledHashMap<CoordEdge, AdjacencyIds>(128);
            GetEdgesInRange(ref edgeValues);
            //this.edgeValues = edgeValues;
            if (!Enable) return;

            var ro = Mutator.CreateReadOnly();
            var aj = Mutator.NavBaker.NavMesh.GetAdjacency();

            var navMin = Range.Min * Coordinate.Granularity / Granularity;
            var navMax = Range.Max * Coordinate.Granularity / Granularity;

            // Edges that are within the bounds
            using var containedEdges = new PooledHashSet<Edge>(16);
            // Edges that intersect the bounds perimeter
            using var perimeterEdges = new PooledHashSet<Edge>(16);
            // Corners that are fully contained and should maybe be removed
            using var containedCorners = new PooledHashSet<ushort>(16);

            {
                Span<Coordinate> path = stackalloc Coordinate[4];
                path[0] = Coordinate.FromInt2(navMin);
                path[1] = Coordinate.FromInt2(new Int2(navMin.X, navMax.Y));
                path[2] = Coordinate.FromInt2(navMax);
                path[3] = Coordinate.FromInt2(new Int2(navMax.X, navMin.Y));

                var it = new NavMesh.PolygonIntersectEnumerator(
                    Mutator.NavBaker.NavMesh, path);
                it.PrimeClosedLoop();

                // Edges that encase the inner triangles
                using var workingEdges = new PooledHashSet<Edge>(16);
                while (it.MoveNext()) {
                    var tri = ro.GetTriangle(it.TriangleId);
                    var c1 = tri.C3;
                    var ifaceEdge = tri.GetEdge(it.Edge);
                    // We pass this edge, so its definitely along the perimeter
                    if (Mutator.GetIsPinnedEdge(ifaceEdge)) {
                        perimeterEdges.AddUnique(ifaceEdge);
                    }
                    // Find any fully contained edges and corners
                    for (int i = 0; i < 3; ++i) {
                        var c2 = tri.GetCorner(i);
                        var p1 = (Int2)ro.GetCorner(c1) * Granularity / Coordinate.Granularity;
                        var p2 = (Int2)ro.GetCorner(c2) * Granularity / Coordinate.Granularity;
                        if (Range.ContainsInclusive(p1)) containedCorners.AddUnique(c1);
                        if (Range.ContainsInclusive(p2)) containedCorners.AddUnique(c2);
                        var edge = new Edge(c1, c2);
                        if (Range.ContainsInclusive(p1) && Range.ContainsInclusive(p2)) {
                            // Working edges can be seen twice at most
                            if (workingEdges.ToggleUnique(edge)) {
                                // Edge is already touched if its in the workingEdges
                                containedEdges.Add(edge);
                            }
                        }
                        c1 = c2;
                    }
                }
                // Find all contained points/edges (flood fill)
                while (workingEdges.TryPop(out var item)) {
                    Trace.Assert(aj.TryGetValue(item, out var edgeAdj));
                    for (int t = 0; t < 2; t++) {
                        var triId = edgeAdj.GetTriangle(t == 0);
                        if (triId == NavMesh.InvalidTriId) continue;
                        var tri = ro.GetTriangle(triId);
                        for (int i = 0; i < 3; i++) {
                            var edge = tri.GetEdgeWrapped(i + 1);
                            if (containedEdges.Contains(edge)) continue;
                            var p1 = (Int2)ro.GetCorner(edge.Corner1) * Granularity / Coordinate.Granularity;
                            var p2 = (Int2)ro.GetCorner(edge.Corner2) * Granularity / Coordinate.Granularity;
                            if (Range.ContainsInclusive(p1)) containedCorners.AddUnique(edge.Corner1);
                            if (Range.ContainsInclusive(p2)) containedCorners.AddUnique(edge.Corner2);
                            if (!Range.ContainsInclusive(p1) || !Range.ContainsInclusive(p2)) continue;
                            containedEdges.Add(edge);
                            workingEdges.Add(edge);
                        }
                    }
                }
            }

            // Edges that are known to be required (but are already pinned)
            using var confirmedEdges = new PooledHashSet<Edge>(16);
            // Split/join perimeter edges
            var navBounds = RectI.FromMinMax(navMin, navMax);
            for (var it1 = perimeterEdges.GetEnumerator(); it1.MoveNext();) {
                var edge1 = it1.Current;
                var e1c1 = (Int2)Mutator.NavBaker.GetCorner(edge1.Corner1);
                var e1c2 = (Int2)Mutator.NavBaker.GetCorner(edge1.Corner2);
                var isect = Intersect(e1c1, e1c2, navBounds);
                // All perimeter edges must intersect (otherwise they are not perimeter)
                Debug.Assert(isect.Y > isect.X);
                var e1d = e1c2 - e1c1;
                var e1l2 = e1d.LengthSquared;
                Trace.Assert(aj.TryGetValue(edge1, out var e1aj));
                var e1t1 = ro.GetTriangle(e1aj.Triangle1).Type;
                var e1t2 = ro.GetTriangle(e1aj.Triangle2).Type;
                bool hasExtraMin = isect.X > 0;
                bool hasExtraMax = isect.Y < e1l2;
                isect.X = Math.Max(isect.X, 0);
                isect.Y = Math.Min(isect.Y, e1l2);
                var e1n = new Int2(e1d.Y, -e1d.X);
                var rsect = isect;
                for (var it2 = edgeValues.GetEnumerator(); it2.MoveNext();) {
                    var edge2 = it2.Current;
                    var e2c1l = (Int2)(edge2.Key.C1) - e1c1;
                    var e2c2l = (Int2)(edge2.Key.C2) - e1c1;
                    // Are lines collinear
                    if (Int2.Dot(e1n, e2c1l) != 0 || Int2.Dot(e1n, e2c2l) != 0) continue;
                    var e2c1dp = (int)Int2.Dot(e1d, e2c1l);
                    var e2c2dp = (int)Int2.Dot(e1d, e2c2l);
                    var sign = e2c1dp < e2c2dp;
                    var e2t1 = edge2.Value.Type1;
                    var e2t2 = edge2.Value.Type2;
                    if (!sign) {
                        Swap(ref e2t1, ref e2t2);
                        Swap(ref e2c1l, ref e2c2l);
                        Swap(ref e2c1dp, ref e2c2dp);
                    }
                    var c1Match = e2c1dp == isect.X;
                    var c2Match = e2c2dp == isect.Y;
                    if (e1t1.Equals(e2t1) && e1t2.Equals(e2t2)) {
                        //if (c1Match) containedCorners.AddUnique(edge2.Key.Corner1);
                        //if (c1Match) containedCorners.AddUnique(edge2.Key.Corner2);
                        // Fully overlaps
                        if (c1Match && c2Match) {
                            // Keep existing pin
                            rsect.X = rsect.Y;
                            it2.RemoveSelf(ref edgeValues);
                            break;
                        }
                        // Can only match at edges
                        c1Match &= hasExtraMin;
                        c2Match &= hasExtraMax;
                        if (c1Match || c2Match) {
                            // Any match means reuse
                            it2.RemoveSelf(ref edgeValues);
                            // Consume into split edge
                            if (c1Match) rsect.X = e2c2dp;
                            if (c2Match) rsect.Y = e2c1dp;
                        }
                        // Edges cannot merge - they dont share an endpoint
                    } else {
                        // Do nothing? Edge will get cut later
                    }
                }
                if (rsect.X == rsect.Y) {
                    // Preserve existing pin
                    it1.RemoveSelf(ref perimeterEdges.AsMutable());
                    confirmedEdges.Add(edge1);
                    continue;
                }
                it1.RemoveSelf(ref perimeterEdges.AsMutable());
                Mutator.UnpinEdge_NoRepair(edge1);
                if (rsect.X != 0) {
                    // Segment exists on min end
                    var start = ro.GetCorner(edge1.Corner1);
                    var end = Coordinate.FromInt2(e1c1 + (e1c2 - e1c1) * rsect.X / e1l2);
                    InsertEdge(ref edgeValues, start, end, e1t1, e1t2);
                }
                if (rsect.Y != e1l2) {
                    // Segment exists on min end
                    var start = ro.GetCorner(edge1.Corner2);
                    var end = Coordinate.FromInt2(e1c1 + (e1c2 - e1c1) * rsect.Y / e1l2);
                    InsertEdge(ref edgeValues, start, end, e1t1, e1t2, true);
                }
            }
            // edgeValues contains edges to be inserted

        }*/

        private void InsertEdge(ref PooledHashMap<Edge, AdjacencyIds> edgeValues, Coordinate coord0, Coordinate coord1, TriangleType t0, TriangleType t1, bool flip = false) {
            var c0 = Mutator.NavBaker.RequireVertexId(coord0);
            var c1 = Mutator.NavBaker.RequireVertexId(coord1);
            var edge = new Edge(c0, c1);
            flip = edge.GetSign(c0) == flip;
            edgeValues.Add(edge, new AdjacencyIds() {
                Type1 = flip ? t1 : t0,
                Type2 = flip ? t0 : t1,
            });
        }
        private static void InsertEdge(ref PooledHashMap<CoordEdge, AdjacencyIds> edgeValues, Coordinate c0, Coordinate c1, TriangleType t0, TriangleType t1, bool flip = false) {
            var edge = new CoordEdge(c0, c1);
            flip = edge.GetSign(c0) == flip;
            edgeValues.Add(edge, new AdjacencyIds() {
                Type1 = t0,
                Type2 = t1,
            });
        }

        internal PooledHashMap<Edge, AdjacencyIds> edgeValues;
        //public int Threshold;
        public void Execute() {
            Span<Int2> Directions = stackalloc Int2[] { new Int2(1, 0), new Int2(0, 1), new Int2(-1, 0), new Int2(0, -1), };
            var edgeValues = new PooledHashMap<Edge, AdjacencyIds>(64);
            var meshSize = Size * Coordinate.Granularity / Granularity;
            using var containedCorners = new PooledHashSet<ushort>(16);
            GetEdgesInRange(ref edgeValues);
            if (Range.Size.X < Size.X || Range.Size.Y < Size.Y) {
                /*using (var marker = new ProfilerMarker("NavMesh Triangulate").Auto()) {
                    var cornerMutator = Mutator.CreateVertexMutator();
                    for (int d = 0; d < 2; d++) {
                        var swizDir = Directions[d];
                        var swizSize = Range.Size;
                        var swizMin = Range.Min;
                        if (d == 1) { swizSize = swizSize.YX; swizMin = swizMin.YX; }
                        if (swizMin.Y > 0) {
                            --swizMin.Y;
                            ++swizSize.Y;
                            ++swizSize.Y;
                        }
                        for (int iy = 1; iy < swizSize.Y; iy++) {
                            for (int ix = 0; ix < swizSize.X; ix++) {
                                var pnt0 = swizMin + new Int2(ix, iy - 1);
                                var pnt1 = swizMin + new Int2(ix, iy);
                                if (d == 1) { pnt0 = pnt0.YX; pnt1 = pnt1.YX; }
                                var pntT0 = ConvertTypeId(map[pnt0.X + pnt0.Y * Size.X]);
                                var pntT1 = ConvertTypeId(map[pnt1.X + pnt1.Y * Size.X]);
                                if (pntT0 == pntT1) continue;
                                int c = 1;
                                var nxt0 = pnt0;
                                var nxt1 = pnt1;
                                for (; ix + c < swizSize.X; c++) {
                                    nxt0 += swizDir;
                                    nxt1 += swizDir;
                                    var nxtT0 = ConvertTypeId(map[nxt0.X + nxt0.Y * Size.X]);
                                    var nxtT1 = ConvertTypeId(map[nxt1.X + nxt1.Y * Size.X]);
                                    if (nxtT0 != pntT0) break;
                                    if (nxtT1 != pntT1) break;
                                }
                                var coord0 = Int2.Clamp((pnt1 + swizDir * 0) * Coordinate.Granularity / Granularity, 0, meshSize);
                                var coord1 = Int2.Clamp((pnt1 + swizDir * c) * Coordinate.Granularity / Granularity, 0, meshSize);
                                var c0 = cornerMutator.RequireVertexId(Coordinate.FromInt2(coord0));
                                var c1 = cornerMutator.RequireVertexId(Coordinate.FromInt2(coord1));
                                var edge = new Edge(c0, c1);
                                if (edge.GetSign(c0) == (d != 0)) { var t = pntT0; pntT0 = pntT1; pntT1 = t; }
                                var adjacency = new AdjacencyIds() {
                                    Type1 = new TriangleType(pntT0),
                                    Type2 = new TriangleType(pntT1),
                                };
                                edgeValues.Add(edge, adjacency);
                                ix += c - 1;
                            }
                        }
                    }
                }*/
                this.edgeValues = edgeValues;
                if (!Enable) return;
                Span<Coordinate> path = stackalloc Coordinate[4];
                var navMin = Range.Min * Coordinate.Granularity / Granularity;
                var navMax = Range.Max * Coordinate.Granularity / Granularity;
                path[0] = Coordinate.FromInt2(navMin);
                path[1] = Coordinate.FromInt2(new Int2(navMin.X, navMax.Y));
                path[2] = Coordinate.FromInt2(navMax);
                path[3] = Coordinate.FromInt2(new Int2(navMax.X, navMin.Y));
                var ro = Mutator.CreateReadOnly();
                var it = new NavMesh.PolygonIntersectEnumerator(
                    Mutator.NavBaker.NavMesh, path);
                it.PrimeClosedLoop();
                // Edges that are within the bounds
                using var containedEdges = new PooledHashSet<Edge>(16);
                // Edges that encase the inner triangles
                using var workingEdges = new PooledHashSet<Edge>(16);
                // Edges that intersect the bounds perimeter
                using var perimeterEdges = new PooledHashSet<Edge>(16);
                while (it.MoveNext()) {
                    var tri = ro.GetTriangle(it.TriangleId);
                    var c1 = tri.C3;
                    var ifaceEdge = tri.GetEdge(it.Edge);
                    // We pass this edge, so its definitely along the perimeter
                    if (Mutator.GetIsPinnedEdge(ifaceEdge)) {
                        perimeterEdges.AddUnique(ifaceEdge);
                    }
                    // Find any fully contained edges and corners
                    for (int i = 0; i < 3; ++i) {
                        var c2 = tri.GetCorner(i);
                        var p1 = (Int2)ro.GetCorner(c1) * Granularity / Coordinate.Granularity;
                        var p2 = (Int2)ro.GetCorner(c2) * Granularity / Coordinate.Granularity;
                        if (Range.ContainsInclusive(p1)) containedCorners.AddUnique(c1);
                        if (Range.ContainsInclusive(p2)) containedCorners.AddUnique(c2);
                        var edge = new Edge(c1, c2);
                        if (Range.ContainsInclusive(p1) && Range.ContainsInclusive(p2)) {
                            // Working edges can be seen twice at most
                            if (workingEdges.ToggleUnique(edge)) {
                                // Edge is already touched if its in the workingEdges
                                containedEdges.Add(edge);
                            }
                        }
                        c1 = c2;
                    }
                }
                var aj = Mutator.NavBaker.NavMesh.GetAdjacency();
                // Find all contained points/edges (flood fill)
                while (workingEdges.TryPop(out var item)) {
                    Trace.Assert(aj.TryGetValue(item, out var edgeAdj));
                    for (int t = 0; t < 2; t++) {
                        var triId = edgeAdj.GetTriangle(t == 0);
                        if (triId == NavMesh.InvalidTriId) continue;
                        var tri = ro.GetTriangle(triId);
                        for (int i = 0; i < 3; i++) {
                            var edge = tri.GetEdgeWrapped(i + 1);
                            if (containedEdges.Contains(edge)) continue;
                            var p1 = (Int2)ro.GetCorner(edge.Corner1) * Granularity / Coordinate.Granularity;
                            var p2 = (Int2)ro.GetCorner(edge.Corner2) * Granularity / Coordinate.Granularity;
                            if (Range.ContainsInclusive(p1)) containedCorners.AddUnique(edge.Corner1);
                            if (Range.ContainsInclusive(p2)) containedCorners.AddUnique(edge.Corner2);
                            if (!Range.ContainsInclusive(p1) || !Range.ContainsInclusive(p2)) continue;

                            containedEdges.Add(edge);
                            workingEdges.Add(edge);
                        }
                    }
                }
                /* Perimeter edges are any edge with a point outside the region
                One end might be contained, or they may pass directly through the region
                - If edge is not collinear: ignore
                - If edge does not touch perimeter edge: ignore
                - If edge overlaps intersection segment
                    - If fully overlaps intersection segment
                        => Remove edgeValue item, Remove perimeter edge (keep existing pin)
                    - Else if edgeValue encompasses perimeter intersection segment
                        => Unpin perimeter edge, extend edgeValue
                    - Else (intersection segment is larger than edgeValue)
                - If edge touches end of perimeter edge:
                    => Check if type IDs match:
                        Remove perimeter edge, append extremes to edgeValues
                    => Else
                        Do nothing, keep edges separate
                Any remaining perimeter that overlap the region should be removed, cut in two, and re-pinned
                */
                /*
                 * Can only combine edges where they both touch/pass a boundary
                 * Can only combine if types match (otherwise no other match is possible)
                 * 
                 * If overlap:
                 * - Types match:
                 *   - New == active old:
                 *     = Move old to confirmed, discard new
                 *   - New touches corner of active old:
                 *     = Unpin old, combine old/new into new
                 *     = Move remaining old into "partial"
                 * - Type mismatch:
                 *   - New any overlaps active old:
                 *     = Unpin old, break, add to Partial (if len>0), keep new
                 *     
                 * Old: Items that should be unpinned
                 * New: Items that need to be newly pinned
                 * Partial: Items that have been split, should be combined with New or pinned
                 * Confirmed: Old items that have been matched (dont unpin)
                 */
                // Edges that are known to be required (but are already pinned)
                using var confirmedEdges = new PooledHashSet<Edge>(16);
                // Split/join perimeter edges
                var navBounds = RectI.FromMinMax(navMin, navMax);
                for (var it1 = perimeterEdges.GetEnumerator(); it1.MoveNext();) {
                    var edge1 = it1.Current;
                    var e1c1 = (Int2)Mutator.NavBaker.GetCorner(edge1.Corner1);
                    var e1c2 = (Int2)Mutator.NavBaker.GetCorner(edge1.Corner2);
                    var isect = Intersect(e1c1, e1c2, navBounds);
                    // All perimeter edges must intersect (otherwise they are not perimeter)
                    Debug.Assert(isect.Y > isect.X);
                    var e1d = e1c2 - e1c1;
                    var e1l2 = e1d.LengthSquared;
                    Trace.Assert(aj.TryGetValue(edge1, out var e1aj));
                    var e1t1 = ro.GetTriangle(e1aj.Triangle1).Type;
                    var e1t2 = ro.GetTriangle(e1aj.Triangle2).Type;
                    bool hasExtraMin = isect.X > 0;
                    bool hasExtraMax = isect.Y < e1l2;
                    isect.X = Math.Max(isect.X, 0);
                    isect.Y = Math.Min(isect.Y, e1l2);
                    var e1n = new Int2(e1d.Y, -e1d.X);
                    var rsect = isect;
                    for (var it2 = edgeValues.GetEnumerator(); it2.MoveNext();) {
                        var edge2 = it2.Current;
                        var e2c1l = (Int2)Mutator.NavBaker.GetCorner(edge2.Key.Corner1) - e1c1;
                        var e2c2l = (Int2)Mutator.NavBaker.GetCorner(edge2.Key.Corner2) - e1c1;
                        // Are lines collinear
                        if (Int2.Dot(e1n, e2c1l) != 0 || Int2.Dot(e1n, e2c2l) != 0) continue;
                        var e2c1dp = (int)Int2.Dot(e1d, e2c1l);
                        var e2c2dp = (int)Int2.Dot(e1d, e2c2l);
                        var sign = e2c1dp < e2c2dp;
                        var e2t1 = edge2.Value.Type1;
                        var e2t2 = edge2.Value.Type2;
                        if (!sign) {
                            Swap(ref e2t1, ref e2t2);
                            Swap(ref e2c1l, ref e2c2l);
                            Swap(ref e2c1dp, ref e2c2dp);
                        }
                        var c1Match = e2c1dp == isect.X;
                        var c2Match = e2c2dp == isect.Y;
                        if (e1t1.Equals(e2t1) && e1t2.Equals(e2t2)) {
                            //if (c1Match) containedCorners.AddUnique(edge2.Key.Corner1);
                            //if (c1Match) containedCorners.AddUnique(edge2.Key.Corner2);
                            // Fully overlaps
                            if (c1Match && c2Match) {
                                // Keep existing pin
                                rsect.X = rsect.Y;
                                it2.RemoveSelf(ref edgeValues);
                                break;
                            }
                            // Can only match at edges
                            c1Match &= hasExtraMin;
                            c2Match &= hasExtraMax;
                            if (c1Match || c2Match) {
                                // Any match means reuse
                                it2.RemoveSelf(ref edgeValues);
                                // Consume into split edge
                                if (c1Match) rsect.X = e2c2dp;
                                if (c2Match) rsect.Y = e2c1dp;
                            }
                            // Edges cannot merge - they dont share an endpoint
                        } else {
                            // Do nothing? Edge will get cut later
                        }
                    }
                    if (rsect.X == rsect.Y) {
                        // Preserve existing pin
                        it1.RemoveSelf(ref perimeterEdges.AsMutable());
                        confirmedEdges.Add(edge1);
                        continue;
                    }
                    it1.RemoveSelf(ref perimeterEdges.AsMutable());
                    Mutator.UnpinEdge_NoRepair(edge1);
                    //containedCorners.AddUnique(edge1.Corner1);
                    //containedCorners.AddUnique(edge1.Corner2);
                    if (rsect.X != 0) {
                        // Segment exists on min end
                        var end = Coordinate.FromInt2(e1c1 + (e1c2 - e1c1) * rsect.X / e1l2);
                        var edge = new Edge(edge1.Corner1, Mutator.NavBaker.RequireVertexId(end));
                        var sign = edge.GetSign(edge1.Corner1);
                        edgeValues.Add(edge, new() {
                            Type1 = sign ? e1t1 : e1t2,
                            Type2 = sign ? e1t2 : e1t1,
                        });
                    }
                    if (rsect.Y != e1l2) {
                        // Segment exists on min end
                        var end = Coordinate.FromInt2(e1c1 + (e1c2 - e1c1) * rsect.Y / e1l2);
                        var edge = new Edge(edge1.Corner2, Mutator.NavBaker.RequireVertexId(end));
                        var sign = !edge.GetSign(edge1.Corner2);
                        edgeValues.Add(edge, new() {
                            Type1 = sign ? e1t1 : e1t2,
                            Type2 = sign ? e1t2 : e1t1,
                        });
                    }
                }
                // Unpin any non-common fully contained edges
                foreach (var edge in containedEdges) {
                    if (!edgeValues.ContainsKey(edge)) {
                        Mutator.UnpinEdge_NoRepair(edge);
                        //containedCorners.AddUnique(edge.Corner1);
                        //containedCorners.AddUnique(edge.Corner2);
                    }
                }
                //Debug.Assert(edgeValues.Count == 4);
                // Dont remove corners that are still required
                foreach (var edge in edgeValues) {
                    containedCorners.Remove(edge.Key.Corner1);
                    containedCorners.Remove(edge.Key.Corner2);
                }
                foreach (var edge in perimeterEdges) {
                    containedCorners.Remove(edge.Corner1);
                    containedCorners.Remove(edge.Corner2);
                }
                foreach (var edge in confirmedEdges) {
                    containedCorners.Remove(edge.Corner1);
                    containedCorners.Remove(edge.Corner2);
                }
                // Remove unrequired corners
                foreach (var corner in containedCorners) {
                    Mutator.CreateVertexMutator().RemoveVertex(corner);
                }
                /* Edges that already exist: Ignored
                 * Old edges that should not exist: Removed
                 * New edges that need to exist: Added
                 */
                // Require pinned edges
                foreach (var edge in edgeValues) {
                    Mutator.PinEdge(edge.Key.Corner1, edge.Key.Corner2);
                }
                // Ensure types are correct
                using (var marker = new ProfilerMarker("NavMesh Set Type").Auto()) {
                    int count = 0;
                    foreach (var edgeKV in edgeValues) {
                        var edge = edgeKV.Key;
                        var types = edgeKV.Value;
                        Mutator.SetTriangleTypeByEdge(edge.Corner1, edge.Corner2, types.Type1, true);
                        Mutator.SetTriangleTypeByEdge(edge.Corner2, edge.Corner1, types.Type2, true);
                        ++count;
                    }
                }
                Mutator.GarbageCollect();
                return;
            } else {
                Enable = true;
            }
            /*using (var marker = new ProfilerMarker("NavMesh Triangulate").Auto()) {
                var cornerMutator = Mutator.CreateVertexMutator();
                for (int d = 0; d < 2; d++) {
                    var swizDir = Directions[d];
                    var swizSize = Range.Size;
                    var swizMin = Range.Min;
                    if (d == 1) { swizSize = swizSize.YX; swizMin = swizMin.YX; }
                    for (int iy = 1; iy < swizSize.Y; iy++) {
                        for (int ix = 0; ix < swizSize.X; ix++) {
                            var pnt0 = swizMin + new Int2(ix, iy - 1);
                            var pnt1 = swizMin + new Int2(ix, iy);
                            if (d == 1) { pnt0 = pnt0.YX; pnt1 = pnt1.YX; }
                            var pntT0 = ConvertTypeId(map[pnt0.X + pnt0.Y * Size.X]);
                            var pntT1 = ConvertTypeId(map[pnt1.X + pnt1.Y * Size.X]);
                            if (pntT0 == pntT1) continue;
                            int c = 1;
                            var nxt0 = pnt0;
                            var nxt1 = pnt1;
                            for (; ix + c < swizSize.X; c++) {
                                nxt0 += swizDir;
                                nxt1 += swizDir;
                                var nxtT0 = ConvertTypeId(map[nxt0.X + nxt0.Y * Size.X]);
                                var nxtT1 = ConvertTypeId(map[nxt1.X + nxt1.Y * Size.X]);
                                if (nxtT0 != pntT0) break;
                                if (nxtT1 != pntT1) break;
                            }
                            var coord0 = Int2.Clamp((pnt1 + swizDir * 0) * Coordinate.Granularity / Granularity, 0, meshSize);
                            var coord1 = Int2.Clamp((pnt1 + swizDir * c) * Coordinate.Granularity / Granularity, 0, meshSize);
                            var c0 = cornerMutator.RequireVertexId(Coordinate.FromInt2(coord0));
                            var c1 = cornerMutator.RequireVertexId(Coordinate.FromInt2(coord1));
                            containedCorners.Remove(c0);
                            containedCorners.Remove(c1);
                            var edge = new Edge(c0, c1);
                            if (!Mutator.PinEdge(edge.Corner1, edge.Corner2)) return;
                            if (edge.GetSign(c0) == (d != 0)) { var t = pntT0; pntT0 = pntT1; pntT1 = t; }
                            var adjacency = new AdjacencyIds() {
                                Type1 = new TriangleType(pntT0),
                                Type2 = new TriangleType(pntT1),
                            };
                            edgeValues.Add(edge, adjacency);
                            ix += c - 1;
                        }
                    }
                }
            }*/
            foreach (var edge in edgeValues) {
                if (!Mutator.PinEdge(edge.Key.Corner1, edge.Key.Corner2)) return;
            }
            using (var marker = new ProfilerMarker("NavMesh Vert Prune").Auto()) {
                var cornerMutator = Mutator.CreateVertexMutator();
                cornerMutator.FilterPinnedVertices(ref containedCorners.AsMutable());
                foreach (var cornerId in containedCorners) {
                    cornerMutator.RemoveVertex(cornerId);
                }
            }
            using (var marker = new ProfilerMarker("NavMesh Repair").Auto()) {
                Mutator.RepairSwap();
            }
            using (var marker = new ProfilerMarker("NavMesh Set Type").Auto()) {
                int count = 0;
                foreach (var edgeKV in edgeValues) {
                    var edge = edgeKV.Key;
                    var types = edgeKV.Value;
                    Mutator.SetTriangleTypeByEdge(edge.Corner1, edge.Corner2, types.Type1, true);
                    Mutator.SetTriangleTypeByEdge(edge.Corner2, edge.Corner1, types.Type2, true);
                    ++count;
                    //if (count > Threshold) break;
                }
            }
            edgeValues.Dispose();
        }

        private void Swap<T>(ref T v1, ref T v2) {
            var t = v1;
            v1 = v2;
            v2 = t;
        }

        private Int2 Intersect(Int2 from, Int2 to, RectI rectI) {
            int min = int.MinValue;
            int max = int.MaxValue;
            var delta = to - from;
            var dLen2 = delta.LengthSquared;
            if (delta.X != 0) {
                min = ((delta.X > 0 ? rectI.Min.X : rectI.Max.X) - from.X) * dLen2 / delta.X;
                max = ((delta.X > 0 ? rectI.Max.X : rectI.Min.X) - from.X) * dLen2 / delta.X;
            }
            if (delta.Y != 0) {
                min = Math.Max(min, ((delta.Y > 0 ? rectI.Min.Y : rectI.Max.Y) - from.Y) * dLen2 / delta.Y);
                max = Math.Min(max, ((delta.Y > 0 ? rectI.Max.Y : rectI.Min.Y) - from.Y) * dLen2 / delta.Y);
            }
            return new(min, max);
        }
    }
    private static ulong ComputeHash(ulong i, byte typeId) {
        ulong hash = i * 1234567;
        hash *= (ulong)typeId + 1;
        return hash;
    }
    private static byte ConvertTypeId(byte typeId) {
        if ((typeId & (byte)LandscapeModes.Mask) != 0) typeId = (byte)(typeId & (byte)LandscapeModes.Mask);
        else typeId = (byte)(typeId != 0 ? 0 : 1);
        return typeId;
    }
    AdjacencyPushJob pushJob;
    public void PushToNavMesh(NavMesh2Baker navMesh) {
        if (!HasChanges) return;

        using var marker = new ProfilerMarker("Updating navmesh").Auto();
        var pushJob = new AdjacencyPushJob() {
            map = map,
            Size = Size,
            Range = RectI.FromMinMax(Int2.Max(changeMin, 0), changeMax + 1),
            Mutator = new NavMesh2Baker.Mutator(navMesh),
            Enable = Input.IsInitialized && Input.GetKeyDown(KeyCode.T),
            //Threshold = (int)Input.mousePosition.x,
        };

        // If full rebuild
        if ((pushJob.Range.ContainsInclusive(Size - 1) && pushJob.Range.ContainsInclusive(0))) {
            navMesh.Clear();
            var size = Size * Coordinate.Granularity / Granularity;
            navMesh.InsertRectangle(new RectI(0, 0, size.X, size.Y), new TriangleType() { TypeId = 1, });
        }
        pushJob.Execute();

        if (pushJob.Enable) {
            changeMin = int.MaxValue;
            changeMax = int.MinValue;
            pushJob = default;
        }
        this.pushJob = pushJob;
    }

    public void DrawGizmos() {
        if (map == null) return;
        /*for (int y = 0; y < Size.Y; y++) {
            for (int x = 0; x < Size.X; x++) {
                var cell = map[x + y * Size.X];
                var pos = (Vector2)GridToSimulation(new Int2(x, y)) / 1024f;
                if (cell != 0) Gizmos.DrawWireCube(new Vector3(pos.X, 0f, pos.Y), new Vector3(0.5f, 0.5f, 0.5f));
            }
        }*/
        if (pushJob.edgeValues.IsCreated) {
            var ro = pushJob.Mutator.CreateReadOnly();
            var min = (Vector2)GridToSimulation(pushJob.Range.Min) / 1024f;
            var max = (Vector2)GridToSimulation(pushJob.Range.Max) / 1024f;
            var ctr = (min + max) / 2.0f - new Vector2(1f, 1f) * 0.25f;
            var siz = (max - min);
            Gizmos.DrawWireCube(
                new(ctr.X, 0f, ctr.Y),
                new(siz.X, 0f, siz.Y),
                Color.Red
            );
            foreach (var edge in pushJob.edgeValues) {
                var c1 = ro.GetCorner(edge.Key.Corner1).ToUVector3(0f);
                var c2 = ro.GetCorner(edge.Key.Corner2).ToUVector3(0f);
                Handles.DrawLine(c1, c2, Color.Blue);
            }
        }
    }

}
/*
                            // Touching one of the ends (and there is extra on that end)
                            if ((c1Match && hasExtraMin) || (c2Match && hasExtraMax)) {
                                it2.RemoveSelf(ref edgeValues);
                                it1.RemoveSelf(ref perimeterEdges.AsMutable());
                                var nec0 = c1Match ? edge2.Key.GetCorner(sign) : edge1.Corner1;
                                var nec1 = c1Match ? edge1.Corner2 : edge2.Key.GetCorner(!sign);
                                var oc = edge2.Key.GetCorner(sign != c1Match);
                                //previousCorners.AddUnique(oc);
                                var newEdge = new Edge(nec0, nec1);
                                var newValue = edge2.Value;
                                if (sign != newEdge.GetSign(nec0)) {
                                    Swap(ref newValue.Type1, ref newValue.Type2);
                                }
                                edgeValues.Add(newEdge, newValue);
                                break;
                            }

#if false
                for (var it1 = perimeterEdges.GetEnumerator(); it1.MoveNext();) {
                    var edge1 = it1.Current;
                    var e1c1 = (Int2)Mutator.NavBaker.GetCorner(edge1.Corner1);
                    var e1c2 = (Int2)Mutator.NavBaker.GetCorner(edge1.Corner2);
                    var isect = Intersect(e1c1, e1c2, navBounds);
                    // Didnt intersect
                    if (isect.Y <= isect.X) continue;
                    {
                        var delta = e1c2 - e1c1;
                        var deltaL2 = delta.LengthSquared;
                        isect.X = Math.Max(isect.X, 0);
                        isect.Y = Math.Min(isect.Y, deltaL2);
                        if (isect.X > 0) {
                            e1c2 = e1c1 + delta * isect.Y / deltaL2;
                            e1c1 = e1c1 + delta * isect.X / deltaL2;
                        }
                    }
                    var e1d = e1c2 - e1c1;
                    var e1l2 = e1d.LengthSquared;
                    // This edge starts on the boundary - always keep it?
                    if (e1l2 == 0) {
                        it1.RemoveSelf(ref perimeterEdges.AsMutable());
                        confirmedEdges.Add(edge1);
                        continue;
                    }
                    var e1n = new Int2(e1d.Y, -e1d.X);
                    for (var it2 = edgeValues.GetEnumerator(); it2.MoveNext();) {
                        var edge2 = it2.Current;
                        var e2c1l = (Int2)Mutator.NavBaker.GetCorner(edge2.Key.Corner1) - e1c1;
                        var e2c2l = (Int2)Mutator.NavBaker.GetCorner(edge2.Key.Corner2) - e1c1;
                        // Are lines collinear
                        if (Int2.Dot(e1n, e2c1l) != 0 || Int2.Dot(e1n, e2c2l) != 0) continue;
                        var sign = Int2.Dot(e1d, e2c1l) < Int2.Dot(e1d, e2c2l);
                        Trace.Assert(aj.TryGetValue(edge1, out var e1aj));
                        var e1t1 = ro.GetTriangle(e1aj.Triangle1).Type;
                        var e1t2 = ro.GetTriangle(e1aj.Triangle2).Type;
                        if (sign) { var t = e1t1; e1t1 = e1t2; e1t2 = t; }
                        if (!e1t1.Equals(edge2.Value.Type1) || !e1t2.Equals(edge2.Value.Type2)) continue;

                        if (!sign) { var t = e2c1l; e2c1l = e2c2l; e2c2l = t; }
                        //var e2d = e2c2l - e2c1l;
                        //var e2l2 = e2d.LengthSquared;
                        //var proj1 = Int2.Dot(e2c1l, e1d);
                        //var proj2 = Int2.Dot(e2c2l, e1d);
                        var c1Match = e2c1l == Int2.Zero;
                        var c2Match = e2c2l == e1d;
                        // Fully overlaps
                        if (c1Match && c2Match) {
                            // Keep existing pin
                            it2.RemoveSelf(ref edgeValues);
                            it1.RemoveSelf(ref perimeterEdges.AsMutable());
                            //previousCorners.AddUnique(edge2.Key.Corner1);
                            //previousCorners.AddUnique(edge2.Key.Corner2);
                            confirmedEdges.Add(edge1);
                            break;
                        }
                        if (c1Match || c2Match) {
                            it2.RemoveSelf(ref edgeValues);
                            it1.RemoveSelf(ref perimeterEdges.AsMutable());
                            var nec0 = c1Match ? edge2.Key.GetCorner(sign) : edge1.Corner1;
                            var nec1 = c1Match ? edge1.Corner2 : edge2.Key.GetCorner(!sign);
                            var oc = edge2.Key.GetCorner(sign != c1Match);
                            //previousCorners.AddUnique(oc);
                            var newEdge = new Edge(nec0, nec1);
                            var newValue = edge2.Value;
                            if (sign != newEdge.GetSign(nec0)) {
                                var t = newValue.Type1; newValue.Type1 = newValue.Type2; newValue.Type2 = t;
                            }
                            edgeValues.Add(newEdge, newValue);
                            break;
                        }
                        continue;
                        // Didnt intersect
                        //var oisect = Intersect(e2c1l, e2c2l, navBounds);
                        //if (oisect.Y < 0 && oisect.X > e2d.LengthSquared) continue;

                        //it2.RemoveSelf(ref edgeValues);
                        //break;
                    }

                }
#endif
*/