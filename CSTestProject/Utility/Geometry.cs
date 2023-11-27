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
    }
}
