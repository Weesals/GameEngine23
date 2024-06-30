#ifndef __NOISE_HLSL__
#define __NOISE_HLSL__

float2 RandomDir(float2 coord, float seed = 0) {
    seed += dot(coord, float2(10.2, 125.62));
    //seed = sin(seed) * 1000;
    seed *= frac(seed);
    return float2(sin(seed), cos(seed));
}

static const float SF2 = 0.5 * (sqrt(3.0) - 1.0);
static const float SG2 = (3.0 - sqrt(3.0)) / 6.0;


float SimplexNoise(float2 p, out float2 dd) {
    float2 i = floor(p + dot(p, 1.0) * SF2);
    float2 L = i - dot(i, 1.0) * SG2;
    
    float2 d0 = p - L;
    float2 o = d0.x > d0.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float2 d1 = d0 - o + SG2;
    float2 d2 = d0 - 1.0 + 2.0 * SG2;
    
    float2 g0 = RandomDir(i + 0);
    float2 g1 = RandomDir(i + o);
    float2 g2 = RandomDir(i + 1);
    float3 g = float3(dot(g0, d0), dot(g1, d1), dot(g2, d2));
    float3 w = float3(dot(d0, d0), dot(d1, d1), dot(d2, d2));
    w = max(0.5 - w, 0.0);
    float3 w2 = w * w;
    float3 w4 = w2 * w2;
    float v = 70.0 * dot(w4, g);
    
    float3 temp = w2 * w * g;
    dd = -8.0 * (temp.x * d0 + temp.y * d1 + temp.z * d2);
    dd += w4.x * g0 + w4.y * g1 + w4.z * g2;
    dd *= 70.0;

    return v;
}
float SimplexNoise(float2 p)
{
    float2 dd;
    return SimplexNoise(p, dd);
}

float Permute(float v) {
    return frac(v * 105.18f - 0.01f);
}
float PermuteV2(float2 v2, float seed = 0.0) {
    return frac(frac(dot(v2, float2(0.6796 + seed, 0.4273))) * 188.1);
}
float PermuteV3(float3 v) {
    return frac(frac(dot(v, float3(0.6796, 0.4273, 0.7273))) * 188.1);
}
float2 PermuteO2(float r) {
    return float2(r, Permute(r));
}
float3 PermuteO3(float r) {
    float y = Permute(r);
    return float3(r, y, Permute(y));
}


struct SimplexSample2D {
    float2 Cell0, Cell1, Cell2;
    float2 Dir0, Dir1, Dir2;
    float3 CellWeights;
    void GetGradients(out float2 g0, out float2 g1, out float2 g2, float seed = 0) {
        g0 = RandomDir(Cell0, seed);
        g1 = RandomDir(Cell1, seed);
        g2 = RandomDir(Cell2, seed);
    }
    float Sample1(float seed = 0) {
        float2 g0, g1, g2; GetGradients(g0, g1, g2, seed);
        float3 g = float3(dot(g0, Dir0), dot(g1, Dir1), dot(g2, Dir2));
        float3 w2 = CellWeights * CellWeights;
        return 70.0 * dot(w2 * w2, g);
    }
    float2 Sample2(float seed = 0) {
        float2 g0, g1, g2; GetGradients(g0, g1, g2, seed);
        float2 o0 = float2(g0.y, -g0.x), o1 = float2(g1.y, -g1.x), o2 = float2(g2.y, -g2.x);
        float3 g = float3(dot(g0, Dir0), dot(g1, Dir1), dot(g2, Dir2));
        float3 o = float3(dot(o0, Dir0), dot(o1, Dir1), dot(o2, Dir2));
        float3 w2 = CellWeights * CellWeights;
        return 70.0 * float2(dot(w2 * w2, g), dot(w2 * w2, o));
    }
    float2 SampleG(float seed = 0) {
        float2 g0, g1, g2; GetGradients(g0, g1, g2, seed);
        return g0 * CellWeights.x + g1 * CellWeights.y + g2 * CellWeights.z;
    }
    float2 SampleDD(float seed = 0) {
        float2 g0, g1, g2; GetGradients(g0, g1, g2, seed);
        float3 g = float3(dot(g0, Dir0), dot(g1, Dir1), dot(g2, Dir2));
        float3 w = CellWeights;
        float3 w2 = w * w;
        float3 w4 = w2 * w2;
        float3 w3g = w2 * w * g;
        float2 dd = -8.0 * (w3g.x * Dir0 + w3g.y * Dir1 + w3g.z * Dir2);
        dd += w4.x * g0 + w4.y * g1 + w4.z * g2;
        return dd * 70.0;
    }
};

SimplexSample2D CreateSimplex2D(float2 p) {
    float2 i = floor(p + dot(p, 1.0) * SF2);
    float2 L = i - dot(i, 1.0) * SG2;

    SimplexSample2D samp;
    
    samp.Dir0 = p - L;
    float2 o = samp.Dir0.x > samp.Dir0.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
    samp.Dir1 = samp.Dir0 - o + SG2;
    samp.Dir2 = samp.Dir0 - 1.0 + 2.0 * SG2;
    
    samp.Cell0 = i + 0;
    samp.Cell1 = i + o;
    samp.Cell2 = i + 1;

    float3 w = float3(dot(samp.Dir0, samp.Dir0), dot(samp.Dir1, samp.Dir1), dot(samp.Dir2, samp.Dir2));
    samp.CellWeights = saturate(0.5 - w);

    return samp;
}

struct SimplexSample3D {
    float3 Cell0, Cell1, Cell2, Cell3;
    float4 CellWeights;
    float4 ValueWeights;
    float Sample1() {
        return dot(ValueWeights, CellWeights * CellWeights);
    }
    float2 Sample2() {
        CellWeights /= dot(CellWeights, 1.0);
        return PermuteO2(PermuteV3(Cell0)) * CellWeights.x +
            PermuteO2(PermuteV3(Cell1)) * CellWeights.y+
            PermuteO2(PermuteV3(Cell2)) * CellWeights.z +
            PermuteO2(PermuteV3(Cell3)) * CellWeights.w;
    }
    float3 Sample3(float seed = 0) {
        CellWeights /= dot(CellWeights, 1.0);
        return PermuteO3(PermuteV3(Cell0 + seed)) * CellWeights.x +
            PermuteO3(PermuteV3(Cell1 + seed)) * CellWeights.y +
            PermuteO3(PermuteV3(Cell2 + seed)) * CellWeights.z +
            PermuteO3(PermuteV3(Cell3 + seed)) * CellWeights.w;
    }
};

SimplexSample3D CreateSimplex3D(float3 position) {
    position = position % 400;
    const float F3 =  0.3333333;
    const float G3 =  0.1666667;
    SimplexSample3D sample;
	sample.Cell0 = floor(position + dot(position, F3));
    float3 posCell0 = position + dot(sample.Cell0, G3);

    float3 x = posCell0 - sample.Cell0;
    float3 e = step(x.yzx, x);

    sample.Cell1 = sample.Cell0 + e - e * e.zxy;
    sample.Cell2 = sample.Cell0 + 1.0 - e.zxy + e * e.zxy;
    sample.Cell3 = sample.Cell0 + 1.0;
    
    /* x1, x2, x3 */
    float3 x1 = posCell0 - sample.Cell1 + G3;
    float3 x2 = posCell0 - sample.Cell2 + 2.0 * G3;
    float3 x3 = posCell0 - sample.Cell3 + 3.0 * G3;

    sample.Cell0 = sample.Cell0 % 50;
    sample.Cell1 = sample.Cell1 % 50;
    sample.Cell2 = sample.Cell2 % 50;
    sample.Cell3 = sample.Cell3 % 50;
    
    float4 w = float4(
        dot(x, x),
        dot(x1, x1),
        dot(x2, x2),
        dot(x3, x3)
    );
    /* w fades from 0.6 at the center of the surflet to 0.0 at the margin */
    w = max(0.6 - w, 0.0);
    w *= w;
    sample.CellWeights = w;
	
    sample.ValueWeights = float4(
        dot(PermuteO3(PermuteV3(sample.Cell0)) - 0.5, x),
        dot(PermuteO3(PermuteV3(sample.Cell1)) - 0.5, x1),
        dot(PermuteO3(PermuteV3(sample.Cell2)) - 0.5, x2),
        dot(PermuteO3(PermuteV3(sample.Cell3)) - 0.5, x3)
    ) * 52.0;
    //sample.ValueWeights /= dot(sample.ValueWeights, 1);
    
    return sample;
}

#endif
