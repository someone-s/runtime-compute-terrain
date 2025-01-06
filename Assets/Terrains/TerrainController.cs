using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

[RequireComponent(typeof(MeshFilter))]
public class TerrainController : MonoBehaviour
{
    private bool tick = true;
    private bool update = false;

    private static int meshSize = 250;
    [SerializeField] private ComputeShader computeShader;

    private static int bufferCount = 50;
    private ComputeBuffer operationBuffer;
    private int index = 0;
    private NativeArray<Operation> operationWriter;

    private ComputeBuffer intersectBuffer;
    private IntersectResult[] intersectOutput = new IntersectResult[1];

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
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("operations"), operationBuffer);

        computeShader.SetInt(Shader.PropertyToID("normalSection"), Mathf.CeilToInt(((float)(meshSize + 1)) / 32f));

        computeShader.SetInt(Shader.PropertyToID("intersectSection"), Mathf.CeilToInt(((float)meshSize) / 32f));
        intersectBuffer = new ComputeBuffer(1, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(findIntersectKernelIndex, Shader.PropertyToID("intersectResult"), intersectBuffer);
    }

    public void Modify(Vector2 normalPosition, float normalRadius, float operationParameter, OperationType operationType)
    {
        if (index == 0)
            operationWriter = operationBuffer.BeginWrite<Operation>(0, 50);

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

    public Vector3? GetIntersect(Vector3 origin, Vector3 direction) {
        computeShader.SetVector(Shader.PropertyToID("origin"), origin);
        computeShader.SetVector(Shader.PropertyToID("direction"), direction);
        computeShader.Dispatch(computeShader.FindKernel("FindIntersect"), 32, 32, 1);
        intersectBuffer.GetData(intersectOutput);
        return intersectOutput[0].hit == 1 ? intersectOutput[0].position : null;
    }

    private void LateUpdate()
    {
        if (tick)
        {
            if (index > 0)
            {
                operationBuffer.EndWrite<Operation>(index);
                computeShader.SetInt(Shader.PropertyToID("count"), index);
                index = 0;

                computeShader.Dispatch(computeShader.FindKernel("ModifyMesh"), 32, 32, 1);
                update = true;
            }
            tick = false;
        }
        else //tock
        {
            if (update) 
            {
                computeShader.Dispatch(computeShader.FindKernel("RecalculateNormal"), 32, 32, 1);
                update = false;
            }
            tick = true;
        }
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct IntersectResult
    {
        public Vector3 position { get; set; }
        public uint hit { get; set; }
    }

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
            for (int y = 0; y < meshSize + 1 + 2; y++)
                for (int x = 0; x < meshSize + 1 + 2; x++)
                {
                    vertices[y * (meshSize + 1 + 2) + x] = new float3((x - 1) / (float)meshSize, 0, (y - 1) / (float)meshSize);
                    normals[y * (meshSize + 1 + 2) + x] = new float3(0f, 1f, 0f);
                }
            
            NativeArray<int> indices = new NativeArray<int>(6 * meshSize * meshSize, Allocator.Temp);
            for (int y = 0; y < meshSize; y++)
                for (int x = 0; x < meshSize; x++)
                {
                    indices[(y * meshSize + x) * 6 + 0] = (y + 1 + 0) * (meshSize + 1 + 2) + (x + 1 + 0);
                    indices[(y * meshSize + x) * 6 + 1] = (y + 1 + 1) * (meshSize + 1 + 2) + (x + 1 + 1);
                    indices[(y * meshSize + x) * 6 + 2] = (y + 1 + 0) * (meshSize + 1 + 2) + (x + 1 + 1);
                    indices[(y * meshSize + x) * 6 + 3] = (y + 1 + 1) * (meshSize + 1 + 2) + (x + 1 + 1);
                    indices[(y * meshSize + x) * 6 + 4] = (y + 1 + 0) * (meshSize + 1 + 2) + (x + 1 + 0);
                    indices[(y * meshSize + x) * 6 + 5] = (y + 1 + 1) * (meshSize + 1 + 2) + (x + 1 + 0);
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
