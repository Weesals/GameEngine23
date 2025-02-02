cbuffer RayCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
    float Time;
    float2 TemporalJitter;
    float TemporalFrame;
    matrix ShadowIVViewProjection;
    matrix InvView;
    matrix InvViewProjection;
    float2 Resolution;
}

RaytracingAccelerationStructure Scene : register(t0);
RWTexture2D<float4> RenderTarget : register(u0);

struct RayPayload {
    float4 color;
};

[shader("raygeneration")]
void RayGen() {
    float2 viewUV = (float2) DispatchRaysIndex() / (float2) DispatchRaysDimensions() * 2.0f - 1.0f;
    viewUV.y = -viewUV.y;

    RayDesc ray;
    ray.Origin = mul(InvView, float4(0, 0, 0, 1)).xyz;

    float4 clipZ = mul(InvViewProjection, float4(viewUV, 0.0, 1.0));
    float4 projZ = transpose(InvViewProjection)[2];
    float3 directionVector = normalize(projZ.xyz * InvViewProjection._44 - clipZ.xyz * projZ.w);
    ray.Direction = directionVector;

    ray.TMin = 0.001;
    ray.TMax = 10000.0;

    RayPayload payload = { float4(0, 0, 0, 0) };
    TraceRay(Scene, RAY_FLAG_NONE, ~0, 0, 1, 0, ray, payload);

    RenderTarget[DispatchRaysIndex().xy] = payload.color;
}

[shader("closesthit")]
void Hit(inout RayPayload payload, in BuiltInTriangleIntersectionAttributes attr) {
    float3 barycentrics = float3(1 - attr.barycentrics.x - attr.barycentrics.y, attr.barycentrics.x, attr.barycentrics.y);
    payload.color = float4(barycentrics, 1);
}

[shader("miss")]
void Miss(inout RayPayload payload) {
    payload.color = float4(0, 0, 0, 1);
}
