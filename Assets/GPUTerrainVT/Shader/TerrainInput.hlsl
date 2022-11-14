#ifndef TERRAIN_COMMON_INPUT
#define TERRAIN_COMMON_INPUT

#define MAX_LOD 4

#define PATCH_MESH_GRID_COUNT 16


struct RenderPatch
{
    float2 position;
    uint lods;//lodTransition.x | lodTransition.y << 4 | lodTransition.z << 8 | lodTransition.w << 12 | lod << 16;
    uint index;
};


#endif