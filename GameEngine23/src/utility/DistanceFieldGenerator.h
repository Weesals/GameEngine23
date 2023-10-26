#pragma once

#include "../MathTypes.h"

#include <vector>
#include <span>
#include <chrono>
#include <algorithm>

class DistanceFieldGenerator {
    std::vector<Vector2> values;
public:
    void SeedAAEdges(std::span<const ColorB4> texdata, Int2 tsize) {
        values.resize(tsize.x * tsize.y);
        std::fill(values.begin(), values.end(), Vector2(0.0f, 1000000.0f));

        auto Observe = [](Vector2& item, Vector2 value, int r) {
            if (value.LengthSquared() < item.LengthSquared()) {
                switch (r) {
                case 0: break;
                case 1: value = Vector2(-value.y, +value.x); break;
                case 2: value = Vector2(-value.x, -value.y); break;
                case 3: value = Vector2(+value.y, -value.x); break;
                }
                item = value;
            }
        };

        for (int y = 1; y < tsize.y; ++y) {
            for (int x = 1; x < tsize.x; ++x) {
                struct Corners {
                    uint8_t p00;
                    uint8_t p10;
                    uint8_t p11;
                    uint8_t p01;
                };
                union {
                    Corners corners;
                    uint8_t pN[4];
                    struct {
                        uint32_t pN1;
                        uint32_t pN2;
                    };
                    uint64_t pN64;
                };
                int indices[4] = {
                    (x - 1) + (y - 1) * tsize.x,
                    (x - 0) + (y - 1) * tsize.x,
                    (x - 0) + (y - 0) * tsize.x,
                    (x - 1) + (y - 0) * tsize.x
                };
                corners.p00 = texdata[indices[0]].a;
                corners.p10 = texdata[indices[1]].a;
                corners.p11 = texdata[indices[2]].a;
                corners.p01 = texdata[indices[3]].a;
                if ((pN1 & 0x80808080) == 0x00000000 || (pN1 & 0x80808080) == 0x80808080) continue;
                pN2 = pN1;
                for (int i = 0; i < 4; ++i) {
                    auto sign4 = ((pN64 >> (i * 8)) & 0x80808080);
                    if (sign4 == 0x00000080 || sign4 == 0x80808000) {
                        auto& rcorners = *(Corners*)(pN + i);
                        auto eX = (float)(127 - rcorners.p00) / (rcorners.p10 - rcorners.p00);
                        auto eY = (float)(127 - rcorners.p00) / (rcorners.p01 - rcorners.p00);
                        Vector2 n(eY, eX);
                        float len = Vector2::Dot(n, n);
                        if (len > 0.0001f) n /= std::sqrt(len);
                        auto e = n * (Vector2::Dot(n, Vector2(0.0f, eY)) - Vector2::Dot(n, Vector2(1.0f, 1.0f)));
                        Observe(values[indices[(i + 0) & 3]], n * (n.y * eY), i);
                        Observe(values[indices[(i + 1) & 3]], Vector2(eX - 1.0f, 0.0f), i);
                        Observe(values[indices[(i + 2) & 3]], e, i);// n* (-1.0f * n.x - n.y * (1.0f - eY)), i);
                        Observe(values[indices[(i + 3) & 3]], Vector2(0.0f, eY - 1.0f), i);
                        break;
                    }
                    if (sign4 == 0x80800000) {
                        auto& rcorners = *(Corners*)(pN + i);
                        auto e0 = (float)(127 - rcorners.p00) / (rcorners.p01 - rcorners.p00);
                        auto e1 = (float)(127 - rcorners.p10) / (rcorners.p11 - rcorners.p10);
                        Vector2 n(e0 - e1, 1.0f);
                        float len = Vector2::Dot(n, n);
                        if (len > 0.0001f) n /= std::sqrt(len);
                        Vector2 n1 = n.x > 0.0f ? n : Vector2(0.0f, 1.0f);
                        Vector2 n2 = n.x < 0.0f ? n : Vector2(0.0f, 1.0f);
                        Observe(values[indices[(i + 0) & 3]], n1 * (e0 - 0.0f), i);
                        Observe(values[indices[(i + 1) & 3]], n2 * (e1 - 0.0f), i);
                        Observe(values[indices[(i + 2) & 3]], n1 * (e1 - 1.0f), i);
                        Observe(values[indices[(i + 3) & 3]], n2 * (e0 - 1.0f), i);
                        break;
                    }
                }
            }
        }
    }
    void Generate(std::span<const ColorB4> texdata, Int2 tsize) {
        auto startTime = std::chrono::system_clock::now();

        values.resize(tsize.x * tsize.y);

        SeedAAEdges(texdata, tsize);
        
        // Calculate distances along Y axis
        for (int x = 0; x < tsize.x; ++x) {
            float lastEdgeY = -1000000.0f;
            for (int y = 0; y < tsize.y; ++y) {
                int iy = y * tsize.x;
                auto pp1 = texdata[x + iy].a;
                if (y > 0) {
                    auto py0 = texdata[x + iy - tsize.x].a;
                    if ((pp1 > 127) != (py0 > 127)) {
                        float prevEdgeY = lastEdgeY;
                        lastEdgeY = y - (float)(127 - pp1) / (py0 - pp1);
                        int start = std::max((int)(prevEdgeY + lastEdgeY + 0.5f) / 2, 0);
                        for (int y2 = start; y2 <= y - 1; ++y2) {
                            float dy = lastEdgeY - y2;
                            if (std::abs(values[x + y2 * tsize.x].AbsSum()) < std::abs(dy)) continue;
                            values[x + y2 * tsize.x] = Vector2(0.0f, dy);
                        }
                    }
                }
                if (std::abs(values[x + iy].AbsSum()) > std::abs(lastEdgeY - y)) {
                    values[x + iy] = Vector2(0, lastEdgeY - y);
                }
                if (x > 0) {
                    auto px0 = texdata[x - 1 + iy].a;
                    if ((pp1 > 127) != (px0 > 127)) {
                        float lastEdgeX = x - (float)(127 - pp1) / (px0 - pp1);
                        for (int x2 = x - 1; x2 <= x; ++x2) {
                            float dx = lastEdgeX - x2;
                            if (std::abs(values[x2 + iy].AbsSum()) < std::abs(dx)) break;
                            values[x2 + iy] = Vector2(dx, 0.0f);
                        }
                    }
                }
            }
        }
        auto timer1 = std::chrono::system_clock::now();
        // Spread distances along X axis
        for (int y = 0; y < tsize.y; ++y) {
            int iy = y * tsize.x;
            int itFX = 0;
            for (int x = 0; x < tsize.x; ++x) {
                int i = x + iy;
                float bestDst2 = values[i].LengthSquared();
                itFX = std::max(x, itFX);
                for (int it2 = itFX; it2 < tsize.x - 1; ++it2) {
                    // Maximum distance possible is too far
                    if ((it2 - x) * (it2 - x) >= bestDst2) break;
                    auto next = Vector2((float)(it2 - x), 0) + values[it2 + iy];
                    float dst2 = next.LengthSquared();
                    if (dst2 > bestDst2) continue;
                    bestDst2 = dst2;
                    itFX = it2;
                    values[i] = next;
                }
            }
            itFX = tsize.x - 1;
            for (int x = tsize.x - 1; x >= 0; --x) {
                int i = x + iy;
                float bestDst2 = values[i].LengthSquared();
                itFX = std::min(x, itFX);
                for (int it2 = itFX; it2 > 0; --it2) {
                    // Maximum distance possible is too far
                    if ((it2 - x) * (it2 - x) >= bestDst2) break;
                    auto next = Vector2((float)(it2 - x), 0) + values[it2 + iy];
                    float dst2 = next.LengthSquared();
                    if (dst2 > bestDst2) continue;
                    bestDst2 = dst2;
                    itFX = it2;
                    values[i] = next;
                }
            }
        }
        auto endTime = std::chrono::system_clock::now();
        char str[] = "Distance field gen 000 000 000 ms\n";
        WriteTime(str + 19, endTime - startTime);
        WriteTime(str + 23, timer1 - startTime);
        WriteTime(str + 27, endTime - timer1);
        //OutputDebugStringA(str);
    }
    void ApplyDistances(std::span<ColorB4> texdata, Int2 tsize) {
        auto startTime = std::chrono::system_clock::now();
        // Calculate final distance values
        for (int y = 0; y < tsize.y; ++y) {
            int iy = y * tsize.x;
            for (int x = 0; x < tsize.x; ++x) {
                auto& a = texdata[x + iy].a;
                a = (uint8_t)std::clamp(127.5f + (a > 127 ? 32.0f : -32.0f) * values[x + iy].Length(), 0.0f, 255.0f);
            }
        }
        auto endTime = std::chrono::system_clock::now();
        char str[] = "Distance field write 000 ms\n";
        WriteTime(str + 21, endTime - startTime);
    }

    static void WriteTime(char* str, std::chrono::system_clock::duration duration) {
        int ms = (int)std::chrono::duration_cast<std::chrono::milliseconds>(duration).count();
        for (int i = 100; i >= 1; i /= 10) *(str++) = '0' + ((ms / i) % 10);
    }
};
