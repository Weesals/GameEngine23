using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public struct Frustum4 {
		public Vector4 mPlaneXs, mPlaneYs, mPlaneZs, mPlaneDs;
		public Vector3 Left => new Vector3(mPlaneXs.X, mPlaneYs.X, mPlaneZs.X);
		public Vector3 Right => new Vector3(mPlaneXs.Y, mPlaneYs.Y, mPlaneZs.Y);
		public Vector3 Down => new Vector3(mPlaneXs.Z, mPlaneYs.Z, mPlaneZs.Z);
		public Vector3 Up => new Vector3(mPlaneXs.W, mPlaneYs.W, mPlaneZs.W);

        public Plane LeftPlane => new(mPlaneXs.X, mPlaneYs.X, mPlaneZs.X, mPlaneDs.X);
        public Plane RightPlane => new(mPlaneXs.Y, mPlaneYs.Y, mPlaneZs.Y, mPlaneDs.Y);
        public Plane DownPlane => new(mPlaneXs.Z, mPlaneYs.Z, mPlaneZs.Z, mPlaneDs.Z);
        public Plane UpPlane => new(mPlaneXs.W, mPlaneYs.W, mPlaneZs.W, mPlaneDs.W);

        public Frustum4(Matrix4x4 vp) {
			mPlaneXs = new Vector4(vp.M14) + new Vector4(vp.M11, -vp.M11, vp.M12, -vp.M12);
			mPlaneYs = new Vector4(vp.M24) + new Vector4(vp.M21, -vp.M21, vp.M22, -vp.M22);
			mPlaneZs = new Vector4(vp.M34) + new Vector4(vp.M31, -vp.M31, vp.M32, -vp.M32);
			mPlaneDs = new Vector4(vp.M44) + new Vector4(vp.M41, -vp.M41, vp.M42, -vp.M42);
		}
		public void Normalize() {
			var factors = new Vector4(
				1.0f / new Vector3(mPlaneXs.X, mPlaneYs.X, mPlaneZs.X).Length(),
				1.0f / new Vector3(mPlaneXs.Y, mPlaneYs.Y, mPlaneZs.Y).Length(),
				1.0f / new Vector3(mPlaneXs.Z, mPlaneYs.Z, mPlaneZs.Z).Length(),
				1.0f / new Vector3(mPlaneXs.W, mPlaneYs.W, mPlaneZs.W).Length()
			);
			mPlaneXs *= factors;
			mPlaneYs *= factors;
			mPlaneZs *= factors;
			mPlaneDs *= factors;
		}
        public float GetVisibility(Vector3 pos) {
			Vector4 distances = GetProjectedDistances(pos);
			return distances.cmin();
		}
		public float GetVisibility(Vector3 pos, Vector3 ext) {
			Vector4 distances = GetProjectedDistances(pos)
				+ dot4(Vector4.Abs(mPlaneXs), Vector4.Abs(mPlaneYs), Vector4.Abs(mPlaneZs), ext.X, ext.Y, ext.Z);
			return distances.cmin();
		}
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsVisible(Vector3 pos) {
            return GetVisibility(pos) > 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsVisible(Vector3 pos, Vector3 ext) {
            return GetVisibility(pos, ext) > 0;
        }
		public void IntersectPlane(Vector3 dir, float c, Span<Vector3> points) {
            var crossXs = mPlaneYs.toxzyw() * mPlaneZs.tozywx() - mPlaneZs.toxzyw() * mPlaneYs.tozywx();
            var crossYs = mPlaneZs.toxzyw() * mPlaneXs.tozywx() - mPlaneXs.toxzyw() * mPlaneZs.tozywx();
            var crossZs = mPlaneXs.toxzyw() * mPlaneYs.tozywx() - mPlaneYs.toxzyw() * mPlaneXs.tozywx();

            var up = new Vector4(dir, c);
            var crossUpXs = up.Y * mPlaneZs.toxzyw() - up.Z * mPlaneYs.toxzyw();
            var crossUpYs = up.Z * mPlaneXs.toxzyw() - up.X * mPlaneZs.toxzyw();
            var crossUpZs = up.X * mPlaneYs.toxzyw() - up.Y * mPlaneXs.toxzyw();

            var dets = crossXs * up.X + crossYs * up.Y + crossZs * up.Z;

            var posXs = (mPlaneDs.toxzyw() * crossUpXs.toyzwx() + up.W * crossXs - mPlaneDs.tozywx() * crossUpXs.toxyzw()) / dets;
            var posYs = (mPlaneDs.toxzyw() * crossUpYs.toyzwx() + up.W * crossYs - mPlaneDs.tozywx() * crossUpYs.toxyzw()) / dets;
            var posZs = (mPlaneDs.toxzyw() * crossUpZs.toyzwx() + up.W * crossZs - mPlaneDs.tozywx() * crossUpZs.toxyzw()) / dets;

            points[0] = new Vector3(posXs.X, posYs.X, posZs.X);
            points[1] = new Vector3(posXs.Y, posYs.Y, posZs.Y);
            points[2] = new Vector3(posXs.Z, posYs.Z, posZs.Z);
            points[3] = new Vector3(posXs.W, posYs.W, posZs.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 GetProjectedDistances(Vector3 pos) {
            return dot4(mPlaneXs, mPlaneYs, mPlaneZs, pos.X, pos.Y, pos.Z) + mPlaneDs;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 dot4(Vector4 xs, Vector4 ys, Vector4 zs, float mx, float my, float mz) {
            return xs * mx + ys * my + zs * mz;
        }

        public void GetPlanes(Span<Plane> planes) {
            planes[0] = new Plane(mPlaneXs.X, mPlaneYs.X, mPlaneZs.X, mPlaneDs.X);
            planes[1] = new Plane(mPlaneXs.Y, mPlaneYs.Y, mPlaneZs.Y, mPlaneDs.Y);
            planes[2] = new Plane(mPlaneXs.Z, mPlaneYs.Z, mPlaneZs.Z, mPlaneDs.Z);
            planes[3] = new Plane(mPlaneXs.W, mPlaneYs.W, mPlaneZs.W, mPlaneDs.W);
        }
    };
	public struct Frustum {
		Frustum4 mFrustum4;
        Vector4 mNearPlane;
		Vector4 mFarPlane;
        public Vector3 Left => mFrustum4.Left;
        public Vector3 Right => mFrustum4.Right;
        public Vector3 Up => mFrustum4.Up;
        public Vector3 Down => mFrustum4.Down;
        public Vector3 Backward => mNearPlane.toxyz();
        public Vector3 Forward => mFarPlane.toxyz();
        public Plane LeftPlane => mFrustum4.LeftPlane;
        public Plane RightPlane => mFrustum4.RightPlane;
        public Plane DownPlane => mFrustum4.DownPlane;
        public Plane UpPlane => mFrustum4.UpPlane;
        public Plane NearPlane => new(mNearPlane.toxyz(), mNearPlane.W);
        public Plane FarPlane => new(mFarPlane.toxyz(), mFarPlane.W);
        public Frustum(Matrix4x4 vp) {
            mFrustum4 = new Frustum4(vp);
            mNearPlane = new Vector4(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43);
            mFarPlane = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);
        }
		public void Normalize() {
            mFrustum4.Normalize();
            mNearPlane /= mNearPlane.toxyzw().Length();
            mFarPlane /= mFarPlane.toxyzw().Length();
        }
		public Matrix4x4 CalculateViewProj() {
            Matrix4x4 r;
            r.M14 = (mFrustum4.mPlaneXs.X + mFrustum4.mPlaneXs.Y) / 2.0f;
            r.M24 = (mFrustum4.mPlaneYs.X + mFrustum4.mPlaneYs.Y) / 2.0f;
            r.M34 = (mFrustum4.mPlaneZs.X + mFrustum4.mPlaneZs.Y) / 2.0f;
            r.M44 = (mFrustum4.mPlaneDs.X + mFrustum4.mPlaneDs.Y) / 2.0f;
            r.M11 = mFrustum4.mPlaneXs.X - r.M14;
            r.M21 = mFrustum4.mPlaneYs.X - r.M24;
            r.M31 = mFrustum4.mPlaneZs.X - r.M34;
            r.M41 = mFrustum4.mPlaneDs.X - r.M44;
            r.M12 = mFrustum4.mPlaneXs.Z - r.M14;
            r.M22 = mFrustum4.mPlaneYs.Z - r.M24;
            r.M32 = mFrustum4.mPlaneZs.Z - r.M34;
            r.M42 = mFrustum4.mPlaneDs.Z - r.M44;
            r.M13 = mNearPlane.X - r.M14;
            r.M23 = mNearPlane.Y - r.M24;
            r.M33 = mNearPlane.Z - r.M34;
            r.M43 = mNearPlane.W - r.M44;
            return r;
        }
        public float GetVisibility(Vector3 pos) {
            Vector4 distances = mFrustum4.GetProjectedDistances(pos);
            Vector2 nfdistances = GetProjectedDistancesNearFar(pos);
            return MathF.Min(distances.cmin(), nfdistances.cmin());
        }
        public float GetVisibility(Vector3 pos, Vector3 ext) {
            Vector4 distances = mFrustum4.GetProjectedDistances(pos)
                + Frustum4.dot4(Vector4.Abs(mFrustum4.mPlaneXs), Vector4.Abs(mFrustum4.mPlaneYs), Vector4.Abs(mFrustum4.mPlaneZs), ext.X, ext.Y, ext.Z);
            Vector2 nfdistances = GetProjectedDistancesNearFar(pos)
                + new Vector2(Vector3.Dot(Vector3.Abs(mNearPlane.toxyz()), ext), Vector3.Dot(Vector3.Abs(mFarPlane.toxyz()), ext));
            return MathF.Min(distances.cmin(), nfdistances.cmin());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetVisibility(Vector3 pos, out bool contained) {
            Vector4 d = mFrustum4.GetProjectedDistances(pos);
            if (d.X <= 0f || d.Y <= 0f || d.Z <= 0f || d.W <= 0f) { contained = false; return false; }
            Vector2 nfd = GetProjectedDistancesNearFar(pos);
            if (nfd.X <= 0f || nfd.Y <= 0f) { contained = false; return false; }
            contained = d.cmax() >= 0f && nfd.cmax() >= 0f;
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetVisibility(BoundingBox bounds, out bool contained) {
            return GetVisibility(bounds.Centre, bounds.Size / 2f, out contained);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetVisibility(Vector3 pos, Vector3 ext, out bool contained) {
            var d = mFrustum4.GetProjectedDistances(pos);
            var ext4 = Frustum4.dot4(Vector4.Abs(mFrustum4.mPlaneXs), Vector4.Abs(mFrustum4.mPlaneYs), Vector4.Abs(mFrustum4.mPlaneZs), ext.X, ext.Y, ext.Z);
            var dp = d + ext4;
            if (dp.X <= 0f || dp.Y <= 0f || dp.Z <= 0f || dp.W <= 0f) { contained = false; return false; }
            var nfd = GetProjectedDistancesNearFar(pos);
            var ext2 = new Vector2(Vector3.Dot(Vector3.Abs(mNearPlane.toxyz()), ext), Vector3.Dot(Vector3.Abs(mFarPlane.toxyz()), ext));
            var nfdp = nfd + ext2;
            if (nfdp.X <= 0f || nfdp.Y <= 0f) { contained = false; return false; }
            var dn = d - ext4;
            var nfdn = nfd - ext2;
            contained = dn.X >= 0f && dn.Y >= 0f && dn.Z >= 0f &&
                dn.W >= 0f && nfdn.X >= 0f && nfdn.Y >= 0f;
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsVisible(Vector3 pos) {
            Vector4 d = mFrustum4.GetProjectedDistances(pos);
            if (d.X <= 0f || d.Y <= 0f || d.Z <= 0f || d.W <= 0f) return false;
            Vector2 nfd = GetProjectedDistancesNearFar(pos);
            return nfd.X > 0f || nfd.Y > 0f;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsVisible(BoundingBox bounds) {
            return GetIsVisible(bounds.Centre, bounds.Size / 2f);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsVisible(Vector3 pos, Vector3 ext) {
            Vector4 d = mFrustum4.GetProjectedDistances(pos)
                + Frustum4.dot4(Vector4.Abs(mFrustum4.mPlaneXs), Vector4.Abs(mFrustum4.mPlaneYs), Vector4.Abs(mFrustum4.mPlaneZs), ext.X, ext.Y, ext.Z);
            if (d.X <= 0f || d.Y <= 0f || d.Z <= 0f || d.W <= 0f) return false;
            Vector2 nfd = GetProjectedDistancesNearFar(pos)
                + new Vector2(Vector3.Dot(Vector3.Abs(mNearPlane.toxyz()), ext), Vector3.Dot(Vector3.Abs(mFarPlane.toxyz()), ext));
            return nfd.X > 0f || nfd.Y > 0f;
        }
        private Vector3 IntersectPlanes(Plane p1, Plane p2, Plane p3) {
            Vector3 n1 = p1.Normal;
            Vector3 n2 = p2.Normal;
            Vector3 n3 = p3.Normal;
            Vector3 cross23 = Vector3.Cross(n2, n3);
            Vector3 cross31 = Vector3.Cross(n3, n1);
            Vector3 cross12 = Vector3.Cross(n1, n2);
            return (p1.D * cross23 + p2.D * cross31 + p3.D * cross12) / -Vector3.Dot(n1, cross23);
        }
        public void GetCorners(Span<Vector3> corners) {
            corners[0] = IntersectPlanes(NearPlane, DownPlane, LeftPlane);
            corners[1] = IntersectPlanes(NearPlane, DownPlane, RightPlane);
            corners[2] = IntersectPlanes(NearPlane, UpPlane, LeftPlane);
            corners[3] = IntersectPlanes(NearPlane, UpPlane, RightPlane);
            corners[4] = IntersectPlanes(FarPlane, DownPlane, LeftPlane);
            corners[5] = IntersectPlanes(FarPlane, DownPlane, RightPlane);
            corners[6] = IntersectPlanes(FarPlane, UpPlane, LeftPlane);
            corners[7] = IntersectPlanes(FarPlane, UpPlane, RightPlane);
            return;
            var vp = CalculateViewProj();
            Matrix4x4.Invert(vp, out vp);
            int i = 0;
            for (float z = -1.0f; z < 1.5f; z += 2.0f) {
                for (float y = -1.0f; y < 1.5f; y += 2.0f) {
                    for (float x = -1.0f; x < 1.5f; x += 2.0f) {
                        var point = Vector4.Transform(new Vector4(x, y, z, 1.0f), vp);
                        corners[i++] = point.toxyz() / point.W;
                    }
                }
            }
        }
        public void IntersectPlane(Vector3 dir, float c, Span<Vector3> points) {
            mFrustum4.IntersectPlane(dir, c, points);
        }

        public Frustum TransformToLocal(in Matrix4x4 tform) {
	        return new Frustum(tform * CalculateViewProj());
        }
        private Vector2 GetProjectedDistancesNearFar(Vector3 pos) {
            return new Vector2(
                Vector3.Dot(mNearPlane.toxyz(), pos) + mNearPlane.W,
                Vector3.Dot(mFarPlane.toxyz(), pos) + mFarPlane.W
            );
        }

        public void GetPlanes(Span<Plane> planes) {
            mFrustum4.GetPlanes(planes);
            planes[4] = new Plane(mNearPlane.X, mNearPlane.Y, mNearPlane.Z, mNearPlane.W);
            planes[5] = new Plane(mFarPlane.X, mFarPlane.Y, mFarPlane.Z, mFarPlane.W);
        }

        public void SetFarClip(float farClip) {
            mFarPlane.W = farClip;
        }
    }
}
