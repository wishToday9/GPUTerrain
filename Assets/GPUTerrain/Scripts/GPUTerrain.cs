using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUTerrain{
    public class GPUTerrain : MonoBehaviour
    {
        public TerrainAsset terrainAsset;

        public bool isFrustumCullEnabled = true;
        public bool isHizOcclusionCullingEnabled = true;

        [Range(0.01f, 1000)]
        public float hizDepthBias = 1;

        [Range(0, 100)]
        public int boundsHeightRedundance = 5;

        [Range(0.1f, 1.9f)]
        public float distanceEvaluation = 1.2f;

        //是否处理LOD之间的接缝问题
        public bool seamLess = true;

        //在渲染的时候，Patch之间要留出来一定的缝隙提供Debug
        public bool patchDebug = false;

        public bool nodeDebug = false;

        public bool mipDebug = false;

        public bool patchBoundsDebug = false;

        private TerrainBuilder _traverse;
        private Material _terrainMaterial;

        private bool _isTerrainMaterialDirty = false;

        void Start()
        {
            _traverse = new TerrainBuilder(terrainAsset);
            terrainAsset.boundsDebugMaterial.SetBuffer("BoundsList", _traverse.patchBoundsBuffer);
            this.ApplySettings();
        }

        void OnValidate()
        {
            this.ApplySettings();
        }

        void OnDestroy()
        {
            _traverse.Dispose();
        }

        void ApplySettings() {
            if (_traverse != null) {
                _traverse.isFrustumCullEnabled = this.isFrustumCullEnabled;
                _traverse.isBoundsBufferOn = this.patchBoundsDebug;
                _traverse.isHizOcclusionCullingEnabled = this.isHizOcclusionCullingEnabled;
                _traverse.boundsHeightRedundance = this.boundsHeightRedundance;
                _traverse.enableSeamDebug = this.patchDebug;
                _traverse.nodeEvalDistance = this.distanceEvaluation;
                _traverse.hizDepthBias = this.hizDepthBias;
            }
        }

        void UpdateTerrainMaterialProperties() {
            _isTerrainMaterialDirty = false;
            if (_terrainMaterial) {
                if (seamLess)
                {
                    _terrainMaterial.EnableKeyword("ENABLE_LOD_SEAMLESS");
                }
                else
                {
                    _terrainMaterial.DisableKeyword("ENABLE_LOD_SEAMLESS");
                }
                if (mipDebug)
                {
                    _terrainMaterial.EnableKeyword("ENABLE_MIP_DEBUG");
                }
                else
                {
                    _terrainMaterial.DisableKeyword("ENABLE_MIP_DEBUG");
                }
                if (this.patchDebug)
                {
                    _terrainMaterial.EnableKeyword("ENABLE_PATCH_DEBUG");
                }
                else
                {
                    _terrainMaterial.DisableKeyword("ENABLE_PATCH_DEBUG");
                }
                if (this.nodeDebug)
                {
                    _terrainMaterial.EnableKeyword("ENABLE_NODE_DEBUG");
                }
                else
                {
                    _terrainMaterial.DisableKeyword("ENABLE_NODE_DEBUG");
                }
                _terrainMaterial.SetVector("_WorldSize", terrainAsset.worldSize);
                _terrainMaterial.SetMatrix("_WorldToNormalMapMatrix", Matrix4x4.Scale(this.terrainAsset.worldSize).inverse);
            }
        }
        private Material EnsureTerrainMaterial() {
            if (!_terrainMaterial) {
                var material = new Material(Shader.Find("GPUTerrain/Terrain"));
                material.SetTexture("_HeightMap", terrainAsset.heightMap);
                material.SetTexture("_NormalMap", terrainAsset.normalMap);
                material.SetTexture("_MainTex", terrainAsset.albedoMap);
                material.SetBuffer("PatchList", _traverse.culledPatchBuffer);
                _terrainMaterial = material;
                this.UpdateTerrainMaterialProperties();
            }
            return _terrainMaterial;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) {
                _traverse.Dispatch();
            }
            _traverse.Dispatch();
            var terrainMaterial = this.EnsureTerrainMaterial();
            if (_isTerrainMaterialDirty) {
                this.UpdateTerrainMaterialProperties();
            }
            Graphics.DrawMeshInstancedIndirect(TerrainAsset.patchMesh, 0, terrainMaterial, new Bounds(Vector3.zero, Vector3.one * 10240), _traverse.patchIndirectArgs);
            if (patchBoundsDebug)
            {
                Graphics.DrawMeshInstancedIndirect(TerrainAsset.unitCubeMesh, 0, terrainAsset.boundsDebugMaterial, new Bounds(Vector3.zero, Vector3.one * 10240), _traverse.boundsIndirectArgs);
            }
        }
    }

}