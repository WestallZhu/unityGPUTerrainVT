using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using UnityEngine.Rendering.Universal;



public unsafe class GPUTerrain : MonoBehaviour
{
    private float unitMeter = 1.0f;
    public Vector3 sceneOffset = Vector3.zero;

    private RenderTexture HeightArray;
    private RenderTexture WorldNormalArray;
    private Texture2DArray SplatArray;
    private Texture2DArray AlbedoArray;
    private Texture2DArray NormalArray;

    private RenderTexture SectorLodTex;
    private RenderTexture HeightRangeRT;

    private TerrainLayer[] TextureLayer;
    private Terrain[] terrainArray;
    private static Dictionary<TerrainLayer, int> TextureLayerMap;

    private float[] splatOffsetArray;
    private float[] splatCountArray;
    private float[] tileData;
    private int splatTotalCount;
    private int heightResolution = 1025;
    private int splatResolution = 512;
    private int albedoResolution = 512;
    private int minMaxHeightResolution;
    private int terrainGridCount;
    private int PerTerrainBatchCount { get { return (heightResolution - 1) / PATCH_MESH_GRID_COUNT; }  }
    public int SectorBlock { get { return (int)(SECTOR_METER / unitMeter); } }
    public float WorldSize { get { return (heightResolution - 1) * unitMeter * terrainGridCount; } }
    const int PATCH_MESH_GRID_COUNT = 16;
    const int SECTOR_METER = 64;
    const int MAX_LOD = 4;

    private Vector4 heightmapScale;

    [Range(0.1f, 1.9f)]
    public float distanceEvaluation = 1.4f;
    [Range(0.0f, 1.0f)]
    public float cosineEvaluation = 0.5f;

    private Mesh batchMesh;

    private bool initializedCS = false;
    private CommandBuffer cmdBuffer;
    public ComputeShader terrainCompute;
    public ComputeShader heightRangeCS;

    private ComputeBuffer sectorIndirectArgs;
    private ComputeBuffer patchIndirectArgs;

    private ComputeBuffer finalNodeList;
    private ComputeBuffer patchRenderList;

    private ComputeBuffer nodeStateList;


    private Plane[] frustumPlanes = new Plane[6];
    private Vector4[] planesVecs = new Vector4[6];

    private Material litMaterial;
    private MaterialPropertyBlock properties;

    public RenderTexture GetSectorLodTex() { return SectorLodTex; }


    private void OnEnable()
    {
        CollectionUnityTerrain();
        CreateTexture();

        InitializeTexture();

        batchMesh = GPUTerrainUtility.CreateBatchMesh(PATCH_MESH_GRID_COUNT, unitMeter);
        litMaterial = new Material(Shader.Find("Terrain/TerrainLit_Instance"));
        litMaterial.enableInstancing = true;


        GPUTerrainPass.s_ExecuteAction += Render;
        RVT.VirtualTextureRenderPass.s_ExecuteAction += Render;

        initializedCS = false;
        cmdBuffer = new CommandBuffer();
        cmdBuffer.name = "GPUTerrainVT";

        CreateGraphicsBuffer();
    }

    public struct RenderPatch
    {
        public float2 position;
        public uint lods;
        public uint index;
    };

    const int MAX_SECTOR_SIZE = 128;
    void CreateGraphicsBuffer()
    {
        //8x8 patches in on sector
        int batchStripSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(RenderPatch));
        patchRenderList = new ComputeBuffer(MAX_SECTOR_SIZE * 64, batchStripSize, ComputeBufferType.Structured);

        //DispatchCompute IndirectArguments
        sectorIndirectArgs = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
        sectorIndirectArgs.SetData(new uint[] { 1, 1, 1 });

        //DrawMeshInstancedIndirect IndirectArguments
        patchIndirectArgs = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
        patchIndirectArgs.SetData(new uint[] { batchMesh.GetIndexCount(0), 0, 0, 0, 0 });


        finalNodeList = new ComputeBuffer(MAX_SECTOR_SIZE, 3 * 4, ComputeBufferType.Structured);

        int lodNodeCount = terrainGridCount;
        int nodeCount = 0;
        for (int lod = MAX_LOD; lod >=0; lod--)
        {

            nodeCount += lodNodeCount * lodNodeCount;
            lodNodeCount *= 2;

        }

        nodeStateList = new ComputeBuffer(nodeCount, 4, ComputeBufferType.IndirectArguments);

    }
    const float kMaxHeight = 32766.0f/65535.0f;
    void CollectionUnityTerrain()
    {
        Terrain[] terrains = this.GetComponentsInChildren<Terrain>();
        int terrainCount = terrains.Length;
        System.Array.Sort<Terrain>(terrains, (a, b) => {
            Vector3 apos = a.transform.position;
            Vector3 bpos = b.transform.position;
            int zc = apos.z.CompareTo(bpos.z);
            return zc != 0 ? zc : apos.x.CompareTo(bpos.x);
        });

        terrainArray = terrains;
        terrainGridCount = (int)Mathf.Sqrt(terrainArray.Length);

        TerrainData terrainData0 = terrains[0].terrainData;
        Vector3 size = terrainData0.size;
        heightmapScale = new Vector3(terrainData0.heightmapScale.x, terrainData0.heightmapScale.y / kMaxHeight, terrainData0.heightmapScale.z);
        heightResolution = terrainData0.heightmapResolution;
        splatResolution = terrainData0.alphamapResolution;
        this.unitMeter = size.x / (heightResolution - 1);
        this.sceneOffset = terrains[0].transform.position;

        TextureLayerMap = new Dictionary<TerrainLayer, int>();

        List<TerrainLayer> layerList = new List<TerrainLayer>();
        for(int i=0; i < terrainCount; i++)
        {
            foreach(var layer in terrains[i].terrainData.terrainLayers)
            {
                if (!TextureLayerMap.ContainsKey(layer))
                {
                    layerList.Add(layer);
                    TextureLayerMap.Add(layer, layerList.Count - 1);
                }
            }
        }

        TextureLayer = layerList.ToArray();

        tileData = new float[TextureLayer.Length];
        for(int i=0; i<tileData.Length; i++)
        {
            tileData[i] = size.x / TextureLayer[i].tileSize.x;
        }

        albedoResolution = TextureLayer[0].diffuseTexture.width;

        splatTotalCount = 0;
        splatOffsetArray = new float[terrainArray.Length];
        splatCountArray = new float[terrainArray.Length];
        for (int i=0; i < terrainCount; i++)
        {
            splatCountArray[i] = terrainArray[0].terrainData.alphamapTextureCount;
            splatOffsetArray[i] = splatTotalCount;
            splatTotalCount += (int)splatCountArray[i];
        }

        for (int i=0; i< terrainCount; i++)
        {
            terrainArray[i].drawHeightmap = false;
        }

}

    void CreateTexture()
    {
        RenderTextureDescriptor HeightTextureDesc = new RenderTextureDescriptor { width = heightResolution, height = heightResolution, volumeDepth = terrainArray.Length, dimension = TextureDimension.Tex2DArray, graphicsFormat = GraphicsFormat.R16_UNorm, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
        HeightArray = RenderTexture.GetTemporary(HeightTextureDesc);
        HeightArray.name = "TerrainHeightTextureArray";

        RenderTextureDescriptor NormalTextureDesc = new RenderTextureDescriptor { width = heightResolution, height = heightResolution, volumeDepth = terrainArray.Length, dimension = TextureDimension.Tex2DArray, graphicsFormat = GraphicsFormat.A2B10G10R10_UNormPack32, depthBufferBits = 0, mipCount = 11, useMipMap = true, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
        WorldNormalArray = RenderTexture.GetTemporary(NormalTextureDesc);
        WorldNormalArray.name = "TerrainWNormalTextureArray";

        SplatArray = new Texture2DArray(splatResolution, splatResolution, terrainArray.Length, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.MipChain);
        SplatArray.name = "TerrainSplatTextureArray";

        AlbedoArray = new Texture2DArray(albedoResolution, albedoResolution, splatTotalCount, GetAlbedoFormat(), TextureCreationFlags.MipChain);
        AlbedoArray.anisoLevel = 16;
        AlbedoArray.name = "TerrainAlbedoTextureArray";


        NormalArray = new Texture2DArray(albedoResolution, albedoResolution, TextureLayer.Length, GetNormalFormat(), TextureCreationFlags.MipChain);
        NormalArray.anisoLevel = 16;
        NormalArray.name = "TerrainNormalTextureArray";

        
        //PerTerrainBatchCount = (heightResolution - 1) / PATCH_MESH_GRID_COUNT;
        int mipCount = (int)Mathf.Log(PerTerrainBatchCount, 2.0f) + 1;
        minMaxHeightResolution = PerTerrainBatchCount * terrainGridCount;
        RenderTextureDescriptor MinMaxTextureDesc = new RenderTextureDescriptor { width = minMaxHeightResolution, height = minMaxHeightResolution, volumeDepth = 1, dimension = TextureDimension.Tex2D, graphicsFormat = GraphicsFormat.R16G16_UNorm, depthBufferBits = 0, mipCount = mipCount, useMipMap = true, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };
        MinMaxTextureDesc.enableRandomWrite = true;
        HeightRangeRT = RenderTexture.GetTemporary(MinMaxTextureDesc);
        HeightRangeRT.name = "HeightRangeRT";
        HeightRangeRT.filterMode = FilterMode.Point;
        HeightRangeRT.Create();


        // Create LOD Texture
        int sectorCount = (heightResolution-1) * terrainGridCount / SectorBlock;
        RenderTextureDescriptor LodTextureDescrptor = new RenderTextureDescriptor(sectorCount, sectorCount, RenderTextureFormat.R8, 0, 0);
        LodTextureDescrptor.autoGenerateMips = false;
        LodTextureDescrptor.enableRandomWrite = true;
        LodTextureDescrptor.sRGB = false;
        SectorLodTex = RenderTexture.GetTemporary(LodTextureDescrptor);
        SectorLodTex.name = "SectorLodTexture";
        SectorLodTex.filterMode = FilterMode.Point;
        SectorLodTex.Create();


    }

    void OnValidate()
    {
        initializedCS = false;
    }


    void InitializeTexture()
    {
        for (int i = 0; i < TextureLayer.Length; i++)
        {
            Graphics.CopyTexture(TextureLayer[i].diffuseTexture, 0, AlbedoArray, i);
            Graphics.CopyTexture(TextureLayer[i].normalMapTexture, 0, NormalArray, i);
        }

        for(int i=0; i < terrainArray.Length; i++)
        {
            Graphics.CopyTexture(terrainArray[i].normalmapTexture, 0, WorldNormalArray, i);
        }

        for(int i=0; i < terrainArray.Length; i++)
        {
            for(int k=0; k < splatCountArray[i]; k++)
            {
                Graphics.CopyTexture(terrainArray[i].terrainData.alphamapTextures[k], 0, SplatArray, (int)splatOffsetArray[i] + k);
            }
        }

        for(int i=0; i < terrainArray.Length; i++)
        {
            Graphics.CopyTexture(terrainArray[i].terrainData.heightmapTexture, 0, HeightArray, i);
        }

    }


    const int ID_CSVisitNode = 0;
    const int ID_CSBuildSectorLodTex = 1;
    const int ID_CSBuildPatches = 2;
    private void BindComputeShader()
    {
        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSVisitNode, ShaderConstants.SectorIndirectArgs, sectorIndirectArgs);
        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSVisitNode, ShaderConstants.FinalNodeList, finalNodeList);
        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSVisitNode, ShaderConstants.NodeStateList, nodeStateList);
        cmdBuffer.SetComputeTextureParam(terrainCompute, ID_CSVisitNode, ShaderConstants.HeightRangeTex, HeightRangeRT);
        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSVisitNode, ShaderConstants.PatchIndirectArgs, patchIndirectArgs);
        cmdBuffer.SetComputeFloatParam(terrainCompute, ShaderConstants.TerrainHeightmapScale, heightmapScale.y);

        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSBuildSectorLodTex, ShaderConstants.NodeStateList, nodeStateList);
        cmdBuffer.SetComputeTextureParam(terrainCompute, ID_CSBuildSectorLodTex, ShaderConstants.SectorLodTex, SectorLodTex);


        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSBuildPatches, ShaderConstants.PatchRenderList, patchRenderList);
        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSBuildPatches, ShaderConstants.FinalNodeList, finalNodeList);
        cmdBuffer.SetComputeBufferParam(terrainCompute, ID_CSBuildPatches, ShaderConstants.PatchIndirectArgs, patchIndirectArgs);
        cmdBuffer.SetComputeTextureParam(terrainCompute, ID_CSBuildPatches, ShaderConstants.HeightRangeTex, HeightRangeRT);
        cmdBuffer.SetComputeTextureParam(terrainCompute, ID_CSBuildPatches, ShaderConstants.SectorLodTex, SectorLodTex);
        cmdBuffer.SetComputeVectorParam(terrainCompute, ShaderConstants.SceneOffset, sceneOffset);
        cmdBuffer.SetComputeFloatParam(terrainCompute, ShaderConstants.UnitMeter, unitMeter);
        cmdBuffer.SetComputeIntParam(terrainCompute, ShaderConstants.MaxLodNodeCount, terrainGridCount);
        cmdBuffer.SetComputeVectorParam(terrainCompute, ShaderConstants.LodEvaluationCoef, new Vector4(distanceEvaluation, cosineEvaluation, 0, 0));
    }

    private void SetMaterialBuffer()
    {
        Material material = litMaterial;
        Shader.SetGlobalFloat(ShaderConstants.TerrainSize, (heightResolution-1) * unitMeter);
        material.SetFloat(ShaderConstants.SectorSize, 64);
        material.SetTexture(ShaderConstants.HeightArray, HeightArray);
        Shader.SetGlobalTexture(ShaderConstants.SplatArray, SplatArray);
        Shader.SetGlobalTexture(ShaderConstants.AlbedoArray, AlbedoArray);
        Shader.SetGlobalTexture(ShaderConstants.NormalArray, NormalArray);
        material.SetVector(ShaderConstants.SceneOffset, sceneOffset);
        material.SetFloat(ShaderConstants.TerrainHeightmapScale, heightmapScale.y);
        material.SetBuffer(ShaderConstants.PatchRenderList, patchRenderList);
        material.SetFloat(ShaderConstants.MaxLodNodeCount, terrainGridCount);
        material.SetTexture(ShaderConstants.WorldNormalArray, WorldNormalArray);
        material.SetFloat(ShaderConstants.UnitMeter, unitMeter);

        Shader.SetGlobalFloatArray("_TileData", tileData);
        Shader.SetGlobalFloatArray("_SplatOffset", splatOffsetArray);
        Shader.SetGlobalFloatArray("_SplatCount", splatCountArray);

        properties = new MaterialPropertyBlock();
        GPUTerrainUtility.SetSHCoefficients(this.transform.position + new Vector3(0, 10, 0), properties);
     
    }

    

    private void UpdateFrustumPlanes(Camera camera)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
        for( int i=0; i< frustumPlanes.Length; i++)
        {
            Plane p = frustumPlanes[i];
            planesVecs[i] = p.normal;
            planesVecs[i].w = p.distance;
        }

        (float3 minFrustumPlanes, float3 maxFrustumPlanes) = GPUTerrainUtility.GetFrustumMinMaxPoint(camera);

        cmdBuffer.SetComputeVectorArrayParam(terrainCompute, ShaderConstants.Planes, planesVecs);

        cmdBuffer.SetComputeVectorParam(terrainCompute, ShaderConstants.FrustumMinPoint, float4(minFrustumPlanes,1.0f));
        cmdBuffer.SetComputeVectorParam(terrainCompute, ShaderConstants.FrustumMaxPoint, float4(maxFrustumPlanes, 1.0f));

    }

    const int ID_LitPASS = 0;
    const int ID_VTFeedback = 1;

    private int lastRenderFrame = 0;
    void Render(ScriptableRenderContext context, CameraData cameraData, int pass)
    {
        cmdBuffer.Clear();
        if (cameraData.cameraType != CameraType.Game || lastRenderFrame == Time.frameCount)
        {
            cmdBuffer.DrawMeshInstancedIndirect(batchMesh, 0, litMaterial, pass, patchIndirectArgs, 0, properties);
            context.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();
            return;
        }

        if (!initializedCS)
        {
            BindComputeShader();
            GeneratePatchHeightRangeTex();
            GeneratHeightRangePyramid(HeightRangeRT);

            SetMaterialBuffer();
            initializedCS = true;
        }

        Camera camera = cameraData.camera;
        cmdBuffer.SetComputeVectorParam(terrainCompute, ShaderConstants.CameraPositionWS, camera.transform.position);

        UpdateFrustumPlanes(camera);

        cmdBuffer.DispatchCompute(terrainCompute, ID_CSVisitNode, 1, 1, 1);


        int sectorGroup = SectorLodTex.width/8;
        cmdBuffer.DispatchCompute(terrainCompute, ID_CSBuildSectorLodTex, sectorGroup, sectorGroup, 1);

        cmdBuffer.DispatchCompute(terrainCompute, ID_CSBuildPatches, sectorIndirectArgs, 0 );


        cmdBuffer.DrawMeshInstancedIndirect(batchMesh, 0, litMaterial, pass, patchIndirectArgs, 0, properties);

        //uint[] args = new uint[3];
        //patchIndirectArgs.GetData(args);
        //RenderPatch[] args2 = new RenderPatch[args[1]];
        //patchRenderList.GetData(args2);


        context.ExecuteCommandBuffer(cmdBuffer);
        lastRenderFrame = Time.frameCount;
    }


    const int ID_CSHeightRange = 0;
    const int ID_CSHeightRangePyramid = 1;
    private void GeneratePatchHeightRangeTex()
    {
        cmdBuffer.SetComputeTextureParam(heightRangeCS, ID_CSHeightRange, ShaderConstants.HeightArray, HeightArray);
        cmdBuffer.SetComputeTextureParam(heightRangeCS, ID_CSHeightRange, ShaderConstants.HeightRangeTex, HeightRangeRT, 0);
        cmdBuffer.SetComputeVectorParam(heightRangeCS, ShaderConstants.ThreadGroupDim, new Vector4(PerTerrainBatchCount, PerTerrainBatchCount, terrainGridCount * terrainGridCount, 0));
        cmdBuffer.DispatchCompute(heightRangeCS, ID_CSHeightRange, PerTerrainBatchCount, PerTerrainBatchCount, terrainGridCount * terrainGridCount);
    }


    private void GeneratHeightRangePyramid(RenderTargetIdentifier pyramidMinMaxTexture)
    {
        int mipCount = HeightRangeRT.mipmapCount;
        int pyramidSize = HeightRangeRT.width;
        int[] pyramidMipIDs = new int[mipCount];
        for( int i = 1; i < mipCount; i++)
        {
            pyramidMipIDs[i] = Shader.PropertyToID("_MinMaxMip" + i);
        }

        int PrevMipHeight = Shader.PropertyToID("_PrevMipHeight");
        int HierarchicalHeight = Shader.PropertyToID("_HierarchicalHeight");
        int PyramidSize = Shader.PropertyToID("_PyramidSize");

        RenderTargetIdentifier lastPyramidTexture = pyramidMinMaxTexture;

        for (int i = 1; i < mipCount; i++)
        {
            pyramidSize /= 2;
            int dispatchSize = pyramidSize / 8;
            if (dispatchSize == 0)
                dispatchSize = 1;

            cmdBuffer.GetTemporaryRT(pyramidMipIDs[i], pyramidSize, pyramidSize, 0, FilterMode.Point, GraphicsFormat.R16G16_UNorm, 1, true);
            cmdBuffer.SetComputeIntParam(heightRangeCS, PyramidSize, pyramidSize);
            cmdBuffer.SetComputeTextureParam(heightRangeCS, ID_CSHeightRangePyramid, PrevMipHeight, lastPyramidTexture);
            cmdBuffer.SetComputeTextureParam(heightRangeCS, ID_CSHeightRangePyramid, HierarchicalHeight, pyramidMipIDs[i]);
            cmdBuffer.DispatchCompute(heightRangeCS, ID_CSHeightRangePyramid, dispatchSize, dispatchSize, 1);
            cmdBuffer.CopyTexture(pyramidMipIDs[i], 0, 0, pyramidMinMaxTexture, 0, i);
            lastPyramidTexture = pyramidMipIDs[i];
        }

        for(int i=1; i< mipCount; i++)
        {
            cmdBuffer.ReleaseTemporaryRT(pyramidMipIDs[i]);
        }
    }
   


    private GraphicsFormat GetAlbedoFormat()
    {
        return TextureLayer[0].diffuseTexture.graphicsFormat;
    }

    private GraphicsFormat GetNormalFormat()
    {
        return TextureLayer[0].normalMapTexture.graphicsFormat;
    }

    


    private void OnDisable()
    {
        patchRenderList.Release();
        finalNodeList.Release();
        sectorIndirectArgs.Release();
        patchIndirectArgs.Release();
        nodeStateList.Release();

        RenderTexture.ReleaseTemporary(HeightArray);
        RenderTexture.ReleaseTemporary(WorldNormalArray);
        RenderTexture.ReleaseTemporary(HeightRangeRT);
        RenderTexture.ReleaseTemporary(SectorLodTex);

        GameObject.DestroyImmediate(SplatArray);
        GameObject.DestroyImmediate(AlbedoArray);
        GameObject.DestroyImmediate(NormalArray);

        GPUTerrainPass.s_ExecuteAction -= Render;
        RVT.VirtualTextureRenderPass.s_ExecuteAction -= Render;

    }



    private class ShaderConstants
    {
        public static readonly int SceneOffset = Shader.PropertyToID("_SceneOffset");
        public static readonly int CameraPositionWS = Shader.PropertyToID("_CameraPositionWS");
        public static readonly int CameraFrustumPlanes = Shader.PropertyToID("_CameraFrustumPlanes");
        public static readonly int MaxLodNodeCount = Shader.PropertyToID("_MaxLodNodeCount");
        public static readonly int UnitMeter = Shader.PropertyToID("_UnitMeter");
        public static readonly int LodEvaluationCoef = Shader.PropertyToID("_LodEvaluationCoef");

        public static readonly int SectorIndirectArgs = Shader.PropertyToID("_SectorIndirectArgs");
        public static readonly int PatchIndirectArgs = Shader.PropertyToID("_PatchIndirectArgs");
        public static readonly int FinalNodeList = Shader.PropertyToID("_FinalNodeList");
        public static readonly int PatchRenderList = Shader.PropertyToID("_PatchRenderList");
        public static readonly int NodeStateList = Shader.PropertyToID("_NodeStateList");
        public static readonly int SectorLodTex = Shader.PropertyToID("_SectorLodTex");
        public static readonly int HeightRangeTex = Shader.PropertyToID("_HeightRangeTex");
        public static readonly int ThreadGroupDim = Shader.PropertyToID("_ThreadGroupDim");
        public static readonly int TerrainHeightmapScale = Shader.PropertyToID("_TerrainHeightmapScale");
        public static readonly int Planes = Shader.PropertyToID("_Planes");
        public static readonly int FrustumMinPoint = Shader.PropertyToID("_FrustumMinPoint");
        public static readonly int FrustumMaxPoint = Shader.PropertyToID("_FrustumMaxPoint");
        

        public static int TerrainSize = Shader.PropertyToID("_TerrainSize");
        public static int SectorSize = Shader.PropertyToID("_SectorSize");
        public static int SectionSize = Shader.PropertyToID("_SectionSize");
        public static int SplatArray = Shader.PropertyToID("_SplatArray");
        public static int HeightArray = Shader.PropertyToID("_HeightArray");
        public static int WorldNormalArray = Shader.PropertyToID("_WorldNormalArray");
        public static int AlbedoArray = Shader.PropertyToID("_AlbedoArray");
        public static int NormalArray = Shader.PropertyToID("_NormalArray");

    }

}
