using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Geometry {
    public static class Triangulation {
        public static bool RayTriangleIntersection(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 bc, out float t) {
            bc = default; t = default;
            Vector3 edge1 = v1 - v0, edge2 = v2 - v0;
            var h = Vector3.Cross(ray.Direction, edge2);
            var a = Vector3.Dot(edge1, h);

            // Ray parallel to triangle or triangle is degenerate
            if (MathF.Abs(a) < float.Epsilon) return false;

            var f = 1.0f / a;
            var s = ray.Origin - v0;
            var u = Vector3.Dot(s, h) * f;

            // Outside of range of edge1
            if (u < 0.0f || u > 1.0f) return false;

            var q = Vector3.Cross(s, edge1);
            var v = Vector3.Dot(ray.Direction, q) * f;

            // Out of range of other edges
            if (v < 0.0f || u + v > 1.0f) return false;

            // Compute barycentric coords, and ray distance
            bc = new Vector3(1.0f - u - v, u, v);
            t = Vector3.Dot(edge2, q) * f;

            return t >= 0.0;
        }
        public static bool RayBoxIntersection(Ray ray, Vector3 pos, Vector3 size, out float t) {
            var minFaces = pos - ray.Origin;
            var maxFaces = minFaces;
            minFaces.X -= size.X * (ray.Direction.X < 0.0f ? -0.5f : 0.5f);
            minFaces.Y -= size.Y * (ray.Direction.Y < 0.0f ? -0.5f : 0.5f);
            minFaces.Z -= size.Z * (ray.Direction.Z < 0.0f ? -0.5f : 0.5f);
            maxFaces.X += size.X * (ray.Direction.X < 0.0f ? -0.5f : 0.5f);
            maxFaces.Y += size.Y * (ray.Direction.Y < 0.0f ? -0.5f : 0.5f);
            maxFaces.Z += size.Z * (ray.Direction.Z < 0.0f ? -0.5f : 0.5f);
            float entry = float.MinValue;
            if (ray.Direction.X != 0.0f) entry = MathF.Max(entry, minFaces.X / ray.Direction.X);
            if (ray.Direction.Y != 0.0f) entry = MathF.Max(entry, minFaces.Y / ray.Direction.Y);
            if (ray.Direction.Z != 0.0f) entry = MathF.Max(entry, minFaces.Z / ray.Direction.Z);
            float exit = float.MaxValue;
            if (ray.Direction.X != 0.0f) exit = MathF.Min(exit, maxFaces.X / ray.Direction.X);
            if (ray.Direction.Y != 0.0f) exit = MathF.Min(exit, maxFaces.Y / ray.Direction.Y);
            if (ray.Direction.Z != 0.0f) exit = MathF.Min(exit, maxFaces.Z / ray.Direction.Z);
            t = entry;
            return entry <= exit;
        }

        unsafe private static bool IsPointInsideTriangleSIMD2(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 v0, Vector2 v1, Vector2 v2) {
            var d12 = v1 - v2;
            var d02 = v0 - v2;
            Span<float> xItems = stackalloc float[Vector<float>.Count];// { p0.X, p1.X, p2.X, v0.X };
            Span<float> yItems = stackalloc float[Vector<float>.Count];// { p0.Y, p1.Y, p2.Y, v0.Y };
            xItems[0] = p0.X; xItems[1] = p1.X; xItems[0] = p2.X; xItems[1] = v0.X;
            yItems[0] = p0.Y; yItems[1] = p1.Y; yItems[0] = p2.Y; yItems[1] = v0.Y;
            var lX = new Vector<float>(xItems);
            var lY = new Vector<float>(yItems);
            lX -= new Vector<float>(v2.X);
            lY -= new Vector<float>(v2.Y);
            var sV = lX * new Vector<float>(d12.Y) + lY * new Vector<float>(-d12.X);
            var tV = *(Vector3*)&lY * new Vector3(d02.X) + *(Vector3*)&lX * new Vector3(-d02.Y);
            if (sV[3] < 0.0f) {
                return ((sV[0] >= 0 && sV[1] >= 0 && sV[2] >= 0) ||
                        (tV[0] >= 0 && tV[1] >= 0 && tV[2] >= 0) ||
                        (sV[0] + tV[0] <= sV[3] && sV[3] + tV[1] <= sV[3] && sV[2] + tV[2] <= sV[3]));
            }
            return ((sV[0] <= 0 && sV[1] <= 0 && sV[2] <= 0) ||
                    (tV[0] <= 0 && tV[1] <= 0 && tV[2] <= 0) ||
                    (sV[0] + tV[0] >= sV[3] && sV[1] + tV[1] >= sV[3] && sV[2] + tV[2] >= sV[3]));
        }
        unsafe private static bool IsPointInsideTriangleSIMD(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 v0, Vector2 v1, Vector2 v2) {
            var d12 = v1 - v2;
            var d02 = v0 - v2;
            var lX = new Vector4(p0.X, p1.X, p2.X, v0.X);
            var lY = new Vector4(p0.Y, p1.Y, p2.Y, v0.Y);
            lX -= new Vector4(v2.X);
            lY -= new Vector4(v2.Y);
            var sV = lX * new Vector4(d12.Y) + lY * new Vector4(-d12.X);
            var tV = *(Vector3*)&lY * new Vector3(d02.X) + *(Vector3*)&lX * new Vector3(-d02.Y);
            if (sV.W < 0.0f) {
                return ((sV.X >= 0 && sV.Y >= 0 && sV.Z >= 0) ||
                        (tV.X >= 0 && tV.Y >= 0 && tV.Z >= 0) ||
                        (sV.X + tV.X <= sV.W && sV.Y + tV.Y <= sV.W && sV.Z + tV.Z <= sV.W));
            }
            return ((sV.X <= 0 && sV.Y <= 0 && sV.Z <= 0) ||
                    (tV.X <= 0 && tV.Y <= 0 && tV.Z <= 0) ||
                    (sV.X + tV.X >= sV.W && sV.Y + tV.Y >= sV.W && sV.Z + tV.Z >= sV.W));
        }
        private static bool IsPointInsideTrianglePre(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 v0, Vector2 v1, Vector2 v2) {
            v0 = v2 - v0;
            v1 -= v2;
            p0 -= v2;
            p1 -= v2;
            p2 -= v2;
            var De = v0.Y * v1.X - v0.X * v1.Y;
            var s = De < 0 ? 1.0f : -1.0f;
            De = De * s;
            v0.X = v0.X * s;
            v0.Y = v0.Y * s;
            v1.X = v1.X * s;
            v1.Y = v1.Y * s;
            var ta = v0.Y * p0.X - v0.X * p0.Y;
            var tb = v0.Y * p1.X - v0.X * p1.Y;
            var tc = v0.Y * p2.X - v0.X * p2.Y;
            var sa = v1.Y * p0.X - v1.X * p0.Y;
            var sb = v1.Y * p1.X - v1.X * p1.Y;
            var sc = v1.Y * p2.X - v1.X * p2.Y;
            return ((sa >= 0 && sb >= 0 && sc >= 0) ||
                    (ta >= 0 && tb >= 0 && tc >= 0) ||
                    (sa + ta <= De && sb + tb <= De && sc + tc <= De));
        }
        private static bool HasNoSeparatingAxis(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 v0, Vector2 v1, Vector2 v2) {
            v0 = v2 - v0;
            v1 -= v2;
            p0 -= v2;
            p1 -= v2;
            p2 -= v2;
            var De = v0.Y * v1.X - v0.X * v1.Y;
            var sa = v1.Y * p0.X - v1.X * p0.Y;
            var sb = v1.Y * p1.X - v1.X * p1.Y;
            var sc = v1.Y * p2.X - v1.X * p2.Y;
            var ta = v0.Y * p0.X - v0.X * p0.Y;
            var tb = v0.Y * p1.X - v0.X * p1.Y;
            var tc = v0.Y * p2.X - v0.X * p2.Y;
            if (De < 0) return ((sa < 0 || sb < 0 || sc < 0) &&
                               (ta < 0 || tb < 0 || tc < 0) &&
                               (sa + ta > De || sb + tb > De || sc + tc > De));
            return ((sa > 0 || sb > 0 || sc > 0) &&
                    (ta > 0 || tb > 0 || tc > 0) &&
                    (sa + ta < De || sb + tb < De || sc + tc < De));
        }
        private static bool HasSeparatingAxis(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 v0, Vector2 v1, Vector2 v2) {
            v0 = v2 - v0;
            v1 -= v2;
            p0 -= v2;
            p1 -= v2;
            p2 -= v2;
            var De = v0.Y * v1.X - v0.X * v1.Y;
            var sa = v1.Y * p0.X - v1.X * p0.Y;
            var sb = v1.Y * p1.X - v1.X * p1.Y;
            var sc = v1.Y * p2.X - v1.X * p2.Y;
            var ta = v0.Y * p0.X - v0.X * p0.Y;
            var tb = v0.Y * p1.X - v0.X * p1.Y;
            var tc = v0.Y * p2.X - v0.X * p2.Y;
            if (De < 0) return ((sa >= 0 && sb >= 0 && sc >= 0) ||
                               (ta >= 0 && tb >= 0 && tc >= 0) ||
                               (sa + ta <= De && sb + tb <= De && sc + tc <= De));
            return ((sa <= 0 && sb <= 0 && sc <= 0) ||
                    (ta <= 0 && tb <= 0 && tc <= 0) ||
                    (sa + ta >= De && sb + tb >= De && sc + tc >= De));
        }
        public static bool GetTrianglesOverlap(Vector2 t0v0, Vector2 t0v1, Vector2 t0v2, Vector2 t1v0, Vector2 t1v1, Vector2 t1v2) {
            return (HasNoSeparatingAxis(t0v0, t0v1, t0v2, t1v0, t1v1, t1v2) &&
                     HasNoSeparatingAxis(t1v0, t1v1, t1v2, t0v0, t0v1, t0v2));
        }

        private static bool IsPointInsideTriangleOG(Vector2 pa, Vector2 pb, Vector2 pc, Vector2 p0, Vector2 p1, Vector2 p2) {
            float dXa = pa.X - p2.X;
            float dYa = pa.Y - p2.Y;
            float dXb = pb.X - p2.X;
            float dYb = pb.Y - p2.Y;
            float dXc = pc.X - p2.X;
            float dYc = pc.Y - p2.Y;

            float dX21 = p2.X - p1.X;
            float dY12 = p1.Y - p2.Y;
            float D = dY12 * (p0.X - p2.X) + dX21 * (p0.Y - p2.Y);
            float sa = dY12 * dXa + dX21 * dYa;
            float sb = dY12 * dXb + dX21 * dYb;
            float sc = dY12 * dXc + dX21 * dYc;
            float ta = (p2.Y - p0.Y) * dXa + (p0.X - p2.X) * dYa;
            float tb = (p2.Y - p0.Y) * dXb + (p0.X - p2.X) * dYb;
            float tc = (p2.Y - p0.Y) * dXc + (p0.X - p2.X) * dYc;

            if (D < 0) {
                return ((sa >= 0 && sb >= 0 && sc >= 0) ||
                        (ta >= 0 && tb >= 0 && tc >= 0) ||
                        (sa + ta <= D && sb + tb <= D && sc + tc <= D));
            }

            return ((sa <= 0 && sb <= 0 && sc <= 0) ||
                    (ta <= 0 && tb <= 0 && tc <= 0) ||
                    (sa + ta >= D && sb + tb >= D && sc + tc >= D));
        }
        public static bool GetTrianglesOverlapOG(Vector2 t0v0, Vector2 t0v1, Vector2 t0v2, Vector2 t1v0, Vector2 t1v1, Vector2 t1v2) {
            return !(IsPointInsideTriangleOG(t0v0, t0v1, t0v2, t1v0, t1v1, t1v2) ||
                     IsPointInsideTriangleOG(t1v0, t1v1, t1v2, t0v0, t0v1, t0v2));
        }
    }
}
