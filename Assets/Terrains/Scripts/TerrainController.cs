using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainController : MonoBehaviour
{

    // General state
    private static int meshSize => TerrainCoordinator.meshSize;
    [SerializeField] private ComputeShader configShader;
    
    #region Vertices Section
    public GraphicsBuffer graphicsBuffer { get; private set; }
    public static int VertexBufferStride => MeshGenerator.GetVertexBufferStride();
    public static int VertexPositionAttributeOffset => MeshGenerator.GetVertexPositionAttributeOffset();
    public static int VertexNormalAttributeOffset => MeshGenerator.GetVertexNormalAttributeOffset();
    public static int VertexBaseAttributeOffset => MeshGenerator.GetVertexBaseAttributeOffset();
    public static int VertexModifyAttributeOffset => MeshGenerator.GetVertexModifyAttributeOffset();
    #endregion

    #region Config Section
    private bool setupComplete = false;
    private void Setup()
    {
        if (setupComplete) return;

        GetComponentInParent<TerrainCoordinator>();

        configShader = Instantiate(configShader);

        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = MeshGenerator.GetMesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        filter.sharedMesh = mesh;
        graphicsBuffer = mesh.GetVertexBuffer(0);
        
        configShader.SetInt(Shader.PropertyToID("size"), meshSize);
        configShader.SetInt(Shader.PropertyToID("meshSection"), Mathf.CeilToInt(((float)(meshSize * 2 + 1)) / 32f));
        configShader.SetInt(Shader.PropertyToID("stride"), VertexBufferStride);
        configShader.SetInt(Shader.PropertyToID("positionOffset"), VertexPositionAttributeOffset);
        configShader.SetInt(Shader.PropertyToID("normalOffset"), VertexNormalAttributeOffset);
        configShader.SetInt(Shader.PropertyToID("baseOffset"), VertexBaseAttributeOffset);
        configShader.SetInt(Shader.PropertyToID("modifyOffset"), VertexModifyAttributeOffset);

        int resetBuffersKernelIndex = configShader.FindKernel("ResetBuffers");
        configShader.SetBuffer(resetBuffersKernelIndex, Shader.PropertyToID("vertices"), graphicsBuffer);

        int restoreBuffersKernelIndex = configShader.FindKernel("RestoreBuffers");
        configShader.SetBuffer(restoreBuffersKernelIndex, Shader.PropertyToID("vertices"), graphicsBuffer);

        int exportBuffersKernelIndex = configShader.FindKernel("ExportBuffers");
        configShader.SetBuffer(exportBuffersKernelIndex, Shader.PropertyToID("vertices"), graphicsBuffer);

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

        int count = (meshSize + 1) * (meshSize + 1);
        int element = sizeof(float);
        int size = element * count;

        int exportBuffersKernelIndex = configShader.FindKernel("ExportBuffers");
        ComputeBuffer exportBuffer = new ComputeBuffer(count, element, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
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
            
        int count = (meshSize + 1) * (meshSize + 1);
        int element = sizeof(float);
        int size = element * count;
        NativeArray<byte> input = new NativeArray<byte>(size, Allocator.Persistent);

        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (GZipStream compressor = new GZipStream(stream, CompressionMode.Decompress))
                compressor.Read(input);
                
        ComputeBuffer exportBuffer = new ComputeBuffer(count, element, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
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
        private static int vertexBaseAttributeOffset;
        private static int vertexModifyAttributeOffset;

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

        public static int GetVertexBaseAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexBaseAttributeOffset;
        }

        public static int GetVertexModifyAttributeOffset()
        {
            if (prototype == null)
                CreateMesh();

            return vertexModifyAttributeOffset;
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
            mesh.SetVertexBufferParams(vertices.Length, new VertexAttributeDescriptor[] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 1, 0), // base
                new VertexAttributeDescriptor(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 4, 0) // mask and modifiers
            });
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
            vertexBaseAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord6);
            vertexModifyAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord7);
        }

    }
}
