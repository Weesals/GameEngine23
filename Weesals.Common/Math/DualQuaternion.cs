using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {

    public struct DualQuaternion : IEquatable<DualQuaternion> {
        public Quaternion Real;
        public Quaternion Dual;

        public Quaternion Rotation {
            get => Real;
            set {
                Dual *= Quaternion.Conjugate(Real);
                Real = value;
                Dual *= Real;
            }
        }
        public Vector3 Translation {
            get => GetXYZ((Dual * 2.0f) * Quaternion.Conjugate(Real));
            set => Dual = (new Quaternion(value, 0.0f) * Real) * 0.5f;
        }

        public DualQuaternion(Quaternion rot, Vector3 trans) {
            Real = rot;
            //Dual = Quaternion.Multiply(Real, new Quaternion(trans, 0.0f) * 0.5f);
            Dual = (new Quaternion(trans, 0.0f) * rot) * 0.5f;
            Debug.Assert((trans - Translation).LengthSquared() < 0.5f);
        }
        public DualQuaternion(Quaternion rot, Quaternion imag) {
            Real = rot;
            Dual = imag;
        }
        public DualQuaternion(Vector3 translation) {
            Real = Quaternion.Identity;
            Dual = new Quaternion(translation, 0.0f);
        }

        public DualQuaternion Inverse() {
            var invRot = Quaternion.Inverse(Real);
            return new DualQuaternion(invRot,
                -Quaternion.Multiply(invRot, Quaternion.Multiply(Dual, invRot)));
        }

        public static DualQuaternion operator *(DualQuaternion q1, DualQuaternion q2) {
            return new DualQuaternion(q1.Real * q2.Real,
                (q1.Real * q2.Dual) + (q1.Dual * q2.Real));
        }

        public static float Dot(DualQuaternion q1, DualQuaternion q2) {
            return Quaternion.Dot(q1.Real, q2.Real) + Quaternion.Dot(q1.Dual, q2.Dual);
        }
        public static DualQuaternion Normalize(DualQuaternion q) {
            float real = MathF.Sqrt(Quaternion.Dot(q.Real, q.Real));
            return new DualQuaternion(q.Real * real, q.Dual * real);
        }
        public static Vector3 Transform(Vector3 v, DualQuaternion trans) {
            var dual = trans.Dual; dual.W *= -1.0f;
            var q = trans * new DualQuaternion(v)
                * new DualQuaternion(Quaternion.Conjugate(trans.Real), dual);
            return GetXYZ(q.Dual);
        }
        /*public static DualQuaternion Log(DualQuaternion dq) {
            var q = dq.Rotation;
            float t = q.W >= 1.0f || q.W == -1.0f ? 1.0f : MathF.Acos(q.W) / MathF.Sqrt(1.0f - q.W * q.W);
            q = new(q.X * t, q.Y * t, q.Z * t, 0.0f);
            dq.Imaginary = new(
                -0.5f * (dq.Imaginary.W * dq.Rotation.X + dq.Imaginary.X * dq.Rotation.W + dq.Imaginary.Y * dq.Rotation.Z - dq.Imaginary.Z * dq.Rotation.Y),
                -0.5f * (dq.Imaginary.W * dq.Rotation.Y - dq.Imaginary.X * dq.Rotation.Y + dq.Imaginary.Y * dq.Rotation.W + dq.Imaginary.Z * dq.Rotation.X),
                -0.5f * (dq.Imaginary.W * dq.Rotation.Z + dq.Imaginary.X * dq.Rotation.Z - dq.Imaginary.Y * dq.Rotation.X + dq.Imaginary.Z * dq.Rotation.W),
                +0.5f * (dq.Imaginary.X * q.X + dq.Imaginary.Y * q.Y + dq.Imaginary.Z * q.Z)
            );
            dq.Rotation = q;
            return dq;
        }
        public static DualQuaternion Exp(DualQuaternion dq) {
            var q = dq.Rotation;
            float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            if (len <= 0.0000001f) q = Quaternion.Identity;
            else {
                float slen = MathF.Sin(len) / len;
                float clen = MathF.Cos(len);
                q = new(q.X * slen, q.Y * slen, q.Z * slen, clen);
            }
            dq.Imaginary = new(
                -0.5f * (dq.Imaginary.W * dq.Rotation.X + dq.Imaginary.X * dq.Rotation.W + dq.Imaginary.Y * dq.Rotation.Z - dq.Imaginary.Z * dq.Rotation.Y),
                -0.5f * (dq.Imaginary.W * dq.Rotation.Y - dq.Imaginary.X * dq.Rotation.Y + dq.Imaginary.Y * dq.Rotation.W + dq.Imaginary.Z * dq.Rotation.X),
                -0.5f * (dq.Imaginary.W * dq.Rotation.Z + dq.Imaginary.X * dq.Rotation.Z - dq.Imaginary.Y * dq.Rotation.X + dq.Imaginary.Z * dq.Rotation.W),
                +0.5f * (dq.Imaginary.X * q.X + dq.Imaginary.Y * q.Y + dq.Imaginary.Z * q.Z)
            );
            dq.Rotation = q;
            return dq;
        }*/

        public static DualQuaternion Inverse(DualQuaternion dq) {
            var invRot = Quaternion.Inverse(dq.Real);
            return new DualQuaternion(invRot,
                -Quaternion.Multiply(invRot, Quaternion.Multiply(dq.Dual, invRot)));
        }

        public static void ToScrew(DualQuaternion dq, out float theta, out Vector3 axis, out float d, out Vector3 moment) {
            var qrLen = GetXYZ(dq.Real).Length();
            if (qrLen <= float.Epsilon) { theta = default; axis = default; d = default; moment = default; return; }

            axis = GetXYZ(dq.Real) / qrLen;
            theta = MathF.Acos(dq.Real.W);
            d = -dq.Dual.W / qrLen;
            moment = (GetXYZ(dq.Dual) - axis * (d * dq.Real.W)) / qrLen;
        }

        public static DualQuaternion Power(DualQuaternion dq, float n) {
            ToScrew(dq, out var theta, out var axis, out var d, out var moment);
            var sine = MathF.Sin(theta * n);
            var cosine = MathF.Cos(theta * n);
            d *= n;
            return new DualQuaternion(
                new Quaternion(axis * sine, cosine),
                new Quaternion(axis * (cosine * d) + moment * sine, -d * sine)
            );
        }

        public static DualQuaternion Slerp(DualQuaternion q1, DualQuaternion q2, float t) {
            if (Dot(q1, q2) < 0) { q2.Real *= -1; q2.Dual *= -1; }
            var delta = q2 * Conjugate(q1);
            if (delta.Real.W > 0.99f) {
                return new(
                    q1.Real * (1 - t) + q2.Real * t,
                    q1.Dual * (1 - t) + q2.Dual * t
                );
            } else {
                return Power(delta, t) * q1;
            }
        }

        public static DualQuaternion Conjugate(DualQuaternion q) {
            return new(Quaternion.Conjugate(q.Real), Quaternion.Conjugate(q.Dual));
        }

        private static Vector3 GetXYZ(Quaternion q) {
            return new(q.X, q.Y, q.Z);
        }

        public override string ToString() {
            return Translation.ToString();
        }

        public bool Equals(DualQuaternion other) {
            return Real == other.Real && Dual == other.Dual;
        }

        private static Quaternion QuatLn(Quaternion q) {
            float t = q.W >= 1.0f || q.W == -1.0f ? 1.0f : MathF.Acos(q.W) / MathF.Sqrt(1.0f - q.W * q.W);
            return new(q.X * t, q.Y * t, q.Z * t, 0.0f);
        }
        private static Quaternion QuatExp(Quaternion q) {
            float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            if (len <= 0.0000001f) return Quaternion.Identity;
            float slen = MathF.Sin(len) / len;
            float clen = MathF.Cos(len);
            return new(q.X * slen, q.Y * slen, q.Z * slen, clen);
        }
    }

}
