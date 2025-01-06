using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using UnityEngine.Events;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainController : MonoBehaviour
{
    // General state
    private static int meshSize = 250;
    [SerializeField] private ComputeShader computeShader;


    private void Start()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = MeshGenerator.GetMesh();
        filter.sharedMesh = mesh;


        computeShader = Instantiate(computeShader);

        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");
        int recalculateNormalKernelIndex = computeShader.FindKernel("RecalculateNormal");
        int findIntersectKernelIndex = computeShader.FindKernel("FindIntersect");

        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("vertices"), mesh.GetVertexBuffer(0));
        computeShader.SetBuffer(recalculateNormalKernelIndex, Shader.PropertyToID("vertices"), mesh.GetVertexBuffer(0));
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("vertices"), mesh.GetVertexBuffer(0));
        computeShader.SetInt(Shader.PropertyToID("stride"), mesh.GetVertexBufferStride(0));
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), mesh.GetVertexAttributeOffset(VertexAttribute.Position));
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), mesh.GetVertexAttributeOffset(VertexAttribute.Normal));

        computeShader.SetInt(Shader.PropertyToID("size"), meshSize);
        computeShader.SetInt(Shader.PropertyToID("meshSection"), Mathf.CeilToInt(((float)(meshSize + 1 + 2)) / 32f));
        operationBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 4 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        operationWriter = new NativeArray<Operation>(bufferCount, Allocator.Persistent);
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("operations"), operationBuffer);

        computeShader.SetInt(Shader.PropertyToID("normalSection"), Mathf.CeilToInt(((float)(meshSize + 1)) / 32f));

        computeShader.SetInt(Shader.PropertyToID("intersectSection"), Mathf.CeilToInt(((float)meshSize) / 32f));
        intersectBuffer = new ComputeBuffer(1, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("intersectResult"), intersectBuffer);
    }

    private void OnDestroy() {
        operationBuffer.Dispose();
        operationWriter.Dispose();
        intersectBuffer.Dispose();
    }

#region Visual Section
    private MeshRenderer visualRenderer;

    public void SetVisible(bool visualState)
    {
        if (visualRenderer == null)
            visualRenderer = GetComponent<MeshRenderer>();
        visualRenderer.enabled = visualState;
    }
#endregion

#region Modify Section
    private static int bufferCount = 50;
    private ComputeBuffer operationBuffer;
    private int index = 0;
    private NativeArray<Operation> operationWriter;

    public enum OperationType
    {
        Add = 1,
        Subtract = 2,
        Level = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Operation
    {
        public Vector2 position { get; set; } // niormalized
        public float radius { get; set; } // normalized
        public float parameter { get; set; } // arbitary space
        public uint type { get; set; }
    }

    public void QueueModify(Vector2 normalPosition, float normalRadius, float operationParameter, OperationType operationType)
    {
        enabled = true;

        if (index >= bufferCount)
        {
            Debug.LogWarning($"Operation ignored, exceeded max count: {bufferCount}");
            return;
        }

        operationWriter[index] = new Operation
        {
            position = normalPosition,
            radius = normalRadius,
            parameter = operationParameter,
            type = (uint)operationType
        };
        index++;
    }

    private void ExecuteModify()
    {
        NativeArray<Operation> operationDestination = operationBuffer.BeginWrite<Operation>(0, index);

        NativeSlice<Operation> destinationRange = new NativeSlice<Operation>(operationDestination, 0, index);
        NativeSlice<Operation> writerRange = new NativeSlice<Operation>(operationWriter, 0, index);
        destinationRange.CopyFrom(writerRange);

        operationBuffer.EndWrite<Operation>(index);

        computeShader.SetInt(Shader.PropertyToID("count"), index);
        index = 0;

        computeShader.Dispatch(computeShader.FindKernel("ModifyMesh"), 32, 32, 1);
    }

    private void ExecuteNormal()
    {
        computeShader.Dispatch(computeShader.FindKernel("RecalculateNormal"), 32, 32, 1);
    }
#endregion

#region Intersect Section
    private Vector3 rayOrigin, rayDirection;
    private int x, z;
    private UnityAction<Vector3> intersectCallback;
    private ComputeBuffer intersectBuffer;

    [StructLayout(LayoutKind.Sequential)]
    private struct IntersectResult
    {
        public Vector3 position { get; set; }
        public uint hit { get; set; }
    }

    public void QueueIntersect(Vector3 origin, Vector3 direction)
    {
        enabled = true;

        rayOrigin = origin;
        rayDirection = direction;
        needFindIntersect = true;
    }

    private void ExecuteIntersect()
    {
        computeShader.SetVector(Shader.PropertyToID("origin"), rayOrigin);
        computeShader.SetVector(Shader.PropertyToID("direction"), rayDirection);
        computeShader.Dispatch(computeShader.FindKernel("FindIntersect"), 32, 32, 1);
        AsyncGPUReadback.Request(intersectBuffer, (request) =>
        {
            NativeArray<IntersectResult> result = request.GetData<IntersectResult>();
            if (result[0].hit == 1)
            {
                Vector3 globalPosition = result[0].position;
                globalPosition.x += x;
                globalPosition.z += z;
                intersectCallback.Invoke(globalPosition);
            }
            result.Dispose();
        });
    }

    public void SetCallbackIndex(int normalX, int normalZ, UnityAction<Vector3> callback)
    {
        x = normalX;
        z = normalZ;
        intersectCallback = callback;
    }

#endregion

#region Work Section
    private bool needRecalculateNormal = false;
    private bool needFindIntersect = false;

    private static int synchronizedMode = 0;
    private static int updateInstance = 0;
    private static void UpdateMode() {
        if (updateInstance != Time.frameCount) {
            synchronizedMode += 1;
            synchronizedMode %= 2;
            updateInstance = Time.frameCount;
        }
    }

    private void LateUpdate()
    {
        switch (synchronizedMode)
        {
            case 0:
                if (index > 0)
                {
                    ExecuteModify();
                    needRecalculateNormal = true;
                }
                break;
            case 1:
                if (needRecalculateNormal)
                {
                    ExecuteNormal();
                    needRecalculateNormal = false;
                }
                break;
        }

        UpdateMode();


        if (needFindIntersect)
        {
            ExecuteIntersect();
            needFindIntersect = false;
        }

        if (!(index > 0 || needRecalculateNormal || needFindIntersect))
            enabled = false;
    }
#endregion
    
    private static class MeshGenerator
    {
        private static Mesh prototype = null;

        public static Mesh GetMesh()
        {
            if (prototype == null)
            {
                prototype = CreateMesh();
            }

            return Instantiate(prototype);
        }

        private static Mesh CreateMesh()
        {
            NativeArray<float3> vertices = new NativeArray<float3>((meshSize + 1 + 2) * (meshSize + 1 + 2), Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>((meshSize + 1 + 2) * (meshSize + 1 + 2), Allocator.Temp);
            for (int z = 0; z < meshSize + 1 + 2; z++)
                for (int x = 0; x < meshSize + 1 + 2; x++)
                {
                    vertices[z * (meshSize + 1 + 2) + x] = new float3((x - 1) / (float)meshSize, 0, (z - 1) / (float)meshSize);
                    normals[z * (meshSize + 1 + 2) + x] = new float3(0f, 1f, 0f);
                }

            NativeArray<int> indices = new NativeArray<int>(6 * meshSize * meshSize, Allocator.Temp);
            for (int z = 0; z < meshSize; z++)
                for (int x = 0; x < meshSize; x++)
                {
                    indices[(z * meshSize + x) * 6 + 0] = (z + 1 + 0) * (meshSize + 1 + 2) + (x + 1 + 0);
                    indices[(z * meshSize + x) * 6 + 1] = (z + 1 + 1) * (meshSize + 1 + 2) + (x + 1 + 1);
                    indices[(z * meshSize + x) * 6 + 2] = (z + 1 + 0) * (meshSize + 1 + 2) + (x + 1 + 1);
                    indices[(z * meshSize + x) * 6 + 3] = (z + 1 + 1) * (meshSize + 1 + 2) + (x + 1 + 1);
                    indices[(z * meshSize + x) * 6 + 4] = (z + 1 + 0) * (meshSize + 1 + 2) + (x + 1 + 0);
                    indices[(z * meshSize + x) * 6 + 5] = (z + 1 + 1) * (meshSize + 1 + 2) + (x + 1 + 0);
                }


            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetNormals(normals);
            mesh.bounds = new Bounds(new Vector3(0.5f, 0f, 0.5f), new Vector3(1f + 1f / meshSize, 200f, 1f + 1f / meshSize));

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            return mesh;
        }

    }
}
