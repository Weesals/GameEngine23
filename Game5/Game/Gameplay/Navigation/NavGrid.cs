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

    private struct AdjacencyIds {
        public TriangleType Type1, Type2;
    }
    public struct AdjacencyPushJob {
        public Int2 Size;
        public RectI Range;
        public byte[] map;
        public NavMesh2Baker.Mutator Mutator;
        //public int Threshold;
        public void Execute() {
            Span<Int2> Directions = stackalloc Int2[] { new Int2(1, 0), new Int2(0, 1), new Int2(-1, 0), new Int2(0, -1), };
            var edgeValues = new PooledHashMap<Edge, AdjacencyIds>(64);
            var meshSize = Size * Coordinate.Granularity / Granularity;
            using var previousCorners = new PooledHashSet<ushort>(16);
            if (Range.Size.X < Size.X || Range.Size.Y < Size.Y) {
                using (var marker = new ProfilerMarker("NavMesh Prune").Auto()) {
                    Span<Coordinate> path = stackalloc Coordinate[4];
                    var navMin = Range.Min * Coordinate.Granularity / Granularity;
                    var navMax = Range.Max * Coordinate.Granularity / Granularity;
                    path[0] = Coordinate.FromInt2(navMin);
                    path[1] = Coordinate.FromInt2(new Int2(navMin.X, navMax.Y));
                    path[2] = Coordinate.FromInt2(navMax);
                    path[3] = Coordinate.FromInt2(new Int2(navMax.X, navMin.Y));
                    var it = new NavMesh.PolygonIntersectEnumerator(
                        Mutator.NavBaker.NavMesh, path);
                    it.PrimeClosedLoop();
                    using var touchedEdges = new PooledHashSet<Edge>(16);
                    using var workingEdges = new PooledHashSet<Edge>(16);
                    var ro = Mutator.CreateReadOnly();
                    while (it.MoveNext()) {
                        var triEdge = it.TriangleEdge;
                        var tri = ro.GetTriangle(triEdge.TriangleId);
                        var c1 = tri.GetCorner(triEdge.EdgeId);
                        for (int i = 0; i < 3; ++i) {
                            var c2 = tri.GetCornerWrapped(triEdge.EdgeId + 1);
                            var p1 = (Int2)ro.GetCorner(c1) * Granularity / Coordinate.Granularity;
                            var p2 = (Int2)ro.GetCorner(c2) * Granularity / Coordinate.Granularity;
                            if (Range.Contains(p1)) previousCorners.AddUnique(c1);
                            if (Range.Contains(p2)) previousCorners.AddUnique(c2);
                            if (Range.Contains(p1) && Range.Contains(p2)) {
                                var edge = new Edge(c1, c2);
                                touchedEdges.Add(edge);
                                workingEdges.ToggleUnique(edge);
                            }
                            c1 = c2;
                            triEdge.EdgeId++;
                        }
                    }
                    var aj = Mutator.NavBaker.NavMesh.GetAdjacency();
                    while (workingEdges.TryPop(out var item)) {
                        Mutator.UnpinEdge_NoRepair(item);
                        Trace.Assert(aj.TryGetValue(item, out var edgeAdj));
                        for (int t = 0; t < 2; t++) {
                            var triId = t == 0 ? edgeAdj.Triangle1 : edgeAdj.Triangle2;
                            if (triId == NavMesh.InvalidTriId) continue;
                            var tri = ro.GetTriangle(triId);
                            for (int i = 0; i < 3; i++) {
                                var edge = tri.GetEdgeWrapped(i + 1);
                                if (touchedEdges.Contains(edge)) continue;
                                var p1 = (Int2)ro.GetCorner(edge.Corner1) * Granularity / Coordinate.Granularity;
                                var p2 = (Int2)ro.GetCorner(edge.Corner2) * Granularity / Coordinate.Granularity;
                                if (Range.Contains(p1)) previousCorners.AddUnique(edge.Corner1);
                                if (Range.Contains(p2)) previousCorners.AddUnique(edge.Corner2);
                                if (!Range.Contains(p1) || !Range.Contains(p2)) continue;

                                touchedEdges.Add(edge);
                                workingEdges.Add(edge);
                            }
                        }
                    }
                }
            }
            using (var marker = new ProfilerMarker("NavMesh Triangulate").Auto()) {
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
                            previousCorners.Remove(c0);
                            previousCorners.Remove(c1);
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
            }
            using (var marker = new ProfilerMarker("NavMesh Vert Prune").Auto()) {
                var cornerMutator = Mutator.CreateVertexMutator();
                foreach (var cornerId in previousCorners) {
                    cornerMutator.RemoveVertex(cornerId);
                }
            }
            using (var marker = new ProfilerMarker("NavMesh Repair").Auto()) {
                //Mutator.RepairSwap();
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
    public void PushToNavMesh(NavMesh2Baker navMesh) {
        new ProfilerMarker("Updating navmesh").Auto();
        var pushJob = new AdjacencyPushJob() {
            map = map,
            Size = Size,
            Range = RectI.FromMinMax(Int2.Max(changeMin - 1, 0), changeMax + 1),
            Mutator = new NavMesh2Baker.Mutator(navMesh),
            //Threshold = (int)Input.mousePosition.x,
        };

        // If full rebuild
        if (true || (pushJob.Range.Contains(pushJob.Size - 1) && pushJob.Range.Contains(0))) {
            navMesh.Clear();
            var size = Size * Coordinate.Granularity / Granularity;
            navMesh.InsertRectangle(new RectI(0, 0, size.X, size.Y), new TriangleType() { TypeId = 1, });
        }
        pushJob.Execute();

        changeMin = int.MaxValue;
        changeMax = int.MinValue;

        /*for (int x = 1; x < Size.x; x++) {
            for (int y = 0; y < Size.y; y++) {
                var pnt0 = new int2(x - 1, y);
                var pnt1 = new int2(x, y);
                var pntT0 = map[pnt0.x + pnt0.y * Size.x];
                var pntT1 = map[pnt1.x + pnt1.y * Size.x];
                if (pntT0 == pntT1) continue;
                int c = 1;
                for (; c < 10; c++) {
                    var nxt0 = pnt0 + new int2(0, c);
                    var nxt1 = pnt1 + new int2(0, c);
                    var nxtT0 = map[nxt0.x + nxt0.y * Size.x];
                    var nxtT1 = map[nxt1.x + nxt1.y * Size.x];
                    if (nxtT0 != pntT0) break;
                    if (nxtT1 != pntT1) break;
                }
                y += c - 1;
                var coord0 = (pnt0 + pnt1 + new int2(0, 0)) * Coordinate.Granularity / 2 / Granularity;
                var coord1 = (pnt0 + pnt1 + new int2(0, c)) * Coordinate.Granularity / 2 / Granularity;
                navMesh.PinEdge(
                    navMesh.RequireVertexId(Coordinate.FromInt2(coord0)),
                    navMesh.RequireVertexId(Coordinate.FromInt2(coord1))
                );
            }
        }*/
        /*for (int y = 1; y < Size.y - 1; y++) {
            for (int x = 1; x < Size.x - 1; x++) {
                var pnt = new int2(x, y);
                var type = map[pnt.x + pnt.y * Size.x];
                //if (type == 0) continue;
                for (int d = 0; d < 2; d++) {
                    var dir = Directions[d];
                    var nrm = new int2(dir.y, -dir.x);
                    int c = 0;
                    for (; c < 10; ++c) {
                        var nxt1 = pnt + nrm * c;
                        var nxt2 = pnt + dir + nrm * c;
                        if (map[nxt1.x + nxt1.y * Size.x] != type) break;
                        if (map[nxt2.x + nxt2.y * Size.x] == type) break;
                    }
                    if (c == 0) continue;
                    var coord0 = (pnt * 2 + dir + nrm) * Coordinate.Granularity / 2 / Granularity;
                    var coord1 = (pnt * 2 + dir - nrm) * Coordinate.Granularity / 2 / Granularity;
                    navMesh.PinEdge(
                        navMesh.RequireVertexId(Coordinate.FromInt2(coord0)),
                        navMesh.RequireVertexId(Coordinate.FromInt2(coord1))
                    );
                }
            }
        }*/
    }

    public void DrawGizmos() {
        if (map == null) return;
        for (int y = 0; y < Size.Y; y++) {
            for (int x = 0; x < Size.X; x++) {
                var cell = map[x + y * Size.X];
                var pos = (Vector2)GridToSimulation(new Int2(x, y)) / 1024f;
                if (cell != 0) Gizmos.DrawWireCube(new Vector3(pos.X, 0f, pos.Y), new Vector3(0.5f, 0.5f, 0.5f));
            }
        }
    }

}
