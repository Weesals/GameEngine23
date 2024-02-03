using System;
using System.Diagnostics;

namespace Weesals.Utility {

    using XRRT = System.Int32;
    using XRT = Fixed16_16;

    /// <summary>
    /// This is a 32 bit deterministic fixed-point value
    /// It uses 16 bits for the integral, and 16 bits for the fractional
    /// </summary>
    public struct Fixed16_16 {

        public const int SHIFT_AMOUNT = 16;
        public const XRRT FractionalMask = ((1 << SHIFT_AMOUNT) - 1);
        public const XRRT RawIdentity = 1 << SHIFT_AMOUNT;
        public static readonly XRT Zero = new XRT(0);
        public static readonly XRT Half = new XRT(1 << (SHIFT_AMOUNT - 1));
        public static readonly XRT One = new XRT(1 << SHIFT_AMOUNT);
        public static readonly XRT NaN = new XRT((XRRT)0x0badc0de);
        public static readonly XRT MaxValue = new XRT(int.MaxValue);
        public static readonly XRT MinValue = new XRT(int.MinValue);

        #region PI, DoublePI
        public const int PI_I = 205887; //PI x 2^16
        public const int PHI_I = 106039; //PI x 2^16
        public static readonly XRT PI = new XRT(PI_I);
        public static readonly XRT HalfPI = new XRT(PI_I / 2);
        public static readonly XRT TwoPI = new XRT(PI_I * 2);
        public static readonly XRT DegToRad = new XRT(PI_I / 180);
        public static readonly XRT RadToDeg = (XRT)180 / PI;
        public static readonly XRT Phi = new XRT(PHI_I);

        internal static readonly XRT NearZero = new XRT(8);
        #endregion

        public readonly XRRT RawValue;
        public Fixed16_16(XRRT rawValue) { RawValue = rawValue; }

        public XRT Negative { get { return new XRT(-RawValue); } }
        public bool IsNaN { get { return RawValue == NaN.RawValue; } }

        // Unary
        public static XRT operator +(XRT r) { return r; }
        public static XRT operator -(XRT r) { return new XRT(-r.RawValue); }

        // Binary
        public static XRT operator *(XRT one, XRT other) {
            var integral = (long)(one.RawValue >> SHIFT_AMOUNT) * other.RawValue;
            var fractional = (long)(one.RawValue & FractionalMask) * other.RawValue;
            return new XRT((XRRT)(integral + (fractional >> SHIFT_AMOUNT)));
        }
        public static XRT operator *(XRT one, int multi) { return new XRT(one.RawValue * multi); }
        public static XRT operator *(int multi, XRT one) { return new XRT(one.RawValue * multi); }

        public static XRT operator /(XRT one, XRT other) {
            return new XRT((XRRT)(((long)one.RawValue << SHIFT_AMOUNT) / (other.RawValue)));
        }
        public static XRT operator /(XRT one, int divisor) { return new XRT(one.RawValue / divisor); }
        public static XRT operator /(int value, XRT one) { return (XRT)value / one; }

        public static XRT operator %(XRT one, XRT other) { return new XRT((one.RawValue) % (other.RawValue)); }
        public static XRT operator %(XRT one, int divisor) { return one % (XRT)divisor; }
        public static XRT operator %(int divisor, XRT one) { return (XRT)divisor % one; }

        // Addition
        public static XRT operator +(XRT one, XRT other) { return new XRT(one.RawValue + other.RawValue); }
        public static XRT operator +(XRT one, int other) { return new XRT(one.RawValue + ((XRRT)other << SHIFT_AMOUNT)); }
        public static XRT operator +(int other, XRT one) { return new XRT(((XRRT)other << SHIFT_AMOUNT) + one.RawValue); }

        // Subtraction
        public static XRT operator -(XRT one, XRT other) { return new XRT(one.RawValue - other.RawValue); }
        public static XRT operator -(XRT one, int other) { return new XRT(one.RawValue - ((XRRT)other << SHIFT_AMOUNT)); }
        public static XRT operator -(int other, XRT one) { return new XRT(((XRRT)other << SHIFT_AMOUNT) - one.RawValue); }

        public static bool operator ==(XRT one, XRT other) { return one.RawValue == other.RawValue; }
        public static bool operator ==(XRT one, int other) { return one == (XRT)other; }
        public static bool operator ==(int other, XRT one) { return (XRT)other == one; }

        public static bool operator !=(XRT one, XRT other) { return one.RawValue != other.RawValue; }
        public static bool operator !=(XRT one, int other) { return one != (XRT)other; }
        public static bool operator !=(int other, XRT one) { return (XRT)other != one; }

        public static bool operator >=(XRT one, XRT other) { return one.RawValue >= other.RawValue; }
        public static bool operator >=(XRT one, int other) { return one >= (XRT)other; }
        public static bool operator >=(int other, XRT one) { return (XRT)other >= one; }

        public static bool operator <=(XRT one, XRT other) { return one.RawValue <= other.RawValue; }
        public static bool operator <=(XRT one, int other) { return one <= (XRT)other; }
        public static bool operator <=(int other, XRT one) { return (XRT)other <= one; }

        public static bool operator >(XRT one, XRT other) { return one.RawValue > other.RawValue; }
        public static bool operator >(XRT one, int other) { return one > (XRT)other; }
        public static bool operator >(int other, XRT one) { return (XRT)other > one; }

        public static bool operator <(XRT one, XRT other) { return one.RawValue < other.RawValue; }
        public static bool operator <(XRT one, int other) { return one < (XRT)other; }
        public static bool operator <(int other, XRT one) { return (XRT)other < one; }

        public static XRT operator <<(XRT one, int Amount) { return new XRT(one.RawValue << Amount); }
        public static XRT operator >>(XRT one, int Amount) { return new XRT(one.RawValue >> Amount); }

        public static explicit operator int(XRT src) { return (int)(src.RawValue >> SHIFT_AMOUNT); }
        public static implicit operator XRT(int src) { return new XRT((XRRT)src << SHIFT_AMOUNT); }

        // Rounds towards 0 (same as float)
        public int ToInt() {
            if (RawValue < 0) return -(int)((-RawValue) >> SHIFT_AMOUNT);
            else return (int)(RawValue >> SHIFT_AMOUNT);
        }
        public float ToFloat() { return (float)RawValue / (float)RawIdentity; }
        public double ToDouble() { return (double)RawValue / (double)RawIdentity; }

        public bool Equals(XRT other) { return other.RawValue == RawValue; }
        public bool Equals(XRT r1, XRT r2) { return r1.RawValue == r2.RawValue; }
        public override bool Equals(object? obj) { return obj is XRT value && value.RawValue == this.RawValue; }
        public int GetHashCode(XRT r) { return r.GetHashCode(); }

        public override int GetHashCode() { return RawValue.GetHashCode(); }
        public override string ToString() { return ToDouble().ToString(); }

    }


    /// <summary>
    /// Trigonometry and whatnot for fixed point types
    /// Adapted from RTS4, not fully tested.
    /// </summary>
    public static partial class FixedMath {

        #region Internal

        private static readonly int[] SIN_TABLE = {
            0,1144,2287,3430,4572,5712,6850,7987,9121,10252,
            11380,12505,13626,14742,15855,16962,18064,19161,20252,21336,
            22415,23486,24550,25607,26656,27697,28729,29753,30767,31772,
            32768,33754,34729,35693,36647,37590,38521,39441,40348,41243,
            42126,42995,43852,44695,45525,46341,47143,47930,48703,49461,
            50203,50931,51643,52339,53020,53684,54332,54963,55578,56175,
            56756,57319,57865,58393,58903,59396,59870,60326,60764,61183,
            61584,61966,62328,62672,62997,63303,63589,63856,64104,64332,
            64540,64729,64898,65048,65177,65287,65376,65446,65496,65526,
            65536,
        };

        #endregion

        #region Fixed16_16

        public static long MultiplyLong(long lvalue, XRT fvalue) {
            var integral = (long)(fvalue.RawValue >> XRT.SHIFT_AMOUNT) * lvalue;
            var fractional = (long)(fvalue.RawValue & XRT.FractionalMask) * lvalue;
            return integral + (fractional >> XRT.SHIFT_AMOUNT);
        }

        #region Sqrt
        public static XRT Sqrt(XRT f, int NumberOfIterations) {
            RequirePositive(f, "Input Error");
            if (f.RawValue == 0)
                return (XRT)0;
            XRT k = (f + XRT.One) >> 1;
            for (int i = 0; i < NumberOfIterations; i++)
                k = (k + (f / k)) >> 1;

            RequirePositive(k, "Overflow");
            return k;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void RequirePositive(XRT f, string name) {
            if (f.RawValue < 0) //NaN in Math.Sqrt
                throw new ArithmeticException(name);
        }

        public static XRT SqrtFast(XRT f) {
            var fShifted = f.RawValue;
            var k = (f.RawValue + XRT.One.RawValue) >> 1;
            for (int i = 0; i < 9; i++)
                k = (k + (fShifted / k)) >> 1;
            return new XRT(k);
        }
        public static XRT Sqrt(XRT f) {
            byte numberOfIterations = 8;
            if (f.RawValue > (0x64000 >> (16 - XRT.SHIFT_AMOUNT)))
                numberOfIterations = 12;
            if (f.RawValue > (0x3e8000 >> (16 - XRT.SHIFT_AMOUNT)))
                numberOfIterations = 16;
            return Sqrt(f, numberOfIterations);
        }
        #endregion

        #region Sin
        public static XRT Sin(XRT i) {
            const int Granularity = 16;
            const int NinetyDegrees = ((int)XRT.PI_I * 2 * Granularity / 4);
            bool negative = i.RawValue < 0;
            var r = (int)(negative ? -i.RawValue : i.RawValue);
            int q = (4 * r / (int)(XRT.PI_I * 2)) % 4;
            r = (Granularity * r) % NinetyDegrees;
            negative ^= q >= 2; // Negate result for last 2 quadrants
            if (q % 2 == 1) r = NinetyDegrees - r;  // Mirrored for every 2nd quadrant

            // Renormalize to 90 items (in LUT)
            // == r * 90 / (PI/2)
            r = r * 90 * 2 / ((int)XRT.PI_I);

            // The index and interpolation
            int ii = r / Granularity;
            int il = r % Granularity;
            int v = SIN_TABLE[ii] + (SIN_TABLE[ii + 1] - SIN_TABLE[ii]) * il / Granularity;
            v >>= (16 - XRT.SHIFT_AMOUNT);
            return new XRT(negative ? -v : v);
        }

        #endregion

        #endregion
        #region Cos, Tan, Asin
        public static XRT Cos(XRT i) {
            return Sin(i + XRT.HalfPI);
        }

        public static XRT Tan(XRT i) {
            return Sin(i) / Cos(i);
        }

        public static XRT Asin(XRT F) {
            bool isNegative = F < 0;
            F = Abs(F);

            if (F > XRT.One)
                throw new ArithmeticException("Bad Asin Input:" + F.ToDouble());

            XRT r1 = new XRT(145103 >> (24 - XRT.SHIFT_AMOUNT));
            r1 = r1 * F - new XRT(599880 >> (24 - XRT.SHIFT_AMOUNT));
            r1 = r1 * F + new XRT(1420468 >> (24 - XRT.SHIFT_AMOUNT));
            r1 = r1 * F - new XRT(3592413 >> (24 - XRT.SHIFT_AMOUNT));
            r1 = r1 * F + new XRT(26353447 >> (24 - XRT.SHIFT_AMOUNT));
            XRT f2 = XRT.PI / 2 - (Sqrt(XRT.One - F) * r1);

            return isNegative ? f2.Negative : f2;
        }
        public static XRT Acos(XRT F) {
            return Asin(F + XRT.PI / 2);
        }
        #endregion

        #region ATan, ATan2
        public static XRT Atan(XRT F) {
            if (F.RawValue > 127 << XRT.SHIFT_AMOUNT) return XRT.HalfPI;
            if (F.RawValue < -127 << XRT.SHIFT_AMOUNT) return -XRT.HalfPI;
            return Asin(Clamp(F / Sqrt(XRT.One + (F * F)), -XRT.One, XRT.One));
        }

        public static XRT Atan2(XRT F1, XRT F2) {
            if (Abs(F2) < Abs(F1)) return XRT.HalfPI - Atan2(F2, F1);
            if (F2 == 0) return (XRT)0;

            var r = Atan(F1 / F2);
            if (F2 < 0) r += XRT.PI;
            return r;
        }
        #endregion

        #region Abs
        public static XRT Abs(XRT F) {
            if (F < 0)
                return F.Negative;
            else
                return F;
        }
        #endregion

        #region Rounding
        public static int RoundToInt(XRT v) {
            var raw = v.RawValue;
            bool negative = raw < 0;
            if (negative) raw = -raw;
            raw += 1 << (XRT.SHIFT_AMOUNT - 1);
            raw >>= XRT.SHIFT_AMOUNT;
            var value = negative ? -(int)raw : (int)raw;
            //Debug.Assert(value == v.RoundedToInt, "This function is incorrect");
            return value;
        }
        public static int CeilToInt(XRT v) {
            var raw = v.RawValue;
            if (v.RawValue > 0) raw += (1 << XRT.SHIFT_AMOUNT) - 1;
            return (int)(raw >> XRT.SHIFT_AMOUNT);
        }
        public static int FloorToInt(XRT v) {
            var raw = v.RawValue;
            if (v.RawValue < 0) raw -= (1 << XRT.SHIFT_AMOUNT) - 1;
            return (int)(raw >> XRT.SHIFT_AMOUNT);
        }
        #endregion

        #region Range
        public static XRT Clamp01(XRT value) {
            return Clamp(value, XRT.Zero, XRT.One);
        }
        public static XRT Clamp(XRT value, XRT min, XRT max) {
            return value < min ? min :
                value > max ? max :
                value;
        }
        public static XRT Lerp(XRT from, XRT to, XRT v) {
            return from + (to - from) * Clamp01(v);
        }

        public static XRT Min(XRT v1, XRT v2) {
            return v1 < v2 ? v1 : v2;
        }
        public static XRT Max(XRT v1, XRT v2) {
            return v1 > v2 ? v1 : v2;
        }

        public static XRT MoveToward(XRT v1, XRT v2, XRT delta) {
            var vdelta = v2 - v1;
            if (Abs(vdelta) < delta) return v2;
            return vdelta < 0 ? v1 - delta : v1 + delta;
        }
        public static XRT MoveTowardWrapped(XRT v1, XRT v2, XRT delta, XRT wrap) {
            var vdelta = v2 - v1;
            if (Abs(vdelta) > wrap / 2) vdelta -= RoundToInt(vdelta / wrap) * wrap;
            if (Abs(vdelta) < delta) return v2;
            return vdelta < 0 ? v1 - delta : v1 + delta;
        }
        #endregion

    }
}
