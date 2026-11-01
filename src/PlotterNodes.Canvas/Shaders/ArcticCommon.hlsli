#ifndef ARCTICCOMMON_HLSLI
#define ARCTICCOMMON_HLSLI
//--------------------------------------------------------------------------------------
// Arctic display: shared constants and sky-light functions.
//
// Included after Common.hlsl (Helix 2.24) by psCanvasMesh.hlsl and psGround.hlsl.
// The C# mirror is Rendering/ArcticConstants.cs: keep MAX_SKY_DIRECTIONS and the order
// of the cbArctic members in sync with it (the constant buffer pool checks the size).
//
// Registers: Helix 2.24 uses b0-b9, t0-t5, t10-t13, t20-t21, t30-t33, t40, t50, t60-t62
// and s0-s9, so the Arctic resources sit on b10, t35, s10 and s11. SkyOcclusionCore binds
// t35/s10/s11 once per frame in the pre-processing stage; Helix binds cbArctic by name.
//--------------------------------------------------------------------------------------

#define MAX_SKY_DIRECTIONS 256

cbuffer cbArctic : register(b10)
{
    float4 ArcticFlags;      // x: Arctic on (0/1), y: use object colors (0/1), z: SSAO strength, w: ground shadow strength
    float4 ArcticLight;      // x: sky light, y: ground bounce, z: fill light, w: exposure
    float4 ArcticAlbedo;     // rgb: material color
    float4 ArcticBackground; // rgb: background color, a: ground plane height (world y)
    float4 SkyInfo;          // x: sky directions in use, y: 1 / sum(weight * up.dir), z: shadow maps valid (0/1)
    float4 GroundInfo;       // xy: scene center (world x, z), z: ground AO fade start radius, w: fade end radius
    float4 SkyU[MAX_SKY_DIRECTIONS];    // shadow map u   = dot(p, xyz) + w
    float4 SkyV[MAX_SKY_DIRECTIONS];    // shadow map v   = dot(p, xyz) + w
    float4 SkyW[MAX_SKY_DIRECTIONS];    // shadow depth   = dot(p, xyz) + w, 0 at the sky side
    float4 SkyDir[MAX_SKY_DIRECTIONS];  // xyz: unit vector toward the sky, w: radiance weight
    float4 SkyBias[MAX_SKY_DIRECTIONS]; // x: depth bias (depth units), y: normal offset (world units)
};

// One R16_UNORM slice per sky direction: the light-space depth of the nearest caster.
Texture2DArray<float> texSkyShadow : register(t35);
// LessEqual comparison with bilinear PCF; border = 1 (outside the map nothing casts).
SamplerComparisonState samplerSkyShadow : register(s10);
// Linear clamp, used for the SSAO map.
SamplerState samplerArcticLinear : register(s11);

//--------------------------------------------------------------------------------------
// Sky irradiance at a world position with normal n, normalized so that an unoccluded,
// upward facing surface receives 1. With shadow maps it is ambient occlusion from the sky
// (overcast distribution); without them it is the unoccluded hemisphere light.
//--------------------------------------------------------------------------------------
float SkyIrradiance(float3 wp, float3 n)
{
    int count = min((int)SkyInfo.x, MAX_SKY_DIRECTIONS);
    if (count <= 0)
    {
        return saturate(0.5 + 0.5 * n.y);
    }
    bool useMaps = SkyInfo.z > 0.5;
    float acc = 0;
    [loop]
    for (int k = 0; k < count; ++k)
    {
        float4 dir = SkyDir[k];
        float ndl = dot(n, dir.xyz);
        [branch]
        if (ndl > 0)
        {
            float vis = 1;
            [branch]
            if (useMaps)
            {
                // Normal offset and slope-scaled depth bias, both growing at grazing angles
                // (mirrored in tools/arctic_prototype/arctic_reference.py).
                float sinT = sqrt(saturate(1 - ndl * ndl));
                float3 p = wp + n * (SkyBias[k].y * (0.5 + sinT));
                float u = dot(p, SkyU[k].xyz) + SkyU[k].w;
                float v = dot(p, SkyV[k].xyz) + SkyV[k].w;
                // Outside a map's footprint nothing casts toward this direction: skip the fetch.
                [branch]
                if (u > 0 && u < 1 && v > 0 && v < 1)
                {
                    float d = dot(p, SkyW[k].xyz) + SkyW[k].w;
                    float bias = SkyBias[k].x * (1 + min(sinT / max(ndl, 0.1), 4));
                    vis = texSkyShadow.SampleCmpLevelZero(samplerSkyShadow, float3(u, v, (float)k), saturate(d) - bias);
                }
            }
            acc += dir.w * ndl * vis;
        }
    }
    return acc * SkyInfo.y;
}

//--------------------------------------------------------------------------------------
// Screen-space ambient occlusion factor (1 = open), already blended by its strength.
// texSSAOMap and SSAOEnabled come from Helix (CommonBuffers.hlsl); Helix renders the map in
// the pre-processing stage when Viewport3DX.EnableSSAO is on.
//--------------------------------------------------------------------------------------
float ArcticSsao(float4 svPosition)
{
    if (!SSAOEnabled)
    {
        return 1;
    }
    float2 uv = svPosition.xy * vResolution.zw;
    float ao = texSSAOMap.SampleLevel(samplerArcticLinear, uv, 0).r;
    return lerp(1, saturate(ao), saturate(ArcticFlags.z));
}

#endif
