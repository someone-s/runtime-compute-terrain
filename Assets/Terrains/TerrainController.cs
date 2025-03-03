using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System;
using Unity.Collections.LowLevel.Unsafe;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainController : MonoBehaviour
{

    // General state
    public static int meshSize { get; private set; } = 125;
    [SerializeField] private ComputeShader configShader;

    #region Buffer Section
    public ComputeBuffer baseBuffer { get; private set; } 
    public ComputeBuffer modifyBuffer { get; private set; } 

    [StructLayout(LayoutKind.Sequential)]
    private struct Modify
    {
        public float mandate;
        public float minimum;
        public float maximum;
        public uint mask;
    } 
    #endregion
    
    #region Vertices Section
    public GraphicsBuffer graphicsBuffer { get; private set; }
    public static int VertexBufferStride => MeshGenerator.GetVertexBufferStride();
    public static int VertexPositionAttributeOffset => MeshGenerator.GetVertexPositionAttributeOffset();
    public static int VertexNormalAttributeOffset => MeshGenerator.GetVertexNormalAttributeOffset();
    #endregion

    #region Config Section
    private bool setupComplete = false;
    private void Setup()
    {
        if (setupComplete) return;

        configShader = Instantiate(configShader);

        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = MeshGenerator.GetMesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);
        
        baseBuffer = new ComputeBuffer((meshSize + 1) * (meshSize + 1), sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        modifyBuffer = new ComputeBuffer((meshSize + 1) * (meshSize + 1), sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        
        configShader.SetInt(Shader.PropertyToID("size"), meshSize);
        configShader.SetInt(Shader.PropertyToID("meshSection"), Mathf.CeilToInt(((float)(meshSize * 2 + 1)) / 32f));
        configShader.SetInt(Shader.PropertyToID("stride"), VertexBufferStride);
        configShader.SetInt(Shader.PropertyToID("positionOffset"), VertexPositionAttributeOffset);
        configShader.SetInt(Shader.PropertyToID("normalOffset"), VertexNormalAttributeOffset);

        int resetBuffersKernelIndex = configShader.FindKernel("ResetBuffers");
        configShader.SetBuffer(resetBuffersKernelIndex, Shader.PropertyToID("base"),   baseBuffer  );
        configShader.SetBuffer(resetBuffersKernelIndex, Shader.PropertyToID("modify"), modifyBuffer);

        int restoreBuffersKernelIndex = configShader.FindKernel("RestoreBuffers");
        configShader.SetBuffer(restoreBuffersKernelIndex, Shader.PropertyToID("vertices"), graphicsBuffer);
        configShader.SetBuffer(restoreBuffersKernelIndex, Shader.PropertyToID("base"),     baseBuffer    );

        int exportBuffersKernelIndex = configShader.FindKernel("ExportBuffers");
        configShader.SetBuffer(exportBuffersKernelIndex, Shader.PropertyToID("vertices"), graphicsBuffer);
        configShader.SetBuffer(exportBuffersKernelIndex, Shader.PropertyToID("base"),     baseBuffer    );

        configShader.Dispatch(resetBuffersKernelIndex, 32, 32, 1);

        setupComplete = true;
    }

    private void Start() => Setup();

    public void Reset()
    {
        Setup();

        Graphics.CopyBuffer(MeshGenerator.GetReference(), graphicsBuffer);

        int resetBuffersKernalIndex = configShader.FindKernel("ResetBuffers");
        configShader.Dispatch(resetBuffersKernalIndex, 32, 32, 1);
    }
    #endregion

    #region IO Section
    public unsafe void Save(string path)
    {

        int exportBuffersKernelIndex = configShader.FindKernel("ExportBuffers");
        ComputeBuffer exportBuffer = new ComputeBuffer((meshSize + 1) * (meshSize + 1), sizeof(float) * 4, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        configShader.SetBuffer(exportBuffersKernelIndex, Shader.PropertyToID("exports"), exportBuffer);
        configShader.Dispatch(exportBuffersKernelIndex, 32, 32, 1);

        AsyncGPUReadback.Request(exportBuffer, (AsyncGPUReadbackRequest request) => {
            Assert.IsFalse(request.hasError, "Error in TerrainController readback");

            NativeArray<byte> output = request.GetData<byte>();

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
               using (GZipStream compressor = new GZipStream(stream, CompressionMode.Compress))
                   compressor.Write(output);

            exportBuffer.Dispose();
            output.Dispose();
        });

    }

    public void Load(string path) 
    {
        Setup();

        int size = sizeof(float) * 4* (meshSize + 1) * (meshSize + 1);
        NativeArray<byte> input = new NativeArray<byte>(size, Allocator.Persistent);

        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (GZipStream compressor = new GZipStream(stream, CompressionMode.Decompress))
                compressor.Read(input);
                
        //graphicsBuffer.SetData(input);
        ComputeBuffer exportBuffer = new ComputeBuffer((meshSize + 1) * (meshSize + 1), sizeof(float) * 4, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        exportBuffer.SetData(input);
        
        int restoreBuffersKernalIndex = configShader.FindKernel("RestoreBuffers");
        configShader.SetBuffer(restoreBuffersKernalIndex, Shader.PropertyToID("exports"), exportBuffer);
        configShader.Dispatch(restoreBuffersKernalIndex, 32, 32, 1);

        exportBuffer.Dispose();
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
