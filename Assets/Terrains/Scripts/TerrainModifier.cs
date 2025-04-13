using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using Unity.Mathematics;
using System.IO;
using System.Linq;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainModifier : MonoBehaviour
{

    // General state
    private static float area => TerrainCoordinator.area;
    private static int meshSize => TerrainCoordinator.meshSize;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;

    #region Configuration Region

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        computeShader = Instantiate(computeShader);


        computeShader.SetFloat(Shader.PropertyToID("_Area"), area);
        computeShader.SetInt(Shader.PropertyToID("_Size"), meshSize);
        computeShader.SetInt(Shader.PropertyToID("_MeshSection"), Mathf.CeilToInt(((float)(meshSize * 2 + 1)) / 32f));

        SetupModify();

        SetupProject();
    }

    private void OnDestroy()
    {
        operationBuffer.Dispose();
    }

    private TerrainController[] SetArea(int kernelIndex, (int x, int z) region)
    {

        computeShader.SetInt(Shader.PropertyToID("_Stride"), TerrainController.VertexBufferStride);
        computeShader.SetInt(Shader.PropertyToID("_PositionOffset"), TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("_NormalOffset"), TerrainController.VertexNormalAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("_BaseOffset"), TerrainController.VertexBaseAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("_ModifyOffset"), TerrainController.VertexModifyAttributeOffset);

        TerrainController[] controllers = new TerrainController[4];
        
        TerrainController controllerBL = coordinator.controllers[region];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("_VerticesBL"), controllerBL.vertexBuffer);
        controllers[0] = controllerBL;

        TerrainController controllerBR = coordinator.controllers[(region.x + 1, region.z)];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("_VerticesBR"), controllerBR.vertexBuffer);
        controllers[1] = controllerBR;

        TerrainController controllerTL = coordinator.controllers[(region.x, region.z + 1)];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("_VerticesTL"), controllerTL.vertexBuffer);
        controllers[2] = controllerTL;

        TerrainController controllerTR = coordinator.controllers[(region.x + 1, region.z + 1)];
        computeShader.SetBuffer(kernelIndex, Shader.PropertyToID("_VerticesTR"), controllerTR.vertexBuffer);
        controllers[3] = controllerTR;

        return controllers;

    }
    #endregion

    #region Modify Section
    private ComputeBuffer operationBuffer;
    private (int x, int z)? currentModifyRegion = null;
    private TerrainController[] currentModifyControllers = null;
    
    [Header("Modify Section")]
    [SerializeField] private int modifyBufferCount = 50;

    private Queue<((int x, int z) region, Operation operation)> operationQueue = new Queue<((int x, int z) region, Operation operation)>();

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
        
        operationBuffer = new ComputeBuffer(modifyBufferCount, sizeof(float) * 4 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("_Operations"), operationBuffer);
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

        NativeArray<Operation> operationDestination = operationBuffer.BeginWrite<Operation>(0, modifyBufferCount);

        (int x, int y) targetRegion = first.region;
        operationDestination[0] = first.operation;

        int index = 1;
        for (; index < modifyBufferCount; index++)
        {
            if (!operationQueue.TryPeek(out ((int x, int y) region, Operation operation) follow))
                break;

            if (follow.region != targetRegion)
                break;

            operationQueue.Dequeue();
            operationDestination[index] = follow.operation;
        }

        operationBuffer.EndWrite<Operation>(index);

        computeShader.SetInt(Shader.PropertyToID("_Count"), index);

        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");

        if (targetRegion != currentModifyRegion)
        {
            currentModifyRegion = targetRegion;
            currentModifyControllers = SetArea(modifyMeshKernelIndex, currentModifyRegion.Value);
        }

        computeShader.Dispatch(modifyMeshKernelIndex, 1, 1, 1);

        foreach (TerrainController controller in currentModifyControllers)
            controller.OnTerrainChange.Invoke();
    }

    #endregion

    #region Project Section

    [Header("Project Section")]
    [SerializeField] private float depth = 1000f;
    private float start => depth * 0.5f;
    private float ignore => 1f / depth;


    [SerializeField] private Transform cameraRig;
    [SerializeField] private Camera mandateCamera;
    private RenderTexture mandateTexture;
    [SerializeField] private Camera minimumCamera;
    private RenderTexture minimumTexture;
    [SerializeField] private Camera maximumCamera;
    private RenderTexture maximumTexture;

    [SerializeField] private ComputeShader projectShader;
    [SerializeField] private List<MeshFilter> filters;
    [SerializeField] private GraphicsBuffer projectBuffer;
    [SerializeField] private Shader shader;

    [SerializeField] private int projectBatchSize = 1;

    private Queue<(int x, int z)> projectQueue = new Queue<(int x, int y)>();

    private void SetupProject() 
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
            width: meshSize * 2 + 1,
            height: meshSize * 2 + 1,
            colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.None,
            depthStencilFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat
        );

        mandateTexture = new RenderTexture(descriptor);
        minimumTexture = new RenderTexture(descriptor);
        maximumTexture = new RenderTexture(descriptor);

        int applyModifiersKernelIndex = computeShader.FindKernel("ApplyModifiers");

        computeShader.SetTexture(applyModifiersKernelIndex, Shader.PropertyToID("_MandateRT"), mandateTexture);
        computeShader.SetTexture(applyModifiersKernelIndex, Shader.PropertyToID("_MinimumRT"), minimumTexture);
        computeShader.SetTexture(applyModifiersKernelIndex, Shader.PropertyToID("_MaximumRT"), maximumTexture);

        computeShader.SetFloat(Shader.PropertyToID("_Start"), start);
        computeShader.SetFloat(Shader.PropertyToID("_Depth"), depth);
        computeShader.SetFloat(Shader.PropertyToID("_Ignore"), ignore);

        mandateCamera.orthographicSize = area + (area / meshSize * 0.5f);
        mandateCamera.nearClipPlane = 0f;
        mandateCamera.farClipPlane = depth;
        mandateCamera.transform.localPosition = new Vector3(0f, start, 0f);
        mandateCamera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        mandateCamera.targetTexture = mandateTexture;
        mandateCamera.SetReplacementShader(shader, string.Empty);
        
        minimumCamera.orthographicSize = area + (area / meshSize * 0.5f);
        minimumCamera.nearClipPlane = 0f;
        minimumCamera.farClipPlane = depth;
        minimumCamera.transform.localPosition = new Vector3(0f, start, 0f);
        minimumCamera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        minimumCamera.targetTexture = minimumTexture;
        minimumCamera.SetReplacementShader(shader, string.Empty);

        maximumCamera.orthographicSize = area + (area / meshSize * 0.5f);
        maximumCamera.nearClipPlane = 0f;
        maximumCamera.farClipPlane = depth;
        maximumCamera.transform.localPosition = new Vector3(0f, -start, 0f);
        maximumCamera.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
        maximumCamera.targetTexture = maximumTexture;
        maximumCamera.SetReplacementShader(shader, string.Empty);

        projectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (meshSize * 2 + 1) * (meshSize * 2 + 1), sizeof(float) * 3);
        int kernel = projectShader.FindKernel("Project");
        projectShader.SetInts("_Dimensions", (meshSize * 2 + 1), (meshSize * 2 + 1));
        projectShader.SetBuffer(kernel, "_Depth", projectBuffer);
        projectShader.SetBuffer(projectShader.FindKernel("Clear"), "_Depth", projectBuffer);
        projectShader.SetBuffer(projectShader.FindKernel("Convert"), "_Depth", projectBuffer);
    }

    public void QueueProject((int x, int z) region)
    {
        if (!projectQueue.Contains(region))
            projectQueue.Enqueue(region);

        enabled = true;
    }

    private void T(string s)
    {

            float[] output = new float[(meshSize * 2 + 1) * (meshSize * 2 + 1) * 3];
            projectBuffer.GetData(output);
            string path = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "/" + s;
            Debug.Log(path);
            File.WriteAllLines(path, output.Select(v => v.ToString()));
    }

    private void ExecuteProject()
    {
        for (int i = 0; i < projectBatchSize && projectQueue.TryDequeue(out (int x, int z) region); i++)
        {

            (int x, int z) = region;

            cameraRig.localPosition = new Vector3((x + 1) * area, 0f, (z + 1) * area);
            //mandateCamera.Render();
            //minimumCamera.Render();
            //maximumCamera.Render();

            projectShader.Dispatch(projectShader.FindKernel("Clear"), Mathf.CeilToInt((float)(meshSize * 2 + 1) / 32), Mathf.CeilToInt((float)(meshSize * 2 + 1) / 32), 1);

            int kernel = projectShader.FindKernel("Project");
            projectShader.SetMatrix("_WorldToClip", mandateCamera.projectionMatrix * mandateCamera.worldToCameraMatrix);
            foreach (var filter in filters)
            {
                projectShader.SetMatrix("_LocalToWorld", filter.transform.localToWorldMatrix);
                projectShader.SetBuffer(kernel, "_Vertices", filter.sharedMesh.GetVertexBuffer(0));
                projectShader.SetBuffer(kernel, "_Indices", filter.sharedMesh.GetIndexBuffer());
                projectShader.SetInt("_Stride", filter.sharedMesh.GetVertexBufferStride(0));
                projectShader.SetInt("_PositionOffset", filter.sharedMesh.GetVertexAttributeOffset(VertexAttribute.Position));
                int triangleCount = (int)filter.sharedMesh.GetIndexCount(0) / 3;
                projectShader.SetInt("_NumTriangle", triangleCount);
                projectShader.Dispatch(kernel, Mathf.CeilToInt(triangleCount / 512), 1, 1);
            }
            projectShader.Dispatch(projectShader.FindKernel("Convert"), Mathf.CeilToInt((float)(meshSize * 2 + 1) / 32), Mathf.CeilToInt((float)(meshSize * 2 + 1) / 32), 1);

            if (Keyboard.current.xKey.isPressed)
                T("temp.txt");

            int applyModifiersKernelIndex = computeShader.FindKernel("ApplyModifiers");

            TerrainController[] currentProjectController = SetArea(applyModifiersKernelIndex, region);

            computeShader.Dispatch(applyModifiersKernelIndex, 1, 1, 1);

            foreach (TerrainController controller in currentProjectController)
                controller.OnTerrainChange.Invoke();
        }
    }
    #endregion

    private bool state = false;
    private void LateUpdate()
    {
        if (state) {
            if (operationQueue.Count > 0)
                ExecuteModify();
            else if (projectQueue.Count > 0)
                ExecuteProject();
        }
        else {
            if (projectQueue.Count > 0)
                ExecuteProject();
            else if (operationQueue.Count > 0)
                ExecuteModify();
        }
        state = !state;
        
        if (operationQueue.Count == 0 && projectQueue.Count == 0)
            enabled = false;
    }
}