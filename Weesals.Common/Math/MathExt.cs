using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace System {
    public static partial class MathExt {
        public static int FloorToInt(float v) { int i = (int)v; return i - (i > v ? 1 : 0); }
        public static int CeilToInt(float v) { int i = (int)v; return i + (i < v ? 1 : 0); }
        public static int RoundToInt(float v) { return (int)(v + (v >= 0f ? 0.5f : -0.5f)); }

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

        public static Quaternion QuaternionFromDirection(Vector3 v1, Vector3 v2) {
            var q = new Quaternion(Vector3.Cross(v1, v2),
                MathF.Sqrt(v1.LengthSquared() * v2.LengthSquared()) + Vector3.Dot(v1, v2));
            return Quaternion.Normalize(q);
        }
    }
}

namespace Weesals.Engine {

    public static class VectorExt {
        public static Vector2 Round(this Vector2 v) { return new Vector2(MathF.Abs(v.X), MathF.Abs(v.Y)); }
        public static Vector3 Round(this Vector3 v) { return new Vector3(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z)); }
        public static Vector2 Floor(this Vector2 v) { return new Vector2(MathF.Floor(v.X), MathF.Floor(v.Y)); }
        public static Vector3 Floor(this Vector3 v) { return new Vector3(MathF.Floor(v.X), MathF.Floor(v.Y), MathF.Floor(v.Z)); }
        public static Vector2 Ceil(this Vector2 v) { return new Vector2(MathF.Ceiling(v.X), MathF.Ceiling(v.Y)); }
        public static Vector3 Ceil(this Vector3 v) { return new Vector3(MathF.Ceiling(v.X), MathF.Ceiling(v.Y), MathF.Ceiling(v.Z)); }

        public static Vector2 Mod(this Vector2 v, float o) { return new Vector2(v.X % o, v.Y % o); }
        public static Vector3 Mod(this Vector3 v, float o) { return new Vector3(v.X % o, v.Y % o, v.Z % o); }

        public static Vector2 YX(this Vector2 v) { return new Vector2(v.Y, v.X); }
        public unsafe static Vector2 toxy(this Vector3 v) {
            return *(Vector2*)&v.X;
        }
        public unsafe static Vector2 toxz(this Vector3 v) {
            return new Vector2(v.X, v.Z);
        }
        public unsafe static Vector2 toyz(this Vector3 v) {
            return *(Vector2*)&v.Y;
        }
        public unsafe static Vector3 toxzy(this Vector3 v) {
            return new Vector3(v.X, v.Z, v.Y);
        }
        public unsafe static Vector2 toxy(this Vector4 v) {
            return *(Vector2*)&v.X;
        }
        public unsafe static Vector2 toyz(this Vector4 v) {
            return *(Vector2*)&v.Y;
        }
        public unsafe static Vector2 tozw(this Vector4 v) {
            return *(Vector2*)&v.Z;
        }
        public unsafe static Vector3 toxyz(this Vector4 v) {
            return *(Vector3*)&v.X;
        }

        public unsafe static void toxy(this ref Vector3 v, Vector2 set) {
            Unsafe.As<Vector3, Vector2>(ref v) = set;
        }
        public unsafe static void toyz(this ref Vector3 v, Vector2 set) {
            Unsafe.As<Vector3, Vector2>(ref Unsafe.AddByteOffset(ref v, 4)) = set;
        }
        public unsafe static void toxy(this ref Vector4 v, Vector2 set) {
            Unsafe.As<Vector4, Vector2>(ref v) = set;
        }
        public unsafe static void toyz(this ref Vector4 v, Vector2 set) {
            Unsafe.As<Vector4, Vector2>(ref Unsafe.AddByteOffset(ref v, 4)) = set;
        }
        public unsafe static void tozw(this ref Vector4 v, Vector2 set) {
            Unsafe.As<Vector4, Vector2>(ref Unsafe.AddByteOffset(ref v, 8)) = set;
        }
        public unsafe static void toxyz(this ref Vector4 v, Vector3 set) {
            Unsafe.As<Vector4, Vector3>(ref v) = set;
        }

        public static Vector4 toxzyw(this Vector4 v) {
            return new Vector4(v.X, v.Z, v.Y, v.W);
        }
        public static Vector4 tozywx(this Vector4 v) {
            return new Vector4(v.Z, v.Y, v.W, v.X);
        }
        public static Vector4 toyzwx(this Vector4 v) {
            return new Vector4(v.Y, v.Z, v.W, v.X);
        }
        public static Vector4 toyxwz(this Vector4 v) {
            return new Vector4(v.Y, v.X, v.W, v.Z);
        }
        public static Vector4 toxyzw(this Vector4 v) {
            return new Vector4(v.X, v.Y, v.Z, v.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cmin(this Vector2 v) {
            return MathF.Min(v.X, v.Y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cmax(this Vector2 v) {
            return MathF.Max(v.X, v.Y);
        }
        public static float cmin(this Vector4 v) {
            return MathF.Min(MathF.Min(v.X, v.Y), MathF.Min(v.Z, v.W));
        }
        public static float cmax(this Vector4 v) {
            return MathF.Max(MathF.Max(v.X, v.Y), MathF.Max(v.Z, v.W));
        }

        public static Vector3 AppendY(this Vector2 v, float y = 0.0f) {
            return new Vector3(v.X, y, v.Y);
        }
        public static Vector3 AppendZ(this Vector2 v, float z = 0.0f) {
            return new Vector3(v.X, v.Y, z);
        }
    }

    public struct Half {
        public ushort Bits;
        unsafe public Half(float value) {
            if (value == 0f) { Bits = 0; return; }
            uint valueBits = *(uint*)&value;
            Bits = (ushort)(
                ((valueBits >> 16) & 0x8000) |
                (((valueBits >> 13) & (0x3fc00 | 0x03ff)) - (112 << 10))
            );
        }
        unsafe public static implicit operator float(Half h) {
            if (h.Bits == 0) return 0.0f;
            uint valueBits =
                (((uint)h.Bits << 16) & 0x80000000) |
                ((((uint)h.Bits << 13) & (0x0f800000 | 0x03ff0000)) + ((uint)112 << 23));
            return *(float*)&valueBits;
        }
        unsafe public static implicit operator Half(float f) { return new Half(f); }
        public override string ToString() { return ((float)this).ToString() + "h"; }
    }
    public struct Half2 {
        public Half X, Y;
        public Half2(Vector2 v) { X = v.X; Y = v.Y; }
        public static implicit operator Vector2(Half2 h) { return new Vector2(h.X, h.Y); }
        public static implicit operator Half2(Vector2 v) { return new Half2(v); }
        public override string ToString() { return $"<{X}, {Y}>"; }
    }
    public struct Half3 {
        public Half X, Y, Z;
        public Half3(Vector3 v) { X = v.X; Y = v.Y; Z = v.Z; }
        public static implicit operator Vector3(Half3 h) { return new Vector3(h.X, h.Y, h.Z); }
        public static implicit operator Half3(Vector3 v) { return new Half3(v); }
        public override string ToString() { return $"<{X}, {Y}, {Z}>"; }
    }
    public struct Half4 {
        public Half X, Y, Z, W;
        public Half4(Vector4 v) { X = v.X; Y = v.Y; Z = v.Z; }
        public static implicit operator Vector4(Half4 h) { return new Vector4(h.X, h.Y, h.Z, h.W); }
        public static implicit operator Half4(Vector4 v) { return new Half4(v); }
        public override string ToString() { return $"<{X}, {Y}, {Z}, {W}>"; }
    }

    [TypeConverter(typeof(Converters.Int2Converter))]
    public struct Int2 : IEquatable<Int2> {
        public int X, Y;
        public int LengthSquared => (X * X + Y * Y);
        public float Length => MathF.Sqrt(LengthSquared);
        public int LengthI => (int)MathExt.SqrtFastI((uint)LengthSquared);
        public Int2 YX => new Int2(Y, X);
        public Int2(int v) : this(v, v) { }
        public Int2(int _x, int _y) { X = _x; Y = _y; }
        public Int2(Vector2 o) { X = ((int)o.X); Y = ((int)o.Y); }
        public int this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (index <= 0 ? ref X : ref Y);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => (index <= 0 ? ref X : ref Y) = value;
        }
        public bool Equals(Int2 o) { return X == o.X && Y == o.Y; }
        public static Int2 operator -(Int2 o1) { return new Int2(-o1.X, -o1.Y); }
        public static Int2 operator +(Int2 o1, Int2 o2) { return new Int2(o1.X + o2.X, o1.Y + o2.Y); }
	    public static Int2 operator -(Int2 o1, Int2 o2) { return new Int2(o1.X - o2.X, o1.Y - o2.Y); }
	    public static Int2 operator *(Int2 o1, Int2 o2) { return new Int2(o1.X * o2.X, o1.Y * o2.Y); }
	    public static Int2 operator /(Int2 o1, Int2 o2) { return new Int2(o1.X / o2.X, o1.Y / o2.Y); }
	    public static Int2 operator +(Int2 o1, int o) { return new Int2(o1.X + o, o1.Y + o); }
	    public static Int2 operator -(Int2 o1, int o) { return new Int2(o1.X - o, o1.Y - o); }
	    public static Int2 operator *(Int2 o1, int o) { return new Int2(o1.X * o, o1.Y * o); }
	    public static Int2 operator /(Int2 o1, int o) { return new Int2(o1.X / o, o1.Y / o); }
	    public static bool operator ==(Int2 o1, Int2 o2) { return o1.X == o2.X && o1.Y == o2.Y; }
	    public static bool operator !=(Int2 o1, Int2 o2) { return o1.X != o2.X || o1.Y != o2.Y; }
        public static Int2 operator >>(Int2 o1, int o) { return new Int2(o1.X >> o, o1.Y >> o); }
        public static Int2 operator <<(Int2 o1, int o) { return new Int2(o1.X << o, o1.Y << o); }
        public static Int2 operator &(Int2 o1, Int2 o) { return new Int2(o1.X & o.X, o1.Y & o.Y); }
        public static Int2 operator |(Int2 o1, Int2 o) { return new Int2(o1.X | o.X, o1.Y | o.Y); }
        public static Int2 operator ^(Int2 o1, int o) { return new Int2(o1.X ^ o, o1.Y ^ o); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Min(Int2 v1, Int2 v2) { return new Int2(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Max(Int2 v1, Int2 v2) { return new Int2(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Abs(Int2 v) { return new Int2(Math.Abs(v.X), Math.Abs(v.Y)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Clamp(Int2 v, Int2 min, Int2 max) { return new Int2(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y)); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DotI(Int2 v1, Int2 v2) { return v1.X * v2.X + v1.Y * v2.Y; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Dot(Int2 v1, Int2 v2) { return (long)v1.X * v2.X + (long)v1.Y * v2.Y; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CSum(Int2 v) { return v.X + v.Y; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CMul(Int2 v) { return v.X * v.Y; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long DistanceSquared(Int2 v1, Int2 v2) { v1 -= v2; return Dot(v1, v1); }

        public static Int2 FloorToInt(Vector2 v) { return new Int2(MathExt.FloorToInt(v.X), MathExt.FloorToInt(v.Y)); }
        public static Int2 RoundToInt(Vector2 v) { return new Int2(MathExt.RoundToInt(v.X), MathExt.RoundToInt(v.Y)); }
        public static Int2 CeilToInt(Vector2 v) { return new Int2(MathExt.CeilToInt(v.X), MathExt.CeilToInt(v.Y)); }

        public static implicit operator Int2(Vector2 v) { return new Int2(v); }
        public static implicit operator Int2(int v) { return new Int2(v, v); }
        public static implicit operator Vector2(Int2 v) { return new Vector2((float)v.X, (float)v.Y); }
        public override bool Equals([NotNullWhen(true)] object? obj) { return obj is Int2 i2 && i2 == this; }
        public override int GetHashCode() { return (X * 887) + Y; }
        public override string ToString() { return "<" + X + "," + Y + ">"; }
        public static readonly Int2 Zero = new Int2(0);
        public static readonly Int2 One = new Int2(1);
    }
    public struct Int3 : IEquatable<Int3> {
        public int X, Y, Z;
        public Int3(int v) : this(v, v, v) { }
        public Int3(int _x, int _y, int _z) { X = _x; Y = _y; Z = _z; }
        public Int3(Int2 v, int _z) { X = v.X; Y = v.Y; Z = _z; }
        public Int3(Vector3 o) : this((int)o.X, (int)o.Y, (int)o.Z) { }
        public bool Equals(Int3 o) { return X == o.X && Y == o.Y && Z == o.Z; }
        public override string ToString() { return $"{X},{Y},{Z}"; }
        unsafe public Int2 XY { get => new Int2(X, Y); set { X = value.X; Y = value.Y; } }
        unsafe public Int2 XZ { get => new Int2(X, Z); set { X = value.X; Z = value.Y; } }
        public static Int3 operator +(Int3 v, Int3 o) { return new Int3(v.X + o.X, v.Y + o.Y, v.Z + o.Z); }
        public static Int3 operator -(Int3 v, Int3 o) { return new Int3(v.X - o.X, v.Y - o.Y, v.Z - o.Z); }
        public static Int3 operator *(Int3 v, Int3 o) { return new Int3(v.X * o.X, v.Y * o.Y, v.Z * o.Z); }
        public static Int3 operator /(Int3 v, Int3 o) { return new Int3(v.X / o.X, v.Y / o.Y, v.Z / o.Z); }
        public static Int3 operator +(Int3 v, int o) { return new Int3(v.X + o, v.Y + o, v.Z + o); }
        public static Int3 operator -(Int3 v, int o) { return new Int3(v.X - o, v.Y - o, v.Z - o); }
        public static Int3 operator *(Int3 v, int o) { return new Int3(v.X * o, v.Y * o, v.Z * o); }
        public static Int3 operator /(Int3 v, int o) { return new Int3(v.X / o, v.Y / o, v.Z / o); }
        public static bool operator ==(Int3 o1, Int3 o2) { return o1.X == o2.X && o1.Y == o2.Y && o1.Z == o2.Z; }
        public static bool operator !=(Int3 o1, Int3 o2) { return o1.X != o2.X || o1.Y != o2.Y || o1.Z != o2.Z; }
        public static Int3 operator >>(Int3 o1, int o) { return new Int3(o1.X >> o, o1.Y >> o, o1.Z >> o); }
        public static Int3 operator <<(Int3 o1, int o) { return new Int3(o1.X << o, o1.Y << o, o1.Z << o); }
        public static Int3 operator &(Int3 o1, Int3 o) { return new Int3(o1.X & o.X, o1.Y & o.Y, o1.Z & o.Z); }
        public static Int3 operator |(Int3 o1, Int3 o) { return new Int3(o1.X | o.X, o1.Y | o.Y, o1.Z | o.Z); }
        public static Int3 operator ^(Int3 o1, int o) { return new Int3(o1.X ^ o, o1.Y ^ o, o1.Z ^ o); }

        public static Int3 FloorToInt(Vector3 v) { return new Int3(MathExt.FloorToInt(v.X), MathExt.FloorToInt(v.Y), MathExt.FloorToInt(v.Z)); }
        public static Int3 RoundToInt(Vector3 v) { return new Int3(MathExt.RoundToInt(v.X), MathExt.RoundToInt(v.Y), MathExt.RoundToInt(v.Z)); }
        public static Int3 CeilToInt(Vector3 v) { return new Int3(MathExt.CeilToInt(v.X), MathExt.CeilToInt(v.Y), MathExt.CeilToInt(v.Z)); }

        public static Int3 Min(Int3 v1, Int3 v2) { return new Int3(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y), Math.Min(v1.Z, v2.Z)); }
        public static Int3 Max(Int3 v1, Int3 v2) { return new Int3(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y), Math.Max(v1.Z, v2.Z)); }
        public static Int3 Clamp(Int3 v, Int3 min, Int3 max) {
            return new Int3(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y), Math.Clamp(v.Z, min.Z, max.Z));
        }
        public static int DotI(Int3 v1, Int3 v2) { return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z; }
        public static long Dot(Int3 v1, Int3 v2) { return (long)v1.X * v2.X + (long)v1.Y * v2.Y + (long)v1.Z * v2.Z; }
        public override bool Equals(object? obj) { return obj is Int3 o && o == this; }
        public override int GetHashCode() { return X + (Y * 7841) + (Z * 3509299); }

        public static implicit operator Int3(int v) { return new Int3(v); }
        public static implicit operator Vector3(Int3 v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
        public static explicit operator Int3(Vector3 v) { return new Int3(v); }
        public static readonly Int3 Zero = new Int3(0);
        public static readonly Int3 One = new Int3(1);
    }
    public struct Int4 : IEquatable<Int4> {
        public int X, Y, Z, W;
	    public Int4(int v) : this(v, v, v, v) { }
	    public Int4(int _x, int _y, int _z, int _w) { X = _x; Y = _y; Z = _z; W = _w; }
	    public Int4(Vector4 o) : this((int)o.X, (int)o.Y, (int)o.Z, (int)o.W) { }
        public bool Equals(Int4 o) { return X == o.X && Y == o.Y && Z == o.Z && W == o.W; }
        public override string ToString() { return $"<{X}, {Y}, {Z}, {W}>"; }
        public static Int4 operator +(Int4 v, Int4 o) { return new Int4(v.X + o.X, v.Y + o.Y, v.Z + o.Z, v.W + o.W); }
	    public static Int4 operator -(Int4 v, Int4 o) { return new Int4(v.X - o.X, v.Y - o.Y, v.Z - o.Z, v.W - o.W); }
	    public static Int4 operator *(Int4 v, Int4 o) { return new Int4(v.X * o.X, v.Y * o.Y, v.Z * o.Z, v.W * o.W); }
	    public static Int4 operator /(Int4 v, Int4 o) { return new Int4(v.X / o.X, v.Y / o.Y, v.Z / o.Z, v.W / o.W); }
	    public static Int4 operator +(Int4 v, int o) { return new Int4(v.X + o, v.Y + o, v.Z + o, v.W + o); }
	    public static Int4 operator -(Int4 v, int o) { return new Int4(v.X - o, v.Y - o, v.Z - o, v.W - o); }
	    public static Int4 operator *(Int4 v, int o) { return new Int4(v.X * o, v.Y * o, v.Z * o, v.W * o); }
	    public static Int4 operator /(Int4 v, int o) { return new Int4(v.X / o, v.Y / o, v.Z / o, v.W / o); }

        unsafe public ref int this[int index] {
            get { fixed (int* ptr = &X) return ref ptr[index]; }
            //return ref MemoryMarshal.Cast<Int4, int>(ref new Span<Int4>(this))[index];
        }

        public static Int4 FloorToInt(Vector4 v) { return new Int4(MathExt.FloorToInt(v.X), MathExt.FloorToInt(v.Y), MathExt.FloorToInt(v.Z), MathExt.FloorToInt(v.W)); }
        public static Int4 RoundToInt(Vector4 v) { return new Int4(MathExt.RoundToInt(v.X), MathExt.RoundToInt(v.Y), MathExt.RoundToInt(v.Z), MathExt.RoundToInt(v.W)); }
        public static Int4 CeilToInt(Vector4 v) { return new Int4(MathExt.CeilToInt(v.X), MathExt.CeilToInt(v.Y), MathExt.CeilToInt(v.Z), MathExt.CeilToInt(v.W)); }

        public static Int4 Min(Int4 v1, Int4 v2) { return new Int4(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y), Math.Min(v1.Z, v2.Z), Math.Min(v1.W, v2.W)); }
	    public static Int4 Max(Int4 v1, Int4 v2) { return new Int4(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y), Math.Max(v1.Z, v2.Z), Math.Max(v1.W, v2.W)); }
        public static Int4 Clamp(Int4 v, Int4 min, Int4 max) {
            return new Int4(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y), Math.Clamp(v.Z, min.Z, max.Z), Math.Clamp(v.W, min.W, max.W));
        }

        public static implicit operator Int4(int v) { return new Int4(v, v, v, v); }
        public static implicit operator Vector4(Int4 v) { return new Vector4((float)v.X, (float)v.Y, (float)v.Z, (float)v.W); }
    }

    public struct Byte4 : IEquatable<Byte4> {
        public byte X, Y, Z, W;
        public Byte4(byte x, byte y, byte z, byte w) { X = x; Y = y; Z = z; W = w; }
        public Byte4(Vector4 v) { X = (byte)v.X; Y = (byte)v.Y; Z = (byte)v.Z; W = (byte)v.W; }
        public Vector4 ToVector4() { return new Vector4(X, Y, Z, W); }
        public bool Equals(Byte4 o) { return X == o.X && Y == o.Y && Z == o.Z && W == o.W; }
        public override string ToString() { return $"<{X}, {Y}, {Z}, {W}>"; }
        public override int GetHashCode() { return X + (Y << 8) + (Z << 16) + (W << 24); }
        public static implicit operator Byte4(byte v) { return new Byte4(v, v, v, v); }
    }
    public struct Short2 : IEquatable<Short2> {
        public short X, Y;
        public Short2(short x, short y) { X = x; Y = y; }
        public Short2(Vector2 v) { X = (short)v.X; Y = (short)v.Y; }
        public Vector2 ToVector2() { return new Vector2(X, Y); }
        public bool Equals(Short2 o) { return X == o.X && Y == o.Y; }
        public override string ToString() { return $"<{X}, {Y}>"; }
        public override int GetHashCode() { return X + (Y << 16); }
        public static implicit operator Short2(short v) { return new Short2(v, v); }
    }
    public struct UShort2 : IEquatable<UShort2> {
        public ushort X, Y;
        public UShort2(ushort x, ushort y) { X = x; Y = y; }
        public UShort2(Vector2 v) { X = (ushort)v.X; Y = (ushort)v.Y; }
        public Vector2 ToVector2() { return new Vector2(X, Y); }
        public bool Equals(UShort2 o) { return X == o.X && Y == o.Y; }
        public override int GetHashCode() { return X + (Y << 16); }
        public override string ToString() { return $"<{X}, {Y}>"; }
        public static implicit operator UShort2(ushort v) { return new UShort2(v, v); }
    }

    public struct Color : IEquatable<Color> {
        public byte R, G, B, A;

        public unsafe uint Packed {
            get { fixed (byte* d = &R) return *(uint*)d; }
            set { fixed (byte* d = &R) *(uint*)d = value; }
        }
        public Color(uint packed) { Packed = packed; }
        public Color(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        public Color(Vector4 v) : this(
            (byte)Math.Clamp(255.0f * v.X, 0.0f, 255.0f),
            (byte)Math.Clamp(255.0f * v.Y, 0.0f, 255.0f),
            (byte)Math.Clamp(255.0f * v.Z, 0.0f, 255.0f),
            (byte)Math.Clamp(255.0f * v.W, 0.0f, 255.0f)) { }
        public Color(Vector3 v, byte a = byte.MaxValue) : this(
            (byte)Math.Clamp(255.0f * v.X, 0.0f, 255.0f),
            (byte)Math.Clamp(255.0f * v.Y, 0.0f, 255.0f),
            (byte)Math.Clamp(255.0f * v.Z, 0.0f, 255.0f),
            a) { }
        public Color WithAlpha(byte a) {
            return new Color() { R = R, G = G, B = B, A = a, };
        }
        public static implicit operator Vector4(Color c) {
            return new Vector4(c.R, c.G, c.B, c.A) * (1.0f / 255.0f);
        }
        public static implicit operator Vector3(Color c) {
            return new Vector3(c.R, c.G, c.B) * (1.0f / 255.0f);
        }
        public static Color operator *(Color c, float v) { return new Color((Vector4)c * v); }
        public static Color operator *(Color c1, Color c2) { return new Color((Vector4)c1 * c2); }
        public static readonly Color White = new Color(0xffffffff);
        public static readonly Color DarkGray = new Color(0xff444444);
        public static readonly Color Gray = new Color(0xff888888);
        public static readonly Color LightGray = new Color(0xffcccccc);
        public static readonly Color Black = new Color(0xff000000);
        public static readonly Color Clear = new Color(0x00000000);
        public static readonly Color Red = new Color(0xff0000ff);
        public static readonly Color Orange = new Color(0xff0088ff);
        public static readonly Color Yellow = new Color(0xff00ffff);
        public static readonly Color Green = new Color(0xff00ff00);
        public static readonly Color Cyan = new Color(0xffffff00);
        public static readonly Color Blue = new Color(0xffff0000);
        public static readonly Color Purple = new Color(0xffff00ff);

        public static bool operator == (Color c1, Color c2) { return c1.Equals(c2); }
        public static bool operator !=(Color c1, Color c2) { return !c1.Equals(c2); }
        public bool Equals(Color other) { return Packed == other.Packed; }
        public override bool Equals([NotNullWhen(true)] object? obj) { return obj is Color c && c == this; }
        public override int GetHashCode() { return (int)Packed; }
        public override string ToString() { return $"<{R},{G},{B},{A}>"; }

        public static Color Lerp(Color from, Color to, float lerp) {
            return new Color(from + ((Vector4)to - from) * lerp);
        }

        public static Color FromFloat(float r, float g, float b) {
            return new Color(new Vector3(r, g, b));
        }
    }

    public struct RectF : IEquatable<RectF> {
        public float X, Y, Width, Height;
        public Vector2 Min => new Vector2(X, Y);
        public Vector2 Max => new Vector2(X + Width, Y + Height);
        public Vector2 Size => new Vector2(Width, Height);
        public float Area => Width * Height;
        public Vector2 Centre => new Vector2(X + Width / 2.0f, Y + Height / 2.0f);
        public Vector2 TopLeft => new Vector2(X, Y);
        public Vector2 TopRight => new Vector2(X + Width, Y);
        public Vector2 BottomLeft => new Vector2(X, Y + Height);
        public Vector2 BottomRight => new Vector2(X + Width, Y + Height);
        public float Top => Y;
        public float Left => X;
        public float Bottom => Y + Height;
        public float Right => X + Width;
        public RectF(Vector2 pos, Vector2 size) { X = pos.X; Y = pos.Y; Width = size.X; Height = size.Y; }
        public RectF(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public static RectF operator +(RectF r, Vector2 o) { r.X += o.X; r.Y += o.Y; return r; }
        public static RectF operator -(RectF r, Vector2 o) { r.X -= o.X; r.Y -= o.Y; return r; }
        public static RectF operator *(RectF r, Vector2 o) { r.X *= o.X; r.Y *= o.Y; r.Width *= o.X; r.Height *= o.Y; return r; }
        public static RectF operator /(RectF r, Vector2 o) { r.X /= o.X; r.Y /= o.Y; r.Width /= o.X; r.Height /= o.Y; return r; }
        public static bool operator ==(RectF r, RectF o) { return r.Equals(o); }
        public static bool operator !=(RectF r, RectF o) { return !r.Equals(o); }

        public Vector2 Unlerp(Vector2 pos) { return (pos - Min) / Size; }
        public Vector2 Lerp(Vector2 pos) { return Min + Size * pos; }
        public RectF Lerp(RectF other) { return new RectF(Lerp(other.Min), other.Size * Size); }
        public RectF ExpandToInclude(RectF other) { return FromMinMax(Vector2.Min(Min, other.Min), Vector2.Max(Max, other.Max)); }
        public RectF ExpandToInclude(Vector2 pnt) { return FromMinMax(Vector2.Min(Min, pnt), Vector2.Max(Max, pnt)); }
        public bool Overlaps(RectF other) { return X < other.Right && Y < other.Bottom && Right > other.X && Bottom > other.Y; }
        public RectF Inset(float v) { return Inset(new Vector2(v, v)); }
        public RectF Inset(Vector2 v) { return new RectF(Min + v, Size - v * 2f); }

        public override bool Equals(object? obj) { return obj is RectF i && Equals(i); }
        public bool Equals(RectF other) { return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height; }
        public override int GetHashCode() { return HashCode.Combine(X, Y, Width, Height); }
        public override string ToString() { return $"<{X}, {Y}, {Width}, {Height}>"; }

        public static readonly RectF Unit01 = new RectF(0f, 0f, 1f, 1f);

        public static implicit operator RectI(RectF r) { return new RectI((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height); }
        public static RectF FromMinMax(float minX, float minY, float maxX, float maxY) {
            return new RectF(minX, minY, maxX - minX, maxY - minY);
        }
        public static RectF FromMinMax(Vector2 min, Vector2 max) {
            return new RectF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }
    }
    public struct RectI : IEquatable<RectI> {
        public int X, Y, Width, Height;
        public Int2 Min => new Int2(X, Y);
        public Int2 Max => new Int2(X + Width, Y + Height);
        public Int2 Size => new Int2(Width, Height);
        public Int2 Centre => new Int2(X + Width / 2, Y + Height / 2);
        public Int2 TopLeft => new Int2(X, Y);
        public Int2 TopRight => new Int2(X + Width, Y);
        public Int2 BottomLeft => new Int2(X, Y + Height);
        public Int2 BottomRight => new Int2(X + Width, Y + Height);
        public int Top => Y;
        public int Left => X;
        public int Bottom => Y + Height;
        public int Right => X + Width;
        public RectI(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public RectI(Int2 pos, Int2 size) { X = pos.X; Y = pos.Y; Width = size.X; Height = size.Y; }
        public static RectI operator +(RectI r, Int2 o) { r.X += o.X; r.Y += o.Y; return r; }
        public static RectI operator -(RectI r, Int2 o) { r.X -= o.X; r.Y -= o.Y; return r; }
        public static RectI operator *(RectI r, Int2 o) { r.X *= o.X; r.Y *= o.Y; r.Width *= o.X; r.Height *= o.Y; return r; }
        public static RectI operator /(RectI r, Int2 o) { r.X /= o.X; r.Y /= o.Y; r.Width /= o.X; r.Height /= o.Y; return r; }
        public static bool operator ==(RectI r, RectI o) { return r.X == o.X && r.Y == o.Y && r.Width == o.Width && r.Height == o.Height; }
        public static bool operator !=(RectI r, RectI o) { return !(r == o); }
        public static implicit operator RectF(RectI r) { return new RectF(r.X, r.Y, r.Width, r.Height); }

        public bool ContainsInclusive(Int2 pnt) {
            return (uint)(pnt.X - X) <= Width && (uint)(pnt.Y - Y) <= Height;
        }

        public RectI ExpandToInclude(Int2 pnt) { return FromMinMax(Int2.Min(Min, pnt), Int2.Max(Max, pnt)); }
        public RectI ExpandToInclude(RectI other) { return FromMinMax(Int2.Min(Min, other.Min), Int2.Max(Max, other.Max)); }
        public RectI ClampToBounds(RectI bounds) { return FromMinMax(Int2.Max(Min, bounds.Min), Int2.Min(Max, bounds.Max)); }
        public bool Overlaps(RectF other) { return X < other.Right && Y < other.Bottom && Right > other.X && Bottom > other.Y; }
        public RectI Inset(int v) { return Inset(new Int2(v, v)); }
        public RectI Inset(Int2 v) { return new RectI(Min + v, Size - v * 2); }
        public RectI Expand(int v) { return Expand(new Int2(v, v)); }
        public RectI Expand(Int2 v) { return new RectI(Min - v, Size + v * 2); }

        public override bool Equals(object? obj) { return obj is RectI i && Equals(i); }
        public bool Equals(RectI other) { return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height; }
        public override int GetHashCode() { return HashCode.Combine(X, Y, Width, Height); }
        public override string ToString() { return $"<{X}, {Y}, {Width}, {Height}>"; }

        public static RectI FromMinMax(int minX, int minY, int maxX, int maxY) {
            return new RectI(minX, minY, maxX - minX, maxY - minY);
        }
        public static RectI FromMinMax(Int2 min, Int2 max) {
            return new RectI(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }
    }

    public struct BoundingBox : IEquatable<BoundingBox> {
        public Vector3 Min, Max;
        public bool IsValid => Max.X >= Min.X;

        public Vector3 Centre => (Min + Max) / 2.0f;
        public Vector3 Extents => (Max - Min) / 2.0f;
        public Vector3 Size => Max - Min;

        public BoundingBox() : this(Vector3.Zero, Vector3.Zero) { }
        public BoundingBox(Vector3 min, Vector3 max) {
            Min = min;
            Max = max;
        }
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

        public bool Equals(BoundingBox other) { return Min.Equals(other.Min) && Max.Equals(other.Max); }
        public override bool Equals(object? obj) { return obj is BoundingBox box && Equals(box); }
        public override string ToString() { return $"BB<{Min} - {Max}>"; }
        public override int GetHashCode() { return HashCode.Combine(Min, Max); }

        public float GetDisOverlap(BoundingBox other) {
            var over = Vector3.Abs(Min - other.Min) + Vector3.Abs(Max - other.Max);
            return over.X + over.Y + over.Z;
        }

        public static BoundingBox Union(BoundingBox box1, BoundingBox box2) {
            return FromMinMax(Vector3.Min(box1.Min, box2.Min), Vector3.Max(box1.Max, box2.Max));
        }
        public static BoundingBox FromMinMax(Vector3 min, Vector3 max) { return new BoundingBox(min, max); }

        public Vector3 Lerp(Vector3 lerp) {
            return Min + (Max - Min) * lerp;
        }

        public static readonly BoundingBox Infinite = new BoundingBox(new Vector3(float.MinValue), new Vector3(float.MaxValue));
        public static readonly BoundingBox Invalid = new BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
        public static bool operator ==(BoundingBox left, BoundingBox right) { return left.Equals(right); }
        public static bool operator !=(BoundingBox left, BoundingBox right) { return !(left == right); }
    };

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
        public float GetDistanceTo(Plane p) {
            return (p.D - Vector3.Dot(p.Normal, Origin)) / Vector3.Dot(p.Normal, Direction);
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
    public static class RayExt {
        public static Vector3 Raycast(this Plane plane, Ray ray) {
            return ray.ProjectTo(plane);
        }
        public static Vector3 Raycast(this Plane plane, Ray ray, out float dst) {
            dst = (plane.D - Vector3.Dot(plane.Normal, ray.Origin)) / Vector3.Dot(plane.Normal, ray.Direction);
            return ray.GetPoint(dst);
        }
    }

}
