using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainController))]
public class InstanceController : MonoBehaviour
{
    [SerializeField] private ComputeShader scatterShader;
    [SerializeField] private Material material;
    [SerializeField] private Mesh grassMesh;
    

    [Header("Instance Setting")]
    [SerializeField, Min(0.05f)] private float density = 4;
    [SerializeField] private float minHeight = 1f;
    [SerializeField] private float maxHeight = 2f;
    [SerializeField] private float scale = 2f;

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

    private int threadGroups;
    private int scatterKernel;

    private RenderParams parameters;
    private MaterialPropertyBlock properties;
    private TerrainController controller;
    
    private GraphicsBuffer transformMatrixBuffer;
    private GraphicsBuffer grassIndexBuffer;
    private int resultSize;

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

        // restore saved memory
        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, resultSize * resultSize, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);

        properties.SetBuffer(Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);

        QueueRefresh();

        enabled = true;
    }

    private void Disable()
    {
        // save memory
        transformMatrixBuffer.Dispose();     

        enabled = false;
    }


    private void Setup() {

        scatterShader = Instantiate(scatterShader);

        scatterKernel = scatterShader.FindKernel("Scatter");

        int meshSize = TerrainCoordinator.meshSize;
        resultSize = Mathf.CeilToInt((float)meshSize * density);

        scatterShader.SetInt(Shader.PropertyToID("_ResultSize"), resultSize);
        scatterShader.SetFloat(Shader.PropertyToID("_ResultSizeInverse"), 1f / resultSize);

        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TerrainVertices"), controller.vertexBuffer);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TerrainTriangles"), controller.triangleBuffer);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainSize"), meshSize);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainStride"), TerrainController.VertexBufferStride);
        scatterShader.SetInt(Shader.PropertyToID("_TerrainPositionOffset"), TerrainController.VertexPositionAttributeOffset);

        scatterShader.SetVector(Shader.PropertyToID("_Anchor"), controller.transform.position);

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, resultSize * resultSize, sizeof(float) * 16);
        scatterShader.SetBuffer(scatterKernel, Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);

        scatterShader.SetFloat(Shader.PropertyToID("_Scale"), scale);
        scatterShader.SetFloat(Shader.PropertyToID("_MinHeight"), minHeight);
        scatterShader.SetFloat(Shader.PropertyToID("_MaxHeight"), maxHeight);
        
        scatterShader.GetKernelThreadGroupSizes(scatterKernel, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
        Assert.IsTrue(threadGroupSizeX == threadGroupSizeY, "Scatter Shader numthread x and y must equal");
        Assert.IsTrue(threadGroupSizeZ == 1, "Scatter Shader numthread z must be 1");
        threadGroups = Mathf.CeilToInt((float)resultSize / threadGroupSizeX);
        
        properties = new MaterialPropertyBlock();
        properties.SetBuffer(Shader.PropertyToID("_TransformMatrices"), transformMatrixBuffer);
        properties.SetBuffer(Shader.PropertyToID("_Vertices"), grassMesh.GetVertexBuffer(0));
        properties.SetInt(Shader.PropertyToID("_Stride"), grassMesh.GetVertexBufferStride(0));
        properties.SetInt(Shader.PropertyToID("_PositionOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Position));
        properties.SetInt(Shader.PropertyToID("_NormalOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Normal));
        properties.SetInt(Shader.PropertyToID("_TangentOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.Tangent));
        properties.SetInt(Shader.PropertyToID("_UVOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0));
        properties.SetInt(Shader.PropertyToID("_StaticLightmapUVOffset"), grassMesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1));

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
        int pick = resultSize * resultSize;
        float jumpScale = 1f;
        if (distance > lod1Distance)
        {
            pick /= lod1Divider;
            jumpScale *= lod1Scale;
        }
        if (distance > lod2Distance)
        {
            pick /= lod2Divider;
            jumpScale *= lod2Scale;
        }
        if (pick < 1)
            return;

        int jump = resultSize * resultSize / pick;
        properties.SetInt(Shader.PropertyToID("_Jump"), jump);
        properties.SetFloat(Shader.PropertyToID("_JumpScale"), jumpScale);
        properties.SetFloat(Shader.PropertyToID("_WindFrequency"), windFrequency);
        properties.SetFloat(Shader.PropertyToID("_WindAmplitude"), windAmplitude);

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
        scatterShader.Dispatch(scatterKernel, threadGroups, threadGroups, 1);

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
