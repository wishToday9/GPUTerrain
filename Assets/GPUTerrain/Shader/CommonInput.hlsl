#ifndef TERRAIN_COMMON_INPUT
#define TERRAIN_COMMON_INPUT

//����LOD������5
#define MAX_TERRAIN_LOD 5

//LOD5->25 LOD4->100 LOD3->400 LOD2->1600 LOD1->6400 LOD0->25600
//Sum all the num
#define MAX_NODE_ID 34124

//һ��PatchMesh��16*16�����񹹳�
#define PATCH_MESH_GRID_COUNT 16

//һ��patchmesh�߳���8m
#define PATCH_MESH_SIZE 8

//һ��Node��8*8��Patch���
#define PATCH_COUNT_PER_NODE 8

//PatchMeshһ�����ӵĴ�СΪ0.5*0.5
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
