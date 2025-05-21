using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using System;
using Unity.Mathematics;

[RequireComponent(typeof(TerrainCoordinator))]
public class TerrainBatchIntersector : MonoBehaviour
{

    #region Configuration Region
    private const float area = TerrainCoordinator.area;
    private const int meshSize = TerrainCoordinator.meshSize;

    [SerializeField] private ComputeShader computeShader;
    private int setupRoundKernel;
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
        minTBufferPool.DestroyBuffers();
        resultBufferPool.DestroyBuffers();
    }

    private void SetupShader()
    {
        computeShader = Instantiate(computeShader);

        setupRoundKernel = computeShader.FindKernel("SetupRound");
        findIntersectKernel = computeShader.FindKernel("FindIntersect");

        computeShader.SetFloat("_Area", area);
        computeShader.SetInt("_Size", meshSize);

        computeShader.SetInt("_Stride", TerrainController.VertexBufferStride);
        computeShader.SetInt("_PositionOffset", TerrainController.VertexPositionAttributeOffset);
        computeShader.SetInt("_BaseOffset", TerrainController.VertexBaseAttributeOffset);

    }

    private BufferPool requestBufferPool = new((count) => new ComputeBuffer(count, Request.size, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates));
    private BufferPool minTBufferPool = new((count) => new ComputeBuffer(count, sizeof(uint), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates));
    private BufferPool resultBufferPool = new((count) => new ComputeBuffer(count, Result.size, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates));


    private class BufferPool
    {
        private Func<int, ComputeBuffer> constructor;
        public BufferPool(Func<int, ComputeBuffer> constructor)
        {
            this.constructor = constructor;
        }

        public List<ComputeBuffer> buffers = new();
        public Dictionary<int, Stack<ComputeBuffer>> unusedBuffers = new();

        public ComputeBuffer GetBuffer(int size)
        {
            int power = math.ceillog2(size);
            int actualSize = 1 << power;

            // Expand buffer table
            unusedBuffers.TryAdd(actualSize, new());
                    
            // Get buffer of good size
            if (!unusedBuffers[actualSize].TryPop(out ComputeBuffer buffer))
            {
                buffer = constructor(actualSize);
                buffers.Add(buffer);
            }

            return buffer;
        }

        public void ReturnBuffer(ComputeBuffer buffer)
        {
            // buffer.count size assumed power of 2
            unusedBuffers[buffer.count].Push(buffer);
        }

        public void DestroyBuffers()
        {
            foreach (var buffer in buffers)
                buffer.Dispose();
        }
    }
    #endregion

    #region Operation Region

    [StructLayout(LayoutKind.Sequential)]
    private struct Request
    {
        public Vector3 rayAnchor;
        public Vector3 rayOrigin;
        public Vector3 rayDirection;
        public uint rayUseBase;

        public const int size = sizeof(float) * 9 + sizeof(uint) * 1;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct Result
    {
        public Vector3 rayOrigin;
        public Vector3 rayHitPoint;
        public uint hit;

        public const int size = sizeof(float) * 6 + sizeof(uint) * 1;
    }

    public void ExecuteIntersect(Bounds bounds, int count, ComputeBuffer requestBuffer, ComputeBuffer minTBuffer, ComputeBuffer resultBuffer)
    {

        computeShader.SetInt("_Count", count);

        computeShader.SetBuffer(setupRoundKernel, "_MinT", minTBuffer);
        computeShader.SetBuffer(setupRoundKernel, "_Result", resultBuffer);
        computeShader.Dispatch(setupRoundKernel, Mathf.CeilToInt(count / 64f), 1, 1);

        computeShader.SetBuffer(findIntersectKernel, "_Request", requestBuffer);
        computeShader.SetBuffer(findIntersectKernel, "_MinT", minTBuffer);
        computeShader.SetBuffer(findIntersectKernel, "_Result", resultBuffer);

        int regionMinX = Mathf.FloorToInt(bounds.min.x / area);
        int regionMinZ = Mathf.FloorToInt(bounds.min.z / area);
        int regionMaxX = Mathf.FloorToInt(bounds.max.x / area);
        int regionMaxZ = Mathf.FloorToInt(bounds.max.z / area);

        for (int regionX = regionMinX; regionX <= regionMaxX; regionX++)
            for (int regionZ = regionMinZ; regionZ <= regionMaxZ; regionZ++)
            {
                if (!coordinator.controllers.TryGetValue((regionX, regionZ), out TerrainController controller))
                    continue;

                computeShader.SetVector("_Anchor", new(regionX * area, 0f, regionZ * area));

                GraphicsBuffer mesh = controller.vertexBuffer;
                computeShader.SetBuffer(findIntersectKernel, "_Vertices", mesh);

                computeShader.Dispatch(findIntersectKernel, count, 1, 1);
            }
    }
    #endregion

}