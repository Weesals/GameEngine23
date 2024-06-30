using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Importers;

namespace Weesals.Engine {
    public enum CurveInterpolation { Step, Linear, Bezier, }
    public struct Keyframe<T> where T : struct {
        public float Time;
        public CurveInterpolation Interpolation = CurveInterpolation.Linear;
        public T Value;
        public T InTangent, OutTangent;
        public Keyframe() { }
        public Keyframe(float time, T value, T inTan = default, T outTan = default) {
            Time = time;
            Value = value;
            InTangent = inTan;
            OutTangent = outTan;
        }
        public override string ToString() { return $"{Time} = {Value}"; }
    }
    public class CurveBase<T> where T : struct, IEquatable<T> {
        protected Keyframe<T>[] keyframes = Array.Empty<Keyframe<T>>();
        public Keyframe<T>[] Keyframes => keyframes;
        public float Duration => keyframes.Length > 0 ? keyframes[^1].Time : 0f;

        public CurveBase() { }
        public CurveBase(int keyframeCount) {
            keyframes = new Keyframe<T>[keyframeCount];
            foreach (ref var k in keyframes.AsSpan()) k = new();
        }
        public CurveBase(params Keyframe<T>[] _keyframes) { keyframes = _keyframes; }
        public override string ToString() { return $"<{Keyframes.Length} +{Duration}>"; }

        protected int FindNextKeyframe(float time) {
            int min = 0, max = keyframes.Length - 1;
            while (min < max) {
                var mid = (min + max) / 2;
                if (keyframes[mid].Time < time) {
                    min = mid + 1;
                } else {
                    max = mid;
                }
            }
            return min;
        }
        protected Vector4 EvaluateBezier(float t) {
            float t2 = t * t, t3 = t2 * t;
            return new Vector4(
                2f * t3 - 3f * t2 + 1f,
                t3 - 2f * t2 + t,
                t3 - t2,
                -2f * t3 + 3f * t2
            );
        }
        protected Vector4 EvaluateBezier(float t, float tangentBoost) {
            float t2 = t * t, t3 = t2 * t;
            return new Vector4(
                2f * t3 - 3f * t2 + 1f,
                (t3 - 2f * t2 + t) * tangentBoost,
                (t3 - t2) * tangentBoost,
                -2f * t3 + 3f * t2
            );
        }
        public void SetConstant(T value) {
            if (keyframes.Length == 0) keyframes = new[] { new Keyframe<T>(0f, value), };
            else foreach (ref var keyframe in keyframes.AsSpan()) keyframe.Value = value;
        }

        public static void InsertTimes(CurveBase<T> curve, List<float> times) {
            int t = 0;
            for (int i = 0; i < curve.Keyframes.Length; i++) {
                var time = curve.Keyframes[i].Time;
                if (t < times.Count && time > times[t]) ++t;
                if (t >= times.Count || time < times[t]) times.Insert(t, time);
                ++t;
            }
        }
        public void Optimize() {
            int offset = 0;
            for (int i = 0; i < keyframes.Length; i++) {
                bool require = false;
                var k1 = keyframes[i];
                if (offset != 0) keyframes[i - offset] = k1;
                var i0 = i - 1 - offset;
                var i1 = i + 1;
                if (i0 >= 0 && !keyframes[i0].Value.Equals(k1.Value)) require = true;
                if (i1 < keyframes.Length && !keyframes[i1].Value.Equals(k1.Value)) require = true;
                if (!require) ++offset;
            }
            if (offset != 0) Array.Resize(ref keyframes, Math.Max(keyframes.Length - offset, 1));
        }
    }
    public class FloatCurve : CurveBase<float> {
    
        public FloatCurve() : base() { }
        public FloatCurve(int keyframeCount) : base(keyframeCount) { }
        public FloatCurve(params Keyframe<float>[] _keyframes) : base(_keyframes) { }

        public float Evaluate(float time) {
            var keyI = FindNextKeyframe(time);
            if (keyI == 0) return keyframes[keyI].Value;
            var k0 = keyframes[keyI - 1];
            var k1 = keyframes[keyI + 0];
            var t = (time - k0.Time) / (k1.Time - k0.Time);
            if (t >= 1f) return k1.Value;
            switch (k0.Interpolation) {
                case CurveInterpolation.Step: return k0.Value;
                case CurveInterpolation.Linear: return float.Lerp(k0.Value, k1.Value, t);
            }
            var weights = EvaluateBezier(t);
            return weights.X * k0.Value
                + (weights.Y * k0.OutTangent + weights.Z * k1.InTangent) * (k1.Time - k0.Time)
                + weights.W * k1.Value;
        }

        public static FloatCurve MakeSmoothStep(float from = 0f, float to = 1f) {
            return new FloatCurve(
                new Keyframe<float>(0f, from, 0f, 0f),
                new Keyframe<float>(1f, to, 0f, 0f)
            );
        }

        public static FloatCurve MakeLinear() {
            return new FloatCurve(
                new Keyframe<float>(0f, 0f, 1f, 1f),
                new Keyframe<float>(1f, 1f, 1f, 1f)
            );
        }
    }

    public class Vector3Curve : CurveBase<Vector3> {

        public Vector3Curve() : base() { }
        public Vector3Curve(int keyframeCount) : base(keyframeCount) { }
        public Vector3Curve(params Keyframe<Vector3>[] _keyframes) : base(_keyframes) { }

        public Vector3 Evaluate(float time) {
            var keyI = FindNextKeyframe(time);
            if (keyI == 0) return keyframes[keyI].Value;
            var k0 = keyframes[keyI - 1];
            var k1 = keyframes[keyI + 0];
            var t = (time - k0.Time) / (k1.Time - k0.Time);
            if (t >= 1f) return k1.Value;
            switch (k0.Interpolation) {
                case CurveInterpolation.Step: return k0.Value;
                case CurveInterpolation.Linear: return Vector3.Lerp(k0.Value, k1.Value, t);
            }
            var weights = EvaluateBezier(t, k1.Time - k0.Time);
            return weights.X * k0.Value
                + (weights.Y * k0.OutTangent + weights.Z * k1.InTangent)
                + weights.W * k1.Value;
        }

    }

    public class QuaternionCurve : CurveBase<Quaternion> {

        public QuaternionCurve() : base() { }
        public QuaternionCurve(int keyframeCount) : base(keyframeCount) { }
        public QuaternionCurve(params Keyframe<Quaternion>[] _keyframes) : base(_keyframes) { }

        public Quaternion Evaluate(float time) {
            var keyI = FindNextKeyframe(time);
            if (keyI == 0) return keyframes[keyI].Value;
            var k0 = keyframes[keyI - 1];
            var k1 = keyframes[keyI + 0];
            var t = (time - k0.Time) / (k1.Time - k0.Time);
            if (t >= 1f) return k1.Value;
            switch (k0.Interpolation) {
                case CurveInterpolation.Step: return k0.Value;
                case CurveInterpolation.Linear: return Quaternion.Slerp(k0.Value, k1.Value, t);
            }
            var weights = EvaluateBezier(t);
            return Quaternion.Lerp(Quaternion.Identity, k0.Value, weights.X) * Quaternion.Lerp(Quaternion.Identity, k0.OutTangent, weights.Y) *
                Quaternion.Lerp(Quaternion.Identity, k0.Value, weights.W) * Quaternion.Lerp(Quaternion.Identity, k0.OutTangent, weights.Z);
        }
    }

    public class MatrixCurve : CurveBase<Matrix4x4> {

        public MatrixCurve() : base() { }
        public MatrixCurve(int keyframeCount) : base(keyframeCount) { }
        public MatrixCurve(params Keyframe<Matrix4x4>[] _keyframes) : base(_keyframes) { }

        public Matrix4x4 Evaluate(float time) {
            var keyI = FindNextKeyframe(time);
            if (keyI == 0) return keyframes[keyI].Value;
            var k0 = keyframes[keyI - 1];
            var k1 = keyframes[keyI + 0];
            var t = (time - k0.Time) / (k1.Time - k0.Time);
            if (t >= 1f) return k1.Value;
            switch (k0.Interpolation) {
                case CurveInterpolation.Step: return k0.Value;
                case CurveInterpolation.Linear: return Matrix4x4.Lerp(k0.Value, k1.Value, t);
            }
            var weights = EvaluateBezier(t, k1.Time - k0.Time);
            return k0.Value * weights.X
                + (k0.OutTangent * weights.Y + k1.InTangent * weights.Z)
                + k1.Value * weights.W;
        }

    }

    public class DualQuaternionCurve : CurveBase<DualQuaternion> {

        public DualQuaternionCurve() : base() { }
        public DualQuaternionCurve(int keyframeCount) : base(keyframeCount) { }
        public DualQuaternionCurve(params Keyframe<DualQuaternion>[] _keyframes) : base(_keyframes) { }

        public DualQuaternion Evaluate(float time) {
            var keyI = FindNextKeyframe(time);
            if (keyI == 0) return keyframes[keyI].Value;
            var k0 = keyframes[keyI - 1];
            var k1 = keyframes[keyI + 0];
            var t = (time - k0.Time) / (k1.Time - k0.Time);
            if (t >= 1f) return k1.Value;
            switch (k0.Interpolation) {
                case CurveInterpolation.Step: return k0.Value;
                case CurveInterpolation.Linear: return DualQuaternion.Slerp(k0.Value, k1.Value, t);
            }

            var invK01 = DualQuaternion.Inverse(k1.Value) * k0.Value;
            return DualQuaternion.Slerp(
                DualQuaternion.Slerp(k0.Value, k1.Value, t),
                DualQuaternion.Slerp(k0.OutTangent * invK01 * k0.Value, k1.InTangent * invK01 * k1.Value, t),
                2 * t * (1 - t));
        }

    }
}
