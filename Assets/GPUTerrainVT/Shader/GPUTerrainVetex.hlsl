#ifndef GPUTERRAIN_VETEX_HLSL
#define GPUTERRAIN_VETEX_HLSL

#include "TerrainInput.hlsl"

float _TerrainHeightmapScale;
float3 _SceneOffset;
float _TerrainSize;
uint _MaxLodNodeCount;
float _UnitMeter;

Texture2DArray _HeightArray;
SamplerState Global_point_clamp_sampler;

struct Attributes
{
    float2 uv : TEXCOORD0;
    float4 vertex : POSITION;
    uint instanceID : SV_InstanceID;
};

StructuredBuffer<RenderPatch> _PatchRenderList;

void StitchingLodMesh(inout float4 vertex, uint4 lodTransition)
{
    uint2 vertIndex = (vertex.xz + 0.001) / _UnitMeter + PATCH_MESH_GRID_COUNT * 0.5;

    if (lodTransition.x > 0 && vertIndex.x == 0)// left
    {
        uint stripCount = exp2(lodTransition.x);
        uint modIndex = vertIndex.y % stripCount;
        if (modIndex != 0)
        {
            vertex.z -= _UnitMeter * modIndex;
            return;
        }
    }

    if (lodTransition.y > 0 && vertIndex.y == 0) // down
    {
        uint stripCount = exp2(lodTransition.y);
        uint modIndex = vertIndex.x % stripCount;
        if (modIndex != 0)
        {
            vertex.x -= _UnitMeter * modIndex;
            return;
        }
    }

    if (lodTransition.z > 0 && vertIndex.x == PATCH_MESH_GRID_COUNT)//right
    {
        uint stripCount = exp2(lodTransition.z);
        uint modIndex = vertIndex.y % stripCount;
        if (modIndex != 0)
        {
            vertex.z += _UnitMeter * (stripCount - modIndex);
            return;
        }
    }

    if (lodTransition.w > 0 && vertIndex.y == PATCH_MESH_GRID_COUNT) // up
    {
        uint stripCount = exp2(lodTransition.w);
        uint modIndex = vertIndex.x % stripCount;
        if (modIndex != 0)
        {
            vertex.x += _UnitMeter * (stripCount - modIndex);
            return;
        }
    }

}

struct GTerrainInput
{
    float3 positionWS;
    float2 sectorUV;
    uint index;
};

GTerrainInput GetGTerrainInput(Attributes v)
{
    GTerrainInput o;
    RenderPatch patch = _PatchRenderList[v.instanceID];

    uint lod = patch.lods >> 16;
    float scale = exp2(lod);

    uint4 lodTransition = { patch.lods & 0x0F,
        (patch.lods >> 4) & 0x0F,
        (patch.lods >> 8) & 0x0F,
        (patch.lods >> 12) & 0x0F
    };

    StitchingLodMesh(v.vertex, lodTransition);

    float3 positionWS;
    positionWS.xz = v.vertex.xz * scale + patch.position;

    float2 sectorUV = (positionWS.xz - _SceneOffset.xz) * rcp(_TerrainSize) - float2(patch.index % _MaxLodNodeCount, patch.index / _MaxLodNodeCount);
    float4 height = _HeightArray.SampleLevel(Global_point_clamp_sampler, float3(sectorUV, patch.index), 0, 0);

    positionWS.y = UnpackHeightmap(height) * _TerrainHeightmapScale + _SceneOffset.y;

    o.positionWS = positionWS;
    o.sectorUV = sectorUV;
    o.index = patch.index;
    return o;
}


#endif