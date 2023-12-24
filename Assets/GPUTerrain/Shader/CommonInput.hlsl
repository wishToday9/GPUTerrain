#ifndef TERRAIN_COMMON_INPUT
#define TERRAIN_COMMON_INPUT

//最大的LOD级别是5
#define MAX_TERRAIN_LOD 5

//LOD5->25 LOD4->100 LOD3->400 LOD2->1600 LOD1->6400 LOD0->25600
//Sum all the num
#define MAX_NODE_ID 34124

//一个PatchMesh由16*16个网格构成
#define PATCH_MESH_GRID_COUNT 16

//一个patchmesh边长是8m
#define PATCH_MESH_SIZE 8

//一个Node由8*8个Patch组成
#define PATCH_COUNT_PER_NODE 8

//PatchMesh一个格子的大小为0.5*0.5
#define PATCH_MESH_GRID_SIZE 0.5

#define SECTOR_COUNT_WORLD 160

struct NodeDescriptor{
	uint branch;
};

struct RenderPatch{
	float2 position;
	//float2 minMaxHeight;
	uint lod;
	//uint4 lodTrans;
};

struct Bounds{
	float3 minPosition;
	float3 maxPosition;
};

struct BoundsDebug{
	Bounds bounds;
	float4 color;
};

#endif
