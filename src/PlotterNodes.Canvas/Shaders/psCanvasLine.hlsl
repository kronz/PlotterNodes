//--------------------------------------------------------------------------------------
// Canvas 3D line pixel shader: replaces Dynamo's psDynamoLine.hlsl inside the canvas window
// (the "DynamoLineShader" technique of the window's effects manager).
//
// Same as Dynamo's, plus: in Arctic display without "Use object colors", curves and edges
// are drawn black, like Rhino's Arctic mode. The selection highlight, frozen and isolated
// transparency and Dynamo's special packages (grid, axes) keep Dynamo's look.
//
// Dynamo packs its state flags into fadeNearDistance (DynamoPointLineRenderCore):
//   IsFrozen = 1, IsSelected = 2, IsIsolated = 4, IsSpecialRenderPackage = 8
//--------------------------------------------------------------------------------------
#define POINTLINE
#include "Common.hlsl"
#include "ArcticCommon.hlsli"

static const float4 vSelectionColor = float4(0.023529, 0.588235, 0.843137, 1.0);

float4 main(PSInputPS input) : SV_Target
{
    int flags = int(fadeNearDistance);
    bool isFrozen = flags & 1;
    bool isSelected = flags & 2;
    bool isIsolated = flags & 4;
    bool isSpecial = flags & 8;

    float4 I = input.c;

    if (ArcticFlags.x > 0.5 && ArcticFlags.y < 0.5 && !isSpecial)
    {
        I.rgb = float3(0, 0, 0);
    }

    // if frozen half alpha
    if (isFrozen)
    {
        I.a = .5f;
    }
    // if isolated, alpha is always low (except when selected)
    if (isIsolated && !isSelected)
    {
        I.a = .2f;
    }

    if (isSelected && !isIsolated)
    {
        I = lerp(vSelectionColor, I, 0.3);
    }

    return I;
}
