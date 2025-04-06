using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainController))]
public class InstanceController : MonoBehaviour
{
    [SerializeField] private ComputeShader scatterShader;
    private int threadGroups;
    private int scatterKernel;

    [SerializeField] private ComputeShader setShader;
    private int setKernel;

    [SerializeField] private Material material;
    [SerializeField] private Mesh mesh;


    [Header("Instance Setting")]
    [SerializeField, Min(0.05f)] private float density = 4;
    [SerializeField] private float minHeight = 1f;
    [SerializeField] private float maxHeight = 2f;
    [SerializeField] private float scale = 2f;

    [Header("Slope Setting")]
    [SerializeField, Range(0f, 180f)] private float slopeMinDegree = 0f;
    [SerializeField, Range(0f, 180f)] private float slopeMaxDegree = 70f;


    [Header("Wind Setting")]
    [SerializeField] private float windFrequency = 2f;
    [SerializeField] private float windAmplitude = 0.05f;


    [Header("LOD Setting")]
    [SerializeField] private int lod1Divider = 4;
    [SerializeField] private float lod1Distance = 100f;
    [SerializeField] private float lod1Scale = 2f;
    [SerializeField] private int lod2Divider = 4;
    [SerializeField] private float lod2Distance = 200f;
    [SerializeField] private float lod2Scale = 2f;


    private RenderParams parameters;
    private MaterialPropertyBlock properties;
    private TerrainController controller;
    private Transform mainCamera;

    private GraphicsBuffer transformMatrixBuffer;
    private GraphicsBuffer meshIndexBuffer;
    private GraphicsBuffer intermediateBuffer;
    private GraphicsBuffer argumentsBuffer;
    private int resultSize;

    private int lodLevel = -1;

    private bool shouldRefresh = false;
    private bool allowRender = false;

    private void Start()
    {
        mainCamera = Camera.main.transform;

        controller = GetComponent<TerrainController>();

        if (controller.terrainReady)
            Setup();
        controller.OnTerrainReady.AddListener(Setup);

        controller.OnTerrainChange.AddListener(QueueRefresh);

        controller.OnTerrainVisible.AddListener(Enable);
        controller.OnTerrainHidden.AddListener(Disable);
    }

    private void Enable()
    {
        allowRender = false;

        // restore saved memory
        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, resultSize * resultSize, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, "_TransformMatrices", transformMatrixBuffer);

        properties.SetBuffer("_TransformMatrices", transformMatrixBuffer);

        QueueRefresh();

        enabled = true;
    }

    private void Disable()
    {
        // save memory
        transformMatrixBuffer.Dispose();

        enabled = false;
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

        scatterShader.SetVector("_Anchor", controller.transform.position);
        scatterShader.SetFloat("_SlopeLower", Mathf.Sin(Mathf.Deg2Rad * (90f - slopeMinDegree)));
        scatterShader.SetFloat("_SlopeUpper", Mathf.Sin(Mathf.Deg2Rad * (90f - slopeMaxDegree)));


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

        parameters = new RenderParams(material);
        parameters.matProps = properties;
        parameters.worldBounds = controller.worldBound;
        parameters.shadowCastingMode = ShadowCastingMode.On;
        parameters.receiveShadows = true;

        meshIndexBuffer = mesh.GetIndexBuffer();

        argumentsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        var arguments = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        arguments[0].baseVertexIndex = mesh.GetBaseVertex(0);
        arguments[0].indexCountPerInstance = mesh.GetIndexCount(0);
        arguments[0].startIndex = mesh.GetIndexStart(0);
        arguments[0].startInstance = 0;
        argumentsBuffer.SetData(arguments);

        intermediateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));

        setShader = Instantiate(setShader);
        setKernel = setShader.FindKernel("Set");
        setShader.SetBuffer(setKernel, "_IndirectArgs", argumentsBuffer);
        setShader.SetBuffer(setKernel, "_Intermediate", intermediateBuffer);


        QueueRefresh();
    }

    private void UpdateLOD()
    {
        float distance = Vector3.Distance(transform.position + transform.localScale * 0.5f, mainCamera.position);
        bool lodChanged = false;

        if (distance < lod1Distance)
        {
            lodChanged = lodLevel != 0;
            lodLevel = 0;
        }
        else if (distance < lod2Distance)
        {
            lodChanged = lodLevel != 1;
            lodLevel = 1;
        }
        else
        {
            lodChanged = lodLevel != 2;
            lodLevel = 2;
        }

        if (lodChanged)
        {
            int jump = 1;
            float jumpScale = 1f;

            switch (lodLevel)
            {
                case 2:
                    jump = lod2Divider;
                    jumpScale = lod2Scale;
                    break;
                case 1:
                    jump = lod1Divider;
                    jumpScale = lod1Scale;
                    break;
                default:
                    jump = 1;
                    jumpScale = 1f;
                    break;
            }

            properties.SetInt("_Jump", jump);
            properties.SetFloat("_JumpScale", jumpScale);

            setShader.SetInt("_Jump", jump);
            setShader.Dispatch(setKernel, 1, 1, 1);
        }
    }

    private void Update()
    {
        if (!allowRender)
            return;

        UpdateLOD();

        properties.SetFloat("_WindFrequency", windFrequency);
        properties.SetFloat("_WindAmplitude", windAmplitude);

        Graphics.RenderPrimitivesIndexedIndirect(parameters, MeshTopology.Triangles, meshIndexBuffer, argumentsBuffer);
    }

    private void OnDestroy()
    {
        transformMatrixBuffer?.Dispose();
        argumentsBuffer?.Dispose();
        intermediateBuffer?.Dispose();
    }

    public void QueueRefresh()
    {
        shouldRefresh = true;
    }

    private void ExecuteRefresh()
    {
        transformMatrixBuffer.SetCounterValue(0);
        scatterShader.Dispatch(scatterKernel, threadGroups, threadGroups, 1);
        GraphicsBuffer.CopyCount(transformMatrixBuffer, intermediateBuffer, 0);
        setShader.Dispatch(setKernel, 1, 1, 1);

        allowRender = true;
    }

    private void LateUpdate()
    {
        if (shouldRefresh)
        {
            ExecuteRefresh();
            shouldRefresh = false;
        }
    }
}
