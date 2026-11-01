//--------------------------------------------------------------------------------------
// Sky shadow map pass, pixel shader: writes the light-space depth (0..1) of the nearest
// caster into the R16_UNORM slice. The depth test uses a separate D32 buffer, so Dynamo's
// per-mesh depth bias (DepthBias = 400) cannot shift the stored values.
//--------------------------------------------------------------------------------------
struct SkyDepthPSInput
{
    float4 p : SV_POSITION;
    float depth : TEXCOORD0;
};

float main(SkyDepthPSInput input) : SV_Target
{
    return saturate(input.depth);
}
