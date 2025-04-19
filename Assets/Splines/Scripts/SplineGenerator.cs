using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;

public class SplineGenerator : Object
{
    public struct Entry
    {
        public Mesh prototype;
        public GraphicsBuffer reference;
        public int vertexBufferStride;
        public int vertexPositionAttributeOffset;
        public int vertexNormalAttributeOffset;
        public int vertexUVAttributeOffset;
    }

    private static Dictionary<SplineProfile, Entry> entries = new();

    public static int GetVertexBufferStride(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return entry.vertexBufferStride;
    }

    public static int GetVertexPositionAttributeOffset(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return entry.vertexPositionAttributeOffset;
    }

    public static int GetVertexNormalAttributeOffset(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return entry.vertexNormalAttributeOffset;
    }

    public static int GetVertexUVAttributeOffset(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return entry.vertexUVAttributeOffset;
    }

    public static Mesh GetMesh(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return Instantiate(entry.prototype);

    }

    public static GraphicsBuffer GetReference(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return entry.reference;
    }

    private static Entry CreateMesh(SplineProfile profile)
    {
        Mesh mesh = new Mesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource;
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

        if (profile.continous != null)
        {
            int maxPointCount = profile.maxPointCount;
            Vector3[] sourceVertices = profile.mesh.vertices;
            Vector3[] sourceNormals = profile.mesh.normals;
            Vector2[] sourceUVs = profile.mesh.uv;

            int[] sourceIndices = profile.continous.Value.mapping;
            int sourceCount = sourceIndices.Length;
            NativeArray<float3> vertices = new NativeArray<float3>(sourceCount * maxPointCount, Allocator.Temp);
            NativeArray<float3> normals  = new NativeArray<float3>(sourceCount * maxPointCount, Allocator.Temp);
            NativeArray<float2> uvs      = new NativeArray<float2>(sourceCount * maxPointCount, Allocator.Temp);
            for (int s = 0; s < maxPointCount; s++)
                for (int i = 0; i < sourceCount; i++)
                {
                    int baseIndex = sourceIndices[i];
                    vertices[s * sourceCount + i] = sourceVertices[baseIndex];
                    normals [s * sourceCount + i] = sourceNormals [baseIndex];
                    uvs     [s * sourceCount + i] = sourceUVs     [baseIndex];
                }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);

            vertices.Dispose();
            normals.Dispose();
            uvs.Dispose();
        
            NativeArray<int> indices = new NativeArray<int>(6 * (sourceCount - 1) * (maxPointCount - 1), Allocator.Temp);
            for (int s = 0; s < maxPointCount - 1; s++)
                for (int p = 0; p < sourceCount - 1; p++)
                {
                    indices[(s * (sourceCount - 1) + p) * 6 + 0] = (s + 0) * sourceCount + (p + 0);
                    indices[(s * (sourceCount - 1) + p) * 6 + 1] = (s + 0) * sourceCount + (p + 1);
                    indices[(s * (sourceCount - 1) + p) * 6 + 2] = (s + 1) * sourceCount + (p + 1);
                    indices[(s * (sourceCount - 1) + p) * 6 + 3] = (s + 1) * sourceCount + (p + 1);
                    indices[(s * (sourceCount - 1) + p) * 6 + 4] = (s + 1) * sourceCount + (p + 0);
                    indices[(s * (sourceCount - 1) + p) * 6 + 5] = (s + 0) * sourceCount + (p + 0);
                }

            mesh.SetIndices(indices, MeshTopology.Triangles, 0);

            indices.Dispose();
        }
        else
        {
            int maxPointCount = profile.maxPointCount;
            Vector3[] sourceVertices = profile.mesh.vertices;
            Vector3[] sourceNormals = profile.mesh.normals;
            Vector2[] sourceUVs = profile.mesh.uv;

            int[] sourceIndices = profile.continous.Value.mapping;
            int sourceCount = sourceIndices.Length;
            NativeArray<float3> vertices = new NativeArray<float3>(sourceCount * maxPointCount, Allocator.Temp);
            NativeArray<float3> normals  = new NativeArray<float3>(sourceCount * maxPointCount, Allocator.Temp);
            NativeArray<float2> uvs      = new NativeArray<float2>(sourceCount * maxPointCount, Allocator.Temp);
            for (int s = 0; s < maxPointCount; s++)
                for (int i = 0; i < sourceCount; i++)
                {
                    int baseIndex = sourceIndices[i];
                    vertices[s * sourceCount + i] = sourceVertices[baseIndex];
                    normals [s * sourceCount + i] = sourceNormals [baseIndex];
                    uvs     [s * sourceCount + i] = sourceUVs     [baseIndex];
                }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);

            vertices.Dispose();
            normals.Dispose();
            uvs.Dispose();
        
            NativeArray<int> indices = new NativeArray<int>(6 * (sourceCount - 1) * (maxPointCount - 1), Allocator.Temp);
            for (int s = 0; s < maxPointCount - 1; s++)
                for (int p = 0; p < sourceCount - 1; p++)
                {
                    indices[(s * (sourceCount - 1) + p) * 6 + 0] = (s + 0) * sourceCount + (p + 0);
                    indices[(s * (sourceCount - 1) + p) * 6 + 1] = (s + 0) * sourceCount + (p + 1);
                    indices[(s * (sourceCount - 1) + p) * 6 + 2] = (s + 1) * sourceCount + (p + 1);
                    indices[(s * (sourceCount - 1) + p) * 6 + 3] = (s + 1) * sourceCount + (p + 1);
                    indices[(s * (sourceCount - 1) + p) * 6 + 4] = (s + 1) * sourceCount + (p + 0);
                    indices[(s * (sourceCount - 1) + p) * 6 + 5] = (s + 0) * sourceCount + (p + 0);
                }

            mesh.SetIndices(indices, MeshTopology.Triangles, 0);

            indices.Dispose();
        }

        Entry entry = new Entry() {
            prototype                       = mesh,
            reference                       = mesh.GetVertexBuffer(0), 
            vertexBufferStride              = mesh.GetVertexBufferStride(0), 
            vertexPositionAttributeOffset   = mesh.GetVertexAttributeOffset(VertexAttribute.Position), 
            vertexNormalAttributeOffset     = mesh.GetVertexAttributeOffset(VertexAttribute.Normal),
            vertexUVAttributeOffset         = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0)
        };

        entries.Add(profile, entry);

        return entry;
        }

}