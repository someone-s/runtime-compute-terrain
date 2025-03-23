using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using System;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainIntersector : MonoBehaviour {


    // General state
    private static int bufferCount = 10;
    private static int meshSize => TerrainCoordinator.meshSize;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;

    #region Configuration Region
    private (int x, int z)? currentRegion = null;

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        computeShader = Instantiate(computeShader);

        int findIntersectKernelIndex = computeShader.FindKernel("FindIntersect");

        computeShader.SetInt(Shader.PropertyToID("size"), meshSize);
        computeShader.SetInt(Shader.PropertyToID("intersectSection"), Mathf.CeilToInt(((float)meshSize * 2) / 32f));

        requestBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 6 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("requests"), requestBuffer);

        resultBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 3 + sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("results"), resultBuffer);
    }

    private void OnDestroy()
    {
        resultBuffer.Dispose();
    }

    private void SetIntersectArea() {
        int findIntersectKernelIndex = computeShader.FindKernel("FindIntersect");

        computeShader.SetInt(Shader.PropertyToID("stride"), TerrainController.VertexBufferStride);
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), TerrainController.VertexNormalAttributeOffset);

        GraphicsBuffer meshBL = coordinator.controllers[currentRegion.Value].vertexBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesBL"), meshBL);

        GraphicsBuffer meshBR = coordinator.controllers[(currentRegion.Value.x + 1, currentRegion.Value.z)].vertexBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesBR"), meshBR);

        GraphicsBuffer meshTL = coordinator.controllers[(currentRegion.Value.x    , currentRegion.Value.z + 1)].vertexBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesTL"), meshTL);

        GraphicsBuffer meshTR = coordinator.controllers[(currentRegion.Value.x + 1, currentRegion.Value.z + 1)].vertexBuffer;
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("verticesTR"), meshTR);

    }
    #endregion

    #region Operation Region
    private ComputeBuffer requestBuffer;
    private Queue<((int x, int y) region, Request operation)> requestQueue = new Queue<((int x, int y) region, Request operation)>(bufferCount);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct Request
    {
        public Vector3 rayOrigin { get; set; }
        public Vector3 rayDirection { get; set; }
        public uint requestID { get; set; }
    }

    private ComputeBuffer resultBuffer;

    [StructLayout(LayoutKind.Sequential)]
    private struct Result
    {
        public Vector3 position { get; private set; }
        public uint hit { get; private set; }
        public uint requestID { get; private set; }
    }

    private static class IndexManager
    {
        private static uint lastIndex = 0;
        private static Stack<uint> freeIndices = new Stack<uint>(bufferCount); 

        public static uint GetIndex() {
            if (freeIndices.TryPop(out uint existingValue)) {
                return existingValue;
            }
            else {
                uint newValue = lastIndex;
                lastIndex++;
                return newValue;
            }
        }

        public static void ReturnIndex(uint recycle) {
            freeIndices.Push(recycle);
        }
    }
    
    private Dictionary<uint, Action<Vector3?>> callbacks = new Dictionary<uint, Action<Vector3?>>(bufferCount);

    public void QueueIntersect((int x, int y) region, Vector3 origin, Vector3 direction, Action<Vector3?> callback)
    {
        uint requestID = IndexManager.GetIndex();

        requestQueue.Enqueue((
            region,
            new Request
            {
                rayOrigin = origin,
                rayDirection = direction,
                requestID = requestID
            }
        ));

        callbacks.Add(requestID, callback);

        enabled = true;
    }

    private void ExecuteIntersect()
    {
        if (!requestQueue.TryDequeue(out ((int x, int y) region, Request request) first))
            return;

        NativeArray<Request> requestDestination = requestBuffer.BeginWrite<Request>(0, bufferCount);

        (int x, int y) targetRegion = first.region;
        requestDestination[0] = first.request;

        int index = 1;
        for (; index < bufferCount; index++) {
            if (!requestQueue.TryPeek(out ((int x, int y) region, Request request) follow))
                break;

            if (follow.region != targetRegion)
                break;

            requestQueue.Dequeue();
            requestDestination[index] = follow.request;
        }

        requestBuffer.EndWrite<Request>(index);

        computeShader.SetInt(Shader.PropertyToID("count"), index);

        if (targetRegion != currentRegion) {
            currentRegion = targetRegion;
            SetIntersectArea();
        }

        computeShader.Dispatch(computeShader.FindKernel("FindIntersect"), 32, 32, 1);
        AsyncGPUReadback.Request(resultBuffer, (readback) =>
        {
            NativeArray<Result> results = readback.GetData<Result>();
            for (int r = 0; r < index; r++)
            {
                Result result = results[r];
                if (!callbacks.Remove(result.requestID, out Action<Vector3?> callback))
                    return;

                IndexManager.ReturnIndex(result.requestID);

                if (result.hit == 1)
                {
                    Vector3 globalPosition = result.position;
                    globalPosition.x += currentRegion.Value.x;
                    globalPosition.z += currentRegion.Value.z;
                    callback(globalPosition);
                }
                else
                    callback(null);
            }
            results.Dispose();
        });
    }
    private void LateUpdate()
    {
        ExecuteIntersect();

        enabled = false;
    }
    #endregion

}