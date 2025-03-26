using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainController))]
public class TreeController : MonoBehaviour
{
    [SerializeField] private ComputeShader scatterShader;
    [SerializeField] private Material material;
    [SerializeField] private Mesh grassMesh;
    

    [Header("Instance Setting")]
    [SerializeField] private int skipCount = 4;
    [SerializeField] private float minHeight = 1f;
    [SerializeField] private float maxHeight = 2f;
    [SerializeField] private float scale = 2f;


    [Header("LOD Setting")]
    [SerializeField] private int multiplier = 4;
    [SerializeField] private int lod1Divider = 4;
    [SerializeField] private float lod1Distance = 100f;
    [SerializeField] private int lod2Divider = 4;
    [SerializeField] private float lod2Distance = 200f;

    private int threadGroups;
    private int scatterKernel;

    private RenderParams parameters;
    private MaterialPropertyBlock properties;
    private TerrainController controller;
    
    private GraphicsBuffer transformMatrixBuffer;
    private GraphicsBuffer grassIndexBuffer;
    private int actualtriangleCount;

    private bool shouldRefresh = false;
    private bool allowRender = false;

    private void Start() 
    {

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

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, actualtriangleCount * multiplier, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);

        scatterShader.SetInt(Shader.PropertyToID("_Multiplier"), multiplier);
        properties.SetBuffer(Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);

        QueueRefresh();

        enabled = true;
    }

    private void Disable()
    {
        transformMatrixBuffer.Dispose();     

        enabled = false;
    }

    private void Setup() {

        scatterShader = Instantiate(scatterShader);

        scatterKernel = scatterShader.FindKernel("Scatter");

        scatterShader.SetInt(Shader.PropertyToID("_Skip"), skipCount);
        scatterShader.SetInt(Shader.PropertyToID("_Multiplier"), multiplier);

        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TerrainVertices"), controller.vertexBuffer);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TerrainTriangles"), controller.triangleBuffer);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainStride"), TerrainController.VertexBufferStride);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainPositionOffset"), TerrainController.VertexPositionAttributeOffset);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainNormalOffset"), TerrainController.VertexNormalAttributeOffset);

        int terrainTriangleCount = controller.triangleBuffer.count / 3;
        actualtriangleCount = terrainTriangleCount / skipCount;

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, actualtriangleCount * multiplier, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainTriangleCount"), terrainTriangleCount);

        scatterShader.SetVector(Shader.PropertyToID("_Anchor"), controller.transform.position);
        scatterShader.SetFloat(Shader.PropertyToID("_Area"), TerrainCoordinator.area);
        scatterShader.SetFloat(Shader.PropertyToID("_MinHeight"), minHeight);
        scatterShader.SetFloat(Shader.PropertyToID("_MaxHeight"), maxHeight);
        scatterShader.SetFloat(Shader.PropertyToID("_MinOffset"), -TerrainCoordinator.area / TerrainCoordinator.meshSize / 2f);
        scatterShader.SetFloat(Shader.PropertyToID("_MaxOffset"), TerrainCoordinator.area / TerrainCoordinator.meshSize / 2f);
        scatterShader.SetFloat(Shader.PropertyToID("_Scale"), scale);
        
        scatterShader.GetKernelThreadGroupSizes(scatterKernel, out uint threadGroupSize, out _, out _);
        threadGroups = Mathf.CeilToInt((float)actualtriangleCount / threadGroupSize);
        
        properties = new MaterialPropertyBlock();
        properties.SetBuffer(Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);
        properties.SetBuffer(Shader.PropertyToID("_Vertices"), grassMesh.GetVertexBuffer(0));
        properties.SetInt(Shader.PropertyToID("_Stride"), grassMesh.GetVertexBufferStride(0));
        properties.SetInt(Shader.PropertyToID("_PositionOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Position));
        properties.SetInt(Shader.PropertyToID("_NormalOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Normal));
        properties.SetInt(Shader.PropertyToID("_UVOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0));

        properties.SetFloat(Shader.PropertyToID("_WindFrequency"), 2f);
        properties.SetFloat(Shader.PropertyToID("_WindAmplitude"), 0.05f);

        parameters = new RenderParams(material);
        parameters.matProps = properties;
        parameters.worldBounds = controller.worldBound;
        parameters.shadowCastingMode = ShadowCastingMode.On;
        parameters.receiveShadows = true;

        grassIndexBuffer = grassMesh.GetIndexBuffer();

        QueueRefresh();
    }

    private void Update()
    {
        if (!allowRender) 
            return;

        
        float distance = Vector3.Distance(transform.position + transform.localScale * 0.5f, Camera.main.transform.position);
        int pick = actualtriangleCount * multiplier;
        if (distance > lod1Distance)
            pick /= lod1Divider;
        if (distance > lod2Distance)
            pick /= lod2Divider;
        if (pick < 1)
            return;

        int jump = actualtriangleCount * multiplier / pick;
        properties.SetInt(Shader.PropertyToID("_Jump"), jump);
        float jumpScale = 1f + (jump - 1) * 0.5f;
        properties.SetFloat(Shader.PropertyToID("_JumpScale"), jumpScale);

        Graphics.RenderPrimitivesIndexed(parameters, MeshTopology.Triangles, grassIndexBuffer, grassIndexBuffer.count, instanceCount: pick);
    }

    private void OnDestroy()
    {
        transformMatrixBuffer?.Dispose();       
    }

    public void QueueRefresh()
    {
        shouldRefresh = true;
    }

    private void ExecuteRefresh()
    {
        scatterShader.Dispatch(scatterKernel, threadGroups, 1, 1);

        allowRender = true;
    }

    private void LateUpdate()
    {
        if (shouldRefresh) {
            ExecuteRefresh();
            shouldRefresh = false;
        }
    }
}
