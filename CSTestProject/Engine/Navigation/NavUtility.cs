using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Weesals.Engine;

namespace Navigation {
    using CornerId = System.UInt16;
    using TriangleId = System.UInt16;

    public interface ICoordinateGranularity {
        int PivotBits { get; }
    }

    public class CoordinateG16 : ICoordinateGranularity {

        public const int PivotBits = 4;
        public const int Granularity = 1 << PivotBits;
        int ICoordinateGranularity.PivotBits => PivotBits;

        public static ushort FromInt(int v) { return (ushort)(v * Granularity); }
        public static ushort FromInt(int v, int g) { return (ushort)(v * Granularity / g); }

        public static ushort FromFloat(float v) { return (ushort)(v * Granularity); }

        public static ushort FromInt<G>(int v, G g) where G : ICoordinateGranularity {
            return (ushort)(v << (PivotBits - g.PivotBits));
        }

        public static float ToFloat(ushort v) {
            return v / (float)Granularity;
        }

    }

    public class CoordinateMath : CoordinateG16 {
    }

    public struct Coordinate : IEquatable<Coordinate> {
        public const int Granularity = CoordinateG16.Granularity;
        public ushort X, Z;
        public Coordinate(ushort x, ushort z) { X = x; Z = z; }
        public bool Equals(Coordinate o) { return X == o.X && Z == o.Z; }
        public Vector2 ToUVector2() {
            return new Vector2(CoordinateMath.ToFloat(X), CoordinateMath.ToFloat(Z));
        }
        public Vector3 ToUVector3(float y) {
            return new Vector3(CoordinateMath.ToFloat(X), y, CoordinateMath.ToFloat(Z));
        }
        public override string ToString() { return ToUVector2().ToString(); }
        public override int GetHashCode() { return X * 49157 + Z; }
        public static implicit operator Int2(Coordinate c) { return new Int2(c.X, c.Z); }
        public static readonly Coordinate Invalid = new Coordinate(ushort.MaxValue, ushort.MaxValue);
        public static Coordinate FromInt2(Int2 pos) { return new Coordinate((ushort)pos.X, (ushort)pos.Y); }
        public static Coordinate FromFloat2(Vector2 pos) { return new Coordinate(CoordinateG16.FromFloat(pos.X), CoordinateG16.FromFloat(pos.Y)); }
    }
    public struct TriangleType : IEquatable<TriangleType> {
        public byte TypeId;
        public byte TypeMask => TypeId;// (byte)(1 << TypeId);
        public TriangleType(byte typeId) { TypeId = typeId; }
        public bool Equals(TriangleType other) { return TypeId == other.TypeId && TypeMask == other.TypeMask; }
        public override int GetHashCode() { return TypeId; }
        public override string ToString() { return TypeId.ToString(); }
    }
    public struct Triangle {
        public const TriangleId InvalidTriId = TriangleId.MaxValue;
        public CornerId C1, C2, C3;
        public TriangleType Type;
        public CornerId GetCorner(int cornerId) { return GetCorner(ref this, cornerId); }
        public void SetCorner(int cornerId, CornerId value) { GetCorner(ref this, cornerId) = value; }
        public static ref CornerId GetCorner(ref Triangle t, int cornerId) {
            switch (cornerId) {
                case 0: return ref t.C1;
                case 1: return ref t.C2;
                case 2: return ref t.C3;
            }
            throw new NotImplementedException();
        }
        public CornerId GetCornerWrapped(int v) { return GetCorner((int)((uint)v % 3)); }
        public int FindCorner(CornerId corner) { return corner == C1 ? 0 : corner == C2 ? 1 : corner == C3 ? 2 : -1; }
        public bool HasCorner(CornerId corner) { return corner == C1 || corner == C2 || corner == C3; }
        public CornerId FindPrevCorner(CornerId corner) { return GetCornerWrapped(FindCorner(corner) + 2); }
        public CornerId FindNextCorner(CornerId corner) { return GetCornerWrapped(FindCorner(corner) + 1); }
        public Edge GetEdge(int i) { return new Edge(GetCorner(i), GetCornerWrapped(i + 1)); }
        public Edge GetEdgeWrapped(int i) { return new Edge(GetCornerWrapped(i), GetCornerWrapped(i + 1)); }
        public override int GetHashCode() { throw new NotImplementedException(); }
    }
    public struct Edge : IEquatable<Edge> {
        public ushort Corner1, Corner2;
        public Edge(ushort edge1, ushort edge2) {
            if (edge1 < edge2) { Corner1 = edge1; Corner2 = edge2; } else { Corner1 = edge2; Corner2 = edge1; }
        }
        public bool GetSign(ushort cornerI) { return Corner1 == cornerI; }
        public bool Equals(Edge o) { return Corner1 == o.Corner1 && Corner2 == o.Corner2; }
        public override int GetHashCode() { return (Corner1 * 49157) + Corner2; }
        public override string ToString() { return Corner1 + "-" + Corner2; }
        public static readonly Edge Invalid = new Edge(ushort.MaxValue, ushort.MaxValue);
    }
    public struct TriangleEdge : IEquatable<TriangleEdge> {
        public const TriangleId InvalidTriId = Triangle.InvalidTriId;
        public TriangleId TriangleId;
        public ushort EdgeId;
        public int AdjacencyIndex => TriangleId * 4 + EdgeId;
        public bool IsValid => TriangleId != InvalidTriId;
        public TriangleEdge(TriangleId triId, ushort edgeId) {
            TriangleId = triId;
            EdgeId = edgeId;
        }
        public static TriangleEdge FromAdjacency(int value) {
            if (value == InvalidTriId) return TriangleEdge.Invalid;
            return new TriangleEdge((ushort)(value >> 2), (ushort)(value & 0x03));
        }
        public TriangleEdge NextEdge() {
            return new TriangleEdge(TriangleId, (ushort)(EdgeId == 2 ? 0 : EdgeId + 1));
        }
        public TriangleEdge PreviousEdge() {
            return new TriangleEdge(TriangleId, (ushort)(EdgeId == 0 ? 2 : EdgeId - 1));
        }
        public bool Equals(TriangleEdge other) {
            return TriangleId == other.TriangleId && EdgeId == other.EdgeId;
        }
        public override string ToString() { return "T" + TriangleId + "[" + EdgeId + "]"; }
        public override int GetHashCode() { return TriangleId * 49157 + EdgeId; }
        public static readonly TriangleEdge Invalid = new TriangleEdge(InvalidTriId, 0);
    }

    public static class NavUtility {

        const int iccerrboundA = 1;

        public struct Behavior {
            public const bool NoExact = true;
        }

        // https://github.com/wo80/Triangle.NET/blob/f70be6a937c4c447b4cee05505d7ee2b75d68e62/src/Triangle/RobustPredicates.cs#L187
        // For clockwise winding, returns positive if pd is a D is inside ABC
        public static long InCircle(Int2 pa, Int2 pb, Int2 pc, Int2 pd) {
            var ad = pa - pd;
            var bd = pb - pd;
            var cd = pc - pd;

            long adlen2 = ad.X * ad.X + ad.Y * ad.Y;
            long bdxcd =  bd.X * cd.Y - cd.X * bd.Y;
            long bdlen2 = bd.X * bd.X + bd.Y * bd.Y;
            long cdxad =  cd.X * ad.Y - ad.X * cd.Y;
            long cdlen2 = cd.X * cd.X + cd.Y * cd.Y;
            long adxbd =  ad.X * bd.Y - bd.X * ad.Y;

            long det = adlen2 * bdxcd
                + bdlen2 * cdxad
                + cdlen2 * adxbd;

            return det;
        }
        public static long GetAreaCW(Int2 pa, Int2 pb, Int2 pc) {
            var e1 = pc - pa;
            var e2 = pb - pa;
            return (long)e1.X * e2.Y - (long)e2.X * e1.Y;
        }
        public static bool IsCCW(Int2 pa, Int2 pb, Int2 pc) {
            var e1 = pb - pa;
            var e2 = pc - pa;
            return (long)e1.X * e2.Y - (long)e2.X * e1.Y > 0;
        }
        public static bool IsCW(Int2 pa, Int2 pb, Int2 pc) {
            var e1 = pb - pa;
            var e2 = pc - pa;
            return (long)e1.X * e2.Y - (long)e2.X * e1.Y < 0;
        }

        public static bool GetIntersects(Int2 a1, Int2 a2, Int2 b1, Int2 b2) {
            var aD = a2 - a1;
            var bD = b2 - b1;
            var p0 = (long)bD.Y * (b2.X - a1.X) - (long)bD.X * (b2.Y - a1.Y);
            var p1 = (long)bD.Y * (b2.X - a2.X) - (long)bD.X * (b2.Y - a2.Y);
            var p2 = (long)aD.Y * (a2.X - b1.X) - (long)aD.X * (a2.Y - b1.Y);
            var p3 = (long)aD.Y * (a2.X - b2.X) - (long)aD.X * (a2.Y - b2.Y);
            return (p0 * p1 <= 0) && (p2 * p3 <= 0);
        }
        public static bool GetIntersectsNE(Int2 a1, Int2 a2, Int2 b1, Int2 b2) {
            var aD = a2 - a1;
            var bD = b2 - b1;
            long p0 = (long)bD.Y * (b2.X - a1.X) - (long)bD.X * (b2.Y - a1.Y);
            long p1 = (long)bD.Y * (b2.X - a2.X) - (long)bD.X * (b2.Y - a2.Y);
            long p2 = (long)aD.Y * (a2.X - b1.X) - (long)aD.X * (a2.Y - b1.Y);
            long p3 = (long)aD.Y * (a2.X - b2.X) - (long)aD.X * (a2.Y - b2.Y);
            return (p0 * p1 < 0) && (p2 * p3 < 0);
        }

        public static bool TriangleContainsCW(Int2 t0, Int2 t1, Int2 t2, Int2 p) {
            var s = (long)(t0.X - t2.X) * (p.Y - t2.Y) - (t0.Y - t2.Y) * (p.X - t2.X);
            if (s > 0) return false;
            var t = (long)(t1.X - t0.X) * (p.Y - t0.Y) - (t1.Y - t0.Y) * (p.X - t0.X);
            if (t > 0) return false;
            //if ((s < 0) != (t < 0) && s != 0 && t != 0) return false;
            var d = (long)(t2.X - t1.X) * (p.Y - t1.Y) - (t2.Y - t1.Y) * (p.X - t1.X);
            if (d > 0) return false;
            //return d == 0 || (d < 0) == (s + t <= 0);
            return true;
        }

        public static long Cross(Int2 v1, Int2 v2) {
            return (long)v1.X * v2.Y - (long)v1.Y * v2.X;
        }

        public static bool GetIntersectionA(Int2 a0, Int2 a1, Int2 b0, Int2 b1, out int alerp, out int divisor) {
            var aD = a1 - a0;
            var bD = b1 - b0;
            divisor = (aD.X * bD.Y - aD.Y * bD.X);
            if (divisor == 0) { alerp = -1; return false; }
            var d0 = a0 - b0;
            alerp = (d0.Y * bD.X - d0.X * bD.Y);
            return alerp >= 0 && alerp < divisor;
        }
        public static bool GetIntersection(Int2 a0, Int2 a1, Int2 b0, Int2 b1, out int alerp, out int blerp, out int divisor) {
            var aD = a1 - a0;
            var bD = b1 - b0;
            divisor = (aD.X * bD.Y - aD.Y * bD.X);
            if (divisor == 0) { alerp = -1; blerp = -1; return false; }
            var d0 = a0 - b0;
            alerp = (d0.Y * bD.X - d0.X * bD.Y);
            blerp = (d0.Y * aD.X - d0.X * aD.Y);
            if (divisor < 0) { divisor = -divisor; alerp = -alerp; blerp = -blerp; }
            return alerp >= 0 && alerp <= divisor && blerp >= 0 && blerp <= divisor;
        }
        public static Int2 GetIntersection(Int2 a0, Int2 a1, Int2 b0, Int2 b1) {
            var aD = a1 - a0;
            var bD = b1 - b0;
            var cross = (aD.X * bD.Y - aD.Y * bD.X);
            if (cross == 0) return default;
            var d0 = a0 - b0;
            var alerp = (d0.Y * bD.X - d0.X * bD.Y);
            var blerp = (d0.Y * aD.X - d0.X * aD.Y);
            //return (a0 + b0 + (aD * alerp + bD * blerp) / cross) / 2;
            var d = aD * alerp + bD * blerp;
            d += (a0 + b0) * cross;
            // Add half divisor to round correctly
            d += new Int2(d.X < 0 ? -1 : 1, d.Y < 0 ? -1 : 1) * (Math.Abs(cross));
            // Divide by cross AND 2 (for averaging the two results)
            d /= cross * 2;
            return d;
        }
        public static Int2 ResolveIntersection(Int2 a0, Int2 a1, Int2 b0, Int2 b1, int alerp, int blerp, int divisor) {
            var d = (a1 - a0) * alerp + (b1 - b0) * blerp;
            d += (a0 + b0) * divisor;
            // Add half divisor to round correctly
            d += new Int2(d.X < 0 ? -1 : 1, d.Y < 0 ? -1 : 1) * (Math.Abs(divisor));
            // Divide by cross AND 2 (for averaging the two results)
            d /= divisor * 2;
            return d;
        }

        public static bool IsVectorBetween(Int2 v1, Int2 mid, Int2 v2) {
            var c1 = NavUtility.Cross(v1, mid);
            var c2 = NavUtility.Cross(mid, v2);
            var cS = NavUtility.Cross(v1, v2);
            if (cS >= 0) {
                if (c1 < 0 || c2 < 0) return false;
            } else {
                if (c1 < 0 && c2 < 0) return false;
            }
            return true;
        }

        public static int Distance(Int2 v1, Int2 v2) {
            v1 -= v2;
            return (int)SqrtFastI((uint)Int2.Dot(v1, v1));
        }
        public static uint SqrtFastI(uint val) {
            if (val <= 1) return val;

            uint place = 0x40000000;
            while (place > val) { place >>= 2; }

            uint remainder = val;
            uint root = 0;
            while (place != 0) {
                if (remainder >= root + place) {
                    remainder -= root + place;
                    root |= place << 1;
                }
                root >>= 1;
                place >>= 2;
            }
            // Rounding (remainder > (2 * root + 1) / 2)
            // (2r+1) comes from (r+1)(r+1) - r*r
            if (remainder > root) ++root;
            return root;
        }

        public static int MultiplyRatio(int value, int numerator, int divisor) {
            return (int)((long)value * numerator / divisor);
        }
        public static Int2 MultiplyRatio(Int2 value, int numerator, int divisor) {
            return new Int2(
                MultiplyRatio(value.X, numerator, divisor),
                MultiplyRatio(value.Y, numerator, divisor)
            );
        }
        public static Int3 MultiplyRatio(Int3 value, int numerator, int divisor) {
            return new Int3(
                MultiplyRatio(value.X, numerator, divisor),
                MultiplyRatio(value.Y, numerator, divisor),
                MultiplyRatio(value.Z, numerator, divisor)
            );
        }
        public static int Lerp(int from, int to, int numerator, int divisor) {
            return from + (int)((long)(to - from) * numerator / divisor);
        }
        public static Int2 Lerp(Int2 from, Int2 to, int numerator, int divisor) {
            return from + MultiplyRatio(to - from, numerator, divisor);
        }
        public static Int3 Lerp(Int3 from, Int3 to, int numerator, int divisor) {
            return from + MultiplyRatio(to - from, numerator, divisor);
        }
    }

    /*public struct NativeKDTree<T> : IDisposable where T : unmanaged {

        public struct Edge {
            public int MarginMin;
            public int MarginMax;
            public int IndexMin;
            public int IndexMax;
        }

        private NativeSparseArray<Edge> edges;
        private NativeSparseArray<T> items;

        public void Allocate() {
            edges.Allocate(32, Allocator.Persistent);
        }
        public void Dispose() {
            edges.Dispose();
        }

        public void Insert(int2 aabbMin, int2 aabbMax, int id) {
            var i = 0;
            var aMin = aabbMin[i & 1];
            var aMax = aabbMax[i & 1];
            if (aMax > edges[i].MarginMin) {
                // Iterate min
            }
            if (aMin > edges[i].MarginMax) {
                // Iterate min
            }
        }

    }*/

    public struct TriangleGrid : IDisposable {
        public const ushort InvalidTriId = NavMesh2Baker.InvalidTriId;
        public readonly int Size;
        private ushort[] cells;
        public TriangleGrid(int size) {
            Size = size;
            cells = new CornerId[size * size];
            for (int i = 0; i < cells.Length; i++) cells[i] = InvalidTriId;
        }
        public void Dispose() {
        }

        public void Clear() {
            for (int i = 0; i < cells.Length; i++) cells[i] = InvalidTriId;
        }

        public ushort FindTriangleAt(Int2 pnt) {
            return cells[pnt.X + pnt.Y * Size];
        }
        public void InsertTriangle(ushort triI, Int2 pnt) {
            cells[pnt.X + pnt.Y * Size] = triI;
        }
        public void RemoveTriangle(ushort triI, Int2 pnt) {
            if (cells[pnt.X + pnt.Y * Size] == triI)
                cells[pnt.X + pnt.Y * Size] = InvalidTriId;
        }

    }
}


