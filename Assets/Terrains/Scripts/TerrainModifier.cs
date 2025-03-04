using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainModifier : MonoBehaviour
{

    // General state
    private static int bufferCount = 50;
    private static int meshSize => TerrainController.meshSize;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;
    private float area => coordinator.area;

    #region Configuration Region

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        computeShader = Instantiate(computeShader);


        computeShader.SetInt(Shader.PropertyToID("size"), meshSize);
        computeShader.SetInt(Shader.PropertyToID("meshSection"), Mathf.CeilToInt(((float)(meshSize * 2 + 1)) / 32f));

        SetupModify();

        SetupProject();
    }

    private void OnDestroy()
    {
        operationBuffer.Dispose();
    }

    private void SetArea(int kernelIndex, (int x, int z) region)
    {

        computeShader.SetInt(Shader.PropertyToID("stride"), TerrainController.VertexBufferStride);
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), TerrainController.VertexNormalAttributeOffset);

        TerrainController controllerBL = coordinator.controllers[region];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("verticesBL"), controllerBL.graphicsBuffer);
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("modifyBL"), controllerBL.deformBuffer);

        TerrainController controllerBR = coordinator.controllers[(region.x + 1, region.z)];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("verticesBR"), controllerBR.graphicsBuffer);
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("modifyBR"), controllerBR.deformBuffer);

        TerrainController controllerTL = coordinator.controllers[(region.x, region.z + 1)];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("verticesTL"), controllerTL.graphicsBuffer);
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("modifyTL"), controllerTL.deformBuffer);

        TerrainController controllerTR = coordinator.controllers[(region.x + 1, region.z + 1)];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("verticesTR"), controllerTR.graphicsBuffer);
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("modifyTR"), controllerTR.deformBuffer);

    }
    #endregion

    #region Modify Section
    private ComputeBuffer operationBuffer;
    private (int x, int z)? currentRegion = null;

    private Queue<((int x, int z) region, Operation operation)> operationQueue = new Queue<((int x, int z) region, Operation operation)>(bufferCount);

    public enum OperationType
    {
        Add = 1,
        Subtract = 2,
        Level = 3,
        Smooth = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Operation
    {
        public Vector2 position { get; set; } // niormalized
        public float radius { get; set; } // normalized
        public float parameter { get; set; } // arbitary space
        public uint type { get; set; }
    }

    private void SetupModify()
    {
        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");
        
        operationBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 4 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("operations"), operationBuffer);
    }

    public void QueueModify((int x, int y) region, Vector2 normalPosition, float normalRadius, float operationParameter, OperationType operationType)
    {
        operationQueue.Enqueue((
            region,
            new Operation
            {
                position = normalPosition,
                radius = normalRadius,
                parameter = operationParameter,
                type = (uint)operationType
            }
        ));

        enabled = true;
    }

    private void ExecuteModify()
    {
        if (!operationQueue.TryDequeue(out ((int x, int y) region, Operation operation) first))
            return;

        NativeArray<Operation> operationDestination = operationBuffer.BeginWrite<Operation>(0, bufferCount);

        (int x, int y) targetRegion = first.region;
        operationDestination[0] = first.operation;

        int index = 1;
        for (; index < bufferCount; index++)
        {
            if (!operationQueue.TryPeek(out ((int x, int y) region, Operation operation) follow))
                break;

            if (follow.region != targetRegion)
                break;

            operationQueue.Dequeue();
            operationDestination[index] = follow.operation;
        }

        operationBuffer.EndWrite<Operation>(index);

        computeShader.SetInt(Shader.PropertyToID("count"), index);

        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");

        if (targetRegion != currentRegion)
        {
            currentRegion = targetRegion;
            SetArea(modifyMeshKernelIndex, currentRegion.Value);
        }

        computeShader.Dispatch(modifyMeshKernelIndex, 32, 32, 1);
    }

    #endregion

    #region Project Section

    [SerializeField] private float start = 500f;
    [SerializeField] private float depth = 1000f;
    [SerializeField] private float ignore = 0.05f;


    [SerializeField] private Transform cameraRig;
    [SerializeField] private Camera mandateCamera;
    private RenderTexture mandateTexture;
    [SerializeField] private Camera minimumCamera;
    private RenderTexture minimumTexture;
    [SerializeField] private Camera maximumCamera;
    private RenderTexture maximumTexture;


    private Queue<(int x, int z)> projectQueue = new Queue<(int x, int y)>(bufferCount);

    private void SetupProject() 
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
            width: TerrainController.meshSize * 2 + 1,
            height: TerrainController.meshSize * 2 + 1,
            colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.None,
            depthStencilFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat
        );

        mandateTexture = new RenderTexture(descriptor);
        minimumTexture = new RenderTexture(descriptor);
        maximumTexture = new RenderTexture(descriptor);

        int applyModifiersKernelIndex = computeShader.FindKernel("ApplyModifiers");

        computeShader.SetTexture(applyModifiersKernelIndex, Shader.PropertyToID("mandateRT"), mandateTexture);
        computeShader.SetTexture(applyModifiersKernelIndex, Shader.PropertyToID("minimumRT"), minimumTexture);
        computeShader.SetTexture(applyModifiersKernelIndex, Shader.PropertyToID("maximumRT"), maximumTexture);

        computeShader.SetFloat(Shader.PropertyToID("start"), start);
        computeShader.SetFloat(Shader.PropertyToID("depth"), depth);
        computeShader.SetFloat(Shader.PropertyToID("ignore"), ignore);

        mandateCamera.orthographicSize = area + (area / TerrainController.meshSize * 0.5f);
        mandateCamera.nearClipPlane = 0f;
        mandateCamera.farClipPlane = depth;
        mandateCamera.transform.localPosition = new Vector3(0f, start, 0f);
        mandateCamera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        mandateCamera.targetTexture = mandateTexture;
        
        minimumCamera.orthographicSize = area + (area / TerrainController.meshSize * 0.5f);
        minimumCamera.nearClipPlane = 0f;
        minimumCamera.farClipPlane = depth;
        minimumCamera.transform.localPosition = new Vector3(0f, start, 0f);
        minimumCamera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        minimumCamera.targetTexture = minimumTexture;

        maximumCamera.orthographicSize = area + (area / TerrainController.meshSize * 0.5f);
        maximumCamera.nearClipPlane = 0f;
        maximumCamera.farClipPlane = depth;
        maximumCamera.transform.localPosition = new Vector3(0f, -start, 0f);
        maximumCamera.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
        maximumCamera.targetTexture = maximumTexture;
    }

    public void QueueProject((int x, int z) region)
    {
        if (!projectQueue.Contains(region))
            projectQueue.Enqueue(region);

        enabled = true;
    }

    private void ExecuteProject()
    {
        for (int i = 0; i < bufferCount && projectQueue.TryDequeue(out (int x, int z) region); i++)
        {

            (int x, int z) = region;

            cameraRig.localPosition = new Vector3((x + 1) * area, 0f, (z + 1) * area);
            mandateCamera.Render();
            minimumCamera.Render();
            maximumCamera.Render();


            int applyModifiersKernelIndex = computeShader.FindKernel("ApplyModifiers");

            SetArea(applyModifiersKernelIndex, region);

            computeShader.Dispatch(applyModifiersKernelIndex, 32, 32, 1);
        }
    }
    #endregion

    private void LateUpdate()
    {
        if (operationQueue.Count > 0)
            ExecuteModify();
        if (projectQueue.Count > 0)
            ExecuteProject();

        if (operationQueue.Count == 0 && projectQueue.Count == 0)
            enabled = false;
    }
}