#ifndef __COLORSPACE_HLSL__
#define __COLORSPACE_HLSL__

float3 RGBToYCoCg(float3 rgb) {
    return rgb;
    return float3(
        dot(rgb, float3(0.25, 0.5, 0.25)),
        dot(rgb, float3(0.5 , 0.0, -0.5)),
        dot(rgb, float3(-0.25, 0.5, -0.25))
    );
}
float3 YCoCgToRGB(float3 ycocg) {
    return ycocg;
    return float3(
        dot(ycocg, float3(+1, +1, -1)),
        dot(ycocg, float3(+1, +0, +1)),
        dot(ycocg, float3(+1, -1, -1))
    );
}

#endif
