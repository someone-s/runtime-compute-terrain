using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainController))]
public class BladeGrassController : MonoBehaviour
{
    [SerializeField] private ComputeShader scatterShader;
    [SerializeField] private Material material;
    [SerializeField] private Mesh grassMesh;
    

    [Header("Grass Setting")]
    [SerializeField] private float minBladeHeight = 1f;
    [SerializeField] private float maxBladeHeight = 2f;
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
    private int terrainTriangleCount;

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

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, terrainTriangleCount * multiplier, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("TransformMatrices"), transformMatrixBuffer);

        scatterShader.SetInt(Shader.PropertyToID("multiplier"), multiplier);
        properties.SetBuffer(Shader.PropertyToID("TransformMatrices"), transformMatrixBuffer);

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

        scatterShader.SetInt(Shader.PropertyToID("multiplier"), multiplier);

        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("TerrainVertices"), controller.vertexBuffer);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("TerrainTriangles"), controller.triangleBuffer);
        scatterShader.SetInt(Shader.PropertyToID("terrainStride"), TerrainController.VertexBufferStride);
        scatterShader.SetInt(Shader.PropertyToID("terrainPositionOffset"), TerrainController.VertexPositionAttributeOffset);
        scatterShader.SetInt(Shader.PropertyToID("terrainNormalOffset"), TerrainController.VertexNormalAttributeOffset);

        terrainTriangleCount = controller.triangleBuffer.count / 3;

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, terrainTriangleCount * multiplier, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("TransformMatrices"), transformMatrixBuffer);
        scatterShader.SetInt(Shader.PropertyToID("terrainTriangleCount"), terrainTriangleCount);

        scatterShader.SetVector(Shader.PropertyToID("anchor"), controller.transform.position);
        scatterShader.SetFloat(Shader.PropertyToID("area"), TerrainCoordinator.area);
        scatterShader.SetFloat(Shader.PropertyToID("minBladeHeight"), minBladeHeight);
        scatterShader.SetFloat(Shader.PropertyToID("maxBladeHeight"), maxBladeHeight);
        scatterShader.SetFloat(Shader.PropertyToID("minOffset"), -TerrainCoordinator.area / TerrainCoordinator.meshSize / 2f);
        scatterShader.SetFloat(Shader.PropertyToID("maxOffset"), TerrainCoordinator.area / TerrainCoordinator.meshSize / 2f);
        scatterShader.SetFloat(Shader.PropertyToID("scale"), scale);
        
        scatterShader.GetKernelThreadGroupSizes(scatterKernel, out uint threadGroupSize, out _, out _);
        threadGroups = Mathf.CeilToInt((float)terrainTriangleCount / threadGroupSize);
        
        properties = new MaterialPropertyBlock();
        properties.SetBuffer(Shader.PropertyToID("TransformMatrices"), transformMatrixBuffer);
        properties.SetBuffer(Shader.PropertyToID("Vertices"), grassMesh.GetVertexBuffer(0));
        properties.SetInt(Shader.PropertyToID("stride"), grassMesh.GetVertexBufferStride(0));
        properties.SetInt(Shader.PropertyToID("positionOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Position));
        properties.SetInt(Shader.PropertyToID("normalOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Normal));
        properties.SetInt(Shader.PropertyToID("uvOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0));

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
        int pick = terrainTriangleCount * multiplier;
        if (distance > lod1Distance)
            pick /= lod1Divider;
        if (distance > lod2Distance)
            pick /= lod2Divider;
        if (pick < 1)
            return;

        //Debug.Log($"{terrainTriangleCount * multiplier / pick} {terrainTriangleCount * multiplier} {pick}");
        int jump = terrainTriangleCount * multiplier / pick;
        properties.SetInt(Shader.PropertyToID("jump"), jump);
        float jumpScale = 1f + (jump - 1) * 0.5f;
        properties.SetFloat(Shader.PropertyToID("jumpScale"), jumpScale);

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
