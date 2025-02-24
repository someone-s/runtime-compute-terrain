using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Events;
using UnityEngine.Rendering;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainModifier : MonoBehaviour
{

    // General state
    private static int bufferCount = 50;
    private static int meshSize = 250;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;

    #region Configuration Region
    private (int x, int y)? currentRegion = null;

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        computeShader = Instantiate(computeShader);

        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");

        computeShader.SetInt(Shader.PropertyToID("size"), meshSize);
        computeShader.SetInt(Shader.PropertyToID("meshSection"), Mathf.CeilToInt(((float)(meshSize * 2 + 1)) / 32f));
        
        operationBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 4 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("operations"), operationBuffer);

    }

    private void OnDestroy()
    {
        operationBuffer.Dispose();
    }


    private void SetModifyArea() {
        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");

        computeShader.SetInt(Shader.PropertyToID("stride"), TerrainController.VertexBufferStride);
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), TerrainController.VertexNormalAttributeOffset);

        GraphicsBuffer meshBL = coordinator.controllers[currentRegion.Value].graphicsBuffer;
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("verticesBL"), meshBL);
        
        GraphicsBuffer meshBR = coordinator.controllers[(currentRegion.Value.x + 1, currentRegion.Value.y)].graphicsBuffer;
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("verticesBR"), meshBR);

        GraphicsBuffer meshTL = coordinator.controllers[(currentRegion.Value.x    , currentRegion.Value.y + 1)].graphicsBuffer;
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("verticesTL"), meshTL);

        GraphicsBuffer meshTR = coordinator.controllers[(currentRegion.Value.x + 1, currentRegion.Value.y + 1)].graphicsBuffer;
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("verticesTR"), meshTR);

    }
    #endregion

    #region Modify Section
    private ComputeBuffer operationBuffer;

    private Queue<((int x, int y) region, Operation operation)> operationQueue = new Queue<((int x, int y) region, Operation operation)>(bufferCount);

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
        for (; index < bufferCount; index++) {
            if (!operationQueue.TryPeek(out ((int x, int y) region, Operation operation) follow))
                break;

            if (follow.region != targetRegion)
                break;

            operationQueue.Dequeue();
            operationDestination[index] = follow.operation;
        }

        //NativeSlice<Operation> destinationRange = new NativeSlice<Operation>(operationDestination, 0, index);
        //NativeSlice<Operation> writerRange = new NativeSlice<Operation>(operationQueue, 0, index);
        //destinationRange.CopyFrom(writerRange);

        operationBuffer.EndWrite<Operation>(index);

        computeShader.SetInt(Shader.PropertyToID("count"), index);

        if (targetRegion != currentRegion) {
            currentRegion = targetRegion;
            SetModifyArea();
        }

        computeShader.Dispatch(computeShader.FindKernel("ModifyMesh"), 32, 32, 1);
    }

    private void LateUpdate()
    {
        if (operationQueue.Count > 0)
            ExecuteModify();

        if (operationQueue.Count == 0)
            enabled = false;
    }
    #endregion
}