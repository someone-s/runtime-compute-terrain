using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainController))]
public class InstanceController : MonoBehaviour
{
    [SerializeField] private ComputeShader scatterShader;
    private int threadGroups;
    private int scatterKernel;

    [SerializeField] private Material material;
    [SerializeField] private Mesh mesh;


    [Header("Instance Setting")]
    [SerializeField] private bool castShadow = false;
    [SerializeField, Min(0.05f)] private float density = 4;
    [SerializeField] private float minHeight = 1f;
    [SerializeField] private float maxHeight = 2f;
    [SerializeField] private float scale = 2f;
    [SerializeField] private int inclusionMask = ~0;
    [SerializeField] private int exclusionMask = 0;


    [Header("Slope Setting")]
    [SerializeField, Range(0f, 180f)] private float slopeMinDegree = 0f;
    [SerializeField, Range(0f, 180f)] private float slopeMaxDegree = 70f;


    [Header("Wind Setting")]
    [SerializeField] private float windFrequency = 2f;
    [SerializeField] private float windAmplitude = 0.05f;


    [Header("LOD Setting")]
    [SerializeField] private float lod1Divider = 0.25f;
    [SerializeField] private float lod1Scale = 2f;
    [SerializeField] private float lod2Divider = 0.0625f;
    [SerializeField] private float lod2Scale = 2f;

    private InstanceUpdater updater;
    private RenderParams parameters;
    private MaterialPropertyBlock properties;
    private TerrainController controller;

    private GraphicsBuffer transformMatrixBuffer;
    private GraphicsBuffer meshIndexBuffer;
    private GraphicsBuffer argumentsBuffer;
    private int resultSize;

    private int lodLevel = -1;

    private void Start()
    {
        updater = InstanceUpdater.Instance;

        controller = GetComponent<TerrainController>();

        if (controller.terrainReady)
            Setup();
        controller.OnTerrainReady.AddListener(Setup);

        controller.OnTerrainChange.AddListener(QueueRefresh);

        controller.OnTerrainVisible.AddListener(Enable);
        controller.OnTerrainLod.AddListener(ChangeLod);
        controller.OnTerrainHidden.AddListener(Disable);
    }

    private void Enable(int selectedLod)
    {
        lodLevel = selectedLod;

        // restore saved memory
        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, resultSize * resultSize, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, "_TransformMatrices", transformMatrixBuffer);

        properties.SetBuffer("_TransformMatrices", transformMatrixBuffer);

        QueueRefresh();
    }

    private void ChangeLod(int selectedLod)
    {
        lodLevel = selectedLod;

        QueueRefresh();
    }

    private void Disable()
    {
        // save memory
        transformMatrixBuffer.Dispose();

        updater.RemoveRender(this);
    }


    private void Setup()
    {

        scatterShader = Instantiate(scatterShader);
        scatterKernel = scatterShader.FindKernel("Scatter");

        int meshSize = TerrainCoordinator.meshSize;
        resultSize = Mathf.CeilToInt((float)meshSize * density);

        scatterShader.SetInt("_ResultSize", resultSize);
        scatterShader.SetFloat("_ResultSizeInverse", 1f / resultSize);

        scatterShader.SetBuffer(scatterKernel, "_TerrainVertices", controller.vertexBuffer);
        scatterShader.SetBuffer(scatterKernel, "_TerrainTriangles", controller.triangleBuffer);
        scatterShader.SetInt("_TerrainSize", meshSize);
        scatterShader.SetInt("_TerrainStride", TerrainController.VertexBufferStride);
        scatterShader.SetInt("_TerrainPositionOffset", TerrainController.VertexPositionAttributeOffset);
        scatterShader.SetInt("_TerrainNormalOffset", TerrainController.VertexNormalAttributeOffset);
        scatterShader.SetInt("_TerrainModifyOffset", TerrainController.VertexModifyAttributeOffset);

        scatterShader.SetVector("_Anchor", controller.transform.position);
        scatterShader.SetFloat("_SlopeLower", Mathf.Sin(Mathf.Deg2Rad * (90f - slopeMinDegree)));
        scatterShader.SetFloat("_SlopeUpper", Mathf.Sin(Mathf.Deg2Rad * (90f - slopeMaxDegree)));
        scatterShader.SetInt("_InclusionMask", inclusionMask);
        scatterShader.SetInt("_ExclusionMask", exclusionMask);

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, resultSize * resultSize, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, "_TransformMatrices", transformMatrixBuffer);

        scatterShader.SetFloat("_Scale", scale);
        scatterShader.SetFloat("_MinHeight", minHeight);
        scatterShader.SetFloat("_MaxHeight", maxHeight);

        scatterShader.GetKernelThreadGroupSizes(scatterKernel, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
        Assert.IsTrue(threadGroupSizeX == threadGroupSizeY, "Scatter Shader numthread x and y must equal");
        Assert.IsTrue(threadGroupSizeZ == 1, "Scatter Shader numthread z must be 1");
        threadGroups = Mathf.CeilToInt((float)resultSize / threadGroupSizeX);

        properties = new MaterialPropertyBlock();
        properties.SetBuffer("_TransformMatrices", transformMatrixBuffer);
        properties.SetBuffer("_Vertices", mesh.GetVertexBuffer(0));
        properties.SetInt("_Stride", mesh.GetVertexBufferStride(0));
        properties.SetInt("_PositionOffset", mesh.GetVertexAttributeOffset(VertexAttribute.Position));
        properties.SetInt("_NormalOffset", mesh.GetVertexAttributeOffset(VertexAttribute.Normal));
        properties.SetInt("_TangentOffset", mesh.GetVertexAttributeOffset(VertexAttribute.Tangent));
        properties.SetInt("_UVOffset", mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0));
        properties.SetInt("_StaticLightmapUVOffset", mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1));

        properties.SetFloat("_WindFrequency", windFrequency);
        properties.SetFloat("_WindAmplitude", windAmplitude);

        parameters = new RenderParams(material);
        parameters.matProps = properties;
        parameters.worldBounds = controller.worldBound;
        parameters.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
        parameters.receiveShadows = true;

        meshIndexBuffer = mesh.GetIndexBuffer();

        argumentsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        var arguments = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        arguments[0].baseVertexIndex = mesh.GetBaseVertex(0);
        arguments[0].indexCountPerInstance = mesh.GetIndexCount(0);
        arguments[0].startIndex = mesh.GetIndexStart(0);
        arguments[0].startInstance = 0;
        argumentsBuffer.SetData(arguments);

        QueueRefresh();
    }

    internal void RenderCommand()
    {
        Graphics.RenderPrimitivesIndexedIndirect(parameters, MeshTopology.Triangles, meshIndexBuffer, argumentsBuffer);
    }

    private void OnDestroy()
    {
        transformMatrixBuffer?.Dispose();
        argumentsBuffer?.Dispose();

        updater.RemoveRender(this);
    }

    public void QueueRefresh()
    {
        updater.QueueRefresh(this);
    }

    internal void ExecuteRefresh()
    {
        if (!transformMatrixBuffer.IsValid()) return;


        float jump;
        float stretch;

        switch (lodLevel)
        {
            case 2:
                jump = lod2Divider;
                stretch = lod2Scale;
                break;
            case 1:
                jump = lod1Divider;
                stretch = lod1Scale;
                break;
            default:
                jump = 1f;
                stretch = 1f;
                break;
        }

        scatterShader.SetFloat("_Stretch", stretch);

        int resultSizeLOD = Mathf.CeilToInt(resultSize * jump);
        scatterShader.SetInt("_ResultSize", resultSizeLOD);
        scatterShader.SetFloat("_ResultSizeInverse", 1f / resultSizeLOD);

        scatterShader.GetKernelThreadGroupSizes(scatterKernel, out uint threadGroupSizeX, out _, out _);
        threadGroups = Mathf.CeilToInt((float)resultSizeLOD / threadGroupSizeX);

        transformMatrixBuffer.SetCounterValue(0);
        scatterShader.Dispatch(scatterKernel, threadGroups, threadGroups, 1);
        GraphicsBuffer.CopyCount(transformMatrixBuffer, argumentsBuffer, sizeof(uint));
        
        updater.AddRender(this);
    }
}
