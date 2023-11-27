﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public struct Frustum4 {
		public Vector4 mPlaneXs, mPlaneYs, mPlaneZs, mPlaneDs;
		public Vector3 Left => new Vector3(mPlaneXs.X, mPlaneYs.X, mPlaneZs.X);
		public Vector3 Right => new Vector3(mPlaneXs.Y, mPlaneYs.Y, mPlaneZs.Y);
		public Vector3 Down => new Vector3(mPlaneXs.Z, mPlaneYs.Z, mPlaneZs.Z);
		public Vector3 Up => new Vector3(mPlaneXs.W, mPlaneYs.W, mPlaneZs.W);
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
		public bool GetIsVisible(Vector3 pos) {
            return GetVisibility(pos) > 0;
        }
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
        public Vector4 GetProjectedDistances(Vector3 pos) {
            return dot4(mPlaneXs, mPlaneYs, mPlaneZs, pos.X, pos.Y, pos.Z) + mPlaneDs;
        }
        public static Vector4 dot4(Vector4 xs, Vector4 ys, Vector4 zs, float mx, float my, float mz) {
            return xs * mx + ys * my + zs * mz;
        }
	};
	public struct Frustum {
		Frustum4 mFrustum4;
        Vector4 mNearPlane;
		Vector4 mFarPlane;
		public Vector3 Backward => mNearPlane.toxyz();
        public Vector3 Forward => mFarPlane.toxyz();
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
        public bool GetIsVisible(Vector3 pos) {
            return GetVisibility(pos) > 0;
        }
        public bool GetIsVisible(Vector3 pos, Vector3 ext) {
            return GetVisibility(pos, ext) > 0;
        }
        public void GetCorners(Span<Vector3> corners) {
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
    }
}
