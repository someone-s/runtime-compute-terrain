using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using System;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainIntersector : MonoBehaviour {

    private class Instance
    {
        public ComputeShader shader;
        public ComputeBuffer requestBuffer;
        public ComputeBuffer resultBuffer;
    }

    // General state
    private static int bufferCount = 10;
    private static float area => TerrainCoordinator.area;
    private static int meshSize => TerrainCoordinator.meshSize;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;

    #region Configuration Region
    //private (int x, int z)? currentRegion = null;

    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();


    }

    private void OnDestroy()
    {
        foreach (var instance in instances)
        {
            instance.requestBuffer.Dispose();
            instance.resultBuffer.Dispose();
        }
    }

    private Instance CreateInstance()
    {
        ComputeShader shader = Instantiate(computeShader);

        int findIntersectKernelIndex = shader.FindKernel("FindIntersect");

        shader.SetFloat("_Area", area);
        shader.SetInt("_Size", meshSize);
        shader.SetInt("_IntersectSection", Mathf.CeilToInt(meshSize * 2 / 32f));

        ComputeBuffer requestBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 6 + sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        shader.SetBuffer(findIntersectKernelIndex, "_Requests", requestBuffer);

        ComputeBuffer resultBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 3 + sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        shader.SetBuffer(findIntersectKernelIndex, "_Results", resultBuffer);

        return new Instance
        {
            shader = shader,
            requestBuffer = requestBuffer,
            resultBuffer = resultBuffer
        };
    }

    private void SetIntersectArea(Instance instance, (int x, int z) targetRegion) {
        int findIntersectKernelIndex = instance.shader.FindKernel("FindIntersect");

        instance.shader.SetInt("_Stride", TerrainController.VertexBufferStride);
        instance.shader.SetInt("_PositionOffset", TerrainController.VertexPositionAttributeOffset);
        instance.shader.SetInt("_BaseOffset", TerrainController.VertexBaseAttributeOffset);

        GraphicsBuffer meshBL = coordinator.controllers[targetRegion].vertexBuffer;
        instance.shader.SetBuffer(findIntersectKernelIndex, "_VerticesBL", meshBL);

        GraphicsBuffer meshBR = coordinator.controllers[(targetRegion.x + 1, targetRegion.z)].vertexBuffer;
        instance.shader.SetBuffer(findIntersectKernelIndex, "_VerticesBR", meshBR);

        GraphicsBuffer meshTL = coordinator.controllers[(targetRegion.x    , targetRegion.z + 1)].vertexBuffer;
        instance.shader.SetBuffer(findIntersectKernelIndex, "_VerticesTL", meshTL);

        GraphicsBuffer meshTR = coordinator.controllers[(targetRegion.x + 1, targetRegion.z + 1)].vertexBuffer;
        instance.shader.SetBuffer(findIntersectKernelIndex, "_VerticesTR", meshTR);
    }
    #endregion

    #region Operation Region
    private Queue<((int x, int y) region, Request operation)> requestQueue = new Queue<((int x, int y) region, Request operation)>(bufferCount);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct Request
    {
        public Vector3 rayOrigin { get; set; }
        public Vector3 rayDirection { get; set; }
        public uint requestID { get; set; }
        public uint useBase { get; set; }
    }

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
    
    private Stack<Instance> instances = new();

    private Dictionary<uint, Action<Vector3?>> callbacks = new Dictionary<uint, Action<Vector3?>>(bufferCount);

    public void QueueIntersect((int x, int y) region, Vector3 origin, Vector3 direction, bool useBase, Action<Vector3?> callback)
    {
        uint requestID = IndexManager.GetIndex();

        requestQueue.Enqueue((
            region,
            new Request
            {
                rayOrigin = origin,
                rayDirection = direction,
                requestID = requestID,
                useBase = useBase ? 1u : 0u
            }
        ));

        callbacks.Add(requestID, callback);

        enabled = true;
    }

    private void ExecuteIntersect()
    {
        if (!requestQueue.TryDequeue(out ((int x, int y) region, Request request) first))
            return;

        if (!instances.TryPop(out Instance instance))
            instance = CreateInstance();

        NativeArray<Request> requestDestination = instance.requestBuffer.BeginWrite<Request>(0, bufferCount);

        (int x, int z) targetRegion = first.region;
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

        instance.requestBuffer.EndWrite<Request>(index);

        instance.shader.SetInt(Shader.PropertyToID("_Count"), index);

        SetIntersectArea(instance, targetRegion);

        instance.shader.Dispatch(instance.shader.FindKernel("FindIntersect"), 1, 1, 1);
        AsyncGPUReadback.Request(instance.resultBuffer, index * instance.resultBuffer.stride, 0, (readback) =>
        {
            NativeArray<Result> results = readback.GetData<Result>();
            for (int r = 0; r < results.Length; r++)
            {
                Result result = results[r];

                if (!callbacks.Remove(result.requestID, out Action<Vector3?> callback))
                    continue;

                IndexManager.ReturnIndex(result.requestID);

                if (result.hit == 1)
                {
                    Vector3 globalPosition = result.position;
                    globalPosition.x += targetRegion.x * area;
                    globalPosition.z += targetRegion.z * area;
                    callback(globalPosition);
                }
                else
                    callback(null);
            }
            results.Dispose();

            instances.Push(instance);
        });
    }
    private void LateUpdate()
    {
        while (requestQueue.Count > 0)
            ExecuteIntersect();

        if (requestQueue.Count <= 0)
        enabled = false;
    }
    #endregion

}