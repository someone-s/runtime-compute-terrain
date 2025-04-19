using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.IO;
using System.IO.Compression;
using UnityEngine.Events;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TerrainController : MonoBehaviour
{
    #region Contributor Section
    internal HashSet<TerrainProjector> projectors = new();
    
    #endregion

    #region Event Section
    public bool terrainReady { get; private set; } = false;
    public UnityEvent OnTerrainReady;
    public UnityEvent OnTerrainChange;
    public UnityEvent<int> OnTerrainVisible;
    public UnityEvent<int> OnTerrainLod;
    public UnityEvent OnTerrainHidden;
    public UnityEvent<float> OnDistanceChange;
    #endregion

    #region Vertices Section
    public const float area = TerrainCoordinator.area;
    private const int meshSize = TerrainCoordinator.meshSize;
    public GraphicsBuffer vertexBuffer { get; private set; }
    public GraphicsBuffer triangleBuffer { get; private set; }
    public Bounds worldBound { get; private set; }
    #endregion

    #region Config Section
    internal void Setup()
    {
        if (terrainReady) return;

        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = TerrainGenerator.GetMesh();
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

        Graphics.CopyBuffer(TerrainGenerator.GetVertexReference(), vertexBuffer);

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

    #region Generator Section
    public static int VertexBufferStride => TerrainGenerator.GetVertexBufferStride();
    public static int VertexPositionAttributeOffset => TerrainGenerator.GetVertexPositionAttributeOffset();
    public static int VertexNormalAttributeOffset => TerrainGenerator.GetVertexNormalAttributeOffset();
    public static int VertexBaseAttributeOffset => TerrainGenerator.GetVertexBaseAttributeOffset();
    public static int VertexModifyAttributeOffset => TerrainGenerator.GetVertexModifyAttributeOffset();
    public static Bounds LocalBounds => TerrainGenerator.GetLocalBounds();
    
    #endregion
}
