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
    public Mesh mesh;
    public GraphicsBuffer vertexBuffer { get; private set; }
    public GraphicsBuffer triangleBuffer { get; private set; }
    public Bounds worldBound { get; private set; }
    private int resetBuffersKernel;
    private int restoreBuffersKernel;
    private int exportBuffersKernel;
    #endregion

    #region Config Section
    internal void Setup()
    {
        if (terrainReady) return;

        MeshFilter filter = GetComponent<MeshFilter>();

        mesh = TerrainGenerator.GetMesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        filter.sharedMesh = mesh;

        vertexBuffer = mesh.GetVertexBuffer(0);
        triangleBuffer = mesh.GetIndexBuffer();

        Vector3 position = transform.position;
        worldBound = new Bounds
        {
            center = Vector3.Scale(mesh.bounds.center, transform.localScale) + position,
            extents = Vector3.Scale(mesh.bounds.extents, transform.localScale)
        };

        SetupIO(new Vector2(position.x, position.z));
        SetupVisual();

        terrainReady = true;

        OnTerrainReady.Invoke();

        OnTerrainChange.Invoke();
    }

    public void Reset()
    {
        Setup();

        Graphics.CopyBuffer(TerrainGenerator.GetVertexReference(), vertexBuffer);

        configShader.Dispatch(resetBuffersKernel, 1, 1, 1);
    }

    private void OnDestroy()
    {
        Destroy(mesh);
        vertexBuffer.Dispose();
        triangleBuffer.Dispose();
        Destroy(configShader);
    }
    #endregion

    #region IO Section
    [SerializeField] private ComputeShader configShader;

    private void SetupIO(Vector2 anchor)
    {
        configShader = Instantiate(configShader);

        configShader.SetFloat("_Area", area);
        configShader.SetVector("_Anchor", anchor);

        configShader.SetInt("_Size",        meshSize);
        configShader.SetInt("_MeshSection", Mathf.CeilToInt(((float)(meshSize + 1)) / 32f));

        configShader.SetInt("_Stride",         VertexBufferStride);
        configShader.SetInt("_PositionOffset", VertexPositionAttributeOffset);
        configShader.SetInt("_BaseOffset",     VertexBaseAttributeOffset);
        configShader.SetInt("_ModifyOffset",   VertexModifyAttributeOffset);

        resetBuffersKernel = configShader.FindKernel("ResetBuffers");
        configShader.SetBuffer(resetBuffersKernel, "_Vertices", vertexBuffer);

        restoreBuffersKernel = configShader.FindKernel("RestoreBuffers");
        configShader.SetBuffer(restoreBuffersKernel, "_Vertices", vertexBuffer);

        exportBuffersKernel = configShader.FindKernel("ExportBuffers");
        configShader.SetBuffer(exportBuffersKernel, "_Vertices", vertexBuffer);

        configShader.Dispatch(resetBuffersKernel, 1, 1, 1);
    }

    public unsafe AsyncGPUReadbackRequest Save(string path)
    {

        int count = (meshSize + 1) * (meshSize + 1);
        int element = sizeof(float);
        int size = element * count;

        ComputeBuffer exportBuffer = new ComputeBuffer(count, element, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        configShader.SetBuffer(exportBuffersKernel, "_Exports", exportBuffer);
        configShader.Dispatch(exportBuffersKernel, 1, 1, 1);

        return AsyncGPUReadback.Request(exportBuffer, (AsyncGPUReadbackRequest request) =>
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

        configShader.SetBuffer(restoreBuffersKernel, "_Exports", exportBuffer);
        configShader.Dispatch(restoreBuffersKernel, 1, 1, 1);

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
