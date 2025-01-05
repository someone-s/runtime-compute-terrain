using UnityEngine;
using UnityEngine.PlayerLoop;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;

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


    private void Start()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        Mesh mesh = MeshGenerator.GetMesh();
        filter.sharedMesh = mesh;

        computeShader = Instantiate(computeShader);


        int modifyMeshKernelIndex = computeShader.FindKernel("ModifyMesh");
        int recalculateNormalKernelIndex = computeShader.FindKernel("RecalculateNormal");

        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("vertices"), mesh.GetVertexBuffer(0));
        computeShader.SetBuffer(recalculateNormalKernelIndex, Shader.PropertyToID("vertices"), mesh.GetVertexBuffer(0));
        computeShader.SetInt(Shader.PropertyToID("stride"), mesh.GetVertexBufferStride(0));
        computeShader.SetInt(Shader.PropertyToID("positionOffset"), mesh.GetVertexAttributeOffset(VertexAttribute.Position));
        computeShader.SetInt(Shader.PropertyToID("normalOffset"), mesh.GetVertexAttributeOffset(VertexAttribute.Normal));

        computeShader.SetInt(Shader.PropertyToID("size"), meshSize);
        computeShader.SetInt(Shader.PropertyToID("section"), Mathf.CeilToInt(((float)meshSize) / 32f));

        operationBuffer = new ComputeBuffer(bufferCount, sizeof(float) * 4 + sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        computeShader.SetBuffer(modifyMeshKernelIndex, Shader.PropertyToID("operations"), operationBuffer);
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
            NativeArray<float3> vertices = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
            NativeArray<float3> normals = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
            for (int y = 0; y <= meshSize; y++)
                for (int x = 0; x <= meshSize; x++)
                {
                    vertices[y * (meshSize + 1) + x] = new float3(((float)x) / meshSize, 0, ((float)y) / meshSize);
                    normals[y * (meshSize + 1) + x] = new float3(0f, 1f, 0f);
                }

            NativeArray<int> indices = new NativeArray<int>(6 * meshSize * meshSize, Allocator.Temp);
            for (int y = 0; y < meshSize; y++)
                for (int x = 0; x < meshSize; x++)
                {
                    indices[(y * meshSize + x) * 6 + 0] = (y + 0) * (meshSize + 1) + (x + 0);
                    indices[(y * meshSize + x) * 6 + 1] = (y + 1) * (meshSize + 1) + (x + 1);
                    indices[(y * meshSize + x) * 6 + 2] = (y + 0) * (meshSize + 1) + (x + 1);
                    indices[(y * meshSize + x) * 6 + 3] = (y + 1) * (meshSize + 1) + (x + 1);
                    indices[(y * meshSize + x) * 6 + 4] = (y + 0) * (meshSize + 1) + (x + 0);
                    indices[(y * meshSize + x) * 6 + 5] = (y + 1) * (meshSize + 1) + (x + 0);
                }

            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.SetNormals(normals);
            mesh.bounds = new Bounds(new Vector3(0.5f, 0f, 0.5f), new Vector3(1f, 200f, 1f));

            vertices.Dispose();
            normals.Dispose();
            indices.Dispose();

            return mesh;
        }

    }
}
