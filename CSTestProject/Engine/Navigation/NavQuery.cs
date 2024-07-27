using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Navigation {
    public struct NavQuery {

        public NavMesh.ReadOnly ReadOnly { get; private set; }
        public NavMesh.ReadAdjacency NavAdjacency { get; private set; }

        private struct Hop {
            public Coordinate Position;
            public TriangleEdge Adjacent;
            public ushort Cost;
            public Hop(Hop other) { Position = default; Adjacent = other.Adjacent; Cost = other.Cost; }
            public static readonly Hop Destination = new Hop() { Cost = 0, Adjacent = TriangleEdge.Invalid, };
            public static readonly Hop Invalid = new Hop() { Cost = ushort.MaxValue, Adjacent = TriangleEdge.Invalid, };
        }

        private PooledPriorityQueue<TriangleEdge, ushort> queue;
        private Dictionary<TriangleEdge, Hop> next;

        private struct WorkingData {
            public Int2 From;
            public Int2 To;
            public ushort FromTri;
            //public ushort ToTri;
            public ushort BestCost;
            public ushort BestDst;
            public Int2 BestPos;
            public TriangleEdge BestEdge;
            public byte NavMask;
        }
        private WorkingData working;

        public bool CanReachTarget => working.BestEdge.EdgeId == ushort.MaxValue;
        public Int2 NearestHop => working.BestPos;
        public ushort NearestTri => working.BestEdge.TriangleId;

        public void Initialise(NavMesh navMesh) {
            ReadOnly = navMesh.GetReadOnly();
            NavAdjacency = navMesh.GetAdjacency();

            if (!queue.IsCreated) queue = new(64);
            if (next == null) next = new(512);
        }

        public bool ComputePath(Int2 from, Int2 to, byte navmask, ref PooledList<TriangleEdge> portals) {
            portals.Clear();
            working.From = Coordinate.FromInt2(from);
            working.To = Coordinate.FromInt2(to);
            working.NavMask = navmask;
            from = Int2.Max(from, 0);
            to = Int2.Max(to, 0);
            working.FromTri = NavAdjacency.FindNearestPathable(ReadOnly, working.NavMask, from);
            if (working.FromTri == NavMesh.InvalidTriId) {
                return false;
            }
            //working.ToTri = NavAdjacency.GetTriangleAt(NavMesh, Coordinate.FromInt2(to));
            /*if (!IsPathable(working.FromTri)) {
                var nearest = FindNearestPathableTriangle(working.FromTri, from);
                if (nearest != -1) working.FromTri = (ushort)nearest;
            }*/
            /*if (working.FromTri == working.ToTri && IsPathable(working.FromTri)) {
                working.BestEdge = new TriangleEdge(working.ToTri, ushort.MaxValue);
                return true;
            }*/
            if (ReadOnly.GetTriangleContains(working.FromTri, to) && IsPathable(working.FromTri)) {
                working.BestEdge = new TriangleEdge(working.FromTri, ushort.MaxValue);
                return true;
            }
            working.BestCost = Hop.Invalid.Cost;
            working.BestDst = ushort.MaxValue;
            working.BestPos = default;
            ProcessTriangle(working.FromTri, new Hop(Hop.Destination) {
                Position = Coordinate.FromInt2(from),
            });
            for (var t = 0; t < 1000 && queue.Count != 0; ++t) {
                var topKey = queue.PeekKey();
                if (working.BestDst == 0 && working.BestCost <= topKey) break;
                var tTop = queue.Dequeue();
                ProcessEdge(tTop, next[tTop]);
            }
            if (working.BestCost == Hop.Invalid.Cost) return false;
            using (new ProfilerMarker("Computing portals").Auto()) {
                for (var portal = working.BestEdge; ;) {
                    if (portals.Count > 1024) {
                        Debug.WriteLine("Something went wrong!");
                        break;
                    }
                    if (portal.TriangleId == working.FromTri) break;

                    //NavAdjacency.GetAdjacentEdge(portal, NavMesh)
                    if (!next.TryGetValue(portal, out var portalHop)) {
                        Debug.WriteLine("Portal not found");
                        break;
                    }
                    portal = portalHop.Adjacent;
                    if (!portal.IsValid) break;
                    Debug.Assert(portal.EdgeId != ushort.MaxValue,
                        "Should not visit a non-edge portal");
                    portals.Add(portal);
                }
                // External systems expect portals in forward order
                // TODO: Perhaps make systems work with inverse ordered portals?
                for (int i = 0; i < portals.Count / 2; i++) {
                    var t = portals[i];
                    portals[i] = portals[portals.Count - i - 1];
                    portals[portals.Count - i - 1] = t;
                }
            }
            queue.Clear();
            next.Clear();
            return true;
        }

        private bool IsPathable(ushort triI) {
            return (ReadOnly.GetTriangle(triI).Type.TypeId & working.NavMask) == working.NavMask;
        }

        private void ProcessTriangle(ushort tTop, in Hop topHop) {
            for (int e = 0; e < 3; e++) {
                var edge = new TriangleEdge(tTop, (ushort)e);
                var edgeHop = GetHop(topHop, edge);
                next[edge] = edgeHop;
                var totalCost = ComputeHeuristicCost(edgeHop);
                queue.Enqueue(edge, totalCost);
                ObserveEdge(edge, edgeHop);
            }
        }

        private Int2 GetNearestPosition(TriangleEdge edge, Int2 position) {
            return ReadOnly.GetNearestPointOnEdge(edge, position);
        }
        private Hop GetHop(in Hop topHop, TriangleEdge edge) {
            var edgeHop = topHop;
            edgeHop.Position = Coordinate.FromInt2(GetNearestPosition(edge, topHop.Position));
            edgeHop.Cost += (ushort)NavUtility.Distance(topHop.Position, edgeHop.Position);
            return edgeHop;
        }
        private ushort ComputeHeuristicCost(in Hop hop) {
            var est = NavUtility.Distance(hop.Position, working.To);
            return (ushort)(hop.Cost + est);
        }

        private void ProcessEdge(TriangleEdge inEdge, Hop topHop) {
            // The edge that we are propagating from (adjacent to inEdge)
            var fromEdge = NavAdjacency.GetAdjacentEdge(inEdge, ReadOnly);
            if (!fromEdge.IsValid) return;

            // We can reach this edge
            //if (ObserveEdge(fromEdge, topHop, inEdge)) return;
            if (IsPathable(inEdge.TriangleId))
                ObserveEdge(inEdge, topHop);

            // Check that tri is pathable
            if (!IsPathable(fromEdge.TriangleId)
                /*&& topAdj.TriangleId != working.ToTri*/) return;

            if (ReadOnly.GetTriangleContains(fromEdge.TriangleId, working.To)) {
                var triCtr = new TriangleEdge(fromEdge.TriangleId, ushort.MaxValue);
                if (ObserveEdge(triCtr, topHop)) {
                    next[triCtr] = new Hop() {
                        Adjacent = inEdge,
                    };
                }
                return;
            }

            for (int e = 1; e < 3; e++) {
                var tNext = fromEdge;
                tNext.EdgeId = (ushort)((tNext.EdgeId + e) % 3);

                // Calculate distance for cost function
                var nxtPos = GetNearestPosition(tNext, topHop.Position);
                var dst = NavUtility.Distance(topHop.Position, nxtPos);
                var cost = (ushort)(topHop.Cost + dst);
                // Compare cost with existing
                if (next.TryGetValue(tNext, out var nextHop)) {
                    if (cost >= nextHop.Cost) return;
                }
                // Compute heuristic
                nextHop = new Hop() {
                    Cost = cost,
                    Adjacent = inEdge,
                    Position = Coordinate.FromInt2(nxtPos),
                };
                next[tNext] = nextHop;
                var totalCost = ComputeHeuristicCost(nextHop);
                queue.Assign(tNext, totalCost);
            }
        }

        private bool ObserveEdge(TriangleEdge edge, Hop hop) {
            //if (edge.TriangleId == working.ToTri) edge.EdgeId = ushort.MaxValue;
            var dstToTarget = edge.EdgeId == ushort.MaxValue ? 0
                : NavUtility.Distance(hop.Position, working.To);
            bool replace = dstToTarget > working.BestDst ? false
                : dstToTarget < working.BestDst || hop.Cost < working.BestCost;
            if (replace) {
                working.BestCost = hop.Cost;
                working.BestEdge = edge;
                working.BestDst = (ushort)dstToTarget;
                working.BestPos = hop.Position;
                return true;
            }
            return false;
        }
    }

    public ref struct NavPortalFunnel {
        public Span<TriangleEdge> Portals { get; private set; }
        public NavMesh.ReadOnly NavMesh { get; private set; }
        public int Index;
        public bool IsEnded => Index > Portals.Length;
        public NavPortalFunnel(Span<TriangleEdge> portals, NavMesh.ReadOnly navMesh) {
            Portals = portals;
            NavMesh = navMesh;
            Index = 0;
        }
        public Int2 FindNextNode(Int2 from, Int2 to) {
            Int2 corner = 0;
            Int2 left = 0, right = 0;
            for (; Index <= Portals.Length; Index++) {
                Int2 vl, vr;
                if (Index >= Portals.Length) {
                    vl = vr = to - from;
                } else {
                    var portal = Portals[Index];
                    var tri = NavMesh.GetTriangle(portal.TriangleId);
                    var c0 = tri.GetCorner(portal.EdgeId);
                    var c1 = tri.GetCornerWrapped(portal.EdgeId + 1);

                    vl = NavMesh.GetCorner(c0) - from;
                    vr = NavMesh.GetCorner(c1) - from;
                    vl = AppendRadius(vl, 10);
                    vr = AppendRadius(vr, -10);
                }

                // Terminate funnel by turning
                if (Perp2D(left, vr) < 0) {
                    corner = left;
                    break;
                }
                if (Perp2D(right, vl) > 0) {
                    corner = right;
                    break;
                }

                // Narrow funnel
                if (Perp2D(left, vl) >= 0) {
                    left = vl;
                }
                if (Perp2D(right, vr) <= 0) {
                    right = vr;
                }
            }
            if (corner.Equals(default)) return default;
            return from + corner;
        }

        private Int2 AppendRadius(Int2 delta, int radius) {
            var right = new Int2(delta.Y, -delta.X);
            int rgtLen = right.LengthI;
            if (rgtLen > 0) delta += right * radius / rgtLen;
            return delta;
        }

        public static long Perp2D(Int2 u, Int2 v) { return u.Y * v.X - u.X * v.Y; }
        public static void Swap<T>(ref T a, ref T b) { var t = a; a = b; b = t; }
    }
}
