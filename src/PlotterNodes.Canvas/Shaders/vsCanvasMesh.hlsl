//--------------------------------------------------------------------------------------
// Canvas 3D mesh vertex shader. Same transform as Dynamo's vsDynamoMesh.hlsl, written
// against the Helix 2.24 headers, and it fills the eye vector for the Arctic pass.
// Used by the replaced "DynamoMeshShader" technique and by the ground plane technique.
//--------------------------------------------------------------------------------------
#define MESH
#include "Common.hlsl"
#include "DataStructs.hlsl"
#pragma pack_matrix( row_major )

PSInput main(VSInput input)
{
    PSInput output = (PSInput)0;

    float4 inputp = input.p;
    float3 inputn = input.n;

    // compose instance matrix (Dynamo's instanced render packages)
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
        inputn = mul(inputn, (float3x3) mInstance);
    }

    output.wp = mul(inputp, mWorld);
    output.p = mul(output.wp, mViewProjection);
    output.n = normalize(mul(inputn, (float3x3) mWorld));
    output.vEye = float4(normalize(vEyePos - output.wp.xyz), 1);
    output.t = input.t;
    output.c = input.c;
    output.cDiffuse = vMaterialDiffuse;
    return output;
}
