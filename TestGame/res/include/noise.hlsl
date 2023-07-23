
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
