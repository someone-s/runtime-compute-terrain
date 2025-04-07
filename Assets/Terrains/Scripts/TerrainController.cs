using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.IO;
using System.IO.Compression;
using UnityEngine.Events;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainController : MonoBehaviour
{
    #region Event Section
    public bool terrainReady { get; private set; } = false;
    public UnityEvent OnTerrainReady
    {
        get
        {
            if (TerrainReadyInstance == null)
                TerrainReadyInstance = new();
            return TerrainReadyInstance;
        }
    }
    private UnityEvent TerrainReadyInstance = null;
    public UnityEvent OnTerrainChange
    {
        get
        {
            if (TerrainChangeInstance == null)
                TerrainChangeInstance = new();
            return TerrainChangeInstance;
        }
    }
    private UnityEvent TerrainChangeInstance = null;
    public UnityEvent<int> OnTerrainVisible
    {
        get
        {
            if (TerrainVisibleInstance == null)
                TerrainVisibleInstance = new();
            return TerrainVisibleInstance;
        }
    }
    private UnityEvent<int> TerrainVisibleInstance = null;
    public UnityEvent<int> OnTerrainLod
    {
        get
        {
            if (TerrainLodInstance == null)
                TerrainLodInstance = new();
            return TerrainLodInstance;
        }
    }
    private UnityEvent<int> TerrainLodInstance = null;
    public UnityEvent OnTerrainHidden
    {
        get
        {
            if (TerrainHiddenInstance == null)
                TerrainHiddenInstance = new();
            return TerrainHiddenInstance;
        }
    }
    private UnityEvent TerrainHiddenInstance = null;
    public UnityEvent<float> OnDistanceChange
    {
        get
        {
            if (DistanceChangeDistance == null)
                DistanceChangeDistance = new();
            return DistanceChangeDistance;
        }
    }
    private UnityEvent<float> DistanceChangeDistance = null;
    #endregion


    #region Vertices Section
    public static float area => TerrainCoordinator.area;
    private static int meshSize => TerrainCoordinator.meshSize;
    public GraphicsBuffer vertexBuffer { get; private set; }
    public GraphicsBuffer triangleBuffer { get; private set; }
    public Bounds worldBound { get; private set; }
    #endregion

    #region Config Section
    internal void Setup()
    {
        if (terrainReady) return;

        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = MeshGenerator.GetMesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        filter.sharedMesh = mesh;
        vertexBuffer = mesh.GetVertexBuffer(0);
        triangleBuffer = mesh.GetIndexBuffer();
        worldBound = new Bounds {
            center = Vector3.Scale(mesh.bounds.center, transform.localScale) + transform.position,
            extents = Vector3.Scale(mesh.bounds.extents, transform.localScale)
        };

        SetupIO();
        SetupVisual();

        terrainReady = true;

        OnTerrainReady.Invoke();

        OnTerrainChange.Invoke();
    }

    private void Start() => Setup();

    public void Reset()
    {
        Setup();

        Graphics.CopyBuffer(MeshGenerator.GetVertexReference(), vertexBuffer);

        int resetBuffersKernalIndex = configShader.FindKernel("ResetBuffers");
        configShader.Dispatch(resetBuffersKernalIndex, 1, 1, 1);
    }
    #endregion

    #region IO Section
    [SerializeField] private ComputeShader configShader;

    private void SetupIO()
    {
        configShader = Instantiate(configShader);

        configShader.SetInt(Shader.PropertyToID("_Size"), meshSize);
        configShader.SetInt(Shader.PropertyToID("_MeshSection"), Mathf.CeilToInt(((float)(meshSize * 2 + 1)) / 32f));
        configShader.SetInt(Shader.PropertyToID("_Stride"), VertexBufferStride);
        configShader.SetInt(Shader.PropertyToID("_PositionOffset"), VertexPositionAttributeOffset);
        configShader.SetInt(Shader.PropertyToID("_BaseOffset"), VertexBaseAttributeOffset);
        configShader.SetInt(Shader.PropertyToID("_ModifyOffset"), VertexModifyAttributeOffset);

        int resetBuffersKernelIndex = configShader.FindKernel("ResetBuffers");
        configShader.SetBuffer(resetBuffersKernelIndex, Shader.PropertyToID("_Vertices"), vertexBuffer);

        int restoreBuffersKernelIndex = configShader.FindKernel("RestoreBuffers");
        configShader.SetBuffer(restoreBuffersKernelIndex, Shader.PropertyToID("_Vertices"), vertexBuffer);

        int exportBuffersKernelIndex = configShader.FindKernel("ExportBuffers");
        configShader.SetBuffer(exportBuffersKernelIndex, Shader.PropertyToID("_Vertices"), vertexBuffer);

        configShader.Dispatch(resetBuffersKernelIndex, 1, 1, 1);
    }

    public unsafe void Save(string path)
    {

        int count = (meshSize + 1) * (meshSize + 1);
        int element = sizeof(float);
        int size = element * count;

        int exportBuffersKernelIndex = configShader.FindKernel("ExportBuffers");
        ComputeBuffer exportBuffer = new ComputeBuffer(count, element, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        configShader.SetBuffer(exportBuffersKernelIndex, Shader.PropertyToID("_Exports"), exportBuffer);
        configShader.Dispatch(exportBuffersKernelIndex, 1, 1, 1);

        AsyncGPUReadback.Request(exportBuffer, (AsyncGPUReadbackRequest request) =>
        {
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
        configShader.SetBuffer(restoreBuffersKernalIndex, Shader.PropertyToID("_Exports"), exportBuffer);
        configShader.Dispatch(restoreBuffersKernalIndex, 1, 1, 1);

        exportBuffer.Dispose();
        input.Dispose();

        OnTerrainChange.Invoke();
    }
    #endregion

    #region Visual Section
    public int lodLevel { get; private set; }
    private MeshRenderer visualRenderer;

    private void SetupVisual()
    {
        visualRenderer = GetComponent<MeshRenderer>();
    }

    public void SetHidden()
    {
        visualRenderer.enabled = false;
        OnTerrainHidden.Invoke();
    }

    public void SetVisible(int selectedLod)
    {
        visualRenderer.enabled = true;
        OnTerrainVisible.Invoke(selectedLod);
    }

    public void SetLod(int selectedLod)
    {
        lodLevel = selectedLod;
        OnTerrainLod.Invoke(selectedLod);
    }
    #endregion

    public static int VertexBufferStride => MeshGenerator.GetVertexBufferStride();
    public static int VertexPositionAttributeOffset => MeshGenerator.GetVertexPositionAttributeOffset();
    public static int VertexNormalAttributeOffset => MeshGenerator.GetVertexNormalAttributeOffset();
    public static int VertexBaseAttributeOffset => MeshGenerator.GetVertexBaseAttributeOffset();
    public static int VertexModifyAttributeOffset => MeshGenerator.GetVertexModifyAttributeOffset();
    public static Bounds LocalBounds => MeshGenerator.GetLocalBounds();
    
    private static class MeshGenerator
    {
        private static Mesh prototype = null;
        private static Bounds localBounds;
        private static GraphicsBuffer vertexReferenceBuffer = null;
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

        public static GraphicsBuffer GetVertexReference()
        {
            if (prototype == null)
                CreateMesh();

            return vertexReferenceBuffer;
        }

        public static Bounds GetLocalBounds()
        {
            if (prototype == null)
                CreateMesh();

            return localBounds;
        }

        private static void CreateMesh()
        {
            NativeArray<float3> vertices = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
            for (int z = 0; z < meshSize + 1; z++)
                for (int x = 0; x < meshSize + 1; x++)
                {
                    vertices[z * (meshSize + 1) + x] = new float3(x / (float)meshSize * area, 0, z / (float)meshSize * area);
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
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertexBufferParams(vertices.Length, new VertexAttributeDescriptor[] {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 1, 0), // base
                new VertexAttributeDescriptor(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 4, 0) // mask and modifiers
            });
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetNormals(normals);
            mesh.bounds = new Bounds(new Vector3(0.5f * area, 0f, 0.5f * area), new Vector3((1f + 1f / meshSize) * area, 200f, (1f + 1f / meshSize) * area));

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            prototype = mesh;
            vertexReferenceBuffer = mesh.GetVertexBuffer(0);
            vertexBufferStride = mesh.GetVertexBufferStride(0);
            vertexPositionAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            vertexNormalAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Normal);
            vertexBaseAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord6);
            vertexModifyAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord7);
            localBounds = mesh.bounds;
        }

    }
}
