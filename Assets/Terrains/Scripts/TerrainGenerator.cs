using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;

public class TerrainGenerator : Object
{
    private const float area = TerrainCoordinator.area;
    private const int meshSize = TerrainCoordinator.meshSize;

    private static Mesh prototype = null;
    private static Bounds localBounds;
    private static GraphicsBuffer vertexReferenceBuffer = null;
    private static int vertexBufferStride;
    private static int vertexPositionAttributeOffset;
    private static int vertexNormalAttributeOffset;
    private static int vertexBaseAttributeOffset;
    private static int vertexModifyAttributeOffset;

    public static int GetVertexBufferStride()
    {
        if (prototype == null)
            CreateMesh();

        return vertexBufferStride;
    }

    public static int GetVertexPositionAttributeOffset()
    {
        if (prototype == null)
            CreateMesh();

        return vertexPositionAttributeOffset;
    }

    public static int GetVertexNormalAttributeOffset()
    {
        if (prototype == null)
            CreateMesh();

        return vertexNormalAttributeOffset;
    }

    public static int GetVertexBaseAttributeOffset()
    {
        if (prototype == null)
            CreateMesh();

        return vertexBaseAttributeOffset;
    }

    public static int GetVertexModifyAttributeOffset()
    {
        if (prototype == null)
            CreateMesh();

        return vertexModifyAttributeOffset;
    }

    public static Mesh GetMesh()
    {
        if (prototype == null)
            CreateMesh();

        return Instantiate(prototype);
    }

    public static GraphicsBuffer GetVertexReference()
    {
        if (prototype == null)
            CreateMesh();

        return vertexReferenceBuffer;
    }

    public static Bounds GetLocalBounds()
    {
        if (prototype == null)
            CreateMesh();

        return localBounds;
    }

    private static void CreateMesh()
    {
        NativeArray<float3> vertices = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
        NativeArray<float3> normals = new NativeArray<float3>((meshSize + 1) * (meshSize + 1), Allocator.Temp);
        for (int z = 0; z < meshSize + 1; z++)
            for (int x = 0; x < meshSize + 1; x++)
            {
                vertices[z * (meshSize + 1) + x] = new float3(x / (float)meshSize * area, 0, z / (float)meshSize * area);
                normals[z * (meshSize + 1) + x] = new float3(0f, 1f, 0f);
            }

        NativeArray<int> indices = new NativeArray<int>(6 * meshSize * meshSize, Allocator.Temp);
        for (int z = 0; z < meshSize; z++)
            for (int x = 0; x < meshSize; x++)
            {
                indices[(z * meshSize + x) * 6 + 0] = (z + 0) * (meshSize + 1) + (x + 0);
                indices[(z * meshSize + x) * 6 + 1] = (z + 1) * (meshSize + 1) + (x + 1);
                indices[(z * meshSize + x) * 6 + 2] = (z + 0) * (meshSize + 1) + (x + 1);
                indices[(z * meshSize + x) * 6 + 3] = (z + 1) * (meshSize + 1) + (x + 1);
                indices[(z * meshSize + x) * 6 + 4] = (z + 0) * (meshSize + 1) + (x + 0);
                indices[(z * meshSize + x) * 6 + 5] = (z + 1) * (meshSize + 1) + (x + 0);
            }


        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.SetVertexBufferParams(vertices.Length, new VertexAttributeDescriptor[] {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 1, 0), // base
            new VertexAttributeDescriptor(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 4, 0) // mask and modifiers
        });
        mesh.SetVertices(vertices);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        mesh.SetNormals(normals);
        mesh.bounds = new Bounds(new Vector3(0.5f * area, 0f, 0.5f * area), new Vector3((1f + 1f / meshSize) * area, 200f, (1f + 1f / meshSize) * area));

        vertices.Dispose();
        normals.Dispose();
        indices.Dispose();

        prototype = mesh;
        vertexReferenceBuffer = mesh.GetVertexBuffer(0);
        vertexBufferStride = mesh.GetVertexBufferStride(0);
        vertexPositionAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
        vertexNormalAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Normal);
        vertexBaseAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord6);
        vertexModifyAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord7);
        localBounds = mesh.bounds;
    }

}