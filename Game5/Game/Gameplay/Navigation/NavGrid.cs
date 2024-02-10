using Navigation;
using System;
using System.Collections;
using System.Collections.Generic;
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

    public void Allocate(Int2 size) {
        Size = size;
        map = new byte[Size.X * Size.Y];
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
            var yPos = gridIt.GridToSimulationY(y);
            gridIt.ComputeXRange(polygon, yPos, ref xCoords.AsMutable());
            for (int iX = 0; iX < xCoords.Count; iX += 2) {
                var xMin = xCoords[iX] + y * Size.X;
                var xMax = xCoords[iX + 1] + y * Size.X;
                for (int x = xMin; x < xMax; x++) {
                    map[x] = (byte)(map[x] + delta);
                }
            }
            xCoords.Clear();
        }
    }
    public Accessor GetAccessor() {
        return new Accessor(Size, map);
    }

    public struct Accessor {
        public Int2 Size;
        public byte[] Map;
        public Accessor(Int2 size, byte[] map) {
            Size = size;
            Map = map;
        }
        public void SetLandscapePassable(Int2 pnt, LandscapeModes mode) {
            int i = pnt.X + pnt.Y * Size.X;
            var cell = Map[i];
            cell = (byte)((cell & ~(byte)LandscapeModes.Mask) | (byte)mode);
            Map[i] = cell;
        }
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
        public byte[] map;
        public NavMesh2Baker.Mutator Mutator;
        //public int Threshold;
        public void Execute() {
            Span<Int2> Directions = stackalloc Int2[] { new Int2(1, 0), new Int2(0, 1), new Int2(-1, 0), new Int2(0, -1), };
            var edgeValues = new PooledHashMap<Edge, AdjacencyIds>(64);
            var size = Size * Coordinate.Granularity / Granularity;
            using (var marker = new ProfilerMarker("Triangulate").Auto()) {
                var mutator = Mutator;
                var cornerMutator = mutator.CreateVertexMutator();
                for (int d = 0; d < 2; d++) {
                    var dir = Directions[d];
                    for (int iy = 1; iy < Size.Y; iy++) {
                        for (int ix = 0; ix < Size.X; ix++) {
                            var pnt0 = new Int2(ix, iy - 1);
                            var pnt1 = new Int2(ix, iy);
                            if (d == 1) { pnt0 = pnt0.YX; pnt1 = pnt1.YX; }
                            var pntT0 = ConvertTypeId(map[pnt0.X + pnt0.Y * Size.X]);
                            var pntT1 = ConvertTypeId(map[pnt1.X + pnt1.Y * Size.X]);
                            if (pntT0 == pntT1) continue;
                            int c = 1;
                            int cMax = checked((int)Int2.Dot(dir, Size - pnt0));
                            var nxt0 = pnt0;
                            var nxt1 = pnt1;
                            for (; c < cMax; c++) {
                                nxt0 += dir;
                                nxt1 += dir;
                                var nxtT0 = ConvertTypeId(map[nxt0.X + nxt0.Y * Size.X]);
                                var nxtT1 = ConvertTypeId(map[nxt1.X + nxt1.Y * Size.X]);
                                if (nxtT0 != pntT0) break;
                                if (nxtT1 != pntT1) break;
                            }
                            var coord0 = Int2.Clamp((pnt1 + dir * 0) * Coordinate.Granularity / Granularity, 0, size);
                            var coord1 = Int2.Clamp((pnt1 + dir * c) * Coordinate.Granularity / Granularity, 0, size);
                            var c0 = cornerMutator.RequireVertexId(Coordinate.FromInt2(coord0));
                            var c1 = cornerMutator.RequireVertexId(Coordinate.FromInt2(coord1));
                            var edge = new Edge(c0, c1);
                            if (!mutator.PinEdge(edge.Corner1, edge.Corner2)) return;
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
            using (var marker = new ProfilerMarker("Repair").Auto()) {
                var mutator = Mutator;
                mutator.RepairSwap();
            }
            using (var marker = new ProfilerMarker("Set Type").Auto()) {
                var mutator = Mutator;
                int count = 0;
                foreach (var edgeKV in edgeValues) {
                    var edge = edgeKV.Key;
                    var types = edgeKV.Value;
                    mutator.SetTriangleTypeByEdge(edge.Corner1, edge.Corner2, types.Type1, true);
                    mutator.SetTriangleTypeByEdge(edge.Corner2, edge.Corner1, types.Type2, true);
                    ++count;
                    //if (count > Threshold) break;
                }
            }
        }

        private byte ConvertTypeId(byte typeId) {
            if ((typeId & (byte)LandscapeModes.Mask) != 0) typeId = (byte)(typeId & (byte)LandscapeModes.Mask);
            else typeId = (byte)(typeId != 0 ? 0 : 1);
            return typeId;
        }
    }
    public void PushToNavMesh(NavMesh2Baker navMesh) {
        new ProfilerMarker("Updating navmesh").Auto();
        navMesh.Clear();
        var size = Size * Coordinate.Granularity / Granularity;
        navMesh.InsertRectangle(new RectI(0, 0, size.X, size.Y), new TriangleType() { TypeId = 1, });

        var pushJob = new AdjacencyPushJob() {
            map = map,
            Size = Size,
            Mutator = new NavMesh2Baker.Mutator(navMesh),
            //Threshold = (int)Input.mousePosition.x,
        };
        pushJob.Execute();

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
