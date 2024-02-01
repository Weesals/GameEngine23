using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
	public struct BoundingBox {
		public Vector3 Min, Max;
        public Vector3 Centre => (Min + Max) / 2.0f;
		public Vector3 Extents => (Max - Min) / 2.0f;
		public BoundingBox() : this(Vector3.Zero, Vector3.Zero) { }
		public BoundingBox(Vector3 min, Vector3 max) {
			Min = min;
			Max = max;
		}
		public static BoundingBox FromMinMax(Vector3 min, Vector3 max) { return new BoundingBox(min, max); }

        public float RayCast(Ray ray) {
            var invRayDir = Vector3.Divide(Vector3.One, ray.Direction);
            var d0 = (Min - ray.Origin) * invRayDir;
            var d1 = (Max - ray.Origin) * invRayDir;

            var min = Vector3.Min(d0, d1);
            var max = Vector3.Max(d0, d1);

            var rmin = MathF.Max(MathF.Max(min.X, min.Y), min.Z);
            var rmax = MathF.Min(MathF.Min(max.X, max.Y), max.Z);

            return rmin <= rmax ? rmin : -1f;
        }
    };

}
