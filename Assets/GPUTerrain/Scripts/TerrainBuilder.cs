using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;


using System;
using System.Collections.Generic;
using System.Security;
using Unity.Collections;
using UnityEngine.Bindings;
using UnityEngine.Scripting;



namespace GPUTerrain { 

    public class TerrainBuilder : System.IDisposable
    {
        private ComputeShader _computeShader;

        //存放LOD5层级的索引 (0, 0) =>(4, 4)
        private ComputeBuffer _maxLODNodeList;

        //consume 和 appendNode 的信息
        private ComputeBuffer _nodeListA;
        private ComputeBuffer _nodeListB;
        
        //最终分配的节点的信息
        private ComputeBuffer _finalNodeListBuffer;
        //判断该节点是否被细分
        private ComputeBuffer _nodeDescriptors;


        //经过剔除后真正需要被绘制的Buffer
        private ComputeBuffer _culledPatchBuffer;
        
        //绘制patch的一些数据参数
        private ComputeBuffer _patchIndirectArgs;

        //patch 包围盒缓冲区
        private ComputeBuffer _patchBoundsBuffer;
        private ComputeBuffer _patchBoundsIndirectArgs;

        private ComputeBuffer _indirectArgsBuffer;
        private RenderTexture _lodMap;

        private const int PatchStripSize = 9 * 4;

        //评估节点的信息
        private Vector4 _nodeEvaluationC = new Vector4(1, 0, 0, 0);
        private bool _isNodeEvaluationCDirty = true;

        private float _hizDepthBias = 1;

        private TerrainAsset _asset;

        private CommandBuffer _commandBuffer = new CommandBuffer();
        private Plane[] _cameraFrustumPlanes = new Plane[6];
        private Vector4[] _cameraFrustumPlanesV4 = new Vector4[6];

        //细分四叉树的节点
        private int _kernelOfTraverseQuadTree;
        //生成LODmap信息
        private int _kernelOfBuildLodMap;
        
        //生成真正的patch数据，传入的都是scetor(8*8 patch)
        private int _kernelOfBuildPatches;


        //Buffer 大小需要根据 预估的最大割分情况进行分配
        private int _maxNodeBufferSize = 200;
        private int _tempNodeBufferSize = 50;

        public TerrainBuilder(TerrainAsset asset) {
            _asset = asset;
            _computeShader = asset.computeShader;
            _commandBuffer.name = "TerrainBuild";
            _culledPatchBuffer = new ComputeBuffer(_maxNodeBufferSize * 64, PatchStripSize, ComputeBufferType.Append);

            _patchIndirectArgs = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
            _patchIndirectArgs.SetData(new uint[] { TerrainAsset.patchMesh.GetIndexCount(0), 0, 0, 0, 0 });

            _patchBoundsIndirectArgs = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
            _patchBoundsIndirectArgs.SetData(new uint[] { TerrainAsset.unitCubeMesh.GetIndexCount(0), 0, 0, 0, 0 });

            _maxLODNodeList = new ComputeBuffer(TerrainAsset.MAX_LOD_NODE_COUNT * TerrainAsset.MAX_LOD_NODE_COUNT, 8, ComputeBufferType.Append);
            this.InitMaxLODNodeListDatas();

            _nodeListA = new ComputeBuffer(_tempNodeBufferSize, 8, ComputeBufferType.Append);
            _nodeListB = new ComputeBuffer(_tempNodeBufferSize, 8, ComputeBufferType.Append);

            _indirectArgsBuffer = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
            _indirectArgsBuffer.SetData(new uint[] { 1, 1, 1 });
            _finalNodeListBuffer = new ComputeBuffer(_maxNodeBufferSize, 12, ComputeBufferType.Append);
            _nodeDescriptors = new ComputeBuffer((int)(TerrainAsset.MAX_NODE_ID + 1), 4);

            _patchBoundsBuffer = new ComputeBuffer(_maxNodeBufferSize * 64, 4 * 10, ComputeBufferType.Append);

            _lodMap = TextureUtility.CreateLODMap(160);

            if (SystemInfo.usesReversedZBuffer)
            {
                _computeShader.EnableKeyword("_REVERSE_Z");
            }else {
                _computeShader.DisableKeyword("_REVERSE_Z");
            }

            this.InitKernels();
            this.InitWorldParams();


            this.boundsHeightRedundance = 5;
            this.hizDepthBias = 1;
        }

        void InitWorldParams() {
            float wSize = _asset.worldSize.x;
            int nodeCount = TerrainAsset.MAX_LOD_NODE_COUNT;
            Vector4[] worldLODParams = new Vector4[TerrainAsset.MAX_LOD + 1];
            for (var lod = TerrainAsset.MAX_LOD; lod >= 0; --lod) {
                //lod node 大小
                var nodeSize = wSize / nodeCount;
                var patchExtent = nodeSize / 16;
                //sector是8*8的patch， LOD0 一个node是一个sector
                var setctorCountPerNode = (int)Mathf.Pow(2, lod);
                worldLODParams[lod] = new Vector4(nodeSize, patchExtent, nodeCount, setctorCountPerNode);
                //lod node 数量  lod5 => 5     lod4 => 10
                nodeCount *= 2;
            }
            _computeShader.SetVectorArray(ShaderConstants.WorldLodParams, worldLODParams);

            int[] nodeIDOffsetLOD = new int[(TerrainAsset.MAX_LOD + 1) * 4];
            int nodeIdOffset = 0;
            for (int lod = TerrainAsset.MAX_LOD; lod >= 0; lod--)
            {
                //为什么*4？
                nodeIDOffsetLOD[lod * 4] = nodeIdOffset;
                nodeIdOffset += (int)(worldLODParams[lod].z * worldLODParams[lod].z);
            }
            //_computeShader.SetInts("NodeIDOffsetOfLOD", nodeIDOffsetLOD);
        }

        void InitKernels() {
            _kernelOfTraverseQuadTree = _computeShader.FindKernel("TraverseQuadTree");
            _kernelOfBuildLodMap = _computeShader.FindKernel("BuildLodMap");
            _kernelOfBuildPatches = _computeShader.FindKernel("BuildPatches");
            this.BindComputeShader(_kernelOfTraverseQuadTree);
            this.BindComputeShader(_kernelOfBuildLodMap);
            this.BindComputeShader(_kernelOfBuildPatches);
        }

        private void BindComputeShader(int kernelIndex) {
            _computeShader.SetTexture(kernelIndex, "QuadTreeTexture", _asset.quadTreeMap);
            if (kernelIndex == _kernelOfTraverseQuadTree)
            {
                _computeShader.SetBuffer(kernelIndex, ShaderConstants.AppendFinalNodeList, _finalNodeListBuffer);
                _computeShader.SetTexture(kernelIndex, "MinMaxHeightTexture", _asset.minMaxHeightMap);
                _computeShader.SetBuffer(kernelIndex, ShaderConstants.NodeDescriptors, _nodeDescriptors);
            }
            else if (kernelIndex == _kernelOfBuildLodMap)
            {
                _computeShader.SetTexture(kernelIndex, ShaderConstants.LodMap, _lodMap);
                _computeShader.SetBuffer(kernelIndex, ShaderConstants.NodeDescriptors, _nodeDescriptors);
            }
            else if (kernelIndex == _kernelOfBuildPatches)
            {
                _computeShader.SetTexture(kernelIndex, ShaderConstants.LodMap, _lodMap);
                _computeShader.SetTexture(kernelIndex, "MinMaxHeightTexture", _asset.minMaxHeightMap);
                _computeShader.SetBuffer(kernelIndex, ShaderConstants.FinalNodeList, _finalNodeListBuffer);
                _computeShader.SetBuffer(kernelIndex, "CulledPatchList", _culledPatchBuffer);
                _computeShader.SetBuffer(kernelIndex, "PatchBoundsList", _patchBoundsBuffer);
            }
        }

        private void InitMaxLODNodeListDatas() {
            //生成最大的层级节点的数据 LOD5
            var maxLodNodeCount = TerrainAsset.MAX_LOD_NODE_COUNT;
            uint2[] datas = new uint2[maxLodNodeCount * maxLodNodeCount];
            var index = 0;
            for (uint i = 0; i < maxLodNodeCount; ++i) {
                for (uint j = 0; j < maxLodNodeCount; ++j) {
                    datas[index] = new uint2(i, j);
                    index++;
                }
            }
            _maxLODNodeList.SetData(datas);
        }


        public void Dispose()
        {
            _culledPatchBuffer.Dispose();
            _patchIndirectArgs.Dispose();
            _finalNodeListBuffer.Dispose();
            _maxLODNodeList.Dispose();
            _nodeListA.Dispose();
            _nodeListB.Dispose();
            _indirectArgsBuffer.Dispose();
            _patchBoundsBuffer.Dispose();
            _patchBoundsIndirectArgs.Dispose();
            _nodeDescriptors.Dispose();
        }


        public ComputeBuffer patchIndirectArgs
        {
            get
            {
                return _patchIndirectArgs;
            }
        }

        public ComputeBuffer culledPatchBuffer
        {
            get
            {
                return _culledPatchBuffer;
            }
        }

        public ComputeBuffer nodeIDList
        {
            get
            {
                return _finalNodeListBuffer;
            }
        }
        public ComputeBuffer patchBoundsBuffer
        {
            get
            {
                return _patchBoundsBuffer;
            }
        }

        public ComputeBuffer boundsIndirectArgs
        {
            get
            {
                return _patchBoundsIndirectArgs;
            }
        }


        private void ClearBufferCounter()
        {
            _commandBuffer.SetBufferCounterValue(_maxLODNodeList, (uint)_maxLODNodeList.count);
            _commandBuffer.SetBufferCounterValue(_nodeListA, 0);
            _commandBuffer.SetBufferCounterValue(_nodeListB, 0);
            _commandBuffer.SetBufferCounterValue(_finalNodeListBuffer, 0);
            _commandBuffer.SetBufferCounterValue(_culledPatchBuffer, 0);
            _commandBuffer.SetBufferCounterValue(_patchBoundsBuffer, 0);
        }

        void UpdateCameraFrustumPlanes(Camera camera) {
            GeometryUtility.CalculateFrustumPlanes(camera, _cameraFrustumPlanes);
            for (var i = 0; i < _cameraFrustumPlanes.Length; ++i) {
                Vector4 v4 = (Vector4)_cameraFrustumPlanes[i].normal;
                v4.w = _cameraFrustumPlanes[i].distance;
                _cameraFrustumPlanesV4[i] = v4;
            }
            _computeShader.SetVectorArray(ShaderConstants.CameraFrustumPlanes, _cameraFrustumPlanesV4);
        }

        public void Dispatch() {
            var camera = Camera.main;

            //clear
            _commandBuffer.Clear();
            this.ClearBufferCounter();

            this.UpdateCameraFrustumPlanes(camera);

            //传入computeshaer一些数据
            if (_isNodeEvaluationCDirty) {
                _isNodeEvaluationCDirty = false;
                _commandBuffer.SetComputeVectorParam(_computeShader, ShaderConstants.NodeEvaluationC, _nodeEvaluationC);
            }

            _commandBuffer.SetComputeVectorParam(_computeShader, ShaderConstants.CameraPositionWS, camera.transform.position);
            _commandBuffer.SetComputeVectorParam(_computeShader, ShaderConstants.WorldSize, _asset.worldSize);

            //四叉树分割计算得到初步的patch列表
            _commandBuffer.CopyCounterValue(_maxLODNodeList, _indirectArgsBuffer, 0);


            //一个消费list，一个添加list
            //输入消费list，判断里面的数据是否需要被划分，如果需要被划分，则将子节点数据添加到添加list中
            //互换两个list数据，继续向更高层级迭代
            ComputeBuffer consumeNodeList = _nodeListA;
            ComputeBuffer appendNodeList = _nodeListB;
            for (var lod = TerrainAsset.MAX_LOD; lod >= 0; --lod) {
                _commandBuffer.SetComputeIntParam(_computeShader, ShaderConstants.PassLOD, lod);
                if (lod == TerrainAsset.MAX_LOD) {
                    _commandBuffer.SetComputeBufferParam(_computeShader, _kernelOfTraverseQuadTree, ShaderConstants.ConsumeNodeList, _maxLODNodeList);
                } else {
                    _commandBuffer.SetComputeBufferParam(_computeShader, _kernelOfTraverseQuadTree, ShaderConstants.ConsumeNodeList, consumeNodeList);
                }

                _commandBuffer.SetComputeBufferParam(_computeShader, _kernelOfTraverseQuadTree, ShaderConstants.AppendNodeList, appendNodeList);
                _commandBuffer.DispatchCompute(_computeShader, _kernelOfTraverseQuadTree, _indirectArgsBuffer, 0);
                _commandBuffer.CopyCounterValue(appendNodeList, _indirectArgsBuffer, 0);


                var temp = consumeNodeList;
                consumeNodeList = appendNodeList;
                appendNodeList = temp;
            }

            //生成LOD Map
            _commandBuffer.DispatchCompute(_computeShader, _kernelOfBuildLodMap, 20, 20, 1);


            //生成patch
            _commandBuffer.CopyCounterValue(_finalNodeListBuffer, _indirectArgsBuffer, 0);
            _commandBuffer.DispatchCompute(_computeShader, _kernelOfBuildPatches, _indirectArgsBuffer, 0);
            //一定要将对应的参数拷贝一下
            _commandBuffer.CopyCounterValue(_culledPatchBuffer, _patchIndirectArgs, 4);


            if (isBoundsBufferOn) {
                _commandBuffer.CopyCounterValue(_patchBoundsBuffer, _patchBoundsIndirectArgs, 4);
            }

            Graphics.ExecuteCommandBuffer(_commandBuffer);
        }

        public int boundsHeightRedundance
        {
            set
            {
                _computeShader.SetInt("_BoundsHeightRedundance", value);
            }
        }

        public float nodeEvalDistance
        {
            set
            {
                _nodeEvaluationC.x = value;
                _isNodeEvaluationCDirty = true;
            }
        }

        public bool enableSeamDebug
        {
            set
            {
                if (value)
                {
                    _computeShader.EnableKeyword("ENABLE_SEAM");
                }
                else
                {
                    _computeShader.DisableKeyword("ENABLE_SEAM");
                }
            }
        }

        public float hizDepthBias
        {
            set
            {
                _hizDepthBias = value;
                _computeShader.SetFloat("_HizDepthBias", Mathf.Clamp(value, 0.01f, 1000f));
            }
            get
            {
                return _hizDepthBias;
            }
        }


        public bool isFrustumCullEnabled{
            set {
                if (value) {
                    _computeShader.EnableKeyword("ENABLE_FRUS_CULL");
                } else {
                    _computeShader.DisableKeyword("ENABLE_FRUS_CULL");
                }
            }
        }

        public bool isHizOcclusionCullingEnabled {
            set {
                if (value)
                {
                    _computeShader.EnableKeyword("ENABLE_HIZ_CULL");
                } else {
                    _computeShader.DisableKeyword("ENABLE_HIZ_CULL");
                }
            }
        }

        private bool _isBoundsBufferOn;

        public bool isBoundsBufferOn{
            set {
                if (value) {
                    _computeShader.EnableKeyword("BOUNDS_DEBUG");
                } else {
                    _computeShader.DisableKeyword("BOUNDS_DEBUG");
                }
                _isBoundsBufferOn = value;
            }
            get {
                return _isBoundsBufferOn;
            }
        }

        private class ShaderConstants
        {

            public static readonly int WorldSize = Shader.PropertyToID("_WorldSize");
            public static readonly int CameraPositionWS = Shader.PropertyToID("_CameraPositionWS");
            public static readonly int CameraFrustumPlanes = Shader.PropertyToID("_CameraFrustumPlanes");
            public static readonly int PassLOD = Shader.PropertyToID("PassLOD");
            public static readonly int AppendFinalNodeList = Shader.PropertyToID("AppendFinalNodeList");
            public static readonly int FinalNodeList = Shader.PropertyToID("FinalNodeList");

            public static readonly int AppendNodeList = Shader.PropertyToID("AppendNodeList");
            public static readonly int ConsumeNodeList = Shader.PropertyToID("ConsumeNodeList");
            public static readonly int NodeEvaluationC = Shader.PropertyToID("_NodeEvaluationC");
            public static readonly int WorldLodParams = Shader.PropertyToID("WorldLodParams");

            public static readonly int NodeDescriptors = Shader.PropertyToID("NodeDescriptors");

            public static readonly int LodMap = Shader.PropertyToID("_LodMap");
        }
    }

}
