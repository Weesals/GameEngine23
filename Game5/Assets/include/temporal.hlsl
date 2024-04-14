#ifndef __TEMPORAL__
#define __TEMPORAL__

#include <common.hlsl>

void TemporalAdjust(inout float2 uv) {
    uv += (ddx(uv) * TemporalJitter.x - ddy(uv) * TemporalJitter.y);
}

#endif
