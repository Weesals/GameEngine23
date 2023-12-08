using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Utility {
    public class Geometry {
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

        private static bool IsPointInsideTriangle(Vector2 pa, Vector2 pb, Vector2 pc, Vector2 p0, Vector2 p1, Vector2 p2) {
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

        private static bool cross2(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 t1v0, Vector2 t1v1, Vector2 t1v2) {
            var da = p0 - t1v2;
            var db = p1 - t1v2;
            var dc = p2 - t1v2;
            var dX12 = t1v1.X - t1v2.X;
            var dY12 = t1v1.Y - t1v2.Y;
            var t1_21 = t1v0 - t1v2;
            var D = dY12 * t1_21.X - dX12 * t1_21.Y;
            var sa = dY12 * da.X - dX12 * da.Y;
            var sb = dY12 * db.X - dX12 * db.Y;
            var sc = dY12 * dc.X - dX12 * dc.Y;
            var ta = t1_21.X * da.Y - t1_21.Y * da.X;
            var tb = t1_21.X * db.Y - t1_21.Y * db.X;
            var tc = t1_21.X * dc.Y - t1_21.Y * dc.X;
            if (D < 0) return ((sa >= 0 && sb >= 0 && sc >= 0) ||
                               (ta >= 0 && tb >= 0 && tc >= 0) ||
                               (sa + ta <= D && sb + tb <= D && sc + tc <= D));
            return ((sa <= 0 && sb <= 0 && sc <= 0) ||
                    (ta <= 0 && tb <= 0 && tc <= 0) ||
                    (sa + ta >= D && sb + tb >= D && sc + tc >= D));
        }
        public static bool GetTrianglesOverlap(Vector2 t0v0, Vector2 t0v1, Vector2 t0v2, Vector2 t1v0, Vector2 t1v1, Vector2 t1v2) {
            /*return IsPointInsideTriangle(t0v0, t1v0, t1v1, t1v2)
                || IsPointInsideTriangle(t0v1, t1v0, t1v1, t1v2)
                || IsPointInsideTriangle(t0v2, t1v0, t1v1, t1v2)
                || IsPointInsideTriangle(t1v0, t0v0, t0v1, t0v2)
                || IsPointInsideTriangle(t1v1, t0v0, t0v1, t0v2)
                || IsPointInsideTriangle(t1v2, t0v0, t0v1, t0v2);*/
            return !(IsPointInsideTriangle(t0v0, t0v1, t0v2, t1v0, t1v1, t1v2) ||
                     IsPointInsideTriangle(t1v0, t1v1, t1v2, t0v0, t0v1, t0v2));
        }
        private static float Cross(Vector2 v1, Vector2 v2) {
            return v1.X * v2.Y - v1.Y * v2.X;
        }
        private static bool IsPointInsideTriangle(Vector2 pnt, Vector2 t1v0, Vector2 t1v1, Vector2 t1v2) {
            t1v0 -= pnt;
            t1v1 -= pnt;
            t1v2 -= pnt;
            return Cross(t1v0, t1v1) > 0f && Cross(t1v1, t1v2) > 0f && Cross(t1v2, t1v0) > 0f;
        }
    }
}
