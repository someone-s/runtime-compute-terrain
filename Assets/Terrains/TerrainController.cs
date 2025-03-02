using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.IO;
using System;
using System.IO.Compression;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainController : MonoBehaviour
{

    // General state
    public static int meshSize { get; private set; } = 125;

    public GraphicsBuffer graphicsBuffer { get; private set; }
    public static int VertexBufferStride => MeshGenerator.GetVertexBufferStride();
    public static int VertexPositionAttributeOffset => MeshGenerator.GetVertexPositionAttributeOffset();
    public static int VertexNormalAttributeOffset => MeshGenerator.GetVertexNormalAttributeOffset();

    #region Config Section
    private bool setupComplete = false;
    private void Setup()
    {
        if (setupComplete) return;

        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = MeshGenerator.GetMesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);

        setupComplete = true;
    }

    private void Start() => Setup();

    public void Reset()
    {
        Setup();

        Graphics.CopyBuffer(MeshGenerator.GetReference(), graphicsBuffer);
    }
    #endregion

    #region IO Section
    public void Save(string path)
    {
        int size = VertexBufferStride * (meshSize + 1) * (meshSize + 1);
        NativeArray<byte> output = new NativeArray<byte>(size, Allocator.Persistent);

        AsyncGPUReadback.RequestIntoNativeArray(ref output, graphicsBuffer, (AsyncGPUReadbackRequest request) => {
            Assert.IsFalse(request.hasError, "Error in TerrainController readback");

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
               using (GZipStream compressor = new GZipStream(stream, CompressionMode.Compress))
                   compressor.Write(output);

            output.Dispose();
        });

    }

    public void Load(string path) 
    {
        Setup();

        int size = VertexBufferStride * (meshSize + 1) * (meshSize + 1);
        NativeArray<byte> input = new NativeArray<byte>(size, Allocator.Persistent);

        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (GZipStream compressor = new GZipStream(stream, CompressionMode.Decompress))
                compressor.Read(input);
                
        graphicsBuffer.SetData(input);

        input.Dispose();
    }
    #endregion

    #region Visual Section
    private MeshRenderer visualRenderer;

    public void SetVisible(bool visualState)
    {
        if (visualRenderer == null)
            visualRenderer = GetComponent<MeshRenderer>();
        visualRenderer.enabled = visualState;
    }
    #endregion

    private static class MeshGenerator
    {
        private static Mesh prototype = null;
        private static GraphicsBuffer reference = null;
        private static int vertexBufferStride;
        private static int vertexPositionAttributeOffset;
        private static int vertexNormalAttributeOffset;

        public static int GetVertexBufferStride()
        {
            if (prototype == null)
                CreateMesh();

            return vertexBufferStride;
        }

        public static int GetVertexPositionAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexPositionAttributeOffset;
        }

        public static int GetVertexNormalAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexNormalAttributeOffset;
        }

        public static Mesh GetMesh()
        {
            if (prototype == null)
                CreateMesh();

            return Instantiate(prototype);
        }

        public static GraphicsBuffer GetReference()
        {
            if (prototype == null)
                CreateMesh();

            return reference;
        }

        private static void CreateMesh()
        {
            NativeArray<float3> vertices = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
            for (int z = 0; z < meshSize + 1; z++)
                for (int x = 0; x < meshSize + 1; x++)
                {
                    vertices[z * (meshSize + 1) + x] = new float3(x / (float)meshSize, 0, z / (float)meshSize);
                    normals[z * (meshSize + 1) + x] = new float3(0f, 1f, 0f);
                }

            NativeArray<int> indices = new NativeArray<int>(6 * meshSize * meshSize, Allocator.Temp);
            for (int z = 0; z < meshSize; z++)
                for (int x = 0; x < meshSize; x++)
                {
                    indices[(z * meshSize + x) * 6 + 0] = (z + 0) * (meshSize + 1) + (x + 0);
                    indices[(z * meshSize + x) * 6 + 1] = (z + 1) * (meshSize + 1) + (x + 1);
                    indices[(z * meshSize + x) * 6 + 2] = (z + 0) * (meshSize + 1) + (x + 1);
                    indices[(z * meshSize + x) * 6 + 3] = (z + 1) * (meshSize + 1) + (x + 1);
                    indices[(z * meshSize + x) * 6 + 4] = (z + 0) * (meshSize + 1) + (x + 0);
                    indices[(z * meshSize + x) * 6 + 5] = (z + 1) * (meshSize + 1) + (x + 0);
                }


            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetNormals(normals);
            mesh.bounds = new Bounds(new Vector3(0.5f, 0f, 0.5f), new Vector3(1f + 1f / meshSize, 200f, 1f + 1f / meshSize));

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            prototype = mesh;
            reference = mesh.GetVertexBuffer(0);
            vertexBufferStride = mesh.GetVertexBufferStride(0);
            vertexPositionAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            vertexNormalAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Normal);
        }

    }
}
