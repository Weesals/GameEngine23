using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {

    public static class Vector4Ext {
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
            fixed (Vector3* w = &v) *(Vector2*)w = set;
        }
        public unsafe static void toyz(this ref Vector3 v, Vector2 set) {
            fixed (Vector3* w = &v) *(Vector2*)&w->Y = set;
        }
        public unsafe static void toxy(this ref Vector4 v, Vector2 set) {
            fixed (Vector4* w = &v) *(Vector2*)&w->X = set;
        }
        public unsafe static void toyz(this ref Vector4 v, Vector2 set) {
            fixed (Vector4* w = &v) *(Vector2*)&w->Y = set;
        }
        public unsafe static void tozw(this ref Vector4 v, Vector2 set) {
            fixed (Vector4* w = &v) *(Vector2*)&w->Z = set;
        }
        public unsafe static void toxyz(this ref Vector4 v, Vector3 set) {
            fixed (Vector4* w = &v) *(Vector3*)&w->X = set;
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
        public static Vector4 toxyzw(this Vector4 v) {
            return new Vector4(v.X, v.Y, v.Z, v.W);
        }
        public static float cmin(this Vector2 v) {
            return MathF.Min(v.X, v.Y);
        }
        public static float cmin(this Vector4 v) {
            return MathF.Min(MathF.Min(v.X, v.Y), MathF.Min(v.Z, v.W));
        }

        public static Vector3 AppendY(this Vector2 v, float y = 0.0f) {
            return new Vector3(v.X, y, v.Y);
        }
        public static Vector3 AppendZ(this Vector2 v, float z = 0.0f) {
            return new Vector3(v.X, v.Y, z);
        }
    }

    public struct Int2 : IEquatable<Int2> {
	    public int X, Y;
        public int LengthSquared => (X * X + Y * Y);
        public float Length => MathF.Sqrt(LengthSquared);
        public Int2(int v) : this(v, v) { }
        public Int2(int _x, int _y) { X = _x; Y = _y; }
	    public Int2(Vector2 o) { X = ((int)o.X); Y = ((int)o.Y); }
        public bool Equals(Int2 o) { return X == o.X && Y == o.Y; }
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
        public static Int2 operator &(Int2 o1, int o) { return new Int2(o1.X & o, o1.Y & o); }
        public static Int2 operator |(Int2 o1, int o) { return new Int2(o1.X | o, o1.Y | o); }
        public static Int2 operator ^(Int2 o1, int o) { return new Int2(o1.X ^ o, o1.Y ^ o); }

        public static Int2 Min(Int2 v1, Int2 v2) { return new Int2(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y)); }
	    public static Int2 Max(Int2 v1, Int2 v2) { return new Int2(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y)); }
        public static Int2 Clamp(Int2 v, Int2 min, Int2 max) { return new Int2(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y)); }
        public static int Dot(Int2 v1, Int2 v2) { return v1.X * v2.X + v1.Y * v2.Y; }
        public static int CSum(Int2 v) { return v.X + v.X; }
        public static int CMul(Int2 v) { return v.X * v.Y; }

        public static Int2 FloorToInt(Vector2 v) { return new Int2((int)MathF.Floor(v.X), (int)MathF.Floor(v.Y)); }
        public static Int2 RoundToInt(Vector2 v) { return new Int2((int)MathF.Round(v.X), (int)MathF.Round(v.Y)); }
        public static Int2 CeilToInt(Vector2 v) { return new Int2((int)MathF.Ceiling(v.X), (int)MathF.Ceiling(v.Y)); }

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
        public Int3(Vector3 o) : this((int)o.X, (int)o.Y, (int)o.Z) { }
        public bool Equals(Int3 o) { return X == o.X && Y == o.Y && Z == o.Z; }
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

        public static Int3 Min(Int3 v1, Int3 v2) { return new Int3(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y), Math.Min(v1.Z, v2.Z)); }
        public static Int3 Max(Int3 v1, Int3 v2) { return new Int3(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y), Math.Max(v1.Z, v2.Z)); }
        public static Int3 Clamp(Int3 v, Int3 min, Int3 max) {
            return new Int3(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y), Math.Clamp(v.Z, min.Z, max.Z));
        }

        public static implicit operator Int3(int v) { return new Int3(v); }
        public static implicit operator Vector3(Int3 v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }
    }
    public struct Int4 : IEquatable<Int4> {
        public int X, Y, Z, W;
	    public Int4(int v) : this(v, v, v, v) { }
	    public Int4(int _x, int _y, int _z, int _w) { X = _x; Y = _y; Z = _z; W = _w; }
	    public Int4(Vector4 o) : this((int)o.X, (int)o.Y, (int)o.Z, (int)o.W) { }
        public bool Equals(Int4 o) { return X == o.X && Y == o.Y && Z == o.Z && W == o.W; }
        public static Int4 operator +(Int4 v, Int4 o) { return new Int4(v.X + o.X, v.Y + o.Y, v.Z + o.Z, v.W + o.W); }
	    public static Int4 operator -(Int4 v, Int4 o) { return new Int4(v.X - o.X, v.Y - o.Y, v.Z - o.Z, v.W - o.W); }
	    public static Int4 operator *(Int4 v, Int4 o) { return new Int4(v.X * o.X, v.Y * o.Y, v.Z * o.Z, v.W * o.W); }
	    public static Int4 operator /(Int4 v, Int4 o) { return new Int4(v.X / o.X, v.Y / o.Y, v.Z / o.Z, v.W / o.W); }
	    public static Int4 operator +(Int4 v, int o) { return new Int4(v.X + o, v.Y + o, v.Z + o, v.W + o); }
	    public static Int4 operator -(Int4 v, int o) { return new Int4(v.X - o, v.Y - o, v.Z - o, v.W - o); }
	    public static Int4 operator *(Int4 v, int o) { return new Int4(v.X * o, v.Y * o, v.Z * o, v.W * o); }
	    public static Int4 operator /(Int4 v, int o) { return new Int4(v.X / o, v.Y / o, v.Z / o, v.W / o); }
        
	    public static Int4 Min(Int4 v1, Int4 v2) { return new Int4(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y), Math.Min(v1.Z, v2.Z), Math.Min(v1.W, v2.W)); }
	    public static Int4 Max(Int4 v1, Int4 v2) { return new Int4(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y), Math.Max(v1.Z, v2.Z), Math.Max(v1.W, v2.W)); }
        public static Int4 Clamp(Int4 v, Int4 min, Int4 max) {
            return new Int4(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y), Math.Clamp(v.Z, min.Z, max.Z), Math.Clamp(v.W, min.W, max.W));
        }

        public static implicit operator Int4(int v) { return new Int4(v, v, v, v); }
        public static implicit operator Vector4(Int4 v) { return new Vector4((float)v.X, (float)v.Y, (float)v.Z, (float)v.W); }
    }

    public struct Short2 : IEquatable<Short2> {
        public short X, Y;
        public Short2(short x, short y) { X = x; Y = y; }
        public Short2(Vector2 v) { X = (short)v.X; Y = (short)v.Y; }
        public Vector2 ToVector2() { return new Vector2(X, Y); }
        public bool Equals(Short2 o) { return X == o.X && Y == o.Y; }
        public override string ToString() { return $"<{X}, {Y}>"; }
        public override int GetHashCode() { return X + (Y << 16); }
    }
    public struct UShort2 : IEquatable<UShort2> {
        public ushort X, Y;
        public UShort2(ushort x, ushort y) { X = x; Y = y; }
        public UShort2(Vector2 v) { X = (ushort)v.X; Y = (ushort)v.Y; }
        public Vector2 ToVector2() { return new Vector2(X, Y); }
        public bool Equals(UShort2 o) { return X == o.X && Y == o.Y; }
        public override string ToString() { return $"<{X}, {Y}>"; }
        public override int GetHashCode() { return X + (Y << 16); }
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
        public static readonly Color White = new Color(0xffffffff);
        public static readonly Color Gray = new Color(0xff888888);
        public static readonly Color Black = new Color(0xff000000);
        public static readonly Color Clear = new Color(0x00000000);
        public static readonly Color Red = new Color(0xff0000ff);
        public static readonly Color Orange = new Color(0xff0088ff);
        public static readonly Color Yellow = new Color(0xff00ffff);
        public static readonly Color Green = new Color(0xff00ff00);
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
        public static RectI operator +(RectI r, Int2 o) { r.X += o.X; r.Y += o.Y; return r; }
        public static RectI operator -(RectI r, Int2 o) { r.X -= o.X; r.Y -= o.Y; return r; }
        public static RectI operator *(RectI r, Int2 o) { r.X *= o.X; r.Y *= o.Y; r.Width *= o.X; r.Height *= o.Y; return r; }
        public static RectI operator /(RectI r, Int2 o) { r.X /= o.X; r.Y /= o.Y; r.Width /= o.X; r.Height /= o.Y; return r; }
        public static bool operator ==(RectI r, RectI o) { return r.X == o.X && r.Y == o.Y && r.Width == o.Width && r.Height == o.Height; }
        public static bool operator !=(RectI r, RectI o) { return !(r == o); }
        public static implicit operator RectF(RectI r) { return new RectF(r.X, r.Y, r.Width, r.Height); }

        public RectI ExpandToInclude(Int2 pnt) { return FromMinMax(Int2.Min(Min, pnt), Int2.Max(Max, pnt)); }
        public RectI ExpandToInclude(RectI other) { return FromMinMax(Int2.Min(Min, other.Min), Int2.Max(Max, other.Max)); }
        public RectI ClampToBounds(RectI bounds) { return FromMinMax(Int2.Max(Min, bounds.Min), Int2.Min(Max, bounds.Max)); }

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

}
