using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using System;
using System.Linq;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainSingleIntersector : MonoBehaviour
{

    #region Configuration Region
    private const float area = TerrainCoordinator.area;
    private const int meshSize = TerrainCoordinator.meshSize;

    [SerializeField] private ComputeShader computeShader;
    private int findIntersectKernel;
    private TerrainCoordinator coordinator;
    private void Start()
    {
        coordinator = GetComponent<TerrainCoordinator>();

        SetupShader();
    }

    private void OnDestroy()
    {
        requestBufferPool.DestroyBuffers();
        resultBufferPool.DestroyBuffers();
    }

    private void SetupShader()
    {
        computeShader = Instantiate(computeShader);

        findIntersectKernel = computeShader.FindKernel("FindIntersect");

        computeShader.SetFloat("_Area", area);
        computeShader.SetInt("_Size", meshSize);

        computeShader.SetInt("_Stride", TerrainController.VertexBufferStride);
        computeShader.SetInt("_PositionOffset", TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt("_BaseOffset", TerrainController.VertexBaseAttributeOffset);

    }

    private BufferPool requestBufferPool = new(() => new ComputeBuffer(1, sizeof(float) * 9 + sizeof(uint) * 5, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates));
    private BufferPool resultBufferPool = new(() => new ComputeBuffer(1, sizeof(float) * 6 + sizeof(uint) * 1, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates));


    private class BufferPool
    {
        private Func<ComputeBuffer> constructor;
        public BufferPool(Func<ComputeBuffer> constructor)
        {
            this.constructor = constructor;
        }

        public List<ComputeBuffer> buffers = new();
        public Stack<ComputeBuffer> unusedBuffers = new();

        public ComputeBuffer GetBuffer()
        {
            if (!unusedBuffers.TryPop(out ComputeBuffer buffer))
            {
                buffer = constructor();
                buffers.Add(buffer);
            }

            return buffer;
        }

        public void ReturnBuffer(ComputeBuffer buffer)
        {
            unusedBuffers.Push(buffer);
        }

        public void DestroyBuffers()
        {
            foreach (var buffer in buffers)
                buffer.Dispose();
        }
    }
    #endregion

    #region Request Region
    private List<(CPURequest, GPURequest)> requestQueue = new();

    private struct CPURequest
    {
        public float minX;
        public float minZ;
        public float maxX;
        public float maxZ;

        public Action<Vector3?> callback;
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


    public void QueueIntersect(Vector3 position, Vector3 direction, float range, bool useBase, Action<Vector3?> callback)
    {
        Vector3 start = position;
        Vector3 end = position + direction * range;

        requestQueue.Add((
            new CPURequest
            {
                minX = Mathf.Min(start.x, end.x),
                minZ = Mathf.Min(start.z, end.z),
                maxX = Mathf.Max(start.x, end.x),
                maxZ = Mathf.Max(start.z, end.z),
                callback = callback
            },
            new GPURequest
            {
                rayOrigin = position,
                rayDirection = direction,
                rayUseBase = useBase ? 1u : 0u
            }
        ));

        enabled = true;
    }
    #endregion
    
    #region Result Region

    [StructLayout(LayoutKind.Sequential)]
    private struct Result
    {
        public Vector3 rayOrigin;
        public Vector3 rayHitPoint;
        public uint hit;
    }

    private class ResultGroup
    {
        private static List<ResultGroup> ongoingReadbackGroups = new();
        private static Stack<ResultGroup> unusedReadbackGroups = new();
        public static bool AnyOngoing => ongoingReadbackGroups.Count > 0;
        public static ResultGroup GetReadbackGroup(Action<Vector3?> callback)
        {
            if (unusedReadbackGroups.TryPop(out ResultGroup readbackGroup))
                readbackGroup.readbacks.Clear();
            else
                readbackGroup = new();

            ongoingReadbackGroups.Add(readbackGroup);

            readbackGroup.callback = callback;

            return readbackGroup;
        }

        public struct ReadbackEntry
        {
            public ComputeBuffer requestBuffer;
            public ComputeBuffer resultBuffer;
            public AsyncGPUReadbackRequest readbackRequest;
        }
        private List<ReadbackEntry> readbacks = new();
        public void AddEntry(ComputeBuffer requestBuffer, ComputeBuffer resultBuffer, AsyncGPUReadbackRequest readbackRequest)
        {
            readbacks.Add(new ReadbackEntry()
            {
                requestBuffer = requestBuffer,
                resultBuffer = resultBuffer,
                readbackRequest = readbackRequest
            });
        }
        
        private Action<Vector3?> callback;

        public static void EvaluateGroups(Action<ComputeBuffer> returnRequestBuffer, Action<ComputeBuffer> returnResultBuffer)
        {
            for (int i = 0; i < ongoingReadbackGroups.Count; i++)
            {
                ResultGroup readbackGroup = ongoingReadbackGroups[i];

                if (!readbackGroup.readbacks.All(entry => entry.readbackRequest.done))
                    continue;

                float minDistance = float.MaxValue;
                Vector3? globalHit = null;
                foreach (ReadbackEntry entry in readbackGroup.readbacks)
                {
                    if (!entry.readbackRequest.hasError)
                    {
                        NativeArray<Result> resultArray = entry.readbackRequest.GetData<Result>();
                        Result result = resultArray[0];
                        resultArray.Dispose();

                        if (result.hit == 1)
                        {
                            float distance = Vector3.Distance(result.rayOrigin, result.rayHitPoint);
                            if (distance < minDistance)
                                globalHit = result.rayHitPoint;
                        }
                    }

                    returnRequestBuffer(entry.requestBuffer);
                    returnResultBuffer(entry.resultBuffer);
                   
                }

                readbackGroup.callback?.Invoke(globalHit);

                ongoingReadbackGroups.RemoveAtSwapBack(i);
                i--; // make sure next check is same index
                unusedReadbackGroups.Push(readbackGroup);
            }

        }
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

        ResultGroup readbackGroup = ResultGroup.GetReadbackGroup(cRequest.callback);

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

                ComputeBuffer requestBuffer = requestBufferPool.GetBuffer();
                NativeArray<GPURequest> requestDestination = requestBuffer.BeginWrite<GPURequest>(0, 1);
                requestDestination[0] = gRequest;
                requestBuffer.EndWrite<GPURequest>(1);
                computeShader.SetBuffer(findIntersectKernel, "_Request", requestBuffer);

                ComputeBuffer resultBuffer = resultBufferPool.GetBuffer();
                computeShader.SetBuffer(findIntersectKernel, "_Result", resultBuffer);

                GraphicsBuffer mesh = controller.vertexBuffer;
                computeShader.SetBuffer(findIntersectKernel, "_Vertices", mesh);

                computeShader.Dispatch(findIntersectKernel, 1, 1, 1);

                readbackGroup.AddEntry(requestBuffer, resultBuffer, AsyncGPUReadback.Request(resultBuffer));
            }
    }

    private void LateUpdate()
    {
        ResultGroup.EvaluateGroups(requestBufferPool.ReturnBuffer, resultBufferPool.ReturnBuffer);

        ExecuteIntersects();

        if (!ResultGroup.AnyOngoing)
            enabled = false;
    }
    #endregion

}