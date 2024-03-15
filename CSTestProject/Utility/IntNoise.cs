using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.Utility {
    public class IntNoise {
        public const int BitShift = 10;
        public const int Amplitude = 1 << BitShift;
        public const int Bitmask = Amplitude - 1;
        public static Int2 Floor1024(Int2 pos) {
            return pos >> BitShift;
            //return (pos + new Int2(pos.X < 0 ? -Amplitude + 1 : 0, pos.Y < 0 ? -Amplitude + 1 : 0)) >> BitShift;
        }
        public static Int3 Floor1024(Int3 pos) {
            return pos >> BitShift;
        }
        public static Int2 PermuteS512(Int2 seed) {
            int seedI = seed.X + (seed.Y << 10);
            seedI *= unchecked((int)0x846ca68bu);
            seedI ^= seedI >> 13;
            seedI *= (int)0x7feb352du;
            seedI ^= seedI >> 13;
            return new Int2((seedI >> 10) & Bitmask, (seedI >> 0) & Bitmask) - 512;
        }
        public static Int3 PermuteS512(Int3 seed) {
            int seedI = seed.X + (seed.Y << 10) + (seed.Z << 20);
            seedI *= unchecked((int)0x846ca68bu);
            seedI ^= seedI >> 13;
            seedI *= (int)0x7feb352du;
            seedI ^= seedI >> 13;
            return new Int3((seedI >> 20) & Bitmask, (seedI >> 10) & Bitmask, (seedI >> 0) & Bitmask) - 512;
        }
    }
    public class VoronoiIntNoise : IntNoise {
        // Uses grid where points are distributed about their integer coords
        // (ie. point0 is between -0.5 and 0.5)
        // Returns xy and sqr dst(z)
        public static Int3 GetNearest_2x2(Int2 from1024) {
            var cell = Floor1024(from1024);
            Int3 nearest = new Int3(0, 0, int.MaxValue);
            for (int y = 0; y < 2; y++) {
                for (int x = 0; x < 2; x++) {
                    var ocell = cell + new Int2(x, y);
                    var pnt = (ocell << BitShift) + PermuteS512(ocell);
                    var delta = pnt - from1024;
                    var dst2 = (int)Int2.Dot(delta, delta);
                    if (dst2 >= nearest.Z) continue;
                    nearest.Z = dst2;
                    nearest.XY = pnt;
                }
            }
            return nearest;
        }
    }
    public class PerlinIntNoise : IntNoise {
        public static int GetAt(Int2 from1024) {
            var cell = Floor1024(from1024);
            var l = (from1024 & Bitmask);
            l = l * l * (3 * 1024 - 2 * l) / (1024 * 1024);
            var n00 = DotGradient(from1024, cell + new Int2(0, 0)) / 1024;
            var n10 = DotGradient(from1024, cell + new Int2(1, 0)) / 1024;
            var n01 = DotGradient(from1024, cell + new Int2(0, 1)) / 1024;
            var n11 = DotGradient(from1024, cell + new Int2(1, 1)) / 1024;
            return (
                n00 * (1024 - l.X) * (1024 - l.Y) +
                n10 * l.X * (1024 - l.Y) +
                n01 * (1024 - l.X) * l.Y +
                n11 * l.X * l.Y
            ) / 1024 / 1024;
        }
        public static int GetAt(Int3 from1024) {
            var cell = Floor1024(from1024);
            var l = (from1024 & Bitmask);
            l = l * l * (3 * 1024 - 2 * l) / (1024 * 1024);
            var n000 = DotGradient(from1024, cell + new Int3(0, 0, 0)) / 1024;
            var n100 = DotGradient(from1024, cell + new Int3(1, 0, 0)) / 1024;
            var n010 = DotGradient(from1024, cell + new Int3(0, 1, 0)) / 1024;
            var n110 = DotGradient(from1024, cell + new Int3(1, 1, 0)) / 1024;
            var n001 = DotGradient(from1024, cell + new Int3(0, 0, 1)) / 1024;
            var n101 = DotGradient(from1024, cell + new Int3(1, 0, 1)) / 1024;
            var n011 = DotGradient(from1024, cell + new Int3(0, 1, 1)) / 1024;
            var n111 = DotGradient(from1024, cell + new Int3(1, 1, 1)) / 1024;
            var r0 = (
                (n000 * (1024 - l.X) + n100 * l.X) * (1024 - l.Y) +
                (n010 * (1024 - l.X) + n110 * l.X) * l.Y
            ) / 1024;
            var r1 = (
                (n001 * (1024 - l.X) + n101 * l.X) * (1024 - l.Y) +
                (n011 * (1024 - l.X) + n111 * l.X) * l.Y
            ) / 1024;
            return (r0 * (1024 - l.Z) + r1 * l.Z) / 1024 / 1024;
        }
        public static int GetAt(Int3 from1024, Int3 wrap) {
            var cell = Floor1024(from1024);
            var l = (from1024 & Bitmask);
            l = l * l * (3 * 1024 - 2 * l) >> (BitShift * 2);
            var n000 = DotGradient(from1024, cell + new Int3(0, 0, 0), wrap);
            var n100 = DotGradient(from1024, cell + new Int3(1, 0, 0), wrap);
            var n010 = DotGradient(from1024, cell + new Int3(0, 1, 0), wrap);
            var n110 = DotGradient(from1024, cell + new Int3(1, 1, 0), wrap);
            var n001 = DotGradient(from1024, cell + new Int3(0, 0, 1), wrap);
            var n101 = DotGradient(from1024, cell + new Int3(1, 0, 1), wrap);
            var n011 = DotGradient(from1024, cell + new Int3(0, 1, 1), wrap);
            var n111 = DotGradient(from1024, cell + new Int3(1, 1, 1), wrap);
            var x00 = (n000 * (Amplitude - l.X) + n100 * l.X) >> BitShift;
            var x10 = (n010 * (Amplitude - l.X) + n110 * l.X) >> BitShift;
            var x01 = (n001 * (Amplitude - l.X) + n101 * l.X) >> BitShift;
            var x11 = (n011 * (Amplitude - l.X) + n111 * l.X) >> BitShift;
            var r0 = (x00 * (Amplitude - l.Y) + x10 * l.Y) >> BitShift;
            var r1 = (x01 * (Amplitude - l.Y) + x11 * l.Y) >> BitShift;
            return (r0 * (Amplitude - l.Z) + r1 * l.Z) >> (BitShift * 2);
        }

        private static int DotGradient(Int2 from1024, Int2 cell) {
            return Int2.DotI(from1024 - (cell << BitShift), PermuteS512(cell));
        }
        private static int DotGradient(Int3 from1024, Int3 cell) {
            return Int3.DotI(from1024 - (cell << BitShift), PermuteS512(cell));
        }
        private static int DotGradient(Int3 from1024, Int3 cell, Int3 wrap) {
            return Int3.DotI(from1024 - (cell << BitShift), PermuteS512(cell & (wrap - 1)));
        }
    }
}
