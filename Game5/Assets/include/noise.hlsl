
float2 RandomDir(float2 coord)
{
    float seed = dot(coord, float2(10.2, 125.62));
    seed = sin(seed) * 1000;
    return float2(sin(seed), cos(seed));
}

static const float SF2 = 0.5 * (sqrt(3.0) - 1.0);
static const float SG2 = (3.0 - sqrt(3.0)) / 6.0;

float SimplexNoise(float2 p, out float2 dd)
{
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
    float3 Sample3() {
        CellWeights /= dot(CellWeights, 1.0);
        return PermuteO3(PermuteV3(Cell0)) * CellWeights.x +
            PermuteO3(PermuteV3(Cell1)) * CellWeights.y +
            PermuteO3(PermuteV3(Cell2)) * CellWeights.z +
            PermuteO3(PermuteV3(Cell3)) * CellWeights.w;
    }
};

SimplexSample3D SimplexNoise3D(float3 position) {
    const float F3 =  0.3333333;
    const float G3 =  0.1666667;
    SimplexSample3D sample;
	sample.Cell0 = floor(position + dot(position, F3));
    float3 x = position - sample.Cell0 + dot(sample.Cell0, G3);
    /* calculate i1 and i2 */
    float3 e = step(0.0, x - x.yzx);
    float3 i1 = e * (1.0 - e.zxy);
    float3 i2 = 1.0 - e.zxy * (1.0 - e);
    /* x1, x2, x3 */
    float3 x1 = x - i1 + G3;
    float3 x2 = x - i2 + 2.0 * G3;
    float3 x3 = x - 1.0 + 3.0 * G3;
    
    float4 w = float4(
        dot(x, x),
        dot(x1, x1),
        dot(x2, x2),
        dot(x3, x3)
    );
    /* w fades from 0.6 at the center of the surflet to 0.0 at the margin */
    w = max(0.6 - w, 0.0);
	
    sample.Cell1 = sample.Cell0 + i1;
    sample.Cell2 = sample.Cell0 + i2;
    sample.Cell3 = sample.Cell0 + 1.0;
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
