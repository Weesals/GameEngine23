using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public struct FloatKeyframe {
        public float Time;
        public float Value;
        public float InTangent, OutTangent;
        public FloatKeyframe(float time, float value, float inTan = 0f, float outTan = 0f) {
            Time = time;
            Value = value;
            InTangent = inTan;
            OutTangent = outTan;
        }
    }
    public class FloatCurve {
        private FloatKeyframe[] keyframes = Array.Empty<FloatKeyframe>();

        public FloatKeyframe[] Keyframes => keyframes;

        public FloatCurve() { }
        public FloatCurve(params FloatKeyframe[] _keyframes) {
            keyframes = _keyframes;
        }

        public float Evaluate(float time) {
            int min = 0, max = keyframes.Length - 1;
            while (min < max) {
                var mid = (min + max) / 2;
                if (keyframes[mid].Time < time) {
                    min = mid + 1;
                } else {
                    max = mid;
                }
            }
            if (min == 0) return keyframes[min].Value;
            var k0 = keyframes[min - 1];
            var k1 = keyframes[min + 0];
            var t = (time - k0.Time) / (k1.Time - k0.Time);
            if (t > 1f) return k1.Value;
            float duration = k1.Time - k0.Time;

            float t2 = t * t, t3 = t2 * t;

            float a = 2f * t3 - 3f * t2 + 1f;
            float b = t3 - 2f * t2 + t;
            float c = t3 - t2;
            float d = -2f * t3 + 3f * t2;

            return a * k0.Value
                + (b * k0.OutTangent + c * k1.InTangent) * duration
                + d * k1.Value;
        }

        public static FloatCurve MakeSmoothStep() {
            return new FloatCurve(
                new FloatKeyframe(0f, 0f, 0f, 0f),
                new FloatKeyframe(1f, 1f, 0f, 0f)
            );
        }

        public static FloatCurve MakeLinear() {
            return new FloatCurve(
                new FloatKeyframe(0f, 0f, 1f, 1f),
                new FloatKeyframe(1f, 1f, 1f, 1f)
            );
        }
    }
}
