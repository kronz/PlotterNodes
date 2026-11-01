//--------------------------------------------------------------------------------------
// Arctic ground plane: a shadow catcher. It has the background color wherever the sky is
// open, so its edges are invisible, and darkens where objects occlude the sky above it.
// Placed just below the lowest object (Rendering/GroundPlaneNode.cs).
//--------------------------------------------------------------------------------------
#define MESH
#include "Common.hlsl"
#include "ArcticCommon.hlsli"
#pragma pack_matrix( row_major )

float4 main(PSInput input) : SV_Target
{
    // Only in Arctic mode, and only seen from above.
    if (ArcticFlags.x < 0.5 || vEyePos.y < ArcticBackground.a)
    {
        discard;
    }

    float sky = SkyIrradiance(input.wp.xyz, float3(0, 1, 0));
    float shade = lerp(1, saturate(sky), saturate(ArcticFlags.w));

    // Screen-space AO fades out away from the scene, so the far ground (grazing angles,
    // low depth precision) stays exactly the background color.
    float r = length(input.wp.xz - GroundInfo.xy);
    float aoWeight = saturate((GroundInfo.w - r) / max(GroundInfo.w - GroundInfo.z, 1e-4));
    shade *= lerp(1, ArcticSsao(input.p), aoWeight);
    return float4(ArcticBackground.rgb * shade, 1);
}
