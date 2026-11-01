//--------------------------------------------------------------------------------------
// Canvas 3D mesh pixel shader: replaces Dynamo's psDynamoMesh.hlsl inside the canvas
// window only (the "DynamoMeshShader" technique of the window's effects manager).
//
//   ArcticFlags.x == 0 : Dynamo shading, a line-by-line port of psDynamoMesh.hlsl
//   ArcticFlags.x == 1 : Arctic display, white material lit by an overcast sky whose
//                        visibility comes from the sky shadow maps, plus ground bounce,
//                        a soft camera fill light and screen-space ambient occlusion.
//
// Dynamo's per-model state flags arrive in vParams.x (DynamoGeometryMeshCore):
//   IsFrozen = 1, IsSelected = 2, IsIsolated = 4, IsSpecialRenderPackage = 8,
//   HasTransparency = 16 (unused), RequiresPerVertexColoration = 32, FlatShade = 64 (unused)
//--------------------------------------------------------------------------------------
#define MESH
#include "Common.hlsl"
#include "ArcticCommon.hlsli"
#pragma pack_matrix( row_major )

static const float4 vSelectionColor = float4(0.023529, 0.588235, 0.843137, 1.0);

float4 calcPhongLighting(float4 LColor, float4 vMaterialTexture, float3 N, float3 L, float3 V, float3 R)
{
    float4 Id = vMaterialTexture * vMaterialDiffuse * saturate(dot(N, L));
    float4 Is = vMaterialSpecular * pow(saturate(dot(R, V)), sMaterialShininess);
    return (Id + Is) * LColor;
}

float4 ShadeDynamo(PSInput input, bool isFrontFacing, int flags)
{
    bool isFrozen = flags & 1;
    bool isSelected = flags & 2;
    bool isIsolated = flags & 4;
    bool isSpecialRenderPackage = flags & 8;
    bool requiresPerVertexColoration = flags & 32;

    float3 n = normalize(input.n);
    float3 eye = normalize(vEyePos - input.wp.xyz);
    n = isFrontFacing ? n : -n;

    if (isSpecialRenderPackage)
    {
        return vMaterialDiffuse + vMaterialEmissive + vMaterialAmbient * vLightAmbient;
    }

    if (bHasDiffuseMap)
    {
        float4 T = texDiffuseMap.Sample(samplerSurface, input.t);
        if (isSelected)
        {
            T = lerp(vSelectionColor, T, 0.3);
        }
        return T;
    }

    float4 I = vMaterialEmissive + vMaterialAmbient * vLightAmbient;
    float4 vMaterialTexture = 1.0f;
    for (int i = 0; i < NumLights; ++i)
    {
        if (Lights[i].iLightType == 1) // directional
        {
            float3 lightDirection = normalize((float3) Lights[i].vLightDir);
            float3 reflectedLightDir = reflect(-lightDirection, n);
            I += calcPhongLighting(Lights[i].vLightColor, vMaterialTexture, n, lightDirection, eye, reflectedLightDir);
        }
    }

    I.a = vMaterialDiffuse.a;
    if (isFrozen)
    {
        I.a = .5f;
    }
    if (isIsolated && !isSelected)
    {
        I.a = .1f;
    }
    if (requiresPerVertexColoration)
    {
        I = I * input.c;
    }
    if (isSelected && !isIsolated)
    {
        I = lerp(vSelectionColor, I, 0.3);
    }
    return I;
}

float4 ShadeArctic(PSInput input, bool isFrontFacing, int flags)
{
    bool isFrozen = flags & 1;
    bool isSelected = flags & 2;
    bool isIsolated = flags & 4;
    bool isSpecialRenderPackage = flags & 8;
    bool requiresPerVertexColoration = flags & 32;

    // Manipulator gizmos keep their flat Dynamo colors.
    if (isSpecialRenderPackage)
    {
        return vMaterialDiffuse + vMaterialEmissive + vMaterialAmbient * vLightAmbient;
    }

    float3 n = normalize(input.n);
    n = isFrontFacing ? n : -n;
    float3 wp = input.wp.xyz;
    float3 v = normalize(vEyePos - wp);

    // Albedo: one material color, or Dynamo's display colors when asked for.
    float3 albedo = ArcticAlbedo.rgb;
    float alpha = vMaterialDiffuse.a;
    if (ArcticFlags.y > 0.5)
    {
        if (bHasDiffuseMap)
        {
            float4 T = texDiffuseMap.Sample(samplerSurface, input.t);
            albedo = T.rgb;
            alpha = T.a;
        }
        else if (requiresPerVertexColoration)
        {
            albedo = input.c.rgb;
        }
    }
    if (requiresPerVertexColoration)
    {
        alpha *= input.c.a;
    }

    // Overcast sky (occluded by the sky shadow maps), light bounced from the ground onto
    // downward facing surfaces, and a soft light from the camera.
    float sky = SkyIrradiance(wp, n);
    float bounce = saturate(0.5 - 0.5 * n.y);
    float fill = saturate(dot(n, v));
    float e = ArcticLight.x * sky + ArcticLight.y * bounce + ArcticLight.z * fill;
    e *= ArcticSsao(input.p);

    float4 I = float4(saturate(albedo * e * ArcticLight.w), alpha);

    if (isFrozen)
    {
        I.a = .5f;
    }
    if (isIsolated && !isSelected)
    {
        I.a = .1f;
    }
    if (isSelected && !isIsolated)
    {
        I.rgb = lerp(I.rgb, vSelectionColor.rgb, 0.55);
    }
    return I;
}

float4 main(PSInput input, bool isFrontFacing : SV_IsFrontFace) : SV_Target
{
    int flags = int(vParams.x);
    if (ArcticFlags.x < 0.5)
    {
        return ShadeDynamo(input, isFrontFacing, flags);
    }
    return ShadeArctic(input, isFrontFacing, flags);
}
