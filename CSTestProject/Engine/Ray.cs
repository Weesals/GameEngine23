using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public struct Ray {
        public Vector3 Origin;
        public Vector3 Direction;
        public Ray(Vector3 origin, Vector3 direction) {
            Origin = origin;
            Direction = direction;
        }
        public Vector3 ProjectTo(Plane p) {
		    return Origin + Direction *
                (p.D - Vector3.Dot(p.Normal, Origin)) / Vector3.Dot(p.Normal, Direction);
	    }
        // Get the distance between a point and the nearest point
        // along this ray
        public float GetDistanceSqr(Vector3 point) {
		    var dirLen2 = Direction.LengthSquared();
            var proj = Origin + Direction *
                (Vector3.Dot(Direction, point - Origin) / dirLen2);
		    return (point - proj).LengthSquared();
        }
        public Vector3 GetPoint(float d) { return Origin + Direction * d; }
	    public Ray Normalize() { return new Ray(Origin, Vector3.Normalize(Direction)); }

        public override string ToString() {
            return $"<{Origin} <> {Direction}>";
        }
    }
}
