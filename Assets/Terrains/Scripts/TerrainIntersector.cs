using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using System;
using System.Linq;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainIntersector : MonoBehaviour
{

    private class Instance
    {
        public int findIntersectKernel;
        public ComputeShader shader;
        public ComputeBuffer requestBuffer;
        public ComputeBuffer resultBuffer;
    }

    // General state
    private static int bufferCount = 10;
    private const float area = TerrainCoordinator.area;
    private const int meshSize = TerrainCoordinator.meshSize;
    [SerializeField] private ComputeShader computeShader;
    private TerrainCoordinator coordinator;

    #region Configuration Region
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

        int findIntersectKernel = shader.FindKernel("FindIntersect");

        shader.SetFloat("_Area", area);
        shader.SetInt("_Size", meshSize);

        shader.SetInt("_Stride", TerrainController.VertexBufferStride);
        shader.SetInt("_PositionOffset", TerrainController.VertexPositionAttributeOffset);
        shader.SetInt("_BaseOffset", TerrainController.VertexBaseAttributeOffset);

        ComputeBuffer requestBuffer = new ComputeBuffer(1, sizeof(float) * 9 + sizeof(uint) * 5, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        shader.SetBuffer(findIntersectKernel, "_Request", requestBuffer);

        ComputeBuffer resultBuffer = new ComputeBuffer(1, sizeof(float) * 6 + sizeof(uint) * 1, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        shader.SetBuffer(findIntersectKernel, "_Result", resultBuffer);

        return new Instance
        {
            findIntersectKernel = findIntersectKernel,
            shader = shader,
            requestBuffer = requestBuffer,
            resultBuffer = resultBuffer
        };
    }
    #endregion

    #region Operation Region
    private List<(CPURequest, GPURequest)> requestQueue = new(bufferCount);

    private struct CPURequest
    {
        public uint id;
        public float minX;
        public float minZ;
        public float maxX;
        public float maxZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GPURequest
    {
        public Vector3 rayAnchor;
        public Vector3 rayOrigin;
        public Vector3 rayDirection;
        public uint rayUseBase;

        public uint gridStartX;
        public uint gridStartZ;
        public uint gridSpanX;
        public uint gridSpanZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Result
    {
        public Vector3 rayOrigin;
        public Vector3 rayHitPoint;
        public uint hit;
    }

    private static class IndexManager
    {
        private static uint lastIndex = 0;
        private static Stack<uint> freeIndices = new Stack<uint>(bufferCount);

        public static uint GetIndex()
        {
            if (freeIndices.TryPop(out uint existingValue))
            {
                return existingValue;
            }
            else
            {
                uint newValue = lastIndex;
                lastIndex++;
                return newValue;
            }
        }

        public static void ReturnIndex(uint recycle)
        {
            freeIndices.Push(recycle);
        }
    }

    private Stack<Instance> instances = new();

    private Dictionary<uint, Action<Vector3?>> callbacks = new(bufferCount);

    private Dictionary<uint, List<OngoingProcess>> processes = new(bufferCount);
    
    private struct OngoingProcess
    {
        public uint id;
        public Instance instance;
        public AsyncGPUReadbackRequest readback;
    }

    public void QueueIntersect(Vector3 position, Vector3 direction, float range, bool useBase, Action<Vector3?> callback)
    {
        Vector3 start = position;
        Vector3 end = position + direction * range;

        uint requestID = IndexManager.GetIndex();

        requestQueue.Add((
            new CPURequest
            {
                id = requestID,
                minX = Mathf.Min(start.x, end.x),
                minZ = Mathf.Min(start.z, end.z),
                maxX = Mathf.Max(start.x, end.x),
                maxZ = Mathf.Max(start.z, end.z)
            },
            new GPURequest
            {
                rayOrigin = position,
                rayDirection = direction,
                rayUseBase = useBase ? 1u : 0u
            }
        ));

        callbacks.Add(requestID, callback);

        enabled = true;
    }

    private void ExecuteIntersects()
    {
        while (requestQueue.Count > 0)
            ExecuteIntersect();
    }
    private void ExecuteIntersect()
    {
        (CPURequest cRequest, GPURequest gRequest) = requestQueue[0];
        requestQueue.RemoveAtSwapBack(0);

        int regionMinX = Mathf.FloorToInt(cRequest.minX / area);
        int regionMinZ = Mathf.FloorToInt(cRequest.minZ / area);
        int regionMaxX = Mathf.FloorToInt(cRequest.maxX / area);
        int regionMaxZ = Mathf.FloorToInt(cRequest.maxZ / area);

        List<OngoingProcess> process = new();
        processes[cRequest.id] = process;

        Vector3 globalRayOrigin = gRequest.rayOrigin;
        for (int regionX = regionMinX; regionX <= regionMaxX; regionX++)
            for (int regionZ = regionMinZ; regionZ <= regionMaxZ; regionZ++)
            {
                int scopedRegionX = regionX;
                int scopedRegionZ = regionZ;
                float anchorX = scopedRegionX * area;
                float anchorZ = scopedRegionZ * area;

                if (!coordinator.controllers.TryGetValue((scopedRegionX, scopedRegionZ), out TerrainController controller))
                    continue;

                gRequest.rayAnchor = new(anchorX, 0f, anchorZ);

                if (!instances.TryPop(out Instance instance))
                    instance = CreateInstance();

                int localMinX = Mathf.Max(0, Mathf.FloorToInt((cRequest.minX - anchorX) / area * meshSize));
                int localMinZ = Mathf.Max(0, Mathf.FloorToInt((cRequest.minZ - anchorZ) / area * meshSize));
                int localMaxX = Mathf.Min(meshSize, Mathf.CeilToInt((cRequest.maxX - anchorX) / area * meshSize));
                int localMaxZ = Mathf.Min(meshSize, Mathf.CeilToInt((cRequest.maxZ - anchorZ) / area * meshSize));

                int countX = localMaxX - localMinX + 1;
                int countZ = localMaxZ - localMinZ + 1;

                gRequest.gridStartX = (uint)localMinX;
                gRequest.gridStartZ = (uint)localMinZ;
                gRequest.gridSpanX = (uint)Mathf.CeilToInt((float)countX / 32);
                gRequest.gridSpanZ = (uint)Mathf.CeilToInt((float)countZ / 32);
                
                NativeArray<GPURequest> requestDestination = instance.requestBuffer.BeginWrite<GPURequest>(0, 1);
                requestDestination[0] = gRequest;
                instance.requestBuffer.EndWrite<GPURequest>(1);

                GraphicsBuffer mesh = controller.vertexBuffer;
                instance.shader.SetBuffer(instance.findIntersectKernel, "_Vertices", mesh);

                instance.shader.Dispatch(instance.findIntersectKernel, 1, 1, 1);

                process.Add(new() {
                    instance = instance,
                    readback = AsyncGPUReadback.Request(instance.resultBuffer,  1 * instance.resultBuffer.stride, 0)
                });
            }
    }

    List<uint> removeIDs = new();
    private void ConsumeIntersects()
    {
        removeIDs.Clear();

        foreach (var entry in processes)
        {
            bool allDone = true;
            foreach (var process in entry.Value)
                if (!process.readback.done)
                {
                    allDone = false;
                    break;
                }
            if (!allDone)
                continue;

            float minDistance = float.MaxValue;
            Vector3? globalHit = null;
            foreach (var process in entry.Value)
            {
                if (!process.readback.hasError)
                {
                    NativeArray<Result> results = process.readback.GetData<Result>();
                    Result result = results[0];

                    if (result.hit == 1)
                    {
                        float distance = Vector3.Distance(result.rayOrigin, result.rayHitPoint);
                        if (distance < minDistance)
                            globalHit = result.rayHitPoint;
                    }

                    results.Dispose();
                }

                instances.Push(process.instance);
            }
        
            if (callbacks.Remove(entry.Key, out Action<Vector3?> callback))
                callback(globalHit);

            IndexManager.ReturnIndex(entry.Key);

            removeIDs.Add(entry.Key);
        }

        foreach (var id in removeIDs)
            processes.Remove(id);
    }
    
    private void LateUpdate()
    {
        ConsumeIntersects();

        ExecuteIntersects();

        if (processes.Count <= 0)
            enabled = false;
    }
    #endregion

}