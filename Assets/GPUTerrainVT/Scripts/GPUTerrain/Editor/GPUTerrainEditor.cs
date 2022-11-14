using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Experimental.Rendering;


[CustomEditor(typeof(GPUTerrain))]
public class GPUTerrainEditor : UnityEditor.Editor
{
    //class Node
    //{
    //    public int x;
    //    public int z;
    //    public int size;
    //    public Vector4 heights;
    //    public Bounds aabb;
    //    public Node parent;
    //    public Node[] children;
    //    public int index;
    //    public Vector3[] points;
    //    public float lodBias;

    //    static public float unitMeter = 1.0f;
    //    static public Vector3 sceneOffset;
    //    internal void Insert(int vx, int vz, Vector4 heights)
    //    {
    //        if(size <= 1)
    //        {
    //            this.heights = heights;
    //            InitBounds();
    //            return;
    //        }

    //        if(children == null)
    //        {
    //            children = new Node[4];
    //            children[0] = new Node() { x = x, z = z, size = size / 2, parent = this };
    //            children[1] = new Node() { x = x + size/2, z = z, size = size / 2, parent = this };
    //            children[2] = new Node() { x = x, z = z + size / 2, size = size / 2, parent = this };
    //            children[3] = new Node() { x = x + size / 2, z = z + size / 2, size = size / 2, parent = this };
    //        }

    //        int offset = 0;
    //        if (vx >= x + size / 2) offset++;
    //        if (vz >= z + size / 2) offset += 2;
    //        children[offset].Insert(vx, vz, heights);
    //    }

    //    void InitBounds()
    //    {
    //        points = new Vector3[] {
    //            new Vector3(x,0,z)*unitMeter + Vector3.up * heights.x + sceneOffset,
    //            new Vector3(x+size,0,z) * unitMeter + Vector3.up * heights.y + sceneOffset,
    //            new Vector3(x, 0, z + size) * unitMeter + Vector3.up * heights.z + sceneOffset,
    //            new Vector3(x+size, 0, z+size) * unitMeter + Vector3.up * heights.w + sceneOffset
    //        };

    //        aabb = new Bounds(points[0], Vector3.zero);
    //        Vector3 n1, n2;
    //        GetNormals(out n1, out n2);
    //        lodBias = 1;
    //        if(children == null)
    //        {
    //            aabb.Encapsulate(points[1]);
    //            aabb.Encapsulate(points[2]);
    //            aabb.Encapsulate(points[3]);
    //        }
    //        else
    //        {
    //            aabb.Encapsulate(children[0].aabb);
    //            aabb.Encapsulate(children[1].aabb);
    //            aabb.Encapsulate(children[2].aabb);
    //            aabb.Encapsulate(children[3].aabb);
    //            lodBias = Mathf.Min(children[0].lodBias, children[1].lodBias, children[2].lodBias, children[3].lodBias);
    //        }
    //        lodBias = Mathf.Min(lodBias, Mathf.Lerp(0, 1, Mathf.Clamp01(Vector3.Dot(n1, n2))));
    //    }

    //    public void UpdateBounds()
    //    {
    //        if (children == null) return;

    //        children[0].UpdateBounds();
    //        children[1].UpdateBounds();
    //        children[2].UpdateBounds();
    //        children[3].UpdateBounds();

    //        heights = new Vector4(children[0].heights.x, children[1].heights.y, children[2].heights.z, children[3].heights.w);
    //        InitBounds();
    //    }


    //    void GetNormals(out Vector3 n1, out Vector3 n2)
    //    {
    //        n1 = Vector3.Normalize(Vector3.Cross(points[0] - points[1], points[2] - points[0]));
    //        n2 = Vector3.Normalize(Vector3.Cross(points[3] - points[2], points[1] - points[3]));

    //    }

    //    public void ClipNode(bool recursion)
    //    {
    //        if (children == null) return;
    //        int sameCount = 0;
    //        if(size * unitMeter <= 64)
    //        {
    //            children = null;
    //            return;
    //        }
    //        for(int i=0; i< children.Length; i++)
    //        {
    //            if (Mathf.Abs(children[i].lodBias - lodBias) < 0.01f &&
    //                Mathf.Abs(children[i].aabb.min.y - aabb.min.y) < 0.01f &&
    //                Mathf.Abs(children[i].aabb.max.y - aabb.max.y) < 0.01f)
    //                sameCount++;
    //        }
    //        if(sameCount == 4)
    //        {
    //            children = null;
    //            //if (parent != null)
    //            //    parent.ClipNode(false);
    //        }
    //        else
    //        {
    //            for (int i = 0; i < children.Length; i++)
    //            {
    //                children[i].ClipNode(true);
    //            }
    //        }
    //    }

    //    public List<Node> GetNodesRaw()
    //    {
    //        List<Node> nodes = new List<Node>();
    //        nodes.Add(this);

    //        int startIndex = 0;
    //        int endIndex = nodes.Count;

    //        while(startIndex != endIndex)
    //        {
    //            for(int i=startIndex; i<endIndex; i++)
    //            {
    //                if (nodes[i].children != null)
    //                {
    //                    nodes.AddRange(nodes[i].children);
    //                }
    //            }
    //            startIndex = endIndex;
    //            endIndex = nodes.Count;
    //        }

    //        return nodes;
    //    }


    //}

    //Node m_Root;

    class Node
    {
        public float minHeight;
        public float maxHeight;
        public float lodBias;
        public Vector3[] points;
    }
    override public void OnInspectorGUI()
    {
        GPUTerrain vTerrain = target as GPUTerrain;
        base.OnInspectorGUI();
        if (Application.isPlaying)
        {
            DrawTexture(vTerrain.GetSectorLodTex(), "SectorLodTex");

        }
        //if (GUILayout.Button("合并unity Terrain生成GPUAsset", GUILayout.ExpandWidth(true)))
        //{
        //    string SaveLocation = EditorUtility.SaveFilePanelInProject("Save TerrainData", "New TerrainData", "asset", "Please enter a file name to save the TerrainData");
        //    if(SaveLocation != "")
        //    {
                //Terrain[] terrains = vTerrain.GetComponentsInChildren<Terrain>();
                //int num = terrains.Length;
                //int ncell = (int)Mathf.Sqrt(num);
                //System.Array.Sort<Terrain>(terrains, (a, b) => {
                //    Vector3 apos = a.transform.position;
                //    Vector3 bpos = b.transform.position;
                //    int zc = apos.z.CompareTo(bpos.z);
                //    return zc != 0 ? zc : apos.x.CompareTo(bpos.x);
                //});

                //TerrainData terrainData0 = terrains[0].terrainData;
                //Vector3 size = terrainData0.size;
                //Vector3 heightmapScale = terrainData0.heightmapScale;
                //int heightmapRes = terrainData0.heightmapResolution;
                //int alphaResolution = terrainData0.alphamapResolution;
                //float unitMeter = size.x / (heightmapRes - 1);
                //Vector3 sceneOffset = terrains[0].transform.position;
                ////vTerrain.unitMeter = unitMeter;
                ////vTerrain.sceneOffset = sceneOffset;

                //TerrainData terrainData = new TerrainData();
                //terrainData.heightmapResolution = (heightmapRes - 1) * ncell + 1;
                //terrainData.size = new Vector3(size.x * ncell, size.y, size.z * ncell);
                //terrainData.alphamapResolution = alphaResolution * ncell;

                //for (int i = 0; i < num; i++)
                //{
                //    int x = i % ncell;
                //    int y = i / ncell;
                //    terrainData.SetHeights(x * (heightmapRes-1), y * (heightmapRes-1), terrains[i].terrainData.GetHeights(0,0, heightmapRes, heightmapRes));
                //}

                //Node.sceneOffset = vTerrain.sceneOffset;
                //Node.unitMeter = vTerrain.unitMeter;

                //int atlasSize = (heightmapRes - 1) * ncell;

                //int levelCount = (int)Mathf.Log(heightmapRes - 1, 2);
                //Node[] [,] levelNodes = new Node[levelCount][,];

                //int curSize = atlasSize;
                //int block = 1;
                //float [,] heights = terrainData.GetHeights(0,0, terrainData.heightmapResolution, terrainData.heightmapResolution);

                //int sectorCount = (int)((terrainData.heightmapResolution - 1) * unitMeter / 64);
 
                //for (int level = 0; level < levelCount; level++)
                //{
                //    curSize = atlasSize / block;
                //    levelNodes[level] = new Node[curSize , curSize];

                //    for (int z=0; z < curSize; z++)
                //    {
                //        for( int x =0; x <curSize; x++)
                //        {
                //            if(level == 0)
                //            {
                //                Vector4 cornerHeights = new Vector4(heights[z, x],
                //                    heights[z, x + 1],
                //                    heights[z + 1, x],
                //                    heights[z + 1, x + 1]);
                //                float min = Mathf.Min(cornerHeights.x, cornerHeights.y, cornerHeights.z, cornerHeights.w);
                //                float max = Mathf.Max(cornerHeights.x, cornerHeights.y, cornerHeights.z, cornerHeights.w);
                //                Vector3[] points = new Vector3[] {
                //                            new Vector3(x,0,z)*unitMeter + Vector3.up * cornerHeights.x * heightmapScale.y + sceneOffset,
                //                            new Vector3(x+1,0,z) * unitMeter + Vector3.up * cornerHeights.y * heightmapScale.y + sceneOffset,
                //                            new Vector3(x, 0, z + 1) * unitMeter + Vector3.up * cornerHeights.z * heightmapScale.y + sceneOffset,
                //                            new Vector3(x+1, 0, z+1) * unitMeter + Vector3.up * cornerHeights.w * heightmapScale.y + sceneOffset
                //                };

                //                Vector3 n1 = Vector3.Normalize(Vector3.Cross(points[0] - points[1], points[2] - points[0]));
                //                Vector3 n2 = Vector3.Normalize(Vector3.Cross(points[3] - points[2], points[1] - points[3]));

                //                float lodBias = Mathf.Lerp(0, 1, Mathf.Clamp01(Vector3.Dot(n1, n2)));
                //                levelNodes[level][z , x] = new Node { minHeight = min, maxHeight = max, lodBias = lodBias, points = points };
                //            }
                //            else
                //            {
                //                int x0 = 2 * x;
                //                int z0 = 2 * z;
                //                Node []children = new Node[4];
                //                children[0] = levelNodes[level-1][z0, x0];
                //                children[1] = levelNodes[level-1][z0, x0 + 1];
                //                children[2] = levelNodes[level-1][z0 + 1, x0];
                //                children[3] = levelNodes[level-1][z0 + 1, x0 + 1];

                //                Vector3[] points = new Vector3[]
                //                {
                //                    children[0].points[0],
                //                    children[1].points[1],
                //                    children[2].points[2],
                //                    children[3].points[3],
                //                };

                //                Vector3 n1 = Vector3.Normalize(Vector3.Cross(points[0] - points[1], points[2] - points[0]));
                //                Vector3 n2 = Vector3.Normalize(Vector3.Cross(points[3] - points[2], points[1] - points[3]));
                //                float lodBias = Mathf.Min(children[0].lodBias, children[1].lodBias, children[2].lodBias, children[3].lodBias);
                //                lodBias = Mathf.Min(lodBias, Mathf.Lerp(0, 1, Mathf.Clamp01(Vector3.Dot(n1, n2))));

                //                float min = Mathf.Min(children[0].minHeight, children[1].minHeight, children[2].minHeight, children[3].minHeight);
                //                float max = Mathf.Max(children[0].maxHeight, children[1].maxHeight, children[2].maxHeight, children[3].maxHeight);

                //                levelNodes[level][z, x] = new Node { minHeight = min, maxHeight = max, lodBias = lodBias, points = points };
                //            }
                //        }
                //    }

                //    block *= 2;
                //}


                //m_Root = new Node();
                //m_Root.size = atlasSize;

                //for(int z=0; z < atlasSize; z++ )
                //{
                //    for(int x=0; x < atlasSize; x++)
                //    {
                //        Vector4 cornerHeights = new Vector4(terrainData.GetHeight(x , z ),
                //            terrainData.GetHeight(x + 1, z ),
                //            terrainData.GetHeight(x , z + 1),
                //            terrainData.GetHeight(x + 1, z + 1));
                //        m_Root.Insert(x, z, cornerHeights);

                //    }
                //}


                //m_Root.UpdateBounds();

                //m_Root.ClipNode(true);

                //List<Node> nodesRaw = m_Root.GetNodesRaw();
                //for(int i=0; i< nodesRaw.Count; i++)
                //{
                //    nodesRaw[i].index = i;
                //}

                //TerrainAsset.NodeCSData[] nodesCS = new TerrainAsset.NodeCSData[nodesRaw.Count];
                //for(int i=0; i< nodesCS.Length; i++)
                //{
                //    Node node = nodesRaw[i];
                //    TerrainAsset.NodeCSData nodeCSData = new TerrainAsset.NodeCSData() { x = node.x, z = node.z, 
                //        lodBias = node.lodBias, 
                //        heightRange = { x = node.aabb.min.y, y = node.aabb.max.y }, 
                //        children = node.children != null ? node.children[0].index : 0
                //    };
                //    nodesCS[i] = nodeCSData;
                //}

                //TerrainAsset terrainAsset = ScriptableObject.CreateInstance<TerrainAsset>();
                //terrainAsset.nodeCSData = nodesCS;

                //int maxLodNodeStart = 0;
                //int k = terrains.Length;
                //while (k > 1)
                //{
                //    k /= 4;
                //    maxLodNodeStart += k > 1 ? 4 : 1;
                //}

                //terrainAsset.maxLodNodeStart = maxLodNodeStart;
                //terrainAsset.maxLodNodeCount = ncell;

                //terrainAsset.terrainCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GPUTerrainVT/Shader/GPUTerrain.compute");
                //AssetDatabase.CreateAsset(terrainAsset, AssetDatabase.GenerateUniqueAssetPath(SaveLocation));
                //vTerrain.terrainAsset = terrainAsset;
                //vTerrain.nodeCSData = nodesCS;
                /** test
                GameObject LandscapeObject = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
                LandscapeObject.name = "Landscape";
                LandscapeObject.transform.position = new Vector3(10000, 0, 10000);
                */

        //    }
        //}
    }


   
    protected void DrawTexture(Texture texture, string label = null)
    {
        if (texture == null)
            return;

        EditorGUILayout.Space();
        if (!string.IsNullOrEmpty(label))
        {
            EditorGUILayout.LabelField(label);
            EditorGUILayout.LabelField(string.Format("    Size: {0} X {1}", texture.width, texture.height));
        }
        else
        {
            EditorGUILayout.LabelField(string.Format("Size: {0} X {1}", texture.width, texture.height));
        }

        EditorGUI.DrawPreviewTexture(GUILayoutUtility.GetAspectRect((float)texture.width / texture.height), texture);
    }

};
