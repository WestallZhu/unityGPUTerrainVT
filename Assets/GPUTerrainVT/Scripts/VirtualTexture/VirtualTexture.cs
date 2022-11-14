using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using UnityEngine.Rendering.Universal;

namespace RVT
{

    public unsafe class VirtualTexture : MonoBehaviour
    {
        public int pageSize = 4096;
        public int oneTerrainSize = 1024;
        public int tileSize { get { return TileSizePadding - paddingSize * 2; } }
        public int paddingSize = 2;
        public float mipmapBias = 0;

        public int tileNum = 16;

        public int NumMip { get { return (int)math.log2(pageSize) + 1; } }
        public int TileSizePadding = 256;
        
        public RenderTexture pageTableTexture { get { return m_pageTableTexture; } }

        public Rect rect;

        internal TileLruCache* lruCache;
        internal RenderTargetIdentifier tableTextureID;
        internal RenderTargetIdentifier[] colorTextureIDs;
        internal int TextureSize { get { return tileNum * TileSizePadding; } }

        private RenderTexture m_physicsTextureA;
        private RenderTexture m_physicsTextureB;
        private RenderTexture m_pageTableTexture;

        internal PageProducer pageProducer;
        internal PageRenderer pageRenderer;


        internal static VirtualTexture s_VirtualTexture;

        public RenderTexture physicsDiffuse { get { return m_physicsTextureA; } }
        public RenderTexture physicsNormal {  get { return m_physicsTextureB; } }

        private void OnEnable()
        {
            s_VirtualTexture = this;

            InitializeVTAsset();

            pageRenderer = new PageRenderer(pageSize, NumMip);
            pageProducer = new PageProducer(tileNum, pageSize, NumMip);

  
            var position = transform.position;

            rect = new Rect(position.x, position.z, pageSize, pageSize);
            Shader.SetGlobalVector("_VTRect", new Vector4(rect.x, rect.y, rect.width, rect.height));
            Shader.EnableKeyword("_TERRAIN_VIRTUAL_TEXTURE");
        }

        private void OnDisable()
        {
            pageProducer.Dispose();
            pageRenderer.Dispose();
            DisposeVTAsset();
            Shader.DisableKeyword("_TERRAIN_VIRTUAL_TEXTURE");
        }


        public void InitializeVTAsset()
        {
            lruCache = (TileLruCache*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(TileLruCache)) * 1, 64, Allocator.Persistent);
            TileLruCache.BuildLruCache(ref lruCache[0], tileNum * tileNum);

            RenderTextureFormat format = RenderTextureFormat.ARGB32;

            RenderTextureDescriptor textureDesctiptor = new RenderTextureDescriptor { width = TextureSize, height = TextureSize, volumeDepth = 1, dimension = TextureDimension.Tex2D, colorFormat = format, depthBufferBits = 0, mipCount = -1, useMipMap = false, autoGenerateMips = false, bindMS = false, msaaSamples = 1 };

            //physics texture
            m_physicsTextureA = new RenderTexture(textureDesctiptor);
            m_physicsTextureA.bindTextureMS = false;
            m_physicsTextureA.name = "PhysicsTextureA";
            m_physicsTextureA.wrapMode = TextureWrapMode.Clamp;
            m_physicsTextureA.filterMode = FilterMode.Trilinear;
            m_physicsTextureA.anisoLevel = 16;
            m_physicsTextureA.Create();

            m_physicsTextureB = new RenderTexture(textureDesctiptor);
            m_physicsTextureB.bindTextureMS = false;
            m_physicsTextureB.name = "PhysicsTextureB";
            m_physicsTextureB.wrapMode = TextureWrapMode.Clamp;
            m_physicsTextureB.filterMode = FilterMode.Trilinear;
            m_physicsTextureB.anisoLevel = 16;
            m_physicsTextureB.Create();

            colorTextureIDs = new RenderTargetIdentifier[2];
            colorTextureIDs[0] = new RenderTargetIdentifier(m_physicsTextureA);
            colorTextureIDs[1] = new RenderTargetIdentifier(m_physicsTextureB);

            m_pageTableTexture = new RenderTexture(pageSize, pageSize, 0, GraphicsFormat.R8G8B8A8_UNorm);
            m_pageTableTexture.bindTextureMS = false;
            m_pageTableTexture.name = "PageTableTexture";
            m_pageTableTexture.filterMode = FilterMode.Point;
            m_pageTableTexture.wrapMode = TextureWrapMode.Clamp;

            tableTextureID = new RenderTargetIdentifier(m_pageTableTexture);

            // 设置Shader参数
            // x: padding 偏移量
            // y: tile 有效区域的尺寸
            // zw: 1/区域尺寸
            Shader.SetGlobalTexture("_PhyscisAlbedo", m_physicsTextureA);
            Shader.SetGlobalTexture("_PhyscisNormal", m_physicsTextureB);
            Shader.SetGlobalTexture("_PageTableTexture", m_pageTableTexture);
            Shader.SetGlobalVector("_VTPageParams", new Vector4(pageSize, 1 / pageSize, NumMip - 1, 0));
            Shader.SetGlobalVector("_VTPageTileParams", new Vector4((float)paddingSize, (float)tileSize, TextureSize, TextureSize));
        }

        public void DisposeVTAsset()
        {
            lruCache[0].Dispose();
            UnsafeUtility.Free((void*)lruCache, Allocator.Persistent);

            m_physicsTextureA.Release();
            m_physicsTextureB.Release();
            m_pageTableTexture.Release();
            Object.Destroy(m_physicsTextureA);
            Object.Destroy(m_physicsTextureB);
            Object.Destroy(m_pageTableTexture);
        }

    }

    internal struct NodeInfo
    {
        public int id;
        public int nextID;
        public int prevID;
    }

    internal unsafe struct TileLruCache : System.IDisposable
    {
        internal int length;
        internal NodeInfo headNodeInfo;
        internal NodeInfo tailNodeInfo;
        [NativeDisableUnsafePtrRestriction]
        internal NodeInfo* nodeInfoList;
        internal int First { get { return headNodeInfo.id; } }

        public TileLruCache(in int length)
        {
            this.length = length;
            this.nodeInfoList = (NodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(NodeInfo)) * length, 64, Allocator.Persistent);

            for (int i = 0; i < length; ++i)
            {
                nodeInfoList[i].id = i;
                //nodeInfoList[i] = new NodeInfo()
                //{
                //    id = i,
                //};
            }
            for (int j = 0; j < length; ++j)
            {
                nodeInfoList[j].prevID = (j != 0) ? nodeInfoList[j - 1].id : 0;
                nodeInfoList[j].nextID = (j + 1 < length) ? nodeInfoList[j + 1].id : length - 1;
            }
            this.headNodeInfo = nodeInfoList[0];
            this.tailNodeInfo = nodeInfoList[length - 1];
        }

        public static void BuildLruCache(ref TileLruCache lruCache, in int count)
        {
            lruCache.length = count;
            lruCache.nodeInfoList = (NodeInfo*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(NodeInfo)) * count, 64, Allocator.Persistent);

            for (int i = 0; i < count; ++i)
            {
                lruCache.nodeInfoList[i].id = i;
                //lruCache.nodeInfoList[i] = new NodeInfo()
                //{
                //    id = i,
                //};
            }
            for (int j = 0; j < count; ++j)
            {
                lruCache.nodeInfoList[j].prevID = (j != 0) ? lruCache.nodeInfoList[j - 1].id : 0;
                lruCache.nodeInfoList[j].nextID = (j + 1 < count) ? lruCache.nodeInfoList[j + 1].id : count - 1;
            }
            lruCache.headNodeInfo = lruCache.nodeInfoList[0];
            lruCache.tailNodeInfo = lruCache.nodeInfoList[count - 1];
        }

        public void Dispose()
        {
            UnsafeUtility.Free((void*)nodeInfoList, Allocator.Persistent);
        }

        public bool SetActive(in int id)
        {
            if (id < 0 || id >= length) { return false; }

            ref NodeInfo nodeInfo = ref nodeInfoList[id];
            if (nodeInfo.id == tailNodeInfo.id) { return true; }

            Remove(ref nodeInfo);
            AddLast(ref nodeInfo);
            return true;
        }

        private void AddLast(ref NodeInfo nodeInfo)
        {
            ref NodeInfo lastNodeInfo = ref nodeInfoList[tailNodeInfo.id];
            tailNodeInfo = nodeInfo;

            lastNodeInfo.nextID = nodeInfo.id;
            nodeInfoList[lastNodeInfo.nextID] = nodeInfo;

            nodeInfo.prevID = lastNodeInfo.id;
            nodeInfoList[nodeInfo.prevID] = lastNodeInfo;
        }

        private void Remove(ref NodeInfo nodeInfo)
        {
            if (headNodeInfo.id == nodeInfo.id)
            {
                headNodeInfo = nodeInfoList[nodeInfo.nextID];
            }
            else
            {
                ref NodeInfo prevNodeInfo = ref nodeInfoList[nodeInfo.prevID];
                ref NodeInfo nextNodeInfo = ref nodeInfoList[nodeInfo.nextID];
                prevNodeInfo.nextID = nodeInfo.nextID;
                nextNodeInfo.prevID = nodeInfo.prevID;
                nodeInfoList[prevNodeInfo.nextID] = nextNodeInfo;
                nodeInfoList[nextNodeInfo.prevID] = prevNodeInfo;
            }
        }
    }

    internal struct Page
    {
        public bool isNull;
        public int mipLevel;
        public RectInt rect;
        public PagePayload payload;

        public Page(int x, int y, int width, int height, int mipLevel, bool isNull = false)
        {
            this.rect = new RectInt(x, y, width, height);
            this.isNull = isNull;
            this.mipLevel = mipLevel;
            this.payload = new PagePayload();
            this.payload.pageCoord = new int2(-1, -1);
            this.payload.notLoading = true;
        }

        public bool Equals(in Page Target)
        {
            return rect.Equals(Target.rect) && payload.Equals(Target.payload) && mipLevel.Equals(Target.mipLevel) && isNull.Equals(Target.isNull);
        }

    }

    internal struct PagePayload
    {
        internal int activeFrame;
        internal bool notLoading;
        internal int2 pageCoord;
        private static readonly int2 s_InvalidTileIndex = new int2(-1, -1);
        internal bool isReady { get { return (!pageCoord.Equals(s_InvalidTileIndex)); } }


        public void ResetTileIndex()
        {
            pageCoord = s_InvalidTileIndex;
        }

        public bool Equals(in PagePayload target)
        {
            return isReady.Equals(target.isReady) && activeFrame.Equals(target.activeFrame) && pageCoord.Equals(target.pageCoord) && notLoading.Equals(target.notLoading);
        }

    }

    internal struct PageLoadInfo : System.IComparable<PageLoadInfo>
    {
        internal int x;
        internal int y;
        internal int mipLevel;

        public PageLoadInfo(in int x, in int y, in int mipLevel)
        {
            this.x = x;
            this.y = y;
            this.mipLevel = mipLevel;
        }

        public bool Equals(in PageLoadInfo target)
        {
            return target.x == x && target.y == y && target.mipLevel == mipLevel;
        }

        public bool NotEquals(PageLoadInfo target)
        {
            return target.x != x || target.y != y || target.mipLevel != mipLevel;
        }

        public override bool Equals(object target)
        {
            return Equals((PageLoadInfo)target);
        }

        public override int GetHashCode()
        {
            return x.GetHashCode() + y.GetHashCode() + mipLevel.GetHashCode();
        }

        public int CompareTo(PageLoadInfo target)
        {
            return mipLevel.CompareTo(target.mipLevel);
        }
    }

    internal unsafe struct PageTable : System.IDisposable
    {
        internal int mipLevel;
        internal int cellSize;
        internal int cellCount;
        internal int2 pageOffset;
        [NativeDisableUnsafePtrRestriction]
        internal Page* pageBuffer;

        public PageTable(in int mipLevel, in int tableSize)
        {
            this.mipLevel = mipLevel;
            this.cellSize = (int)math.pow(2, mipLevel);
            this.cellCount = tableSize / cellSize;
            this.pageBuffer = (Page*)UnsafeUtility.Malloc(Marshal.SizeOf(typeof(Page)) * (cellCount * cellCount), 64, Allocator.Persistent);
            this.pageOffset = int2.zero;

            for (int i = 0; i < cellCount; ++i)
            {
                for (int j = 0; j < cellCount; ++j)
                {
                    this.pageBuffer[i * cellCount + j] = new Page(i * cellSize, j * cellSize, cellSize, cellSize, mipLevel);
                }
            }
        }

        public ref Page GetPage(in int x, in int y)
        {
            int2 uv = new int2((x / cellSize) % cellCount, (y / cellSize) % cellCount);
            return ref pageBuffer[uv.x * cellCount + uv.y];
        }

        public void Dispose()
        {
            UnsafeUtility.Free((void*)pageBuffer, Allocator.Persistent);
        }
    }


    internal unsafe class PageProducer : IDisposable
    {
        public NativeArray<PageTable> pageTables;
        public NativeHashMap<int2, int3> activePageMap;

        public PageProducer(in int numTile, in int pageSize, in int maxMipLevel)
        {
            pageTables = new NativeArray<PageTable>(maxMipLevel, Allocator.Persistent);
            activePageMap = new NativeHashMap<int2, int3>(numTile * numTile, Allocator.Persistent);

            for (int i = 0; i < maxMipLevel; ++i)
            {
                pageTables[i] = new PageTable(i, pageSize);
            }
        }

        public void ProcessFeedback(ref NativeArray<int4> readbackDatas, in int maxMip, in int tileNum, in int pageSize, TileLruCache* lruCache, ref NativeList<PageLoadInfo> loadRequests)
        {
            AnalysisFeedbackJob analysisFeedbackJob;
            analysisFeedbackJob.maxMip = maxMip - 1;
            analysisFeedbackJob.tileNum = tileNum;
            analysisFeedbackJob.pageSize = pageSize;
            analysisFeedbackJob.lruCache = lruCache;
            analysisFeedbackJob.pageTables = pageTables;
            analysisFeedbackJob.loadRequests = loadRequests;
            analysisFeedbackJob.frameCount = Time.frameCount;
            analysisFeedbackJob.readbackDatas = readbackDatas;
            analysisFeedbackJob.Run();
        }

        public void Reset()
        {
            for (int i = 0; i < pageTables.Length; ++i)
            {
                PageTable pageTable = pageTables[i];

                for (int j = 0; j < pageTable.cellCount; ++j)
                {
                    for (int k = 0; k < pageTable.cellCount; k++)
                    {
                        ref Page page = ref pageTable.pageBuffer[j * pageTable.cellCount + k];
                        InvalidatePage(page.payload.pageCoord);
                    }
                }
            }
            activePageMap.Clear();
        }

        public void InvalidatePage(in int2 id)
        {
            if (!activePageMap.TryGetValue(id, out int3 index))
                return;

            PageTable pageTable = pageTables[index.z];
            ref Page page = ref pageTable.GetPage(index.x, index.y);

            page.payload.ResetTileIndex();
            activePageMap.Remove(id);
        }

        public void Dispose()
        {
            for (int i = 0; i < pageTables.Length; ++i)
            {
                PageTable pageTable = pageTables[i];
                pageTable.Dispose();
            }
            pageTables.Dispose();
            activePageMap.Dispose();
        }
    }


    [BurstCompile]
    internal unsafe struct AnalysisFeedbackJob : IJob
    {
        internal int maxMip;

        internal int pageSize;

        internal int tileNum;

        internal int frameCount;

        [NativeDisableUnsafePtrRestriction]
        internal TileLruCache* lruCache;

        [ReadOnly]
        internal NativeArray<int4> readbackDatas;

        [ReadOnly]
        internal NativeArray<PageTable> pageTables;

        internal NativeList<PageLoadInfo> loadRequests;

        public void Execute()
        {
            int4 prevValue = -1;

            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                int4 readbackData = readbackDatas[i];

                if (readbackData.Equals(prevValue)) //skip same page
                    continue;

                prevValue = readbackData;

                if (readbackData.z > maxMip || readbackData.z < 0 || readbackData.x < 0 || readbackData.y < 0 || readbackData.x >= pageSize || readbackData.y >= pageSize)
                    continue;

                ref Page page = ref pageTables[readbackData.z].GetPage(readbackData.x, readbackData.y);

                if (page.isNull)
                    continue;

                if (!page.payload.isReady && page.payload.notLoading)
                {
                    page.payload.notLoading = false;
                    loadRequests.AddNoResize(new PageLoadInfo(readbackData.x, readbackData.y, readbackData.z));
                }

                if (page.payload.isReady && page.payload.activeFrame != frameCount)
                {
                    page.payload.activeFrame = frameCount;
                    lruCache[0].SetActive(page.payload.pageCoord.y * tileNum + page.payload.pageCoord.x);
                }
            }
        }
    }


    internal struct PageTableInfo
    {
        public float4 pageData;
        public float4x4 matrix_M;
    }

    internal struct PageDrawInfo : IComparable<PageDrawInfo>
    {
        public int mip;
        public Rect rect;
        public float2 drawPos;

        public int CompareTo(PageDrawInfo target)
        {
            return -(mip.CompareTo(target.mip));
        }
    }



    internal static class PageShaderID
    {
        public static int PageTableBuffer = Shader.PropertyToID("_PageTableBuffer");
        public static int SplatTileOffset = Shader.PropertyToID("_SplatTileOffset");
        public static int TerrainIndex = Shader.PropertyToID("_TerrainIndex");
    }

    internal class PageRenderer : IDisposable
    {
        private int m_Limit;
        private int m_PageSize;
        private Mesh m_DrawPageMesh;
        private Material m_DrawPageMaterial;
        private ComputeBuffer m_PageTableBuffer;
        private MaterialPropertyBlock m_Property;
        private NativeList<PageDrawInfo> m_DrawInfos;
        internal NativeList<PageLoadInfo> loadRequests;

        public PageRenderer(in int pageSize, in int limit = 8)
        {
            this.m_Limit = limit;
            this.m_PageSize = pageSize;
            this.m_Property = new MaterialPropertyBlock();
            this.m_DrawInfos = new NativeList<PageDrawInfo>(256, Allocator.Persistent);
            this.loadRequests = new NativeList<PageLoadInfo>(4096 * 2, Allocator.Persistent);
            this.m_PageTableBuffer = new ComputeBuffer(pageSize, Marshal.SizeOf(typeof(PageTableInfo)));
            this.m_DrawPageMesh = BuildQuadMesh();
            this.m_DrawPageMaterial = new Material(Shader.Find("VirtualTexture/DrawPageTable"));
            this.m_DrawPageMaterial.enableInstancing = true;
        }

        public void DrawPageTable(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, PageProducer pageProducer)
        {
            m_Property.Clear();
            m_DrawInfos.Clear();

            //Build PageDrawInfo
            PageDrawInfoBuildJob pageDrawInfoBuildJob;
            pageDrawInfoBuildJob.pageSize = m_PageSize;
            pageDrawInfoBuildJob.frameTime = Time.frameCount;
            pageDrawInfoBuildJob.drawInfos = m_DrawInfos;
            pageDrawInfoBuildJob.pageTables = pageProducer.pageTables;
            pageDrawInfoBuildJob.pageEnumerator = pageProducer.activePageMap.GetEnumerator();
            pageDrawInfoBuildJob.Run();

            //Sort PageDrawInfo
            if (m_DrawInfos.Length == 0) { return; }
            PageDrawInfoSortJob pageDrawInfoSortJob;
            pageDrawInfoSortJob.drawInfos = m_DrawInfos;
            pageDrawInfoSortJob.Run();

            //Get NativeData
            NativeArray<PageTableInfo> pageTableInfos = new NativeArray<PageTableInfo>(m_DrawInfos.Length, Allocator.TempJob);

            //Build PageTableInfo
            PageTableInfoBuildJob pageTableInfoBuildJob;
            pageTableInfoBuildJob.pageSize = m_PageSize;
            pageTableInfoBuildJob.drawInfos = m_DrawInfos;
            pageTableInfoBuildJob.pageTableInfos = pageTableInfos;
            pageTableInfoBuildJob.Run(m_DrawInfos.Length);

            //Draw PageTable
            m_Property.Clear();
            m_Property.SetBuffer(PageShaderID.PageTableBuffer, m_PageTableBuffer);
            m_PageTableBuffer.SetData<PageTableInfo>(pageTableInfos, 0, 0, pageTableInfos.Length);
            cmdBuffer.DrawMeshInstancedProcedural(m_DrawPageMesh, 0, m_DrawPageMaterial, 0, pageTableInfos.Length, m_Property);

            //Release NativeData
            pageTableInfos.Dispose();
        }

        public void DrawPageColor(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, PageProducer pageProducer, VirtualTexture virtualTexture, ref TileLruCache lruCache)
        {
            if (loadRequests.Length <= 0) { return; }
            PageRequestInfoSortJob pageRequestInfoSortJob;
            pageRequestInfoSortJob.loadRequests = loadRequests;
            pageRequestInfoSortJob.Run();

            int count = m_Limit;
            while (count > 0 && loadRequests.Length > 0)
            {
                count--;
                PageLoadInfo loadRequest = loadRequests[loadRequests.Length - 1];
                loadRequests.RemoveAt(loadRequests.Length - 1);

                int3 pageUV = new int3(loadRequest.x, loadRequest.y, loadRequest.mipLevel);
                PageTable pageTable = pageProducer.pageTables[pageUV.z];
                ref Page page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.isNull == true) { continue; }
                page.payload.notLoading = true;

                int2 pageCoord = new int2(lruCache.First % virtualTexture.tileNum, lruCache.First / virtualTexture.tileNum);
                if (lruCache.SetActive(pageCoord.y * virtualTexture.tileNum + pageCoord.x))
                {
                    pageProducer.InvalidatePage(pageCoord);

                    RectInt pageRect = new RectInt(pageCoord.x * virtualTexture.TileSizePadding, pageCoord.y * virtualTexture.TileSizePadding, virtualTexture.TileSizePadding, virtualTexture.TileSizePadding);
                    RenderPage(cmdBuffer, virtualTexture, pageRect, loadRequest);
                }

                page.payload.pageCoord = pageCoord;
                pageProducer.activePageMap.Add(pageCoord, pageUV);
            }
        }

        private void RenderPage(CommandBuffer cmdBuffer, VirtualTexture virtualTexture, in RectInt pageRect, in PageLoadInfo loadRequest)
        {
            int x = loadRequest.x;
            int y = loadRequest.y;
            int perSize = (int)Mathf.Pow(2, loadRequest.mipLevel);
            x = x - x % perSize;
            y = y - y % perSize;

            var rect = virtualTexture.rect;

            var padding = (int)virtualTexture.paddingSize * perSize * (rect.width / virtualTexture.pageSize) / virtualTexture.tileSize;
            var volumeRect = new Rect(rect.x + (float)x / virtualTexture.pageSize * rect.width - padding, rect.y + (float)y / virtualTexture.pageSize * rect.height - padding, rect.width / virtualTexture.pageSize * perSize + 2f * padding, rect.width / virtualTexture.pageSize * perSize + 2f * padding);

            int oneTerrainSize = virtualTexture.oneTerrainSize;
            int nGrid = virtualTexture.pageSize / oneTerrainSize;
            int nCount = nGrid * nGrid;

            Vector3 position0 = virtualTexture.transform.position;
            for(int i=0; i< nCount; i++)
            {
                int ix = i % nGrid;
                int iz = i / nGrid;

                var terrainRect = Rect.zero;
                terrainRect.xMin = position0.x + ix * oneTerrainSize;
                terrainRect.yMin = position0.z + iz * oneTerrainSize;
                terrainRect.width = oneTerrainSize;
                terrainRect.height = oneTerrainSize;

                if (!volumeRect.Overlaps(terrainRect)) { continue; }

                var maxRect = volumeRect;
                maxRect.xMin = Mathf.Max(volumeRect.xMin, terrainRect.xMin);
                maxRect.yMin = Mathf.Max(volumeRect.yMin, terrainRect.yMin);
                maxRect.xMax = Mathf.Min(volumeRect.xMax, terrainRect.xMax);
                maxRect.yMax = Mathf.Min(volumeRect.yMax, terrainRect.yMax);

                var scaleFactor = pageRect.width / volumeRect.width;
                Rect offsetRect = new Rect(pageRect.x + (maxRect.xMin - volumeRect.xMin) * scaleFactor, pageRect.y + (maxRect.yMin - volumeRect.yMin) * scaleFactor, maxRect.width * scaleFactor, maxRect.height * scaleFactor);
                float l = offsetRect.x * 2.0f / virtualTexture.TextureSize - 1;
                float r = (offsetRect.x + offsetRect.width) * 2.0f / virtualTexture.TextureSize - 1;
                float b = offsetRect.y * 2.0f / virtualTexture.TextureSize - 1;
                float t = (offsetRect.y + offsetRect.height) * 2.0f / virtualTexture.TextureSize - 1;
                Matrix4x4 Matrix_MVP = new Matrix4x4();
                Matrix_MVP.m00 = r - l;
                Matrix_MVP.m03 = l;
                Matrix_MVP.m11 = t - b;
                Matrix_MVP.m13 = b;
                Matrix_MVP.m23 = -1;
                Matrix_MVP.m33 = 1;

                float4 scaleOffset = new float4(maxRect.width / terrainRect.width, maxRect.height / terrainRect.height, (maxRect.xMin - terrainRect.xMin) / terrainRect.width, (maxRect.yMin - terrainRect.yMin) / terrainRect.height);
                m_Property.Clear();
                m_Property.SetVector(PageShaderID.SplatTileOffset, scaleOffset);
                m_Property.SetMatrix(Shader.PropertyToID("_ImageMVP"), GL.GetGPUProjectionMatrix(Matrix_MVP, true));
                m_Property.SetInt(PageShaderID.TerrainIndex, i);
                cmdBuffer.DrawMesh(m_DrawPageMesh, Matrix4x4.identity, m_DrawPageMaterial, 0, 1, m_Property);
            }
        }

        public void Dispose()
        {
            m_DrawInfos.Dispose();
            loadRequests.Dispose();
            m_PageTableBuffer.Dispose();
            Object.DestroyImmediate(m_DrawPageMesh);
            Object.DestroyImmediate(m_DrawPageMaterial);
        }

        public static Mesh BuildQuadMesh()
        {
            List<Vector3> VertexArray = new List<Vector3>();
            List<int> IndexArray = new List<int>();
            List<Vector2> UB0Array = new List<Vector2>();

            VertexArray.Add(new Vector3(0, 1, 0.1f));
            VertexArray.Add(new Vector3(0, 0, 0.1f));
            VertexArray.Add(new Vector3(1, 0, 0.1f));
            VertexArray.Add(new Vector3(1, 1, 0.1f));

            UB0Array.Add(new Vector2(0, 1));
            UB0Array.Add(new Vector2(0, 0));
            UB0Array.Add(new Vector2(1, 0));
            UB0Array.Add(new Vector2(1, 1));

            IndexArray.Add(0);
            IndexArray.Add(1);
            IndexArray.Add(2);
            IndexArray.Add(2);
            IndexArray.Add(3);
            IndexArray.Add(0);

            Mesh mesh = new Mesh();
            mesh.SetVertices(VertexArray);
            mesh.SetUVs(0, UB0Array);
            mesh.SetTriangles(IndexArray, 0);
            return mesh;
        }

    }


    [BurstCompile]
    internal struct PageDrawInfoBuildJob : IJob
    {
        internal int pageSize;

        internal int frameTime;

        [ReadOnly]
        internal NativeArray<PageTable> pageTables;

        [WriteOnly]
        internal NativeList<PageDrawInfo> drawInfos;

        [ReadOnly]
        internal NativeHashMap<int2, int3>.Enumerator pageEnumerator;

        public void Execute()
        {
            while (pageEnumerator.MoveNext())
            {
                var pageCoord = pageEnumerator.Current.Value;
                PageTable pageTable = pageTables[pageCoord.z];
                ref Page page = ref pageTable.GetPage(pageCoord.x, pageCoord.y);
                if (page.payload.activeFrame != frameTime) { continue; }

                int2 rectXY = new int2(page.rect.xMin, page.rect.yMin);
                while (rectXY.x < 0)
                {
                    rectXY.x += pageSize;
                }
                while (rectXY.y < 0)
                {
                    rectXY.y += pageSize;
                }

                PageDrawInfo drawInfo;
                drawInfo.mip = page.mipLevel;
                drawInfo.rect = new Rect(rectXY.x, rectXY.y, page.rect.width, page.rect.height);
                drawInfo.drawPos = new float2((float)page.payload.pageCoord.x / 255, (float)page.payload.pageCoord.y / 255);
                drawInfos.Add(drawInfo);
            }
        }
    }

    [BurstCompile]
    internal struct PageDrawInfoSortJob : IJob
    {
        internal NativeList<PageDrawInfo> drawInfos;

        public void Execute()
        {
            drawInfos.Sort();
        }
    }

    [BurstCompile]
    internal struct PageTableInfoBuildJob : IJobParallelFor
    {
        internal int pageSize;

        [ReadOnly]
        internal NativeList<PageDrawInfo> drawInfos;

        [WriteOnly]
        internal NativeArray<PageTableInfo> pageTableInfos;

        public void Execute(int i)
        {
            PageTableInfo pageInfo;
            pageInfo.pageData = new float4(drawInfos[i].drawPos.x, drawInfos[i].drawPos.y, drawInfos[i].mip / 255f, 0);
            pageInfo.matrix_M = float4x4.TRS(new float3(drawInfos[i].rect.x / pageSize, drawInfos[i].rect.y / pageSize, 0), quaternion.identity, drawInfos[i].rect.width / pageSize);
            pageTableInfos[i] = pageInfo;
        }
    }

    [BurstCompile]
    internal struct PageRequestInfoSortJob : IJob
    {
        internal NativeList<PageLoadInfo> loadRequests;

        public void Execute()
        {
            loadRequests.Sort();
        }
    }

}