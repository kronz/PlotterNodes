//--------------------------------------------------------------------------------------
// Sky shadow map pass, vertex shader. Renders a Dynamo mesh into one slice of the sky
// shadow array: an orthographic view along one sky direction, fitted to the bounds of all
// casters (Rendering/SkyOcclusion.cs). Helix calls it through SceneNode.RenderDepth, which
// uploads the per-model cbMeshSimple (world matrix, instancing flag).
//--------------------------------------------------------------------------------------
#define MESHSIMPLE
#include "Common.hlsl"
#include "DataStructs.hlsl"
#pragma pack_matrix( row_major )

cbuffer cbArcticLight : register(b11)
{
    float4 LightU;   // u01 = dot(p, xyz) + w
    float4 LightV;   // v01 = dot(p, xyz) + w
    float4 LightW;   // depth01 = dot(p, xyz) + w, 0 at the sky side
    float4 LightPad;
};

struct SkyDepthPSInput
{
    float4 p : SV_POSITION;
    float depth : TEXCOORD0;
};

SkyDepthPSInput main(VSInput input)
{
    float4 p = input.p;
    if (bHasInstances)
    {
        matrix mInstance =
        {
            input.mr0,
            input.mr1,
            input.mr2,
            input.mr3
        };
        p = mul(p, mInstance);
    }
    float3 wp = mul(p, mWorld).xyz;

    float u = dot(wp, LightU.xyz) + LightU.w;
    float v = dot(wp, LightV.xyz) + LightV.w;
    float d = dot(wp, LightW.xyz) + LightW.w;

    SkyDepthPSInput output;
    output.p = float4(u * 2 - 1, 1 - v * 2, saturate(d), 1);
    output.depth = d;
    return output;
}
