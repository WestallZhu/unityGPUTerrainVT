#ifndef UNIVERSAL_TERRAIN_LIT_PASSES_INCLUDED
#define UNIVERSAL_TERRAIN_LIT_PASSES_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "GPUTerrainVetex.hlsl"


struct Varyings
{
    uint index : SV_InstanceID;
    float4 uvMainAndLM : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float4 positionCS : SV_POSITION;
    half3 viewDirectionWS : TEXCOORD2;
    half fogFactor : TEXCOORD3;
};

#ifdef _TERRAIN_VIRTUAL_TEXTURE

TEXTURE2D(_PhyscisAlbedo);
TEXTURE2D(_PhyscisNormal);
Texture2D<float4> _PageTableTexture;

SAMPLER(sampler_PhyscisAlbedo);
SAMPLER(sampler_PhyscisNormal);
SAMPLER(sampler_PageTableTexture);

float4 _VTRect;

// xy: page count
// z:  max mipmap level
float4 _VTPageParams;


//paddingSize, tileSize, TextureSize, TextureSize
float4 _VTPageTileParams;

void VirtualTextureSample(Varyings In, out half3 albedo, out half3 normal)
{
    float2 uv = (In.positionWS.xz - _VTRect.xy) / _VTRect.zw;
    float2 uvInt = uv - frac(uv * _VTPageParams.x) * _VTPageParams.y;
    float4 pageTable = _PageTableTexture.SampleLevel(sampler_PageTableTexture, uvInt, 0) * 255.0f;

    float2 curPageOffset = frac(uvInt * exp2(_VTPageParams.z - pageTable.b));
    uvInt = (pageTable.rg * (_VTPageTileParams.y + _VTPageTileParams.x * 2) + curPageOffset * _VTPageTileParams.y + _VTPageTileParams.x) / _VTPageTileParams.zw;

    albedo.rgb = _PhyscisAlbedo.SampleLevel(sampler_PhyscisAlbedo, uvInt, 0).rgb;
    normal.xyz = _PhyscisNormal.SampleLevel(sampler_PhyscisNormal, uvInt, 0).xyz;
}
#endif

Texture2DArray _SplatArray, _WorldNormalArray, _AlbedoArray, _NormalArray;


SamplerState Global_trilinear_clamp_sampler;
SamplerState Global_trilinear_repeat_sampler;

float _SplatOffset[16];
float _SplatCount[16];
float  _TileData[16];


float MipLevel(float2 UV)
{
    float2 DX = ddx(UV);
    float2 DY = ddy(UV);
    float MaxSqr = max(dot(DX, DX), dot(DY, DY));
    float MipLevel = 0.5 * log2(MaxSqr);
    return max(0, MipLevel);
}

Varyings SplatmapVert(Attributes v)
{
    Varyings o;

    float2 sectorUV;
    GTerrainInput vinput;
    vinput = GetGTerrainInput(v);
    o.positionWS = vinput.positionWS;
    o.positionCS = mul(UNITY_MATRIX_VP, float4(vinput.positionWS, 1.0));
    o.uvMainAndLM.xy = vinput.sectorUV;
    o.uvMainAndLM.zw = vinput.sectorUV * unity_LightmapST.xy + unity_LightmapST.zw;
    o.index = vinput.index;
    o.viewDirectionWS = GetWorldSpaceViewDir(vinput.positionWS);
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    return o;
}

void SplatmapMix(int passIndex, float splatOffset, float2 splatUV, float2 surfaceUV, out half weight, out half4 mixedDiffuse, out half4 mixedNormal)
{
    half4 splatControl = _SplatArray.Sample(Global_trilinear_clamp_sampler, float3(splatUV, splatOffset));

    weight = dot(splatControl, 1.0h);
    splatControl /= (weight + HALF_MIN);

    mixedDiffuse = 0.0f;
    mixedNormal = 0.0f;

    float index = passIndex * 4;
    mixedDiffuse += splatControl.r * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index], index));
    mixedDiffuse += splatControl.g * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 1], index + 1));
    mixedDiffuse += splatControl.b * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 2], index + 2));
    mixedDiffuse += splatControl.a * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 3], index + 3));


    mixedNormal += splatControl.r * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index], index));
    mixedNormal += splatControl.g * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 1], index + 1));
    mixedNormal += splatControl.b * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 2], index + 2));
    mixedNormal += splatControl.a * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 3], index + 3));
    mixedNormal.xyz = UnpackNormal(mixedNormal);
    mixedNormal.xyz = normalize(mixedNormal.xyz);
}
half4 SplatmapFragment(Varyings In) : SV_Target
{
    float3 normalWS = _WorldNormalArray.Sample(Global_trilinear_clamp_sampler, float3(In.uvMainAndLM.xy, In.index)).rgb * 2 - 1;

    Light light = GetMainLight();
    half4 color = max(0.05, dot(light.direction, normalWS));

    float splatOffset = _SplatOffset[In.index];
    float splatCount = _SplatCount[In.index];

    half weight;
    half4 mixedDiffuse=0;
    half4 normalTS=0;
    float2 splatUV = (In.uvMainAndLM.xy * (_TerrainSize - 1.0f) + 0.5f) * rcp(_TerrainSize);
    float2 surfaceUV = In.uvMainAndLM.xy;


#ifdef _TERRAIN_VIRTUAL_TEXTURE
    VirtualTextureSample(In, mixedDiffuse.rgb, normalTS.xyz);
#else
    [unroll(2)]
    for (int i = 0; i < splatCount; i++)
    {
        SplatmapMix(0, splatOffset + i, splatUV, surfaceUV,  weight, mixedDiffuse, normalTS);
    }
#endif
    half3 tangentWS = cross(GetObjectToWorldMatrix()._13_23_33, normalWS);
    normalWS = TransformTangentToWorld(normalTS, half3x3(-tangentWS, cross(normalWS, tangentWS), normalWS));

    half3 SH = 0;
    InputData inputData;
    inputData.positionWS = In.positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = normalize(In.viewDirectionWS);
    inputData.bakedGI = SampleSH(normalWS); //SAMPLE_GI(input.uvMainAndLM.zw, SH, normalWS);

    inputData.shadowCoord = TransformWorldToShadowCoord(In.positionWS);
    inputData.fogCoord = In.fogFactor;
    inputData.vertexLighting = 0;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(In.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.uvMainAndLM.zw);

    half3 albedo = mixedDiffuse.rgb;
    half metallic = 0;
    half alpha = 1;
    half smoothness = 0;
    half occlusion = 1;
    return UniversalFragmentPBR(inputData, albedo, metallic, /* specular */ half3(0.0h, 0.0h, 0.0h), smoothness, occlusion, /* emission */ half3(0, 0, 0), alpha);
}

#endif