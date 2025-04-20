using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Linq;
using System;

public class SplineGenerator : UnityEngine.Object
{
    public struct Entry
    {
        public Mesh prototype;
        public GraphicsBuffer reference;
        public int vertexBufferStride;
        public int vertexPositionAttributeOffset;
        public int vertexNormalAttributeOffset;
        public int vertexUVAttributeOffset;
        public SubMeshRange[] subMeshRanges;
        public float uvStretch;
        public Func<float, float> GetStartOffset;
        public Func<int, int> GetActualPointCount;
    }

    public struct SubMeshRange
    {
        public int vertexStart;
        public int vertexCount;
        public int indexStart;
        public int indexCount;
    }

    private static Dictionary<SplineProfile, Entry> entries = new();

    public static Entry GetEntry(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return entry;
    }
    public static (Mesh mesh, Entry entry) GetMesh(SplineProfile profile)
    {
        if (!entries.TryGetValue(profile, out var entry))
            entry = CreateMesh(profile);

        return (Instantiate(entry.prototype), entry);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;
    }

    private static Entry CreateMesh(SplineProfile profile)
    {
        Mesh mesh = new Mesh();
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

        VertexAttributeDescriptor[] layout = new[] {
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
        };

        MeshUpdateFlags updateFlags =
            MeshUpdateFlags.DontValidateIndices |    // dont check against CPU index buffer
            MeshUpdateFlags.DontResetBoneBounds |    // dont reset skinned mesh bones bound
            MeshUpdateFlags.DontNotifyMeshUsers |    // dont notify on possible mesh bound change 
            MeshUpdateFlags.DontRecalculateBounds;   // dont recalculate bounds

        int maxPointCount = profile.maxPointCount;
        Vector3[] sourceVertices = profile.mesh.vertices;
        Vector3[] sourceNormals = profile.mesh.normals;
        Vector2[] sourceUVs = profile.mesh.uv;

        SubMeshRange[] subMeshRanges;

        if (profile.continous != null)
        {
            int[] sourceIndices = profile.continous.Value.mapping;
            int sourceCount = sourceIndices.Length;
            int destCount = sourceCount * maxPointCount;
            mesh.SetVertexBufferParams(destCount, layout);

            NativeArray<Vertex> vertices = new NativeArray<Vertex>(destCount, Allocator.Temp);
            for (int s = 0; s < maxPointCount; s++)
                for (int i = 0; i < sourceCount; i++)
                {
                    int baseIndex = sourceIndices[i];
                    vertices[s * sourceCount + i] = new Vertex
                    {
                        position = sourceVertices[baseIndex],
                        normal = sourceNormals[baseIndex],
                        uv = sourceUVs[baseIndex]
                    };
                }
            mesh.SetVertexBufferData(vertices, 0, 0, destCount, flags: updateFlags);
            vertices.Dispose();

            int indexCount = 6 * (sourceCount - 1) * (maxPointCount - 1);
            mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

            NativeArray<int> indices = new NativeArray<int>(indexCount, Allocator.Temp);
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
            mesh.SetIndexBufferData(indices, 0, 0, indexCount, flags: updateFlags);
            indices.Dispose();

            subMeshRanges = new SubMeshRange[] { new SubMeshRange() {
                vertexStart = 0,
                vertexCount = sourceCount,
                indexStart = 0,
                indexCount = 6 * (sourceCount - 1) } };
        }
        else
        {
            int totalVertexCount = profile.mesh.vertexCount;
            IEnumerable<SubMeshDescriptor> subMeshes = Enumerable.Range(0, profile.mesh.subMeshCount).Select(i => profile.mesh.GetSubMesh(i));

            subMeshRanges = new SubMeshRange[profile.mesh.subMeshCount]; // extra entry at the end not used

            {
                int destVertexCount = totalVertexCount * maxPointCount;
                mesh.SetVertexBufferParams(destVertexCount, layout);

                NativeArray<Vertex> vertices = new NativeArray<Vertex>(destVertexCount, Allocator.Temp);
                int s = 0;
                int subMeshVertexStart = 0;
                foreach (var subMesh in subMeshes)
                {
                    for (int p = 0; p < maxPointCount; p++)
                        for (int v = 0; v < subMesh.vertexCount; v++)
                            vertices[subMeshVertexStart + p * subMesh.vertexCount + v] = new Vertex
                            {
                                position = sourceVertices[subMesh.baseVertex + v],
                                normal = sourceNormals[subMesh.baseVertex + v],
                                uv = sourceUVs[subMesh.baseVertex + v]
                            };

                    subMeshRanges[s] = new SubMeshRange() { 
                        vertexStart = subMeshVertexStart, 
                        vertexCount = subMesh.vertexCount };
                    subMeshVertexStart += subMesh.vertexCount * maxPointCount;
                    s++;
                }
                mesh.SetVertexBufferData(vertices, 0, 0, destVertexCount, flags: updateFlags);
                vertices.Dispose();
            }

            {
                int sourceIndexCount = subMeshes.Sum(subMesh => subMesh.indexCount);
                int destIndexCount = sourceIndexCount * maxPointCount;
                mesh.SetIndexBufferParams(destIndexCount, IndexFormat.UInt32);

                NativeArray<int> indices = new NativeArray<int>(destIndexCount, Allocator.Temp);
                int s = 0;
                int subMeshIndexStart = 0;
                foreach (var subMesh in subMeshes)
                {
                    int[] sourceIndices = profile.mesh.GetIndices(s, applyBaseVertex: false);
                    int subMeshVertexStart = subMeshRanges[s].vertexStart;
                    for (int p = 0; p < maxPointCount; p++)
                        for (int i = 0; i < subMesh.indexCount; i++)
                            indices[subMeshIndexStart + p * subMesh.indexCount + i] = subMeshVertexStart + p * subMesh.vertexCount + sourceIndices[i];

                    subMeshRanges[s].indexStart = subMeshIndexStart;
                    subMeshRanges[s].indexCount = subMesh.indexCount;
                    subMeshIndexStart += subMesh.indexCount * maxPointCount;
                    s++;
                }
                mesh.SetIndexBufferData(indices, 0, 0, destIndexCount, flags: updateFlags);
                indices.Dispose();
            }
        }

        mesh.subMeshCount = subMeshRanges.Length;

        Entry entry = new Entry()
        {
            prototype = mesh,
            reference = mesh.GetVertexBuffer(0),
            vertexBufferStride = mesh.GetVertexBufferStride(0),
            vertexPositionAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position),
            vertexNormalAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Normal),
            vertexUVAttributeOffset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0),
            subMeshRanges = subMeshRanges,
            uvStretch = profile.continous != null ? profile.continous.Value.stretch : 0f,
            GetStartOffset = profile.continous != null ? (_) => 0f : (originalSpacing) => originalSpacing * 0.5f,
            GetActualPointCount = profile.continous != null ? (segmentCount) => segmentCount + 1 : (segmentCount) => segmentCount 
        };

        entries.Add(profile, entry);

        return entry;
    }

}