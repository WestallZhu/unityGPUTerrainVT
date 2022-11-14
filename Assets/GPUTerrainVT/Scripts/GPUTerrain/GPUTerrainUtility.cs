using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using static Unity.Mathematics.math;

internal static unsafe class GPUTerrainUtility
{
    public static Mesh CreateBatchMesh(int size, float unitMeter)
    {
        var mesh = new Mesh();
        int quadCount = size * size;
        int triCount = quadCount * 2;
        float centerOffset = -size * unitMeter * 0.5f;

        int vertCount = (size + 1) * (size + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            int x = i % (size + 1);
            int z = i / (size + 1);
            vertices[i] = new Vector3(centerOffset + x * unitMeter, 0, centerOffset + z * unitMeter);
            uvs[i] = new Vector2(x / (float)size, z / (float)size);
        }
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);

        int[] indices = new int[triCount * 3];// 6*quad

        for (int i = 0; i < quadCount; i++)
        {
            int offset = i * 6;
            int index = (i / size) * (size + 1) + (i % size);

            indices[offset] = index;
            indices[offset + 1] = index + size + 1;
            indices[offset + 2] = index + 1;
            indices[offset + 3] = index + 1;
            indices[offset + 4] = index + size + 1;
            indices[offset + 5] = index + size + 2;
        }
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        mesh.UploadMeshData(false);

        return mesh;
    }

    public static void SetSHCoefficients(Vector3 position, MaterialPropertyBlock properties)
    {
        SphericalHarmonicsL2 sh;
        LightProbes.GetInterpolatedProbe(position + new Vector3(0, 10, 0), null, out sh);

        // Constant + Linear
        for (var i = 0; i < 3; i++)
            properties.SetVector(idSHA[i], new Vector4(
                sh[i, 3], sh[i, 1], sh[i, 2], sh[i, 0] - sh[i, 6]
            ));

        // Quadratic polynomials
        for (var i = 0; i < 3; i++)
            properties.SetVector(idSHB[i], new Vector4(
                sh[i, 4], sh[i, 6], sh[i, 5] * 3, sh[i, 7]
            ));

        // Final quadratic polynomial
        properties.SetVector(idSHC, new Vector4(
            sh[0, 8], sh[2, 8], sh[1, 8], 1
        ));
    }


    static int[] idSHA = {
        Shader.PropertyToID("unity_SHAr"),
        Shader.PropertyToID("unity_SHAg"),
        Shader.PropertyToID("unity_SHAb")
    };

    static int[] idSHB = {
        Shader.PropertyToID("unity_SHBr"),
        Shader.PropertyToID("unity_SHBg"),
        Shader.PropertyToID("unity_SHBb")
    };

    static int idSHC =
        Shader.PropertyToID("unity_SHC");


    public struct PerspCam
    {
        public float3 right;
        public float3 up;
        public float3 forward;
        public float3 position;
        public float fov;
        public float nearClipPlane;
        public float farClipPlane;
        public float aspect;
    }

    public static (float3, float3) GetFrustumMinMaxPoint(Camera camera)
    {
        Transform trans = camera.transform;
        PerspCam perspCam = new PerspCam
        {
            fov = camera.fieldOfView,
            nearClipPlane = camera.nearClipPlane,
            farClipPlane = camera.farClipPlane,
            aspect = camera.aspect,
            forward = trans.forward,
            right = trans.right,
            up = trans.up,
            position = trans.position,
        };
        float3* frustumCorners = stackalloc float3[8];
        GetFrustumCorner(ref perspCam, frustumCorners);
        float3 minFrustumPlanes = frustumCorners[0];
        float3 maxFrustumPlanes = frustumCorners[0];
        for (int i = 1; i < 8; ++i)
        {
            minFrustumPlanes = min(minFrustumPlanes, frustumCorners[i]);
            maxFrustumPlanes = max(maxFrustumPlanes, frustumCorners[i]);
        }
        return (minFrustumPlanes, maxFrustumPlanes);
    }

    public static void GetFrustumCorner(ref PerspCam perspCam, float3* corners)
    {
        float fov = tan(Mathf.Deg2Rad * perspCam.fov * 0.5f);
        void GetCorner(float dist, ref PerspCam persp)
        {
            float upLength = dist * (fov);
            float rightLength = upLength * persp.aspect;
            float3 farPoint = persp.position + dist * persp.forward;
            float3 upVec = upLength * persp.up;
            float3 rightVec = rightLength * persp.right;
            corners[0] = farPoint - upVec - rightVec;
            corners[1] = farPoint - upVec + rightVec;
            corners[2] = farPoint + upVec - rightVec;
            corners[3] = farPoint + upVec + rightVec;
            corners += 4;
        }
        GetCorner(perspCam.nearClipPlane, ref perspCam);
        GetCorner(perspCam.farClipPlane, ref perspCam);
    }


}
