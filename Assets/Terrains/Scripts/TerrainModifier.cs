using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainModifier : MonoBehaviour
{

    // General state
    private const float area = TerrainCoordinator.area;
    private const int meshSize = TerrainCoordinator.meshSize;
    private const int quadSize = meshSize * 2 + 1;
    private TerrainCoordinator coordinator;

    #region Configuration Region

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        SetupModify();

        SetupProject();
    }

    private void OnDestroy()
    {
        operationBuffer?.Dispose();
        projectBuffer?.Dispose();
    }

    private void SetupShader(ref ComputeShader shader, int kernelIndex)
    {
        shader = Instantiate(shader);

        shader.SetFloat("_Area", area);
        shader.SetInt("_Size", meshSize);
        shader.SetInt("_QuadSize", quadSize);
        shader.SetInt("_MeshSection", Mathf.CeilToInt(quadSize / 32f));
    }

    private TerrainController[] SetArea(ComputeShader shader, int kernelIndex, (int x, int z) region)
    {

        shader.SetInt("_Stride", TerrainController.VertexBufferStride);
        shader.SetInt("_PositionOffset", TerrainController.VertexPositionAttributeOffset);
        shader.SetInt("_NormalOffset", TerrainController.VertexNormalAttributeOffset);
        shader.SetInt("_BaseOffset", TerrainController.VertexBaseAttributeOffset);
        shader.SetInt("_ModifyOffset", TerrainController.VertexModifyAttributeOffset);

        TerrainController[] controllers = new TerrainController[4];
        
        TerrainController controllerBL = coordinator.controllers[region];
        shader.SetBuffer(kernelIndex, "_VerticesBL", controllerBL.vertexBuffer);
        controllers[0] = controllerBL;

        TerrainController controllerBR = coordinator.controllers[(region.x + 1, region.z)];
        shader.SetBuffer(kernelIndex, "_VerticesBR", controllerBR.vertexBuffer);
        controllers[1] = controllerBR;

        TerrainController controllerTL = coordinator.controllers[(region.x, region.z + 1)];
        shader.SetBuffer(kernelIndex, "_VerticesTL", controllerTL.vertexBuffer);
        controllers[2] = controllerTL;

        TerrainController controllerTR = coordinator.controllers[(region.x + 1, region.z + 1)];
        shader.SetBuffer(kernelIndex, "_VerticesTR", controllerTR.vertexBuffer);
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
    [SerializeField] private ComputeShader applyOperationsShader;
    private int applyOperationsKernel;

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
        applyOperationsKernel = applyOperationsShader.FindKernel("ApplyOperations");
        SetupShader(ref applyOperationsShader, applyOperationsKernel);
        
        operationBuffer = new ComputeBuffer(modifyBufferCount, sizeof(float) * 4 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        applyOperationsShader.SetBuffer(applyOperationsKernel, "_Operations", operationBuffer);
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

        applyOperationsShader.SetInt("_Count", index);

        if (targetRegion != currentModifyRegion)
        {
            currentModifyRegion = targetRegion;
            currentModifyControllers = SetArea(applyOperationsShader, applyOperationsKernel, currentModifyRegion.Value);
        }

        applyOperationsShader.Dispatch(applyOperationsKernel, 1, 1, 1);

        foreach (TerrainController controller in currentModifyControllers)
            controller.OnTerrainChange.Invoke();
    }

    #endregion

    #region Project Section

    [Header("Project Section")]
    [SerializeField] private ComputeShader applyProjectionsShader;
    private int applyProjectionsKernel;
    [SerializeField] private ComputeShader projectShader;
    private int projectExecuteKernel;
    private int projectClearKernel;
    private int projectConvertKernel;
    [SerializeField] private GraphicsBuffer projectBuffer;

    private Matrix4x4 projectionMatrix;
    private Quaternion projectionRotation;
    private Vector3 projectionScale;
    private Vector3 projectionPosition;
    private float projectionHalfPitch;

    [SerializeField] private int projectBatchSize = 1;

    private Queue<(int x, int z)> projectQueue = new Queue<(int x, int y)>();

    private void SetupProject() 
    {   
        projectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, quadSize * quadSize, sizeof(float) * 3);

        applyProjectionsKernel = applyProjectionsShader.FindKernel("ApplyProjections");
        SetupShader(ref applyProjectionsShader, applyProjectionsKernel);

        applyProjectionsShader.SetBuffer(applyProjectionsKernel, "_Depth", projectBuffer);
        applyProjectionsShader.SetFloat("_InvalidMin", -1000000000);
        applyProjectionsShader.SetFloat("_InvalidMax",  1000000000);
        
        projectExecuteKernel = projectShader.FindKernel("Project");
        projectClearKernel = projectShader.FindKernel("Clear");
        projectConvertKernel = projectShader.FindKernel("Convert");

        projectShader = Instantiate(projectShader);

        projectShader.SetFloat("_InvalidMin", -1000000000);
        projectShader.SetFloat("_InvalidMax",  1000000000);
        projectShader.SetInts("_Dimensions", quadSize, quadSize);
        
        projectShader.SetBuffer(projectExecuteKernel, "_Depth", projectBuffer);
        projectShader.SetBuffer(projectClearKernel, "_Depth", projectBuffer);
        projectShader.SetBuffer(projectConvertKernel, "_Depth", projectBuffer);

        projectionMatrix = Matrix4x4.Ortho(-1f, 1f, -1f, 1f, -1f, 1f);
        projectionHalfPitch = area / meshSize * 0.5f;
        projectionPosition = new Vector3(0f, 0f, 0f);
        projectionRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        projectionScale = new Vector3((area + projectionHalfPitch) * 2f, (area + projectionHalfPitch) * 2f, 1f);
    }

    public void QueueProject((int x, int z) region)
    {
        if (this == null) return;

        if (!projectQueue.Contains(region))
            projectQueue.Enqueue(region);

        enabled = true;
    }

    private HashSet<TerrainProjector> tempProjectors = new();
    private void PerformProject(TerrainProjector projector)
    {
        if (tempProjectors.Contains(projector)) return;
        tempProjectors.Add(projector);

        if (projector.mode == TerrainMask.Unset) return;

        // Make sure _WorldToClip already set before this

        projectShader.SetInt("_Mode", (int)projector.mode);

        projectShader.SetMatrix("_LocalToWorld", projector.currentLocalToWorld);

        projectShader.SetBuffer(projectExecuteKernel, "_Vertices",       projector.filter.sharedMesh.GetVertexBuffer(0));
        projectShader.SetInt(                         "_Stride",         projector.filter.sharedMesh.GetVertexBufferStride(0));
        projectShader.SetInt(                         "_PositionOffset", projector.filter.sharedMesh.GetVertexAttributeOffset(VertexAttribute.Position));

        int triangleCount = (int)projector.filter.sharedMesh.GetIndexCount(0) / 3;
        projectShader.SetBuffer(projectExecuteKernel, "_Indices",     projector.filter.sharedMesh.GetIndexBuffer());
        projectShader.SetInt(                         "_NumTriangle", triangleCount);
        
        projectShader.Dispatch(projectExecuteKernel, Mathf.CeilToInt(triangleCount / 512f), 1, 1);
    }

    private void ExecuteProject()
    {
        for (int i = 0; i < projectBatchSize && projectQueue.TryDequeue(out (int x, int z) region); i++)
        {

            (int x, int z) = region;
            TerrainController[] currentProjectControllers = SetArea(applyProjectionsShader, applyProjectionsKernel, region);

            projectShader.Dispatch(projectClearKernel, Mathf.CeilToInt((float)quadSize / 32), Mathf.CeilToInt((float)quadSize / 32), 1);

            projectionPosition.x = x * area - projectionHalfPitch;
            projectionPosition.z = z * area - projectionHalfPitch;
            projectShader.SetMatrix("_WorldToClip", projectionMatrix * Matrix4x4.Inverse(Matrix4x4.TRS(projectionPosition, projectionRotation, projectionScale)));
            
            tempProjectors.Clear();
            foreach (TerrainController controller in currentProjectControllers)
                foreach (TerrainProjector projector in controller.projectors)
                    PerformProject(projector);
            
            projectShader.Dispatch(projectConvertKernel, Mathf.CeilToInt((float)quadSize / 32), Mathf.CeilToInt((float)quadSize / 32), 1);

            applyProjectionsShader.Dispatch(applyProjectionsKernel, 1, 1, 1);

            foreach (TerrainController controller in currentProjectControllers)
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