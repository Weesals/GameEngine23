using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Utility {
    /// <summary>
    /// Trigonometry and whatnot for fixed point types
    /// </summary>
    public static partial class FixedMath {

        public static Int2 Int2NaN = new Int2(int.MinValue, int.MinValue);

        public static uint Multiply(uint v0, uint v1, int bitBase = 16) {
            return (uint)(((ulong)v0 * v1) >> bitBase);
        }
        public static uint Divide(uint v0, uint v1, int bitBase = 16) {
            return (uint)(v0 / v1) << bitBase;
        }

        public static Int2 Multiply(Int2 v, int mul, int bitBase = 16) {
            return new Int2(
                (int)(((long)v.X * mul) >> bitBase),
                (int)(((long)v.Y * mul) >> bitBase)
            );
        }

        public static int Distance(Int2 v1, Int2 v2, int scalar = 1) {
            return Length(v1 - v2, scalar);
        }
        public static int Length(Int2 v1, int scalar = 1) {
            return (int)SqrtFastI((uint)(Int2.Dot(v1, v1) * scalar));
        }
        public static Int2 MoveToward(Int2 from, Int2 to, int amount) {
            var delta = to - from;
            var deltaLen2 = (long)delta.X * delta.X + (long)delta.Y * delta.Y;
            var deltaLen = SqrtFastL((ulong)deltaLen2);
            if (amount >= deltaLen) return to;
            var x = from.X + (long)delta.X * amount / deltaLen;
            var y = from.Y + (long)delta.Y * amount / deltaLen;
            return new Int2((int)x, (int)y);
        }

        public static int MultiplyRatio(int value, int numerator, int divisor) {
            return (int)((long)value * numerator / divisor);
        }
        public static Int2 MultiplyRatio(Int2 value, int numerator, int divisor) {
            return new Int2(
                MultiplyRatio(value.X, numerator, divisor),
                MultiplyRatio(value.Y, numerator, divisor)
            );
        }
        public static Int3 MultiplyRatio(Int3 value, int numerator, int divisor) {
            return new Int3(
                MultiplyRatio(value.X, numerator, divisor),
                MultiplyRatio(value.Y, numerator, divisor),
                MultiplyRatio(value.Z, numerator, divisor)
            );
        }
        public static int Lerp(int from, int to, int numerator, int divisor) {
            return from + (int)((long)(to - from) * numerator / divisor);
        }
        public static Int2 Lerp(Int2 from, Int2 to, int numerator, int divisor) {
            return from + MultiplyRatio(to - from, numerator, divisor);
        }
        public static Int3 Lerp(Int3 from, Int3 to, int numerator, int divisor) {
            return from + MultiplyRatio(to - from, numerator, divisor);
        }

        public static uint SqrtFastI(uint val) {
            if (val <= 1) return val;

            uint place = 0x40000000;
            while (place > val) { place >>= 2; }

            uint remainder = val;
            uint root = 0;
            while (place != 0) {
                if (remainder >= root + place) {
                    remainder -= root + place;
                    root |= place << 1;
                }
                root >>= 1;
                place >>= 2;
            }
            // Rounding (remainder > (2 * root + 1) / 2)
            // (2r+1) comes from (r+1)(r+1) - r*r
            if (remainder > root) ++root;
            return root;
        }
        public static uint SqrtFastL(ulong val) {
            if (val <= 1) return (uint)val;

            ulong place = 0x40000000;
            if (val > place) place <<= 16;
            if (val > place) place <<= 16;
            while (place > val) { place >>= 2; }

            ulong remainder = val;
            ulong root = 0;
            while (place != 0) {
                if (remainder >= root + place) {
                    remainder -= root + place;
                    root |= place << 1;
                }
                root >>= 1;
                place >>= 2;
            }
            // Rounding (remainder > (2 * root + 1) / 2)
            // (2r+1) comes from (r+1)(r+1) - r*r
            if (remainder > root) ++root;
            return (uint)root;
        }

    }
}
