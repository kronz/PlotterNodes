//--------------------------------------------------------------------------------------
// Canvas 3D line vertex shader: Dynamo's vsDynamoPointLine.hlsl (DynamoCoreWpf, compiled
// into Dynamo's resources and not reachable from a package), with the unused outputs set.
//--------------------------------------------------------------------------------------
#define POINTLINE
#include "Common.hlsl"
#pragma pack_matrix( row_major )

GSInputPS main(VSInputPS input)
{
    GSInputPS output = (GSInputPS)0;

    float4 inputp = input.p;

    // compose instance matrix
    if (bHasInstances)
    {
        matrix mInstance =
        {
            input.mr0,
            input.mr1,
            input.mr2,
            input.mr3
        };
        inputp = mul(inputp, mInstance);
    }

    // set position into clip space
    output.wp = mul(inputp, mWorld);
    float3 vEye = vEyePos - output.wp.xyz;
    output.vEye = float4(normalize(vEye), length(vEye));
    output.p = mul(output.wp, mView);
    output.p = mul(output.p, mProjection);

    output.c = input.c * pColor;

    return output;
}
