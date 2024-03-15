using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Geometry {
    public ref struct DistanceFieldGenerator {
        //Vector2[] values;
        Span<Vector2> values;
        struct Corners {
            public byte p00;
            public byte p10;
            public byte p11;
            public byte p01;
        };
        unsafe public void Dispose() {
            //PooledArray<Vector2>.Return(ref values);
            fixed (Vector2* ptr = values) {
                Marshal.FreeHGlobal((nint)ptr);
            }
        }
        unsafe public void SeedAAEdges(Span<Color> texdata, Int2 tsize) {
            int count = tsize.X * tsize.Y;
            values = new Span<Vector2>((Vector2*)Marshal.AllocHGlobal(count * sizeof(Vector2)), count);
            //PooledArray<Vector2>.Resize(ref values, tsize.X * tsize.Y);
            values.Fill(new Vector2(1000000.0f, 1000000.0f));

            for (int y = 1; y < tsize.Y; ++y) {
                for (int x = 1; x < tsize.X; ++x) {
                    ulong pN64;
                    byte* pN = (byte*)&pN64;
                    Int4 indicesV4 = new Int4(
                        (x - 1) + (y - 1) * tsize.X,
                        (x - 0) + (y - 1) * tsize.X,
                        (x - 0) + (y - 0) * tsize.X,
                        (x - 1) + (y - 0) * tsize.X
                    );
                    int* indices = &indicesV4.X;
                    *(Corners*)&pN64 = new Corners() {
                        p00 = texdata[indices[0]].A,
                        p10 = texdata[indices[1]].A,
                        p11 = texdata[indices[2]].A,
                        p01 = texdata[indices[3]].A,
                    };
                    uint pN1Masked = ((uint*)&pN64)[0] & 0x80808080;
                    if (pN1Masked == 0x00000000 || pN1Masked == 0x80808080) continue;
                    ((uint*)&pN64)[1] = ((uint*)&pN64)[0];
                    for (int i = 0; i < 4; ++i) {
                        var sign4 = ((pN64 >> (i * 8)) & 0x80808080);
                        // Two pixels aligned
                        if (sign4 == 0x80800000) {
                            ref var rcorners = ref *(Corners*)(pN + i);
                            var e0 = (float)(127 - rcorners.p00) / (rcorners.p01 - rcorners.p00);
                            var e1 = (float)(127 - rcorners.p10) / (rcorners.p11 - rcorners.p10);
                            Vector2 n = Vector2.Normalize(new(e0 - e1, 1.0f));
                            Vector2 n1 = n.X > 0.0f ? n : new Vector2(0.0f, 1.0f);
                            Vector2 n2 = n.X < 0.0f ? n : new Vector2(0.0f, 1.0f);
                            Observe(ref values[indices[(i + 0) & 3]], n1 * (e0 - 0.0f), i);
                            Observe(ref values[indices[(i + 1) & 3]], n2 * (e1 - 0.0f), i);
                            Observe(ref values[indices[(i + 2) & 3]], n1 * (e1 - 1.0f), i);
                            Observe(ref values[indices[(i + 3) & 3]], n2 * (e0 - 1.0f), i);
                            break;
                        }
                        // Single pixel
                        sign4 &= 0xff00ffff;
                        if (sign4 == 0x00000080 || sign4 == 0x80008000) {
                            ref var rcorners = ref *(Corners*)(pN + i);
                            var eX = (float)(127 - rcorners.p00) / (rcorners.p10 - rcorners.p00);
                            var eY = (float)(127 - rcorners.p00) / (rcorners.p01 - rcorners.p00);
                            Vector2 n = new(eY, eX);
                            Vector2 nN = Vector2.Normalize(n);
                            Observe(ref values[indices[(i + 0) & 3]], nN * (n.Y * eY), i);
                            Observe(ref values[indices[(i + 1) & 3]], new Vector2(eX - 1.0f, 0.0f), i);
                            Observe(ref values[indices[(i + 2) & 3]], nN * (n.Y * eY - n.X - n.Y), i);
                            Observe(ref values[indices[(i + 3) & 3]], new Vector2(0.0f, eY - 1.0f), i);
                            break;
                        }
                    }
                }
            }
        }
        static void Observe(ref Vector2 item, Vector2 value, int r) {
            if (value.LengthSquared() < item.LengthSquared()) {
                switch (r) {
                    case 0: break;
                    case 1: value = new Vector2(-value.Y, +value.X); break;
                    case 2: value = new Vector2(-value.X, -value.Y); break;
                    case 3: value = new Vector2(+value.Y, -value.X); break;
                }
                item = value;
            }
        }

        public void Generate(Span<Color> texdata, Int2 tsize) {
            var stopatch = new Stopwatch();
            stopatch.Start();
            var startTime = stopatch.ElapsedTicks;

            SeedAAEdges(texdata, tsize);

            var seedTime = stopatch.ElapsedTicks;
        
            // Calculate distances along Y axis
            for (int x = 0; x < tsize.X; ++x) {
                int lastEdgeY = 0;
                Vector2 lastEdge = new(0.0f, -1000000.0f);
                for (int y = 0; y < tsize.Y; ++y) {
                    int iy = y * tsize.X;
                    if (values[x + iy].Y >= 1000000.0f && y < tsize.Y - 1) continue;
                    int mid = lastEdgeY == 0 ? 0 : y == tsize.Y - 1 ? tsize.Y : (lastEdgeY + y) / 2;
                    int y2 = lastEdgeY;
                    for (; y2 < mid; ++y2) {
                        values[x + y2 * tsize.X] = lastEdge - new Vector2((float)x, (float)y2);
                    }
                    lastEdge = values[x + iy] + new Vector2((float)x, (float)y);
                    for (; y2 < y; ++y2) {
                        values[x + y2 * tsize.X] = lastEdge - new Vector2((float)x, (float)y2);
                    }
                    lastEdgeY = y + 1;
                }
            }
            var pass1Time = stopatch.ElapsedTicks;
            // Spread distances along X axis
            for (int y = 0; y < tsize.Y; ++y) {
                int iy = y * tsize.X;
                int itFX = 0;
                for (int x = 0; x < tsize.X; ++x) {
                    int i = x + iy;
                    float bestDst2 = values[i].LengthSquared();
                    itFX = Math.Max(x, itFX);
                    for (int it2 = itFX; it2 < tsize.X - 1; ++it2) {
                        // Maximum distance possible is too far
                        if ((it2 - x) * (it2 - x) >= bestDst2) break;
                        var next = new Vector2((float)(it2 - x), 0) + values[it2 + iy];
                        float dst2 = next.LengthSquared();
                        if (dst2 > bestDst2) continue;
                        bestDst2 = dst2;
                        itFX = it2;
                        values[i] = next;
                    }
                }
                itFX = tsize.X - 1;
                for (int x = tsize.X - 1; x >= 0; --x) {
                    int i = x + iy;
                    float bestDst2 = values[i].LengthSquared();
                    itFX = Math.Min(x, itFX);
                    for (int it2 = itFX; it2 > 0; --it2) {
                        // Maximum distance possible is too far
                        if ((it2 - x) * (it2 - x) >= bestDst2) break;
                        var next = new Vector2((float)(it2 - x), 0) + values[it2 + iy];
                        float dst2 = next.LengthSquared();
                        if (dst2 > bestDst2) continue;
                        bestDst2 = dst2;
                        itFX = it2;
                        values[i] = next;
                    }
                }
            }
            var endTime = stopatch.ElapsedTicks;
            Trace.WriteLine($"CS Distance field gen " +
                $"{(seedTime - startTime) / TimeSpan.TicksPerMillisecond} " +
                $"{(pass1Time - seedTime) / TimeSpan.TicksPerMillisecond} " +
                $"{(endTime - pass1Time) / TimeSpan.TicksPerMillisecond} " +
                $"= {(endTime - startTime) / TimeSpan.TicksPerMillisecond} ms");
        }
        public void ApplyDistances(Span<Color> texdata, Int2 tsize, float spread = 32.0f) {
            var stopatch = new Stopwatch();
            stopatch.Start();
            var startTime = stopatch.ElapsedTicks;
            //float midPoint = 127.5f + 0.5f * 127.5f / spread;
            // Calculate final distance values
            for (int y = 0; y < tsize.Y; ++y) {
                int iy = y * tsize.X;
                for (int x = 0; x < tsize.X; ++x) {
                    ref var a = ref texdata[x + iy].A;
                    float distance = values[x + iy].Length();
                    distance *= a > 127 ? 1.0f : -1.0f;
                    a = (byte)Math.Clamp(127.5f + 128.0f * distance / spread, 0.0f, 255.0f);
                }
            }
            var endTime = stopatch.ElapsedTicks;
            Trace.WriteLine($"CS Distance field write " +
                $"{(endTime - startTime) / TimeSpan.TicksPerMillisecond} ms");
        }
    }
}
